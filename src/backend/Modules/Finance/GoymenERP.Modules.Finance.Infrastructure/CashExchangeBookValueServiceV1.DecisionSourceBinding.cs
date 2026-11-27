using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class CashExchangeBookValueServiceV1
{
    /// <summary>One native formatter for queue and purpose; no copied money/rule calculation.</summary>
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, CashExchangeBookValueDecisionDetailV1? detail)
    {
        if (item is null || DecisionItem(detail) is not { } expected) return false;
        return item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc
            && item.PreparedBy == expected.PreparedBy && item.Link == expected.Link && item.Scope is null
            && item.Rules is not null && item.References is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References.SequenceEqual(expected.References)
            && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
    }

    internal static WorkInboxItemV1? DecisionItem(CashExchangeBookValueDecisionDetailV1? detail)
    {
        if (detail?.Transfer is not { Preparation: { SourceMoney: not null, TargetMoney: not null } } transfer || detail.Proposal is not { Evidence: not null } p
            || p.Id == Guid.Empty || p.CompanyId == Guid.Empty || p.TransferId == Guid.Empty
            || !Enum.IsDefined(p.Status) || p.Version != (p.Status == CashExchangeBookValueStatusV1.Pending ? "1" : "2")) return null;
        try
        {
            var source = JsonSerializer.Serialize(transfer, Json);
            var row = new CashExchangeBookValueProposalRow { Id = p.Id, CompanyId = p.CompanyId, TransferId = p.TransferId,
                TransferSourceJson = source, TransferSourceHash = p.TransferSourceHash, PreparedBy = p.PreparedBy, PreparedAtUtc = p.PreparedAtUtc,
                PreparationJson = JsonSerializer.Serialize(new ProposeCashExchangeBookValueV1(p.TransferId, p.TransferVersion,
                    p.SourceBookValueTl, p.Evidence, p.Reason, p.TargetFunctionalValueTl), Json) };
            CashExchangeBookValueDecisionRow? decision = p.Status == CashExchangeBookValueStatusV1.Pending ? null : new()
            { CompanyId = p.CompanyId, ProposalId = p.Id, TransferId = p.TransferId, PreparedBy = p.PreparedBy,
                Approved = p.Status == CashExchangeBookValueStatusV1.Approved, DecidedBy = p.DecidedBy!,
                DecidedAtUtc = p.DecidedAtUtc ?? default, Reason = p.DecisionReason };
            // Pure native restoration enforces the existing preparation/transfer/evidence/decision rules.
            if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(View(row, decision), Json), JsonSerializer.SerializeToNode(p, Json))) return null;
            if (detail.DecisionProof is { } proof && (proof != new CashExchangeBookValueDecisionProofV1(p.CompanyId, p.Id,
                p.TransferId, p.PreparedBy, p.PreparedAtUtc, p.Status.ToString(), p.Version, p.TransferSourceHash,
                ProposalFactsDigest(p), DecisionPreviewDigest(detail)))) return null;
            return FinanceCashExchangeBookValueWorkInboxSourceV1.Item(row,
                new Dictionary<Guid, string?> { [p.TransferId] = transfer.DocumentNumber?.Number });
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        { return null; }
    }
}
