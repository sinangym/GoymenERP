using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// The persistent model without a database: schema, tenant filters, keys and the write guard,
/// which refuses before any connection is opened. What SQL Server itself enforces is proven by
/// the relational acceptance tests, not here.
/// </summary>
public sealed class InventoryPersistenceModelTests
{
    private static readonly Type[] TenantRows =
        [
            typeof(LotRow), typeof(LabelRow), typeof(LabelPrintRow), typeof(LabelDocumentRow),
            typeof(LabelTransformationRow), typeof(LabelTransformationInputRow), typeof(LabelTransformationOutputRow),
            typeof(MovementRow), typeof(MovementLineRow),
            typeof(BalanceRow), typeof(SourceGrainRow), typeof(OpeningCountRow), typeof(OpeningCountLineRow),
            typeof(InventoryCommandRow), typeof(AllocationRow), typeof(InventoryOutboxRow),
            typeof(StockCountRow), typeof(StockCountDecisionRow), typeof(AllocationChangeRow),
            typeof(InventoryTransferRow), typeof(InventoryTransferLineRow),
            typeof(InventoryTransferDispatchRow), typeof(InventoryTransferDispatchLineRow),
            typeof(InventoryTransferBucketRow), typeof(InventoryTransferAcceptanceRow), typeof(AverageCostLedgerRow),
            typeof(FutureSupplyAllocationRow), typeof(FutureSupplyLineRow),
            typeof(InventoryTransferDispatchCancelClosureRow), typeof(InventoryTransferDispatchCancellationRow),
            typeof(InventoryTransferReceiptRow), typeof(InventoryTransferOverageDecisionRow),
            typeof(InventoryTransferLossRequestRow), typeof(InventoryTransferLossDecisionRow),
            typeof(FutureSupplyConversionRow),
        ];

