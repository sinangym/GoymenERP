using System.Text.Json;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal sealed partial class IdentityAccessAssignmentService
{
    private sealed record AccessPurposeAttempt(string ExpectedImpactDigest, Func<CancellationToken, Task> ConsumerFence);

    public Task<IdentityAccessCommandResultV1> DecidePurposeAsync(Guid requestId, bool approve, string expectedVersion,
        string? reasonCode, string expectedImpactDigest, IdentityAccessCommandMetadataV1 metadata,
        Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumerFence);
        if (expectedImpactDigest is null || expectedImpactDigest.Length != 64
            || expectedImpactDigest.Any(x => x is not (>= 'A' and <= 'F' or >= '0' and <= '9'))
            || (approve && reasonCode is not null))
            throw new ArgumentException("Yetki kararının içerik bağı geçersiz.");
        return RunAccessApprovalAsync(
            () => approve
                ? ApproveNativeAsync(requestId, new(expectedVersion), metadata, cancellationToken)
                : RejectNativeAsync(requestId, new(expectedVersion, reasonCode!), metadata, cancellationToken),
            TryParseVersion(expectedVersion, MinimumExpectedVersion, out _)
                && (approve || AccessGrantRequest.IsCanonicalReasonCode(reasonCode!)), metadata, cancellationToken,
            new(expectedImpactDigest, consumerFence));
    }

    private object PurposeApprovedRequestImage(AccessGrantRequest request, string roleCode, IReadOnlyList<string> permissions)
    {
        if (accessApproval?.Purpose is null) return RequestImage(request);
        var image = JsonSerializer.SerializeToNode(RequestImage(request), Json)!.AsObject();
        image["appliedPermissionDigest"] = AccessDecisionImpact(request, roleCode, permissions);
        image["appliedPermissionCount"] = permissions.Count;
        image["appliedRoleCode"] = roleCode;
        return image;
    }
}
