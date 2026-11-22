using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// WP-A1: the gateway's read port over the versioned AI gateway policy and the append-only usage ledger. Order: cancelled
/// → <see cref="OperationCanceledException"/>; no access, or a company other than the server-resolved one →
/// <c>settings_access_denied</c>; an unreadable store → <c>settings_provider_unavailable</c>; a stored shape the rules
/// would not have written → <c>settings_profile_invalid</c>. No recorded version answers
/// <see cref="SettingsAiGatewayPolicyV1.NotRecorded"/> (everything closed), never an invented default.
/// <para>
/// Reservation: inside one transaction the policy version in force is re-read (it must equal the caller's routed version
/// and carry a ceiling and a per-request reservation), the month row is created on first use, and a single conditional
/// update moves the committed total only while <c>committed + reservation ≤ ceiling</c>; the ledger entry is written in the
/// same unit. Settlement moves the total by <c>used − reserved</c> once (a settled entry is never touched again), so a
/// provider that reports more than it reserved is recorded honestly and closes the month for the next request.
/// </para>
/// </summary>
public sealed class SettingsAiGatewayStoreV1 : ISettingsAiGatewayPolicyProviderV1, ISettingsAiBudgetLedgerV1
{
    private const int ReservedState = 1;
    private const int SettledState = 2;

    private readonly SettingsDbContext _database;
    private readonly ICompanyContext _companyContext;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public SettingsAiGatewayStoreV1(SettingsDbContext database, ICompanyContext companyContext, ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(companyContext);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _companyContext = companyContext;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task<SettingsAiGatewayPolicyV1> GetAiGatewayPolicyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = _currentUser.UserId;
        RequireAccess(companyId, cancellationToken);
        var policy = await Guarded(() => AiGatewayPolicyReader.ReadLatestAsync(_database, companyId, cancellationToken));
        RequireAccess(companyId, cancellationToken);
        if (actor != _currentUser.UserId) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        return policy;
    }

    public async Task<SettingsAiBudgetReservationV1> ReserveAsync(SettingsAiBudgetReservationRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireAccess(request.CompanyId, cancellationToken);
        if (request.PolicyVersion <= 0 || !Enum.IsDefined(request.Feature) || !Enum.IsDefined(request.TaskKind)
            || !Enum.IsDefined(request.Placement) || string.IsNullOrWhiteSpace(request.ModelDeployment)
            || request.ModelDeployment.Length > SettingsAiGatewayLimitsV1.ModelDeploymentMaximumLength
            || string.IsNullOrWhiteSpace(request.ActorId) || request.ActorId.Length > 128
            || string.IsNullOrWhiteSpace(request.CorrelationId) || request.CorrelationId.Length > 128)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.RequestInvalid);

        var now = _timeProvider.GetUtcNow();
        var month = SettingsAiBudgetMonthV1.Of(now);
        try
        {
            await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
            var latest = await AiGatewayPolicyReader.ReadLatestAsync(_database, request.CompanyId, cancellationToken);
            if (latest.Version != request.PolicyVersion)
                return new(SettingsAiBudgetReservationOutcomeV1.PolicyChanged, null, month, 0);
            if (latest.MonthlyUsageCeiling is not { } ceiling || latest.MaxUnitsPerRequest is not { } units)
                return new(SettingsAiBudgetReservationOutcomeV1.BudgetClosed, null, month, 0);

            await EnsureMonthAsync(request.CompanyId, month, cancellationToken);
            var moved = await _database.AiBudgetMonthTotals
                .Where(row => row.CompanyId == request.CompanyId && row.BudgetMonth == month
                    && row.CommittedUnits + units <= ceiling)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CommittedUnits, row => row.CommittedUnits + units),
                    cancellationToken);
            if (moved != 1)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new(SettingsAiBudgetReservationOutcomeV1.CeilingReached, null, month, 0);
            }

            var entryId = Guid.NewGuid();
            _database.AiBudgetLedgerEntries.Add(new AiBudgetLedgerEntryRow
            {
                CompanyId = request.CompanyId,
                EntryId = entryId,
                BudgetMonth = month,
                PolicyVersion = latest.Version,
                Feature = (int)request.Feature,
                TaskKind = (int)request.TaskKind,
                Placement = (int)request.Placement,
                ModelDeployment = request.ModelDeployment,
                ReservedUnits = units,
                UsedUnits = null,
                State = ReservedState,
                ActorId = request.ActorId,
                CorrelationId = request.CorrelationId,
                ReservedAtUtc = now,
            });
            await _database.SaveChangesAsync(cancellationToken);
            RequireAccess(request.CompanyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(SettingsAiBudgetReservationOutcomeV1.Reserved, entryId, month, units);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
        finally
        {
            _database.ChangeTracker.Clear();
        }
    }

    public async Task SettleAsync(Guid companyId, Guid entryId, int usedUnits, CancellationToken cancellationToken = default)
    {
        RequireAccess(companyId, cancellationToken);
        if (entryId == Guid.Empty || usedUnits < 0)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.RequestInvalid);
        try
        {
            await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
            var entry = await _database.AiBudgetLedgerEntries.AsNoTracking()
                .Where(row => row.CompanyId == companyId && row.EntryId == entryId)
                .Select(row => new { row.BudgetMonth, row.ReservedUnits, row.State })
                .SingleOrDefaultAsync(cancellationToken);
            if (entry is null || entry.State != ReservedState)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return;
            }

            var settledAt = _timeProvider.GetUtcNow();
            var settled = await _database.AiBudgetLedgerEntries
                .Where(row => row.CompanyId == companyId && row.EntryId == entryId && row.State == ReservedState)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.State, SettledState)
                    .SetProperty(row => row.UsedUnits, (int?)usedUnits)
                    .SetProperty(row => row.SettledAtUtc, (DateTimeOffset?)settledAt), cancellationToken);
            if (settled != 1)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return;
            }

            long delta = (long)usedUnits - entry.ReservedUnits;
            if (delta != 0)
                await _database.AiBudgetMonthTotals
                    .Where(row => row.CompanyId == companyId && row.BudgetMonth == entry.BudgetMonth)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CommittedUnits, row => row.CommittedUnits + delta),
                        cancellationToken);
            RequireAccess(companyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    public async Task<SettingsAiBudgetMonthUsageV1> GetCurrentMonthUsageAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        RequireAccess(companyId, cancellationToken);
        var month = SettingsAiBudgetMonthV1.Of(_timeProvider.GetUtcNow());
        var usage = await Guarded(() => AiGatewayPolicyReader.ReadMonthAsync(_database, companyId, month, cancellationToken));
        RequireAccess(companyId, cancellationToken);
        return usage;
    }

    private async Task EnsureMonthAsync(Guid companyId, int month, CancellationToken cancellationToken)
    {
        if (await _database.AiBudgetMonthTotals.AsNoTracking()
                .AnyAsync(row => row.CompanyId == companyId && row.BudgetMonth == month, cancellationToken))
            return;
        _database.AiBudgetMonthTotals.Add(new AiBudgetMonthTotalRow { CompanyId = companyId, BudgetMonth = month, CommittedUnits = 0 });
        try
        {
            await _database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            // Another request opened the month first; its row is the fence.
        }
        finally
        {
            _database.ChangeTracker.Clear();
        }
    }

    private static async Task<T> Guarded<T>(Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        }
    }

    private void RequireAccess(Guid companyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !_companyContext.IsResolved || _companyContext.CompanyId == Guid.Empty
            || companyId != _companyContext.CompanyId
            || !_currentUser.CanAccessCompany(_companyContext.CompanyId))
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
    }
}