    [Fact]
    public void Every_inventory_table_is_in_its_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in TenantRows)
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal(InventoryDbContext.Schema, entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.NotNull(entity.FindProperty("CompanyId"));
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        Assert.Single(audit.GetDeclaredQueryFilters());
    }

    [Fact]
    public void A_label_code_is_unique_per_company_and_a_source_per_kind()
    {
        using var context = Context();
        var labels = context.Model.FindEntityType(typeof(LabelRow))!;
        Assert.Contains(labels.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Code"]));
        // No group-wide code index: a code never reveals another company's label.
        Assert.DoesNotContain(labels.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["Code"]));

        var movements = context.Model.FindEntityType(typeof(MovementRow))!;
        var whole = Assert.Single(movements.GetIndexes(), index => index.GetDatabaseName() == "UX_Movements_Source");
        var line = Assert.Single(movements.GetIndexes(), index => index.GetDatabaseName() == "UX_Movements_SourceLine");
        Assert.True(whole.IsUnique && line.IsUnique);
        Assert.Equal("[SourceLineId] IS NULL", whole.GetFilter());
        Assert.Equal("[SourceLineId] IS NOT NULL", line.GetFilter());
        Assert.Equal(["CompanyId", "Kind", "SourceKind", "SourceId", "SourceLineId"],
            line.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Every_movement_references_the_one_grain_of_its_source()
    {
        // #293 ORG P3-1: one grain row per (company, kind, source); a movement names it with its own
        // grain, so a whole and a line movement of one source cannot both exist in SQL.
        using var context = Context();
        var grains = context.Model.FindEntityType(typeof(SourceGrainRow))!;
        Assert.Equal(["CompanyId", "Kind", "SourceKind", "SourceId"],
            grains.FindPrimaryKey()!.Properties.Select(property => property.Name));

        var movements = context.Model.FindEntityType(typeof(MovementRow))!;
        var reference = Assert.Single(movements.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(SourceGrainRow));
        Assert.Equal(["CompanyId", "Kind", "SourceKind", "SourceId", "SourceGrain"],
            reference.Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "Kind", "SourceKind", "SourceId", "Grain"],
            reference.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, reference.DeleteBehavior);
        // Check constraints live in the design-time model only; SQL proves the constraint itself (547).
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MovementRow))!;
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_Movements_SourceGrain");
    }

    [Fact]
    public void Locations_and_labels_are_referenced_by_company_and_id()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(MovementLineRow), typeof(BalanceRow), typeof(AllocationRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.GetTableName() == "Locations"
                && key.PrincipalEntityType.GetSchema() == "organization"
                && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LocationId"]));
            Assert.Contains(entity.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LabelRow)
                && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LabelId"]));
        }

        var balances = context.Model.FindEntityType(typeof(BalanceRow))!;
        Assert.Single(balances.GetIndexes(), index => index.IsUnique && index.GetDatabaseName() == "UX_Balances_Position");
        Assert.True(balances.FindProperty(nameof(BalanceRow.RowVersion))!.IsConcurrencyToken);
        var labels = context.Model.FindEntityType(typeof(LabelRow))!;
        Assert.True(labels.FindProperty(nameof(LabelRow.Version))!.IsConcurrencyToken);
        Assert.True(labels.FindProperty(nameof(LabelRow.RowVersion))!.IsConcurrencyToken);
        var productVersion = labels.FindProperty(nameof(LabelRow.ProductVersion))!;
        Assert.True(productVersion.IsNullable);
        Assert.Equal(MaterialLabel.MaximumProductVersionLength, productVersion.GetMaxLength());
        Assert.Equal("nvarchar(128)", productVersion.GetColumnType());
        var allocations = context.Model.FindEntityType(typeof(AllocationRow))!;
        Assert.True(allocations.FindProperty(nameof(AllocationRow.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(["CompanyId", "Id"], allocations.GetKeys().Single(key => !key.IsPrimaryKey())
            .Properties.Select(property => property.Name));
        Assert.Contains(allocations.GetIndexes(), index => index.GetDatabaseName() == "IX_Allocations_ActivePosition");
        Assert.Contains(allocations.GetIndexes(), index => index.GetDatabaseName() == "IX_Allocations_Demand");
    }

    [Fact]
    public void Label_genealogy_edges_are_company_scoped_and_reference_both_event_and_label()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(LabelTransformationInputRow), typeof(LabelTransformationOutputRow) })
        {
            var edge = context.Model.FindEntityType(type)!;
            Assert.Contains(edge.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LabelTransformationRow)
                && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "TransformationId"])
                && key.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"])
                && key.DeleteBehavior == DeleteBehavior.Restrict);
            Assert.Contains(edge.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LabelRow)
                && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LabelId"])
                && key.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"])
                && key.DeleteBehavior == DeleteBehavior.Restrict);
            Assert.Contains(edge.GetIndexes(), index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual(["CompanyId", "TransformationId", "LabelId"]));
            Assert.Contains(edge.GetIndexes(), index => index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "LabelId", "TransformationId"]));
        }

        var transformation = context.Model.FindEntityType(typeof(LabelTransformationRow))!;
        Assert.Equal(["CompanyId", "Id"], transformation.GetKeys().Single(key => !key.IsPrimaryKey())
            .Properties.Select(property => property.Name));
    }

    [Fact]
    public void The_migration_is_discoverable_and_the_snapshot_matches_the_model()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_InitialInventory", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventorySourceGrain", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddMaterialLots", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddOpeningCounts", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryCommands", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryDefiniteShipmentRuntime", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryPhysicalAllocationLifecycle", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryLabelGenealogy", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryStockCounts", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryAllocationChanges", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryStockScraps", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryTransfers", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryLabelProductVersion", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryAverageCostLedger", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_RelaxInventoryAverageCostReasonToZeroCostReceipt", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_WidenInventoryLabelTransformationKindToManufacturing", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryTransferDispatchDocumentVersion", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryLabelProductionStatus", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryManufacturingOutputReversal", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryFutureSupplyAllocations", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventoryTransferDispatchCancellation", StringComparison.Ordinal));
        Assert.Contains(context.Database.GetMigrations(),
            migration => migration.EndsWith("_AddInventorySupplierReturnExit", StringComparison.Ordinal));
        Assert.Equal("AddInventoryOpeningCosts", context.Database.GetMigrations().Last()[15..]);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void A_label_belongs_to_a_lot_of_its_own_company()
    {
        using var context = Context();
        var labels = context.Model.FindEntityType(typeof(LabelRow))!;
        // DEC-PRD-003: (company, lot) against the lot's (company, id), so another company's lot cannot be named.
        var lot = Assert.Single(labels.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LotRow));
        Assert.Equal(["CompanyId", "LotId"], lot.Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "Id"], lot.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, lot.DeleteBehavior);
        Assert.True(lot.IsRequired);

        var lots = context.Model.FindEntityType(typeof(LotRow))!;
        Assert.Equal(("Lots", 64, 64), (lots.GetTableName(), lots.FindProperty(nameof(LotRow.HeatNumber))!.GetMaxLength(),
            lots.FindProperty(nameof(LotRow.CoilNumber))!.GetMaxLength()));
        Assert.True(lots.FindProperty(nameof(LotRow.HeatNumber))!.IsNullable);
        Assert.True(lots.FindProperty(nameof(LotRow.CoilNumber))!.IsNullable);
        // Constraint names are design-time facts: read from the design-time model.
        var design = context.GetService<IDesignTimeModel>().Model;
        Assert.Contains(design.FindEntityType(typeof(LotRow))!.GetCheckConstraints(), check => check.Name == "CK_Lots_Trace");
        var designLabel = design.FindEntityType(typeof(LabelRow))!;
        Assert.Contains(designLabel.GetCheckConstraints(), check => check.Name == "CK_Labels_ProductVersion");
        Assert.Equal("FK_Labels_Lots_CompanyId_LotId", Assert.Single(designLabel.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(LotRow)).GetConstraintName());
    }

    [Theory]
    [InlineData("other-company")]
    [InlineData("no-company-context")]
    [InlineData("empty-company")]
    public void A_row_of_another_company_or_without_a_company_is_refused_before_the_database(string fault)
    {
        using var context = Context(resolved: fault != "no-company-context");
        context.Labels.Add(new LabelRow
        {
            Id = Guid.NewGuid(),
            CompanyId = fault switch { "other-company" => CompanyB, "empty-company" => Guid.Empty, _ => CompanyA },
            Code = "SYN-GUARD",
            ProductId = BandProduct,
            LotId = Lot1,
            Version = 1,
        });

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Theory]
    [InlineData("delete-balance")]
    [InlineData("edit-movement")]
    [InlineData("edit-label-code")]
    [InlineData("edit-balance-company")]
    [InlineData("edit-source-grain")]
    [InlineData("edit-lot-heat")]
    [InlineData("delete-lot")]
    [InlineData("edit-opening-count")]
    [InlineData("edit-opening-line")]
    [InlineData("edit-command")]
    [InlineData("edit-allocation-position")]
    [InlineData("edit-allocation-demand")]
    [InlineData("edit-outbox")]
    [InlineData("edit-transformation")]
    [InlineData("edit-transformation-input")]
    [InlineData("delete-transformation-output")]
    [InlineData("edit-stock-count")]
    [InlineData("delete-stock-count")]
    [InlineData("edit-stock-count-decision")]
    [InlineData("edit-stock-scrap")]
    [InlineData("delete-stock-scrap")]
    [InlineData("edit-stock-scrap-decision")]
    [InlineData("edit-opening-cost")]
    [InlineData("delete-opening-cost")]
    [InlineData("edit-opening-cost-decision")]
    [InlineData("edit-allocation-change")]
    [InlineData("delete-allocation-change")]
    [InlineData("edit-allocation-label")]
    [InlineData("edit-average-cost-ledger-position")]
    [InlineData("edit-average-cost-ledger-company")]
    [InlineData("delete-average-cost-ledger")]
    public void Stored_stock_is_never_deleted_and_only_its_allowed_field_changes(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete-balance":
                context.Balances.Remove(Attach(context, Balance()));
                break;
            case "edit-movement":
                Attach(context, Movement()).SourceKind = "changed.source";
                break;
            case "edit-label-code":
                Attach(context, Label()).Code = "SYN-CHANGED";
                break;
            case "edit-lot-heat":
                // Business #66 5734060341 (Q3): no path changes a lot's heat or coil afterwards.
                Attach(context, Lot()).HeatNumber = "SYN-CHANGED";
                break;
            case "delete-lot":
                context.Lots.Remove(Attach(context, Lot()));
                break;
            case "edit-opening-count":
                // INV-OPEN-1: the count document is append-only; a later difference is a new process (ST-05).
                Attach(context, new OpeningCountRow
                {
                    Id = Guid.NewGuid(), CompanyId = CompanyA, LocationId = Guid.NewGuid(), CountedOn = new DateOnly(2026, 9, 19),
                    SheetReference = "SYN-SHEET", ContentHash = new string('0', 64), RecordedBy = "synthetic", CorrelationId = "synthetic",
                }).SheetReference = "SYN-CHANGED";
                break;
            case "edit-opening-line":
                Attach(context, new OpeningCountLineRow
                {
                    CompanyId = CompanyA, CountId = Guid.NewGuid(), LineNumber = 1, LineId = Guid.NewGuid(),
                    LabelId = Guid.NewGuid(), LotId = Guid.NewGuid(), QuantityKg = 100m,
                }).QuantityKg = 99m;
                break;
            case "edit-command":
                // ADR-011: a key's recorded answer is never rewritten; a replay repeats it.
                Attach(context, new InventoryCommandRow
                {
                    CompanyId = CompanyA, CommandName = "inventory.opening-count.record", IdempotencyKeyHash = new string('A', 64),
                    ActorId = "synthetic", PayloadHash = new string('0', 64), ResultJson = "{}", CorrelationId = "synthetic",
                }).ResultJson = "{\"changed\":true}";
                break;
            case "edit-source-grain":
                Attach(context, new SourceGrainRow
                {
                    CompanyId = CompanyA, Kind = 1, SourceKind = "test.source", SourceId = Guid.NewGuid(), Grain = 1,
                }).Grain = 2;
                break;
            case "edit-allocation-position":
                Attach(context, Allocation()).LocationId = Guid.NewGuid();
                break;
            case "edit-allocation-demand":
                Attach(context, Allocation()).Reason = "SYN-CHANGED";
                break;
            case "edit-outbox":
                Attach(context, Outbox()).PayloadJson = "{\"changed\":true}";
                break;
            case "edit-transformation":
                Attach(context, Transformation()).Reason = "SYN-CHANGED";
                break;
            case "edit-transformation-input":
                Attach(context, TransformationInput()).QuantityKg = 2m;
                break;
            case "delete-transformation-output":
                context.LabelTransformationOutputs.Remove(Attach(context, TransformationOutput()));
                break;
            case "edit-stock-count":
                // ST-05 / DEC-STK-007: a recorded count is evidence; a wrong one is answered by a new count.
                Attach(context, StockCount()).CountedKg = 1m;
                break;
            case "delete-stock-count":
                context.StockCounts.Remove(Attach(context, StockCount()));
                break;
            case "edit-stock-count-decision":
                Attach(context, new StockCountDecisionRow
                {
                    CompanyId = CompanyA, CountId = Guid.NewGuid(), Decision = 2, Reason = "SYN-REJECT",
                    PayloadHash = new string('0', 64), DecidedAtUtc = At, DecidedBy = "synthetic", CorrelationId = "synthetic",
                }).Decision = 1;
                break;
            case "edit-stock-scrap":
                // ST-02 scrap write-off: a recorded scrap is a request; a wrong one is rejected and recorded again.
                Attach(context, StockScrap()).ScrapKg = 1m;
                break;
            case "delete-stock-scrap":
                context.StockScraps.Remove(Attach(context, StockScrap()));
                break;
            case "edit-stock-scrap-decision":
                Attach(context, new StockScrapDecisionRow
                {
                    CompanyId = CompanyA, ScrapId = Guid.NewGuid(), Decision = 2, Reason = "SYN-REJECT",
                    PayloadHash = new string('0', 64), DecidedAtUtc = At, DecidedBy = "synthetic", CorrelationId = "synthetic",
                }).Decision = 1;
                break;
            case "edit-opening-cost":
                // SAT-D304 opening cost: a submitted cost is never rewritten; a wrong one is rejected and entered again.
                Attach(context, OpeningCost()).UnitCostPerKg = 1m;
                break;
            case "delete-opening-cost":
                context.OpeningCosts.Remove(Attach(context, OpeningCost()));
                break;
            case "edit-opening-cost-decision":
                Attach(context, new OpeningCostDecisionRow
                {
                    CompanyId = CompanyA, OpeningCostId = Guid.NewGuid(), Decision = 2, Reason = "SYN-REJECT",
                    PayloadHash = new string('0', 64), DecidedAtUtc = At, DecidedBy = "synthetic", CorrelationId = "synthetic",
                }).CostedStockKg = 1m;
                break;
            case "edit-allocation-change":
                // INV-AL-1 change: the history of a resize or move is evidence; it is never rewritten.
                Attach(context, AllocationChange()).TargetRemainingKg = 2m;
                break;
            case "delete-allocation-change":
                context.AllocationChanges.Remove(Attach(context, AllocationChange()));
                break;
            case "edit-allocation-label":
                // A move never re-points an allocation: it releases it and creates a new one.
                Attach(context, Allocation()).LabelId = Guid.NewGuid();
                break;
            case "edit-average-cost-ledger-position":
                // SAT-D262: a ledger row is one position; its key never changes (only the cost state does).
                Attach(context, AverageCostLedger()).ProductId = Guid.NewGuid();
                break;
            case "edit-average-cost-ledger-company":
                Attach(context, AverageCostLedger()).CompanyId = CompanyB;
                break;
            case "delete-average-cost-ledger":
                context.AverageCostLedger.Remove(Attach(context, AverageCostLedger()));
                break;
            default:
                Attach(context, Balance()).CompanyId = CompanyB;
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_average_cost_ledger_has_its_nullability_precision_checks_position_key_and_company_scope()
    {
        using var context = Context();
        var ledger = context.Model.FindEntityType(typeof(AverageCostLedgerRow))!;
        Assert.Equal(("inventory", "AverageCostLedger"), (ledger.GetSchema(), ledger.GetTableName()));

        Assert.Equal(
            (false, false, false, true, true, true),
            (ledger.FindProperty(nameof(AverageCostLedgerRow.CompanyId))!.IsNullable,
                ledger.FindProperty(nameof(AverageCostLedgerRow.LocationId))!.IsNullable,
                ledger.FindProperty(nameof(AverageCostLedgerRow.Status))!.IsNullable,
                ledger.FindProperty(nameof(AverageCostLedgerRow.UnreadableReason))!.IsNullable,
                ledger.FindProperty(nameof(AverageCostLedgerRow.QuantityKg))!.IsNullable,
                ledger.FindProperty(nameof(AverageCostLedgerRow.AverageCostPerKg))!.IsNullable));
        Assert.Equal((18, 3), (ledger.FindProperty(nameof(AverageCostLedgerRow.QuantityKg))!.GetPrecision()!.Value,
            ledger.FindProperty(nameof(AverageCostLedgerRow.QuantityKg))!.GetScale()!.Value));
        // The internal precision of SAT-D263 is four decimals.
        Assert.Equal((18, 4), (ledger.FindProperty(nameof(AverageCostLedgerRow.AverageCostPerKg))!.GetPrecision()!.Value,
            ledger.FindProperty(nameof(AverageCostLedgerRow.AverageCostPerKg))!.GetScale()!.Value));
        Assert.True(ledger.FindProperty(nameof(AverageCostLedgerRow.RowVersion))!.IsConcurrencyToken);

        Assert.Contains(ledger.GetIndexes(), index => index.IsUnique && index.GetDatabaseName() == "UX_AverageCostLedger_Position"
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LocationId", "ProductId"]));

        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AverageCostLedgerRow))!;
        Assert.Equal(InventoryDbContext.AverageCostLedgerStateCheck,
            Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_AverageCostLedger_State").Sql);
        Assert.Contains(design.GetCheckConstraints(), check => check.Name == "CK_AverageCostLedger_Identity");
        Assert.Contains(design.GetForeignKeys(), key => key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LocationId"]));
    }

    [Fact]
    public void The_average_cost_ledger_Down_refuses_while_a_row_exists_and_is_the_only_raw_sql()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryAverageCostLedger();

        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51250", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[AverageCostLedger]", guard.Sql, StringComparison.Ordinal);
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>(migration.DownOperations[1]);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_zero_cost_receipt_reason_migration_widens_the_check_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.RelaxInventoryAverageCostReasonToZeroCostReceipt();

        Assert.Contains("BETWEEN 1 AND 9", InventoryDbContext.AverageCostLedgerStateCheck, StringComparison.Ordinal);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51258", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[UnreadableReason] = 9", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_manufacturing_genealogy_kind_migration_widens_the_kind_check_and_its_Down_refuses_first()
    {
        using var context = Context();
        var transformations = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LabelTransformationRow))!;
        Assert.Equal("[Kind] IN (1, 2, 3)", InventoryDbContext.LabelTransformationKindCheck);
        Assert.Equal(InventoryDbContext.LabelTransformationKindCheck,
            Assert.Single(transformations.GetCheckConstraints(),
                check => check.Name == "CK_LabelTransformations_Kind").Sql);

        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.WidenInventoryLabelTransformationKindToManufacturing();
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51262", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[LabelTransformations] WHERE [Kind] = 3", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var restored = Assert.Single(migration.DownOperations
            .OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>());
        Assert.Equal("[Kind] IN (1, 2)", restored.Sql);
    }

    [Fact]
    public void The_dispatch_document_version_migration_adds_a_checked_nullable_column_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryTransferDispatchDocumentVersion();
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(InventoryTransferDispatchRow))!;

        Assert.True(design.FindProperty(nameof(InventoryTransferDispatchRow.DocumentVersion))!.IsNullable);
        Assert.Equal(InventoryDbContext.TransferDispatchDocumentVersionCheck,
            Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_TransferDispatches_DocumentVersion").Sql);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51272", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[TransferDispatches]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[DocumentVersion] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_label_production_status_migration_adds_a_checked_write_once_column_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryLabelProductionStatus();
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LabelRow))!;

        Assert.True(design.FindProperty(nameof(LabelRow.ProductionStatus))!.IsNullable);
        Assert.Equal("[ProductionStatus] IS NULL OR [ProductionStatus] IN (1)", InventoryDbContext.LabelProductionStatusCheck);
        Assert.Equal(InventoryDbContext.LabelProductionStatusCheck,
            Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_Labels_ProductionStatus").Sql);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51291", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[Labels]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ProductionStatus] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_future_supply_tables_carry_the_reviewed_checks_keys_and_indexes_and_their_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryFutureSupplyAllocations();
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var allocations = model.FindEntityType(typeof(FutureSupplyAllocationRow))!;
        var ledger = model.FindEntityType(typeof(FutureSupplyLineRow))!;

        Assert.Equal(("FutureSupplyAllocations", "FutureSupplyLines"), (allocations.GetTableName(), ledger.GetTableName()));
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["CK_FutureSupplyAllocations_Identity"] = InventoryDbContext.FutureSupplyAllocationIdentityCheck,
                ["CK_FutureSupplyAllocations_Kinds"] = InventoryDbContext.FutureSupplyAllocationKindsCheck,
                ["CK_FutureSupplyAllocations_Quantity"] = InventoryDbContext.FutureSupplyAllocationQuantityCheck,
                ["CK_FutureSupplyAllocations_Release"] = InventoryDbContext.FutureSupplyAllocationReleaseCheck,
            },
            allocations.GetCheckConstraints().ToDictionary(check => check.Name!, check => (string?)check.Sql));
        Assert.Equal(InventoryDbContext.FutureSupplyLineLedgerCheck,
            Assert.Single(ledger.GetCheckConstraints(), check => check.Name == "CK_FutureSupplyLines_Ledger").Sql);
        Assert.Equal(new[] { "CompanyId", "PurchaseOrderId", "PurchaseOrderLineId" },
            ledger.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(ledger.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PurchaseOrderLineId"]));
        Assert.Contains(allocations.GetIndexes(), index => !index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PurchaseOrderLineId", "Status"]));
        Assert.Contains(allocations.GetIndexes(), index => !index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "DemandKind", "DemandId", "DemandLineId", "Status"]));
        Assert.Contains(allocations.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(FutureSupplyLineRow)
            && key.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "PurchaseOrderId", "PurchaseOrderLineId"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Equal((3, 3, 3), (allocations.FindProperty("AllocatedQuantity")!.GetScale(),
            allocations.FindProperty("RemainingQuantity")!.GetScale(), ledger.FindProperty("ActiveAllocated")!.GetScale()));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51460", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[FutureSupplyAllocations]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[FutureSupplyLines]", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void The_output_reversal_migration_widens_the_movement_kind_check_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryManufacturingOutputReversal();
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MovementRow))!;

        Assert.Equal(10, (int)StockMovementKind.ManufacturingOutputReversal);
        Assert.Equal("[Kind] BETWEEN 1 AND 15",
            Assert.Single(design.GetCheckConstraints(), check => check.Name == "CK_Movements_Kind").Sql);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51420", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[Movements]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] = 10", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var narrowed = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(
            migration.DownOperations[^1]);
        Assert.Equal("[Kind] BETWEEN 1 AND 9", narrowed.Sql);
    }

    [Fact]
    public void The_dispatch_cancellation_migration_widens_the_checks_keeps_one_closure_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryTransferDispatchCancellation();
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var movements = model.FindEntityType(typeof(MovementRow))!;
        var transfers = model.FindEntityType(typeof(InventoryTransferRow))!;
        var dispatches = model.FindEntityType(typeof(InventoryTransferDispatchRow))!;
        var closures = model.FindEntityType(typeof(InventoryTransferDispatchCancelClosureRow))!;
        var cancellations = model.FindEntityType(typeof(InventoryTransferDispatchCancellationRow))!;

        Assert.Equal(11, (int)StockMovementKind.TransferDispatchCancellation);
        Assert.Equal("[Kind] BETWEEN 1 AND 15",
            Assert.Single(movements.GetCheckConstraints(), check => check.Name == "CK_Movements_Kind").Sql);
        Assert.Contains("[Status] BETWEEN 1 AND 6",
            Assert.Single(transfers.GetCheckConstraints(), check => check.Name == "CK_Transfers_State").Sql,
            StringComparison.Ordinal);
        Assert.Equal(InventoryDbContext.TransferDispatchSourceScaleSettingCheck,
            Assert.Single(dispatches.GetCheckConstraints(),
                check => check.Name == "CK_TransferDispatches_SourceScaleSetting").Sql);
        var setting = dispatches.FindProperty(nameof(InventoryTransferDispatchRow.SourceScaleSettingAtDispatch))!;
        Assert.False(setting.IsNullable);
        Assert.Equal((int)GoymenERP.Modules.Inventory.Domain.Transfers.InventoryTransferSourceScaleSetting.Off,
            setting.GetDefaultValue());

        Assert.Equal(["CompanyId", "TransferId"], closures.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(["CompanyId", "TransferId"],
            cancellations.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(cancellations.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "MovementId"]));
        Assert.All(closures.GetForeignKeys().Concat(cancellations.GetForeignKeys()),
            key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        Assert.True(cancellations.FindProperty("LogisticsConfirmationVersion")!.IsNullable);
        Assert.True(cancellations.FindProperty("EDispatchConfirmationVersion")!.IsNullable);

        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51471", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] = 11", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[TransferDispatchCancellations]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[TransferDispatchCancelClosures]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[SourceScaleSettingAtDispatch] <> 1", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 5", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var narrowed = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(
            migration.DownOperations[^1]);
        Assert.Equal("[Kind] BETWEEN 1 AND 10", narrowed.Sql);
    }

    [Fact]
    public void The_transfer_receipt_migration_adds_one_row_per_line_widens_the_checks_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryTransferReceipts();
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var movements = model.FindEntityType(typeof(MovementRow))!;
        var transfers = model.FindEntityType(typeof(InventoryTransferRow))!;
        var receipts = model.FindEntityType(typeof(InventoryTransferReceiptRow))!;
        var overageDecisions = model.FindEntityType(typeof(InventoryTransferOverageDecisionRow))!;
        var lossRequests = model.FindEntityType(typeof(InventoryTransferLossRequestRow))!;
        var lossDecisions = model.FindEntityType(typeof(InventoryTransferLossDecisionRow))!;

        Assert.Equal((12, 13, 14), ((int)StockMovementKind.TransferReceiptShortage,
            (int)StockMovementKind.TransferReceiptOverage, (int)StockMovementKind.TransferTransitLoss));
        Assert.Equal("[Kind] BETWEEN 1 AND 15",
            Assert.Single(movements.GetCheckConstraints(), check => check.Name == "CK_Movements_Kind").Sql);
        Assert.Contains("[Status] BETWEEN 1 AND 6",
            Assert.Single(transfers.GetCheckConstraints(), check => check.Name == "CK_Transfers_State").Sql,
            StringComparison.Ordinal);

        foreach (var entity in new[] { receipts, overageDecisions, lossRequests, lossDecisions })
        {
            Assert.Equal(["CompanyId", "TransferId", "LineId"],
                entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
            Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        }

        // KLN-D21: the receiver's kilograms balance the dispatched kilograms through exactly one of shortage/overage.
        Assert.Contains("[ReceivedKg] + [ShortageKg] - [OverageKg] = [DispatchedKg]",
            Assert.Single(receipts.GetCheckConstraints(), check => check.Name == "CK_TransferReceipts_Quantities").Sql,
            StringComparison.Ordinal);
        // KLN-D24: an excess always carries its reason; no reason without an excess.
        Assert.Contains("[OverageReason] IS NOT NULL",
            Assert.Single(receipts.GetCheckConstraints(), check => check.Name == "CK_TransferReceipts_Overage").Sql,
            StringComparison.Ordinal);
        Assert.Contains(receipts.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(InventoryTransferAcceptanceRow));
        Assert.Contains(receipts.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "ShortageMovementId"]));
        Assert.Contains(overageDecisions.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(InventoryTransferReceiptRow));
        Assert.Contains(lossRequests.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(InventoryTransferLineRow));
        Assert.Contains(lossDecisions.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(InventoryTransferLossRequestRow));
        foreach (var decision in new[] { overageDecisions, lossDecisions })
        {
            Assert.Contains(decision.GetIndexes(), index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "MovementId"]));
            Assert.Contains(decision.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(MovementRow));
        }

        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51646", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] IN (12, 13, 14)", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[TransferReceipts]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[TransferOverageDecisions]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[TransferLossRequests]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[TransferLossDecisions]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 6", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var narrowed = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(
            migration.DownOperations[^1]);
        Assert.Equal("[Kind] BETWEEN 1 AND 11", narrowed.Sql);
    }

    [Fact]
    public void The_opening_cost_migration_adds_company_scoped_append_only_tables_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryOpeningCosts();
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;

        // SAT-D304: a positive cost (a missing one is "yok", never zero) at the ledger's internal scale.
        var openingCost = design.FindEntityType(typeof(OpeningCostRow))!;
        Assert.Equal("[UnitCostPerKg] > 0",
            Assert.Single(openingCost.GetCheckConstraints(), check => check.Name == "CK_OpeningCosts_Cost").Sql);
        Assert.Equal((18, 4), (openingCost.FindProperty(nameof(OpeningCostRow.UnitCostPerKg))!.GetPrecision(),
            openingCost.FindProperty(nameof(OpeningCostRow.UnitCostPerKg))!.GetScale()));
        Assert.Contains(openingCost.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(OrganizationLocationReference)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LocationId"]));
        Assert.Equal(["CompanyId", "Id"], openingCost.GetKeys().Single(key => !key.IsPrimaryKey())
            .Properties.Select(property => property.Name));

        // One decision per submission; an approval names the positive stock it costed, a rejection none.
        var decision = design.FindEntityType(typeof(OpeningCostDecisionRow))!;
        Assert.Equal(["CompanyId", "OpeningCostId"], decision.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(decision.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(OpeningCostRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OpeningCostId"])
            && key.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Id"])
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Equal("([Decision] = 1 AND [CostedStockKg] IS NOT NULL AND [CostedStockKg] > 0) OR ([Decision] = 2 AND [CostedStockKg] IS NULL)",
            Assert.Single(decision.GetCheckConstraints(), check => check.Name == "CK_OpeningCostDecisions_Decision").Sql);

        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51712", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[OpeningCosts]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[OpeningCostDecisions]", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Equal(["OpeningCostDecisions", "OpeningCosts"], migration.DownOperations
            .OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>().Select(operation => operation.Name));
    }

    [Fact]
    public void The_supplier_return_exit_migration_widens_the_movement_kind_check_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventorySupplierReturnExit();
        using var context = Context();
        var movements = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MovementRow))!;

        Assert.Equal(15, (int)StockMovementKind.SupplierReturnExit);
        Assert.Equal("[Kind] BETWEEN 1 AND 15",
            Assert.Single(movements.GetCheckConstraints(), check => check.Name == "CK_Movements_Kind").Sql);
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51654", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[Movements]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Kind] = 15", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var widened = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(
            migration.UpOperations[^1]);
        Assert.Equal("[Kind] BETWEEN 1 AND 15", widened.Sql);
        var narrowed = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation>(
            migration.DownOperations[^1]);
        Assert.Equal("[Kind] BETWEEN 1 AND 14", narrowed.Sql);
    }

    [Fact]
    public void The_future_supply_conversion_link_carries_the_reviewed_checks_keys_and_indexes_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryFutureSupplyConversions();
        using var context = Context();
        var conversions = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(FutureSupplyConversionRow))!;

        Assert.Equal("FutureSupplyConversions", conversions.GetTableName());
        Assert.Equal(
            new Dictionary<string, string?>
            {
                ["CK_FutureSupplyConversions_Identity"] = InventoryDbContext.FutureSupplyConversionIdentityCheck,
                ["CK_FutureSupplyConversions_Outcome"] = InventoryDbContext.FutureSupplyConversionOutcomeCheck,
            },
            conversions.GetCheckConstraints().ToDictionary(check => check.Name!, check => (string?)check.Sql));
        Assert.Contains(conversions.GetIndexes(), index => index.IsUnique && index.GetFilter() is null
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "AcceptanceId", "FutureAllocationId"]));
        Assert.Contains(conversions.GetIndexes(), index => index.IsUnique
            && index.GetFilter() == "[PhysicalAllocationId] IS NOT NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PhysicalAllocationId"]));
        Assert.Contains(conversions.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(FutureSupplyAllocationRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "FutureAllocationId"]));
        Assert.Contains(conversions.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(AllocationRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PhysicalAllocationId"]));
        Assert.Contains(conversions.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(LabelRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "LabelId"]));
        Assert.All(conversions.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        Assert.Equal((false, true, true, true),
            (conversions.FindProperty("Quantity")!.IsNullable, conversions.FindProperty("PhysicalAllocationId")!.IsNullable,
                conversions.FindProperty("PhysicalQuantityKg")!.IsNullable, conversions.FindProperty("FailureReason")!.IsNullable));
        Assert.Equal((3, 3, 64), (conversions.FindProperty("Quantity")!.GetScale(),
            conversions.FindProperty("PhysicalQuantityKg")!.GetScale(), conversions.FindProperty("FailureReason")!.GetMaxLength()));
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51711", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[FutureSupplyConversions]", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
    }

    [Fact]
    public void A_stored_conversion_link_is_never_modified()
    {
        using var context = Context();
        var row = new FutureSupplyConversionRow
        {
            Id = Guid.Parse("8a17c000-0000-4000-8000-0000000000c1"), CompanyId = CompanyA,
            AcceptanceId = Guid.Parse("8a17c000-0000-4000-8000-0000000000c2"),
            FutureAllocationId = Guid.Parse("8a17c000-0000-4000-8000-0000000000c3"),
            LabelId = Guid.Parse("8a17c000-0000-4000-8000-0000000000c4"), Outcome = 2, Quantity = 5m,
            FailureReason = "label-blocked", CreatedAtUtc = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero),
        };
        context.Attach(row);
        row.Quantity = 6m;

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_substitute_flag_migration_adds_a_sales_only_flag_and_its_Down_refuses_first()
    {
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryAllocationSubstituteFlag();
        using var context = Context();
        var allocations = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AllocationRow))!;

        var flag = allocations.FindProperty(nameof(AllocationRow.IsSubstitute))!;
        Assert.False(flag.IsNullable);
        Assert.Equal(InventoryDbContext.AllocationSubstituteCheck,
            Assert.Single(allocations.GetCheckConstraints(), check => check.Name == "CK_Allocations_Substitute").Sql);
        Assert.Contains("[DemandKind] IS NOT NULL", InventoryDbContext.AllocationSubstituteCheck, StringComparison.Ordinal);
        var added = Assert.Single(migration.UpOperations
            .OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>());
        Assert.Equal(("IsSubstitute", false, (object?)false), (added.Name, added.IsNullable, added.DefaultValue));

        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51653", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[Allocations]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[IsSubstitute] = 1", guard.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation>(migration.DownOperations[^1]);
    }

    [Fact]
    public void The_canonical_payload_is_stable_and_names_every_fact()
    {
        var movement = Receipt(Position(), 100m);
        var same = new StockMovement(movement.Id, movement.CompanyId, movement.Kind, movement.OccurredAtUtc, movement.Source,
            movement.Lines, CostResult.NotCalculated);
        var otherQuantity = new StockMovement(movement.Id, movement.CompanyId, movement.Kind, movement.OccurredAtUtc,
            movement.Source, [In(Position(), 100.001m)]);
        var otherSourceLine = new StockMovement(movement.Id, movement.CompanyId, movement.Kind, movement.OccurredAtUtc,
            new StockSourceReference(movement.Source.Kind, movement.Source.Id, Guid.NewGuid()), movement.Lines);

        Assert.Equal(64, InventoryStore.PayloadHash(movement).Length);
        Assert.Equal(InventoryStore.PayloadHash(movement), InventoryStore.PayloadHash(same));
        Assert.NotEqual(InventoryStore.PayloadHash(movement), InventoryStore.PayloadHash(otherQuantity));
        Assert.NotEqual(InventoryStore.PayloadHash(movement), InventoryStore.PayloadHash(otherSourceLine));
    }

    [Fact]
    public void The_canonical_payload_writes_a_cost_amount_without_trailing_zeros()
    {
        // #293 SİPARİŞ P3-2: 5.00 and 5 are the same amount, so the same movement offered again with
        // its amount serialised at another scale is a replay, not an id conflict.
        var movement = Receipt(Position(), 100m);
        StockMovement WithCost(decimal amount) => new(movement.Id, movement.CompanyId, movement.Kind, movement.OccurredAtUtc,
            movement.Source, movement.Lines, KnownCost(amount));

        Assert.Equal(InventoryStore.PayloadHash(WithCost(5m)), InventoryStore.PayloadHash(WithCost(5.00m)));
        Assert.Equal(InventoryStore.PayloadHash(WithCost(0m)), InventoryStore.PayloadHash(WithCost(0.000000m)));
        Assert.Equal(InventoryStore.PayloadHash(WithCost(12.5m)), InventoryStore.PayloadHash(WithCost(12.500000000000000000m)));
        Assert.NotEqual(InventoryStore.PayloadHash(WithCost(5m)), InventoryStore.PayloadHash(WithCost(5.01m)));
        Assert.NotEqual(InventoryStore.PayloadHash(WithCost(5m)), InventoryStore.PayloadHash(WithCost(50m)));
        Assert.NotEqual(InventoryStore.PayloadHash(WithCost(5m)), InventoryStore.PayloadHash(movement));
    }

    private static T Attach<T>(InventoryDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static AverageCostLedgerRow AverageCostLedger() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, LocationId = Guid.NewGuid(), ProductId = Guid.NewGuid(),
        Status = (int)AverageCostStatus.Known, QuantityKg = 5m, AverageCostPerKg = 10.5714m, Version = 1, UpdatedAtUtc = At,
    };

    private static AllocationChangeRow AllocationChange() => new AllocationChangeRow()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Kind = 1, SourceAllocationId = Guid.NewGuid(), SourceVersion = 1,
        SourceLabelId = Label1, SourceRemainingKg = 10m, TargetLabelId = Label1, TargetRemainingKg = 5m,
        Reason = "SYN-CHANGE", ChangedBy = "synthetic", CorrelationId = "synthetic", ChangedAtUtc = At,
    };

    private static StockCountRow StockCount() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, LabelId = Label1, LabelVersion = 1, OwnerKind = 1, OwnerId = CompanyA,
        CustodianKind = 1, CustodianId = CompanyA, LocationId = Warehouse1, ProductId = BandProduct, Block = 1,
        BookKg = 10m, CountedKg = 9m, CountedAtUtc = At, Reason = "SYN-COUNT", PayloadHash = new string('0', 64),
        RecordedAtUtc = At, RecordedBy = "synthetic", CorrelationId = "synthetic",
    };

    private static StockScrapRow StockScrap() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, LabelId = Label1, LabelVersion = 1, OwnerKind = 1, OwnerId = CompanyA,
        CustodianKind = 1, CustodianId = CompanyA, LocationId = Warehouse1, ProductId = BandProduct, Block = 1,
        BookKg = 10m, ScrapKg = 2m, ScrappedAtUtc = At, Reason = "SYN-SCRAP", PayloadHash = new string('0', 64),
        RecordedAtUtc = At, RecordedBy = "synthetic", CorrelationId = "synthetic",
    };

    private static LotRow Lot() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, ProductId = BandProduct, Version = 1, CreatedAtUtc = At, CreatedBy = "synthetic-actor",
    };

    private static LabelRow Label() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Code = "SYN-L", ProductId = BandProduct, LotId = Lot1, Version = 1,
    };

    private static OpeningCostRow OpeningCost() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = CompanyA,
        LocationId = Warehouse1,
        ProductId = BandProduct,
        UnitCostPerKg = 12.5m,
        Reason = "SYN-OPENING",
        PayloadHash = new string('0', 64),
        RecordedAtUtc = At,
        RecordedBy = "synthetic",
        CorrelationId = "synthetic",
    };

    private static BalanceRow Balance() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, OwnerKind = 1, OwnerId = CompanyA, CustodianKind = 1, CustodianId = CompanyA,
        LocationId = Warehouse1, LabelId = Label1, ProductId = BandProduct, Block = 1, QuantityKg = 1m,
    };

    private static AllocationRow Allocation() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, OwnerKind = 1, OwnerId = CompanyA,
        CustodianKind = 1, CustodianId = CompanyA, LocationId = Warehouse1, LabelId = Label1,
        Block = 1, AllocatedKg = 10m, RemainingKg = 10m, Status = 1, Version = 1,
        CreatedAtUtc = At, UpdatedAtUtc = At,
    };

    private static InventoryOutboxRow Outbox() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA,
        EventType = "inventory.definite-shipment.recorded.v1", SchemaVersion = 1,
        SourceOperation = "inventory.definite-shipment.record.v1",
        AggregateType = "Inventory.StockAllocation", AggregateId = Guid.NewGuid(),
        AggregateVersion = 2, Sequence = 2, PayloadJson = "{}", OccurredAtUtc = At,
        CorrelationId = "synthetic", RecordedAtUtc = At,
    };

    private static LabelTransformationRow Transformation() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Kind = 1, OccurredAtUtc = At,
        Reason = "synthetic split", PayloadHash = new string('0', 64), RecordedAtUtc = At,
        RecordedBy = "synthetic-actor", CorrelationId = "synthetic-correlation",
    };

    private static LabelTransformationInputRow TransformationInput() => new()
    {
        CompanyId = CompanyA, TransformationId = Guid.NewGuid(), Sequence = 1,
        LabelId = Guid.NewGuid(), LabelVersion = 1, QuantityKg = 1m,
    };

    private static LabelTransformationOutputRow TransformationOutput() => new()
    {
        CompanyId = CompanyA, TransformationId = Guid.NewGuid(), Sequence = 1,
        LabelId = Guid.NewGuid(), LabelVersion = 1, QuantityKg = 1m,
    };

    private static MovementRow Movement() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Kind = 1, OccurredAtUtc = At, SourceKind = "test.source",
        SourceId = Guid.NewGuid(), CostStatus = 1, PayloadHash = new string('0', 64), RecordedAtUtc = At,
        RecordedBy = "synthetic-actor", CorrelationId = "synthetic-correlation",
    };

    /// <summary>A context of company A, or of no resolved company at all.</summary>
    private static InventoryDbContext Context(bool resolved = true)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new InventoryDbContext(options, company);
    }
}
