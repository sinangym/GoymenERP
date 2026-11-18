using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryNegativeStockAttemptProofTests
{
    [Fact]
    public void A_boundary_downgrade_refuses_observations_count_history_and_the_validation_mutex_before_any_drop()
    {
        var migration = new AddInventoryNegativeStockCountBoundaries();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51988", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[NegativeStockCountBoundaries]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[NegativeStockValidationGuards]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ObservedCountBoundaryVersion] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[CountBoundaryVersion] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(2, migration.DownOperations.OfType<DropTableOperation>().Count());
        Assert.Equal(2, migration.DownOperations.OfType<DropColumnOperation>().Count());
    }

    [Fact]
    public void A_downgrade_refuses_attempt_or_resolution_history_before_either_table_can_be_dropped()
    {
        var migration = new AddInventoryNegativeStockAttempts();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51986", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[NegativeStockAttempts]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[NegativeStockAttemptResolutions]", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(2, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    [Fact]
    public void Repeated_identical_lines_keep_the_actual_failing_ordinal_and_preceding_bucket_mass()
    {
        var ledger = new StockLedger();
        ledger.Apply(Receipt(Position(), 100m));
        var proposed = Movement(StockMovementKind.Issue, Out(Position(), 60m), Out(Position(), 60m));
        var refusal = Assert.Throws<InventoryRuleException>(() => ledger.Apply(proposed));
        Assert.Equal(2, refusal.Shortfall!.LineNumber);
        Assert.Equal(40m, refusal.Shortfall.Available.Value);
        Assert.Equal(100m, ledger.QuantityAt(Position()).Value);
        Assert.DoesNotContain(proposed, ledger.AppliedMovements);
    }

    [Fact]
    public void An_unobserved_outbound_shortage_cannot_claim_a_pre_validation_count_boundary()
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        using var database = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var metadata = new InventoryCommandMetadata("synthetic-actor", "inventory.stock-transfer.write", [],
            "synthetic-correlation", null, "synthetic-key", "Web");
        var store = new InventoryStore(database, company, new AuditEventCollector(), new InventoryKilogramScale(null!), TimeProvider.System);
        var proposed = Issue(Position(), 120m);
        var shortfall = new StockShortfall(proposed.Kind, proposed.Source, Position(), Kilograms.Positive(120m), Kilograms.Of(100m), 1);
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            store.PhysicalStockRefusal(proposed, shortfall, metadata, CodeOf(Label1)));
        Assert.Contains("pre-validation count boundary", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Fact]
    public void An_incoming_line_or_a_different_ordinal_cannot_supply_outbound_refusal_evidence()
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        using var database = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var store = new InventoryStore(database, company, new AuditEventCollector(), new InventoryKilogramScale(null!), TimeProvider.System);
        var proposed = Receipt(Position(), 1m);
        var metadata = new InventoryCommandMetadata("actor", "inventory.opening.write", [], "correlation", null, "key", "Web");
        var shortfall = new StockShortfall(proposed.Kind, proposed.Source, Position(), Kilograms.Positive(1m), Kilograms.Of(0m), 1);
        Assert.Throws<InvalidOperationException>(() => store.PhysicalStockRefusal(proposed, shortfall, metadata, CodeOf(Label1)));
    }

    [Fact]
    public void A_reservation_only_refusal_carries_no_negative_physical_attempt_evidence()
    {
        var refusal = InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient,
            new InventoryManufacturingStockShortfallV1(StockMovementKind.Issue, "manufacturing.input-issue", Guid.NewGuid(),
                Guid.NewGuid(), Label1, BandProduct, Warehouse1, Kilograms.Positive(30m), Kilograms.Of(100m), Kilograms.Of(80m)));
        Assert.Null(InventoryNegativeStockAttemptEvidenceException.FindEvidence(refusal));
        Assert.Equal(20m, refusal.StockShortfall!.AvailableKg.Value);
    }
}
