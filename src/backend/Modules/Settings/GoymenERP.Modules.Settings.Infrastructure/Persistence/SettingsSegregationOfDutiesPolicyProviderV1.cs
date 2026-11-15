using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The host's <see cref="ISettingsSegregationOfDutiesPolicyProviderV1"/> (F-024, NS-01). The only source is the company's
/// stored segregation of duties versions; the latest one answers. No recorded version answers the decided default (the
/// preparer may not approve), version 0. Order: cancelled → <see cref="OperationCanceledException"/>; no access, or a company
/// other than the server-resolved one → <c>settings_access_denied</c>; an unreadable store →
/// <c>settings_provider_unavailable</c>; a stored row of another company → <c>settings_profile_invalid</c>. An error never
/// falls back to a permissive answer.
/// <para>
/// KIM-D39: a stored relaxation answers only while the Identity owner reports exactly one active user in the company, asked
/// on every read through <see cref="ISettingsCompanyActiveUsersV1"/>. With a second active user the answer is
/// <c>PreparerMayApprove = false</c> at the same version (the relaxation closed by itself; the read writes nothing, the
/// durable system version is written by the administration's read). A refused fence → <c>settings_access_denied</c>; an
/// owner failure or no composed port → <c>settings_provider_unavailable</c>.
/// </para>
/// </summary>
public sealed class SettingsSegregationOfDutiesPolicyProviderV1 : ISettingsSegregationOfDutiesPolicyProviderV1
{
    private const string ProfileInvalidCode = "settings_profile_invalid";
    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly ISettingsCompanyActiveUsersV1? _activeUsers;

    public SettingsSegregationOfDutiesPolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser, ISettingsCompanyActiveUsersV1? activeUsers = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
        _activeUsers = activeUsers;
    }

    public async Task<SettingsSegregationOfDutiesPolicyV1> GetSegregationOfDutiesPolicyAsync(Guid companyId,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(companyId, cancellationToken);
        SegregationOfDutiesPolicyVersionRow? row;
        try
        {
            row = await SegregationOfDutiesPolicyReads.LatestAsync(_database, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
        }

        if (row is null)
        {
            return new SettingsSegregationOfDutiesPolicyV1(companyId, SegregationOfDutiesRuleV1.DefaultPreparerMayApprove, 0);
        }

        if (row.CompanyId != companyId || row.Version <= 0)
        {
            throw new SettingsPrerequisiteException(ProfileInvalidCode);
        }

        if (!row.PreparerMayApprove)
        {
            return new SettingsSegregationOfDutiesPolicyV1(companyId, false, row.Version);
        }

        var single = await SegregationOfDutiesSingleUserCheck.HasSingleActiveUserAsync(
            _activeUsers, companyId, _currentUser.UserId!, cancellationToken);
        RequireAccess(companyId, cancellationToken);
        return new SettingsSegregationOfDutiesPolicyV1(companyId, single, row.Version);
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
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.AccessDenied);
        }
    }
}
