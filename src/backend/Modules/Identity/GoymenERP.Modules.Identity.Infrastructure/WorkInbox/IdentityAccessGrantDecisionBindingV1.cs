using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;

namespace GoymenERP.Modules.Identity.Infrastructure.WorkInbox;

/// <summary>Exact native item and selected owner facts. Current permissions never reconstruct historical applied impact.</summary>
public static class IdentityAccessGrantDecisionBindingV1
{
    public static WorkIdentityAccessGrantProofV1 Proof(Guid companyId, IdentityAccessGrantDecisionDetailV1 detail)
    {
        var r = detail.Request;
        return new(companyId, r.RequestId, r.RequestedBy.UserId, r.Target.UserId, r.RoleId, detail.RequiresSecondApproval,
            r.RequestedAtUtc, r.ValidUntilUtc, r.RoleName, r.Target.DisplayName, r.Status, r.Version,
            detail.SourceFactsDigest, detail.CurrentImpactDigest, detail.PreviewDigest);
    }

    public static WorkInboxItemV1? Item(Guid companyId, IdentityAccessGrantDecisionDetailV1? detail) =>
        ValidDetail(companyId, detail) ? IdentityAccessGrantRequestWorkInboxSourceV1.DecisionItem(Proof(companyId, detail!)) : null;

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, Guid companyId, IdentityAccessGrantDecisionDetailV1? detail) =>
        ValidDetail(companyId, detail) && MatchesDecisionProof(item, Proof(companyId, detail!));

    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkIdentityAccessGrantProofV1? proof)
    {
        if (!ValidProof(proof) || item is null || IdentityAccessGrantRequestWorkInboxSourceV1.DecisionItem(proof!) is not { } expected) return false;
        return item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc
            && item.Link == expected.Link && item.PreparedBy == expected.PreparedBy && item.Scope is null
            && (item.ExcludedDeciders is null ? expected.ExcludedDeciders is null
                : expected.ExcludedDeciders is not null && item.ExcludedDeciders.SequenceEqual(expected.ExcludedDeciders))
            && item.Rules is not null && item.Rules.SequenceEqual(expected.Rules)
            && item.References is not null && item.References.SequenceEqual(expected.References);
    }

    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? snapshot, IdentityAccessGrantDecisionDetailV1? detail) =>
        snapshot is not null && ValidDetail(snapshot.CompanyId, detail) && snapshot.SourceVersion == detail!.Request.Version
        && snapshot.Pending == (detail.Request.Status == "Pending") && snapshot.IdentityAccessGrantProof == Proof(snapshot.CompanyId, detail)
        && MatchesDecisionItem(snapshot.Item, snapshot.CompanyId, detail);

    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot, IdentityAccessGrantDecisionReceiptV1? receipt,
        IdentityAccessCommandResultV1? result, bool approve, string expectedVersion, string? reasonCode, string key, string actorId)
    {
        if (snapshot?.IdentityAccessGrantProof is not { } selected || snapshot.CompanyId != selected.CompanyId
            || snapshot.SourceVersion != selected.Version || snapshot.Pending != (selected.Status == "Pending")
            || !MatchesDecisionProof(snapshot.Item, selected) || receipt is null || result?.Answer is null
            || !TryVersion(expectedVersion, out var expected) || expected == long.MaxValue
            || receipt.ResultVersion != (expected + 1).ToString(CultureInfo.InvariantCulture)
            || string.IsNullOrWhiteSpace(key) || !Guid.TryParse(actorId, out var actor) || actor == Guid.Empty
            || selected.RequiresSecondApproval && (actor == selected.RequestedBy || actor == selected.TargetUserId)
            || (approve ? reasonCode is not null : !AccessGrantRequest.IsCanonicalReasonCode(reasonCode!))) return false;
        var name = approve ? IdentityAccessCommandsV1.Approve : IdentityAccessCommandsV1.Reject;
        object payload = approve ? new ApproveAccessGrantRequestV1(expectedVersion) : new RejectAccessGrantRequestV1(expectedVersion, reasonCode!);
        var answer = receipt.OriginalAnswer;
        return receipt.CompanyId == selected.CompanyId && receipt.RequestId == selected.RequestId && receipt.TargetUserId == selected.TargetUserId
            && receipt.RoleId == selected.RoleId && receipt.RequestedBy == selected.RequestedBy && receipt.RequestedAtUtc == selected.RequestedAtUtc
            && receipt.SourceFactsDigest == selected.SourceFactsDigest && receipt.ExpectedVersion == expectedVersion
            && receipt.CommandName == name && receipt.ActorId == actorId && receipt.KeyHash == IdempotencyHash.ComputeKey(key)
            && receipt.PayloadHash == IdempotencyHash.ComputePayload(new { Command = name, Target = selected.RequestId.ToString("D", CultureInfo.InvariantCulture), Request = payload })
            && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId) && receipt.OriginalCorrelationId == result.OriginalCorrelationId
            && answer is { StatusCode: 200, Code: null, Result: { } changed }
            && changed.Id == selected.RequestId && changed.Version == receipt.ResultVersion && changed.Status == (approve ? "Approved" : "Rejected")
            && (approve ? changed.RoleAssignmentId is { } assignment && assignment != Guid.Empty : changed.RoleAssignmentId is null)
            && receipt.OriginalAnswerDigest == IdempotencyHash.ComputePayload(answer)
            && receipt.OriginalAnswerDigest == IdempotencyHash.ComputePayload(result.Answer)
            && (receipt.AppliedImpactDigest is null && receipt.AppliedPermissionCount is null
                || approve && Digest(receipt.AppliedImpactDigest) && receipt.AppliedPermissionCount is >= 0);
    }

    public static bool TryVersion(string? value, out long version) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out version) && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;

    private static bool ValidDetail(Guid companyId, IdentityAccessGrantDecisionDetailV1? detail)
    {
        if (detail?.Request is not { RequestedBy: { } requester, Target: { } target } r
            || detail.CurrentPermissions is null || detail.CurrentPermissions.Any(string.IsNullOrWhiteSpace)
            || !detail.CurrentPermissions.SequenceEqual(detail.CurrentPermissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            || detail.CurrentRoleIsCritical != detail.CurrentPermissions.Any(ErpPermissions.IsCritical)
            || !AccessGrantRequest.IsValidWindow(r.ValidFromUtc, r.ValidUntilUtc)
            || r.Reason is { } reason && (reason.Length > AccessGrantRequest.ReasonMaxLength || reason != reason.Trim())
            || string.IsNullOrWhiteSpace(requester.DisplayName) || string.IsNullOrWhiteSpace(r.RoleCode)) return false;
        var source = IdempotencyHash.ComputePayload(new { Id = r.RequestId, CompanyId = companyId, RequestedByUserId = requester.UserId,
            TargetUserId = target.UserId, r.RoleId, detail.RequiresSecondApproval, r.RequestedAtUtc, r.ValidFromUtc, r.ValidUntilUtc, r.Reason });
        return ValidProof(Proof(companyId, detail)) && detail.SourceFactsDigest == source
            && detail.CurrentImpactDigest == IdempotencyHash.ComputePayload(new { Source = source, r.RoleCode, Permissions = detail.CurrentPermissions })
            && detail.PreviewDigest == IdempotencyHash.ComputePayload(detail with { PreviewDigest = string.Empty });
    }

    private static bool ValidProof(WorkIdentityAccessGrantProofV1? p) => p is not null && p.CompanyId != Guid.Empty
        && p.RequestId != Guid.Empty && p.RequestedBy != Guid.Empty && p.TargetUserId != Guid.Empty && p.RoleId != Guid.Empty
        && TryVersion(p.Version, out var version) && p.Status is "Pending" or "Approved" or "Rejected" or "Withdrawn"
        && (p.Status == "Pending" || version >= 2) && Digest(p.SourceFactsDigest) && Digest(p.CurrentImpactDigest) && Digest(p.PreviewDigest)
        && IdentityAccessGrantRequestWorkInboxSourceV1.DecisionItem(p) is not null;

    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
