using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    internal async Task RequireMixedSupplierOriginAsync(PurchaseServiceOrderState state,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        if (dbContext.Database.CurrentTransaction is null || state.CompanyId != RequireResolvedCompany()
            || state.Status != PurchaseServiceOrderStatus.Draft || state.SupplierRevisionOrigin is not { } origin)
            throw MixedSupplierUnavailable();
        var receipt = await ReadMixedSupplierVersionAsync(origin.ParentOrderId, origin.ParentRevisionVersion, fence, token,
            origin.ParentRevisionVersion == long.MaxValue ? long.MaxValue : origin.ParentRevisionVersion + 1);
        if (receipt.KeyHash != origin.ParentCommandKeyHash || receipt.CompanyId != state.CompanyId
            || receipt.FrozenRequest.SupplierPartyId != state.SupplierPartyId || receipt.CompanyAccountId != state.CompanyAccountId
            || receipt.Children.Count(x => x.Revised.OrderId == state.OrderId && x.Revised.Version <= state.Version) != 1)
            throw MixedSupplierUnavailable();
        var rows = await ReadMixedOwnerAsync(() => dbContext.Orders.AsNoTracking().Where(x => x.Id == origin.ParentOrderId).Take(2).ToArrayAsync(token), fence, token);
        if (rows.Length != 1 || rows[0].Status != (int)PurchaseOrderStatus.Draft || rows[0].SupplierPartyId != state.SupplierPartyId
            || rows[0].CompanyAccountId != state.CompanyAccountId) throw MixedSupplierUnavailable();
        PurchaseSupplierRevisionHistory? history;
        try { history = PurchaseSupplierRevisionHistory.Read(rows[0]); }
        catch (InvalidOperationException) { throw MixedSupplierUnavailable(); }
        if (history?.Revisions.Last().OrderVersionAfter != origin.ParentRevisionVersion) throw MixedSupplierUnavailable();
        if (fence is not null) await fence(token);
    }
    // The origin in a child snapshot is only an exact lookup locator. Only the actual parent
    // Attempt, integrity-protected Audit and immutable child revisions establish its meaning.
    private async Task<PurchaseMixedSupplierRevisionAnswerV1?> ReadMixedSupplierOriginalAsync(Guid id,
        RevisePurchaseOrderSupplierRequest request, PurchasingWriteEnvelope envelope, string hash,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var company = RequireResolvedCompany(); var key = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempts = await ReadMixedOwnerAsync(() => dbContext.Attempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == PurchasingCommands.OrderSupplierReviseV1 && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
        if (attempts.Length == 0) return null;
        if (attempts.Length != 1) throw MixedSupplierUnavailable();
        if (attempts[0].ActorId != envelope.ActorId.Trim() || attempts[0].RequestHash != hash || attempts[0].OrderId is { } target && target != id)
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "The original linked supplier revision belongs to another content.");
        if (attempts[0].Outcome == AttemptOutcomeRefused)
        {
            var refusal = ReadMixedRefusal(attempts[0].RefusalJson);
            if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
                || refusal.Errors.Any(x => x is null || string.IsNullOrWhiteSpace(x.Field) || string.IsNullOrWhiteSpace(x.Code))
                || !PurchasingPhysicalStockRefusalDetailV1.IsValid(refusal.Status, refusal.Code, refusal.Detail)
                || !MixedJsonEqual(attempts[0].RefusalJson!, JsonSerializer.Serialize(refusal, Json))) throw MixedSupplierUnavailable();
            return new(null, true, refusal, attempts[0].CorrelationId);
        }
        var receipt = await ReadMixedSupplierArtifactsAsync(attempts[0], id, request, envelope.PermissionCode, fence, token, long.MaxValue);
        return new(receipt, true);
    }

    private async Task<PurchaseMixedSupplierRevisionReceiptV1> ReadMixedSupplierVersionAsync(Guid id, long version,
        Func<CancellationToken, Task>? fence, CancellationToken token, long ceiling)
    {
        if (version < 1 || version > ceiling) throw MixedSupplierUnavailable();
        var company = RequireResolvedCompany(); var entity = id.ToString("D");
        var audits = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
            && x.EntityType == OrderEntityType && x.EntityId == entity && x.EventType == "purchasing.purchase-order.supplier-revised.v1"
            && x.EntityVersionAfter == version).Take(2).ToArrayAsync(token), fence, token);
        if (audits.Length != 1) throw MixedSupplierUnavailable();
        var embedded = ReadMixedSupplierAfter(audits[0].AfterJson)?.Revision ?? throw MixedSupplierUnavailable();
        var key = audits[0].IdempotencyKeyHash;
        var attempts = await ReadMixedOwnerAsync(() => dbContext.Attempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == PurchasingCommands.OrderSupplierReviseV1 && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
        if (attempts.Length != 1) throw MixedSupplierUnavailable();
        return await ReadMixedSupplierArtifactsAsync(attempts[0], id, embedded.FrozenRequest, audits[0].PermissionCode!, fence, token, ceiling);
    }

    private async Task<PurchaseMixedSupplierRevisionReceiptV1> ReadMixedSupplierArtifactsAsync(PurchaseAttemptRow attempt,
        Guid id, RevisePurchaseOrderSupplierRequest request, string permission, Func<CancellationToken, Task>? fence,
        CancellationToken token, long ceiling)
    {
        try
        {
            var company = RequireResolvedCompany(); var entity = id.ToString("D"); var key = attempt.IdempotencyKeyHash;
            RevisePurchaseOrderSupplierRequest frozen;
            try { frozen = PurchaseMixedSupplierRevisionIntentV1.Freeze(request); }
            catch (PurchasingServiceException) { throw MixedSupplierUnavailable(); }
            var selections = frozen.MixedServices ?? throw MixedSupplierUnavailable();
            var expected = PurchaseMixedOrderApprovalIntentV1.Version(frozen.ExpectedVersion);
            if (expected == long.MaxValue || expected + 1 > ceiling || attempt.CompanyId != company || attempt.OrderId != id
                || attempt.CommandName != PurchasingCommands.OrderSupplierReviseV1 || attempt.Outcome != AttemptOutcomeRecorded
                || attempt.RefusalJson is not null || string.IsNullOrWhiteSpace(attempt.ActorId) || string.IsNullOrWhiteSpace(attempt.CorrelationId)
                || attempt.RecordedAtUtc.Offset != TimeSpan.Zero || PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, id, frozen) != attempt.RequestHash)
                throw MixedSupplierUnavailable();
            var audits = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
                && x.EntityType == OrderEntityType && x.EntityId == entity && x.IdempotencyKeyHash == key
                && x.EventType == "purchasing.purchase-order.supplier-revised.v1").Take(2).ToArrayAsync(token), fence, token);
            if (audits.Length != 1 || !MixedAuditIdentity(audits[0], attempt, OrderEntityType, id,
                "purchasing.purchase-order.supplier-revised.v1", "ReviseSupplier", permission)) throw MixedSupplierUnavailable();
            var audit = audits[0]; var after = ReadMixedSupplierAfter(audit.AfterJson);
            if (after?.Revision is not { } result || !MixedJsonEqual(audit.AfterJson, JsonSerializer.Serialize(after, Json))
                || result.CompanyId != company || result.OrderId != id || result.ActorId != attempt.ActorId || result.KeyHash != key
                || result.PayloadHash != attempt.RequestHash || result.OriginalCorrelationId != attempt.CorrelationId
                || result.RecordedAtUtc != attempt.RecordedAtUtc || result.ExpectedVersion != expected || result.ResultVersion != expected + 1
                || audit.EntityVersionBefore != expected || audit.EntityVersionAfter != expected + 1
                || !MixedJsonEqual(JsonSerializer.Serialize(result.FrozenRequest, PurchasingJson.Options), JsonSerializer.Serialize(frozen, PurchasingJson.Options))
                || result.PreviousCompanyAccountId == Guid.Empty || result.CompanyAccountId != frozen.CompanyAccountId
                || result.NativeRevision is not { } revision || revision.PreviousApproval is null || revision.PreviousLines is not { Count: > 0 } || revision.PreviousLines.Any(x => x is null)
                || revision.OrderVersionAfter != expected + 1
                || revision.SupplierPartyId != frozen.SupplierPartyId || revision.PreparedBy != attempt.ActorId
                || revision.Reason != frozen.Reason.Trim() || revision.RecordedAtUtc != attempt.RecordedAtUtc
                || revision.PreviousApprovalVersion < 1 || revision.PreviousApprovalVersion > expected
                || result.Children is not { Count: > 0 } || result.Children.Any(x => x is null || !MixedChildShape(x.Original) || !MixedChildShape(x.Revised))
                || result.Children.Count != selections.Count || result.Children.Select(x => x.Original.OrderId).Distinct().Count() != result.Children.Count)
                throw MixedSupplierUnavailable();
            // Descending exact native versions prevent circular/self-certifying receipt chains.
            var previous = await ReadMixedApprovalVersionAsync(id, revision.PreviousApprovalVersion, fence, token, result.ResultVersion);
            if (previous.Original.Visible.Order.CompanyAccountId != result.PreviousCompanyAccountId
                || previous.Original.Visible.Order.SupplierPartyId != revision.PreviousSupplierPartyId
                || previous.ActorId != revision.PreviousApproval.ApprovedBy || previous.RecordedAtUtc != revision.PreviousApproval.ApprovedAtUtc)
                throw MixedSupplierUnavailable();
            ValidateMixedSupplierParent(audit, after.NativeOrder, result, previous);
            var members = await ReadMixedOwnerAsync(() => dbContext.OrderServiceMemberships.AsNoTracking()
                .Where(x => x.CompanyId == company && x.OrderId == id && x.OrderVersionAfter <= expected).ToArrayAsync(token), fence, token);
            if (members.Length != result.Children.Count || members.Any(x => !previous.Original.Membership.Any(m => MixedStoredMemberMatches(x, m))))
                throw MixedSupplierUnavailable();
            var revisions = new List<PurchaseServiceOrderRevisionRow>();
            var childEvents = new List<AuditEvent>();
            foreach (var batch in result.Children.Chunk(64))
            {
                var row = Expression.Parameter(typeof(PurchaseServiceOrderRevisionRow), "revision"); Expression predicate = Expression.Constant(false);
                foreach (var child in batch)
                    foreach (var version in new[] { child.Original.Version, child.Revised.Version })
                        predicate = Expression.OrElse(predicate, Expression.AndAlso(
                            Expression.Equal(Expression.Property(row, nameof(PurchaseServiceOrderRevisionRow.CompanyId)), Expression.Constant(company)),
                            Expression.AndAlso(Expression.Equal(Expression.Property(row, nameof(PurchaseServiceOrderRevisionRow.OrderId)), Expression.Constant(child.Original.OrderId)),
                                Expression.Equal(Expression.Property(row, nameof(PurchaseServiceOrderRevisionRow.Version)), Expression.Constant(version)))));
                var exact = Expression.Lambda<Func<PurchaseServiceOrderRevisionRow, bool>>(predicate, row);
                revisions.AddRange(await ReadMixedOwnerAsync(() => dbContext.ServiceOrderRevisions.AsNoTracking().Where(exact).ToArrayAsync(token), fence, token));
                var ids = batch.Select(x => x.Original.OrderId.ToString("D")).ToArray();
                childEvents.AddRange(await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
                    && x.EntityType == "Purchasing.ServiceOrder" && ids.Contains(x.EntityId) && x.IdempotencyKeyHash == key
                    && x.EventType == MixedChildSupplierRevisionEvent).Take(ids.Length + 1).ToArrayAsync(token), fence, token));
            }
            if (revisions.Count != result.Children.Count * 2 || childEvents.Count != result.Children.Count) throw MixedSupplierUnavailable();
            foreach (var child in result.Children)
            {
                var old = child.Original; var next = child.Revised; var selected = selections.SingleOrDefault(x => x.ServiceOrderId == old.OrderId);
                if (selected is null || old.CompanyId != company || old.SupplierPartyId != revision.PreviousSupplierPartyId
                    || old.CompanyAccountId != result.PreviousCompanyAccountId || old.Version.ToString(CultureInfo.InvariantCulture) != selected.ExpectedVersion
                    || selected.LineIds.Count != old.Lines.Count || !selected.LineIds.ToHashSet().SetEquals(old.Lines.Select(x => x.LineId))) throw MixedSupplierUnavailable();
                var native = PurchaseServiceOrder.PrepareSupplierRevision(old, old.Version, frozen.SupplierPartyId, frozen.CompanyAccountId,
                    next.PartyVersion, next.AccountVersion, attempt.ActorId, frozen.Reason.Trim(), attempt.RecordedAtUtc, PurchaseOrderInvoiceHistory.None)
                    with { SupplierRevisionOrigin = new(id, result.ResultVersion, key) };
                if (!PurchaseMixedOrderApprovalFactsV1.SameChild(native, next)) throw MixedSupplierUnavailable();
                foreach (var state in new[] { old, next })
                {
                    var exact = revisions.Where(x => x.CompanyId == company && x.OrderId == state.OrderId && x.Version == state.Version).ToArray();
                    if (exact.Length != 1 || !MixedJsonEqual(exact[0].SnapshotJson, JsonSerializer.Serialize(state, PurchaseServiceOrderStoreV1.Json))) throw MixedSupplierUnavailable();
                }
                var events = childEvents.Where(x => x.EntityId == old.OrderId.ToString("D")).ToArray();
                if (events.Length != 1 || !MixedAuditIdentity(events[0], attempt, "Purchasing.ServiceOrder", old.OrderId,
                    MixedChildSupplierRevisionEvent, MixedChildSupplierRevisionCommand, permission)
                    || events[0].EntityVersionBefore != old.Version || events[0].EntityVersionAfter != next.Version
                    || !MixedJsonEqual(events[0].BeforeJson, JsonSerializer.Serialize(old, PurchaseServiceOrderStoreV1.Json))
                    || !MixedJsonEqual(events[0].AfterJson, JsonSerializer.Serialize(next, PurchaseServiceOrderStoreV1.Json))) throw MixedSupplierUnavailable();
            }
            if (fence is not null) await fence(token);
            return result;
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException
            or PurchaseServiceOrderException or PurchasingRuleException)
        { throw MixedSupplierUnavailable(); }
    }

    private static void ValidateMixedSupplierParent(AuditEvent audit, JsonElement after, PurchaseMixedSupplierRevisionReceiptV1 receipt, PurchaseMixedOrderApprovalReceiptV1 previous)
    {
        var before = JsonNode.Parse(audit.BeforeJson)?.AsObject() ?? throw MixedSupplierUnavailable();
        var expected = (JsonObject)before.DeepClone(); var revision = receipt.NativeRevision;
        if (before["version"] is not JsonValue || before["status"] is not JsonValue || before["supplierPartyId"] is not JsonValue
            || before["supplierRevision"] is not JsonValue || before["supplierRevisions"] is not JsonArray
            || before["lines"] is not JsonArray nativeLines || nativeLines.Any(x => x is not JsonObject obj
                || obj["lineId"] is not JsonValue || !obj.ContainsKey("price"))) throw MixedSupplierUnavailable();
        if (before["version"]!.GetValue<long>() != receipt.ExpectedVersion || before["status"]!.GetValue<string>() != "Approved"
            || before["supplierPartyId"]!.GetValue<Guid>() != revision.PreviousSupplierPartyId
            || before["preparedBy"]?.GetValue<string>() != revision.PreviousPreparer
            || !JsonNode.DeepEquals(before["approval"], JsonSerializer.SerializeToNode(revision.PreviousApproval, Json))
            || before["supplierRevision"]!.GetValue<int>() + 1 != revision.Revision) throw MixedSupplierUnavailable();
        var lines = expected["lines"]!.AsArray();
        if (revision.PreviousLines.Count != lines.Count) throw MixedSupplierUnavailable();
        foreach (var line in lines)
        {
            var prior = revision.PreviousLines.SingleOrDefault(x => x.LineId == line!["lineId"]!.GetValue<Guid>()) ?? throw MixedSupplierUnavailable();
            if (!JsonNode.DeepEquals(line!["price"], JsonSerializer.SerializeToNode(prior.Price, Json))) throw MixedSupplierUnavailable();
            line["price"] = null;
        }
        expected["version"] = receipt.ResultVersion; expected["status"] = "Draft"; expected["supplierPartyId"] = JsonSerializer.SerializeToNode(revision.SupplierPartyId);
        expected["preparedBy"] = revision.PreparedBy; expected["approval"] = null;
        expected["supplierRevision"] = revision.Revision; expected["supplierRevisions"]!.AsArray().Add(JsonSerializer.SerializeToNode(revision, Json));
        if (!MixedJsonEqual(expected.ToJsonString(), after.GetRawText()) || receipt.Visible?.Order is not { } header
            || receipt.Withheld?.Order is not { } hidden || header.Id != receipt.OrderId || header.CompanyId != receipt.CompanyId
            || header.Version != receipt.ResultVersion.ToString(CultureInfo.InvariantCulture) || header.Status != "Draft"
            || header.SupplierPartyId != revision.SupplierPartyId || header.CompanyAccountId != receipt.CompanyAccountId
            || header.UpdatedAtUtc != receipt.RecordedAtUtc || !MixedJsonEqual(JsonSerializer.Serialize(header, PurchasingJson.Options), JsonSerializer.Serialize(hidden, PurchasingJson.Options))
            || receipt.Visible.PriceDisclosure != PurchasingPriceDisclosureV1.Visible || receipt.Withheld.PriceDisclosure != PurchasingPriceDisclosureV1.Withheld
            || receipt.Visible.Lines is null || receipt.Visible.Lines.Count != lines.Count || header.LineCount != lines.Count
            || receipt.Visible.Lines.Any(x => x is null || x.Price is not null)
            || receipt.Visible.Lines.Select(x => x.LineId).Distinct().Count() != lines.Count
            || !receipt.Visible.Lines.Select(x => x.LineId).ToHashSet().SetEquals(lines.Select(x => x!["lineId"]!.GetValue<Guid>()))
            || receipt.Visible.Currency is not { } currency
            || currency.CurrencyId != MixedRequiredField(MixedRequiredField(after, "currency"), "currencyId").GetGuid()
            || currency.CurrencyCode != MixedRequiredField(MixedRequiredField(after, "currency"), "currencyCode").GetString()
            || currency.Scale != MixedRequiredField(MixedRequiredField(after, "currency"), "scale").GetInt32()) throw MixedSupplierUnavailable();
        var expectedHeader = previous.Original.Visible.Order with { Status = "Draft", Version = header.Version,
            SupplierPartyId = revision.SupplierPartyId, CompanyAccountId = receipt.CompanyAccountId, UpdatedAtUtc = receipt.RecordedAtUtc };
        if (!MixedJsonEqual(JsonSerializer.Serialize(expectedHeader, PurchasingJson.Options), JsonSerializer.Serialize(header, PurchasingJson.Options)))
            throw MixedSupplierUnavailable();
        foreach (var line in receipt.Visible.Lines)
        {
            var prior = previous.Original.Visible.Lines.SingleOrDefault(x => x.LineId == line.LineId) ?? throw MixedSupplierUnavailable();
            if (line.LineNo != prior.LineNo || line.Kind != prior.Kind || line.ParentLineId != prior.ParentLineId
                || line.SourceRequestLineId != prior.SourceRequestLineId || line.ProductId != prior.ProductId || line.ProductVersion != prior.ProductVersion
                || line.UnitId != prior.UnitId || line.Ordered != prior.Ordered || line.Technical != prior.Technical || line.Committed != prior.Committed)
                throw MixedSupplierUnavailable();
        }
        var masked = receipt.Visible with { PriceDisclosure = PurchasingPriceDisclosureV1.Withheld, PricePending = null, KgPrice = null, Realization = null,
            Lines = receipt.Visible.Lines.Select(x => x with { Price = null, KgPrice = null, Realization = null }).ToArray() };
        if (!MixedJsonEqual(JsonSerializer.Serialize(masked, PurchasingJson.Options), JsonSerializer.Serialize(receipt.Withheld, PurchasingJson.Options))) throw MixedSupplierUnavailable();
    }

    private async Task<PurchaseMixedOrderApprovalReceiptV1> ReadMixedApprovalVersionAsync(Guid id, long version,
        Func<CancellationToken, Task>? fence, CancellationToken token, long ceiling)
    {
        if (version < 1 || version > ceiling) throw MixedSupplierUnavailable();
        var company = RequireResolvedCompany(); var entity = id.ToString("D");
        var audits = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
            && x.EntityType == OrderEntityType && x.EntityId == entity && x.EventType == "purchasing.purchase-order.approved.v1"
            && x.EntityVersionAfter == version).Take(2).ToArrayAsync(token), fence, token);
        if (audits.Length != 1) throw MixedSupplierUnavailable();
        var original = ReadMixedAuditAfter(audits[0].AfterJson)?.MixedApproval ?? throw MixedSupplierUnavailable(); var key = audits[0].IdempotencyKeyHash;
        var attempts = await ReadMixedOwnerAsync(() => dbContext.Attempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == PurchasingCommands.OrderApproveV1 && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
        if (attempts.Length != 1) throw MixedSupplierUnavailable();
        var answer = await ReadMixedArtifactsAsync(attempts[0], id, original.FrozenRequest, original.ActorId, audits[0].PermissionCode!, key,
            original.PayloadHash, fence, token, version);
        return answer.Receipt ?? throw MixedSupplierUnavailable();
    }

    private static MixedSupplierAuditAfter? ReadMixedSupplierAfter(string json)
    { try { return JsonSerializer.Deserialize<MixedSupplierAuditAfter>(json, Json); } catch (JsonException) { return null; } }
    private static PurchasingServiceException MixedSupplierUnavailable() => new(503, "service_unavailable");
}
