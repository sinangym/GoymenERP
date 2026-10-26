using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>
/// Explicit adapter until an approved Settings source exists. This adapter never
/// returns data and is not registered as a runtime-ready catalog or policy service.
/// Company access is checked before revealing source availability.
/// </summary>
public sealed class UnavailableSettingsPrerequisiteProvider(
    ICompanyContext companyContext, ICurrentUser currentUser)
    : ISettingsPrerequisiteProvider, ISettingsMoneyCandidateReader, ISettingsMoneyCandidateReaderV2,
        ISettingsManufacturingToleranceProviderV1,
        ISettingsSalesPolicyProviderV1
{
    /// <summary>
    /// No currency source exists, so there are no candidates to offer — which is an
    /// exception, never an empty list: an empty list reads as "this company has no
    /// currency" and a consumer could treat it as a settled answer.
    /// </summary>
    public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
        CancellationToken cancellationToken = default) =>
        Unavailable<IReadOnlyList<MoneyCurrencyCandidateV1>>(cancellationToken);

    public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
        string operation, CancellationToken cancellationToken = default) =>
        Unavailable<IReadOnlyList<MoneyCurrencyCandidateV1>>(cancellationToken);

    public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default) => Unavailable<UnitProfileV1>(cancellationToken);

    public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation,
        DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        Unavailable<MoneyProfileV1>(cancellationToken);

    public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation,
        DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
        Unavailable<PeriodEligibilityV1>(cancellationToken);

    public Task<ManufacturingToleranceProfileV1> GetManufacturingToleranceAsync(
        string process, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        Unavailable<ManufacturingToleranceProfileV1>(cancellationToken);

    public Task<SalesDeliveryTolerancePolicyV1> GetSalesDeliveryToleranceAsync(
        DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        Unavailable<SalesDeliveryTolerancePolicyV1>(cancellationToken);

    public Task<SalesDiscountLimitPolicyV1> GetSalesDiscountLimitAsync(
        Guid limitGroupId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        Unavailable<SalesDiscountLimitPolicyV1>(cancellationToken);

    public Task<SalesApprovalAmountThresholdPolicyV1> GetSalesApprovalAmountThresholdAsync(
        Guid currencyId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        Unavailable<SalesApprovalAmountThresholdPolicyV1>(cancellationToken);

    private Task<T> Unavailable<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            return Task.FromException<T>(new SettingsPrerequisiteException("settings_access_denied"));
        }

        return Task.FromException<T>(new SettingsPrerequisiteException("settings_provider_unavailable"));
    }
}
