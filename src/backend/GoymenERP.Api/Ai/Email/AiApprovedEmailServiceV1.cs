using System.Security.Cryptography;
using System.Text.Json;

namespace GoymenERP.Api.Ai.Email;

/// <summary>C7 orchestration only. No registration, provider, persistent ledger, scheduler or send implementation.</summary>
public sealed class AiApprovedEmailServiceV1(IAiApprovedEmailOwnerV1 owner, IAiEmailAuthorityV1 authority,
    AiEmailRecipientResolverV1 recipients)
{
    // Reuses the command pipeline's concrete-effect identity principle, without admitting email as RecordChange.
    public static string Digest(AiEmailScopeV1 scope, IReadOnlyList<AiEmailRecipientV1> targets, AiEmailContentV1 content) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            scope.CompanyId, scope.ActorId, scope.SessionId,
            Recipients = targets.OrderBy(x => x.PartyId).ThenBy(x => x.AccountId)
                .ThenBy(x => x.Address, StringComparer.Ordinal).ToArray(), Content = content
        })));

    public async Task<AiEmailReceiptV1> ExecuteApprovedAsync(AiEmailApprovalV1 approval,
        CancellationToken cancellationToken = default)
    {
        // Snapshot the caller's collection before the first await; subsequent mutation cannot alter the approved effect.
        approval = approval with { Recipients = approval.Recipients.ToArray() };
        if (!Valid(approval)) return Result(approval, AiEmailStateV1.Refused);
        if (!owner.IsAvailable) return Result(approval, AiEmailStateV1.Unavailable);
        if (!await authority.IsCurrentAsync(approval.Scope, cancellationToken)) return Result(approval, AiEmailStateV1.Refused);
        var prior = await owner.FindAsync(approval.Scope, approval.OperationId, cancellationToken);
        if (!await authority.IsCurrentAsync(approval.Scope, cancellationToken)) return Result(approval, AiEmailStateV1.Refused);
        // A durable Unknown/Queued/Delivered receipt never re-enters enqueue or rebinds to newer contact data.
        if (prior is not null) return Bound(approval, prior) ? prior : Result(approval, AiEmailStateV1.Refused);
        if (!await recipients.MatchesAsync(approval.Scope, approval.Recipients, cancellationToken)
            || !await authority.IsCurrentAsync(approval.Scope, cancellationToken)) return Result(approval, AiEmailStateV1.Refused);
        try
        {
            var receipt = await owner.EnqueueApprovedOnceAsync(approval, cancellationToken);
            if (!await authority.IsCurrentAsync(approval.Scope, cancellationToken)) return Result(approval, AiEmailStateV1.Unknown);
            return Bound(approval, receipt) ? receipt : Result(approval, AiEmailStateV1.Unknown);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No automatic resend. The caller must retain operation/digest and use ReconcileAsync.
            return Result(approval, AiEmailStateV1.Unknown);
        }
    }

    public async Task<AiEmailReceiptV1> ReconcileAsync(AiEmailApprovalV1 approval, CancellationToken cancellationToken = default)
    {
        // Snapshot the caller's collection before the first await; subsequent mutation cannot alter the approved effect.
        approval = approval with { Recipients = approval.Recipients.ToArray() };
        if (!Valid(approval)) return Result(approval, AiEmailStateV1.Refused);
        if (!owner.IsAvailable) return Result(approval, AiEmailStateV1.Unavailable);
        if (!await authority.IsCurrentAsync(approval.Scope, cancellationToken)) return Result(approval, AiEmailStateV1.Refused);
        var receipt = await owner.FindAsync(approval.Scope, approval.OperationId, cancellationToken);
        if (!await authority.IsCurrentAsync(approval.Scope, cancellationToken)) return Result(approval, AiEmailStateV1.Refused);
        return receipt is not null && Bound(approval, receipt) ? receipt : Result(approval, AiEmailStateV1.Unknown);
    }

    private static bool Valid(AiEmailApprovalV1 value) => value.OperationId != Guid.Empty
        && value.Scope.CompanyId != Guid.Empty && value.Scope.ActorId != Guid.Empty
        && !string.IsNullOrWhiteSpace(value.Scope.SessionId) && value.Recipients.Count > 0
        && value.Recipients.All(x => x.PartyId != Guid.Empty && x.AccountId != Guid.Empty
            && !string.IsNullOrWhiteSpace(x.AccountVersion) && !string.IsNullOrWhiteSpace(x.Address))
        && !string.IsNullOrWhiteSpace(value.Content.Subject) && !string.IsNullOrWhiteSpace(value.Content.Body)
        && string.Equals(value.Digest, Digest(value.Scope, value.Recipients, value.Content), StringComparison.Ordinal);
    private static bool Bound(AiEmailApprovalV1 approval, AiEmailReceiptV1 receipt) =>
        receipt.OperationId == approval.OperationId && receipt.CompanyId == approval.Scope.CompanyId
        && receipt.Digest == approval.Digest && Enum.IsDefined(receipt.State);
    private static AiEmailReceiptV1 Result(AiEmailApprovalV1 approval, AiEmailStateV1 state) =>
        new(approval.OperationId, approval.Scope.CompanyId, approval.Digest, state);
}
