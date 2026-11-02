using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// MOD-03 → Sales: the order's tax through the Settings line tax port <see cref="ISettingsLineTaxResolverV1"/> (#866, #873).
/// <list type="bullet">
/// <item><b>Header (SAT-D140/D147/D158):</b> the buyer tax situation is chosen per order from the Settings buyer situation list in
/// force on the order's business date; a new order starts "Normal" (SAT-D140's implementation detail). An exemption code is chosen
/// only for a "KDV %0 with an exemption code" situation and only from the exemption list; a buyer document only for a situation
/// that requires one. A missing document or code never blocks the order — the invoice waits (SAT-D147, fail-closed).</item>
/// <item><b>Lines (SAT-D73…D76, D82, D93, D133, D139):</b> every line's tax is the port's answer for the product's item type and the
/// order's buyer situation, kept as the line's snapshot (SAT-D82: a later Settings change never rewrites it) and re-resolved only
/// at the line's create, a commercial revision or a buyer situation change. Empty Settings values (SAT-D148) stay "kaynak yok"
/// with the port's reason — never a guessed rate, code or threshold.</item>
/// <item><b>Line change (SAT-D93/D97):</b> the salesperson changes a line's basis with a reason (audit), ONLY to Settings values: an
/// item type default, a buyer situation of the list and an exemption code of the list. It is approved with the order (SAT-D97);
/// on an approved order it is a commercial revision and re-enters approval (SAT-D105).</item>
/// <item><b>Port faults:</b> denied → 403; not composed or unreadable → 503 <c>sales_tax_provider_unavailable</c> when the command
/// needs it. Without a composed port a create stores no snapshot (the totals say <c>tax_snapshot_missing</c>).</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService : ISalesDraftTaxService
{
    /// <summary>SAT-D140 (implementation detail, reversible): the situation a new order starts in when none is chosen.</summary>
    private const string StartBuyerSituation = TaxBuyerSituationsV1.Normal;

    // ---- reads --------------------------------------------------------------------------------------------------------------

    /// <summary>The order's buyer tax and every line's tax snapshot, under the draft read AND the price read (else 403).</summary>
    public async Task<SalesOrderDraftTaxResponse> GetDraftTaxAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // SAT-D150: the tax is commercial content — no draft is touched for a caller who may not see prices, and it is asked again
        // as the final await.
        await RequirePriceDisclosureAsync(scope, cancellationToken);
        var draft = await LoadDraftAsync(scope, bindings.ReadPermission, draftId, cancellationToken);
        await RequirePriceDisclosureAsync(scope, cancellationToken);
        var buyer = draft.Header.BuyerTax;
        return new SalesOrderDraftTaxResponse(draft.Version.ToString(CultureInfo.InvariantCulture),
            SalesDraftApiV1.RevisionText(draft.Revision), draft.Header.BusinessDate, WireBuyer(buyer),
            [.. draft.Lines.Select(line => WireLineTax(line.Id, line.LineNo, line.Tax, buyer))]);
    }

    /// <summary>The Settings lists the order screens offer for <paramref name="on"/>, under the draft read; reference data, no amounts.</summary>
    public async Task<SalesTaxOptionsResponse> GetTaxOptionsAsync(DateOnly on, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        var permission = bindings.ReadPermission;
        var situations = await TaxPortAsync(scope, permission, port => port.GetBuyerSituationsAsync(on, cancellationToken),
            cancellationToken);
        var exemptions = await TaxPortAsync(scope, permission,
            port => port.GetCodeListAsync(TaxCodeListKindsV1.Exemption, on, cancellationToken), cancellationToken);
        if (situations.CompanyId != scope.CompanyId || situations.On != on || exemptions.CompanyId != scope.CompanyId
            || exemptions.On != on || situations.Entries is null || exemptions.Entries is null)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        }

        return new SalesTaxOptionsResponse(on,
            situations.State, situations.Reason, situations.ListVersion,
            [.. situations.Entries.Select(entry => new SalesTaxBuyerSituationOptionV1(entry.Code, entry.Name, entry.WithholdingApplies,
                entry.VatMode, entry.DocumentRequired))],
            exemptions.State, exemptions.Reason,
            [.. exemptions.Entries.Select(entry => new SalesTaxCodeOptionV1(entry.Code, entry.Name))],
            TaxItemTypesV1.All);
    }

    // ---- create -------------------------------------------------------------------------------------------------------------

    /// <summary>The create's tax: the order's buyer tax and every request line's resolved snapshot (keyed by the client line id).</summary>
    private sealed record CreateTax(SalesBuyerTax Buyer, IReadOnlyDictionary<Guid, SalesLineTaxSnapshot?> Lines);

    /// <summary>
    /// SAT-D140/D93: the buyer tax (the request's, checked against the Settings lists; else "Normal") and each line's snapshot (the
    /// Settings default, or the line's reasoned change). Without a composed port: a request that names a buyer tax or a line change
    /// is 503; otherwise no snapshot is stored ("kaynak yok").
    /// </summary>
    private async Task<CreateTax> ResolveCreateTaxAsync(Scope scope, string? permission, CreateSalesOrderDraftRequest request,
        CancellationToken cancellationToken)
    {
        var buyer = BuyerTaxOf(request.BuyerTax, "buyerTax") ?? SalesBuyerTax.Create(StartBuyerSituation);
        var anyChange = request.Lines.Any(line => line.TaxOverride is not null);
        if (lineTaxes is null)
        {
            if (request.BuyerTax is not null || anyChange)
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
            }

            return new CreateTax(buyer, request.Lines.ToDictionary(line => line.LineId, _ => (SalesLineTaxSnapshot?)null));
        }

        var lists = new TaxLists(request.BusinessDate);
        if (request.BuyerTax is not null)
        {
            await RequireBuyerTaxAllowedAsync(scope, permission, buyer, lists, "buyerTax", cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var snapshots = new Dictionary<Guid, SalesLineTaxSnapshot?>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var change = line.TaxOverride is { } requested
                ? await RequireLineTaxChangeAsync(scope, permission, requested.ItemType, requested.BuyerSituation, requested.ExemptionCode,
                    requested.Reason, lists, $"lines[{index}].taxOverride", now, cancellationToken)
                : null;
            snapshots[line.LineId] = await ResolveLineTaxAsync(scope, permission, line.ProductId, buyer, change, request.BusinessDate,
                cancellationToken);
        }

        return new CreateTax(buyer, snapshots);
    }

    /// <summary>The create's price-write fields of the line tax changes (a line tax change moves the line's amounts).</summary>
    private static IEnumerable<SalesDraftFieldError> TaxChangeFields(CreateSalesOrderDraftRequest request) =>
        request.Lines.Select((line, index) => (line, index))
            .Where(pair => pair.line.TaxOverride is not null)
            .Select(pair => new SalesDraftFieldError($"lines[{pair.index}].taxOverride", SalesDraftProblemCodesV1.PriceWriteForbidden));

    // ---- commercial revision ------------------------------------------------------------------------------------------------

    /// <summary>The commercial revision's re-resolved taxes, at the draft version they were resolved on.</summary>
    private sealed record RevisionTax(long DraftVersion, IReadOnlyDictionary<Guid, SalesLineTaxSnapshot?> Lines);

    /// <summary>
    /// SAT-D93/D97/D105: every line's tax re-resolved for the revision — a line's <c>tax</c> choice (<c>Default</c> drops a change,
    /// <c>Override</c> sets one), or its current choice when omitted. Read on the draft as it stands under the fence; null (the lines
    /// keep their snapshots) when no port is composed and no line names a choice, or when the draft is not at the expected version
    /// (the domain then refuses the stale version). A legacy order without a buyer tax keeps "no snapshot" for a line without a change.
    /// </summary>
    private async Task<RevisionTax?> ResolveRevisionTaxAsync(Scope scope, string permission, Guid draftId, long expectedVersion,
        SalesDraftCommercialRevisionRequest request, CancellationToken cancellationToken)
    {
        if (lineTaxes is null)
        {
            if (request.Lines.Any(line => line.Tax is not null))
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
            }

            return null;
        }

        var draft = await LoadDraftAsync(scope, permission, draftId, cancellationToken);
        if (draft.Version != expectedVersion)
        {
            return null;
        }

        var on = draft.Header.BusinessDate;
        var lists = new TaxLists(on);
        var now = timeProvider.GetUtcNow();
        var indexes = request.Lines.Select((line, index) => (line, index)).ToDictionary(pair => pair.line.LineId, pair => pair);
        var snapshots = new Dictionary<Guid, SalesLineTaxSnapshot?>();
        foreach (var line in draft.Lines)
        {
            SalesLineTaxOverride? change = line.Tax?.Override;
            if (indexes.TryGetValue(line.Id, out var named) && named.line.Tax is { } choice)
            {
                if (choice.Mode == SalesDraftLineTaxModesV1.Default)
                {
                    change = null;
                }
                else
                {
                    var requested = await RequireLineTaxChangeAsync(scope, permission, choice.ItemType, choice.BuyerSituation!,
                        choice.ExemptionCode, choice.Reason!, lists, $"lines[{named.index}].tax", now, cancellationToken);
                    // The same basis and reason again is no new change: who/when stay (so a value-equal revision is no change).
                    change = requested.SameChoice(change) ? change : requested;
                }
            }

            snapshots[line.Id] = draft.Header.BuyerTax is null && change is null
                ? null
                : await ResolveLineTaxAsync(scope, permission, line.Product.ProductId,
                    draft.Header.BuyerTax ?? SalesBuyerTax.Create(StartBuyerSituation), change, on, cancellationToken);
        }

        return new RevisionTax(draft.Version, snapshots);
    }

    // ---- header revision ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// SAT-D140 / SAT-D105: a header revision's buyer tax — null when the request names none or the same one (nothing re-resolved);
    /// otherwise the new buyer tax (checked against the Settings lists) and every line's snapshot: a line without a change
    /// re-resolved for it, a line with a reasoned change kept as it is (SAT-D140: "satırlar yine tek tek değiştirilebilir").
    /// </summary>
    private async Task<(SalesBuyerTax Buyer, IReadOnlyDictionary<Guid, SalesLineTaxSnapshot?> Lines)?> ResolveHeaderTaxAsync(Scope scope,
        string? permission, SalesOrderDraft draft, SalesDraftHeaderRevisionRequest request, CancellationToken cancellationToken)
    {
        var buyer = BuyerTaxOf(request.BuyerTax, "buyerTax");
        if (buyer is null || buyer == draft.Header.BuyerTax)
        {
            return null;
        }

        if (lineTaxes is null)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        }

        var on = draft.Header.BusinessDate;
        await RequireBuyerTaxAllowedAsync(scope, permission, buyer, new TaxLists(on), "buyerTax", cancellationToken);
        var snapshots = new Dictionary<Guid, SalesLineTaxSnapshot?>();
        foreach (var line in draft.Lines)
        {
            snapshots[line.Id] = line.Tax?.Override is not null
                ? line.Tax
                : await ResolveLineTaxAsync(scope, permission, line.Product.ProductId, buyer, null, on, cancellationToken);
        }

        return (buyer, snapshots);
    }

    // ---- shared -------------------------------------------------------------------------------------------------------------

    /// <summary>The Settings lists one command reads at most once each (the buyer situations and the exemption codes on one date).</summary>
    private sealed class TaxLists(DateOnly on)
    {
        public DateOnly On { get; } = on;

        public SettingsTaxBuyerSituationListResolutionV1? Situations { get; set; }

        public SettingsTaxCodeListResolutionV1? Exemptions { get; set; }
    }

    /// <summary>The domain's buyer tax of the wire's (null stays null); a shape refusal is 422 on <paramref name="field"/>.</summary>
    private static SalesBuyerTax? BuyerTaxOf(SalesDraftBuyerTaxV1? wire, string field)
    {
        if (wire is null)
        {
            return null;
        }

        try
        {
            return SalesBuyerTax.Create(wire.Situation, wire.ExemptionCode, wire.DocumentNo, wire.DocumentDate);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new(field, exception.Code)]);
        }
    }

    /// <summary>
    /// SAT-D158/D147: the situation is in the list in force; an exemption code only for a "KDV %0 with an exemption code" situation
    /// and only from the exemption list; a document only for a situation that requires one. Each refusal is 422 on
    /// <paramref name="field"/>.
    /// </summary>
    private async Task<TaxBuyerSituationEntryViewV1> RequireBuyerTaxAllowedAsync(Scope scope, string? permission, SalesBuyerTax buyer,
        TaxLists lists, string field, CancellationToken cancellationToken)
    {
        var entry = await RequireSituationAsync(scope, permission, buyer.Situation, lists, $"{field}.situation", cancellationToken);
        if (buyer.ExemptionCode is { } code)
        {
            await RequireExemptionCodeAsync(scope, permission, entry, code, lists, $"{field}.exemptionCode", cancellationToken);
        }

        if (buyer.HasDocument && !entry.DocumentRequired)
        {
            throw new SalesDraftServiceException(422, "validation_failed",
                [new($"{field}.documentNo", SalesRuleCodes.TaxBuyerDocumentNotApplicable)]);
        }

        return entry;
    }

    /// <summary>SAT-D93/D97: a line's reasoned change of its tax basis, every value checked against the Settings lists.</summary>
    private async Task<SalesLineTaxOverride> RequireLineTaxChangeAsync(Scope scope, string? permission, string? itemType,
        string buyerSituation, string? exemptionCode, string reason, TaxLists lists, string field, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is null)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new($"{field}.reason", SalesRuleCodes.TaxOverrideReasonRequired)]);
        }

        if (trimmed.Length > SalesLineTaxOverride.MaximumReasonLength)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new($"{field}.reason", SalesRuleCodes.TaxOverrideReasonTooLong)]);
        }

        if (itemType is not null && !TaxItemTypesV1.IsKnown(itemType))
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new($"{field}.itemType", SalesRuleCodes.TaxItemTypeUnknown)]);
        }

        // The situation and the code follow the order header's shape rules and the same Settings lists.
        var basis = BuyerTaxOf(new SalesDraftBuyerTaxV1(buyerSituation, exemptionCode), field)!;
        var entry = await RequireSituationAsync(scope, permission, basis.Situation, lists, $"{field}.buyerSituation", cancellationToken);
        if (basis.ExemptionCode is { } code)
        {
            await RequireExemptionCodeAsync(scope, permission, entry, code, lists, $"{field}.exemptionCode", cancellationToken);
        }

        return new SalesLineTaxOverride(itemType, basis.Situation, basis.ExemptionCode, trimmed, scope.ActorId, now.ToUniversalTime());
    }

    private async Task<TaxBuyerSituationEntryViewV1> RequireSituationAsync(Scope scope, string? permission, string situation,
        TaxLists lists, string field, CancellationToken cancellationToken)
    {
        lists.Situations ??= await TaxPortAsync(scope, permission, port => port.GetBuyerSituationsAsync(lists.On, cancellationToken),
            cancellationToken);
        if (lists.Situations.CompanyId != scope.CompanyId || lists.Situations.On != lists.On || lists.Situations.Entries is null)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        }

        return lists.Situations.State == TaxResolutionStatesV1.Resolved
            && lists.Situations.Entries.SingleOrDefault(entry => string.Equals(entry.Code, situation, StringComparison.Ordinal)) is { } found
            ? found
            : throw new SalesDraftServiceException(422, "validation_failed", [new(field, SalesRuleCodes.TaxBuyerSituationUnknown)]);
    }

    private async Task RequireExemptionCodeAsync(Scope scope, string? permission, TaxBuyerSituationEntryViewV1 situation, string code,
        TaxLists lists, string field, CancellationToken cancellationToken)
    {
        if (situation.VatMode != TaxBuyerSituationVatModesV1.ZeroWithExemptionCode)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new(field, SalesRuleCodes.TaxExemptionCodeNotApplicable)]);
        }

        lists.Exemptions ??= await TaxPortAsync(scope, permission,
            port => port.GetCodeListAsync(TaxCodeListKindsV1.Exemption, lists.On, cancellationToken), cancellationToken);
        if (lists.Exemptions.CompanyId != scope.CompanyId || lists.Exemptions.On != lists.On || lists.Exemptions.Entries is null)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        }

        if (lists.Exemptions.State != TaxResolutionStatesV1.Resolved
            || lists.Exemptions.Entries.All(entry => !string.Equals(entry.Code, code, StringComparison.Ordinal)))
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new(field, SalesRuleCodes.TaxExemptionCodeUnknown)]);
        }
    }

    /// <summary>
    /// One line's tax from the port: the line's change when it has one (its item type's default when it names one, else the product's
    /// type; its own situation and code), else the product's default for the order's buyer tax. The answer must be for this company,
    /// date, situation (and product): anything else is the port's fault (503), never stored.
    /// </summary>
    private async Task<SalesLineTaxSnapshot> ResolveLineTaxAsync(Scope scope, string? permission, Guid productId, SalesBuyerTax buyer,
        SalesLineTaxOverride? change, DateOnly on, CancellationToken cancellationToken)
    {
        var situation = change?.BuyerSituation ?? buyer.Situation;
        var exemptionCode = change is null ? buyer.ExemptionCode : change.ExemptionCode;
        var answer = change?.ItemType is { } itemType
            ? await TaxPortAsync(scope, permission,
                port => port.GetItemTypeDefaultAsync(itemType, situation, on, exemptionCode, cancellationToken), cancellationToken)
            : await TaxPortAsync(scope, permission,
                port => port.ResolveAsync(productId, situation, on, exemptionCode, cancellationToken), cancellationToken);
        if (answer is null || answer.CompanyId != scope.CompanyId || answer.On != on
            || !string.Equals(answer.BuyerSituation, situation, StringComparison.Ordinal)
            || (change?.ItemType is null ? answer.ProductId != productId : answer.ProductId is not null)
            || string.IsNullOrWhiteSpace(answer.State))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        }

        return new SalesLineTaxSnapshot(productId, on, situation, answer.ItemType, answer.State, answer.Reason, answer.Source,
            answer.RecordId, answer.RecordVersion, answer.VatState, answer.VatPercent, answer.ExemptionCodeState, answer.ExemptionCode,
            answer.ExemptionCodeReason, answer.WithholdingState, answer.WithholdingPercent, answer.WithholdingCodeState,
            answer.WithholdingCode, answer.WithholdingCodeReason, answer.BuyerAdjustment, answer.BuyerSituationListVersion,
            answer.BuyerVatMode, answer.BuyerDocumentRequired, change);
    }

    /// <summary>One read of the port under the fence: denied → 403; not composed or unreadable → 503 (never a guessed answer).</summary>
    private async Task<T> TaxPortAsync<T>(Scope scope, string? permission, Func<ISettingsLineTaxResolverV1, Task<T>> read,
        CancellationToken cancellationToken)
    {
        var port = lineTaxes ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        try
        {
            return await GuardedAsync(scope, permission, () => read(port), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == TaxMasterProblemCodesV1.AccessDenied)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        }
    }

    private static SalesDraftBuyerTaxV1? WireBuyer(SalesBuyerTax? buyer) =>
        buyer is null ? null : new SalesDraftBuyerTaxV1(buyer.Situation, buyer.ExemptionCode, buyer.DocumentNo, buyer.DocumentDate);

    private static SalesDraftLineTaxV1 WireLineTax(Guid lineId, int lineNo, SalesLineTaxSnapshot? tax, SalesBuyerTax? buyer)
    {
        if (tax is null)
        {
            return new SalesDraftLineTaxV1(lineId, lineNo, false, null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, false, null, null, null, null, SalesDraftTaxApiV1.RateNoSource,
                SalesLineTaxReasons.SnapshotMissing, [SalesLineTaxReasons.SnapshotMissing], null);
        }

        var rate = tax.Rate;
        return new SalesDraftLineTaxV1(lineId, lineNo, true, tax.Origin, tax.ResolvedOn, tax.BuyerSituation, tax.ItemType, tax.State,
            tax.Reason, tax.VatState, Percent(tax.VatPercent), tax.ExemptionCodeState, tax.ExemptionCode, tax.ExemptionCodeReason,
            tax.WithholdingState, Percent(tax.WithholdingPercent), tax.WithholdingCodeState, tax.WithholdingCode,
            tax.WithholdingCodeReason, tax.BuyerAdjustment, tax.BuyerDocumentRequired, tax.Source, tax.RecordId, tax.RecordVersion,
            tax.BuyerSituationListVersion, rate is null ? SalesDraftTaxApiV1.RateNoSource : SalesDraftTaxApiV1.RateComputed,
            tax.UnavailableReason, tax.InvoiceBlockers(buyer),
            tax.Override is { } change
                ? new SalesDraftLineTaxOverrideViewV1(change.ItemType, change.BuyerSituation, change.ExemptionCode, change.Reason,
                    change.ChangedBy, change.ChangedAtUtc)
                : null);
    }

    private static string? Percent(decimal? value) => value is { } exact ? SalesLinePriceCalculator.Format(exact) : null;
}
