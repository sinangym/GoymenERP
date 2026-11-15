using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// F-017 STOK §3.2-§3.4 (F017-b): the rules the writer and the release port share — metadata checks, hashes, the row→view map,
/// the release step (allocation + ledger in place), the audit draft and the outbox rows. It never opens a transaction or saves:
/// the caller owns both.
/// </summary>
internal static class InventoryFutureAllocationEngine
{
    internal const string EntityType = InventoryFutureAllocationCreatedOutboxV1.AggregateType;
    internal const string CreatedOperation = "Create";
    internal const string ReleasedOperation = "Release";

    private const int MetadataMaximumLength = 128;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static InventoryAllocationException Error(int status, string code, Exception? inner = null) =>
        new(status, code, innerException: inner);

    /// <summary>Every write names actor, permission, roles, correlation, idempotency key and channel (programming error otherwise).</summary>
    internal static void RequireMetadata(InventoryCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.ActorId) || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || metadata.RoleCodes is null
            || metadata.ActorId.Trim().Length > MetadataMaximumLength
            || metadata.CorrelationId.Trim().Length > MetadataMaximumLength)
        {
            throw new ArgumentException("Stok yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(metadata));
        }
    }

    /// <summary>The idempotency key and the correlation id are stored exactly as given: no surrounding space, no control character.</summary>
    internal static void RequireCanonicalKeys(InventoryCommandMetadata metadata)
    {
        if (!string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > MetadataMaximumLength || metadata.IdempotencyKey.Any(char.IsControl)
            || !string.Equals(metadata.CorrelationId, metadata.CorrelationId.Trim(), StringComparison.Ordinal)
            || metadata.CorrelationId.Length > MetadataMaximumLength
            || (metadata.CausationId is not null && metadata.CausationId.Length > MetadataMaximumLength))
        {
            throw Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        }
    }

    internal static string KeyHash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim())));

    internal static string PayloadHash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))))
            .ToLowerInvariant();

    /// <summary>A release request's own checks (shape only); the allocation's state is checked by the domain afterwards.</summary>
    internal static (FutureSupplyQuantity? Quantity, string ReasonCode, string ReasonText) ParseRelease(string? quantityText,
        string? reasonCode, string? reasonText)
    {
        FutureSupplyQuantity? quantity = null;
        if (quantityText is not null)
        {
            if (!InventoryFutureAllocationTextV1.TryPositiveQuantity(quantityText, out var parsed))
            {
                throw Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
            }

            quantity = parsed;
        }

        if (!InventoryFutureAllocationReleaseReasonsV1.IsWellFormed(reasonCode))
        {
            throw Error(400, InventoryFutureAllocationCodesV1.ValidationFailed);
        }

        try
        {
            return (quantity, reasonCode!, FutureSupplyAllocation.RequireReason(reasonText));
        }
        catch (FutureSupplyRuleException exception)
        {
            throw Error(400, InventoryFutureAllocationCodesV1.ValidationFailed, exception);
        }
    }

    /// <summary>Rebuilds the stored allocation; a stored row that breaks the rules is a technical failure (503), never repaired.</summary>
    internal static FutureSupplyAllocation RestoreStored(FutureSupplyAllocationRow row)
    {
        try
        {
            return FutureSupplyAllocation.Restore(row.Id, row.CompanyId, FutureSupplyQuantity.Of(row.AllocatedQuantity),
                FutureSupplyQuantity.Of(row.RemainingQuantity), FutureSupplyQuantity.Of(row.ReleasedQuantity),
                (FutureSupplyAllocationStatus)row.Status, row.Version);
        }
        catch (Exception exception) when (exception is ArgumentException or FutureSupplyRuleException)
        {
            throw Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, exception);
        }
    }

    internal static InventoryFutureAllocationViewV1 View(FutureSupplyAllocationRow row)
    {
        var allocation = RestoreStored(row);
        return new InventoryFutureAllocationViewV1(
            row.Id,
            row.CompanyId,
            row.Version.ToString(CultureInfo.InvariantCulture),
            allocation.Status.ToString(),
            row.DemandId,
            row.DemandLineId,
            row.PurchaseOrderId,
            row.PurchaseOrderLineId,
            row.ProductId,
            row.ProductVersion,
            row.PurchaseUnitId,
            allocation.Allocated.ToString(),
            allocation.Remaining.ToString(),
            allocation.Released.ToString(),
            allocation.Consumed.ToString(),
            row.ManualSequence,
            row.Reason,
            row.ReleaseReasonCode,
            row.ReleaseOrigin,
            row.ReleaseReason,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.ReleasedAtUtc);
    }

    /// <summary>
    /// F-017 STOK §3.3/§3.4: releases part (or all) of one TRACKED allocation row in place and returns the quantity to the purchase
    /// line ledger in the same change set. The id, creation time and manual sequence stay; the version steps by one. Domain
    /// refusals map to stable codes; a ledger that cannot take the quantity back is inconsistent (503, nothing written).
    /// </summary>
    internal static async Task<ReleaseStep> ReleaseTrackedAsync(
        InventoryDbContext context,
        FutureSupplyAllocationRow row,
        long expectedVersion,
        FutureSupplyQuantity? quantity,
        string reasonCode,
        string origin,
        string reasonText,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var allocation = RestoreStored(row);
        var before = View(row);
        var versionBefore = row.Version;
        FutureSupplyQuantity released;
        try
        {
            released = allocation.Release(expectedVersion, quantity);
        }
        catch (FutureSupplyRuleException exception)
        {
            throw exception.Failure switch
            {
                FutureSupplyRuleFailure.StateConflict => Error(409, InventoryFutureAllocationCodesV1.StateConflict, exception),
                FutureSupplyRuleFailure.VersionConflict => Error(409, InventoryFutureAllocationCodesV1.VersionConflict, exception),
                FutureSupplyRuleFailure.ReleaseExceedsRemaining or FutureSupplyRuleFailure.QuantityNotPositive =>
                    Error(422, InventoryFutureAllocationCodesV1.ReleaseSelectionInvalid, exception),
                _ => Error(400, InventoryFutureAllocationCodesV1.ValidationFailed, exception),
            };
        }

        var ledgerRow = await context.FutureSupplyLines
            .SingleOrDefaultAsync(ledger => ledger.PurchaseOrderLineId == row.PurchaseOrderLineId, cancellationToken)
            ?? throw Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        if (ledgerRow.PurchaseOrderId != row.PurchaseOrderId || ledgerRow.PurchaseUnitId != row.PurchaseUnitId)
        {
            throw Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable);
        }

        FutureSupplyLedger ledger;
        try
        {
            ledger = FutureSupplyLedger.Restore(ledgerRow.CompanyId, ledgerRow.PurchaseOrderLineId,
                FutureSupplyQuantity.Of(ledgerRow.AllocatableSnapshot), FutureSupplyQuantity.Of(ledgerRow.ActiveAllocated),
                ledgerRow.Version);
            ledger.Return(released);
        }
        catch (Exception exception) when (exception is ArgumentException or FutureSupplyRuleException)
        {
            throw Error(503, InventoryFutureAllocationCodesV1.ServiceUnavailable, exception);
        }

        ledgerRow.ActiveAllocated = ledger.ActiveAllocated.Value;
        ledgerRow.Version = ledger.Version;
        ledgerRow.UpdatedAtUtc = now;

        row.RemainingQuantity = allocation.Remaining.Value;
        row.ReleasedQuantity = allocation.Released.Value;
        row.Status = (int)allocation.Status;
        row.ReleaseReasonCode = reasonCode;
        row.ReleaseOrigin = origin;
        row.ReleaseReason = reasonText;
        row.Version = allocation.Version;
        row.UpdatedAtUtc = now;
        row.ReleasedAtUtc = now;
        return new ReleaseStep(before, View(row), released, versionBefore);
    }

    internal static void AddCreatedOutbox(InventoryDbContext context, FutureSupplyAllocationRow row,
        InventoryCommandMetadata metadata, DateTimeOffset now)
    {
        var payload = new InventoryFutureAllocationCreatedV1(
            row.CompanyId,
            row.Id,
            row.Version.ToString(CultureInfo.InvariantCulture),
            row.DemandId,
            row.DemandLineId,
            row.PurchaseOrderId,
            row.PurchaseOrderLineId,
            row.ProductId,
            row.ProductVersion,
            row.PurchaseUnitId,
            FutureSupplyQuantity.Of(row.AllocatedQuantity).ToString(),
            metadata.CorrelationId,
            metadata.CausationId);
        context.OutboxMessages.Add(new InventoryOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = row.CompanyId,
            EventType = InventoryFutureAllocationCreatedOutboxV1.EventType,
            SchemaVersion = InventoryFutureAllocationCreatedOutboxV1.SchemaVersion,
            SourceOperation = InventoryFutureAllocationCreatedOutboxV1.SourceOperation,
            AggregateType = InventoryFutureAllocationCreatedOutboxV1.AggregateType,
            AggregateId = row.Id,
            AggregateVersion = row.Version,
            Sequence = row.Version,
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            OccurredAtUtc = now,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            RecordedAtUtc = now,
        });
    }

    internal static void AddReleasedOutbox(InventoryDbContext context, FutureSupplyAllocationRow row, ReleaseStep step,
        InventoryCommandMetadata metadata, DateTimeOffset now)
    {
        var payload = new InventoryFutureAllocationReleasedV1(
            row.CompanyId,
            row.Id,
            row.Version.ToString(CultureInfo.InvariantCulture),
            row.DemandId,
            row.DemandLineId,
            row.PurchaseOrderId,
            row.PurchaseOrderLineId,
            row.PurchaseUnitId,
            step.Released.ToString(),
            step.After.RemainingQuantity,
            step.After.Status,
            row.ReleaseReasonCode!,
            row.ReleaseReason!,
            row.ReleaseOrigin!,
            metadata.CorrelationId,
            metadata.CausationId);
        context.OutboxMessages.Add(new InventoryOutboxRow
        {
            Id = Guid.NewGuid(),
            CompanyId = row.CompanyId,
            EventType = InventoryFutureAllocationReleasedOutboxV1.EventType,
            SchemaVersion = InventoryFutureAllocationReleasedOutboxV1.SchemaVersion,
            SourceOperation = InventoryFutureAllocationReleasedOutboxV1.SourceOperation,
            AggregateType = InventoryFutureAllocationReleasedOutboxV1.AggregateType,
            AggregateId = row.Id,
            AggregateVersion = row.Version,
            Sequence = row.Version,
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            OccurredAtUtc = now,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            RecordedAtUtc = now,
        });
    }

    internal static void Audit(IAuditEventCollector auditEvents, InventoryCommandMetadata metadata, Guid companyId, Guid entityId,
        string eventType, string operation, long? versionBefore, long? versionAfter, string before, string after, string reason)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventType,
            EntityType,
            entityId.ToString("D"),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            before,
            after,
            reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));
    }

    /// <summary>What one release changed: the views before and after, the released quantity and the version it started from.</summary>
    internal sealed record ReleaseStep(
        InventoryFutureAllocationViewV1 Before,
        InventoryFutureAllocationViewV1 After,
        FutureSupplyQuantity Released,
        long VersionBefore);
}
