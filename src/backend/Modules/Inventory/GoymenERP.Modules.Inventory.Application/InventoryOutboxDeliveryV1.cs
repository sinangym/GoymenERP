using System.Security.Cryptography;
using System.Text;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// The first registered, SQL-backed consumer of Inventory's immutable definite-shipment event.
/// This contract does not activate a hosted worker and deliberately has no timing defaults.
/// </summary>
public static class InventoryOutboxDeliveryV1
{
    public const string ConsumerName = "work.shipment-evidence.v1";
    public const string PayloadHashAlgorithm = "SHA-256";

    public static string PayloadSha256(string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))).ToLowerInvariant();
    }

    public static void ValidateClaimRequest(InventoryOutboxDeliveryClaimRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId == Guid.Empty || request.ClaimId == Guid.Empty)
        {
            throw new ArgumentException("A company and claim identity are required.", nameof(request));
        }

        if (!string.Equals(request.ConsumerName, ConsumerName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The outbox consumer is not registered.", nameof(request));
        }

        if (request.ClaimedAtUtc.Offset != TimeSpan.Zero
            || request.ClaimExpiresAtUtc.Offset != TimeSpan.Zero
            || request.ClaimExpiresAtUtc <= request.ClaimedAtUtc)
        {
            throw new ArgumentException("An explicit positive UTC claim window is required.", nameof(request));
        }
    }

    public static void ValidateCompletion(InventoryOutboxDeliveryCompletionV1 completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (completion.CompanyId == Guid.Empty || completion.MessageId == Guid.Empty
            || completion.ClaimId == Guid.Empty || completion.Fence <= 0)
        {
            throw new ArgumentException("Completion identity and fencing are required.", nameof(completion));
        }

        if (!string.Equals(completion.ConsumerName, ConsumerName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The outbox consumer is not registered.", nameof(completion));
        }

        if (completion.CompletedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Completion time must be UTC.", nameof(completion));
        }
    }

    public static void ValidateClaim(InventoryOutboxDeliveryClaimV1 claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ValidateClaimRequest(new InventoryOutboxDeliveryClaimRequestV1(
            claim.CompanyId,
            claim.ConsumerName,
            claim.ClaimId,
            claim.ClaimedAtUtc,
            claim.ClaimExpiresAtUtc));

        if (claim.MessageId == Guid.Empty || claim.AggregateId == Guid.Empty
            || claim.AggregateVersion <= 0 || claim.Sequence <= 0 || claim.Fence <= 0)
        {
            throw new ArgumentException("The claimed event envelope is incomplete.", nameof(claim));
        }

        if (!string.Equals(claim.EventType, InventoryDefiniteShipmentOutboxV1.EventType, StringComparison.Ordinal)
            || claim.SchemaVersion != InventoryDefiniteShipmentOutboxV1.SchemaVersion
            || !string.Equals(claim.SourceOperation, InventoryDefiniteShipmentOutboxV1.SourceOperation,
                StringComparison.Ordinal)
            || !string.Equals(claim.AggregateType, InventoryDefiniteShipmentOutboxV1.AggregateType,
                StringComparison.Ordinal)
            || claim.AggregateVersion != claim.Sequence)
        {
            throw new ArgumentException("The claimed event is outside the admitted source contract.", nameof(claim));
        }

        if (claim.OccurredAtUtc.Offset != TimeSpan.Zero || claim.RecordedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(claim.CorrelationId)
            || !string.Equals(claim.CorrelationId, claim.CorrelationId.Trim(), StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(claim.PayloadJson)
            || !string.Equals(PayloadSha256(claim.PayloadJson), claim.PayloadSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The claimed event payload or correlation facts are invalid.", nameof(claim));
        }
    }
}

public sealed record InventoryOutboxDeliveryClaimRequestV1(
    Guid CompanyId,
    string ConsumerName,
    Guid ClaimId,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ClaimExpiresAtUtc);

public sealed record InventoryOutboxDeliveryClaimV1(
    Guid MessageId,
    Guid CompanyId,
    string ConsumerName,
    string EventType,
    int SchemaVersion,
    string SourceOperation,
    string AggregateType,
    Guid AggregateId,
    long AggregateVersion,
    long Sequence,
    string PayloadJson,
    string PayloadSha256,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset RecordedAtUtc,
    long Fence,
    Guid ClaimId,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ClaimExpiresAtUtc);

public sealed record InventoryOutboxDeliveryCompletionV1(
    Guid CompanyId,
    string ConsumerName,
    Guid MessageId,
    long Fence,
    Guid ClaimId,
    DateTimeOffset CompletedAtUtc);

public enum InventoryOutboxDeliveryCompletionDecisionV1
{
    Completed = 1,
    Replay = 2,
    StaleClaim = 3,
    Refused = 4
}

public interface IInventoryOutboxDeliveryStoreV1
{
    Task<InventoryOutboxDeliveryClaimV1?> ClaimNextAsync(
        InventoryOutboxDeliveryClaimRequestV1 request,
        CancellationToken cancellationToken = default);

    Task<InventoryOutboxDeliveryCompletionDecisionV1> CompleteAsync(
        InventoryOutboxDeliveryCompletionV1 completion,
        CancellationToken cancellationToken = default);
}
