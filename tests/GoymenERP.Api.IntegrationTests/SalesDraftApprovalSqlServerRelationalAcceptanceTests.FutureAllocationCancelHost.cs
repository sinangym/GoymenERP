using System.Data;
using System.Data.Common;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// K3-A P2-9: the X1 sales-cancellation bridge (<see cref="SalesFutureAllocationReleaseHostParticipant"/>) and the F17-4 purchasing
/// link bridge (<see cref="PurchasingFutureAllocationLinkHostParticipant"/>) over the REAL Sales store transaction, the REAL Inventory
/// release session and REAL SQL Server locks. Owner product/unit/supply/schedule snapshots are explicitly synthetic (the
/// FutureDemandHost seed); no test below claims a Purchasing business workflow, an HTTP route or browser acceptance.
/// </summary>
public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    private static readonly SalesDraftStateAudit CancelHostAudit = new("sales.order-draft.cancelled.v1", "Cancel", "SENTETIK iptal");

    [SqlServerFact]
    public async Task A_cancelled_order_releases_its_real_future_allocation_in_the_sales_transaction_once_and_replay_releases_nothing()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var seed = await SeedFutureDemandHostAsync(run, draftId);

        var cancelled = await CancelWithHostAsync(run, draftId, "host-cancel", CancelHost());
        Assert.Equal((SalesDraftState.Cancelled, 9L), (cancelled!.Draft.State, cancelled.Draft.Version));
        var replay = await CancelWithHostAsync(run, draftId, "host-cancel",
            new ThrowingCancelHost("SENTETIK replay must not ask the owner again"));
        Assert.Equal(SalesDraftWriteOutcome.Replayed, replay!.Outcome);

        // One release in the cancellation's company, under the Sales command's own envelope (permission, key, correlation).
        Assert.Equal((3, 0m, 1.25m, 2L, InventoryFutureAllocationReleaseReasonsV1.SalesLineCancelled, InventoryFutureAllocationOriginsV1.Sales),
            await AllocationAsync(run, seed.AllocationId));
        Assert.Equal(CompanyA, await run.ScalarAsync<Guid>("SELECT CompanyId FROM inventory.FutureSupplyAllocations WHERE Id=@id",
            ("@id", seed.AllocationId)));
        Assert.Equal(0m, await run.ScalarAsync<decimal>("SELECT ActiveAllocated FROM inventory.FutureSupplyLines WHERE PurchaseOrderLineId=@line",
            ("@line", seed.PurchaseLineId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType=@event AND CompanyId=@company",
            ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType), ("@company", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event AND PermissionCode=@permission AND CompanyId=@company",
            ("@id", seed.AllocationId.ToString("D")), ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType),
            ("@permission", SalesPermissionManifestV1.OrdersCancel), ("@company", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftCancellations WHERE DraftId=@id", ("@id", draftId)));
    }

    [SqlServerFact]
    public async Task A_sales_cancellation_save_failure_rolls_back_the_real_release_ledger_audit_and_outbox()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var seed = await SeedFutureDemandHostAsync(run, draftId);

        await Assert.ThrowsAsync<FutureDemandSaveFault>(() =>
            CancelWithHostAsync(run, draftId, "host-cancel-fail", CancelHost(), new FailAfterFutureDemandSave()));

        await AssertCancelHostZeroEffectAsync(run, draftId, seed, "host-cancel-fail");
    }

    [SqlServerFact]
    public async Task Another_orders_allocation_or_a_busy_purchase_line_refuses_the_cancellation_with_zero_effect_and_records_nothing()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var seed = await SeedFutureDemandHostAsync(run, draftId);

        // The order the owner answers is not the order being cancelled: never released on its word (503, not terminal).
        var foreign = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            CancelWithHostAsync(run, draftId, "host-cancel-foreign", CancelHost(), orderIdOverride: Guid.NewGuid()));
        Assert.Equal((503, InventoryFutureAllocationCodesV1.ServiceUnavailable, false), (foreign.Status, foreign.Code, foreign.Terminal));
        await AssertCancelHostZeroEffectAsync(run, draftId, seed, "host-cancel-foreign");

        // A purchase line held by another open transaction: busy (409, not terminal) — the retry asks again.
        await using var holder = new SqlConnection(run.Factory.DatabaseConnectionString);
        await holder.OpenAsync();
        await using var held = (SqlTransaction)await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await new PurchaseLineSerializationLockHostBinder(FutureDemandHostCompany()).Bind(holder, held).AcquireAsync(seed.PurchaseLineId);
        var busy = await Assert.ThrowsAsync<SalesFutureAllocationReleaseException>(() =>
            CancelWithHostAsync(run, draftId, "host-cancel-busy", CancelHost()));
        Assert.Equal((409, PurchaseLineSerializationLockContractV1.BusyCode, false), (busy.Status, busy.Code, busy.Terminal));
        await held.RollbackAsync();
        await AssertCancelHostZeroEffectAsync(run, draftId, seed, "host-cancel-busy");
    }

    /// <summary>
    /// K3-A P3 (lock order): between the first read of the active allocations and the purchase line lock, a purchase-side link drop
    /// (F17-4, the REAL purchasing bridge on its own connection) releases the allocation and commits. The cancellation reads the
    /// allocations again under the purchase line lock, finds nothing to release and commits — never a stale-version terminal 409
    /// recorded under the Sales key. The allocation is released exactly once, by Purchasing.
    /// </summary>
    [SqlServerFact]
    public async Task A_purchase_link_drop_racing_the_cancellation_wins_once_and_the_cancellation_records_no_terminal_conflict()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var seed = await SeedFutureDemandHostAsync(run, draftId);
        var barrier = new PausingPurchaseLocks(new PurchaseLineSerializationLockHostBinder(FutureDemandHostCompany()));
        var cancel = CancelWithHostAsync(run, draftId, "host-cancel-race", CancelHost(barrier));
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                var released = await LinkHost().ReleaseAsync(transaction, new PurchaseFutureAllocationLinkReleaseV1(seed.PurchaseOrderId,
                    seed.AllocationId, 1, PurchaseFutureAllocationLinkReasonsV1.LinkDropped, "SENTETIK bağ düşürüldü",
                    PurchasingHostEnvelope("host-link-race")));
                Assert.Equal(seed.AllocationId, released.AllocationId);
                await transaction.CommitAsync();
            }

            barrier.Resume.TrySetResult();
            var cancelled = await cancel;
            Assert.Equal((SalesDraftState.Cancelled, 9L), (cancelled!.Draft.State, cancelled.Draft.Version));
        }
        finally
        {
            barrier.Resume.TrySetResult();
            try { await cancel; } catch (Exception) { /* the assertion above already reported the cancellation's outcome */ }
        }

        Assert.Equal((3, 0m, 1.25m, 2L, PurchaseFutureAllocationLinkReasonsV1.LinkDropped, InventoryFutureAllocationOriginsV1.Purchasing),
            await AllocationAsync(run, seed.AllocationId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType=@event",
            ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash=@key AND Outcome<>1",
            ("@key", Hash("host-cancel-race").ToUpperInvariant())));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftCancellations WHERE DraftId=@id", ("@id", draftId)));
    }

    /// <summary>
    /// K3-A P2-9: the F17-4 purchasing bridge on the REAL Inventory session inside a host transaction it does not own: it lists the
    /// active link in the context company, refuses another order's view and a stale version (terminal, Purchasing's own code) with
    /// zero effect, and releases with Purchasing's origin and envelope only when the host commits.
    /// </summary>
    [SqlServerFact]
    public async Task The_purchasing_link_bridge_lists_refuses_and_releases_only_inside_the_host_transaction_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var (draftId, _) = await PrepareFutureDemandAcceptanceAsync(run);
        var seed = await SeedFutureDemandHostAsync(run, draftId);
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();

        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            var link = Assert.Single(await LinkHost().ListActiveAsync(transaction, seed.PurchaseOrderId, [seed.PurchaseLineId]));
            Assert.Equal((seed.AllocationId, 1L, seed.SalesOrderId, seed.SalesLineId, "1.25"),
                (link.AllocationId, link.Version, link.SalesOrderId, link.SalesOrderLineId, link.RemainingQuantity));
            var foreign = await Assert.ThrowsAsync<PurchaseFutureAllocationLinkException>(() =>
                LinkHost().ListActiveAsync(transaction, Guid.NewGuid(), [seed.PurchaseLineId]));
            Assert.Equal((503, false), (foreign.Status, foreign.Terminal));
            var stale = await Assert.ThrowsAsync<PurchaseFutureAllocationLinkException>(() => LinkHost().ReleaseAsync(transaction,
                new PurchaseFutureAllocationLinkReleaseV1(seed.PurchaseOrderId, seed.AllocationId, 2,
                    PurchaseFutureAllocationLinkReasonsV1.LinkDropped, "SENTETIK eski sürüm", PurchasingHostEnvelope("host-link-stale"))));
            Assert.Equal((409, PurchasingProblemCodesV1.FutureAllocationVersionConflict, true), (stale.Status, stale.Code, stale.Terminal));
            await transaction.RollbackAsync();
        }

        Assert.Equal((1, 1.25m, 0m, 1L, (string?)null, (string?)null), await AllocationAsync(run, seed.AllocationId));
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await LinkHost().ReleaseAsync(transaction, new PurchaseFutureAllocationLinkReleaseV1(seed.PurchaseOrderId, seed.AllocationId, 1,
                PurchaseFutureAllocationLinkReasonsV1.SourceCancelled, "SENTETIK kaynak iptal", PurchasingHostEnvelope("host-link-release")));
            // Rolled back by the host: nothing of the release stays.
            await transaction.RollbackAsync();
        }

        Assert.Equal((1, 1.25m, 0m, 1L, (string?)null, (string?)null), await AllocationAsync(run, seed.AllocationId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType=@event",
            ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await LinkHost().ReleaseAsync(transaction, new PurchaseFutureAllocationLinkReleaseV1(seed.PurchaseOrderId, seed.AllocationId, 1,
                PurchaseFutureAllocationLinkReasonsV1.SourceCancelled, "SENTETIK kaynak iptal", PurchasingHostEnvelope("host-link-release")));
            await transaction.CommitAsync();
        }

        Assert.Equal((3, 0m, 1.25m, 2L, PurchaseFutureAllocationLinkReasonsV1.SourceCancelled, InventoryFutureAllocationOriginsV1.Purchasing),
            await AllocationAsync(run, seed.AllocationId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event AND PermissionCode=@permission AND CompanyId=@company",
            ("@id", seed.AllocationId.ToString("D")), ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType),
            ("@permission", PurchasingHostPermission), ("@company", CompanyA)));
    }

    private const string PurchasingHostPermission = "SENTETIK-purchasing-permission";

    private static PurchasingWriteEnvelope PurchasingHostEnvelope(string key) =>
        new("SENTETIK-purchaser", PurchasingHostPermission, ["SENTETIK-PURCHASING"], "SENTETIK-purchasing-correlation", null, key, "Web");

    private static async Task<SalesDraftStateResult?> CancelWithHostAsync(Run run, Guid draftId, string key,
        ISalesOrderFutureAllocationReleaseParticipantV1 host, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? fault = null,
        Guid? orderIdOverride = null)
    {
        var envelope = run.Envelope(key) with { PermissionCode = SalesPermissionManifestV1.OrdersCancel };
        await using var sales = run.Store(CompanyA, fault);
        return await sales.Store.CancelDraftAsync(envelope, Hash(key), SalesDraftCommands.CancelV1, draftId,
            (draft, impact, now) => draft.Cancel(8, run.Actor, "SENTETIK müşteri vazgeçti", impact, now), CancelHostAudit,
            (_, _) => Task.FromResult(new CancellationImpactSnapshot(new string('e', 64),
                [new(SalesCancellationImpactSourcesV1.ActiveAllocations, SalesCancellationImpactStatusesV1.None)])),
            (draft, transaction, token) => host.ReleaseForCancelledOrderAsync(transaction,
                new SalesOrderFutureAllocationReleaseRequestV1(orderIdOverride ?? draft.Id, [.. draft.AllLines.Select(line => line.Id).Order()],
                    SalesOrderFutureAllocationReleaseTextsV1.CancelledOrder, envelope), token));
    }

    private static async Task AssertCancelHostZeroEffectAsync(Run run, Guid draftId, FutureDemandHostSeed seed, string key)
    {
        Assert.Equal((1, 1.25m, 0m, 1L, (string?)null, (string?)null), await AllocationAsync(run, seed.AllocationId));
        Assert.Equal(1.25m, await run.ScalarAsync<decimal>("SELECT ActiveAllocated FROM inventory.FutureSupplyLines WHERE PurchaseOrderLineId=@line",
            ("@line", seed.PurchaseLineId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType=@event",
            ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@event",
            ("@id", seed.AllocationId.ToString("D")), ("@event", InventoryFutureAllocationReleasedOutboxV1.EventType)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftCancellations WHERE DraftId=@id", ("@id", draftId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE IdempotencyKeyHash=@key",
            ("@key", Hash(key).ToUpperInvariant())));
        await using var fresh = run.Store(CompanyA);
        Assert.Equal((SalesDraftState.Draft, 8L), ((await fresh.Store.FindDraftAsync(draftId))!.State, (await fresh.Store.FindDraftAsync(draftId))!.Version));
    }

    /// <summary>(Status, RemainingQuantity, ReleasedQuantity, Version, ReleaseReasonCode, ReleaseOrigin) as SQL Server holds the allocation.</summary>
    private static async Task<(int, decimal, decimal, long, string?, string?)> AllocationAsync(Run run, Guid allocationId) =>
        (await run.ScalarAsync<int>("SELECT Status FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
            await run.ScalarAsync<decimal>("SELECT RemainingQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
            await run.ScalarAsync<decimal>("SELECT ReleasedQuantity FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
            await run.ScalarAsync<long>("SELECT Version FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId)),
            NullIfMarker(await run.ScalarAsync<string>(
                "SELECT ISNULL(ReleaseReasonCode, N'<null>') FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId))),
            NullIfMarker(await run.ScalarAsync<string>(
                "SELECT ISNULL(ReleaseOrigin, N'<null>') FROM inventory.FutureSupplyAllocations WHERE Id=@id", ("@id", allocationId))));

    private static string? NullIfMarker(string value) => value == "<null>" ? null : value;

    private static InventoryFutureAllocationReleasePort CancelHostReleasePort()
    {
        var company = FutureDemandHostCompany();
        return new InventoryFutureAllocationReleasePort(company, new PurchaseLineSerializationLockHostBinder(company), TimeProvider.System,
            new FutureDemandHostKilograms(), new FutureDemandHostSchedule());
    }

    private static SalesFutureAllocationReleaseHostParticipant CancelHost(IPurchaseLineSerializationLockBinderV1? purchaseLocks = null)
    {
        var company = FutureDemandHostCompany();
        return new(new SalesOrderLineSerializationLockHostBinder(company), purchaseLocks ?? new PurchaseLineSerializationLockHostBinder(company),
            CancelHostReleasePort());
    }

    private static PurchasingFutureAllocationLinkHostParticipant LinkHost() => new(CancelHostReleasePort(), FutureDemandHostCompany());

    private sealed class ThrowingCancelHost(string message) : ISalesOrderFutureAllocationReleaseParticipantV1
    {
        public Task ReleaseForCancelledOrderAsync(IDbTransaction transaction, SalesOrderFutureAllocationReleaseRequestV1 request,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(message);
    }

    /// <summary>A race barrier: the cancellation's FIRST purchase line lock waits until the test lets it go, then takes the real lock.</summary>
    private sealed class PausingPurchaseLocks(IPurchaseLineSerializationLockBinderV1 inner) : IPurchaseLineSerializationLockBinderV1
    {
        private int paused;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IPurchaseLineSerializationLockV1 Bind(DbConnection connection, DbTransaction transaction) =>
            new Pausing(inner.Bind(connection, transaction), this);

        private async Task PauseOnceAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref paused, 1) != 0) return;
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
        }

        private sealed class Pausing(IPurchaseLineSerializationLockV1 inner, PausingPurchaseLocks owner) : IPurchaseLineSerializationLockV1
        {
            public async Task AcquireAsync(Guid lineId, CancellationToken cancellationToken = default)
            {
                await owner.PauseOnceAsync(cancellationToken);
                await inner.AcquireAsync(lineId, cancellationToken);
            }

            public async Task AcquireAllAsync(IEnumerable<Guid> lineIds, CancellationToken cancellationToken = default)
            {
                await owner.PauseOnceAsync(cancellationToken);
                await inner.AcquireAllAsync(lineIds, cancellationToken);
            }
        }
    }
}
