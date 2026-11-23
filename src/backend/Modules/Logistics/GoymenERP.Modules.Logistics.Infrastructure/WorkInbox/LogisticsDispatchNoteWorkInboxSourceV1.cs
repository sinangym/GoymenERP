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
/// </summary>
internal sealed class LogisticsDispatchNoteWorkInboxSourceV1 : IWorkInboxSourceV1
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

        var companyId = context.CompanyId;
        const int draft = (int)DispatchNoteStatus.Draft;
        const int issued = (int)LoadingInstructionStatus.Issued;
        List<PendingNote> pending;
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
            pending = [.. read.Select(note => new PendingNote(note.InstructionId, note.DispatchNoteId, note.CreatedBy, note.CreatedAtUtc))];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await fence.UnavailableAsync(context, ApproverPermission, cancellationToken);
        }

        if (await fence.CheckAsync(context, ApproverPermission, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

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

    private sealed record PendingNote(Guid InstructionId, Guid DispatchNoteId, string CreatedBy, DateTimeOffset CreatedAtUtc);
}
