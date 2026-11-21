using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsWorkCenterCapacityProviderV1"/> (URT-D87/URT-D89). The only source is the company's stored
/// capacity versions of the work center; the latest one answers. No recorded version answers <c>null</c> (no capacity: the
/// due-date suggestion is "hesaplanamadı", never a guessed capacity); a version without a buffer answers a zero buffer with no
/// unit. Order: cancelled → <see cref="OperationCanceledException"/>; no access, or a company other than the server-resolved
/// one → <c>settings_access_denied</c>; an unreadable store → <c>settings_provider_unavailable</c>; a stored row of another
/// company or work center, or outside the published shape → <c>settings_profile_invalid</c>. An error never falls back to a
/// default capacity.
/// </summary>
public sealed class SettingsWorkCenterCapacityProviderV1 : ISettingsWorkCenterCapacityProviderV1
{
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;

    public SettingsWorkCenterCapacityProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
    }

    public async Task<SettingsWorkCenterCapacityV1?> GetWorkCenterCapacityAsync(Guid companyId, Guid workCenterId,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(companyId, cancellationToken);
        if (workCenterId == Guid.Empty)
        {
            return null;
        }

        WorkCenterCapacityVersionRow? row;
        try
        {
            row = await WorkCenterCapacityReads.LatestAsync(_database, workCenterId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(WorkCenterCapacityProblemCodesV1.ProviderUnavailable);
        }

        if (row is null)
        {
            return null;
        }

        var bufferValid = row.SafetyBufferValue is null
            ? row.SafetyBufferUnit is null
            : row.SafetyBufferValue > 0m && WorkCenterCapacityRulesV1.IsSafetyBufferUnit(row.SafetyBufferUnit);
        if (row.CompanyId != companyId || row.WorkCenterId != workCenterId || row.Version <= 0
            || row.ProfileId == Guid.Empty || row.InputCapacityKgPerHour <= 0m || !bufferValid)
        {
            throw new SettingsPrerequisiteException(WorkCenterCapacityProblemCodesV1.ProfileInvalid);
        }

        return new SettingsWorkCenterCapacityV1(
            companyId,
            workCenterId,
            new ProfileVersionV1(row.ProfileId, row.Version),
            row.InputCapacityKgPerHour,
            row.SafetyBufferValue ?? 0m,
            row.SafetyBufferUnit);
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
            throw new SettingsPrerequisiteException(WorkCenterCapacityProblemCodesV1.AccessDenied);
        }
    }
}
