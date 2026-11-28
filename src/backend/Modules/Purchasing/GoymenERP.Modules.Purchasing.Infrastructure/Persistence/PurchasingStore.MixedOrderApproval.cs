using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchaseMixedOrderApprovalStoreV1
{
    private PurchasingDbContext MixedDatabase => dbContext;
    private IAuditEventCollector MixedCollector => auditEvents;
    private IAuditTransactionWriterV1? MixedWriter => decisionAuditWriter;
    public bool MixedApprovalEntryIsClean => CleanMixedPreviewEntry();

    public async Task<PurchaseMixedOrderApprovalStoreAnswerV1> ApproveMixedOrderAsync(Guid orderId, ApprovePurchaseOrderRequest request,
        PurchasingWriteEnvelope envelope, string requestHash, bool cleanBeforeAuthority,
        Func<StoredPurchaseOrder, IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<PurchaseMixedOrderApprovalPreparationV1>> prepare,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(freshFence);
        RequireEnvelope(envelope); RequireHash(requestHash);
        if (!cleanBeforeAuthority || !CleanMixedPreviewEntry()) throw new PurchasingServiceException(503, "service_unavailable");
        var frozen = FreezeMixedRequest(request);
        if (PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, orderId, frozen) != requestHash)
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Mixed approval content changed before admission.");
        var company = RequireResolvedCompany();
        async Task Fence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
            await freshFence(token); token.ThrowIfCancellationRequested();
            if (RequireResolvedCompany() != company) throw new PurchasingServiceException(403, "forbidden");
        }
        var execution = new MixedApprovalExecution(this, frozen, envelope, requestHash, prepare, project, Fence);
        try
        {
            await MutateOrderGuardedCoreAsync(envelope, requestHash, PurchasingCommands.OrderApproveV1, orderId,
                execution.Approve, new("purchasing.purchase-order.approved.v1", "Approve", "Satınalma siparişi onaylandı"),
                Fence, execution, cancellationToken);
            return execution.Answer ?? throw new PurchasingServiceException(503, "service_unavailable");
        }
        catch (Exception failure) when (!execution.CommitStarted && !execution.Committed && execution.CleanupComplete && execution.Aborted && ReferenceEquals(execution.OriginalFailure, failure) && MixedWinnerFailure(failure))
        {
            // Exactly one fresh readonly original-key lookup after the owned transaction has disposed/cleaned.
            // A second error, cleanup fault or an absent winner never restarts a mutation.
            if (!CleanMixedPreviewEntry()) throw;
            await ReadOnlyFence(cancellationToken);
            var winner = await ReadMixedOriginalAsync(orderId, frozen, envelope, requestHash, ReadOnlyFence, cancellationToken);
            await ReadOnlyFence(cancellationToken);
            return winner ?? throw new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange);
        }
        async Task ReadOnlyFence(CancellationToken ct)
        {
            await Fence(ct);
            if (!CleanMixedPreviewEntry()) throw new PurchasingServiceException(503, "service_unavailable");
        }
    }

    internal static ApprovePurchaseOrderRequest FreezeMixedRequest(ApprovePurchaseOrderRequest request) => PurchaseMixedOrderApprovalIntentV1.Freeze(request);
    private static long RequireMixedVersion(string text) => PurchaseMixedOrderApprovalIntentV1.Version(text);
    private static bool MixedDeadlock(Exception failure) => failure is SqlException { Number: 1205 }
        || failure is DbUpdateException { InnerException: SqlException { Number: 1205 } };
    private static bool MixedWinnerFailure(Exception failure) => MixedDeadlock(failure) || failure is DbUpdateConcurrencyException
        || failure is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } }
        || failure is PurchasingStoreException { Code: PurchasingStoreCodes.ConcurrentChange };

    // Per-call state, never a mutable service field. Only the existing parent-owned guard may enter this participant.
    internal sealed class MixedApprovalExecution(PurchasingStore owner, ApprovePurchaseOrderRequest request,
        PurchasingWriteEnvelope envelope, string hash,
        Func<StoredPurchaseOrder, IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<PurchaseMixedOrderApprovalPreparationV1>> prepare,
        Func<StoredPurchaseOrder, string, PurchaseOrderDetailView> project, Func<CancellationToken, Task> fence) : MixedOwnedExecution(fence)
    {
        internal ApprovePurchaseOrderRequest Request { get; } = request;
        internal PurchaseMixedOrderApprovalStoreAnswerV1? Answer { get; set; }
        private IDbContextTransaction? transaction;
        private StoredPurchaseOrder? parent;
        private IReadOnlyList<PurchaseServiceOrderState>? original;
        private PurchaseMixedOrderApprovalPreparationV1? preparation;
        private IReadOnlyList<PurchaseServiceOrderState>? approved;

        internal override void Enter(IDbContextTransaction owned)
        {
            if (transaction is not null || !ReferenceEquals(owner.MixedDatabase.Database.CurrentTransaction, owned)
                || owner.MixedWriter is null || owner.MixedWriter.HasPendingDrafts || owner.MixedDatabase.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Mixed participant requires the same clean parent-owned transaction.");
            transaction = owned;
        }
        internal void RequireParticipant(PurchasingDbContext database, IAuditEventCollector collector, IAuditTransactionWriterV1 writer)
        {
            if (!ReferenceEquals(database, owner.MixedDatabase) || !ReferenceEquals(collector, owner.MixedCollector)
                || !ReferenceEquals(writer, owner.MixedWriter) || transaction is null
                || !ReferenceEquals(database.Database.CurrentTransaction, transaction) || Committed || Aborted)
                throw new InvalidOperationException("A mixed participant cannot borrow another owner or scope.");
        }
        internal async Task PrepareAsync(StoredPurchaseOrder loaded, CancellationToken token)
        {
            if (parent is not null || loaded.Order.ServiceMemberships.Count == 0) throw new PurchasingServiceException(503, "service_unavailable");
            var expected = RequireMixedVersion(Request.ExpectedVersion);
            if (loaded.Order.Version != expected) throw new PurchasingVersionConflictException(expected, loaded.Order.Version);
            if (loaded.Order.Status != PurchaseOrderStatus.Draft) throw new PurchasingRuleException(PurchasingRuleCodes.OrderNotDraft, "Only native Draft can be approved.");
            parent = loaded;
            original = await owner.ReadCurrentMixedChildrenAsync(loaded, Fence, token);
            var selections = Request.MixedServices ?? throw new PurchasingServiceException(503, "service_unavailable");
            if (selections.Count != original.Count || original.Any(child => !selections.Any(selected =>
                selected.ServiceOrderId == child.OrderId && selected.ExpectedVersion == child.Version.ToString(CultureInfo.InvariantCulture)
                && selected.LineIds.Count == child.Lines.Count && selected.LineIds.ToHashSet().SetEquals(child.Lines.Select(x => x.LineId)))))
                throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            await Fence(token);
            var supplied = await prepare(loaded, original, token);
            if (supplied?.Services is null || supplied.TheoreticalKilograms is null || supplied.Services.Count != original.Count
                || supplied.Services.Any(child => child is null || child.Lines is null || child.Materials is null || child.ExpectedOutputs is null || child.Acceptances is null))
                throw new PurchasingServiceException(503, "service_unavailable");
            // Detach the returned owner collections immediately, before the next async authority callback.
            preparation = new(Array.AsReadOnly(supplied.Services.Select(child => child with {
                Lines = Array.AsReadOnly(child.Lines.ToArray()), Materials = Array.AsReadOnly(child.Materials.ToArray()),
                ExpectedOutputs = Array.AsReadOnly(child.ExpectedOutputs.ToArray()), Acceptances = Array.AsReadOnly(child.Acceptances.ToArray()) }).ToArray()),
                new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, PurchaseOrderLineTheoreticalKilograms>(supplied.TheoreticalKilograms.ToDictionary(x => x.Key, x => x.Value)));
            await Fence(token);
            if (preparation.Services.Where((child, i) => !PurchaseMixedOrderApprovalFactsV1.SameChild(original[i], child, true)).Any())
                throw new PurchasingServiceException(503, "service_unavailable");
            var latest = await owner.ReadCurrentMixedChildrenAsync(loaded, Fence, token);
            if (!original.SequenceEqual(latest, MixedChildComparer.Instance)) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
        }
        internal void Approve(PurchaseOrder actual, DateTimeOffset at)
        {
            if (parent is null || !ReferenceEquals(parent.Order, actual) || preparation is null || original is null || approved is not null)
                throw new InvalidOperationException("The complete actual child preparation must precede the parent transition.");
            approved = actual.ApproveSelectedServiceOrders(RequireMixedVersion(Request.ExpectedVersion), parent.CompanyAccountId,
                preparation.Services, Request.MixedServices!.Select(x => new PurchaseOrderServiceSelection(x.ServiceOrderId,
                    RequireMixedVersion(x.ExpectedVersion), x.LineIds)).ToArray(), envelope.ActorId.Trim(), at,
                SettingsCurrencyIdentitiesV1.TurkishLira, preparation.TheoreticalKilograms);
        }
        internal async Task StageChildrenAsync(CancellationToken token)
        {
            if (original is null || approved is null) throw new InvalidOperationException("Native child transitions are missing.");
            for (var i = 0; i < original.Count; i++)
            {
                await Fence(token);
                await PurchaseServiceOrderStoreV1.StageMixedApprovalAsync(owner.MixedDatabase, owner.MixedCollector, owner.MixedWriter!,
                    this, original[i], approved[i], envelope, token);
                await Fence(token);
            }
        }
        internal string RecordAfter(StoredPurchaseOrder actual, DateTimeOffset at)
        {
            if (parent is null || original is null || preparation is null || approved is null || !ReferenceEquals(actual.Order, parent.Order))
                throw new InvalidOperationException("The parent and all actual child results must share one approval.");
            var recorded = new PurchaseMixedOrderRecordedApprovalV1(actual.Order.CompanyId, actual.Order.Id, envelope.ActorId.Trim(),
                IdempotencyKeyHash(envelope.IdempotencyKey), hash, envelope.CorrelationId.Trim(), at, Request,
                Array.AsReadOnly(actual.Order.ServiceMemberships.ToArray()), Array.AsReadOnly(original.Select((before, i) =>
                    new PurchaseMixedOrderRecordedChildV1(before, preparation.Services[i], approved[i])).ToArray()),
                project(actual, PurchasingPriceDisclosureV1.Visible), project(actual, PurchasingPriceDisclosureV1.Withheld));
            return JsonSerializer.Serialize(new MixedAuditAfter(JsonSerializer.Deserialize<JsonElement>(Snapshot(actual.Order)), recorded), Json);
        }
        internal async Task SaveAsync(CancellationToken token)
        {
            RequireParticipant(owner.MixedDatabase, owner.MixedCollector, owner.MixedWriter!);
            await Fence(token);
            await owner.MixedWriter!.WritePendingAsync(transaction!.GetDbTransaction(), token);
            await Fence(token);
            await owner.MixedDatabase.SaveChangesAsync(token);
            await Fence(token);
            if (owner.MixedWriter!.HasPendingDrafts) throw new InvalidOperationException("Owned Audit drafts were not flushed.");
        }
    }
}
