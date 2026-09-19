using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Inventory.Domain.Costing;
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
            typeof(LotRow), typeof(LabelRow), typeof(LabelPrintRow), typeof(LabelDocumentRow), typeof(MovementRow), typeof(MovementLineRow),
            typeof(BalanceRow), typeof(SourceGrainRow), typeof(OpeningCountRow), typeof(OpeningCountLineRow),
            typeof(InventoryCommandRow),
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
        foreach (var type in new[] { typeof(MovementLineRow), typeof(BalanceRow) })
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
        Assert.Equal("FK_Labels_Lots_CompanyId_LotId", Assert.Single(design.FindEntityType(typeof(LabelRow))!.GetForeignKeys(),
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
            default:
                Attach(context, Balance()).CompanyId = CompanyB;
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
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

    private static LotRow Lot() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, ProductId = BandProduct, Version = 1, CreatedAtUtc = At, CreatedBy = "synthetic-actor",
    };

    private static LabelRow Label() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, Code = "SYN-L", ProductId = BandProduct, LotId = Lot1, Version = 1,
    };

    private static BalanceRow Balance() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, OwnerKind = 1, OwnerId = CompanyA, CustodianKind = 1, CustodianId = CompanyA,
        LocationId = Warehouse1, LabelId = Label1, ProductId = BandProduct, Block = 1, QuantityKg = 1m,
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
