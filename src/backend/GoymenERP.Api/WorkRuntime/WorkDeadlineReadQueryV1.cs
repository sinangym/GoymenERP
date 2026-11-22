using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>One source's state in the answer. A denied source is left out entirely (the caller cannot see that it exists).</summary>
public sealed record WorkDeadlineSourceStateV1(string Module, string Kind, string Outcome, bool HasMore);

/// <summary>The "yaklaşan süreler" page: every visible owner's deadlines in the owner's order, sources in module/kind order.</summary>
public sealed record WorkDeadlinePageV1(Guid CompanyId, IReadOnlyList<WorkDeadlineItemV1> Items, IReadOnlyList<WorkDeadlineSourceStateV1> Sources);

public enum WorkDeadlineReadOutcomeV1
{
    Found,
    Denied,
    Unavailable,
}

public sealed record WorkDeadlineReadResultV1(WorkDeadlineReadOutcomeV1 Outcome, WorkDeadlinePageV1? Resource);

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): Work's read-only "yaklaşan süreler" view. Work owns no deadline: it asks each registered
/// <see cref="IWorkDeadlineSourceV1"/> (each applies its own read permission, computes its own dates and writes nothing) and
/// lays the answers side by side. A source that fails is reported as <c>unavailable</c>, never dropped silently; a source the
/// caller may not see is left out. Work adds no permission and computes no date.
/// </summary>
public sealed class WorkDeadlineReadQueryV1(
    IEnumerable<IWorkDeadlineSourceV1> sources,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext)
{
    /// <summary>Page bound per source (a page size, not a policy).</summary>
    public const int MaximumItemsPerSource = 50;

    public const string OutcomeFound = "found";
    public const string OutcomeUnavailable = "unavailable";

    public async Task<WorkDeadlineReadResultV1> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.IsAuthenticated
            || string.IsNullOrWhiteSpace(currentUser.UserId) || !currentUser.CanAccessCompany(companyContext.CompanyId))
            return new(WorkDeadlineReadOutcomeV1.Denied, null);
        if (!correlationContext.IsResolved || string.IsNullOrWhiteSpace(correlationContext.CorrelationId))
            return new(WorkDeadlineReadOutcomeV1.Unavailable, null);
        var context = new WorkDeadlineSourceContextV1(companyContext.CompanyId, currentUser.UserId, correlationContext.CorrelationId);
        var items = new List<WorkDeadlineItemV1>();
        var states = new List<WorkDeadlineSourceStateV1>();
        foreach (var source in sources.OrderBy(source => source.Module, StringComparer.Ordinal).ThenBy(source => source.Kind, StringComparer.Ordinal))
        {
            WorkDeadlineSourceResultV1 result;
            try { result = await source.ListUpcomingAsync(context, MaximumItemsPerSource, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { result = WorkDeadlineSourceResultV1.Unavailable(); }
            switch (result.Outcome)
            {
                case WorkDeadlineSourceOutcomeV1.Denied:
                    continue;
                case WorkDeadlineSourceOutcomeV1.Found when result.Items.Count <= MaximumItemsPerSource
                    && result.Items.All(item => Valid(item, source)):
                    items.AddRange(result.Items);
                    states.Add(new(source.Module, source.Kind, OutcomeFound, result.HasMore));
                    break;
                default:
                    states.Add(new(source.Module, source.Kind, OutcomeUnavailable, false));
                    break;
            }
        }

        if (companyContext.CompanyId != context.CompanyId || currentUser.UserId != context.UserId)
            return new(WorkDeadlineReadOutcomeV1.Denied, null);
        return new(WorkDeadlineReadOutcomeV1.Found, new(context.CompanyId, items, states));
    }

    /// <summary>An item must be the source's own and internally consistent; otherwise the whole source is unavailable.</summary>
    private static bool Valid(WorkDeadlineItemV1 item, IWorkDeadlineSourceV1 source) =>
        item is not null && item.Module == source.Module && item.Kind == source.Kind
        && !string.IsNullOrWhiteSpace(item.Code) && item.Code.StartsWith(source.Module + ".", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(item.Link) && item.Link.StartsWith('/')
        && (item.State == WorkDeadlineStatesV1.Defined
            ? item.DueDate is { } due && item.DaysRemaining is >= 0 && !string.IsNullOrWhiteSpace(item.DefinitionVersion)
                && item.OriginalDueDate is { } original && WorkDeadlineAdjustmentsV1.IsKnown(item.DueDateAdjustment)
                && (item.DueDateAdjustment == WorkDeadlineAdjustmentsV1.MovedToBusinessDay ? due > original : due == original)
            : item.State == WorkDeadlineStatesV1.Undefined && item.DueDate is null && item.DaysRemaining is null
                && item.PeriodStart is null && item.PeriodEnd is null && item.DefinitionVersion is null
                && item.OriginalDueDate is null && item.DueDateAdjustment is null);
}
