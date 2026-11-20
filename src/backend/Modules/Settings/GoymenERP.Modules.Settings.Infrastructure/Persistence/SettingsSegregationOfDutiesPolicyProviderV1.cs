using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

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
/// <c>PreparerMayApprove = false</c> at a new durable system version, committed with its audit before answering.
/// Returning to one user never revives the earlier relaxation. A refused fence → <c>settings_access_denied</c>; an
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
    private readonly ICorrelationContext? _correlation;
    private readonly TimeProvider _time;
    private readonly IServiceScopeFactory? _scopes;

    public SettingsSegregationOfDutiesPolicyProviderV1(SettingsDbContext database, ICompanyContext companyContext,
        ICurrentUser currentUser, ISettingsCompanyActiveUsersV1? activeUsers = null,
        ICorrelationContext? correlation = null, TimeProvider? timeProvider = null, IServiceScopeFactory? scopes = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
        _activeUsers = activeUsers;
        _correlation = correlation;
        _time = timeProvider ?? TimeProvider.System;
        _scopes = scopes;
    }

    public async Task<SettingsSegregationOfDutiesPolicyV1> GetSegregationOfDutiesPolicyAsync(Guid companyId,
        CancellationToken cancellationToken = default)
    {
        RequireAccess(companyId, cancellationToken);
        SegregationOfDutiesPolicyVersionRow? row;
        try
        {
            row = await ReadLatestAsync(companyId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
        }

        RequireAccess(companyId, cancellationToken);
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
        if (!single)
        {
            var actorId = _currentUser.UserId!;
            row = await SegregationOfDutiesAutomaticClosure.CloseAsync(_scopes, companyId, actorId,
                _correlation, _time, async token =>
                {
                    RequireAccess(companyId, token);
                    if (!string.Equals(actorId, _currentUser.UserId, StringComparison.Ordinal))
                        throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.AccessDenied);
                    // Refresh the Identity owner's caller fence. Once a second user has been observed, a return to
                    // one user does not cancel the closure or restore the earlier permission.
                    await SegregationOfDutiesSingleUserCheck.HasSingleActiveUserAsync(_activeUsers, companyId, actorId, token);
                    RequireAccess(companyId, token);
                }, cancellationToken);
            RequireAccess(companyId, cancellationToken);
        }

        return new SettingsSegregationOfDutiesPolicyV1(companyId, row.PreparerMayApprove, row.Version);
    }

    private async Task<SegregationOfDutiesPolicyVersionRow?> ReadLatestAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        // A scope does not suppress an ambient transaction. Refuse it rather than enlist the owner read/closure
        // in a transaction whose completion the provider cannot control.
        if (System.Transactions.Transaction.Current is not null)
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);

        if (_scopes is null)
        {
            if (_database.Database.CurrentTransaction is not null)
                throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
            return await SegregationOfDutiesPolicyReads.LatestAsync(_database, cancellationToken);
        }

        // Even the first read belongs to the isolated owner: a latest-version read on a caller's Serializable
        // transaction would keep a range lock and block the separate closure insert until this call returns.
        await using var scope = _scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var database = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        if (ReferenceEquals(database, _database) || database.ChangeTracker.Entries().Any()
            || database.Database.CurrentTransaction is not null)
            throw new SettingsPrerequisiteException(SegregationOfDutiesPolicyProblemCodesV1.ProviderUnavailable);
        return await SegregationOfDutiesPolicyReads.LatestAsync(database, cancellationToken);
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
