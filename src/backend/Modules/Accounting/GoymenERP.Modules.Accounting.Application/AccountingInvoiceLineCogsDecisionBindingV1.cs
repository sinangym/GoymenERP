using System.Globalization;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Accounting.Application;

public static class AccountingInvoiceLineCogsDecisionBindingV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.invoice-line-cogs-correction.v1";
    public const string ScreenPath = "/muhasebe/fatura-satiri-smm-onay";
    public const string PurposePath = "/onaylarim/accounting.invoice-line-cogs-correction.v1";
    public const string ApprovalRequiredRule = "accounting.invoice-line-cogs-correction-approval-required";
    public static WorkInboxItemV1? Item(Guid proposalId, Guid invoiceId, Guid lineId, Guid preparedBy, DateTimeOffset preparedAt)
    {
        if (proposalId == Guid.Empty || invoiceId == Guid.Empty || lineId == Guid.Empty || preparedBy == Guid.Empty) return null;
        return new(ModuleName, KindName, proposalId, Caption(proposalId), preparedAt.ToUniversalTime(), PurposePath + "/" + proposalId.ToString("D", CultureInfo.InvariantCulture), preparedBy.ToString("D", CultureInfo.InvariantCulture),
            [new(ApprovalRequiredRule, null, null, null, null)],
            [new("invoice", Caption(invoiceId), invoiceId, null), new("invoice-line", Caption(lineId), lineId, null)]);
    }
    public static WorkInboxItemV1? FromView(InvoiceLineCogsCorrectionViewV1 view) =>
        Item(view.ProposalId, view.InvoiceId, view.InvoiceLineId, view.PreparedBy, view.PreparedAtUtc);
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkInvoiceLineCogsProofV1? proof) =>
        proof is not null && proof.CompanyId != Guid.Empty && proof.FinalizedVersion > 0 && proof.ExpectedLineVersion >= 0
        && proof.Status is "Prepared" or "Approved" && proof.PreparedAtUtc.Offset == TimeSpan.Zero && Digest(proof.SourceFactsDigest) && Digest(proof.PreviewDigest)
        && Item(proof.ProposalId, proof.InvoiceId, proof.InvoiceLineId, proof.PreparedBy, proof.PreparedAtUtc) is { } expected && Same(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, InvoiceLineCogsDecisionDetailV1? detail) =>
        InvoiceLineCogsCorrectionServiceV1.ValidCogsDecisionDetail(detail) && FromView(detail!.Proposal) is { } expected && Same(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, InvoiceLineCogsCorrectionViewV1? original, InvoiceLineCogsApprovalReceiptV1? receipt) =>
        original is { Status: "Approved", Version: 2 } p && receipt is not null && p.CompanyId != Guid.Empty && p.CompanyId == receipt.CompanyId
        && p.ProposalId == receipt.ProposalId && p.InvoiceId == receipt.InvoiceId && p.InvoiceLineId == receipt.InvoiceLineId
        && p.PreparedBy == receipt.PreparedBy && p.PreparedAtUtc == receipt.PreparedAtUtc && p.ApprovedBy == receipt.ActorId
        && receipt.ActorId != Guid.Empty && receipt.ActorId != receipt.PreparedBy && p.ApprovedAtUtc == receipt.ApprovedAtUtc
        && receipt.ExpectedVersion == 1 && receipt.ResultVersion == 2 && receipt.CommandName == InvoiceLineCogsCorrectionServiceV1.CogsApproveCommand
        && Digest(receipt.IdempotencyKeyHash) && receipt.PayloadHash == InvoiceLineCogsCorrectionServiceV1.CogsApprovalPayloadHash(p.InvoiceId, p.InvoiceLineId, p.ProposalId, 1)
        && Digest(receipt.SourceFactsDigest) && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
        && receipt.OriginalResultDigest == InvoiceLineCogsCorrectionServiceV1.CogsOriginalResultDigest(new(AccountingApiStatus.Ok, p, false, null))
        && FromView(p) is { } expected && Same(item, expected);
    private static string Caption(Guid id) => id.ToString("D", CultureInfo.InvariantCulture)[..8];
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool Same(WorkInboxItemV1? item, WorkInboxItemV1 expected) => item is not null && item.Module == expected.Module && item.Kind == expected.Kind
        && item.ItemId == expected.ItemId && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc
        && item.Link == expected.Link && item.PreparedBy == expected.PreparedBy && item.Scope is null && item.Rules is not null && item.References is not null
        && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References) && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
}
