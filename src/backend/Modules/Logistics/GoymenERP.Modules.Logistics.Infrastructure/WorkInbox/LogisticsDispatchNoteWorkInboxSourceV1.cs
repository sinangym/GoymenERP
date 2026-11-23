using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;

namespace GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the dispatch-note (irsaliye) drafts waiting for approval (AT-055, user decision 2026-09-30),
/// as Logistics lists them for the Work inbox. A note waits while it is a Draft on an Issued instruction — the exact state
/// <c>ApproveLoadingInstructionDispatchNoteV1</c> accepts — and is decided on the instruction detail screen by a holder of
/// <c>logistics.dispatch-notes.approve</c> (the inbox decides nothing). Every pending draft of the company is returned, the
/// caller's own included: Work hides a maker's own item. WP6: the owner's approval refuses the draft's preparer (ROL-D14,
/// <c>logistics_dispatch_note_self_approval</c>), so the creator is exactly the owner's "may not decide" set.
/// <para>
/// No document number exists before approval (the system number is given on approval), so the label is the note's identity.
/// The draft has no separate submission: it waits from its creation. No threshold applies: every draft needs approval, so the
/// one rule carries no numbers.
/// </para>
/// <para>
/// WP2: it is also a follow-up source — the same pending list for the resolved company without a caller (Work's reminder,
/// escalation and "şimdi" notification), and the note's own approval or cancellation time as its decision time (D10).
/// </para>
/// </summary>
internal sealed class LogisticsDispatchNoteWorkInboxSourceV1 : IWorkInboxFollowUpSourceV1
{
    public const string ModuleName = "logistics";
    public const string KindName = "logistics.dispatch-note.v1";

    /// <summary>A dispatch-note draft always needs approval before it is numbered and final.</summary>
    public const string DraftRule = "logistics.dispatch-note-draft";

    private readonly LogisticsWorkInboxRows rows;
    private readonly LogisticsWorkInboxFence fence;

    public LogisticsDispatchNoteWorkInboxSourceV1(LogisticsDbContext dbContext, LogisticsWorkInboxFence fence)
        : this(new LogisticsWorkInboxDbRows(dbContext), fence)
    {
    }

    internal LogisticsDispatchNoteWorkInboxSourceV1(LogisticsWorkInboxRows rows, LogisticsWorkInboxFence fence)
    {
        this.rows = rows;
        this.fence = fence;
    }

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => LogisticsPermissionManifestV1.DispatchNotesApprove;


