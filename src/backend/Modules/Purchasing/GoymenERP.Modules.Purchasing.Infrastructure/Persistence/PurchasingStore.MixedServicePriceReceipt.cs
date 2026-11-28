using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    private async Task<PurchaseMixedServicePriceAnswerV1?> ReadMixedServicePriceOriginalAsync(Guid id,
        RevisePurchaseMixedServicePricesRequest request, PurchasingWriteEnvelope envelope, string hash,
        Func<CancellationToken, Task>? fence, CancellationToken token)
    {
        var company = RequireResolvedCompany(); var key = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempts = await ReadMixedOwnerAsync(() => dbContext.Attempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == PurchaseMixedServicePriceIntentV1.Command && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
        if (attempts.Length == 0) return null;
        if (attempts.Length != 1) throw MixedSupplierUnavailable();
        if (attempts[0].ActorId != envelope.ActorId.Trim() || attempts[0].RequestHash != hash || attempts[0].OrderId is { } target && target != id)
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Original price revision belongs to another actor/content.");
        if (attempts[0].Outcome == AttemptOutcomeRefused)
        {
            var refusal = ReadMixedRefusal(attempts[0].RefusalJson);
            if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
                || refusal.Errors.Any(x => x is null || string.IsNullOrWhiteSpace(x.Field) || string.IsNullOrWhiteSpace(x.Code))
                || !PurchasingPhysicalStockRefusalDetailV1.IsValid(refusal.Status, refusal.Code, refusal.Detail)
                || !MixedJsonEqual(attempts[0].RefusalJson!, JsonSerializer.Serialize(refusal, Json))) throw MixedSupplierUnavailable();
            return new(null, true, refusal, attempts[0].CorrelationId);
        }
        return new(await ReadMixedServicePriceArtifactsAsync(attempts[0], id, request, envelope.PermissionCode, fence, token, long.MaxValue), true);
    }

    private async Task<PurchaseMixedServicePriceReceiptV1> ReadMixedServicePriceVersionAsync(Guid id, long resultVersion,
        Func<CancellationToken, Task>? fence, CancellationToken token, long ceiling)
    {
        if (resultVersion < 1 || resultVersion > ceiling) throw MixedSupplierUnavailable();
        var company = RequireResolvedCompany(); var entity = id.ToString("D");
        var events = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
            && x.EntityType == OrderEntityType && x.EntityId == entity && x.EventType == PurchaseMixedServicePriceIntentV1.Event
            && x.EntityVersionAfter == resultVersion).Take(2).ToArrayAsync(token), fence, token);
        if (events.Length != 1) throw MixedSupplierUnavailable();
        var receipt = ReadMixedServicePriceAfter(events[0].AfterJson)?.PriceRevision ?? throw MixedSupplierUnavailable();
        var key = events[0].IdempotencyKeyHash;
        var attempts = await ReadMixedOwnerAsync(() => dbContext.Attempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == PurchaseMixedServicePriceIntentV1.Command && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
        if (attempts.Length != 1) throw MixedSupplierUnavailable();
        return await ReadMixedServicePriceArtifactsAsync(attempts[0], id, receipt.FrozenRequest, events[0].PermissionCode!, fence, token, ceiling);
    }

    private async Task<PurchaseMixedServicePriceReceiptV1> ReadMixedServicePriceArtifactsAsync(PurchaseAttemptRow attempt, Guid id,
        RevisePurchaseMixedServicePricesRequest mutable, string permission, Func<CancellationToken, Task>? fence, CancellationToken token, long ceiling)
    {
        try
        {
            var frozen = PurchaseMixedServicePriceIntentV1.Freeze(mutable);
            var expected = PurchaseMixedOrderApprovalIntentV1.Version(frozen.ExpectedVersion); var company = RequireResolvedCompany();
            if (expected + 1 > ceiling || attempt.CompanyId != company || attempt.OrderId != id || attempt.RequestId is not null
                || attempt.CommandName != PurchaseMixedServicePriceIntentV1.Command || attempt.Outcome != AttemptOutcomeRecorded
                || attempt.RefusalJson is not null || attempt.RecordedAtUtc.Offset != TimeSpan.Zero
                || PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, id, frozen) != attempt.RequestHash)
                throw MixedSupplierUnavailable();
            var entity = id.ToString("D"); var key = attempt.IdempotencyKeyHash;
            var events = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
                && x.EntityType == OrderEntityType && x.EntityId == entity && x.EventType == PurchaseMixedServicePriceIntentV1.Event
                && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
            if (events.Length != 1 || !MixedAuditIdentity(events[0], attempt, OrderEntityType, id,
                PurchaseMixedServicePriceIntentV1.Event, "ReviseServicePrices", permission)) throw MixedSupplierUnavailable();
            var audit = events[0]; var after = ReadMixedServicePriceAfter(audit.AfterJson);
            if (after?.PriceRevision is not { } receipt || !MixedJsonEqual(audit.AfterJson, JsonSerializer.Serialize(after, Json))
                || receipt.CompanyId != company || receipt.OrderId != id || receipt.ActorId != attempt.ActorId || receipt.KeyHash != key
                || receipt.PayloadHash != attempt.RequestHash || receipt.OriginalCorrelationId != attempt.CorrelationId
                || receipt.RecordedAtUtc != attempt.RecordedAtUtc || receipt.ExpectedVersion != expected || receipt.ResultVersion != expected + 1
                || audit.EntityVersionBefore != expected || audit.EntityVersionAfter != expected + 1 || receipt.NativeRevision is not { } revision
                || revision.Kind != PurchaseMixedServicePriceIntentV1.Command || revision.ExpectedVersion != expected || revision.OrderVersionAfter != expected + 1 || revision.PreparedBy != attempt.ActorId
                || revision.RecordedAtUtc != attempt.RecordedAtUtc || revision.Reason != frozen.Reason.Trim()
                || revision.PreviousApproval is null || revision.PreviousApprovalVersion < 1 || revision.PreviousApprovalVersion > expected
                || !MixedJsonEqual(JsonSerializer.Serialize(receipt.FrozenRequest, PurchasingJson.Options), JsonSerializer.Serialize(frozen, PurchasingJson.Options))
                || receipt.Children is not { Count: > 0 } || receipt.Children.Count != frozen.Services.Count
                || receipt.Children.Any(x => x is null || !MixedChildShape(x.Before) || !MixedChildShape(x.After))
                || receipt.Children.Select(x => x.Before.OrderId).Distinct().Count() != receipt.Children.Count) throw MixedSupplierUnavailable();
            var previous = await ReadMixedApprovalVersionAsync(id, revision.PreviousApprovalVersion, fence, token, expected);
            if (previous.ActorId != revision.PreviousApproval.ApprovedBy || previous.RecordedAtUtc != revision.PreviousApproval.ApprovedAtUtc
                || previous.Original.Children.Count != receipt.Children.Count || receipt.Children.Any(x => !HasNativeApproval(x.Before, previous)))
                throw MixedSupplierUnavailable();
            var memberships = await ReadMixedOwnerAsync(() => dbContext.OrderServiceMemberships.AsNoTracking()
                .Where(x => x.CompanyId == company && x.OrderId == id && x.OrderVersionAfter <= expected).ToArrayAsync(token), fence, token);
            if (memberships.Length != receipt.Children.Count || memberships.Any(x => !previous.Original.Membership.Any(m => MixedStoredMemberMatches(x, m))))
                throw MixedSupplierUnavailable();
            var ids = receipt.Children.Select(x => x.Before.OrderId).ToArray();
            foreach (var chunk in ids.Chunk(64))
            {
                var states = receipt.Children.Where(x => chunk.Contains(x.Before.OrderId)).SelectMany(x => new[] { x.Before, x.After })
                    .DistinctBy(x => (x.OrderId, x.Version)).ToArray();
                var parameter = Expression.Parameter(typeof(PurchaseServiceOrderRevisionRow), "revision");
                Expression predicate = Expression.Constant(false);
                foreach (var state in states)
                    predicate = Expression.OrElse(predicate, Expression.AndAlso(
                        Expression.Equal(Expression.Property(parameter, nameof(PurchaseServiceOrderRevisionRow.CompanyId)), Expression.Constant(company)),
                        Expression.AndAlso(Expression.Equal(Expression.Property(parameter, nameof(PurchaseServiceOrderRevisionRow.OrderId)), Expression.Constant(state.OrderId)),
                            Expression.Equal(Expression.Property(parameter, nameof(PurchaseServiceOrderRevisionRow.Version)), Expression.Constant(state.Version)))));
                var exactPairs = Expression.Lambda<Func<PurchaseServiceOrderRevisionRow, bool>>(predicate, parameter);
                var revisions = await ReadMixedOwnerAsync(() => dbContext.ServiceOrderRevisions.AsNoTracking().Where(exactPairs).ToArrayAsync(token), fence, token);
                var entities = chunk.Select(x => x.ToString("D")).ToArray();
                var priceEvent = PurchaseServiceOrderCommandsV1.RevisePrices.Replace(".v1", ".recorded.v1", StringComparison.Ordinal);
                var allChildEvents = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
                    && x.EntityType == "Purchasing.ServiceOrder" && entities.Contains(x.EntityId) && x.IdempotencyKeyHash == key
                    && x.EventType == priceEvent).Take(chunk.Length + 1).ToArrayAsync(token), fence, token);
                // Technical 64-child query batches retain every exact pair, never an admission/business cap.
                foreach (var child in receipt.Children.Where(x => chunk.Contains(x.Before.OrderId)))
                {
                    var selected = frozen.Services.Single(x => x.ServiceOrderId == child.Before.OrderId);
                    if (selected.ExpectedVersion != child.Before.Version.ToString(CultureInfo.InvariantCulture)
                        || !selected.LineIds.ToHashSet().SetEquals(child.Before.Lines.Select(x => x.LineId))) throw MixedSupplierUnavailable();
                    var change = frozen.Changes.SingleOrDefault(x => x.ServiceOrderId == child.Before.OrderId);
                    var wanted = change is null ? child.Before : PurchaseServiceOrder.RevisePrices(child.Before, child.Before.Version,
                        PurchaseServiceOrderServiceV1.ReadNativeRevisionPrices(change.Lines), attempt.ActorId, frozen.Reason, attempt.RecordedAtUtc);
                    if (!PurchaseMixedOrderApprovalFactsV1.SameChild(wanted, child.After)) throw MixedSupplierUnavailable();
                    foreach (var state in new[] { child.Before, child.After }.DistinctBy(x => x.Version))
                    {
                        var exact = revisions.Where(x => x.OrderId == state.OrderId && x.Version == state.Version).ToArray();
                        if (exact.Length != 1 || !MixedJsonEqual(exact[0].SnapshotJson, JsonSerializer.Serialize(state, PurchaseServiceOrderStoreV1.Json)))
                            throw MixedSupplierUnavailable();
                    }
                    var childEntity = child.Before.OrderId.ToString("D");
                    var childEvents = allChildEvents.Where(x => x.EntityId == childEntity).ToArray();
                    if (change is null ? childEvents.Length != 0 : childEvents.Length != 1 || !MixedAuditIdentity(childEvents[0], attempt,
                        "Purchasing.ServiceOrder", child.Before.OrderId, PurchaseServiceOrderCommandsV1.RevisePrices.Replace(".v1", ".recorded.v1", StringComparison.Ordinal),
                        PurchaseServiceOrderCommandsV1.RevisePrices, permission) || childEvents[0].EntityVersionBefore != child.Before.Version
                        || childEvents[0].EntityVersionAfter != child.After.Version
                        || !MixedJsonEqual(childEvents[0].BeforeJson, JsonSerializer.Serialize(child.Before, PurchaseServiceOrderStoreV1.Json))
                        || !MixedJsonEqual(childEvents[0].AfterJson, JsonSerializer.Serialize(child.After, PurchaseServiceOrderStoreV1.Json))) throw MixedSupplierUnavailable();
                }
            }
            var before = JsonNode.Parse(audit.BeforeJson) ?? throw MixedSupplierUnavailable();
            if (before["version"]?.GetValue<long>() != expected || before["status"]?.GetValue<string>() != "Approved"
                || before["preparedBy"]?.GetValue<string>() != revision.PreviousPreparer
                || !JsonNode.DeepEquals(before["approval"], JsonSerializer.SerializeToNode(new {
                    approvedBy = revision.PreviousApproval.ApprovedBy, approvedAtUtc = revision.PreviousApproval.ApprovedAtUtc }, Json))) throw MixedSupplierUnavailable();
            before["version"] = receipt.ResultVersion; before["status"] = "Draft"; before["preparedBy"] = revision.PreparedBy; before["approval"] = null;
            if (!MixedJsonEqual(before.ToJsonString(), after.NativeOrder.GetRawText()) || receipt.Visible?.Order is not { } header
                || receipt.Withheld?.Order is not { } hidden || header.Id != id || header.CompanyId != company || header.Status != "Draft"
                || header.Version != receipt.ResultVersion.ToString(CultureInfo.InvariantCulture) || header.UpdatedAtUtc != receipt.RecordedAtUtc
                || header.CompanyAccountId != previous.Original.Visible.Order.CompanyAccountId
                || header.SupplierPartyId != previous.Original.Visible.Order.SupplierPartyId || header != hidden
                || receipt.Visible.PriceDisclosure != PurchasingPriceDisclosureV1.Visible || receipt.Withheld.PriceDisclosure != PurchasingPriceDisclosureV1.Withheld)
                throw MixedSupplierUnavailable();
            var masked = receipt.Visible with { PriceDisclosure = PurchasingPriceDisclosureV1.Withheld, PricePending = null,
                KgPrice = null, Realization = null, Lines = receipt.Visible.Lines.Select(x => x with { Price = null, KgPrice = null, Realization = null }).ToArray() };
            if (!MixedJsonEqual(JsonSerializer.Serialize(masked, PurchasingJson.Options), JsonSerializer.Serialize(receipt.Withheld, PurchasingJson.Options)))
                throw MixedSupplierUnavailable();
            return receipt;
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException or FormatException
            or OverflowException or KeyNotFoundException or PurchaseServiceOrderException) { throw MixedSupplierUnavailable(); }
    }

    private static MixedServicePriceAuditAfter? ReadMixedServicePriceAfter(string json)
    { try { return JsonSerializer.Deserialize<MixedServicePriceAuditAfter>(json, Json); } catch (JsonException) { return null; } }
}
