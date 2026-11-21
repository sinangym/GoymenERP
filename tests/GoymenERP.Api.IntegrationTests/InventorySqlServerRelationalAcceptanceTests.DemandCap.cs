using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// STK-S41 (KLN-D65 / KLN-D66 / KLN-D67, F-017 STOK spec §3.2 step 5, §9.1 candidate 3) against a REAL SQL Server: the ONE total
/// cap of a Sales order line — active physical kilograms + active future allocations + the request ≤ the line's open quantity —
/// checked on both creation paths under the same host Sales order line lock, the fail-closed kilogram conversion, and the recorded
/// terminal refusal of the future writer. The Sales and purchase sources are fixed test doubles; the lock, the tables, the audit
/// interceptor and the Settings unit identities are the real ones. Every identity is synthetic test data. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    /// <summary>How long the second writer is observed waiting on the Sales line lock; well under the lock's own timeout.</summary>
    private static readonly TimeSpan LockWaitObservation = TimeSpan.FromSeconds(1);

    /// <summary>The longest any gate in these tests waits before the test fails instead of hanging.</summary>
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    [SqlServerFact]
    public async Task Physical_allocation_create_over_the_sales_line_open_quantity_counting_future_allocations_is_refused_without_effect()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-CAP-PHYS", 100m);
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        // An active future allocation of 20 kg already serves the same Sales line.
        var future = FutureSupplyIds.New() with
        {
            Unit = SettingsUnitIdentitiesV1.Kilogram, Product = source.ProductId, DemandOrder = orderId, DemandLine = lineId,
        };
        await run.ExecuteAsync(InsertFutureSupplyLine, future.Line(CompanyA, snapshot: 100m, active: 20m));
        await run.ExecuteAsync(InsertFutureSupplyAllocation,
            future.Allocation(CompanyA, Guid.NewGuid(), allocated: 20m, remaining: 20m, status: 1));
        var demand = new FixedDemandSource(new InventoryFutureDemandLineV1(orderId, lineId, source.ProductId, "7",
            SettingsUnitIdentitiesV1.Kilogram, "50"));
        var before = await AllocationEffectCountsAsync(run);

        await using (var scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() => CappedPhysical(scope, demand).CreateAsync(
                AllocationRequest(source, "40") with { SalesOrderId = orderId, SalesOrderLineId = lineId }, Metadata(run)));
            Assert.Equal((409, InventoryAllocationCodesV1.ExceedsDemandOpenQuantity), (refused.Status, refused.Code));
        }

        // KLN-D65 (2): nothing is written and the existing future allocation stays as it is.
        Assert.Equal(before, await AllocationEffectCountsAsync(run));
        Assert.Equal(20m, await run.ScalarAsync<decimal>(
            "SELECT SUM(RemainingQuantity) FROM inventory.FutureSupplyAllocations WHERE DemandLineId = @line AND Status = 1",
            ("@line", lineId)));

        // 20 future + 30 physical = 50: exactly the open quantity fits.
        await using (var scope = run.Store(CompanyA))
        {
            var created = await CappedPhysical(scope, demand).CreateAsync(
                AllocationRequest(source, "30") with { SalesOrderId = orderId, SalesOrderLineId = lineId }, Metadata(run));
            Assert.Equal(("Active", "30"), (created.Allocation.Status, created.Allocation.RemainingKg));
        }

        Assert.Equal(30m, await run.ScalarAsync<decimal>(
            "SELECT SUM(RemainingKg) FROM inventory.Allocations WHERE DemandLineId = @line AND Status = 1", ("@line", lineId)));
    }

    [SqlServerFact]
    public async Task Physical_allocation_on_a_sales_line_whose_order_unit_has_no_kilogram_conversion_is_refused_422_without_effect()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-CAP-UNIT", 100m);
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        // A unit Settings does not know: physical kilograms cannot be compared with it (KLN-D66 (3), never a guess).
        var demand = new FixedDemandSource(new InventoryFutureDemandLineV1(orderId, lineId, source.ProductId, "7",
            Guid.NewGuid(), "1000"));
        var before = await AllocationEffectCountsAsync(run);

        await using (var scope = run.Store(CompanyA))
        {
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() => CappedPhysical(scope, demand).CreateAsync(
                AllocationRequest(source, "10") with { SalesOrderId = orderId, SalesOrderLineId = lineId }, Metadata(run)));
            Assert.Equal((422, InventoryAllocationCodesV1.UnitConversionUnavailable), (refused.Status, refused.Code));
        }

        Assert.Equal(before, await AllocationEffectCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Future_allocation_create_needing_a_missing_kilogram_conversion_records_a_terminal_422_and_replays_it_by_key()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-CAP-REFUSE", 100m);
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var demand = new FixedDemandSource(new InventoryFutureDemandLineV1(orderId, lineId, source.ProductId, "7",
            SettingsUnitIdentitiesV1.Kilogram, "100"));
        await using (var scope = run.Store(CompanyA))
        {
            await CappedPhysical(scope, demand).CreateAsync(
                AllocationRequest(source, "10") with { SalesOrderId = orderId, SalesOrderLineId = lineId }, Metadata(run));
        }

        // The purchase line is in a unit Settings cannot prove to be the kilogram, while 10 kg are physically held.
        var purchaseUnit = Guid.NewGuid();
        var supply = new FixedSupplySource(new InventoryFutureSupplyLineV1(Guid.NewGuid(), Guid.NewGuid(), source.ProductId,
            "3", purchaseUnit, "500", 1));
        var request = new CreateFutureSupplyAllocationV1(Guid.NewGuid(), orderId, lineId, supply.Line.OrderId,
            supply.Line.LineId, "5", purchaseUnit, "SENTETIK gelecek ayırma");
        var metadata = Metadata(run) with { CorrelationId = $"cap-refuse-{Guid.NewGuid():N}" };

        await using (var writer = FutureWriter(run, CompanyA, demand, supply))
        {
            var refused = await Assert.ThrowsAsync<InventoryAllocationException>(
                () => writer.Writer.CreateAsync(request, metadata, AllowingFence.Instance));
            Assert.Equal((422, InventoryFutureAllocationCodesV1.UnitConversionUnavailable, false),
                (refused.Status, refused.Code, refused.IsReplay));
        }

        await using (var writer = FutureWriter(run, CompanyA, demand, supply))
        {
            var replay = await Assert.ThrowsAsync<InventoryAllocationException>(() => writer.Writer.CreateAsync(request,
                metadata with { CorrelationId = $"cap-replay-{Guid.NewGuid():N}" }, AllowingFence.Instance));
            Assert.Equal((422, InventoryFutureAllocationCodesV1.UnitConversionUnavailable, true, metadata.CorrelationId),
                (replay.Status, replay.Code, replay.IsReplay, replay.OriginalCorrelationId));
        }

        await using (var writer = FutureWriter(run, CompanyA, demand, supply))
        {
            var conflict = await Assert.ThrowsAsync<InventoryAllocationException>(() => writer.Writer.CreateAsync(
                request with { QuantityText = "4" }, metadata, AllowingFence.Instance));
            Assert.Equal((409, InventoryFutureAllocationCodesV1.IdempotencyConflict), (conflict.Status, conflict.Code));
        }

        // One command row holds the refusal; no allocation, ledger or outbox row; one refusal audit event.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @command",
            ("@command", InventoryFutureAllocationCommandsV1.Create)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyAllocations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyLines"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.OutboxMessages WHERE EventType = @type",
            ("@type", InventoryFutureAllocationCreatedOutboxV1.EventType)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = @type AND CorrelationId = @c",
            ("@type", InventoryFutureAllocationWriter.CreateRefusedEventType), ("@c", metadata.CorrelationId)));
    }

    [SqlServerFact]
    public async Task A_physical_and_a_future_create_on_one_sales_line_serialise_on_the_line_lock_and_only_one_fits_the_cap()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-CAP-RACE", 100m);
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        // Both writers ask 60 kg of a 100 kg line; the physical writer is held inside its cap read, under the lock.
        var demand = new FixedDemandSource(new InventoryFutureDemandLineV1(orderId, lineId, source.ProductId, "7",
            SettingsUnitIdentitiesV1.Kilogram, "100"), holdFirstRead: true);
        var supply = new FixedSupplySource(new InventoryFutureSupplyLineV1(Guid.NewGuid(), Guid.NewGuid(), source.ProductId,
            "3", SettingsUnitIdentitiesV1.Kilogram, "500", 1));
        var futureRequest = new CreateFutureSupplyAllocationV1(Guid.NewGuid(), orderId, lineId, supply.Line.OrderId,
            supply.Line.LineId, "60", SettingsUnitIdentitiesV1.Kilogram, "SENTETIK gelecek ayırma");

        await using var physicalScope = run.Store(CompanyA);
        var physical = CappedPhysical(physicalScope, demand).CreateAsync(
            AllocationRequest(source, "60") with { SalesOrderId = orderId, SalesOrderLineId = lineId }, Metadata(run));
        await demand.FirstReadEntered.WaitAsync(GateTimeout);

        await using var futureWriter = FutureWriter(run, CompanyA, demand, supply);
        var futureMetadata = Metadata(run) with { CorrelationId = $"cap-race-{Guid.NewGuid():N}" };
        var future = futureWriter.Writer.CreateAsync(futureRequest, futureMetadata, AllowingFence.Instance);

        // KLN-D65 (3): the future writer waits on the same Sales line lock and never reads the demand meanwhile.
        await Task.Delay(LockWaitObservation);
        Assert.False(future.IsCompleted);
        Assert.Equal(1, demand.Reads);
        demand.ReleaseFirstRead();

        var created = await physical;
        var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() => future);

        Assert.Equal(("Active", "60"), (created.Allocation.Status, created.Allocation.RemainingKg));
        Assert.Equal((409, InventoryFutureAllocationCodesV1.ExceedsDemandOpenQuantity), (refused.Status, refused.Code));
        Assert.Equal(60m, await run.ScalarAsync<decimal>(
            "SELECT SUM(RemainingKg) FROM inventory.Allocations WHERE DemandLineId = @line AND Status = 1", ("@line", lineId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.FutureSupplyAllocations WHERE DemandLineId = @line", ("@line", lineId)));
        // The 409 rule refusal decided under the locks is recorded under the future writer's key.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE CommandName = @command",
            ("@command", InventoryFutureAllocationCommandsV1.Create)));
    }

    private static InventoryPhysicalAllocationDemandCapWriter CappedPhysical(StoreScope scope, IInventoryFutureDemandSourceV1 demand) =>
        new(scope.Store, scope.DbContext, demand, SettingsConverter(),
            new SalesOrderLineSerializationLockHostBinder(Company(CompanyA)));

    private static InventorySettingsFutureUnitConverterV1 SettingsConverter() =>
        new(new ApprovedSettingsUnitIdentityCatalog());

    private static FutureWriterScope FutureWriter(Run run, Guid company, IInventoryFutureDemandSourceV1 demand,
        IInventoryFutureSupplySourceV1 supply)
    {
        var companyContext = Company(company);
        var collector = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
            .Options;
        var dbContext = new InventoryDbContext(options, companyContext);
        var writer = new InventoryFutureAllocationWriter(dbContext, companyContext, collector,
            new SalesOrderLineSerializationLockHostBinder(companyContext), new PurchaseLineSerializationLockHostBinder(companyContext),
            supply, demand, SettingsConverter(), TimeProvider.System);
        return new FutureWriterScope(dbContext, writer);
    }

    private sealed class FutureWriterScope(InventoryDbContext dbContext, InventoryFutureAllocationWriter writer) : IAsyncDisposable
    {
        public InventoryFutureAllocationWriter Writer => writer;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>The host fence of a caller whose authority still holds.</summary>
    private sealed class AllowingFence : IInventoryFutureAllocationAuthorityFenceV1
    {
        public static AllowingFence Instance { get; } = new();

        public Task RequireStillAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// One open Sales line. With <c>holdFirstRead</c>, the first read signals <see cref="FirstReadEntered"/> and waits for
    /// <see cref="ReleaseFirstRead"/>, so the caller keeps the Sales line lock while the test observes the other writer.
    /// </summary>
    private sealed class FixedDemandSource(InventoryFutureDemandLineV1 line, bool holdFirstRead = false)
        : IInventoryFutureDemandSourceV1
    {
        private readonly TaskCompletionSource firstReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource firstReadReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;

        public Task FirstReadEntered => firstReadEntered.Task;

        public int Reads => Volatile.Read(ref reads);

        public void ReleaseFirstRead() => firstReadReleased.TrySetResult();

        public async Task<InventoryFutureDemandSourceAnswerV1> GetDemandLineAsync(Guid salesOrderId, Guid salesOrderLineId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref reads) == 1 && holdFirstRead)
            {
                firstReadEntered.TrySetResult();
                await firstReadReleased.Task.WaitAsync(GateTimeout, cancellationToken);
            }

            return salesOrderId == line.OrderId && salesOrderLineId == line.LineId
                ? new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.Open, line)
                : new InventoryFutureDemandSourceAnswerV1(InventoryFutureDemandSourceOutcomeV1.NotFound, null);
        }
    }

    /// <summary>One allocatable purchase general line.</summary>
    private sealed class FixedSupplySource(InventoryFutureSupplyLineV1 line) : IInventoryFutureSupplySourceV1
    {
        public InventoryFutureSupplyLineV1 Line => line;

        public Task<InventoryFutureSupplySourceAnswerV1> GetAllocatableLineAsync(Guid purchaseOrderId, Guid purchaseOrderLineId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(purchaseOrderId == line.OrderId && purchaseOrderLineId == line.LineId
                ? new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.Allocatable, line)
                : new InventoryFutureSupplySourceAnswerV1(InventoryFutureSupplySourceOutcomeV1.NotFound, null));
    }

    /// <summary>
    /// The delivery owner's answer for an order nothing was shipped against yet: the Sales open quantity is the line quantity
    /// (the HTTP allocation tests replace the real Logistics port with it, so the cap reads a known open quantity).
    /// </summary>
    private sealed class NoRecordedSalesDeliveries(Guid company) : ISalesOrderDeliveryReadPortV1
    {
        public Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderDeliveryReadAnswerV1(company, orderId, Array.Empty<SalesOrderLineDeliveryV1>()));
    }
}
