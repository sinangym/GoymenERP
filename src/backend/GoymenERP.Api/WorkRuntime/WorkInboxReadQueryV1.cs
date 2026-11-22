using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;

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
/// duration, the approval rule version that gave its duration class (<see cref="RuleVersion"/>, WP1) and its explanation
/// card. The decision is given on <see cref="Link"/> with the owner's own command.
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
    string RuleVersion,
    WorkInboxExplanationV1 Explanation);

/// <summary>
/// One source the caller may decide: its duration class and the rule version that gives it (WP1), its state in this answer
/// and whether the owner holds more than shown.
/// </summary>
internal sealed record WorkInboxSourceStateV1(
    string Module,
    string Kind,
    string AwaitingPermission,
    string DurationClass,
    string RuleVersion,
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
/// W1-A / WP1 Work inbox — one box (D5): reads every registered <see cref="IWorkInboxSourceV1"/> (the four native kinds
/// included) the caller may decide. The permission is checked by Work before asking and again by the owner; a company-wide
/// grant sees the whole company, and a site/location/work-center scoped grant sees only the records inside its scope of a
/// source whose owner accepts scoped approvers (D11). Each record is hidden from every user the owner's own rule refuses
/// as its decider (D4: the maker and the owner's further exclusions, YOL-D23: no one else). The duration class comes from
/// the company's versioned approval rule table (WP1; never from the source), its length from Settings (YOL-D18 defaults),
/// business days are counted in the business time zone (D6), and the owner's deterministic explanation is attached. Work
/// records its own measurement events, never reads or writes an owner table and never decides; no model or AI call is made.
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
    private readonly ISettingsWorkHolidayCalendarProviderV1 holidayCalendars;
    private readonly IWorkApprovalInboxMeasurementsV1 measurements;
    private readonly IWorkApprovalRuleReaderV1 rules;
    private readonly IWorkBusinessTimeZoneSourceV1 timeZone;
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
        ISettingsWorkHolidayCalendarProviderV1 holidayCalendars,
        IWorkApprovalInboxMeasurementsV1 measurements,
        IWorkApprovalRuleReaderV1 rules,
        IWorkBusinessTimeZoneSourceV1 timeZone,
        TimeProvider time,
        ILogger<WorkInboxReadQueryV1> logger,
        IEnumerable<IWorkInboxSourceV1> registered)
    {
        this.companyContext = companyContext;
        this.currentUser = currentUser;
        this.correlationContext = correlationContext;
        this.authority = authority;
        this.durations = durations;
        this.holidayCalendars = holidayCalendars;
        this.measurements = measurements;
        this.rules = rules;
        this.timeZone = timeZone;
        this.time = time;
        this.logger = logger;

        var valid = true;
        var byKind = new Dictionary<string, IWorkInboxSourceV1>(StringComparer.Ordinal);
        foreach (var source in registered)
        {
            if (source is null
                || !NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind)
                || !WorkInboxRulesV1.IsValidPermission(source.ApproverPermission)
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

        var resolved = await ReadRulesAsync(context, selected, cancellationToken);
        if (!SameContext(context))
        {
            return new(WorkInboxReadOutcomeV1.Denied, null);
        }
        if (resolved is null)
        {
            // The rule table could not be read: no duration class is assumed, nothing is shown.
            return new(WorkInboxReadOutcomeV1.Unavailable, null);
        }

        SettingsWorkApprovalDurationPolicyV1? policy = null;
        var policyRead = false;
        var now = time.GetUtcNow();
        var visible = new List<(IWorkInboxSourceV1 Source, WorkApprovalRuleResolutionV1 Rule, WorkInboxItemV1 Item)>();
        var states = new List<WorkInboxSourceStateV1>();
        foreach (var source in selected)
        {
            var decision = await MayDecideAsync(source, context, cancellationToken);
            if (!decision.Allowed)
            {
                if (!SameContext(context))
                {
                    return new(WorkInboxReadOutcomeV1.Denied, null);
                }
                continue;
            }

            var rule = resolved[source.Kind];
            if (!rule.ApproverPermissionMatches)
            {
                // The rule was recorded against another approver permission than the owner declares: fail closed.
                states.Add(State(source, rule, SourceUnavailable, false));
                continue;
            }

            var sourceContext = context with { GrantScope = decision.Scope };
            WorkInboxSourceResultV1 result;
            try
            {
                result = await source.ListPendingAsync(sourceContext, NativeApprovalQueueRulesV1.MaxItemsPerSource, cancellationToken);
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
                || Accept(source, sourceContext, result) is not { } accepted)
            {
                states.Add(State(source, rule, SourceUnavailable, false));
                continue;
            }

            // A scoped read sees only part of the company: it never closes another scope's cycle.
            await ObserveAsync(context, source, accepted, !result.HasMore && decision.Scope is null, cancellationToken);
            if (!policyRead)
            {
                policy = await ReadPolicyAsync(context, cancellationToken);
                policyRead = true;
            }
            foreach (var item in accepted)
            {
                if (MayShow(item, context.UserId, decision.Scope))
                {
                    visible.Add((source, rule, item));
                }
            }
            states.Add(State(source, rule, SourceReady, result.HasMore));
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

        WorkInboxHolidaysV1? holidays = null;
        var zone = timeZone.BusinessTimeZone;
        if (policy is { BusinessDays: > 0 } definedPolicy
            && zone is not null
            && visible.Any(entry => entry.Rule.DurationClass == WorkApprovalDurationClassV1.BusinessDays))
        {
            holidays = await ReadHolidaysAsync(context, visible
                .Where(entry => entry.Rule.DurationClass == WorkApprovalDurationClassV1.BusinessDays)
                .Select(entry => WorkInboxDurationCalculatorV1.CandidateYears(entry.Item.WaitingSinceUtc,
                    definedPolicy.BusinessDays!.Value, zone))
                .ToArray(), cancellationToken);
            if (!SameContext(context))
            {
                return new(WorkInboxReadOutcomeV1.Denied, null);
            }
        }
        var items = visible.Select(entry => new WorkInboxPageItemV1(
                entry.Item.Module,
                entry.Item.Kind,
                entry.Item.ItemId,
                entry.Item.DocumentLabel,
                entry.Item.Link,
                entry.Item.WaitingSinceUtc,
                entry.Source.ApproverPermission,
                WorkInboxDurationCalculatorV1.Compute(entry.Rule.DurationClass, policy, entry.Item.WaitingSinceUtc, now, zone, holidays),
                entry.Rule.VersionText,
                new WorkInboxExplanationV1(entry.Item.Rules, entry.Item.References)))
            .ToArray();
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
        var decision = await MayDecideAsync(source, context, cancellationToken);
        if (!decision.Allowed)
        {
            return new(WorkInboxReadOutcomeV1.Denied, null);
        }

        var sourceContext = context with { GrantScope = decision.Scope };
        WorkInboxSourceResultV1 result;
        try
        {
            result = await source.ListPendingAsync(sourceContext, NativeApprovalQueueRulesV1.MaxItemsPerSource, cancellationToken);
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
        if (result is not { Outcome: NativeApprovalQueueOutcomeV1.Found } || Accept(source, sourceContext, result) is not { } accepted)
        {
            return new(WorkInboxReadOutcomeV1.Unavailable, null);
        }

        var waitingSince = request.WaitingSinceUtc.ToUniversalTime();
        var visible = accepted.Any(item => item.ItemId == request.ItemId
            && item.WaitingSinceUtc == waitingSince
            && MayShow(item, context.UserId, decision.Scope));
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

    private readonly record struct Decision(bool Allowed, WorkInboxGrantScopeV1? Scope);

    /// <summary>
    /// A company-wide grant may decide every record (no scope). A site/location/work-center scoped grant (D11) may decide only
    /// the records inside its scope, and only of a source whose owner accepts scoped approvers; otherwise the source is not asked.
    /// </summary>
    private async Task<Decision> MayDecideAsync(
        IWorkInboxSourceV1 source,
        WorkInboxSourceContextV1 context,
        CancellationToken cancellationToken)
    {
        try
        {
            var scope = await authority.RevalidatePermissionScopeAsync(
                source.ApproverPermission, context.CompanyId, cancellationToken);
            if (!SameContext(context) || scope is not { IsGranted: true })
            {
                return default;
            }
            if (scope.IsCompanyWide)
            {
                return new(true, null);
            }
            if (!source.AcceptsScopedApprovers
                || scope.SiteIds.Count + scope.LocationIds.Count + scope.WorkCenterIds.Count == 0)
            {
                return default;
            }
            return new(true, new WorkInboxGrantScopeV1(
                scope.SiteIds.ToHashSet(), scope.LocationIds.ToHashSet(), scope.WorkCenterIds.ToHashSet()));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Work inbox permission check for {Kind} failed.", source.Kind);
            return default;
        }
    }

    /// <summary>
    /// D4 / YOL-D23: an item is shown unless the owner's own rule refuses the caller as its decider (the maker or the
    /// owner's further exclusions); D11: a scoped caller sees only items inside the grant's scope.
    /// </summary>
    internal static bool MayShow(WorkInboxItemV1 item, string userId, WorkInboxGrantScopeV1? grant) =>
        !item.Excludes(userId) && (grant is null || grant.Covers(item.Scope));

    private async Task<IReadOnlyDictionary<string, WorkApprovalRuleResolutionV1>?> ReadRulesAsync(
        WorkInboxSourceContextV1 context,
        IReadOnlyList<IWorkInboxSourceV1> selected,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await rules.ResolveAsync(context.CompanyId, selected, cancellationToken);
            return resolved is not null && selected.All(source => resolved.TryGetValue(source.Kind, out var rule)
                    && string.Equals(rule.Kind, source.Kind, StringComparison.Ordinal)
                    && Enum.IsDefined(rule.DurationClass))
                ? resolved
                : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Work inbox could not read the approval rule table.");
            return null;
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

    private async Task<WorkInboxHolidaysV1?> ReadHolidaysAsync(
        WorkInboxSourceContextV1 context,
        IReadOnlyList<(int FirstYear, int LastYear)> spans,
        CancellationToken cancellationToken)
    {
        var fromYear = Math.Max(spans.Min(span => span.FirstYear), SettingsWorkHolidayCalendarLimitsV1.MinimumYear);
        var toYear = Math.Min(spans.Max(span => span.LastYear), SettingsWorkHolidayCalendarLimitsV1.MaximumYear);
        if (fromYear > toYear)
        {
            return new WorkInboxHolidaysV1(new Dictionary<int, IReadOnlySet<DateOnly>>());
        }
        try
        {
            var calendars = await holidayCalendars.GetWorkHolidayCalendarsAsync(context.CompanyId, fromYear, toYear, cancellationToken);
            if (calendars is null || calendars.CompanyId != context.CompanyId)
            {
                return null;
            }
            var years = new Dictionary<int, IReadOnlySet<DateOnly>>();
            foreach (var year in calendars.DefinedYears)
            {
                if (year is null || year.Year < fromYear || year.Year > toYear || !years.TryAdd(year.Year, year.Days.ToHashSet()))
                {
                    return null;
                }
            }
            return new WorkInboxHolidaysV1(years);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Work inbox could not read the holiday calendar.");
            return null;
        }
    }

    private static WorkInboxSourceStateV1 State(
        IWorkInboxSourceV1 source,
        WorkApprovalRuleResolutionV1 rule,
        string state,
        bool hasMore) =>
        new(source.Module, source.Kind, source.ApproverPermission, rule.DurationClass.ToString(), rule.VersionText, state, hasMore);

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
                || item.PreparedBy is not null && string.IsNullOrWhiteSpace(item.PreparedBy)
                || item.ExcludedDeciders is { } excluded
                    && (excluded.Count > WorkInboxRulesV1.MaxExcludedDecidersPerItem || excluded.Any(string.IsNullOrWhiteSpace))
                || !IsValidScope(source, context, item.Scope)
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

    /// <summary>
    /// A scope names at least one non-empty key. A source asked for a scoped caller must put every item in a scope (an item
    /// without one could never be shown to that caller and means the owner did not filter).
    /// </summary>
    private static bool IsValidScope(IWorkInboxSourceV1 source, WorkInboxSourceContextV1 context, WorkInboxItemScopeV1? scope)
    {
        if (scope is null)
        {
            return context.GrantScope is null;
        }
        return source.AcceptsScopedApprovers
            && (scope.SiteId is not null || scope.LocationId is not null || scope.WorkCenterId is not null)
            && scope.SiteId != Guid.Empty && scope.LocationId != Guid.Empty && scope.WorkCenterId != Guid.Empty;
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
