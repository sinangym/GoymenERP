using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using GoymenERP.Modules.Audit.Domain.Events;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchaseMixedOrderMembershipStoreV1
{
    // Common lifecycle data belongs to one call of the existing parent-owned runner. No second transaction/retry engine.
    internal abstract class MixedOwnedExecution(Func<CancellationToken, Task> fence)
    {
        internal Func<CancellationToken, Task> Fence { get; } = fence;
        internal bool CommitStarted { get; set; }
        internal bool Committed { get; set; }
        internal bool Aborted { get; set; }
        internal bool CleanupComplete { get; set; }
        internal Exception? OriginalFailure { get; set; }
        internal abstract void Enter(IDbContextTransaction owned);
    }
    private sealed record MixedMembershipAuditAfter(JsonElement NativeOrder, PurchaseMixedOrderMembershipReceiptV1 Membership,
        IReadOnlyList<PurchaseOrderServiceMembership> Added);
    public bool MembershipEntryIsClean => CleanMixedPreviewEntry();

    public async Task<PurchaseMixedOrderMembershipAnswerV1> BindServicesAsync(Guid orderId, BindPurchaseOrderServicesRequest mutableRequest,
        PurchasingWriteEnvelope envelope, string hash, bool cleanBeforeAuthority,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> freshFence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(freshFence);
        RequireEnvelope(envelope); RequireHash(hash);
        if (!cleanBeforeAuthority || !CleanMixedPreviewEntry()) throw new PurchasingServiceException(503, "service_unavailable");
        var request = PurchaseMixedOrderMembershipV1.Freeze(mutableRequest);
        if (PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, orderId, request) != hash)
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Membership content changed before admission.");
        var company = RequireResolvedCompany();
        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
            await freshFence(token); token.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
        }
        var execution = new MixedMembershipExecution(this, orderId, request, envelope, hash, project, Fence);
        try
        {
            await MutateOrderGuardedCoreAsync(envelope, hash, PurchaseMixedOrderMembershipV1.Command, orderId,
                execution.Bind, new(PurchaseMixedOrderMembershipV1.Event, "BindServices", "Hizmet siparişi üyeliği bağlandı"),
                Fence, null, cancellationToken, execution);
            return execution.Answer ?? throw new PurchasingServiceException(503, "service_unavailable");
        }
        catch (Exception failure) when (!execution.CommitStarted && !execution.Committed && execution.CleanupComplete
            && execution.Aborted && ReferenceEquals(execution.OriginalFailure, failure) && MixedWinnerFailure(failure))
        {
            if (!CleanMixedPreviewEntry()) throw;
            await ReadOnlyFence(cancellationToken);
            var winner = await ReadMembershipOriginalAsync(orderId, request, envelope, hash, ReadOnlyFence, cancellationToken);
            await ReadOnlyFence(cancellationToken);
            return winner ?? throw new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange);
        }
        async Task ReadOnlyFence(CancellationToken token)
        { await Fence(token); if (!CleanMixedPreviewEntry()) throw new PurchasingServiceException(503, "service_unavailable"); }
    }

    internal sealed class MixedMembershipExecution(PurchasingStore owner, Guid orderId, BindPurchaseOrderServicesRequest request,
        PurchasingWriteEnvelope envelope, string hash, Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project,
        Func<CancellationToken, Task> fence) : MixedOwnedExecution(fence)
    {
        internal PurchaseMixedOrderMembershipAnswerV1? Answer { get; private set; }
        private IDbContextTransaction? transaction;
        private IReadOnlyList<PurchaseServiceOrderState>? children;
        private PurchaseOrder? actual;
        private Guid account;
        internal override void Enter(IDbContextTransaction owned)
        {
            if (transaction is not null || owner.MixedWriter is null || owner.MixedWriter.HasPendingDrafts
                || owner.MixedDatabase.ChangeTracker.HasChanges() || !ReferenceEquals(owner.MixedDatabase.Database.CurrentTransaction, owned))
                throw new InvalidOperationException("Membership requires the same clean parent transaction and actual Audit writer.");
            transaction = owned;
        }
        internal async Task<bool> ReplayAsync(CancellationToken token)
        { Answer = await owner.ReadMembershipOriginalAsync(orderId, request, envelope, hash, Fence, token); return Answer is not null; }
        internal async Task PrepareAsync(PurchaseOrder parent, Guid companyAccount, CancellationToken token)
        {
            if (transaction is null || actual is not null || !ReferenceEquals(owner.MixedDatabase.Database.CurrentTransaction, transaction))
                throw new InvalidOperationException("Membership is not admitted outside its exact parent command.");
            if (parent.Version != PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion))
                throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            if (parent.Status != PurchaseOrderStatus.Draft) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.OrderState);
            var ids = request.Services.Select(x => x.ServiceOrderId).ToArray();
            var company = owner.RequireResolvedCompany();
            var already = await ReadMixedOwnerAsync(() => owner.MixedDatabase.OrderServiceMemberships.AsNoTracking()
                .AnyAsync(x => x.CompanyId == company && ids.Contains(x.ServiceOrderId), token), Fence, token);
            if (already) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            // Tracked exact immutable revisions satisfy the existing EF membership guard. Current headers are not modified.
            var revisions = await ReadMixedOwnerAsync(() => (from header in owner.MixedDatabase.ServiceOrders.AsNoTracking()
                join revision in owner.MixedDatabase.ServiceOrderRevisions on new { header.CompanyId, header.OrderId, header.Version }
                    equals new { revision.CompanyId, revision.OrderId, revision.Version }
                where header.CompanyId == company && ids.Contains(header.OrderId)
                select revision).ToArrayAsync(token), Fence, token);
            if (revisions.Length != ids.Length || revisions.Select(x => x.OrderId).Distinct().Count() != ids.Length)
                throw new PurchasingServiceException(503, "service_unavailable");
            var selected = new List<PurchaseServiceOrderState>();
            foreach (var intent in request.Services)
            {
                var row = revisions.Single(x => x.OrderId == intent.ServiceOrderId);
                PurchaseServiceOrderState child;
                try
                {
                    child = JsonSerializer.Deserialize<PurchaseServiceOrderState>(row.SnapshotJson, PurchaseServiceOrderStoreV1.Json)
                        ?? throw new PurchasingServiceException(503, "service_unavailable");
                    PurchaseServiceOrder.Validate(child);
                }
                catch (Exception failure) when (failure is JsonException or ArgumentException or PurchaseServiceOrderException)
                { throw new PurchasingServiceException(503, "service_unavailable"); }
                if (row.CompanyId != company || child.CompanyId != company || child.OrderId != row.OrderId || child.Version != row.Version
                    || child.Version.ToString(CultureInfo.InvariantCulture) != intent.ExpectedVersion || child.Status != PurchaseServiceOrderStatus.Draft
                    || child.SupplierPartyId != parent.SupplierPartyId || child.CompanyAccountId != companyAccount
                    || child.Money.CurrencyId != parent.Money?.CurrencyId || string.IsNullOrWhiteSpace(child.DocumentNumber)
                    || !intent.LineIds.ToHashSet().SetEquals(child.Lines.Select(x => x.LineId)))
                    throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
                selected.Add(child);
            }
            actual = parent; account = companyAccount; children = selected.AsReadOnly();
            await Fence(token);
        }
        internal void Bind(PurchaseOrder parent, DateTimeOffset at)
        {
            if (!ReferenceEquals(parent, actual) || children is null) throw new InvalidOperationException("Actual complete child selection is required.");
            parent.BindServiceOrders(PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion), account, children,
                request.Services.Select(x => new PurchaseOrderServiceSelection(x.ServiceOrderId,
                    PurchaseMixedOrderMembershipV1.SelectedChildVersion(x.ExpectedVersion), x.LineIds)).ToArray(), at);
        }
        internal bool MatchesAdded(PurchaseOrder parent, int countBefore) => ReferenceEquals(parent, actual)
            && parent.ServiceMemberships.Count == countBefore + request.Services.Count
            && parent.ServiceMembershipBatches.LastOrDefault() is { } batch && batch.Members.Count == request.Services.Count
            && batch.OrderVersionAfter == parent.Version && batch.RecordedAtUtc == parent.UpdatedAtUtc
            && batch.Members.Select(x => x.ServiceOrderId).ToHashSet().SetEquals(request.Services.Select(x => x.ServiceOrderId));
        internal void StageMemberships(PurchaseOrder parent, long before)
        {
            if (children is null || !ReferenceEquals(parent, actual)) throw new InvalidOperationException("Actual binding is required.");
            owner.MixedDatabase.OrderServiceMemberships.AddRange(ServiceMembershipRows(parent).Where(x => x.OrderVersionAfter > before));
        }
        internal string RecordAfter(StoredPurchaseOrder parent, DateTimeOffset at)
        {
            if (!ReferenceEquals(parent.Order, actual)) throw new InvalidOperationException("Unrelated membership result.");
            var receipt = new PurchaseMixedOrderMembershipReceiptV1(parent.Order.CompanyId, orderId, envelope.ActorId.Trim(),
                IdempotencyKeyHash(envelope.IdempotencyKey), hash, envelope.CorrelationId.Trim(), at, request,
                project(parent, PurchasingPriceDisclosureV1.Visible), project(parent, PurchasingPriceDisclosureV1.Withheld));
            return JsonSerializer.Serialize(new MixedMembershipAuditAfter(JsonSerializer.Deserialize<JsonElement>(Snapshot(parent.Order)), receipt,
                parent.Order.ServiceMembershipBatches.Last().Members), Json);
        }
        internal async Task SaveAsync(CancellationToken token)
        {
            if (transaction is null || !ReferenceEquals(owner.MixedDatabase.Database.CurrentTransaction, transaction))
                throw new InvalidOperationException("Membership cannot borrow a transaction.");
            await Fence(token); await owner.MixedWriter!.WritePendingAsync(transaction.GetDbTransaction(), token);
            await Fence(token); await owner.MixedDatabase.SaveChangesAsync(token); await Fence(token);
            if (owner.MixedWriter!.HasPendingDrafts) throw new InvalidOperationException("Membership audit was not flushed.");
        }
        internal async Task VerifySavedAsync(CancellationToken token)
        {
            Answer = await owner.ReadMembershipOriginalAsync(orderId, request, envelope, hash, Fence, token)
                ?? throw new PurchasingServiceException(503, "service_unavailable");
            Answer = Answer with { IsReplay = false };
            await Fence(token);
            if (owner.MixedDatabase.ChangeTracker.HasChanges() || owner.MixedWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Membership did not flush its owned work.");
        }
    }

    private async Task<PurchaseMixedOrderMembershipAnswerV1?> ReadMembershipOriginalAsync(Guid orderId,
        BindPurchaseOrderServicesRequest request, PurchasingWriteEnvelope envelope, string hash,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var company = RequireResolvedCompany(); var key = IdempotencyKeyHash(envelope.IdempotencyKey);
        var attempts = await ReadMixedOwnerAsync(() => dbContext.Attempts.AsNoTracking().Where(x => x.CompanyId == company
            && x.CommandName == PurchaseMixedOrderMembershipV1.Command && x.IdempotencyKeyHash == key).Take(2).ToArrayAsync(token), fence, token);
        if (attempts.Length == 0) return null;
        if (attempts.Length != 1) throw new PurchasingServiceException(503, "service_unavailable");
        var attempt = attempts[0];
        RequireSameContent(attempt, envelope.ActorId.Trim(), hash);
        if (attempt.OrderId != orderId || attempt.Outcome != AttemptOutcomeRecorded || attempt.RefusalJson is not null)
            throw new PurchasingServiceException(503, "service_unavailable");
        var entity = orderId.ToString("D");
        var events = await ReadMixedOwnerAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x => x.CompanyId == company
            && x.EntityType == OrderEntityType && x.EntityId == entity && x.IdempotencyKeyHash == key
            && x.EventType == PurchaseMixedOrderMembershipV1.Event).Take(2).ToArrayAsync(token), fence, token);
        if (events.Length != 1) throw new PurchasingServiceException(503, "service_unavailable");
        var ids = request.Services.Select(x => x.ServiceOrderId).ToArray();
        var members = await ReadMixedOwnerAsync(() => dbContext.OrderServiceMemberships.AsNoTracking()
            .Where(x => x.CompanyId == company && x.OrderId == orderId && ids.Contains(x.ServiceOrderId)).ToArrayAsync(token), fence, token);
        var prepared = await ReadMixedOwnerAsync(() => (from member in dbContext.OrderServiceMemberships.AsNoTracking()
            join revision in dbContext.ServiceOrderRevisions.AsNoTracking() on new { member.CompanyId, OrderId = member.ServiceOrderId, Version = member.PreparedVersion }
                equals new { revision.CompanyId, revision.OrderId, revision.Version }
            where member.CompanyId == company && member.OrderId == orderId && ids.Contains(member.ServiceOrderId)
            select revision).ToArrayAsync(token), fence, token);
        var receipt = RestoreMembershipReceipt(attempt, events[0], members, prepared, orderId, request, envelope, hash)
            ?? throw new PurchasingServiceException(503, "service_unavailable");
        return new(receipt, true);
    }

    internal static PurchaseMixedOrderMembershipReceiptV1? RestoreMembershipReceipt(PurchaseAttemptRow attempt, AuditEvent audit,
        IReadOnlyList<PurchaseOrderServiceMembershipRow> members, IReadOnlyList<PurchaseServiceOrderRevisionRow> prepared,
        Guid orderId, BindPurchaseOrderServicesRequest request, PurchasingWriteEnvelope envelope, string hash)
    {
        try
        {
            request = PurchaseMixedOrderMembershipV1.Freeze(request);
            var expected = PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion);
            if (attempt.CompanyId == Guid.Empty || orderId == Guid.Empty || attempt.OrderId != orderId
                || attempt.CommandName != PurchaseMixedOrderMembershipV1.Command || attempt.Outcome != AttemptOutcomeRecorded || attempt.RefusalJson is not null
                || attempt.ActorId != envelope.ActorId.Trim() || attempt.IdempotencyKeyHash != IdempotencyKeyHash(envelope.IdempotencyKey)
                || attempt.RequestHash != hash || PurchasingRequestHash.Compute(PurchaseMixedOrderMembershipV1.Command, orderId, request) != hash
                || attempt.RecordedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(attempt.CorrelationId)
                || !MixedAuditIdentity(audit, attempt, OrderEntityType, orderId, PurchaseMixedOrderMembershipV1.Event, "BindServices", envelope.PermissionCode)
                || audit.EntityVersionBefore != expected || audit.EntityVersionAfter != expected + 1) return null;
            var after = JsonSerializer.Deserialize<MixedMembershipAuditAfter>(audit.AfterJson, Json);
            var original = after?.Membership;
            if (original is null || after is null || !MixedJsonEqual(audit.AfterJson, JsonSerializer.Serialize(after, Json))
                || original.CompanyId != attempt.CompanyId || original.OrderId != orderId || original.ActorId != attempt.ActorId
                || original.KeyHash != attempt.IdempotencyKeyHash || original.PayloadHash != hash
                || original.OriginalCorrelationId != attempt.CorrelationId || original.RecordedAtUtc != attempt.RecordedAtUtc
                || !MixedJsonEqual(JsonSerializer.Serialize(original.FrozenRequest, PurchasingJson.Options), JsonSerializer.Serialize(request, PurchasingJson.Options))
                || original.Visible?.Order is not { } header || original.Withheld?.Order is not { } hidden
                || header.CompanyId != attempt.CompanyId || header.Id != orderId || header.Status != "Draft"
                || header.Version != (expected + 1).ToString(CultureInfo.InvariantCulture) || header.UpdatedAtUtc != attempt.RecordedAtUtc
                || !MixedJsonEqual(JsonSerializer.Serialize(header, PurchasingJson.Options), JsonSerializer.Serialize(hidden, PurchasingJson.Options))
                || original.Visible.PriceDisclosure != PurchasingPriceDisclosureV1.Visible || original.Withheld.PriceDisclosure != PurchasingPriceDisclosureV1.Withheld
                || original.Visible.Lines is not { Count: > 0 } || original.Visible.Lines.Count != header.LineCount || original.Visible.Lines.Any(x => x is null)
                || after.Added is null || after.Added.Any(x => x is null) || after.Added.Count != request.Services.Count
                || members.Count != request.Services.Count || prepared.Count != request.Services.Count
                || after.Added.Select(x => x.ServiceOrderId).Distinct().Count() != after.Added.Count) return null;
            var before = JsonSerializer.Deserialize<JsonElement>(audit.BeforeJson);
            if (MixedRequiredField(before, "status").GetString() != "Draft" || MixedRequiredField(before, "version").GetInt64() != expected
                || MixedRequiredField(after.NativeOrder, "status").GetString() != "Draft" || MixedRequiredField(after.NativeOrder, "version").GetInt64() != expected + 1
                || MixedRequiredField(after.NativeOrder, "supplierPartyId").GetGuid() != header.SupplierPartyId
                || MixedRequiredField(after.NativeOrder, "lines").GetArrayLength() != header.LineCount
                || original.Visible.Currency is not { } currency
                || MixedRequiredField(MixedRequiredField(after.NativeOrder, "currency"), "currencyId").GetGuid() != currency.CurrencyId
                || MixedRequiredField(MixedRequiredField(after.NativeOrder, "currency"), "currencyCode").GetString() != currency.CurrencyCode
                || MixedRequiredField(MixedRequiredField(after.NativeOrder, "currency"), "scale").GetInt32() != currency.Scale
                || !original.Visible.Lines.Select(x => x.LineId).ToHashSet().SetEquals(MixedRequiredField(after.NativeOrder, "lines")
                    .EnumerateArray().Select(x => MixedRequiredField(x, "lineId").GetGuid()))) return null;
            // Membership changes only version/time and private metadata, never existing native human facts.
            var unchanged = System.Text.Json.Nodes.JsonNode.Parse(audit.BeforeJson)!.AsObject();
            unchanged["version"] = expected + 1;
            if (!MixedJsonEqual(unchanged.ToJsonString(), after.NativeOrder.GetRawText())) return null;
            var masked = original.Visible with { PriceDisclosure = PurchasingPriceDisclosureV1.Withheld, PricePending = null,
                KgPrice = null, Realization = null, Lines = original.Visible.Lines.Select(x => x with { Price = null, KgPrice = null, Realization = null }).ToArray() };
            if (!MixedJsonEqual(JsonSerializer.Serialize(masked, PurchasingJson.Options), JsonSerializer.Serialize(original.Withheld, PurchasingJson.Options))) return null;
            foreach (var intent in request.Services)
            {
                var member = members.SingleOrDefault(x => x.ServiceOrderId == intent.ServiceOrderId);
                var revision = prepared.SingleOrDefault(x => x.OrderId == intent.ServiceOrderId);
                var recorded = after.Added.SingleOrDefault(x => x.ServiceOrderId == intent.ServiceOrderId);
                if (member is null || revision is null || recorded is null || !MixedStoredMemberMatches(member, recorded)
                    || member.OrderVersionAfter != expected + 1 || member.RecordedAtUtc != attempt.RecordedAtUtc
                    || member.CompanyId != attempt.CompanyId || member.OrderId != orderId || member.SupplierPartyId != header.SupplierPartyId
                    || member.CompanyAccountId != header.CompanyAccountId || member.CurrencyId != currency.CurrencyId || member.MemberCount != request.Services.Count
                    || member.PreparedVersion.ToString(CultureInfo.InvariantCulture) != intent.ExpectedVersion
                    || !intent.LineIds.ToHashSet().SetEquals(recorded.LineIds)) return null;
                _ = PreparedServiceMembership(member, revision);
            }
            if (!members.Select(x => x.MemberNo).Order().SequenceEqual(Enumerable.Range(1, members.Count))) return null;
            return original;
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException
            or FormatException or OverflowException or KeyNotFoundException or PurchaseServiceOrderException or PurchasingServiceException)
        { return null; }
    }
}
