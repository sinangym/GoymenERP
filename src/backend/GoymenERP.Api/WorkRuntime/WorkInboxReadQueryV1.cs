using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>The inbox views (W1-A delivers the first of four: "benden karar bekleyen").</summary>
internal static class WorkInboxViewsV1
{
    public const string AwaitingMyDecision = "awaiting-my-decision";
}

/// <summary>The modelless explanation card of one item (AI-D32): the owner's rule results and the records it rests on.</summary>
internal sealed record WorkInboxExplanationV1(
    IReadOnlyList<WorkApprovalRuleResultV1> Rules,
    IReadOnlyList<WorkApprovalReferenceV1> References);

/// <summary>
/// One inbox item: kind, owner record and its screen, the permission it waits on ("kimde bekliyor"), since when, its
/// duration and its explanation card. The decision is given on <see cref="Link"/> with the owner's own command.
/// </summary>
internal sealed record WorkInboxPageItemV1(
    string Module,
    string Kind,
    Guid ItemId,
    string DocumentLabel,
    string Link,
    DateTimeOffset WaitingSinceUtc,
    string AwaitingPermission,
    WorkInboxDurationV1 Duration,
    WorkInboxExplanationV1 Explanation);

/// <summary>One source the caller may decide: its state in this answer and whether the owner holds more than shown.</summary>
internal sealed record WorkInboxSourceStateV1(
    string Module,
    string Kind,
    string AwaitingPermission,
    string DurationClass,
    string State,
    bool HasMore);

internal sealed record WorkInboxPageV1(
    Guid CompanyId,
    string View,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<WorkInboxPageItemV1> Items,
    IReadOnlyList<WorkInboxSourceStateV1> Sources);

internal enum WorkInboxReadOutcomeV1
{
    Found,
    Invalid,
    NotFound,
    Denied,
    Unavailable,
}

internal sealed record WorkInboxReadResultV1<T>(WorkInboxReadOutcomeV1 Outcome, T? Resource) where T : class;

/// <summary>The request body of <c>POST /api/v1/work/inbox/events</c>.</summary>
internal sealed record RecordWorkInboxEventRequestV1(
    Guid ClientEventId,
    string Kind,
    Guid ItemId,
    DateTimeOffset WaitingSinceUtc,
    string EventType);

internal sealed record WorkInboxEventRecordedV1(Guid ClientEventId, bool Replayed);

/// <summary>
/// W1-A Work inbox: reads every registered <see cref="IWorkInboxSourceV1"/> the caller may decide (permission checked
/// by Work before asking and again by the owner), hides each record from its own maker (maker ≠ checker), attaches the
/// Settings duration and the owner's deterministic explanation, and records Work's own measurement events. Work never
/// reads or writes an owner table and never decides; no model or AI call is made.
/// </summary>
internal sealed class WorkInboxReadQueryV1
{
    public const string SourceReady = "Ready";
    public const string SourceUnavailable = "Unavailable";

    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly ICorrelationContext correlationContext;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly ISettingsWorkApprovalDurationPolicyProviderV1 durations;
    private readonly IWorkApprovalInboxMeasurementsV1 measurements;
    private readonly TimeProvider time;
    private readonly ILogger<WorkInboxReadQueryV1> logger;
    private readonly IReadOnlyList<IWorkInboxSourceV1> sources;
    private readonly bool registrationIsValid;

