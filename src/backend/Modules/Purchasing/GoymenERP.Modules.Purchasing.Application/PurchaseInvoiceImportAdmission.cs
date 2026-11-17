using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>ALS-D7/D41: the preview is re-read from Clean immutable Documents bytes on the server.
/// The authorized actor explicitly maps line and payment identities; no source master or tax/FX rule is inferred.</summary>
public sealed partial class PurchaseInvoiceImportAdmission(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, ICorrelationContext correlation, IPurchaseInvoiceImportV1 imports,
    IPurchasingIncomingInvoiceStoreV1 store, IPurchasingPartyAccountPortV2 parties,
    IProductPurchaseEligibilityPortV1 products, ISettingsPrerequisiteProvider profiles,
    ISettingsReferenceListReaderV1 references, TimeProvider time,
    ISettingsUnitIdentityCatalogV1? unitCatalog = null, ISettingsMoneyCandidateReaderV2? moneyCatalog = null,
    IPurchaseInvoiceImportProductSearchV1? productSearch = null) : IPurchaseInvoiceImportAdmissionV1, IPurchaseInvoiceImportMappingV1
{
    private sealed record Scope(Guid Company, string Actor, IReadOnlyCollection<string> Roles);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PurchaseIncomingInvoiceOperationResultV1> RecordAsync(Guid importId,
        PurchaseInvoiceImportAdmissionRequestV1 request, PurchasingCommandMetadata metadata, CancellationToken token = default)
    {
        var scope = await RequireAsync(token);
        if (request is null || request.Source is null || request.ExpectedInvoiceVersion != "0" || importId == Guid.Empty
            || request.Lines is not { Count: > 0 and <= PurchaseInvoiceFileBoundsV1.MaximumRows }
            || request.Lines.Any(x => x is null) || request.Installments is not { Count: > 0 and <= PurchaseInvoiceFileBoundsV1.MaximumRows }
            || request.Installments.Any(x => x is null) || !Identity(request.ExpectedPartyVersion, 32)
            || !Identity(request.ExpectedAccountVersion, 32) || metadata is null
            || !Identity(metadata.IdempotencyKey, 200) || !Identity(metadata.CorrelationId, 128)
            || metadata.CausationId is { } cause && !Identity(cause, 128)) throw Invalid();
        var envelope = new PurchasingWriteEnvelope(scope.Actor, PurchaseIncomingInvoicePermissionNamesV1.Import, scope.Roles,
            metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent);
        var hash = PurchasingRequestHash.Compute(PurchaseIncomingInvoiceCommandsV1.Record, importId, request);
        var replay = await GuardAsync(scope, () => store.FindIncomingInvoiceAttemptAsync(PurchaseIncomingInvoiceCommandsV1.Record, envelope, hash, token), token);
        if (replay is not null) return Result(replay, scope.Company, importId);
        var preview = await GuardAsync(scope, () => imports.PreviewAsync(importId, request.Source, token), token);
        if (preview is null || preview.CompanyId != scope.Company || preview.ImportId != importId || preview.Issues is null
            || preview.DocumentId != request.Source.DocumentId || preview.DocumentVersion != request.Source.DocumentVersion
            || !string.Equals(preview.DocumentSha256, request.Source.DocumentSha256, StringComparison.OrdinalIgnoreCase)) throw Unavailable();
        if (preview.Invoice is not { } file || preview.Issues.Count != 0) throw Invalid();
        if (file.Lines is null || file.Lines.Any(x => x is null || x.Taxes is null || x.Net is null)
            || file.Totals is null || file.Taxes is null || file.Rates is null || file.Payments is null || file.Adjustments is null) throw Unavailable();
        try
        {
            var supplier = await GuardAsync(scope, () => parties.GetAuthorizedAsync(request.SupplierPartyId, request.CompanyAccountId, token), token);
            if (supplier.Outcome == PurchasingPartyAccountOutcomeV1.Denied) throw Forbidden();
            if (supplier.Outcome == PurchasingPartyAccountOutcomeV1.Unavailable) throw Unavailable();
            if (supplier.Outcome != PurchasingPartyAccountOutcomeV1.Found || supplier.Account is not { } account
                || account.CompanyId != scope.Company || account.PartyId != request.SupplierPartyId
                || account.CompanyAccountId != request.CompanyAccountId || !account.PartyIsActive || !account.AccountIsActive
                || !account.AccountHasSupplierRole) throw Invalid();
            if (account.PartyVersion != request.ExpectedPartyVersion || account.AccountVersion != request.ExpectedAccountVersion) throw Conflict();
            var money = await MoneyAsync(scope, request.CurrencyId, file.InvoiceDate, token);
            if (money.CurrencyCode != file.Currency) throw Invalid();
            var functional = request.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira ? money
                : await MoneyAsync(scope, SettingsCurrencyIdentitiesV1.TurkishLira, file.InvoiceDate, token);
            if (functional.CurrencyCode != "TRY") throw Unavailable();
            var foreign = money.CurrencyId != functional.CurrencyId;
            var (rate, ratePurpose) = OfficialRate(file, foreign);
            if (request.Lines.Select(x => x.SourceLineId).Distinct(StringComparer.Ordinal).Count() != file.Lines.Count
                || request.Lines.Count != file.Lines.Count || request.Lines.Select(x => x.LineId).Distinct().Count() != file.Lines.Count)
                throw Invalid();
            var lines = new List<PurchaseInvoiceLine>();
            var unitProofs = new List<object>();
            foreach (var source in file.Lines)
            {
                var map = request.Lines.SingleOrDefault(x => x.SourceLineId == source.Id) ?? throw Invalid();
                var unit = await GuardAsync(scope, () => profiles.GetUnitProfileAsync(map.UnitId, file.InvoiceDate, token), token);
                if (unit.CompanyId != scope.Company || unit.UnitId != map.UnitId || !unit.IsActive || unit.UnitCode != source.UnitCode
                    || unit.Profile.ProfileId == Guid.Empty || unit.Profile.Version < 1 || unit.Scale is < 0 or > 6
                    || unit.EffectiveFrom > file.InvoiceDate || unit.EffectiveThrough is { } end && end < file.InvoiceDate) throw Unavailable();
                var quantity = Decimal(source.Quantity);
                if (quantity <= 0 || decimal.Round(quantity, unit.Scale) != quantity) throw Invalid();
                unitProofs.Add(new { source.Id, unit.UnitId, unit.Profile, unit.Scale });
                var itemType = map.ItemType; var purpose = map.LinePurpose;
                if (map.ServiceTypeId is { } service)
                {
                    if (map.ProductId != Guid.Empty || map.ExpenseTypeId is not null) throw Invalid();
                    await ReferenceAsync(scope, SettingsReferenceListKindV1.ServiceType, service, map.ExpectedServiceTypeVersion, token);
                    if (itemType != "Service" || purpose != "service") throw Invalid();
                }
                else if (map.ExpenseTypeId is { } expense)
                {
                    if (map.ProductId != Guid.Empty) throw Invalid();
                    await ReferenceAsync(scope, SettingsReferenceListKindV1.ExpenseType, expense, map.ExpectedExpenseTypeVersion, token);
                    if (itemType != "Expense" || purpose != "expense") throw Invalid();
                }
                else
                {
                    var product = await GuardAsync(scope, () => products.EvaluateAsync(new(map.ProductId,
                        map.ExpectedProductVersion, map.UnitId, file.InvoiceDate), token), token);
                    if (product.Outcome == ProductPurchaseEligibilityOutcomeV1.Denied) throw Forbidden();
                    if (product.Outcome == ProductPurchaseEligibilityOutcomeV1.Unavailable) throw Unavailable();
                    if (product.Outcome != ProductPurchaseEligibilityOutcomeV1.Eligible) throw Invalid();
                    if (product.ProductVersion != map.ExpectedProductVersion) throw Conflict();
                }
                var amounts = Amounts(source, map.VatTaxCode, file.Currency, out var vatPercent);
                lines.Add(new(map.LineId, lines.Count + 1, map.ProductId, map.UnitId, quantity, itemType, purpose,
                    vatPercent, amounts, source.FunctionalAmounts is null ? null : Functional(source.FunctionalAmounts), map.ServiceTypeId, map.ExpenseTypeId));
            }
            var amountsTotal = new PurchaseInvoiceAmounts(lines.Sum(x => x.Amounts.Net), lines.Sum(x => x.Amounts.Vat), lines.Sum(x => x.Amounts.Withholding));
            RequireTotals(file, amountsTotal);
            if (file.Taxes.Count > 0 && (file.Taxes.Any(x => x.Amount.Currency != file.Currency
                || !x.IsWithholding && !request.Lines.Any(line => line.VatTaxCode == x.Code))
                || file.Taxes.Where(x => !x.IsWithholding).Sum(x => Decimal(x.Amount.Value)) != amountsTotal.Vat
                || file.Taxes.Where(x => x.IsWithholding).Sum(x => Decimal(x.Amount.Value)) != amountsTotal.Withholding)) throw Invalid();
            PurchaseInvoiceAmounts? functionalTotal = null;
            if (foreign)
            {
                var documentTotal = file.FunctionalTotals is null ? null : Functional(file.FunctionalTotals);
                if (lines.All(x => x.FunctionalAmounts is null))
                {
                    var converted = PurchaseInvoiceFunctionalAmountCalculator.Calculate(lines.Select(x =>
                        new PurchaseInvoiceFunctionalAmountCalculator.Line(x.Id, x.Amounts)).ToArray(), rate!.Value,
                        functional.CurrencyId, functional.Scale, documentTotal);
                    lines = lines.Select((line, index) => line with { FunctionalAmounts = converted.Lines[index].Amounts }).ToList();
                    functionalTotal = converted.Total;
                }
                else
                {
                    if (lines.Any(x => x.FunctionalAmounts is null)) throw Invalid();
                    functionalTotal = new(lines.Sum(x => x.FunctionalAmounts!.Net), lines.Sum(x => x.FunctionalAmounts!.Vat), lines.Sum(x => x.FunctionalAmounts!.Withholding));
                    if (documentTotal is not null && documentTotal != functionalTotal) throw Invalid();
                }
            }
            var installments = request.Installments.Select(x => new PurchaseInvoiceInstallment(x.Id, x.OpenItemId, x.Sequence,
                x.DueOn, Decimal(x.Amount), x.FunctionalAmount is null ? null : Decimal(x.FunctionalAmount))).ToArray();
            // One installment is the whole authoritative total, not a distribution policy. Multiple
            // installments still require explicit TL shares; the domain checks their exact total.
            if (foreign && installments.Length == 1 && installments[0].FunctionalAmount is null)
                installments[0] = installments[0] with { FunctionalAmount = functionalTotal!.Payable };
            // A source due date/amount is authoritative when present; missing dates require the actor's explicit plan.
            var sourcePlan = file.Payments.Where(x => x.Amount is not null).ToArray();
            if (sourcePlan.Length != 0 && (sourcePlan.Length != installments.Length || sourcePlan.Where((x, i) =>
                x.Amount!.Currency != file.Currency || Decimal(x.Amount.Value) != installments[i].Amount
                || (x.InstallmentDueOn ?? x.DueOn) is { } due && due != installments[i].DueOn).Any())) throw Invalid();
            if (sourcePlan.Length == 0)
            {
                var sourceDates = file.Payments.Select(x => x.InstallmentDueOn ?? x.DueOn).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
                if (sourceDates.Length > 0 && (installments.Any(x => !sourceDates.Contains(x.DueOn))
                    || sourceDates.Any(date => !installments.Any(x => x.DueOn == date)))) throw Invalid();
            }
            var proof = new PurchaseInvoiceImportProof(importId, "purchase-invoice-file-reader.v1",
                JsonSerializer.Serialize(request.Source.Options, Json), JsonSerializer.Serialize(request.Lines, Json),
                money.Profile.ProfileId, money.Profile.Version, functional.Profile.ProfileId, functional.Profile.Version,
                JsonSerializer.Serialize(unitProofs, Json), ratePurpose);
            var invoice = PurchaseIncomingInvoice.Record(importId, scope.Company, request.SupplierPartyId, request.CompanyAccountId,
                money.CurrencyId, functional.CurrencyId, money.Scale, functional.Scale, rate,
                new(preview.DocumentId, preview.DocumentVersion, preview.DocumentSha256, file.Number, file.Ettn, file.InvoiceDate, proof),
                amountsTotal, functionalTotal, lines, installments, scope.Actor, time.GetUtcNow());
            var result = await GuardAsync(scope, () => store.RecordIncomingInvoiceAsync(invoice, envelope, hash,
                ct => FenceAsync(scope, ct), token), token);
            return Result(result, scope.Company, importId);
        }
        catch (PurchasingServiceException exception) when (exception.Status == 409 && exception.Code == PurchasingProblemCodesV1.VersionConflict)
        {
            // ADR-011: a terminal stale-owner decision belongs to this exact command attempt.
            // It must not turn into a different answer when a master changes before a retry.
            var refused = await GuardAsync(scope, () => store.RecordIncomingInvoiceRefusalAsync(
                PurchaseIncomingInvoiceCommandsV1.Record, envelope, hash,
                new(409, exception.Code, [new("invoice", exception.Code)]),
                ct => FenceAsync(scope, ct), token), token);
            return Result(refused, scope.Company, importId);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or FormatException)
        { await FenceAsync(scope, token); throw Invalid(); }
    }

    private async Task ReferenceAsync(Scope scope, SettingsReferenceListKindV1 kind, Guid id, string? version, CancellationToken token)
    {
        var answer = await GuardAsync(scope, () => references.GetAsync(scope.Company, kind, id, token), token);
        if (answer.Outcome == SettingsReferenceListLookupOutcomeV1.Unavailable) throw Unavailable();
        if (answer.Outcome != SettingsReferenceListLookupOutcomeV1.Found || answer.Entry is not { Active: true } entry || entry.Id != id) throw Invalid();
        if (entry.Version.ToString(CultureInfo.InvariantCulture) != version) throw Conflict();
    }
    private async Task<MoneyProfileV1> MoneyAsync(Scope scope, Guid id, DateOnly date, CancellationToken token)
    {
        var money = await GuardAsync(scope, () => profiles.GetMoneyProfileAsync(id, PurchasingMoneyOperationsV1.OrderV1, date, token), token);
        if (money.CompanyId != scope.Company || money.CurrencyId != id || !money.IsActive || money.Operation != PurchasingMoneyOperationsV1.OrderV1
            || money.Profile.ProfileId == Guid.Empty || money.Profile.Version < 1 || money.Scale is < 0 or > 6
            || money.EffectiveFrom > date || money.EffectiveThrough is { } end && end < date) throw Unavailable();
        return money;
    }
    private static (decimal? Rate, string? Purpose) OfficialRate(PurchaseInvoiceFileFactsV1 file, bool foreign)
    {
        if (!foreign) return (null, null);
        var rates = file.Rates.Where(x => (x.Purpose is "OfficialInvoiceRate" or "TaxExchangeRate")
            && x.SourceCurrency == file.Currency && x.TargetCurrency == "TRY").ToArray();
        if (rates.Length != 1 || rates[0].Operator is not null || rates[0].SourceBaseRate is { } source && Decimal(source) != 1m
            || rates[0].TargetBaseRate is { } target && Decimal(target) != 1m || Decimal(rates[0].Rate) <= 0) throw Invalid();
        return (Decimal(rates[0].Rate), rates[0].Purpose);
    }
    private static PurchaseInvoiceAmounts Amounts(PurchaseInvoiceFileLineV1 line, string vatCode, string currency, out decimal percent)
    {
        if (line.Net.Currency != currency || line.Taxes.Any(x => x.Amount.Currency != currency)
            || line.Taxes.Any(x => !x.IsWithholding && x.Code != vatCode)) throw Invalid();
        var vat = line.Taxes.Where(x => !x.IsWithholding).ToArray();
        if (vat.Length > 1 || vat.Length == 1 && vat[0].Percent is null) throw Invalid();
        percent = vat.Length == 0 ? 0m : Decimal(vat[0].Percent!);
        return new(Decimal(line.Net.Value), vat.Sum(x => Decimal(x.Amount.Value)), line.Taxes.Where(x => x.IsWithholding).Sum(x => Decimal(x.Amount.Value)));
    }
    private static void RequireTotals(PurchaseInvoiceFileFactsV1 file, PurchaseInvoiceAmounts total)
    {
        foreach (var field in new[] { "AllowanceTotalAmount", "ChargeTotalAmount", "PrepaidAmount", "PayableRoundingAmount" })
            if (file.Totals.TryGetValue(field, out var adjustment) && Decimal(adjustment.Value) != 0m) throw Invalid();
        foreach (var (name, expected) in new[] { ("LineExtensionAmount", total.Net), ("TaxExclusiveAmount", total.Net),
            ("TaxInclusiveAmount", checked(total.Net + total.Vat)), ("PayableAmount", total.Payable) })
            if (!file.Totals.TryGetValue(name, out var source) || source.Currency != file.Currency || Decimal(source.Value) != expected) throw Invalid();
    }
    private async Task<Scope> RequireAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw new PurchasingServiceException(401, "unauthenticated");
        var id = company.CompanyId; var actor = user.UserId;
        if (!Same(id, actor)) throw Forbidden();
        var permission = await authority.RevalidatePermissionScopeAsync(PurchaseIncomingInvoicePermissionNamesV1.Import, id, token);
        if (!permission.IsGranted || !permission.IsCompanyWide || !Same(id, actor)) throw Forbidden();
        var price = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersPriceRead, id, token);
        if (!price.IsGranted || !price.IsCompanyWide || !Same(id, actor)) throw Forbidden();
        return new(id, actor!, permission.RoleCodes.Order(StringComparer.Ordinal).ToArray());
    }
    private bool Same(Guid id, string? actor) => company.IsResolved && id != Guid.Empty && company.CompanyId == id
        && user.IsAuthenticated && user.UserId == actor && user.CanAccessCompany(id);
    private async Task FenceAsync(Scope scope, CancellationToken token)
    { var current = await RequireAsync(token); if (current.Company != scope.Company || current.Actor != scope.Actor) throw Forbidden(); }
    private async Task<T> GuardAsync<T>(Scope scope, Func<Task<T>> action, CancellationToken token)
    {
        try { var result = await action(); await FenceAsync(scope, token); return result; }
        catch (OperationCanceledException) { throw; }
        catch (PurchasingServiceException) { await FenceAsync(scope, token); throw; }
        catch (PurchasingStoreException exception)
        {
            await FenceAsync(scope, token);
            throw exception.Code switch
            {
                PurchasingStoreCodes.SameKeyPayloadMismatch => new PurchasingServiceException(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch),
                PurchasingStoreCodes.ConcurrentChange => new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange),
                PurchasingStoreCodes.CompanyContextMismatch => Forbidden(), _ => Unavailable(),
            };
        }
        catch { await FenceAsync(scope, token); throw Unavailable(); }
    }
    private static PurchaseIncomingInvoiceOperationResultV1 Result(PurchasingIncomingInvoiceWriteResult result, Guid company, Guid id)
    {
        if (result.Refusal is { } refusal) throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors,
            result.IsReplay, result.OriginalCorrelationId);
        if (result.Invoice is not { } invoice || invoice.CompanyId != company || invoice.Id != id) throw Unavailable();
        return new(invoice, result.IsReplay, result.OriginalCorrelationId);
    }
    private static decimal Decimal(string value)
    {
        if (value is null || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var result)) throw Invalid();
        // decimal.TryParse can round excess significant digits. Reject lost source/request precision;
        // only redundant leading/trailing zeroes are normalized, never money or quantity.
        var negative = value.StartsWith('-');
        var magnitude = value.StartsWith('-') || value.StartsWith('+') ? value[1..] : value;
        var parts = magnitude.Split('.');
        var whole = parts[0].TrimStart('0'); if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        var canonical = (negative && (whole != "0" || fraction.Length > 0) ? "-" : "") + whole + (fraction.Length == 0 ? "" : "." + fraction);
        if (canonical != result.ToString("0.############################", CultureInfo.InvariantCulture)) throw Invalid();
        return result;
    }
    private static PurchaseInvoiceAmounts Functional(PurchaseInvoiceFileFunctionalAmountsV1 amounts)
        => new(Decimal(amounts.Net), Decimal(amounts.Vat), Decimal(amounts.Withholding));
    private static bool Identity(string value, int maximum) => !string.IsNullOrWhiteSpace(value) && value == value.Trim()
        && value.Length <= maximum && !value.Any(char.IsControl);
    private static PurchasingServiceException Invalid() => new(400, "validation_failed");
    private static PurchasingServiceException Conflict() => new(409, PurchasingProblemCodesV1.VersionConflict);
    private static PurchasingServiceException Forbidden() => new(403, "forbidden");
    private static PurchasingServiceException Unavailable() => new(503, "service_unavailable");
}
