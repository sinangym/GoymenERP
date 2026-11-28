using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    private sealed record MixedAuditAfter(JsonElement NativeOrder, PurchaseMixedOrderRecordedApprovalV1 MixedApproval);

    private async Task<PurchaseMixedOrderApprovalStoreAnswerV1?> ReadMixedOriginalAsync(Guid orderId,
        ApprovePurchaseOrderRequest request, PurchasingWriteEnvelope envelope, string hash,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var company = RequireResolvedCompany(); var key = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempts = await ReadMixedOwnerAsync(() => dbContext.Attempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == PurchasingCommands.OrderApproveV1 && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
        if (attempts.Length == 0) return null;
        if (attempts.Length != 1) throw new PurchasingServiceException(503, "service_unavailable");
        var attempt = attempts[0];
        if (attempt.ActorId != envelope.ActorId.Trim() || attempt.RequestHash != hash || attempt.OrderId is { } target && target != orderId)
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "The original mixed approval key belongs to another content.");
        if (attempt.Outcome == AttemptOutcomeRefused)
        {
            var refusal = ReadMixedRefusal(attempt.RefusalJson);
            if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
                || refusal.Errors.Any(x => x is null || string.IsNullOrWhiteSpace(x.Field) || string.IsNullOrWhiteSpace(x.Code))
                || !PurchasingPhysicalStockRefusalDetailV1.IsValid(refusal.Status, refusal.Code, refusal.Detail)
                || !MixedJsonEqual(attempt.RefusalJson!, JsonSerializer.Serialize(refusal, Json)))
                throw new PurchasingServiceException(503, "service_unavailable");
            return new(null, refusal, true, attempt.CorrelationId);
        }
        return await ReadMixedArtifactsAsync(attempt, orderId, request, envelope.ActorId.Trim(), envelope.PermissionCode, key, hash, fence, token);
    }

    private async Task<PurchaseMixedOrderApprovalStoreAnswerV1> ReadMixedArtifactsAsync(PurchaseAttemptRow attempt, Guid orderId,
        ApprovePurchaseOrderRequest request, string actor, string permission, string key, string hash,
        Func<CancellationToken, Task>? fence, CancellationToken token)
    {
        var company = RequireResolvedCompany();
        var entity = orderId.ToString("D");
        var events = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
            && x.EntityType == OrderEntityType && x.EntityId == entity && x.IdempotencyKeyHash == key
            && x.EventType == "purchasing.purchase-order.approved.v1").Take(2).ToArrayAsync(token), fence, token);
        if (events.Length != 1) throw new PurchasingServiceException(503, "service_unavailable");
        var after = ReadMixedAuditAfter(events[0].AfterJson);
        var original = after?.MixedApproval ?? throw new PurchasingServiceException(503, "service_unavailable");
        var memberships = await ReadMixedOwnerAsync(() => dbContext.OrderServiceMemberships.AsNoTracking()
            .Where(x => x.CompanyId == company && x.OrderId == orderId).ToArrayAsync(token), fence, token);
        if (original.Children is null || original.Membership is null
            || original.Children.Any(x => x is null || !MixedChildShape(x.OriginalBefore) || !MixedChildShape(x.Prepared) || !MixedChildShape(x.After))
            || original.Membership.Any(x => x is null) || memberships.Length != original.Membership.Count
            || original.Children.Count != memberships.Length || memberships.Length == 0
            || memberships.Any(x => !original.Membership.Any(m => MixedStoredMemberMatches(x, m))))
            throw new PurchasingServiceException(503, "service_unavailable");
        // Exact (company, child, native version) pairs, not a current-state read or a broad revision scan.
        var row = Expression.Parameter(typeof(PurchaseServiceOrderRevisionRow), "revision");
        Expression predicate = Expression.Constant(false);
        foreach (var child in original.Children)
            foreach (var version in new[] { child.OriginalBefore.Version, child.After.Version })
            {
                var pair = Expression.AndAlso(Expression.Equal(Expression.Property(row, nameof(PurchaseServiceOrderRevisionRow.CompanyId)), Expression.Constant(company)),
                    Expression.AndAlso(Expression.Equal(Expression.Property(row, nameof(PurchaseServiceOrderRevisionRow.OrderId)), Expression.Constant(child.OriginalBefore.OrderId)),
                        Expression.Equal(Expression.Property(row, nameof(PurchaseServiceOrderRevisionRow.Version)), Expression.Constant(version))));
                predicate = Expression.OrElse(predicate, pair);
            }
        var exact = Expression.Lambda<Func<PurchaseServiceOrderRevisionRow, bool>>(predicate, row);
        var revisions = await ReadMixedOwnerAsync(() => dbContext.ServiceOrderRevisions.AsNoTracking().Where(exact).ToArrayAsync(token), fence, token);
        var childIds = original.Children.Select(x => x.OriginalBefore.OrderId.ToString("D")).ToArray();
        var childEvents = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
            && x.EntityType == "Purchasing.ServiceOrder" && childIds.Contains(x.EntityId) && x.IdempotencyKeyHash == key
            && x.EventType == "purchasing.service-order.approve.recorded.v1").Take(childIds.Length + 1).ToArrayAsync(token), fence, token);
        var receipt = RestoreMixedOrderReceiptCore(attempt, events, revisions, childEvents, orderId, request, actor, permission, key, hash)
            ?? throw new PurchasingServiceException(503, "service_unavailable");
        return new(receipt, null, true, attempt.CorrelationId);
    }

    private async Task<PurchaseMixedOrderApprovalReceiptV1?> ReadMixedRestoreReceiptAsync(PurchaseOrderRow row,
        Func<CancellationToken, Task>? fence, CancellationToken token)
    {
        if (row.Status == (int)GoymenERP.Modules.Purchasing.Domain.Orders.PurchaseOrderStatus.Draft) return null;
        var entity = row.Id.ToString("D");
        var approvedVersion = PurchaseSupplierRevisionHistory.Read(row)?.CurrentApprovalVersion;
        var events = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == row.CompanyId
            && x.EntityType == OrderEntityType && x.EntityId == entity && x.EventType == "purchasing.purchase-order.approved.v1"
            && (approvedVersion == null || x.EntityVersionAfter == approvedVersion))
            .Take(2).ToArrayAsync(token), fence, token);
        if (events.Length != 1) throw new PurchasingServiceException(503, "service_unavailable");
        var original = ReadMixedAuditAfter(events[0].AfterJson)?.MixedApproval
            ?? throw new PurchasingServiceException(503, "service_unavailable");
        var key = events[0].IdempotencyKeyHash;
        var attempts = await ReadMixedOwnerAsync(() => dbContext.Attempts.AsNoTracking().Where(x => x.CompanyId == row.CompanyId
            && x.CommandName == PurchasingCommands.OrderApproveV1 && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
        if (attempts.Length != 1 || original.FrozenRequest is null || original.PayloadHash != attempts[0].RequestHash
            || original.ActorId != row.ApprovedBy || original.RecordedAtUtc != row.ApprovedAtUtc)
            throw new PurchasingServiceException(503, "service_unavailable");
        var answer = await ReadMixedArtifactsAsync(attempts[0], row.Id, original.FrozenRequest, original.ActorId,
            events[0].PermissionCode!, key, original.PayloadHash, fence, token);
        return answer.Receipt ?? throw new PurchasingServiceException(503, "service_unavailable");
    }

    // No current parent/child read is an original result. The caller supplies exact persisted artifacts from one owned/read-only scope.
    internal static PurchaseMixedOrderApprovalReceiptV1? RestoreMixedOrderReceipt(PurchaseAttemptRow attempt, IReadOnlyList<AuditEvent> events,
        IReadOnlyList<PurchaseServiceOrderRevisionRow> revisions, IReadOnlyList<AuditEvent> childEvents,
        Guid orderId, ApprovePurchaseOrderRequest request, PurchasingWriteEnvelope envelope, string hash)
        => RestoreMixedOrderReceiptCore(attempt, events, revisions, childEvents, orderId, request,
            envelope.ActorId.Trim(), envelope.PermissionCode, IdempotencyKeyHash(envelope.IdempotencyKey), hash);

    private static PurchaseMixedOrderApprovalReceiptV1? RestoreMixedOrderReceiptCore(PurchaseAttemptRow attempt, IReadOnlyList<AuditEvent> events,
        IReadOnlyList<PurchaseServiceOrderRevisionRow> revisions, IReadOnlyList<AuditEvent> childEvents, Guid orderId,
        ApprovePurchaseOrderRequest request, string actor, string permission, string key, string hash)
    {
        try
        {
            if (attempt.CompanyId == Guid.Empty || attempt.OrderId != orderId || orderId == Guid.Empty
                || attempt.CommandName != PurchasingCommands.OrderApproveV1 || attempt.Outcome != AttemptOutcomeRecorded
                || attempt.RefusalJson is not null || attempt.ActorId != actor
                || attempt.IdempotencyKeyHash != key || attempt.RequestHash != hash
                || string.IsNullOrWhiteSpace(attempt.CorrelationId)
                || attempt.RecordedAtUtc.Offset != TimeSpan.Zero
                || PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, orderId, request) != hash
                || events is not { Count: 1 } || events[0] is null || request.MixedServices is not { Count: > 0 }
                || request.MixedServices.Any(x => x is null || x.LineIds is null)) return null;
            var audit = events[0];
            if (!MixedAuditIdentity(audit, attempt, OrderEntityType, orderId, "purchasing.purchase-order.approved.v1", "Approve", permission)) return null;
            var after = ReadMixedAuditAfter(audit.AfterJson);
            if (after?.MixedApproval is not { } original || !MixedJsonEqual(audit.AfterJson, JsonSerializer.Serialize(after, Json))
                || original.CompanyId != attempt.CompanyId || original.OrderId != orderId || original.ActorId != attempt.ActorId
                || original.IdempotencyKeyHash != attempt.IdempotencyKeyHash || original.PayloadHash != hash
                || original.OriginalCorrelationId != attempt.CorrelationId || original.RecordedAtUtc != attempt.RecordedAtUtc
                || !MixedJsonEqual(JsonSerializer.Serialize(original.FrozenRequest, PurchasingJson.Options), JsonSerializer.Serialize(request, PurchasingJson.Options))
                || !long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var before)
                || before < 1 || before == long.MaxValue || before.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion || audit.EntityVersionBefore != before || audit.EntityVersionAfter != before + 1
                || original.Visible?.Order is not { } header || original.Withheld?.Order is not { } hidden
                || header.Id != orderId || hidden.Id != orderId || header.CompanyId != attempt.CompanyId || hidden.CompanyId != attempt.CompanyId
                || header.Version != (before + 1).ToString(CultureInfo.InvariantCulture) || hidden.Version != header.Version
                || header.Status != "Approved" || hidden.Status != "Approved" || header.UpdatedAtUtc != attempt.RecordedAtUtc
                || original.Visible.PriceDisclosure != PurchasingPriceDisclosureV1.Visible
                || original.Withheld.PriceDisclosure != PurchasingPriceDisclosureV1.Withheld
                || !MixedJsonEqual(JsonSerializer.Serialize(header, PurchasingJson.Options), JsonSerializer.Serialize(hidden, PurchasingJson.Options))
                || original.Visible.Lines is not { Count: > 0 } || original.Withheld.Lines is null
                || original.Visible.Lines.Any(x => x is null)
                || original.Visible.Lines.Count != header.LineCount || original.Withheld.Lines.Count != header.LineCount
                || original.Withheld.PricePending is not null || original.Withheld.KgPrice is not null || original.Withheld.Realization is not null
                || original.Withheld.Lines.Any(x => x is null || x.Price is not null || x.KgPrice is not null || x.Realization is not null)
                || MixedRequiredField(after.NativeOrder, "preparedBy").GetString() is not { Length: > 0 } maker || maker == actor
                || MixedRequiredField(JsonSerializer.Deserialize<JsonElement>(audit.BeforeJson), "version").GetInt64() != before
                || MixedRequiredField(JsonSerializer.Deserialize<JsonElement>(audit.BeforeJson), "status").GetString() != "Draft"
                || MixedRequiredField(after.NativeOrder, "lines").GetArrayLength() != header.LineCount
                || MixedRequiredField(after.NativeOrder, "version").GetInt64() != before + 1
                || MixedRequiredField(after.NativeOrder, "status").GetString() != "Approved"
                || MixedRequiredField(MixedRequiredField(after.NativeOrder, "approval"), "approvedBy").GetString() != attempt.ActorId
                || MixedRequiredField(MixedRequiredField(after.NativeOrder, "approval"), "approvedAtUtc").GetDateTimeOffset() != attempt.RecordedAtUtc
                || original.Children is not { Count: > 0 } || original.Membership is null
                || original.Children.Any(x => x is null || !MixedChildShape(x.OriginalBefore) || !MixedChildShape(x.Prepared) || !MixedChildShape(x.After))
                || original.Membership.Any(x => x is null)
                || original.Children.Count != original.Membership.Count || request.MixedServices.Count != original.Children.Count
                || original.Children.Select(x => x.OriginalBefore.OrderId).Distinct().Count() != original.Children.Count
                || original.Membership.Select(x => x.ServiceOrderId).Distinct().Count() != original.Children.Count
                || request.MixedServices.Select(x => x.ServiceOrderId).Distinct().Count() != original.Children.Count
                || revisions.Count != original.Children.Count * 2 || childEvents.Count != original.Children.Count) return null;
            var masked = original.Visible with { PriceDisclosure = PurchasingPriceDisclosureV1.Withheld, PricePending = null,
                KgPrice = null, Realization = null, Lines = original.Visible.Lines.Select(x => x with { Price = null, KgPrice = null, Realization = null }).ToArray() };
            if (!MixedJsonEqual(JsonSerializer.Serialize(masked, PurchasingJson.Options), JsonSerializer.Serialize(original.Withheld, PurchasingJson.Options))
                || MixedRequiredField(after.NativeOrder, "supplierPartyId").GetGuid() != header.SupplierPartyId
                || (MixedRequiredField(after.NativeOrder, "sourceRequestId").ValueKind == JsonValueKind.Null
                    ? header.SourceRequestId is not null : header.SourceRequestId != MixedRequiredField(after.NativeOrder, "sourceRequestId").GetGuid())
                || original.Visible.Currency is not { } currency || currency.CurrencyId != MixedRequiredField(MixedRequiredField(after.NativeOrder, "currency"), "currencyId").GetGuid()
                || currency.CurrencyCode != MixedRequiredField(MixedRequiredField(after.NativeOrder, "currency"), "currencyCode").GetString()
                || currency.Scale != MixedRequiredField(MixedRequiredField(after.NativeOrder, "currency"), "scale").GetInt32()
                || original.Membership.Any(x => x.SupplierPartyId != header.SupplierPartyId || x.CompanyAccountId != header.CompanyAccountId
                    || x.CurrencyId != MixedRequiredField(MixedRequiredField(after.NativeOrder, "currency"), "currencyId").GetGuid())
                || !original.Visible.Lines.Select(x => x.LineId).ToHashSet().SetEquals(MixedRequiredField(after.NativeOrder, "lines").EnumerateArray().Select(x => MixedRequiredField(x, "lineId").GetGuid()))) return null;
            foreach (var child in original.Children)
            {
                var old = child.OriginalBefore; var prepared = child.Prepared; var next = child.After;
                PurchaseServiceOrder.Validate(old); PurchaseServiceOrder.Validate(prepared); PurchaseServiceOrder.Validate(next);
                var selected = request.MixedServices.SingleOrDefault(x => x.ServiceOrderId == old.OrderId);
                var membership = original.Membership.SingleOrDefault(x => x.ServiceOrderId == old.OrderId);
                if (selected is null || membership is null || old.CompanyId != attempt.CompanyId || membership.CompanyId != old.CompanyId
                    || membership.PurchaseOrderId != orderId || old.SupplierPartyId != membership.SupplierPartyId
                    || old.CompanyAccountId != membership.CompanyAccountId || old.Money.CurrencyId != membership.CurrencyId
                    || selected.ExpectedVersion != old.Version.ToString(CultureInfo.InvariantCulture)
                    || selected.LineIds is null || selected.LineIds.Count != old.Lines.Count
                    || selected.LineIds.Distinct().Count() != selected.LineIds.Count
                    || !selected.LineIds.ToHashSet().SetEquals(old.Lines.Select(x => x.LineId))
                    || !PurchaseMixedOrderApprovalFactsV1.SameChild(old, prepared, allowSupplierRefresh: true)
                    || !PurchaseMixedOrderApprovalFactsV1.SameChild(PurchaseServiceOrder.Approve(prepared, old.Version, attempt.ActorId, attempt.RecordedAtUtc), next)) return null;
                foreach (var expected in new[] { old, next })
                {
                    var stored = revisions.Where(x => x.CompanyId == expected.CompanyId && x.OrderId == expected.OrderId && x.Version == expected.Version).ToArray();
                    if (stored.Length != 1 || !MixedJsonEqual(stored[0].SnapshotJson, JsonSerializer.Serialize(expected, PurchaseServiceOrderStoreV1.Json))) return null;
                }
                var audits = childEvents.Where(x => x.EntityId == old.OrderId.ToString("D")).ToArray();
                if (audits.Length != 1 || !MixedAuditIdentity(audits[0], attempt, "Purchasing.ServiceOrder", old.OrderId,
                    PurchaseServiceOrderCommandsV1.Approve.Replace(".v1", ".recorded.v1", StringComparison.Ordinal), PurchaseServiceOrderCommandsV1.Approve, permission)
                    || audits[0].EntityVersionBefore != old.Version || audits[0].EntityVersionAfter != next.Version
                    || !MixedJsonEqual(audits[0].BeforeJson, JsonSerializer.Serialize(old, PurchaseServiceOrderStoreV1.Json))
                    || !MixedJsonEqual(audits[0].AfterJson, JsonSerializer.Serialize(next, PurchaseServiceOrderStoreV1.Json))) return null;
            }
            return new(attempt.CompanyId, orderId, attempt.ActorId, attempt.IdempotencyKeyHash, hash, attempt.CorrelationId,
                before, before + 1, attempt.RecordedAtUtc, audit.Id, audit.IntegrityHash, original);
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or KeyNotFoundException or PurchaseServiceOrderException)
        { return null; }
    }

    private static JsonElement MixedRequiredField(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var field))
            throw new InvalidOperationException("The original mixed receipt is missing a required field.");
        return field;
    }

    private static bool MixedChildShape(PurchaseServiceOrderState? value) => value is not null && value.Money is not null
        && value.Lines is not null && value.Materials is not null && value.ExpectedOutputs is not null && value.Acceptances is not null
        && value.Lines.All(x => x is not null) && value.Materials.All(x => x is not null)
        && value.ExpectedOutputs.All(x => x is not null) && value.Acceptances.All(x => x is not null);
    private static MixedAuditAfter? ReadMixedAuditAfter(string json)
    { try { return JsonSerializer.Deserialize<MixedAuditAfter>(json, Json); } catch (JsonException) { return null; } }
    private static PurchasingTerminalRefusal? ReadMixedRefusal(string? json)
    { try { return json is null ? null : JsonSerializer.Deserialize<PurchasingTerminalRefusal>(json, Json); } catch (JsonException) { return null; } }
    private static bool MixedStoredMemberMatches(PurchaseOrderServiceMembershipRow row, GoymenERP.Modules.Purchasing.Domain.Orders.PurchaseOrderServiceMembership expected)
    { try { return MixedJsonEqual(JsonSerializer.Serialize(expected, Json), JsonSerializer.Serialize(ServiceMembershipOf(row), Json)); }
      catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException) { return false; } }

    private static bool MixedAuditIdentity(AuditEvent audit, PurchaseAttemptRow attempt, string entityType, Guid entityId,
        string eventType, string operation, string permission) => audit.Id != Guid.Empty && audit.CompanyId == attempt.CompanyId
        && audit.SubjectKind == AuditEvent.CompanySubject && audit.EntityType == entityType && audit.EntityId == entityId.ToString("D")
        && audit.EventType == eventType && audit.Operation == operation && audit.ActorType == AuditActorTypes.User
        && audit.ActorId == attempt.ActorId && audit.PermissionCode == permission && audit.Outcome == AuditOutcomes.Succeeded
        && audit.CorrelationId == attempt.CorrelationId && audit.IdempotencyKeyHash == attempt.IdempotencyKeyHash
        && audit.SchemaVersion == 1 && audit.IntegrityHash == AuditEventIntegrity.Compute(audit.ToIntegrityEnvelope());
    private static bool MixedJsonEqual(string left, string right) => JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));
}
