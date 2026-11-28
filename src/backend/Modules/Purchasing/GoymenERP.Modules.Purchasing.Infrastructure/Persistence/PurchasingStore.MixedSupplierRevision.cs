using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchaseMixedSupplierRevisionStoreV1
{
    private sealed record MixedSupplierAuditAfter(JsonElement NativeOrder, PurchaseMixedSupplierRevisionReceiptV1 Revision);
    internal const string MixedChildSupplierRevisionCommand = "purchasing.service-order.supplier-revise.v1";
    internal const string MixedChildSupplierRevisionEvent = "purchasing.service-order.supplier-revise.recorded.v1";
    public bool MixedSupplierRevisionEntryIsClean => CleanMixedPreviewEntry();

    public async Task<PurchaseMixedSupplierRevisionAnswerV1> ReviseMixedSupplierAsync(Guid id, RevisePurchaseOrderSupplierRequest mutable,
        PurchasingWriteEnvelope envelope, string hash, bool cleanBeforeAuthority,
        Func<IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<IReadOnlyList<PurchaseServiceSupplierRevisionPreparationV1>>> prepare,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> freshFence,
        Func<PurchaseOrder, IDbTransaction, CancellationToken, Task> allocationGuard, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(prepare); ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(freshFence); ArgumentNullException.ThrowIfNull(allocationGuard); RequireEnvelope(envelope); RequireHash(hash);
        if (!cleanBeforeAuthority || !CleanMixedPreviewEntry()) throw new PurchasingServiceException(503, "service_unavailable");
        var request = PurchaseMixedSupplierRevisionIntentV1.Freeze(mutable);
        if (PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, id, request) != hash)
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Mixed revision content changed.");
        var company = RequireResolvedCompany();
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
            await freshFence(ct); ct.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
        }
        var execution = new MixedSupplierRevisionExecution(this, id, request, envelope, hash, prepare, project, Fence, allocationGuard);
        try
        {
            await MutateOrderGuardedCoreAsync(envelope, hash, PurchasingCommands.OrderSupplierReviseV1, id, execution.Revise,
                new("purchasing.purchase-order.supplier-revised.v1", "ReviseSupplier", request.Reason.Trim()), Fence, null, token, revision: execution);
            return execution.Answer ?? throw new PurchasingServiceException(503, "service_unavailable");
        }
        catch (Exception failure) when (!execution.CommitStarted && !execution.Committed && execution.Aborted && execution.CleanupComplete
            && ReferenceEquals(execution.OriginalFailure, failure) && MixedWinnerFailure(failure))
        {
            if (!CleanMixedPreviewEntry()) throw;
            await ReadOnlyFence(token);
            var winner = await ReadMixedSupplierOriginalAsync(id, request, envelope, hash, ReadOnlyFence, token);
            await ReadOnlyFence(token);
            return winner ?? throw new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange);
        }
        async Task ReadOnlyFence(CancellationToken ct)
        { await Fence(ct); if (!CleanMixedPreviewEntry()) throw new PurchasingServiceException(503, "service_unavailable"); }
    }

    internal sealed class MixedSupplierRevisionExecution(PurchasingStore owner, Guid id, RevisePurchaseOrderSupplierRequest request,
        PurchasingWriteEnvelope envelope, string hash,
        Func<IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<IReadOnlyList<PurchaseServiceSupplierRevisionPreparationV1>>> prepare,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> fence,
        Func<PurchaseOrder, IDbTransaction, CancellationToken, Task> allocations) : MixedOwnedExecution(fence)
    {
        internal Guid TargetAccount => request.CompanyAccountId;
        internal Func<PurchaseOrder, IDbTransaction, CancellationToken, Task> AllocationGuard => allocations;
        internal PurchaseMixedSupplierRevisionAnswerV1? Answer { get; private set; }
        private IDbContextTransaction? transaction;
        private StoredPurchaseOrder? parent;
        private IReadOnlyList<PurchaseServiceOrderState>? original;
        private IReadOnlyList<PurchaseServiceOrderState>? candidates;
        private IReadOnlyList<PurchaseServiceOrderState>? revised;
        internal override void Enter(IDbContextTransaction owned)
        {
            if (transaction is not null || owner.MixedWriter is null || owner.MixedWriter.HasPendingDrafts
                || owner.MixedDatabase.ChangeTracker.HasChanges() || !ReferenceEquals(owner.MixedDatabase.Database.CurrentTransaction, owned))
                throw new InvalidOperationException("Mixed revision requires the same clean parent owner scope.");
            transaction = owned;
        }
        internal void RequireParticipant(PurchasingDbContext database, IAuditEventCollector collector, IAuditTransactionWriterV1 writer)
        {
            if (transaction is null || !ReferenceEquals(database, owner.MixedDatabase) || !ReferenceEquals(collector, owner.MixedCollector)
                || !ReferenceEquals(writer, owner.MixedWriter) || !ReferenceEquals(database.Database.CurrentTransaction, transaction) || Committed || Aborted)
                throw new InvalidOperationException("Mixed supplier revision cannot adopt another owner scope.");
        }
        internal async Task<bool> ReplayAsync(CancellationToken token)
        { Answer = await owner.ReadMixedSupplierOriginalAsync(id, request, envelope, hash, Fence, token); return Answer is not null; }
        internal async Task PrepareAsync(StoredPurchaseOrder loaded, CancellationToken token)
        {
            if (parent is not null || loaded.Order.ServiceMemberships.Count == 0)
                throw new PurchasingServiceException(503, "service_unavailable");
            var expected = PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion);
            if (loaded.Order.Version != expected) throw new PurchasingVersionConflictException(expected, loaded.Order.Version);
            if (loaded.Order.Status != PurchaseOrderStatus.Approved)
                throw new PurchasingRuleException(PurchaseOrder.SupplierRevisionBlocked, "Yalnız onaylı sipariş için cari revizyonu açılabilir.");
            parent = loaded; original = await owner.ReadCurrentMixedChildrenAsync(loaded, Fence, token, requireDraft: false);
            var selected = request.MixedServices!;
            if (selected.Count != original.Count || original.Any(child => !selected.Any(x => x.ServiceOrderId == child.OrderId
                && x.ExpectedVersion == child.Version.ToString(CultureInfo.InvariantCulture) && x.LineIds.Count == child.Lines.Count
                && x.LineIds.ToHashSet().SetEquals(child.Lines.Select(line => line.LineId)))))
                throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            var ids = original.Select(x => x.OrderId).ToArray();
            var invoice = await ReadMixedOwnerAsync(() => owner.MixedDatabase.InvoiceOrderBindings.AsNoTracking()
                .AnyAsync(x => x.OrderId == id, token), Fence, token);
            var serviceInvoice = await ReadMixedOwnerAsync(() => owner.MixedDatabase.ServiceInvoiceBindings.AsNoTracking()
                .AnyAsync(x => ids.Contains(x.OrderId), token), Fence, token);
            if (invoice || serviceInvoice || loaded.Order.Acceptances.Count != 0 || original.Any(x => x.Acceptances.Count != 0))
                throw new PurchasingRuleException(PurchaseOrder.SupplierRevisionBlocked, "Kabul veya fatura bulunan siparişin carisi değiştirilemez.");
            var facts = await prepare(original, token); await Fence(token);
            if (facts is null || facts.Count != original.Count || facts.Any(x => x is null || x.CompanyId != loaded.Order.CompanyId
                || x.ActorId != envelope.ActorId.Trim() || x.Original is null || x.Candidate is null))
                throw new PurchasingServiceException(503, "service_unavailable");
            var detached = new List<PurchaseServiceOrderState>();
            foreach (var child in original)
            {
                var prepared = facts.SingleOrDefault(x => x.Original.OrderId == child.OrderId) ?? throw new PurchasingServiceException(503, "service_unavailable");
                if (!PurchaseMixedOrderApprovalFactsV1.SameChild(child, prepared.Original)) throw new PurchasingServiceException(503, "service_unavailable");
                var value = prepared.Candidate;
                if (!MixedChildShape(value)) throw new PurchasingServiceException(503, "service_unavailable");
                detached.Add(value with { Lines = Array.AsReadOnly(value.Lines.ToArray()), Materials = Array.AsReadOnly(value.Materials.ToArray()),
                    ExpectedOutputs = Array.AsReadOnly(value.ExpectedOutputs.ToArray()), Acceptances = Array.AsReadOnly(value.Acceptances.ToArray()) });
            }
            candidates = detached.AsReadOnly(); await Fence(token);
            var latest = await owner.ReadCurrentMixedChildrenAsync(loaded, Fence, token, requireDraft: false);
            if (!original.SequenceEqual(latest, MixedChildComparer.Instance)) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
        }
        internal void Revise(PurchaseOrder actual, DateTimeOffset at)
        {
            if (parent is null || original is null || candidates is null || !ReferenceEquals(parent.Order, actual))
                throw new InvalidOperationException("Actual complete supplier revision preparation is required.");
            var changed = actual.ReviseSupplierWithServiceOrders(PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion), parent.CompanyAccountId,
                original, candidates, request.MixedServices!.Select(x => new PurchaseOrderServiceSelection(x.ServiceOrderId,
                    PurchaseMixedOrderApprovalIntentV1.Version(x.ExpectedVersion), x.LineIds)).ToArray(), request.SupplierPartyId, request.CompanyAccountId,
                PurchaseOrderInvoiceHistory.None, envelope.ActorId.Trim(), request.Reason.Trim(), at);
            var origin = new PurchaseServiceSupplierRevisionOrigin(id, actual.Version, IdempotencyKeyHash(envelope.IdempotencyKey));
            revised = Array.AsReadOnly(changed.Select(x => x with { SupplierRevisionOrigin = origin }).ToArray());
        }
        internal async Task StageChildrenAsync(CancellationToken token)
        {
            if (original is null || revised is null) throw new InvalidOperationException("Native revision was not prepared.");
            for (var i = 0; i < original.Count; i++)
            {
                await Fence(token);
                await PurchaseServiceOrderStoreV1.StageMixedSupplierRevisionAsync(owner.MixedDatabase, owner.MixedCollector,
                    owner.MixedWriter!, this, original[i], revised[i], envelope, request.Reason.Trim(), token);
                await Fence(token);
            }
        }
        internal string RecordAfter(StoredPurchaseOrder actual, DateTimeOffset at)
        {
            if (parent is null || original is null || revised is null || !ReferenceEquals(parent.Order, actual.Order))
                throw new InvalidOperationException("Actual linked revision results are required.");
            var receipt = new PurchaseMixedSupplierRevisionReceiptV1(actual.Order.CompanyId, id, envelope.ActorId.Trim(), IdempotencyKeyHash(envelope.IdempotencyKey),
                hash, envelope.CorrelationId.Trim(), at, PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion), actual.Order.Version,
                parent.CompanyAccountId, request.CompanyAccountId, request, actual.Order.SupplierRevisions.Last(),
                Array.AsReadOnly(original.Select((x, i) => new PurchaseMixedSupplierRevisionChildV1(x, revised[i])).ToArray()),
                project(actual, PurchasingPriceDisclosureV1.Visible), project(actual, PurchasingPriceDisclosureV1.Withheld));
            return JsonSerializer.Serialize(new MixedSupplierAuditAfter(JsonSerializer.Deserialize<JsonElement>(Snapshot(actual.Order)), receipt), Json);
        }
        internal async Task SaveAsync(CancellationToken token)
        {
            RequireParticipant(owner.MixedDatabase, owner.MixedCollector, owner.MixedWriter!);
            await Fence(token); await owner.MixedWriter!.WritePendingAsync(transaction!.GetDbTransaction(), token);
            await Fence(token); await owner.MixedDatabase.SaveChangesAsync(token); await Fence(token);
        }
        internal async Task VerifySavedAsync(CancellationToken token)
        {
            Answer = await owner.ReadMixedSupplierOriginalAsync(id, request, envelope, hash, Fence, token) ?? throw new PurchasingServiceException(503, "service_unavailable");
            Answer = Answer with { IsReplay = false }; await Fence(token);
            if (owner.MixedDatabase.ChangeTracker.HasChanges() || owner.MixedWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Mixed supplier revision did not flush its owned work.");
        }
    }
}
