using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's read ports over the company policy registry (B-13, ADR-SET-002) and the FNS-D97 credit account mapping. The only
/// source is the company's stored versions; the latest one answers. Order: cancelled → <see cref="OperationCanceledException"/>;
/// no access, or a company other than the server-resolved one → <c>settings_access_denied</c>; an unreadable store →
/// <c>settings_provider_unavailable</c>; a stored row of another company, key or shape → <c>settings_profile_invalid</c>.
/// <list type="bullet">
/// <item>Opening period: no row is <see cref="FinanceOpeningPeriodStateV1.NotRecorded"/> (never open), version 0.</item>
/// <item>Due-date buffer: no row is the decided start value <see cref="CompanyPolicyInputLimitsV1.DefaultDueDateBufferWorkingDays"/>
/// (URT-D100), version 0.</item>
/// <item>Subcontract tax default: no row is "no default" (<c>null</c>), version 0; sales and purchasing are separate keys.</item>
/// <item>Credit account mapping: no row is <see cref="FinanceCreditAccountMappingStatusV1.MappingMissing"/>.</item>
/// </list>
/// </summary>
public sealed class SettingsCompanyPolicyProviderV1 :
    ISettingsFinancePolicyProviderV1,
    ISettingsManufacturingDueDateBufferProviderV1,
    ISettingsSubcontractTaxDefaultProviderV1,
    ISettingsFinanceCreditAccountMappingProviderV1
{
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;

    public SettingsCompanyPolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
    }

    public async Task<SettingsFinancePolicyV1> GetFinancePolicyAsync(Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var row = await LatestAsync(companyId, CompanyPolicyKeysV1.FinanceOpeningPeriod, cancellationToken);
        if (row is null)
        {
            return new SettingsFinancePolicyV1(companyId, FinanceOpeningPeriodStateV1.NotRecorded, 0);
        }

        if (row.BooleanValue is not { } open)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        }

        return new SettingsFinancePolicyV1(companyId,
            open ? FinanceOpeningPeriodStateV1.Open : FinanceOpeningPeriodStateV1.Closed, row.Version);
    }

    public async Task<SettingsManufacturingDueDateBufferV1> GetDueDateBufferAsync(Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var row = await LatestAsync(companyId, CompanyPolicyKeysV1.ManufacturingDueDateBuffer, cancellationToken);
        if (row is null)
        {
            return new SettingsManufacturingDueDateBufferV1(companyId,
                CompanyPolicyInputLimitsV1.DefaultDueDateBufferWorkingDays, 0);
        }

        if (row.WholeNumberValue is not { } days || days < 0)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        }

        return new SettingsManufacturingDueDateBufferV1(companyId, days, row.Version);
    }

    public async Task<SettingsSubcontractTaxDefaultAnswerV1> GetSubcontractTaxDefaultAsync(Guid companyId,
        SubcontractTaxSideV1 side, CancellationToken cancellationToken = default)
    {
        var key = side switch
        {
            SubcontractTaxSideV1.Sales => CompanyPolicyKeysV1.SalesSubcontractTaxDefault,
            SubcontractTaxSideV1.Purchasing => CompanyPolicyKeysV1.PurchasingSubcontractTaxDefault,
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
        var row = await LatestAsync(companyId, key, cancellationToken);
        if (row is null)
        {
            return new SettingsSubcontractTaxDefaultAnswerV1(companyId, side, null, 0);
        }

        var value = CompanyPolicyRowValues.TaxDefault(row)
            ?? throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new SettingsSubcontractTaxDefaultAnswerV1(companyId, side, value, row.Version);
    }

    public async Task<SettingsFinanceCreditAccountMappingV1> GetCreditAccountMappingAsync(Guid companyId,
        FinanceCreditAccountPurposeV1 purpose, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        RequireAccess(companyId, cancellationToken);
        FinanceCreditAccountMappingVersionRow? row;
        try
        {
            row = await CompanyPolicyReads.LatestCreditMappingAsync(_database, (int)purpose, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }

        if (row is null)
        {
            return new SettingsFinanceCreditAccountMappingV1(companyId, purpose,
                FinanceCreditAccountMappingStatusV1.MappingMissing, null, 0);
        }

        if (row.CompanyId != companyId || row.Version <= 0 || string.IsNullOrWhiteSpace(row.LedgerAccountCode))
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        }

        return new SettingsFinanceCreditAccountMappingV1(companyId, purpose, FinanceCreditAccountMappingStatusV1.Mapped,
            row.LedgerAccountCode, row.Version);
    }

    private async Task<CompanyPolicyVersionRow?> LatestAsync(Guid companyId, string policyKey,
        CancellationToken cancellationToken)
    {
        RequireAccess(companyId, cancellationToken);
        CompanyPolicyVersionRow? row;
        try
        {
            row = await CompanyPolicyReads.LatestAsync(_database, policyKey, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }

        if (row is not null && (row.CompanyId != companyId || row.Version <= 0
            || !string.Equals(row.PolicyKey, policyKey, StringComparison.Ordinal)))
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        }

        return row;
    }

    /// <summary>
    /// Cancelled → <see cref="OperationCanceledException"/>; no access, or a requested company other than the server-resolved
    /// one → <c>settings_access_denied</c>.
    /// </summary>
    private void RequireAccess(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || companyId != _companyContext.CompanyId
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        }
    }
}

/// <summary>Reads a stored tax default row back into its value, refusing a shape the rules would not have written.</summary>
internal static class CompanyPolicyRowValues
{
    public static SettingsSubcontractTaxDefaultV1? TaxDefault(CompanyPolicyVersionRow row)
    {
        if (row.VatApplies is not { } vatApplies || row.WithholdingApplies is not { } withholdingApplies)
        {
            return null;
        }

        var value = new SettingsSubcontractTaxDefaultV1(vatApplies, row.VatPercent, row.ExemptionCode, withholdingApplies,
            row.WithholdingPercent, row.WithholdingCode);
        return CompanyPolicyRulesV1.IsValidTaxDefault(value) ? value : null;
    }
}
