using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceCreditMovementServiceV1
{
    /// <summary>Bind the human or original native answer to the exact owner inbox facts, not a second formatter.</summary>
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, FinanceCreditMovementDecisionDetailV1? detail)
    {
        if (item is null || detail?.Movement is not { } view || view.IsOwnProposal || !ValidDecisionMovement(view, detail.ProposedBy)
            || detail.SourceFactsDigest != SourceFactsDigest(view, detail.ProposedBy)) return false;
        var expected = FinanceCreditMovementWorkInboxSourceV1.DecisionItem(detail);
        return expected is not null && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc
            && item.PreparedBy == expected.PreparedBy && item.Link == expected.Link && item.Scope is null
            && item.Rules is not null && item.References is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References.SequenceEqual(expected.References)
            && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
    }
}
