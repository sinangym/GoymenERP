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
    ICompanyContext companyContext, ICurrentUser currentUser) : ISettingsPrerequisiteProvider
{
    public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default) => Unavailable<UnitProfileV1>(cancellationToken);

    public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation,
        DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
        Unavailable<MoneyProfileV1>(cancellationToken);

    public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation,
        DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) =>
        Unavailable<PeriodEligibilityV1>(cancellationToken);

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
