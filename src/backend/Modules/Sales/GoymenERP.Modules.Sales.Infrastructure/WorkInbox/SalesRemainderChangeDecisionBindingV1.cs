using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

public static class SalesRemainderChangeDecisionBindingV1
{
    public const string KindName = "sales.remainder-change.v1";
    public const string RequestedRule = "sales.remainder-change-requested";
    internal static WorkInboxItemV1 PendingItem(SalesRemainderPendingV1 r, bool support) => Item(new(r.CompanyId, r.DraftId, r.DocumentNo, r.ChangeNo,
        r.LineId, r.RequestRevision, r.RequestedBy, r.RequestedAtUtc, r.DraftCreatedBy, support, r.CurrentVersion, r.CurrentVersion, "Pending", "", ""));
    public static WorkSalesRemainderChangeProofV1 Proof(SalesRemainderDecisionDetailV1 d)
    {
        if (!SalesRequestDecisionBindingV1.TryVersion(d.Version, out var n) || !SalesQuoteConversionDecisionBindingV1.TryRevision(d.Change.Revision, out var revision)
            || d.IsSalesSupport is null) throw new ArgumentException("Invalid native remainder facts.");
        return new(d.CompanyId, d.DraftId, d.DocumentNo, d.Change.ChangeNo, d.Change.LineId, revision, d.Change.RequestedBy, d.Change.RequestedAtUtc,
            d.DraftCreatedBy, d.IsSalesSupport.Value, n, n, d.Change.State, d.SourceFactsDigest, d.PreviewDigest);
    }
    public static WorkSalesRemainderChangeProofV1 Proof(SalesRemainderOriginalReceiptV1 r, long observedCurrentVersion)
    {
        if (!r.CreatorProofEstablished || !SalesQuoteConversionDecisionBindingV1.TryRevision(r.OriginalChange.Revision, out var revision)) throw new ArgumentException("Invalid original remainder identity.");
        return new(r.CompanyId, r.DraftId, r.DocumentNo, r.ChangeNo, r.LineId, revision, r.OriginalChange.RequestedBy, r.OriginalChange.RequestedAtUtc,
            r.DraftCreatedBy, false, r.ExpectedVersion, observedCurrentVersion, r.OriginalChange.State, r.SourceFactsDigest, r.OriginalDecisionDigest);
    }
    public static WorkInboxItemV1? Item(SalesRemainderDecisionDetailV1? detail) => ValidDetail(detail) ? Item(Proof(detail!)) : null;
    public static WorkInboxItemV1? Item(SalesRemainderOriginalReceiptV1? receipt, long currentVersion) => ValidReceipt(receipt) && currentVersion >= receipt!.ResultVersion ? Item(Proof(receipt, currentVersion)) : null;
    internal static WorkInboxItemV1 Item(WorkSalesRemainderChangeProofV1 p)
    {
        var document = SalesWorkInboxItemsV1.RequireLabel(p.DocumentNo); var label = SalesWorkInboxItemsV1.RequireLabel($"{document} #{p.ChangeNo.ToString(CultureInfo.InvariantCulture)}");
        var excluded = p.ApplyPersonaRestriction ? new[] { p.RequestedBy, p.DraftCreatedBy }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.Ordinal).ToArray() : null;
        return new("sales", KindName, p.DraftId, label, p.RequestedAtUtc, SalesWorkInboxItemsV1.OrderLink(p.DraftId), p.RequestedBy,
            [new(RequestedRule, null, null, null, null)], [SalesWorkInboxItemsV1.OrderReference(p.DraftId, document),
                new("sales-order-line", "Sipariş satırı", p.LineId, SalesWorkInboxItemsV1.OrderLink(p.DraftId))], ExcludedDeciders: excluded);
    }
    public static bool ValidProof(WorkSalesRemainderChangeProofV1? p) => p is { CompanyId: var company, DraftId: var id, LineId: var line, ChangeNo: > 0, RequestRevision: >= 0,
        SelectedExpectedVersion: > 0, ObservedCurrentVersion: > 0 } && company != Guid.Empty && id != Guid.Empty && line != Guid.Empty
        && p.SelectedExpectedVersion < long.MaxValue && p.ObservedCurrentVersion >= p.SelectedExpectedVersion
        && !string.IsNullOrWhiteSpace(p.DocumentNo) && p.DocumentNo == p.DocumentNo.Trim() && !string.IsNullOrWhiteSpace(p.RequestedBy) && p.RequestedBy == p.RequestedBy.Trim()
        && (p.DraftCreatedBy is null || !string.IsNullOrWhiteSpace(p.DraftCreatedBy) && p.DraftCreatedBy == p.DraftCreatedBy.Trim())
        && p.RequestedAtUtc.Offset == TimeSpan.Zero && p.RequestState is "Pending" or "Approved" or "Rejected"
        && Hash(p.SourceFactsDigest) && Hash(p.PreviewDigest);
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkSalesRemainderChangeProofV1? proof)
    {
        if (item is null || !ValidProof(proof)) return false;
        try
        {
            var expected = Item(proof!);
            return item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId && item.DocumentLabel == expected.DocumentLabel
                && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link && item.PreparedBy == expected.PreparedBy && item.Scope is null
                && item.Rules is not null && item.Rules.SequenceEqual(expected.Rules) && item.References is not null && item.References.SequenceEqual(expected.References)
                && (item.ExcludedDeciders is null && expected.ExcludedDeciders is null || item.ExcludedDeciders is not null && expected.ExcludedDeciders is not null && item.ExcludedDeciders.SequenceEqual(expected.ExcludedDeciders));
        }
        catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; }
    }
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, SalesRemainderDecisionDetailV1? d) => ValidDetail(d) && MatchesDecisionProof(item, Proof(d!));
    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? snapshot, SalesRemainderDecisionDetailV1? d) => snapshot is not null && ValidDetail(d)
        && snapshot.CompanyId == d!.CompanyId && snapshot.SalesRemainderChangeProof == Proof(d) && snapshot.SourceVersion == d.Version
        && snapshot.Pending == (d.Change.State == "Pending") && MatchesDecisionItem(snapshot.Item, d);
    public static bool ValidDetail(SalesRemainderDecisionDetailV1? d) => d is not null && d.IsSalesSupport is not null && d.Change is { ChangeNo: > 0, LineId: var line }
        && line != Guid.Empty && d.Line is { } current && current.Id == line && current.LineNo == d.Change.LineNo
        && d.ApprovalSupported == (d.Change.State == "Pending") && d.RejectionSupported == d.ApprovalSupported
        && d.SourceFactsDigest == SalesRemainderDecisionDigestsV1.SourceFactsDigest(d.CompanyId, d.DraftId, d.DocumentNo, d.Change)
        && d.PreviewDigest == SalesRemainderDecisionDigestsV1.PreviewDigest(d) && SafeProof(d);
    private static bool SafeProof(SalesRemainderDecisionDetailV1 d) { try { return ValidProof(Proof(d)); } catch (ArgumentException) { return false; } }
    public static bool ValidReceipt(SalesRemainderOriginalReceiptV1? r) => r is { CreatorProofEstablished: true, ExpectedVersion: > 0, ChangeNo: > 0 }
        && r.CompanyId != Guid.Empty && r.DraftId != Guid.Empty && r.LineId != Guid.Empty && !string.IsNullOrWhiteSpace(r.ActorId) && r.ActorId == r.ActorId.Trim()
        && !string.IsNullOrWhiteSpace(r.OriginalCorrelationId) && r.RecordedAtUtc.Offset == TimeSpan.Zero
        && r.ExpectedVersion < long.MaxValue && r.ResultVersion == r.ExpectedVersion + 1 && r.OriginalChange is { } c && c.ChangeNo == r.ChangeNo && c.LineId == r.LineId
        && c.State == (r.CommandName == SalesDraftCommands.ApproveRemainderChangeV1 ? "Approved" : "Rejected")
        && r.CommandName is SalesDraftCommands.ApproveRemainderChangeV1 or SalesDraftCommands.RejectRemainderChangeV1
        && c.DecidedBy == r.ActorId && c.DecidedAtUtc == r.RecordedAtUtc && r.AuditEventId != Guid.Empty && NativeHash(r.AuditIntegrityHash) && NativeHash(r.IdempotencyKeyHash) && Hash(r.PayloadHash)
        && r.SourceFactsDigest == SalesRemainderDecisionDigestsV1.SourceFactsDigest(r.CompanyId, r.DraftId, r.DocumentNo, c)
        && r.OriginalDecisionDigest == SalesRemainderDecisionDigestsV1.OriginalDecisionDigest(r);
    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot, SalesOrderDraftResponse? response,
        SalesDraftRemainderDecisionRequest request, string key, string actor, bool approve)
    {
        if (snapshot?.SalesRemainderChangeProof is not { } p || !MatchesDecisionProof(snapshot.Item, p) || response?.OriginalRemainderDecisionReceipt is not { } r
            || !ValidReceipt(r) || request is null || string.IsNullOrWhiteSpace(key) || key != key.Trim() || string.IsNullOrWhiteSpace(actor)
            || snapshot.CompanyId != p.CompanyId || snapshot.SourceVersion != request.ExpectedVersion
            || !SalesRequestDecisionBindingV1.TryVersion(request.ExpectedVersion, out var n) || n != p.SelectedExpectedVersion || r.ExpectedVersion != n
            || r.DraftId != p.DraftId || r.ChangeNo != p.ChangeNo || r.LineId != p.LineId || r.ActorId != actor || r.CompanyId != p.CompanyId
            || r.DocumentNo != p.DocumentNo || r.SourceFactsDigest != p.SourceFactsDigest || r.OriginalChange.RequestedBy != p.RequestedBy || r.OriginalChange.RequestedAtUtc != p.RequestedAtUtc
            || r.DraftCreatedBy != p.DraftCreatedBy || r.OriginalChange.Revision != SalesDraftApiV1.RevisionText(p.RequestRevision)
            || r.CommandName != (approve ? SalesDraftCommands.ApproveRemainderChangeV1 : SalesDraftCommands.RejectRemainderChangeV1)
            || r.IdempotencyKeyHash != SalesStore.IdempotencyKeyHash(key) || r.PayloadHash != SalesDraftRequestHash.Compute(r.CommandName, p.DraftId, p.ChangeNo, request)
            || response.OriginalCorrelationId != r.OriginalCorrelationId || response.Detail.Draft.Id != p.DraftId
            || r.CurrentResponseDigest != SalesRemainderDecisionDigestsV1.CurrentResponseDigest(response)
            || !snapshot.Pending && p.PreviewDigest != r.OriginalDecisionDigest
            || !response.IsReplay && (!snapshot.Pending || p.RequestState != "Pending" || p.SelectedExpectedVersion != p.ObservedCurrentVersion)) return false;
        return true;
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(x => x is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool NativeHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
