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

        var vatRates = await RateListOptionAsync(scope, permission, TaxRateListKindsV1.VatRate, on, cancellationToken);
        var ratios = await RateListOptionAsync(scope, permission, TaxRateListKindsV1.WithholdingRatio, on, cancellationToken);
        return new SalesTaxOptionsResponse(on,
            situations.State, situations.Reason, situations.ListVersion,
            [.. situations.Entries.Select(entry => new SalesTaxBuyerSituationOptionV1(entry.Code, entry.Name, entry.WithholdingApplies,
                entry.VatMode, entry.DocumentRequired))],
            exemptions.State, exemptions.Reason,
            [.. exemptions.Entries.Select(entry => new SalesTaxCodeOptionV1(entry.Code, entry.Name))],
            TaxItemTypesV1.All,
            vatRates.State, vatRates.Reason, RateOptions(vatRates),
            ratios.State, ratios.Reason, RateOptions(ratios));
    }

    /// <summary>SAT-D181: a rate list for the options read; a port not composed is "Unresolved" with the provider reason (no list offered).</summary>
    private async Task<SettingsTaxRateListResolutionV1> RateListOptionAsync(Scope scope, string? permission, string kind, DateOnly on,
        CancellationToken cancellationToken)
    {
        if (rateLists is null)
        {
            return new SettingsTaxRateListResolutionV1(scope.CompanyId, kind, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                TaxMasterProblemCodesV1.ProviderUnavailable);
        }

        var list = await RateListPortAsync(scope, permission, kind, on, cancellationToken);
        return list;
    }

    private static IReadOnlyList<SalesTaxRateOptionV1> RateOptions(SettingsTaxRateListResolutionV1 list) =>
        [.. list.Entries.Select(entry => new SalesTaxRateOptionV1(entry.Code, entry.Name, SalesLinePriceCalculator.Format(entry.Percent), entry.TaxCode))];

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
                    requested.Reason, lists, $"lines[{index}].taxOverride", now, cancellationToken, requested.VatRateCode,
                    requested.WithholdingRatioCode)
                : null;
            snapshots[line.LineId] = await ResolveLineTaxAsync(scope, permission, line.ProductId, buyer, change, request.BusinessDate,
                cancellationToken, lists, $"lines[{index}].taxOverride");
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
                        choice.ExemptionCode, choice.Reason!, lists, $"lines[{named.index}].tax", now, cancellationToken,
                        choice.VatRateCode, choice.WithholdingRatioCode);
                    // The same basis and reason again is no new change: who/when stay (so a value-equal revision is no change).
                    change = requested.SameChoice(change) ? change : requested;
                }
            }

            // A change named in THIS request is checked against the lists (a refusal names its field); a kept one is re-resolved
            // fail-closed (a pick no longer listed leaves the line unresolved, never a guessed rate).
            var field = indexes.TryGetValue(line.Id, out var asked) && asked.line.Tax is { Mode: SalesDraftLineTaxModesV1.Override }
                ? $"lines[{asked.index}].tax"
                : null;
            snapshots[line.Id] = draft.Header.BuyerTax is null && change is null
                ? null
                : await ResolveLineTaxAsync(scope, permission, line.Product.ProductId,
                    draft.Header.BuyerTax ?? SalesBuyerTax.Create(StartBuyerSituation), change, on, cancellationToken, lists, field);
        }

        return new RevisionTax(draft.Version, snapshots);
    }

    // ---- header revision ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// SAT-D140 / SAT-D105: a header revision's buyer tax — null when the request names none or the same one (nothing re-resolved);
    /// otherwise the new buyer tax (checked against the Settings lists) and every line's snapshot: a line without a change
    /// re-resolved for it, a line with a reasoned change kept as it is (SAT-D140: "satırlar yine tek tek değiştirilebilir").
    /// </summary>
    private async Task<(SalesBuyerTax Buyer, IReadOnlyDictionary<Guid, SalesLineTaxSnapshot?>? Lines)?> ResolveHeaderTaxAsync(Scope scope,
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
        if (draft.Header.BuyerTax is { } current && SalesOrderDraft.SameBuyerTaxBasis(buyer, current))
        {
            // SAT-D185 (EK27): only the buyer's document changed — recorded in place, the lines are not re-resolved.
            return (buyer, null);
        }

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

        /// <summary>SAT-D181: the withholding (GİB) code list a picked ratio's code is checked against.</summary>
        public SettingsTaxCodeListResolutionV1? WithholdingCodes { get; set; }

        /// <summary>SAT-D181: the VAT rate and withholding ratio lists, by kind.</summary>
        public Dictionary<string, SettingsTaxRateListResolutionV1> RateLists { get; } = new(StringComparer.Ordinal);
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
        CancellationToken cancellationToken, string? vatRateCode = null, string? withholdingRatioCode = null)
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
        // SAT-D181: a picked VAT rate / withholding ratio must be an entry of the Settings list in force (no list → nothing to pick).
        var vatRate = vatRateCode is null
            ? null
            : await RequireRateEntryAsync(scope, permission, TaxRateListKindsV1.VatRate, vatRateCode, lists, $"{field}.vatRateCode",
                SalesRuleCodes.TaxVatRateUnknown, cancellationToken);
        var zeroRated = entry.VatMode == TaxBuyerSituationVatModesV1.ZeroWithExemptionCode || vatRate?.Percent == 0m;
        if (vatRate?.Percent == 0m && basis.ExemptionCode is null)
        {
            // SAT-D181 (EK26): "%0 KDV seçimi istisna kodu ister" — a picked 0 rate names its exemption code from the list.
            throw new SalesDraftServiceException(422, "validation_failed", [new($"{field}.exemptionCode", SalesRuleCodes.TaxExemptionCodeRequired)]);
        }
        if (basis.ExemptionCode is { } code)
        {
            await RequireExemptionCodeAsync(scope, permission, entry, code, lists, $"{field}.exemptionCode", cancellationToken,
                allowed: zeroRated);
        }

        if (withholdingRatioCode is not null)
        {
            // SAT-D147: no withholding where the VAT is 0 (a KDV %0 situation or a picked 0 rate).
            if (zeroRated)
            {
                throw new SalesDraftServiceException(422, "validation_failed",
                    [new($"{field}.withholdingRatioCode", SalesRuleCodes.TaxWithholdingNotApplicable)]);
            }

            await RequireRateEntryAsync(scope, permission, TaxRateListKindsV1.WithholdingRatio, withholdingRatioCode, lists,
                $"{field}.withholdingRatioCode", SalesRuleCodes.TaxWithholdingRatioUnknown, cancellationToken);
        }

        return new SalesLineTaxOverride(itemType, basis.Situation, basis.ExemptionCode, trimmed, scope.ActorId, now.ToUniversalTime(),
            vatRateCode, withholdingRatioCode);
    }

    /// <summary>The entry of a rate list in force with <paramref name="code"/>; else 422 on <paramref name="field"/>.</summary>
    private async Task<TaxRateListEntryViewV1> RequireRateEntryAsync(Scope scope, string? permission, string kind, string code,
        TaxLists lists, string field, string refusal, CancellationToken cancellationToken) =>
        await RateEntryAsync(scope, permission, kind, code, lists, cancellationToken)
        ?? throw new SalesDraftServiceException(422, "validation_failed", [new(field, refusal)]);

    /// <summary>The entry of a rate list in force with <paramref name="code"/>, or null (not listed, or no list entered).</summary>
    private async Task<TaxRateListEntryViewV1?> RateEntryAsync(Scope scope, string? permission, string kind, string code,
        TaxLists lists, CancellationToken cancellationToken)
    {
        if (!lists.RateLists.TryGetValue(kind, out var list))
        {
            list = await RateListPortAsync(scope, permission, kind, lists.On, cancellationToken);
            lists.RateLists[kind] = list;
        }

        return list.State == TaxResolutionStatesV1.Resolved
            ? list.Entries.SingleOrDefault(entry => string.Equals(entry.Code, code, StringComparison.Ordinal))
            : null;
    }

    /// <summary>One rate list read under the fence; checked for this company and date (else 503).</summary>
    private async Task<SettingsTaxRateListResolutionV1> RateListPortAsync(Scope scope, string? permission, string kind, DateOnly on,
        CancellationToken cancellationToken)
    {
        var port = rateLists ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        SettingsTaxRateListResolutionV1 list;
        try
        {
            list = await GuardedAsync(scope, permission, () => port.GetRateListAsync(kind, on, cancellationToken), cancellationToken);
        }
        catch (SettingsPrerequisiteException exception) when (exception.Code == TaxMasterProblemCodesV1.AccessDenied)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
        }

        return list is not null && list.CompanyId == scope.CompanyId && list.On == on && list.Entries is not null
            && string.Equals(list.Kind, kind, StringComparison.Ordinal)
            && list.Entries.All(entry => entry is not null && entry.Percent is >= 0m and <= 100m
                && (kind != TaxRateListKindsV1.WithholdingRatio || entry.Percent > 0m))
            ? list
            : throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.TaxProviderUnavailable);
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
        TaxLists lists, string field, CancellationToken cancellationToken, bool? allowed = null)
    {
        if (!(allowed ?? situation.VatMode == TaxBuyerSituationVatModesV1.ZeroWithExemptionCode))
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
        SalesLineTaxOverride? change, DateOnly on, CancellationToken cancellationToken, TaxLists? lists = null, string? field = null)
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

        var snapshot = new SalesLineTaxSnapshot(productId, on, situation, answer.ItemType, answer.State, answer.Reason, answer.Source,
            answer.RecordId, answer.RecordVersion, answer.VatState, answer.VatPercent, answer.ExemptionCodeState, answer.ExemptionCode,
            answer.ExemptionCodeReason, answer.WithholdingState, answer.WithholdingPercent, answer.WithholdingCodeState,
            answer.WithholdingCode, answer.WithholdingCodeReason, answer.BuyerAdjustment, answer.BuyerSituationListVersion,
            answer.BuyerVatMode, answer.BuyerDocumentRequired, change);
        return change is { VatRateCode: null, WithholdingRatioCode: null } or null
            ? snapshot
            : await ApplyListedRatesAsync(scope, permission, snapshot, change, lists ?? new TaxLists(on), field, cancellationToken);
    }

    /// <summary>
    /// SAT-D181: the line change's picks from the Settings rate lists applied over the resolved default — the VAT percent and/or
    /// the withheld share are the list entry's. A picked VAT rate of 0 needs the exemption code (the change's, from the list, else
    /// "kaynak yok" and the invoice waits); a picked VAT above 0 needs none. A picked withholding ratio keeps the default's code
    /// part only when the default withheld the same share, else its code is "kaynak yok". Fail-closed: an unresolved default stays
    /// unresolved (nothing is computed over it); a pick no longer in the list in force, or a withholding over a VAT of 0, leaves the
    /// line unresolved with its reason — and a change named in this request is refused on <paramref name="field"/> instead.
    /// </summary>
    private async Task<SalesLineTaxSnapshot> ApplyListedRatesAsync(Scope scope, string? permission, SalesLineTaxSnapshot snapshot,
        SalesLineTaxOverride change, TaxLists lists, string? field, CancellationToken cancellationToken)
    {
        if (snapshot.State != SalesLineTaxStates.Resolved)
        {
            return snapshot;
        }

        SalesLineTaxSnapshot Unresolved(string reason) => snapshot with
        {
            State = SalesLineTaxStates.Unresolved, Reason = reason, VatState = null, VatPercent = null, ExemptionCodeState = null,
            ExemptionCode = null, ExemptionCodeReason = null, WithholdingState = null, WithholdingPercent = null,
            WithholdingCodeState = null, WithholdingCode = null, WithholdingCodeReason = null,
        };

        var result = snapshot;
        if (change.VatRateCode is { } vatCode)
        {
            if (await RateEntryAsync(scope, permission, TaxRateListKindsV1.VatRate, vatCode, lists, cancellationToken) is not { } vat)
            {
                return field is null
                    ? Unresolved(SalesLineTaxReasons.VatRateNotInList)
                    : throw new SalesDraftServiceException(422, "validation_failed", [new($"{field}.vatRateCode", SalesRuleCodes.TaxVatRateUnknown)]);
            }

            var codeListed = vat.Percent == 0m && change.ExemptionCode is { } exemption
                && (await ExemptionListAsync(scope, permission, lists, cancellationToken))
                    .Any(entry => string.Equals(entry.Code, exemption, StringComparison.Ordinal));
            result = result with
            {
                VatState = SalesLineTaxStates.Resolved,
                VatPercent = vat.Percent,
                ExemptionCodeState = vat.Percent > 0m ? SalesLineTaxStates.NotRequired
                    : codeListed ? SalesLineTaxStates.Resolved : SalesLineTaxStates.Unresolved,
                ExemptionCode = vat.Percent > 0m ? null : codeListed ? change.ExemptionCode : null,
                ExemptionCodeReason = vat.Percent > 0m || codeListed ? null
                    : change.ExemptionCode is null ? TaxResolutionReasonsV1.ExemptionCodeMissing : TaxResolutionReasonsV1.CodeNotInList,
            };
            if (vat.Percent == 0m)
            {
                // No withholding over a VAT of 0 (SAT-D147's rule, here for a picked 0 rate).
                result = result with
                {
                    WithholdingState = SalesLineTaxStates.NotApplied, WithholdingPercent = null,
                    WithholdingCodeState = SalesLineTaxStates.NotRequired, WithholdingCode = null, WithholdingCodeReason = null,
                };
            }
        }

        if (change.WithholdingRatioCode is { } ratioCode)
        {
            var vatCarried = result.VatState == SalesLineTaxStates.Resolved && result.VatPercent is > 0m;
            if (!vatCarried)
            {
                return field is null
                    ? Unresolved(SalesRuleCodes.TaxWithholdingNotApplicable)
                    : throw new SalesDraftServiceException(422, "validation_failed",
                        [new($"{field}.withholdingRatioCode", SalesRuleCodes.TaxWithholdingNotApplicable)]);
            }

            if (await RateEntryAsync(scope, permission, TaxRateListKindsV1.WithholdingRatio, ratioCode, lists, cancellationToken) is not { } ratio)
            {
                return field is null
                    ? Unresolved(SalesLineTaxReasons.WithholdingRatioNotInList)
                    : throw new SalesDraftServiceException(422, "validation_failed",
                        [new($"{field}.withholdingRatioCode", SalesRuleCodes.TaxWithholdingRatioUnknown)]);
            }

            // SAT-D181 (EK26): the ratio is invoiced under the GİB code its entry maps to, checked against the SAT-D148 withholding
            // code list in force; no mapping or a code not listed is "kaynak yok" (the invoice waits; the rate stays usable).
            var codeListed = ratio.TaxCode is { } taxCode
                && (await WithholdingCodeListAsync(scope, permission, lists, cancellationToken))
                    .Any(entry => string.Equals(entry.Code, taxCode, StringComparison.Ordinal));
            result = result with
            {
                WithholdingState = SalesLineTaxStates.Resolved,
                WithholdingPercent = ratio.Percent,
                WithholdingCodeState = codeListed ? SalesLineTaxStates.Resolved : SalesLineTaxStates.Unresolved,
                WithholdingCode = codeListed ? ratio.TaxCode : null,
                WithholdingCodeReason = codeListed ? null
                    : ratio.TaxCode is null ? TaxResolutionReasonsV1.WithholdingCodeMissing : TaxResolutionReasonsV1.CodeNotInList,
            };
        }

        return result;
    }

    private async Task<IReadOnlyList<TaxCodeListEntryViewV1>> WithholdingCodeListAsync(Scope scope, string? permission, TaxLists lists,
        CancellationToken cancellationToken)
    {
        lists.WithholdingCodes ??= await TaxPortAsync(scope, permission,
            port => port.GetCodeListAsync(TaxCodeListKindsV1.Withholding, lists.On, cancellationToken), cancellationToken);
        return lists.WithholdingCodes.CompanyId == scope.CompanyId && lists.WithholdingCodes.On == lists.On
            && lists.WithholdingCodes.State == TaxResolutionStatesV1.Resolved && lists.WithholdingCodes.Entries is not null
            ? lists.WithholdingCodes.Entries
            : [];
    }

    private async Task<IReadOnlyList<TaxCodeListEntryViewV1>> ExemptionListAsync(Scope scope, string? permission, TaxLists lists,
        CancellationToken cancellationToken)
    {
        lists.Exemptions ??= await TaxPortAsync(scope, permission,
            port => port.GetCodeListAsync(TaxCodeListKindsV1.Exemption, lists.On, cancellationToken), cancellationToken);
        return lists.Exemptions.CompanyId == scope.CompanyId && lists.Exemptions.On == lists.On
            && lists.Exemptions.State == TaxResolutionStatesV1.Resolved && lists.Exemptions.Entries is not null
            ? lists.Exemptions.Entries
            : [];
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
                    change.ChangedBy, change.ChangedAtUtc, change.VatRateCode, change.WithholdingRatioCode)
                : null);
    }

    private static string? Percent(decimal? value) => value is { } exact ? SalesLinePriceCalculator.Format(exact) : null;
}
