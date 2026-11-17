using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.SettingsRuntime;

/// <summary>Composition of actual Settings owners, not a second collateral policy store or an authority grant.</summary>
public sealed class SettingsFinanceCollateralPolicyReader(ISettingsBankInstitutionCatalogV1 banks,
    ISettingsCollateralRateProviderV1 rates, ICompanyContext company, ICurrentUser user) : IFinanceCollateralPolicyReaderV1
{
    public async Task<FinanceCollateralBankCatalogV1> GetBankInstitutionsAsync(Guid companyId, DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        var actor = Capture(companyId, cancellationToken);
        var answer = await banks.GetBankInstitutionsAsync(companyId, businessDate, cancellationToken);
        Same(companyId, actor, cancellationToken);
        if (answer.Outcome != SettingsFinancePolicyReadOutcomeV1.Available || answer.CompanyId != companyId
            || answer.Entries is null || string.IsNullOrWhiteSpace(answer.SourceVersion)
            || answer.Entries.Any(x => x.BankInstitutionId == Guid.Empty || x.SourceVersion != answer.SourceVersion
                || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.EftCode))
            || answer.Entries.Select(x => x.BankInstitutionId).Distinct().Count() != answer.Entries.Count)
            return new(false, companyId, []);
        return new(true, companyId, answer.Entries.Where(x => x.Active).Select(x => new FinanceCollateralBankProofV1(
            companyId, x.BankInstitutionId, x.Name, x.EftCode, x.SourceVersion)).ToArray());
    }

    public async Task<FinanceCollateralRateReadV1> GetRateAsync(Guid companyId, Guid collateralTypeId, Guid? bankInstitutionId,
        string? bankSourceVersion, DateOnly? validUntil, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        var actor = Capture(companyId, cancellationToken);
        var answer = await rates.GetCollateralRateAsync(companyId, collateralTypeId, bankInstitutionId, bankSourceVersion,
            validUntil, businessDate, cancellationToken);
        Same(companyId, actor, cancellationToken);
        return answer.Outcome == SettingsFinancePolicyReadOutcomeV1.Available && !string.IsNullOrWhiteSpace(answer.SourceVersion)
            && answer.Scope is "Type" or "TypeBank" or "TypeBankRemainingDays" or "Company"
            ? new(true, answer.RatePercent, answer.SourceVersion, answer.Scope, null, answer.CollateralTypeVersion)
            : new(false, null, null, null, "finance_collateral_type_rate_unavailable");
    }

    private string Capture(Guid id, CancellationToken token)
    { var actor = user.UserId; Same(id, actor, token); return actor!; }
    private void Same(Guid id, string? actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (id == Guid.Empty || !company.IsResolved || company.CompanyId != id || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(id))
            throw new UnauthorizedAccessException();
    }
}
