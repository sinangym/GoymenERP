using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

public static class SalesCustomerReturnDecisionBindingV1
{
    public const string KindName = "sales.customer-return.v1";
    public static WorkSalesCustomerReturnProofV1 Proof(SalesCustomerReturnDecisionDetailV1 d)
    {
        if (!SalesRequestDecisionBindingV1.TryVersion(d.CustomerReturn.Version, out var n)) throw new ArgumentException("Invalid native return version.");
        var v = d.CustomerReturn;
        return new(d.CompanyId, v.Id, v.OriginalInvoiceId, v.OriginalDocumentNo, v.CustomerDocumentNo, v.CreatedBy, v.CreatedAtUtc,
            v.SubmittedBy!, v.SubmittedAtUtc!.Value, n, n, v.State, d.SourceFactsDigest, d.PreviewDigest, null);
    }
    public static WorkSalesCustomerReturnProofV1 Proof(SalesCustomerReturnOriginalReceiptV1 r, long current)
    {
        var v = r.OriginalDecision;
        return new(r.CompanyId, r.ReturnId, v.OriginalInvoiceId, v.OriginalDocumentNo, v.CustomerDocumentNo, v.CreatedBy, v.CreatedAtUtc,
            v.SubmittedBy!, v.SubmittedAtUtc!.Value, r.ExpectedVersion, current, v.State, r.SourceFactsDigest, r.OriginalDecisionDigest, r.OriginalDecisionDigest);
    }
    public static WorkInboxItemV1? Item(SalesCustomerReturnDecisionDetailV1? d) => ValidDetail(d) ? Item(Proof(d!)) : null;
    public static WorkInboxItemV1? Item(SalesCustomerReturnOriginalReceiptV1? r, long current) => ValidReceipt(r) && current >= r!.ResultVersion ? Item(Proof(r, current)) : null;
    private static WorkInboxItemV1 Item(WorkSalesCustomerReturnProofV1 p) => SalesCustomerReturnWorkInboxSourceV1.Project(p.CompanyId,
        [new(p.ReturnId, p.CompanyId, p.CustomerDocumentNo, p.OriginalInvoiceId, p.OriginalDocumentNo, p.PreparedBy, p.SubmittedBy, p.SubmittedAtUtc)], 1).Items.Single();
    public static bool ValidProof(WorkSalesCustomerReturnProofV1? p) => p is not null && p.CompanyId != Guid.Empty && p.ReturnId != Guid.Empty && p.OriginalInvoiceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(p.OriginalDocumentNo) && !string.IsNullOrWhiteSpace(p.CustomerDocumentNo)
        && !string.IsNullOrWhiteSpace(p.PreparedBy) && p.PreparedBy == p.PreparedBy.Trim() && !string.IsNullOrWhiteSpace(p.SubmittedBy) && p.SubmittedBy == p.SubmittedBy.Trim()
        && p.PreparedAtUtc.Offset == TimeSpan.Zero && p.SubmittedAtUtc.Offset == TimeSpan.Zero
        && p.SelectedExpectedVersion > 0 && p.SelectedExpectedVersion < long.MaxValue && p.CurrentVersion >= p.SelectedExpectedVersion
        && p.State is "PendingApproval" or "Approved" or "Finalized" or "Rejected" or "Cancelled"
        && Hash(p.SourceFactsDigest) && Hash(p.PreviewDigest) && (p.OriginalDecisionDigest is null || Hash(p.OriginalDecisionDigest)
            && p.PreviewDigest == p.OriginalDecisionDigest && p.CurrentVersion > p.SelectedExpectedVersion && p.State is "Approved" or "Rejected");
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkSalesCustomerReturnProofV1? p)
    {
        if (item is null || !ValidProof(p)) return false;
        try
        {
            var expected = Item(p!);
            return item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId && item.DocumentLabel == expected.DocumentLabel
                && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link && item.PreparedBy == expected.PreparedBy && item.Scope is null
                && item.Rules is not null && item.Rules.SequenceEqual(expected.Rules) && item.References is not null && item.References.SequenceEqual(expected.References)
                && (item.ExcludedDeciders is null && expected.ExcludedDeciders is null || item.ExcludedDeciders is not null && expected.ExcludedDeciders is not null && item.ExcludedDeciders.SequenceEqual(expected.ExcludedDeciders));
        }
        catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; }
    }
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, SalesCustomerReturnDecisionDetailV1? d) => ValidDetail(d) && MatchesDecisionProof(item, Proof(d!));
    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? snapshot, SalesCustomerReturnDecisionDetailV1? d) => snapshot is not null && ValidDetail(d)
        && snapshot.CompanyId == d!.CompanyId && snapshot.SalesCustomerReturnProof == Proof(d) && snapshot.SourceVersion == d.CustomerReturn.Version
        && snapshot.Pending == (d.CustomerReturn.State == "PendingApproval") && MatchesDecisionItem(snapshot.Item, d);
    public static bool ValidDetail(SalesCustomerReturnDecisionDetailV1? d) => d?.CustomerReturn is { } v && d.CompanyId != Guid.Empty && v.Id != Guid.Empty && v.OriginalInvoiceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(v.SubmittedBy) && v.SubmittedAtUtc is not null && v.Lines is { Count: > 0 } && v.Lines.All(x => x is not null)
        && d.ApprovalSupported == (v.State == "PendingApproval") && d.RejectionSupported == d.ApprovalSupported
        && d.SourceFactsDigest == SalesCustomerReturnDecisionDigestsV1.SourceFactsDigest(d.CompanyId, v) && d.PreviewDigest == SalesCustomerReturnDecisionDigestsV1.PreviewDigest(d) && SafeProof(d);
    private static bool SafeProof(SalesCustomerReturnDecisionDetailV1 d) { try { return ValidProof(Proof(d)); } catch (ArgumentException) { return false; } }
    public static bool ValidReceipt(SalesCustomerReturnOriginalReceiptV1? r) => SalesCustomerReturnDecisionDigestsV1.IsOriginalReceipt(r);
    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot, SalesCustomerReturnCommandResult? response,
        SalesDraftStateCommandRequest request, string key, string actor, bool approve)
    {
        if (snapshot?.SalesCustomerReturnProof is not { } p || !MatchesDecisionProof(snapshot.Item, p) || response?.OriginalDecisionReceipt is not { } r || !ValidReceipt(r)
            || request is null || string.IsNullOrWhiteSpace(key) || key != key.Trim() || string.IsNullOrWhiteSpace(actor) || snapshot.CompanyId != p.CompanyId
            || snapshot.SourceVersion != request.ExpectedVersion || !SalesRequestDecisionBindingV1.TryVersion(request.ExpectedVersion, out var n) || n != p.SelectedExpectedVersion
            || r.ExpectedVersion != n || r.CompanyId != p.CompanyId || r.ReturnId != p.ReturnId || r.ActorId != actor || r.SourceFactsDigest != p.SourceFactsDigest
            || r.OriginalDecision.OriginalInvoiceId != p.OriginalInvoiceId || r.OriginalDecision.OriginalDocumentNo != p.OriginalDocumentNo || r.OriginalDecision.CustomerDocumentNo != p.CustomerDocumentNo
            || r.OriginalDecision.CreatedBy != p.PreparedBy || r.OriginalDecision.CreatedAtUtc != p.PreparedAtUtc || r.OriginalDecision.SubmittedBy != p.SubmittedBy || r.OriginalDecision.SubmittedAtUtc != p.SubmittedAtUtc
            || r.CommandName != (approve ? SalesCustomerReturnCommands.ApproveV1 : SalesCustomerReturnCommands.RejectV1)
            || r.IdempotencyKeyHash != SalesStore.IdempotencyKeyHash(key) || r.PayloadHash != (approve ? SalesCustomerReturnHash.Compute(r.CommandName, p.ReturnId, request) : SalesCustomerReturnHash.ComputeDecision(r.CommandName, p.ReturnId, request))
            || response.OriginalCorrelationId != r.OriginalCorrelationId || response.CustomerReturn.Id != p.ReturnId || r.CurrentResponseDigest != SalesCustomerReturnDecisionDigestsV1.CurrentResponseDigest(response)
            || !snapshot.Pending && p.OriginalDecisionDigest != r.OriginalDecisionDigest
            || !response.IsReplay && (!snapshot.Pending || p.State != "PendingApproval" || p.SelectedExpectedVersion != p.CurrentVersion)) return false;
        return true;
    }
    private static bool Hash(string? s) => s is { Length: 64 } && s.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f');
}
