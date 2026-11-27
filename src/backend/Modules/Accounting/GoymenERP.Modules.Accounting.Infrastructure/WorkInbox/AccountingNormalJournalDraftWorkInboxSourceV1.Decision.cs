using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

internal sealed partial class AccountingNormalJournalDraftWorkInboxSourceV1 : IWorkApprovalDecisionSourceV1
{
    public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); await Fence(token);
        if (owner is null) throw new InvalidOperationException("Normal journal decision owner unavailable.");
        var answer = await owner.ReadDecisionAsync(itemId, Fence, token);
        await Fence(token);
        if (answer is null) throw new InvalidOperationException("Normal journal decision answer unavailable.");
        if (answer.Status == AccountingApiStatus.Denied && answer.Resource is null) throw new UnauthorizedAccessException();
        if (answer.Status == AccountingApiStatus.NotFound && answer.Resource is null) return null;
        if (answer.Status != AccountingApiStatus.Ok || answer.Resource is not { } detail || detail.Draft is not { } view
            || view.CompanyId != context.CompanyId || view.DraftId != itemId || detail.IsCreditSource is null
            || detail.SourceFactsDigest is not { Length: 64 } || !detail.SourceFactsDigest.All(c => char.IsAsciiDigit(c) || c is >= 'A' and <= 'F')
            || detail.PreviewDigest != NormalJournalDraftServiceV1.NormalPreviewDigest(view)
            || DecisionItem(detail) is not { } item) throw new InvalidOperationException("Normal journal decision facts unavailable.");
        if (item.Excludes(context.UserId)) throw new UnauthorizedAccessException();
        return new(context.CompanyId, item, view.Version.ToString(CultureInfo.InvariantCulture), view.Status == "Draft")
        {
            NormalJournalDraftProof = new(context.CompanyId, itemId, view.PreparedByUserId, view.PreparedAtUtc, view.Status,
                view.Version, detail.SourceFactsDigest, detail.PreviewDigest),
        };
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context.GrantScope is not null) throw new UnauthorizedAccessException();
            var refusal = await fence.CheckAsync(context, ApproverPermission, ct);
            ct.ThrowIfCancellationRequested();
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Normal journal authority unavailable.");
        }
    }

    internal static WorkInboxItemV1? DecisionItem(NormalJournalDraftDecisionDetailV1 detail)
    {
        var view = detail.Draft;
        if (detail.IsCreditSource is not { } credit || view.Version <= 0 || view.Status is not ("Draft" or "Approved")) return null;
        // This is the existing pending caption/rule/reference projection, also used for a terminal decision's context.
        // Actual terminal fields remain in the separately bound native view; no database row is altered.
        return Item(new NormalJournalDraftRow { Id = view.DraftId, CompanyId = view.CompanyId, PreparedByUserId = view.PreparedByUserId,
            PreparedAtUtc = view.PreparedAtUtc, SourceId = view.SourceId, CurrencyId = view.CurrencyId }, credit);
    }
}

/// <summary>Exact native inbox formatting for a fresh human view or an original approval answer. No parallel calculator.</summary>
public static class AccountingNormalJournalDraftDecisionBindingV1
{
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, NormalJournalDraftDecisionDetailV1? detail)
    {
        if (item is null || detail?.Draft is null || detail.IsCreditSource is null
            || AccountingNormalJournalDraftWorkInboxSourceV1.DecisionItem(detail) is not { } expected) return false;
        return item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc
            && item.PreparedBy == expected.PreparedBy && item.Link == expected.Link && item.Scope is null
            && item.Rules is not null && item.References is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References.SequenceEqual(expected.References)
            && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
    }
}
