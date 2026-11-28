using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchaseMixedServicePriceRevisionStoreV1
{
    private sealed record MixedServicePriceAuditAfter(JsonElement NativeOrder, PurchaseMixedServicePriceReceiptV1 PriceRevision);
    public bool MixedServicePriceEntryIsClean => CleanMixedPreviewEntry();

    public async Task<PurchaseMixedServicePriceAnswerV1> ReviseMixedServicePricesAsync(Guid id, RevisePurchaseMixedServicePricesRequest mutable,
        PurchasingWriteEnvelope envelope, string hash, bool cleanBeforeAuthority,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> freshFence,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(freshFence);
        RequireEnvelope(envelope); RequireHash(hash);
        if (!cleanBeforeAuthority || !CleanMixedPreviewEntry()) throw MixedSupplierUnavailable();
        var request = PurchaseMixedServicePriceIntentV1.Freeze(mutable);
        if (PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, id, request) != hash)
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Mixed service price content changed.");
        var company = RequireResolvedCompany();
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
            await freshFence(ct); ct.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
        }
        var execution = new MixedServicePriceExecution(this, id, request, envelope, hash, project, Fence);
        try
        {
            await MutateOrderGuardedCoreAsync(envelope, hash, PurchaseMixedServicePriceIntentV1.Command, id, execution.Revise,
                new(PurchaseMixedServicePriceIntentV1.Event, "ReviseServicePrices", request.Reason.Trim()), Fence, null, token, servicePrices: execution);
            return execution.Answer ?? throw MixedSupplierUnavailable();
        }
        catch (Exception failure) when (!execution.CommitStarted && !execution.Committed && execution.Aborted && execution.CleanupComplete
            && ReferenceEquals(execution.OriginalFailure, failure) && MixedWinnerFailure(failure))
        {
            if (!CleanMixedPreviewEntry()) throw;
            await ReadOnlyFence(token);
            var winner = await ReadMixedServicePriceOriginalAsync(id, request, envelope, hash, ReadOnlyFence, token);
            await ReadOnlyFence(token);
            return winner ?? throw new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange);
        }
        async Task ReadOnlyFence(CancellationToken ct)
        { await Fence(ct); if (!CleanMixedPreviewEntry()) throw MixedSupplierUnavailable(); }
    }

    internal sealed class MixedServicePriceExecution(PurchasingStore owner, Guid id, RevisePurchaseMixedServicePricesRequest request,
        PurchasingWriteEnvelope envelope, string hash, Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project,
        Func<CancellationToken, Task> fence) : MixedOwnedExecution(fence)
    {
        private IDbContextTransaction? transaction;
        private StoredPurchaseOrder? parent;
        private IReadOnlyList<PurchaseServiceOrderState>? original;
        private IReadOnlyList<PurchaseServiceOrderState>? revised;
        private IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal>>? prices;
        internal PurchaseMixedServicePriceAnswerV1? Answer { get; private set; }
        internal override void Enter(IDbContextTransaction owned)
        {
            if (transaction is not null || owner.MixedWriter is null || owner.MixedWriter.HasPendingDrafts
                || owner.MixedDatabase.ChangeTracker.HasChanges() || !ReferenceEquals(owner.MixedDatabase.Database.CurrentTransaction, owned))
                throw new InvalidOperationException("Mixed service price revision requires the same clean parent owner scope.");
            transaction = owned;
        }
        internal void RequireParticipant(PurchasingDbContext database, IAuditEventCollector collector, IAuditTransactionWriterV1 writer)
        {
            if (transaction is null || !ReferenceEquals(database, owner.MixedDatabase) || !ReferenceEquals(collector, owner.MixedCollector)
                || !ReferenceEquals(writer, owner.MixedWriter) || !ReferenceEquals(database.Database.CurrentTransaction, transaction) || Committed || Aborted)
                throw new InvalidOperationException("Mixed price revision cannot adopt another scope.");
        }
        internal async Task<bool> ReplayAsync(CancellationToken token)
        { Answer = await owner.ReadMixedServicePriceOriginalAsync(id, request, envelope, hash, Fence, token); return Answer is not null; }
        internal async Task PrepareAsync(StoredPurchaseOrder loaded, CancellationToken token)
        {
            if (parent is not null || loaded.Order.ServiceMemberships.Count == 0) throw MixedSupplierUnavailable();
            var expected = PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion);
            if (loaded.Order.Version != expected) throw new PurchasingVersionConflictException(expected, loaded.Order.Version);
            if (loaded.Order.Status != PurchaseOrderStatus.Approved)
                throw new PurchasingRuleException(PurchasingRuleCodes.OrderNotApproved, "Only the approved native parent can enter price revision.");
            parent = loaded; original = await owner.ReadCurrentMixedChildrenAsync(loaded, Fence, token, requireDraft: false);
            var previous = await owner.ReadMixedRestoreReceiptAsync(owner.MixedDatabase.Orders.Local.Single(x => x.Id == id), Fence, token)
                ?? throw MixedSupplierUnavailable();
            if (previous.ResultVersion != loaded.Order.ApprovalVersion || previous.Original.Children.Count != original.Count
                || original.Any(child => !HasNativeApproval(child, previous))) throw MixedSupplierUnavailable();
            prices = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal>>(
                request.Changes.ToDictionary(x => x.ServiceOrderId, x => PurchaseServiceOrderServiceV1.ReadNativeRevisionPrices(x.Lines)));
            var latest = await owner.ReadCurrentMixedChildrenAsync(loaded, Fence, token, requireDraft: false);
            if (!original.SequenceEqual(latest, MixedChildComparer.Instance)) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
        }
        internal void Revise(PurchaseOrder actual, DateTimeOffset at)
        {
            if (parent is null || !ReferenceEquals(parent.Order, actual) || original is null || prices is null || revised is not null)
                throw new InvalidOperationException("Complete actual child selection must precede parent price revision.");
            revised = actual.ReviseSelectedServicePrices(PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion), parent.CompanyAccountId,
                original, request.Services.Select(x => new PurchaseOrderServiceSelection(x.ServiceOrderId,
                    PurchaseMixedOrderMembershipV1.SelectedChildVersion(x.ExpectedVersion), x.LineIds)).ToArray(), prices,
                envelope.ActorId.Trim(), request.Reason, at);
        }
        internal async Task StageChildrenAsync(CancellationToken token)
        {
            if (original is null || revised is null) throw new InvalidOperationException("Native child revision is missing.");
            for (var i = 0; i < original.Count; i++)
            {
                if (!request.Changes.Any(x => x.ServiceOrderId == original[i].OrderId)) continue;
                await Fence(token);
                await PurchaseServiceOrderStoreV1.StageMixedServicePriceRevisionAsync(owner.MixedDatabase, owner.MixedCollector,
                    owner.MixedWriter!, this, original[i], revised[i], envelope, request.Reason, token);
                await Fence(token);
            }
        }
        internal string RecordAfter(StoredPurchaseOrder actual, DateTimeOffset at)
        {
            if (parent is null || original is null || revised is null || !ReferenceEquals(actual.Order, parent.Order))
                throw new InvalidOperationException("Actual parent and child revision results are required.");
            var receipt = new PurchaseMixedServicePriceReceiptV1(actual.Order.CompanyId, id, envelope.ActorId.Trim(), IdempotencyKeyHash(envelope.IdempotencyKey),
                hash, envelope.CorrelationId.Trim(), at, PurchaseMixedOrderApprovalIntentV1.Version(request.ExpectedVersion), actual.Order.Version,
                request, actual.Order.ServicePriceRevisions.Last(), Array.AsReadOnly(original.Select((x, i) =>
                    new PurchaseMixedServicePriceRecordedChildV1(x, revised[i])).ToArray()), project(actual, PurchasingPriceDisclosureV1.Visible),
                project(actual, PurchasingPriceDisclosureV1.Withheld));
            return JsonSerializer.Serialize(new MixedServicePriceAuditAfter(JsonSerializer.Deserialize<JsonElement>(Snapshot(actual.Order)), receipt), Json);
        }
        internal async Task SaveAsync(CancellationToken token)
        {
            RequireParticipant(owner.MixedDatabase, owner.MixedCollector, owner.MixedWriter!);
            await Fence(token); await owner.MixedWriter!.WritePendingAsync(transaction!.GetDbTransaction(), token);
            await Fence(token); await owner.MixedDatabase.SaveChangesAsync(token); await Fence(token);
        }
        internal async Task VerifySavedAsync(CancellationToken token)
        {
            Answer = await owner.ReadMixedServicePriceOriginalAsync(id, request, envelope, hash, Fence, token) ?? throw MixedSupplierUnavailable();
            Answer = Answer with { IsReplay = false }; await Fence(token);
            if (owner.MixedDatabase.ChangeTracker.HasChanges() || owner.MixedWriter!.HasPendingDrafts)
                throw new InvalidOperationException("Mixed price revision did not flush its owned work.");
        }
    }

    private static bool HasNativeApproval(PurchaseServiceOrderState current, PurchaseMixedOrderApprovalReceiptV1 prior)
    {
        var old = prior.Original.Children.SingleOrDefault(x => x.After.OrderId == current.OrderId)?.After;
        return old is not null && HasNativeApprovalState(current, old);
    }
    private static bool HasNativeApprovalState(PurchaseServiceOrderState current, PurchaseServiceOrderState old) => current.Status == PurchaseServiceOrderStatus.Approved && old.Status == PurchaseServiceOrderStatus.Approved
            && old.CompanyId == current.CompanyId && old.OrderId == current.OrderId && old.Version <= current.Version
            && old.PreparedBy == current.PreparedBy && old.ApprovedBy == current.ApprovedBy
            && old.SupplierPartyId == current.SupplierPartyId && old.CompanyAccountId == current.CompanyAccountId
            && old.Lines.Count == current.Lines.Count && old.Lines.All(line => current.Lines.Count(x => x.LineId == line.LineId
                && (x with { AcceptedQuantity = line.AcceptedQuantity }) == line) == 1)
            && old.Money == current.Money && old.Materials.SequenceEqual(current.Materials) && old.ExpectedOutputs.SequenceEqual(current.ExpectedOutputs)
            && old.PriceRevision == current.PriceRevision && old.PartyVersion == current.PartyVersion && old.AccountVersion == current.AccountVersion
            && old.SupplierRevisionOrigin == current.SupplierRevisionOrigin
            && (old with { Version = current.Version, UpdatedAtUtc = current.UpdatedAtUtc, Lines = current.Lines, Acceptances = current.Acceptances }) == current
            && old.Acceptances.All(x => current.Acceptances.Contains(x))
            && old.Lines.All(x => current.Lines.Single(line => line.LineId == x.LineId).AcceptedQuantity >= x.AcceptedQuantity);
}