    public WorkInboxReadQueryV1(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        ICorrelationContext correlationContext,
        IErpAuthorityRevalidationService authority,
        ISettingsWorkApprovalDurationPolicyProviderV1 durations,
        IWorkApprovalInboxMeasurementsV1 measurements,
        TimeProvider time,
        ILogger<WorkInboxReadQueryV1> logger,
        IEnumerable<IWorkInboxSourceV1> registered)
    {
        this.companyContext = companyContext;
        this.currentUser = currentUser;
        this.correlationContext = correlationContext;
        this.authority = authority;
        this.durations = durations;
        this.measurements = measurements;
        this.time = time;
        this.logger = logger;

        var valid = true;
        var byKind = new Dictionary<string, IWorkInboxSourceV1>(StringComparer.Ordinal);
        foreach (var source in registered)
        {
            if (source is null
                || !NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind)
                || NativeApprovalKindsV1.IsKnown(source.Kind)
                || !WorkInboxRulesV1.IsValidPermission(source.ApproverPermission)
                || !Enum.IsDefined(source.DurationClass)
                || !byKind.TryAdd(source.Kind, source))
            {
                valid = false;
            }
        }
        sources = byKind.Values
            .OrderBy(source => source.Module, StringComparer.Ordinal)
            .ThenBy(source => source.Kind, StringComparer.Ordinal)
            .ToArray();
        registrationIsValid = valid;
    }

    public async Task<WorkInboxReadResultV1<WorkInboxPageV1>> ListAsync(
        string? view,
        string? kind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (view is not null && !string.Equals(view, WorkInboxViewsV1.AwaitingMyDecision, StringComparison.Ordinal))
        {
            return new(WorkInboxReadOutcomeV1.Invalid, null);
        }
        if (!TryCapture(out var context, out var refusal))
        {
            return new(refusal, null);
        }
        if (!registrationIsValid)
        {
            return new(WorkInboxReadOutcomeV1.Unavailable, null);
        }

        var selected = sources;
        if (kind is not null)
        {
            selected = sources.Where(source => string.Equals(source.Kind, kind, StringComparison.Ordinal)).ToArray();
            if (selected.Count == 0)
            {
                return new(WorkInboxReadOutcomeV1.Invalid, null);
            }
        }

        SettingsWorkApprovalDurationPolicyV1? policy = null;
        var policyRead = false;
        var now = time.GetUtcNow();
        var items = new List<WorkInboxPageItemV1>();
        var states = new List<WorkInboxSourceStateV1>();
        foreach (var source in selected)
        {
            if (!await MayDecideAsync(source, context, cancellationToken))
            {
                if (!SameContext(context))
                {
                    return new(WorkInboxReadOutcomeV1.Denied, null);
                }
                continue;
            }

            WorkInboxSourceResultV1 result;
            try
            {
                result = await source.ListPendingAsync(context, NativeApprovalQueueRulesV1.MaxItemsPerSource, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Work inbox source {Kind} failed.", source.Kind);
                result = WorkInboxSourceResultV1.Unavailable();
            }
            if (!SameContext(context))
            {
                return new(WorkInboxReadOutcomeV1.Denied, null);
            }

            if (result is { Outcome: NativeApprovalQueueOutcomeV1.Denied, Items: null })
            {
                continue;
            }
            if (result is not { Outcome: NativeApprovalQueueOutcomeV1.Found }
                || Accept(source, context, result) is not { } accepted)
            {
                states.Add(State(source, SourceUnavailable, false));
                continue;
            }

            await ObserveAsync(context, source, accepted, !result.HasMore, cancellationToken);
            if (!policyRead)
            {
                policy = await ReadPolicyAsync(context, cancellationToken);
                policyRead = true;
            }
            foreach (var item in accepted)
            {
                // Maker ≠ checker also in the list: a record never waits on its own maker's decision.
                if (string.Equals(item.PreparedBy, context.UserId, StringComparison.Ordinal))
                {
                    continue;
                }
                items.Add(new WorkInboxPageItemV1(
                    item.Module,
                    item.Kind,
                    item.ItemId,
                    item.DocumentLabel,
                    item.Link,
                    item.WaitingSinceUtc,
                    source.ApproverPermission,
                    WorkInboxDurationCalculatorV1.Compute(source.DurationClass, policy, item.WaitingSinceUtc, now),
                    new WorkInboxExplanationV1(item.Rules, item.References)));
            }
            states.Add(State(source, SourceReady, result.HasMore));
        }
        if (!SameContext(context))
        {
            return new(WorkInboxReadOutcomeV1.Denied, null);
        }
        if (selected.Count > 0 && states.Count == 0)
        {
            return new(WorkInboxReadOutcomeV1.Denied, null);
        }
        if (states.Count > 0 && states.All(state => state.State == SourceUnavailable))
        {
            return new(WorkInboxReadOutcomeV1.Unavailable, null);
        }

        var ordered = items
            .OrderBy(item => item.WaitingSinceUtc)
            .ThenBy(item => item.Module, StringComparer.Ordinal)
            .ThenBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.ItemId)
            .ToArray();
        return new(WorkInboxReadOutcomeV1.Found,
            new WorkInboxPageV1(context.CompanyId, WorkInboxViewsV1.AwaitingMyDecision, now, ordered, states));
    }

    /// <summary>
    /// Records that the caller opened an item card or followed the owner link. The item must be one the caller may decide
    /// right now (pending, not the caller's own); a retry with the same <c>clientEventId</c> is a replay.
    /// </summary>
    public async Task<WorkInboxReadResultV1<WorkInboxEventRecordedV1>> RecordEventAsync(
        RecordWorkInboxEventRequestV1? request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null
            || request.ClientEventId == Guid.Empty
            || request.ItemId == Guid.Empty
            || request.WaitingSinceUtc == default
            || string.IsNullOrWhiteSpace(request.Kind)
            || !TryEventType(request.EventType, out var eventType))
        {
            return new(WorkInboxReadOutcomeV1.Invalid, null);
        }
        if (!TryCapture(out var context, out var refusal))
        {
            return new(refusal, null);
        }
        if (!registrationIsValid)
        {
            return new(WorkInboxReadOutcomeV1.Unavailable, null);
        }
        var source = sources.FirstOrDefault(candidate => string.Equals(candidate.Kind, request.Kind, StringComparison.Ordinal));
        if (source is null)
        {
            return new(WorkInboxReadOutcomeV1.Invalid, null);
        }
        if (!await MayDecideAsync(source, context, cancellationToken))
        {
            return new(WorkInboxReadOutcomeV1.Denied, null);
        }

        WorkInboxSourceResultV1 result;
        try
        {
            result = await source.ListPendingAsync(context, NativeApprovalQueueRulesV1.MaxItemsPerSource, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Work inbox source {Kind} failed.", source.Kind);
            return new(WorkInboxReadOutcomeV1.Unavailable, null);
        }
        if (!SameContext(context))
        {
            return new(WorkInboxReadOutcomeV1.Denied, null);
        }
        if (result is { Outcome: NativeApprovalQueueOutcomeV1.Denied })
        {
            return new(WorkInboxReadOutcomeV1.Denied, null);
        }
        if (result is not { Outcome: NativeApprovalQueueOutcomeV1.Found } || Accept(source, context, result) is not { } accepted)
        {
            return new(WorkInboxReadOutcomeV1.Unavailable, null);
        }

        var waitingSince = request.WaitingSinceUtc.ToUniversalTime();
        var visible = accepted.Any(item => item.ItemId == request.ItemId
            && item.WaitingSinceUtc == waitingSince
            && !string.Equals(item.PreparedBy, context.UserId, StringComparison.Ordinal));
        if (!visible)
        {
            return new(WorkInboxReadOutcomeV1.NotFound, null);
        }

        var outcome = await measurements.RecordInteractionAsync(
            context.CompanyId,
            new WorkApprovalInboxInteractionV1(request.ClientEventId, source.Kind, request.ItemId, waitingSince, eventType),
            cancellationToken);
        if (!SameContext(context))
        {
            return new(WorkInboxReadOutcomeV1.Denied, null);
        }
        return outcome switch
        {
            WorkApprovalInboxInteractionOutcomeV1.Recorded => new(WorkInboxReadOutcomeV1.Found, new(request.ClientEventId, false)),
            WorkApprovalInboxInteractionOutcomeV1.Replayed => new(WorkInboxReadOutcomeV1.Found, new(request.ClientEventId, true)),
            _ => new(WorkInboxReadOutcomeV1.Invalid, null),
        };
    }

    internal static bool TryEventType(string? value, out WorkApprovalInboxEventTypeV1 eventType)
    {
        switch (value)
        {
            case nameof(WorkApprovalInboxEventTypeV1.Opened):
                eventType = WorkApprovalInboxEventTypeV1.Opened;
                return true;
            case nameof(WorkApprovalInboxEventTypeV1.OwnerScreenOpened):
                eventType = WorkApprovalInboxEventTypeV1.OwnerScreenOpened;
                return true;
            default:
                eventType = default;
                return false;
        }
    }

    private async Task<bool> MayDecideAsync(
        IWorkInboxSourceV1 source,
        WorkInboxSourceContextV1 context,
        CancellationToken cancellationToken)
    {
        try
        {
            var scope = await authority.RevalidatePermissionScopeAsync(
                source.ApproverPermission, context.CompanyId, cancellationToken);
            return scope is { IsGranted: true, IsCompanyWide: true } && SameContext(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Work inbox permission check for {Kind} failed.", source.Kind);
            return false;
        }
    }

    private async Task ObserveAsync(
        WorkInboxSourceContextV1 context,
        IWorkInboxSourceV1 source,
        IReadOnlyList<WorkInboxItemV1> items,
        bool complete,
        CancellationToken cancellationToken)
    {
        try
        {
            await measurements.ObserveAsync(
                context.CompanyId,
                source.Kind,
                items.Select(item => new WorkApprovalInboxCycleV1(item.ItemId, item.WaitingSinceUtc)).ToArray(),
                complete,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Measurement never blocks the inbox; a missed observation is caught by the next complete read.
            logger.LogWarning(exception, "Work inbox measurement for {Kind} was not recorded.", source.Kind);
        }
    }

    private async Task<SettingsWorkApprovalDurationPolicyV1?> ReadPolicyAsync(
        WorkInboxSourceContextV1 context,
        CancellationToken cancellationToken)
    {
        try
        {
            var policy = await durations.GetWorkApprovalDurationAsync(context.CompanyId, cancellationToken);
            return policy is not null && policy.CompanyId == context.CompanyId ? policy : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Work inbox could not read the approval duration policy.");
            return null;
        }
    }

    private static WorkInboxSourceStateV1 State(IWorkInboxSourceV1 source, string state, bool hasMore) =>
        new(source.Module, source.Kind, source.ApproverPermission, source.DurationClass.ToString(), state, hasMore);

    internal static IReadOnlyList<WorkInboxItemV1>? Accept(
        IWorkInboxSourceV1 source,
        WorkInboxSourceContextV1 context,
        WorkInboxSourceResultV1 result)
    {
        if (result.CompanyId != context.CompanyId
            || result.Items is not { } items
            || items.Count > NativeApprovalQueueRulesV1.MaxItemsPerSource)
        {
            return null;
        }

        var seen = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item is null
                || !string.Equals(item.Module, source.Module, StringComparison.Ordinal)
                || !string.Equals(item.Kind, source.Kind, StringComparison.Ordinal)
                || item.ItemId == Guid.Empty
                || !seen.Add(item.ItemId)
                || string.IsNullOrWhiteSpace(item.DocumentLabel)
                || item.DocumentLabel.Length > WorkInboxRulesV1.MaxDocumentLabelLength
                || item.WaitingSinceUtc == default
                || item.WaitingSinceUtc.Offset != TimeSpan.Zero
                || !NativeApprovalQueueRulesV1.IsValidLink(item.Link)
                || string.IsNullOrWhiteSpace(item.PreparedBy)
                || item.Rules is not { Count: > 0 and <= WorkInboxRulesV1.MaxRulesPerItem }
                || item.Rules.Any(rule => !WorkInboxRulesV1.IsValidRule(source.Module, rule))
                || item.References is not { Count: <= WorkInboxRulesV1.MaxReferencesPerItem }
                || item.References.Any(reference => !WorkInboxRulesV1.IsValidReference(reference)))
            {
                return null;
            }
        }
        return items;
    }

    private bool TryCapture(out WorkInboxSourceContextV1 context, out WorkInboxReadOutcomeV1 refusal)
    {
        context = null!;
        if (!companyContext.IsResolved
            || companyContext.CompanyId == Guid.Empty
            || !currentUser.IsAuthenticated
            || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            refusal = WorkInboxReadOutcomeV1.Denied;
            return false;
        }
        if (!correlationContext.IsResolved || string.IsNullOrWhiteSpace(correlationContext.CorrelationId))
        {
            refusal = WorkInboxReadOutcomeV1.Unavailable;
            return false;
        }

        context = new WorkInboxSourceContextV1(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId);
        refusal = default;
        return true;
    }

    private bool SameContext(WorkInboxSourceContextV1 captured) =>
        companyContext.IsResolved
        && companyContext.CompanyId == captured.CompanyId
        && captured.CompanyId != Guid.Empty
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, captured.UserId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(captured.CompanyId)
        && correlationContext.IsResolved
        && string.Equals(correlationContext.CorrelationId, captured.CorrelationId, StringComparison.Ordinal)
        && string.Equals(correlationContext.CausationId, captured.CausationId, StringComparison.Ordinal);
}
