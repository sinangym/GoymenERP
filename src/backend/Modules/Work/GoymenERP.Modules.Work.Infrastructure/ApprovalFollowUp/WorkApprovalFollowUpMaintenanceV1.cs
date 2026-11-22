using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoymenERP.Modules.Work.Infrastructure.ApprovalFollowUp;

/// <summary>
/// WP2 — one follow-up cycle for the server-resolved company (system work; the scheduler runs it only when
/// <c>Work:CollaborationScheduler:ApprovalFollowUpEnabled</c> is true, off by default). For every approval source that is a
/// <see cref="IWorkInboxFollowUpSourceV1"/>:
/// <list type="number">
/// <item>D10: each measured cycle the owner no longer lists is asked for the owner's own decision; a decided cycle gets a
/// <c>Decided</c> measurement event at the owner's time. Then the pending list is observed (arrivals and, on a complete list,
/// observed resolutions) — the measurement no longer waits for someone to open the inbox.</item>
/// <item>AI-D29: a newly waiting cycle tells its eligible approvers "şimdi" — holders of the owner's approver permission now
/// (company-wide, or scoped when the owner accepts scoped approvers and the grant covers the record, D11), minus the owner's
/// maker and further exclusions (D4, YOL-D23).</item>
/// <item>YOL-D17 / ROL-D3: a cycle without an eligible approver is a "sahipsiz onay": the holders of the rule's escalation
/// permission are told and the follow-up view lists it.</item>
/// <item>YOL-D17: once the due time passes (rule table class, Settings length, business days with the holiday list, D6 zone)
/// the eligible approvers get the one reminder.</item>
/// <item>YOL-D28: one more class length later (ASSUMPTION-WP2-1) the cycle is escalated to the holders of the rule's
/// escalation permission (<c>work.approvals.escalate</c> by default); while no one holds it nothing escalates and only the
/// reminder went out.</item>
/// </list>
/// Every step is one append-only follow-up row per cycle and step (unique), and one notification per recipient. Nothing is
/// approved or rejected; no owner table is read or written; any owner failure leaves the step for the next cycle.
/// </summary>
public sealed class WorkApprovalFollowUpMaintenanceV1(
    WorkDbContext database,
    ICompanyContext company,
    IEnumerable<IWorkInboxSourceV1> sources,
    IWorkApprovalRuleReaderV1 rules,
    IWorkApprovalInboxMeasurementsV1 measurements,
    IWorkApprovalTimingReaderV1 timings,
    IWorkPermissionHolderDirectoryV1 holders,
    IWorkBusinessTimeZoneSourceV1 timeZone,
    TimeProvider time,
    ILogger<WorkApprovalFollowUpMaintenanceV1> logger) : IWorkApprovalFollowUpMaintenanceV1
{
    public async Task<WorkApprovalFollowUpCycleV1> RunCycleAsync(CancellationToken cancellationToken = default)
    {
        var companyId = company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId
            : throw new InvalidOperationException("A resolved company is required for the approval follow-up cycle.");
        var followed = sources.OfType<IWorkInboxFollowUpSourceV1>()
            .Where(source => NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind)
                && WorkInboxRulesV1.IsValidPermission(source.ApproverPermission))
            .GroupBy(source => source.Kind, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(source => source.Kind, StringComparer.Ordinal)
            .ToArray();
        var counts = new Counts();
        if (followed.Length == 0)
        {
            return counts.Result();
        }

        var resolved = await rules.ResolveAsync(companyId, followed, cancellationToken);
        RequireCompany(companyId);
        if (resolved is null)
        {
            counts.Unavailable += followed.Length;
            return counts.Result();
        }

        var now = time.GetUtcNow();
        var zone = timeZone.BusinessTimeZone;
        var localYear = zone is null ? now.Year : TimeZoneInfo.ConvertTime(now, zone).Year;
        // The oldest waiting record can be far back; the business-day count crosses at most the next year of its start.
        var timing = await ReadTimingAsync(companyId, localYear - 1, localYear + 1, cancellationToken);
        var correlationId = "work-approval-follow-up-" + Guid.NewGuid().ToString("N");
        var holderCache = new Dictionary<string, WorkPermissionHoldersV1>(StringComparer.Ordinal);
        foreach (var source in followed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!resolved.TryGetValue(source.Kind, out var rule) || !rule.ApproverPermissionMatches)
            {
                counts.Unavailable++;
                continue;
            }

            try
            {
                if (await FollowAsync(companyId, source, rule, timing, zone, now, correlationId, holderCache, counts, cancellationToken))
                {
                    counts.Followed++;
                }
                else
                {
                    counts.Unavailable++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                counts.Unavailable++;
                logger.LogWarning(exception, "Approval follow-up for {Kind} failed; it will be retried.", source.Kind);
            }
            finally
            {
                database.ChangeTracker.Clear();
            }
        }

        return counts.Result();
    }

    private async Task<bool> FollowAsync(
        Guid companyId,
        IWorkInboxFollowUpSourceV1 source,
        WorkApprovalRuleResolutionV1 rule,
        WorkApprovalTimingV1? timing,
        TimeZoneInfo? zone,
        DateTimeOffset now,
        string correlationId,
        Dictionary<string, WorkPermissionHoldersV1> holderCache,
        Counts counts,
        CancellationToken cancellationToken)
    {
        var result = await source.ListPendingForFollowUpAsync(companyId, NativeApprovalQueueRulesV1.MaxItemsPerSource, cancellationToken);
        RequireCompany(companyId);
        if (result is not { Outcome: NativeApprovalQueueOutcomeV1.Found } || Accept(source, companyId, result) is not { } pending)
        {
            return false;
        }

        // D10: the owner's exact decision of every measured cycle it no longer lists, before the observation closes it.
        var listed = pending.Select(item => (item.ItemId, item.WaitingSinceUtc)).ToHashSet();
        var open = await measurements.ListOpenCyclesAsync(companyId, source.Kind, cancellationToken);
        RequireCompany(companyId);
        foreach (var cycle in open.Where(cycle => !listed.Contains((cycle.ItemId, cycle.WaitingSinceUtc))))
        {
            var answer = await source.ReadDecisionAsync(companyId, cycle.ItemId, cycle.WaitingSinceUtc, cancellationToken);
            RequireCompany(companyId);
            if (answer is { Outcome: WorkInboxDecisionOutcomeV1.Decided, DecidedAtUtc: { } decidedAt }
                && WorkInboxDecisionsV1.IsValid(answer, cycle.WaitingSinceUtc)
                && await measurements.RecordDecisionAsync(companyId, source.Kind, cycle, decidedAt, cancellationToken))
            {
                counts.Decisions++;
            }
        }

        await measurements.ObserveAsync(companyId, source.Kind,
            pending.Select(item => new WorkApprovalInboxCycleV1(item.ItemId, item.WaitingSinceUtc)).ToArray(),
            !result.HasMore, cancellationToken);
        RequireCompany(companyId);
        if (pending.Count == 0)
        {
            return true;
        }

        var approvers = await HoldersAsync(companyId, source.ApproverPermission, holderCache, cancellationToken);
        if (!approvers.Available)
        {
            return false;
        }

        var escalation = rule.EscalationPermission is { } escalationPermission
            ? await HoldersAsync(companyId, escalationPermission, holderCache, cancellationToken)
            : new WorkPermissionHoldersV1(true, []);
        var itemIds = pending.Select(item => item.ItemId).ToArray();
        var done = await database.ApprovalFollowUps.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Kind == source.Kind && itemIds.Contains(row.ItemId))
            .Select(row => new { row.ItemId, row.CycleStartedAtUtc, row.Step })
            .ToListAsync(cancellationToken);
        RequireCompany(companyId);
        var steps = done.GroupBy(row => (row.ItemId, row.CycleStartedAtUtc))
            .ToDictionary(group => group.Key, group => group.Select(row => row.Step).ToHashSet());

        var added = false;
        foreach (var item in pending.Where(item => item.WaitingSinceUtc <= now))
        {
            var taken = steps.TryGetValue((item.ItemId, item.WaitingSinceUtc), out var existing) ? existing : [];
            var eligible = approvers.Holders.Where(holder => MayDecide(source, holder, item)).Select(holder => holder.UserId)
                .Distinct().Order().ToArray();
            var due = WorkApprovalDueCalculatorV1.Due(rule.DurationClass, timing, item.WaitingSinceUtc, zone);
            if (eligible.Length > 0 && !taken.Contains((int)WorkApprovalFollowUpStepV1.Waiting))
            {
                Step(companyId, source, rule, item, WorkApprovalFollowUpStepV1.Waiting, due, eligible, now, correlationId, counts);
                counts.Waiting++;
                added = true;
            }

            if (eligible.Length == 0 && !taken.Contains((int)WorkApprovalFollowUpStepV1.Orphaned))
            {
                if (!escalation.Available)
                {
                    continue;
                }

                // ROL-D3 / YOL-D17: no one may decide it: it is visible as "sahipsiz onay" and the escalation holders are told.
                Step(companyId, source, rule, item, WorkApprovalFollowUpStepV1.Orphaned, due,
                    EscalationRecipients(escalation, item), now, correlationId, counts);
                counts.Orphaned++;
                added = true;
            }

            if (due is not { } dueAt || now <= dueAt.Due || eligible.Length == 0)
            {
                continue;
            }

            var reminded = taken.Contains((int)WorkApprovalFollowUpStepV1.Reminder);
            if (!reminded)
            {
                Step(companyId, source, rule, item, WorkApprovalFollowUpStepV1.Reminder, due, eligible, now, correlationId, counts);
                counts.Reminders++;
                added = true;
                reminded = true;
            }

            if (!reminded || taken.Contains((int)WorkApprovalFollowUpStepV1.Escalated) || rule.EscalationPermission is null
                || !escalation.Available)
            {
                continue;
            }

            var escalationDue = WorkApprovalDueCalculatorV1.EscalationDue(rule.DurationClass, dueAt.Length, dueAt.Due, zone,
                timing!.HolidayYears);
            var recipients = EscalationRecipients(escalation, item);
            // YOL-D28: while no one holds the escalation permission, only the reminder went to the approvers.
            if (escalationDue is { } escalateAt && now > escalateAt && recipients.Length > 0)
            {
                Step(companyId, source, rule, item, WorkApprovalFollowUpStepV1.Escalated, due, recipients, now, correlationId, counts);
                counts.Escalations++;
                added = true;
            }
        }

        if (!added)
        {
            return true;
        }

        await using var transaction = database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        RequireCompany(companyId);
        await database.SaveChangesAsync(cancellationToken);
        RequireCompany(companyId);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return true;
    }

    private void Step(
        Guid companyId,
        IWorkInboxFollowUpSourceV1 source,
        WorkApprovalRuleResolutionV1 rule,
        WorkInboxItemV1 item,
        WorkApprovalFollowUpStepV1 step,
        (DateTimeOffset Due, int Length, string Version)? due,
        IReadOnlyList<Guid> recipients,
        DateTimeOffset now,
        string correlationId,
        Counts counts)
    {
        var id = Guid.NewGuid();
        database.ApprovalFollowUps.Add(new WorkApprovalFollowUpRow
        {
            CompanyId = companyId, Id = id, Kind = source.Kind, ItemId = item.ItemId, CycleStartedAtUtc = item.WaitingSinceUtc,
            Step = (int)step, DocumentLabel = item.DocumentLabel, Link = item.Link, ApproverPermission = source.ApproverPermission,
            EscalationPermission = rule.EscalationPermission, RuleVersion = rule.VersionText, PolicyVersion = due?.Version,
            DueAtUtc = due?.Due, RecipientCount = recipients.Count, RecordedAtUtc = now, CorrelationId = correlationId,
        });
        var kind = WorkApprovalFollowUpStepsV1.NotificationKind(step);
        foreach (var recipient in recipients)
        {
            database.InboxNotifications.Add(new WorkInboxNotificationRow
            {
                CompanyId = companyId, Id = Guid.NewGuid(), RecipientUserId = recipient, Kind = (int)kind,
                Class = (int)WorkInboxNotificationClassesV1.Of(kind), SourceId = id, RecordedAtUtc = now, CorrelationId = correlationId,
            });
            counts.Notifications++;
        }
    }

    /// <summary>The escalation holders, minus everyone the owner refuses as the record's decider (the maker included).</summary>
    private static Guid[] EscalationRecipients(WorkPermissionHoldersV1 escalation, WorkInboxItemV1 item) =>
        escalation.Holders.Where(holder => holder.IsCompanyWide && !item.Excludes(holder.UserId.ToString("D")))
            .Select(holder => holder.UserId).Distinct().Order().ToArray();

    /// <summary>
    /// The inbox's own rule (D4, D11, YOL-D23): the holder is not refused by the owner and holds the approver permission
    /// company-wide, or scoped where the owner accepts scoped approvers and the grant covers the record.
    /// </summary>
    internal static bool MayDecide(IWorkInboxSourceV1 source, WorkPermissionHolderV1 holder, WorkInboxItemV1 item) =>
        holder.UserId != Guid.Empty
        && !item.Excludes(holder.UserId.ToString("D"))
        && (holder.IsCompanyWide
            || source.AcceptsScopedApprovers
            && new WorkInboxGrantScopeV1(holder.SiteIds, holder.LocationIds, holder.WorkCenterIds).Covers(item.Scope));

    private async Task<WorkPermissionHoldersV1> HoldersAsync(Guid companyId, string permission,
        Dictionary<string, WorkPermissionHoldersV1> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(permission, out var known))
        {
            return known;
        }

        WorkPermissionHoldersV1 answer;
        try
        {
            answer = await holders.ListHoldersAsync(companyId, permission, cancellationToken) ?? WorkPermissionHoldersV1.Unavailable;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Permission holders of {Permission} could not be read.", permission);
            answer = WorkPermissionHoldersV1.Unavailable;
        }

        RequireCompany(companyId);
        if (answer.Available && (answer.Holders is null || answer.Holders.Any(holder => holder is null || holder.UserId == Guid.Empty)))
        {
            answer = WorkPermissionHoldersV1.Unavailable;
        }

        cache[permission] = answer;
        return answer;
    }

    private async Task<WorkApprovalTimingV1?> ReadTimingAsync(Guid companyId, int fromYear, int toYear,
        CancellationToken cancellationToken)
    {
        try
        {
            var timing = await timings.ReadAsync(companyId, fromYear, toYear, cancellationToken);
            RequireCompany(companyId);
            return timing is not null && timing.CompanyId == companyId && timing.HolidayYears is not null ? timing : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Approval timing could not be read; no reminder or escalation this cycle.");
            return null;
        }
    }

    /// <summary>The inbox's shape rules for an owner answer; anything else is unavailable (never a partial list).</summary>
    internal static IReadOnlyList<WorkInboxItemV1>? Accept(IWorkInboxSourceV1 source, Guid companyId, WorkInboxSourceResultV1 result)
    {
        if (result.CompanyId != companyId || result.Items is not { } items || items.Count > NativeApprovalQueueRulesV1.MaxItemsPerSource)
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
                || item.Scope is not null && !source.AcceptsScopedApprovers)
            {
                return null;
            }
        }

        return items;
    }

    private void RequireCompany(Guid expected)
    {
        if (!company.IsResolved || company.CompanyId != expected)
        {
            throw new InvalidOperationException("The company changed during the approval follow-up cycle.");
        }
    }

    private sealed class Counts
    {
        public int Followed;
        public int Unavailable;
        public int Waiting;
        public int Reminders;
        public int Escalations;
        public int Orphaned;
        public int Notifications;
        public int Decisions;

        public WorkApprovalFollowUpCycleV1 Result() =>
            new(Followed, Unavailable, Waiting, Reminders, Escalations, Orphaned, Notifications, Decisions);
    }
}