/// <summary>Reads stored AI gateway rows back, refusing any shape the record command would not have written.</summary>
internal static class AiGatewayPolicyReader
{
    public static async Task<SettingsAiGatewayPolicyV1> ReadLatestAsync(SettingsDbContext database, Guid companyId,
        CancellationToken cancellationToken)
    {
        var latest = await database.AiGatewayPolicyVersions.AsNoTracking()
            .Where(row => row.CompanyId == companyId)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is null) return SettingsAiGatewayPolicyV1.NotRecorded(companyId);
        var features = await database.AiGatewayFeatures.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Version == latest.Version)
            .Select(row => row.Feature).ToArrayAsync(cancellationToken);
        var routes = await database.AiGatewayModelRoutes.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Version == latest.Version)
            .ToArrayAsync(cancellationToken);
        return Build(companyId, latest, features, routes);
    }

    public static SettingsAiGatewayPolicyV1 Build(Guid companyId, AiGatewayPolicyVersionRow version, IReadOnlyCollection<int> features,
        IReadOnlyCollection<AiGatewayModelRouteRow> routes)
    {
        if (version.CompanyId != companyId || version.Version <= 0
            || features.Count != version.FeatureCount || routes.Count != version.RouteCount
            || version.MonthlyUsageCeiling is < 0 or > SettingsAiGatewayLimitsV1.MaximumMonthlyUsageCeiling
            || version.MaxUnitsPerRequest is < 1 or > SettingsAiGatewayLimitsV1.MaximumUnitsPerRequest
            || features.Any(feature => !Enum.IsDefined((AiFeatureV1)feature)) || features.Distinct().Count() != features.Count)
            throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        var readRoutes = new List<SettingsAiModelRouteV1>(routes.Count);
        foreach (var route in routes.OrderBy(row => row.TaskKind).ThenBy(row => row.DataClass))
        {
            var task = (AiTaskKindV1)route.TaskKind;
            var dataClass = (AiDataClassV1)route.DataClass;
            var placement = (AiModelPlacementV1)route.Placement;
            if (route.CompanyId != companyId || route.Version != version.Version || !Enum.IsDefined(task)
                || !AiGatewayPlacementRulesV1.PlacementAllows(placement, dataClass)
                || string.IsNullOrWhiteSpace(route.ModelDeployment)
                || route.ModelDeployment.Length > SettingsAiGatewayLimitsV1.ModelDeploymentMaximumLength)
                throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
            readRoutes.Add(new(task, dataClass, placement, route.ModelDeployment));
        }

        return new(companyId, version.Version, version.MonthlyUsageCeiling, version.MaxUnitsPerRequest,
            features.Select(feature => (AiFeatureV1)feature).ToHashSet(), readRoutes);
    }

    public static async Task<SettingsAiBudgetMonthUsageV1> ReadMonthAsync(SettingsDbContext database, Guid companyId, int month,
        CancellationToken cancellationToken)
    {
        var committed = await database.AiBudgetMonthTotals.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.BudgetMonth == month)
            .Select(row => (long?)row.CommittedUnits).FirstOrDefaultAsync(cancellationToken);
        var count = await database.AiBudgetLedgerEntries.AsNoTracking()
            .CountAsync(row => row.CompanyId == companyId && row.BudgetMonth == month, cancellationToken);
        if (committed is < 0) throw new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProfileInvalid);
        return new(companyId, month, committed ?? 0, count);
    }
}
