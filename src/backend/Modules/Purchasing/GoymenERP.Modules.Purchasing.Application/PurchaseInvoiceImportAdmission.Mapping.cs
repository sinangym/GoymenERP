using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchaseInvoiceImportAdmission
{
    public async Task<PurchaseInvoiceImportReferencesV1> ReferencesAsync(Guid importId, PurchaseInvoiceImportPreviewRequestV1 source, CancellationToken token = default)
    {
        var scope = await RequireAsync(token); var preview = await MappingSourceAsync(scope, importId, source, token);
        var date = preview.Invoice!.InvoiceDate;
        if (unitCatalog is null || moneyCatalog is null) throw Unavailable();
        var identities = await GuardAsync(scope, () => unitCatalog.ListAsync(token), token);
        if (identities is null || identities.Any(x => x is null || x.UnitId == Guid.Empty || string.IsNullOrWhiteSpace(x.Code))
            || identities.Select(x => x.UnitId).Distinct().Count() != identities.Count) throw Unavailable();
        var units = new List<PurchaseInvoiceImportUnitV1>();
        foreach (var identity in identities.Where(identity => preview.Invoice.Lines.Any(line => line.UnitCode == identity.Code)))
        {
            var profile = await GuardAsync(scope, () => profiles.GetUnitProfileAsync(identity.UnitId, date, token), token);
            if (profile.CompanyId != scope.Company || profile.UnitId != identity.UnitId || profile.Profile.ProfileId == Guid.Empty
                || profile.Profile.Version < 1 || profile.Scale is < 0 or > 6 || profile.UnitCode != identity.Code) throw Unavailable();
            if (profile.IsActive && profile.EffectiveFrom <= date && (profile.EffectiveThrough is not { } through || date <= through))
                units.Add(new(profile.UnitId, profile.UnitCode, profile.Profile.ProfileId, TextVersion(profile.Profile.Version), profile.Scale));
        }
        var currencies = new List<PurchaseInvoiceImportCurrencyV1>();
        var candidates = await GuardAsync(scope, async () => MoneyCurrencyCandidatesV1.EnsureWellFormed(
            await moneyCatalog.GetMoneyCurrencyCandidatesAsync(PurchasingMoneyOperationsV1.OrderV1, token)), token);
        foreach (var candidate in candidates.Where(x => x.Code == preview.Invoice.Currency))
        {
            var profile = await MoneyAsync(scope, candidate.CurrencyId, date, token);
            currencies.Add(new(profile.CurrencyId, profile.CurrencyCode, profile.Profile.ProfileId, TextVersion(profile.Profile.Version), profile.Scale));
        }
        if (!preview.Invoice.Totals.TryGetValue("PayableAmount", out var payable) || payable.Currency != preview.Invoice.Currency) throw Invalid();
        string? functionalPayable = null;
        if (preview.Invoice.Currency != "TRY")
        {
            var profile = await MoneyAsync(scope, SettingsCurrencyIdentitiesV1.TurkishLira, date, token);
            var file = preview.Invoice; var (rate, _) = OfficialRate(file, true);
            var lines = file.Lines.Select(line =>
            {
                var taxes = line.Taxes.Where(tax => !tax.IsWithholding).ToArray();
                if (taxes.Length > 1) throw Invalid();
                return new PurchaseInvoiceFunctionalAmountCalculator.Line(Guid.NewGuid(),
                    Amounts(line, taxes.FirstOrDefault()?.Code ?? "", file.Currency, out _));
            }).ToArray();
            PurchaseInvoiceAmounts total;
            try
            {
                if (file.Lines.All(line => line.FunctionalAmounts is null))
                    total = PurchaseInvoiceFunctionalAmountCalculator.Calculate(lines, rate!.Value, profile.CurrencyId, profile.Scale,
                        file.FunctionalTotals is null ? null : Functional(file.FunctionalTotals)).Total;
                else
                {
                    if (file.Lines.Any(line => line.FunctionalAmounts is null)) throw Invalid();
                    total = new(file.Lines.Sum(line => Decimal(line.FunctionalAmounts!.Net)), file.Lines.Sum(line => Decimal(line.FunctionalAmounts!.Vat)),
                        file.Lines.Sum(line => Decimal(line.FunctionalAmounts!.Withholding)));
                    total.Validate(profile.Scale);
                    if (file.FunctionalTotals is not null && Functional(file.FunctionalTotals) != total) throw Invalid();
                }
                functionalPayable = total.Payable.ToString(CultureInfo.InvariantCulture);
            }
            catch (ArgumentException) { throw Invalid(); }
            catch (OverflowException) { throw Invalid(); }
        }
        return new(scope.Company, importId, preview.DocumentId, preview.DocumentVersion, preview.DocumentSha256, date, units, currencies,
            payable.Value, functionalPayable,
            await OptionalTypesAsync(scope, SettingsReferenceListKindV1.ServiceType, token),
            await OptionalTypesAsync(scope, SettingsReferenceListKindV1.ExpenseType, token));
    }

    public async Task<PurchaseInvoiceImportSupplierV1> SupplierAsync(Guid importId, PurchaseInvoiceImportSupplierRequestV1 request, CancellationToken token = default)
    {
        var scope = await RequireAsync(token);
        if (request is null || request.SupplierPartyId == Guid.Empty || request.CompanyAccountId == Guid.Empty) throw Invalid();
        await MappingSourceAsync(scope, importId, request.Source, token);
        var answer = await GuardAsync(scope, () => parties.GetAuthorizedAsync(request.SupplierPartyId, request.CompanyAccountId, token), token);
        if (answer.Outcome == PurchasingPartyAccountOutcomeV1.Denied) throw Forbidden();
        if (answer.Outcome == PurchasingPartyAccountOutcomeV1.Unavailable) throw Unavailable();
        if (answer.Outcome != PurchasingPartyAccountOutcomeV1.Found || answer.Account is not { } account
            || account.CompanyId != scope.Company || account.PartyId != request.SupplierPartyId || account.CompanyAccountId != request.CompanyAccountId
            || !account.PartyIsActive || !account.AccountIsActive || !account.AccountHasSupplierRole) throw Invalid();
        if (!Identity(account.PartyVersion, 32) || !Identity(account.AccountVersion, 32)) throw Unavailable();
        return new(scope.Company, importId, account.PartyId, account.CompanyAccountId, account.PartyVersion, account.AccountVersion);
    }

    public async Task<PurchaseInvoiceImportProductsV1> ProductsAsync(Guid importId, PurchaseInvoiceImportProductsRequestV1 request, CancellationToken token = default)
    {
        var scope = await RequireAsync(token);
        if (request is null || request.Q is { Length: > 200 }) throw Invalid();
        var preview = await MappingSourceAsync(scope, importId, request.Source, token);
        var port = productSearch ?? throw Unavailable();
        var answer = await GuardAsync(scope, () => port.SearchAsync(request.Q, preview.Invoice!.InvoiceDate, token), token);
        if (answer.Items is null || answer.Items.Count > 25 || answer.Items.Any(x => x is null || x.ProductId == Guid.Empty
            || x.UnitId == Guid.Empty || x.ProfileId == Guid.Empty || !Identity(x.ProductVersion, 128) || !Identity(x.Code, 128)
            || !Identity(x.Name, 4000) || !Identity(x.UnitCode, 128) || !Identity(x.ProfileVersion, 32))
            || answer.Items.Select(x => (x.ProductId, x.UnitId)).Distinct().Count() != answer.Items.Count) throw Unavailable();
        return new(scope.Company, importId, answer.Items, answer.SearchWindowFull);
    }

    private async Task<PurchaseInvoiceImportPreviewV1> MappingSourceAsync(Scope scope, Guid id, PurchaseInvoiceImportPreviewRequestV1 source, CancellationToken token)
    {
        if (source is null || id == Guid.Empty) throw Invalid();
        var answer = await GuardAsync(scope, () => imports.PreviewAsync(id, source, token), token);
        if (answer is null || answer.CompanyId != scope.Company || answer.ImportId != id || answer.DocumentId != source.DocumentId
            || answer.DocumentVersion != source.DocumentVersion || !string.Equals(answer.DocumentSha256, source.DocumentSha256, StringComparison.OrdinalIgnoreCase)) throw Unavailable();
        if (answer.Invoice is null || answer.Issues is null || answer.Issues.Count != 0) throw Invalid();
        if (answer.Invoice.Lines is null || answer.Invoice.Lines.Any(x => x is null || x.Taxes is null || x.Net is null)
            || answer.Invoice.Totals is null || answer.Invoice.Rates is null) throw Unavailable();
        return answer;
    }
    private async Task<IReadOnlyList<PurchaseInvoiceImportReferenceV1>?> OptionalTypesAsync(Scope scope, SettingsReferenceListKindV1 kind, CancellationToken token)
    {
        try
        {
            var list = await GuardAsync(scope, () => references.ListAsync(scope.Company, kind, token), token);
            if (list.CompanyId != scope.Company || list.Kind != kind || list.Entries is null) throw Unavailable();
            if (!list.Enabled) return null;
            if (list.Entries.Any(x => x is null || x.Id == Guid.Empty || x.Version < 1 || !Identity(x.Code, 128) || !Identity(x.Name, 4000))
                || list.Entries.Select(x => x.Id).Distinct().Count() != list.Entries.Count) throw Unavailable();
            return list.Entries.Where(x => x.Active).Select(x => new PurchaseInvoiceImportReferenceV1(x.Id, TextVersion(x.Version), x.Code, x.Name)).ToArray();
        }
        catch (PurchasingServiceException error) when (error.Status == 503) { return null; }
    }
    private static string TextVersion(long value) => value.ToString(CultureInfo.InvariantCulture);
}
