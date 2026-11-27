using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
namespace GoymenERP.Modules.Accounting.Application;
public static class AccountingInventoryCostCancellationDecisionBindingV1
{
    public const string Kind = "accounting.inventory-cost-cancellation.v1";
    public const string ScreenPath = "/muhasebe/sevk-smm-ters-kayit-onay";
    public const string Rule = "accounting.inventory-cost-cancellation-approval-required";
    public static WorkInboxItemV1? Item(Guid id, Guid cancellationId, Guid periodId, Guid maker, DateTimeOffset recordedAtUtc) =>
        id == Guid.Empty || cancellationId == Guid.Empty || periodId == Guid.Empty || maker == Guid.Empty ? null
        : new("accounting", Kind, id, Short(id), recordedAtUtc.ToUniversalTime(), ScreenPath, maker.ToString("D", CultureInfo.InvariantCulture),
            [new(Rule, null, null, null, null)], [new("inventory-cost-proposal", Short(id), id, null),
            new("invoice-cancellation", Short(cancellationId), cancellationId, null), new("period", Short(periodId), periodId, null)]);
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkInventoryCostCancellationProofV1? proof) =>
        proof is not null && proof.CompanyId != Guid.Empty && proof.ComponentCancellationId != Guid.Empty && proof.ProposalVersion > 0
        && proof.PreparedCancellationVersion > 0 && proof.PreparedAtUtc.Offset == TimeSpan.Zero && proof.PreparedRecordedAtUtc.Offset == TimeSpan.Zero
        && proof.Status is "Prepared" or "Approved" && Digest(proof.SourceFactsDigest) && Digest(proof.PreviewDigest)
        && Item(proof.InventoryCostProposalId, proof.SourceCancellationId, proof.PeriodId, proof.PreparedBy, proof.PreparedRecordedAtUtc) is { } expected && Same(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingInventoryCostCancellationDecisionDetailV1? detail) =>
        AccountingInventoryCostServiceV1.ValidCancellationDecisionDetail(detail) && detail is not null
        && Item(detail.OriginalProposal.ProposalId, detail.PreparedCancellation.CancellationId, detail.PreparedCancellation.PeriodId,
            detail.PreparedCancellation.PreparedBy, detail.PreparedRecordedAtUtc) is { } expected && Same(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingInventoryCostProposalV1? original,
        AccountingInventoryCostCancellationApprovalReceiptV1? receipt) => receipt is not null && original?.ComponentCancellation is { } c
        && receipt.CompanyId != Guid.Empty && receipt.ActorId != Guid.Empty && receipt.ExpectedProposalVersion > 0
        && original.Kind == AccountingInventoryCostKindV1.SalesInvoicedShipment && original.CancellationId == c.CancellationId
        && c.CompanyId == original.CompanyId && c.InventoryCostProposalId == original.ProposalId
        && original.CompanyId == receipt.CompanyId && original.ProposalId == receipt.InventoryCostProposalId && original.Version == receipt.ExpectedProposalVersion
        && receipt.ResultProposalVersion == original.Version && original.Status == "CancelledByComponentReversal" && c.Status == "Approved"
        && c.Id == receipt.ComponentCancellationId && c.CancellationId == receipt.SourceCancellationId && c.Version == receipt.ResultCancellationVersion
        && receipt.ExpectedCancellationVersion > 0 && receipt.ExpectedCancellationVersion < long.MaxValue && c.Version == receipt.ExpectedCancellationVersion + 1
        && c.PreparedBy == receipt.PreparedBy && c.PreparedAtUtc == receipt.PreparedAtUtc && c.ApprovedBy == receipt.ActorId && receipt.ActorId != c.PreparedBy
        && c.ApprovedAtUtc == receipt.DecidedAtUtc && receipt.CommandName == AccountingInventoryCostServiceV1.InventoryCostCancellationApproveCommand
        && receipt.PayloadHash == AccountingInventoryCostServiceV1.CancellationApprovalPayloadHash(original.ProposalId, new(receipt.ExpectedProposalVersion, receipt.ExpectedCancellationVersion))
        && receipt.PreparedAtUtc.Offset == TimeSpan.Zero && receipt.PreparedRecordedAtUtc.Offset == TimeSpan.Zero && receipt.DecidedAtUtc.Offset == TimeSpan.Zero
        && AccountingInventoryCostServiceV1.InventoryCostOriginalResultDigest(new(AccountingApiStatus.Ok, original, false, null)) == receipt.OriginalResultDigest
        && Digest(receipt.IdempotencyKeyHash) && Digest(receipt.SourceFactsDigest) && Digest(receipt.OriginalResultDigest) && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
        && Item(original.ProposalId, c.CancellationId, c.PeriodId, c.PreparedBy, receipt.PreparedRecordedAtUtc) is { } expected && Same(item, expected);
    private static bool Same(WorkInboxItemV1? item, WorkInboxItemV1 expected) => item is not null
        && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId && item.DocumentLabel == expected.DocumentLabel
        && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link && item.PreparedBy == expected.PreparedBy && item.Scope is null
        && item.Rules is not null && item.References is not null && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References)
        && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static string Short(Guid id) => id.ToString("D", CultureInfo.InvariantCulture)[..8];
}
