using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-017 STOK F017-b against a REAL SQL Server: the future supply tables' checks, the forward-fix Down guard of
/// <see cref="AddInventoryFutureSupplyAllocations"/> (THROW 51460) and the fail-closed read port Purchasing and Sales use. Rows are
/// written with raw SQL so that the schema itself, not the writer, is what refuses. Every identity is synthetic test data in this
/// test's own database. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private const string InsertFutureSupplyLine =
        """
        INSERT INTO inventory.FutureSupplyLines
            (CompanyId, PurchaseOrderId, PurchaseOrderLineId, PurchaseUnitId, AllocatableSnapshot, ActiveAllocated, Version,
             CreatedAtUtc, UpdatedAtUtc)
        VALUES (@company, @order, @line, @unit, @snapshot, @active, 1, @at, @at);
        """;

    private const string InsertFutureSupplyAllocation =
        """
        INSERT INTO inventory.FutureSupplyAllocations
            (Id, CompanyId, SourceKind, PurchaseOrderId, PurchaseOrderLineId, PurchaseUnitId, ProductId, ProductVersion, DemandKind,
             DemandId, DemandLineId, AllocatedQuantity, RemainingQuantity, ReleasedQuantity, ManualSequence, Reason, Status,
             ReleaseReasonCode, ReleaseOrigin, ReleaseReason, Version, ObservedPurchaseOrderVersion, CreatedAtUtc, UpdatedAtUtc,
             ReleasedAtUtc)
        VALUES (@id, @company, 'purchasing.order-line', @order, @line, @unit, @product, '1', 'sales.order-line', @demand,
                @demandLine, @allocated, @remaining, 0, NULL, N'SENTETIK ayırma', @status, NULL, NULL, NULL, 1, 1, @at, @at, NULL);
        """;

    [SqlServerFact]
    public async Task The_future_supply_migrations_Down_passes_on_empty_tables_and_refuses_while_a_ledger_or_allocation_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddInventoryFutureSupplyAllocations().DownOperations[0]).Sql;
        var supply = FutureSupplyIds.New();

        // Empty tables: an untouched store rolls back freely.
        await run.ExecuteAsync(guard);

        await run.ExecuteAsync(InsertFutureSupplyLine, supply.Line(CompanyA, snapshot: 100m, active: 0m));
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));

        Assert.Equal(51460, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.FutureSupplyLines WHERE PurchaseOrderLineId = @line", ("@line", supply.PurchaseLine)));
    }

    [SqlServerFact]
    public async Task The_future_supply_checks_refuse_a_ledger_above_its_snapshot_and_an_active_allocation_without_remainder()
    {
        await using var run = await Run.StartAsync();
        var overdrawn = FutureSupplyIds.New();
        var supply = FutureSupplyIds.New();

        var ledger = await Assert.ThrowsAsync<SqlException>(
            () => run.ExecuteAsync(InsertFutureSupplyLine, overdrawn.Line(CompanyA, snapshot: 10m, active: 10.001m)));
        await run.ExecuteAsync(InsertFutureSupplyLine, supply.Line(CompanyA, snapshot: 100m, active: 0m));
        var empty = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(InsertFutureSupplyAllocation,
            supply.Allocation(CompanyA, Guid.NewGuid(), allocated: 5m, remaining: 0m, status: 1)));
        var orphan = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(InsertFutureSupplyAllocation,
            FutureSupplyIds.New().Allocation(CompanyA, Guid.NewGuid(), allocated: 5m, remaining: 5m, status: 1)));

        Assert.Equal((547, 547, 547), (ledger.Number, empty.Number, orphan.Number));
        Assert.Contains("CK_FutureSupplyLines_Ledger", ledger.Message, StringComparison.Ordinal);
        Assert.Contains("CK_FutureSupplyAllocations_Quantity", empty.Message, StringComparison.Ordinal);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.FutureSupplyAllocations"));
    }

    [SqlServerFact]
    public async Task The_future_supply_read_port_answers_present_for_its_company_none_for_another_and_unreadable_on_disagreement()
    {
        await using var run = await Run.StartAsync();
        var supply = FutureSupplyIds.New();
        await run.ExecuteAsync(InsertFutureSupplyLine, supply.Line(CompanyA, snapshot: 100m, active: 40.125m));
        await run.ExecuteAsync(InsertFutureSupplyAllocation,
            supply.Allocation(CompanyA, Guid.NewGuid(), allocated: 40.125m, remaining: 40.125m, status: 1));

        await using (var a = run.Context(CompanyA))
        {
            var port = new InventoryFutureAllocationReadPort(a, Company(CompanyA));
            var sum = await port.GetAllocatedOnPurchaseLineAsync(supply.PurchaseLine);
            var consumed = await port.GetConsumedPresenceOnPurchaseLineAsync(supply.PurchaseLine);
            Assert.Equal(InventoryFutureAllocationReadOutcomeV1.Present, sum.Outcome);
            Assert.Equal("40.125", sum.QuantityText);
            Assert.Equal(supply.Unit, sum.UnitId);
            Assert.Equal(InventoryFutureAllocationReadOutcomeV1.None, consumed.Outcome);
        }

        await using (var b = run.Context(CompanyB))
        {
            var port = new InventoryFutureAllocationReadPort(b, Company(CompanyB));
            Assert.Equal(InventoryFutureAllocationReadOutcomeV1.None, (await port.GetAllocatedOnPurchaseLineAsync(supply.PurchaseLine)).Outcome);
        }

        // The ledger no longer equals Σ active remaining: fail-closed, never the ledger's figure.
        await run.ExecuteAsync(
            "UPDATE inventory.FutureSupplyLines SET ActiveAllocated = 30 WHERE PurchaseOrderLineId = @line", ("@line", supply.PurchaseLine));
        await using (var a = run.Context(CompanyA))
        {
            var port = new InventoryFutureAllocationReadPort(a, Company(CompanyA));
            var sum = await port.GetAllocatedOnPurchaseLineAsync(supply.PurchaseLine);
            Assert.Equal(InventoryFutureAllocationReadOutcomeV1.Unreadable, sum.Outcome);
            Assert.Null(sum.QuantityText);
            Assert.Null(sum.UnitId);
        }
    }

    private static CompanyContext Company(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return context;
    }

    /// <summary>One synthetic purchase order line, its unit, product and Sales demand line.</summary>
    private sealed record FutureSupplyIds(
        Guid PurchaseOrder, Guid PurchaseLine, Guid Unit, Guid Product, Guid DemandOrder, Guid DemandLine)
    {
        public static FutureSupplyIds New() =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        public (string Name, object Value)[] Line(Guid company, decimal snapshot, decimal active) =>
        [
            ("@company", company), ("@order", PurchaseOrder), ("@line", PurchaseLine), ("@unit", Unit), ("@snapshot", snapshot),
            ("@active", active), ("@at", At),
        ];

        public (string Name, object Value)[] Allocation(Guid company, Guid id, decimal allocated, decimal remaining, int status) =>
        [
            ("@id", id), ("@company", company), ("@order", PurchaseOrder), ("@line", PurchaseLine), ("@unit", Unit),
            ("@product", Product), ("@demand", DemandOrder), ("@demandLine", DemandLine), ("@allocated", allocated),
            ("@remaining", remaining), ("@status", status), ("@at", At),
        ];
    }
}