    public async Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } refused)
        {
            return refused;
        }

        var read = await ReadPendingAsync(context.CompanyId, maxCount, cancellationToken);
        if (read is null)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        return Items(context.CompanyId, read, maxCount);
    }

    public async Task<WorkInboxSourceResultV1> ListPendingForFollowUpAsync(
        Guid companyId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        if (!fence.SameCompany(companyId))
        {
            return WorkInboxSourceResultV1.Denied();
        }

        var read = await ReadPendingAsync(companyId, maxCount, cancellationToken);
        if (!fence.SameCompany(companyId))
        {
            return WorkInboxSourceResultV1.Denied();
        }

        return read is null ? WorkInboxSourceResultV1.Unavailable() : Items(companyId, read, maxCount);
    }

    public async Task<WorkInboxDecisionAnswerV1> ReadDecisionAsync(
        Guid companyId,
        Guid itemId,
        DateTimeOffset waitingSinceUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!fence.SameCompany(companyId) || itemId == Guid.Empty)
        {
            return WorkInboxDecisionAnswerV1.Unavailable();
        }

        List<NoteDecision> found;
        try
        {
            found = await rows.ListAsync(
                (from note in rows.DispatchNotes
                 join instruction in rows.Instructions
                     on new { note.CompanyId, Id = note.InstructionId } equals new { instruction.CompanyId, instruction.Id }
                 where note.CompanyId == companyId && instruction.CompanyId == companyId && note.DispatchNoteId == itemId
                 select new NoteDecision(note.Status, instruction.Status, note.CreatedAtUtc, note.ApprovedAtUtc, note.CancelledAtUtc))
                .Take(2),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return WorkInboxDecisionAnswerV1.Unavailable();
        }

        if (!fence.SameCompany(companyId) || found.Count > 1)
        {
            return WorkInboxDecisionAnswerV1.Unavailable();
        }

        if (found.Count == 0 || found[0].CreatedAtUtc.ToUniversalTime() != waitingSinceUtc)
        {
            return WorkInboxDecisionAnswerV1.NoDecisionRecorded();
        }

        var decision = found[0];
        return decision.NoteStatus switch
        {
            (int)DispatchNoteStatus.Approved when decision.ApprovedAtUtc is { } approved && approved >= decision.CreatedAtUtc =>
                WorkInboxDecisionAnswerV1.Decided(approved, WorkInboxDecisionsV1.Approved),
            (int)DispatchNoteStatus.Cancelled when decision.CancelledAtUtc is { } cancelled && cancelled >= decision.CreatedAtUtc =>
                WorkInboxDecisionAnswerV1.Decided(cancelled, WorkInboxDecisionsV1.Cancelled),
            (int)DispatchNoteStatus.Draft when decision.InstructionStatus == (int)LoadingInstructionStatus.Issued =>
                WorkInboxDecisionAnswerV1.StillPending(),
            (int)DispatchNoteStatus.Draft => WorkInboxDecisionAnswerV1.NoDecisionRecorded(),
            _ => WorkInboxDecisionAnswerV1.Unavailable(),
        };
    }

    /// <summary>The pending drafts of the company (at most one more than asked), or null when Logistics could not read them.</summary>
    private async Task<List<PendingNote>?> ReadPendingAsync(Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        const int draft = (int)DispatchNoteStatus.Draft;
        const int issued = (int)LoadingInstructionStatus.Issued;
        try
        {
            var read = await rows.ListAsync(
                (from note in rows.DispatchNotes
                 join instruction in rows.Instructions
                     on new { note.CompanyId, Id = note.InstructionId } equals new { instruction.CompanyId, instruction.Id }
                 where note.CompanyId == companyId && instruction.CompanyId == companyId
                     && note.Status == draft && instruction.Status == issued
                 orderby note.CreatedAtUtc, note.DispatchNoteId
                 select new { note.InstructionId, note.DispatchNoteId, note.CreatedBy, note.CreatedAtUtc })
                .Take(maxCount + 1),
                cancellationToken);
            return [.. read.Select(note => new PendingNote(note.InstructionId, note.DispatchNoteId, note.CreatedBy, note.CreatedAtUtc))];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static WorkInboxSourceResultV1 Items(Guid companyId, List<PendingNote> pending, int maxCount)
    {
        var items = new List<WorkInboxItemV1>(Math.Min(pending.Count, maxCount));
        foreach (var note in pending.Take(maxCount))
        {
            if (note.InstructionId == Guid.Empty || note.DispatchNoteId == Guid.Empty || string.IsNullOrWhiteSpace(note.CreatedBy))
            {
                // A record Logistics cannot read as its own invariants say: unavailable, never a partial list.
                return WorkInboxSourceResultV1.Unavailable();
            }

            items.Add(new WorkInboxItemV1(ModuleName, KindName, note.DispatchNoteId, LogisticsWorkInboxFence.Id(note.DispatchNoteId),
                note.CreatedAtUtc.ToUniversalTime(), LogisticsWorkInboxFence.InstructionLink(note.InstructionId), note.CreatedBy,
                [new WorkApprovalRuleResultV1(DraftRule, null, null, null, null)],
                [LogisticsWorkInboxFence.InstructionReference(note.InstructionId)]));
        }

        return WorkInboxSourceResultV1.Found(companyId, items, pending.Count > maxCount);
    }

    private sealed record NoteDecision(int NoteStatus, int InstructionStatus, DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ApprovedAtUtc, DateTimeOffset? CancelledAtUtc);

    private sealed record PendingNote(Guid InstructionId, Guid DispatchNoteId, string CreatedBy, DateTimeOffset CreatedAtUtc);
}
