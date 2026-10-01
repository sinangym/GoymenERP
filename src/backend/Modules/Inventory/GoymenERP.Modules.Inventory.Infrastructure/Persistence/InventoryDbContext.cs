using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Domain.Labels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Inventory's own tables in the <c>inventory</c> schema (ADR-INV-001 option A, ADR-002).
/// <list type="bullet">
/// <item>Every row is a tenant row of the company holding the stock: a global query filter on the
/// server-resolved company, and a write guard that refuses any row of another company, any
/// company change and any write without a resolved company.</item>
/// <item>A label belongs to a lot of its own company (DEC-PRD-003): (company, lot) references the
/// lot's (company, id) alternate key, so a label can never name another company's lot and never
/// exist without its lot.</item>
/// <item>A label is unique by (company, code) only; there is no group-wide code index, so no
/// code can reveal another company's stock.</item>
/// <item>A location is referenced by (company, location) against Organization's alternate key, so
/// a row can never point at another company's location; a company by its id.</item>
/// <item>Lots, movements, their lines, prints and document attachments are append-only; a balance
/// changes its quantity only; a label its version only. Nothing is ever deleted.</item>
/// <item>One source (or source line) causes at most one movement of each kind: two filtered
/// unique indexes (ORG #281 P3-1). A source is answered whole or line by line per kind, never
/// both: every movement references its source's one grain row (#293 ORG P3-1).</item>
/// <item>Label codes and source kinds compare under the columns' database collation (SQL Server's
/// default, case-insensitive today); no collation is chosen here (README, INV-3).</item>
/// </list>
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "inventory";

    internal DbSet<LotRow> Lots => Set<LotRow>();

    internal DbSet<LabelRow> Labels => Set<LabelRow>();

    internal DbSet<LabelPrintRow> LabelPrints => Set<LabelPrintRow>();

    internal DbSet<LabelDocumentRow> LabelDocuments => Set<LabelDocumentRow>();

    internal DbSet<LabelTransformationRow> LabelTransformations => Set<LabelTransformationRow>();

    internal DbSet<LabelTransformationInputRow> LabelTransformationInputs => Set<LabelTransformationInputRow>();

    internal DbSet<LabelTransformationOutputRow> LabelTransformationOutputs => Set<LabelTransformationOutputRow>();

    internal DbSet<MovementRow> Movements => Set<MovementRow>();

    internal DbSet<SourceGrainRow> SourceGrains => Set<SourceGrainRow>();

    internal DbSet<MovementLineRow> MovementLines => Set<MovementLineRow>();

    internal DbSet<BalanceRow> Balances => Set<BalanceRow>();

    internal DbSet<AllocationRow> Allocations => Set<AllocationRow>();

    internal DbSet<AllocationChangeRow> AllocationChanges => Set<AllocationChangeRow>();

    internal DbSet<InventoryOutboxRow> OutboxMessages => Set<InventoryOutboxRow>();

    internal DbSet<OpeningCountRow> OpeningCounts => Set<OpeningCountRow>();

    internal DbSet<OpeningCountLineRow> OpeningCountLines => Set<OpeningCountLineRow>();

    internal DbSet<InventoryCommandRow> Commands => Set<InventoryCommandRow>();

    internal DbSet<StockCountRow> StockCounts => Set<StockCountRow>();

    internal DbSet<StockCountDecisionRow> StockCountDecisions => Set<StockCountDecisionRow>();

    internal DbSet<StockScrapRow> StockScraps => Set<StockScrapRow>();

    internal DbSet<StockScrapDecisionRow> StockScrapDecisions => Set<StockScrapDecisionRow>();

    internal DbSet<InventoryTransferRow> Transfers => Set<InventoryTransferRow>();

    internal DbSet<InventoryTransferLineRow> TransferLines => Set<InventoryTransferLineRow>();

    internal DbSet<InventoryTransferDispatchRow> TransferDispatches => Set<InventoryTransferDispatchRow>();

    internal DbSet<InventoryTransferDispatchLineRow> TransferDispatchLines => Set<InventoryTransferDispatchLineRow>();

    internal DbSet<InventoryTransferBucketRow> TransferBuckets => Set<InventoryTransferBucketRow>();

    internal DbSet<InventoryTransferAcceptanceRow> TransferAcceptances => Set<InventoryTransferAcceptanceRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureOrganizationReferences(modelBuilder);
        ConfigureLots(modelBuilder);
        ConfigureLabels(modelBuilder);
        ConfigureLabelPrints(modelBuilder);
        ConfigureLabelDocuments(modelBuilder);
        ConfigureLabelTransformations(modelBuilder);
        ConfigureSourceGrains(modelBuilder);
        ConfigureMovements(modelBuilder);
        ConfigureMovementLines(modelBuilder);
        ConfigureBalances(modelBuilder);
        ConfigureAllocations(modelBuilder);
        ConfigureAllocationChanges(modelBuilder);
        ConfigureOutboxMessages(modelBuilder);
        ConfigureOpeningCounts(modelBuilder);
        ConfigureCommands(modelBuilder);
        ConfigureStockCounts(modelBuilder);
        ConfigureStockScraps(modelBuilder);
        ConfigureInventoryTransfers(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureOrganizationReferences(ModelBuilder modelBuilder)
    {
        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        var location = modelBuilder.Entity<OrganizationLocationReference>();
        location.ToTable("Locations", "organization", table => table.ExcludeFromMigrations());
        location.HasKey(x => new { x.CompanyId, x.Id });
    }

    private void ConfigureLots(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LotRow>();
        entity.ToTable("Lots", Schema, table =>
        {
            table.HasCheckConstraint("CK_Lots_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Lots_Trace",
                "([HeatNumber] IS NULL OR LEN([HeatNumber]) > 0) AND ([CoilNumber] IS NULL OR LEN([CoilNumber]) > 0)");
            table.HasCheckConstraint("CK_Lots_CreatedBy", "LEN([CreatedBy]) > 0");
            table.HasCheckConstraint("CK_Lots_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.HeatNumber).HasMaxLength(64);
        entity.Property(x => x.CoilNumber).HasMaxLength(64);
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.ProductId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLabels(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LabelRow>();
        entity.ToTable("Labels", Schema, table =>
        {
            table.HasCheckConstraint("CK_Labels_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [LotId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Labels_ProductVersion",
                "[ProductVersion] IS NULL OR LEN([ProductVersion]) > 0");
            table.HasCheckConstraint("CK_Labels_Code", "LEN([Code]) > 0");
            table.HasCheckConstraint("CK_Labels_RequiredDocumentsJson", "ISJSON([RequiredDocumentsJson]) = 1");
            table.HasCheckConstraint("CK_Labels_Version", "[Version] >= 1");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Code).HasMaxLength(64).IsRequired();
        entity.Property(x => x.HeatNumber).HasMaxLength(64);
        entity.Property(x => x.CoilNumber).HasMaxLength(64);
        entity.Property(x => x.RequiredDocumentsJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.ProductVersion).HasMaxLength(MaterialLabel.MaximumProductVersionLength);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.ProductId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        // DEC-PRD-003: the label's lot, of the same company (the lot's alternate key).
        entity.HasOne<LotRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LotId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .HasConstraintName("FK_Labels_Lots_CompanyId_LotId")
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLabelPrints(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LabelPrintRow>();
        entity.ToTable("LabelPrints", Schema, table =>
        {
            table.HasCheckConstraint("CK_LabelPrints_Sequence", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_LabelPrints_Reason",
                "([Sequence] = 1 AND [Reason] IS NULL) OR ([Sequence] > 1 AND [Reason] IS NOT NULL AND LEN([Reason]) > 0)");
            table.HasCheckConstraint("CK_LabelPrints_PrintedBy", "LEN([PrintedBy]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.LabelId, x.Sequence });
        entity.Property(x => x.PrintedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(1000);
        LabelOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLabelDocuments(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LabelDocumentRow>();
        entity.ToTable("LabelDocuments", Schema, table =>
        {
            table.HasCheckConstraint("CK_LabelDocuments_Sequence", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_LabelDocuments_Text", "LEN([Kind]) > 0 AND LEN([AttachedBy]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.LabelId, x.Sequence });
        entity.Property(x => x.Kind).HasMaxLength(64).IsRequired();
        entity.Property(x => x.AttachedBy).HasMaxLength(128).IsRequired();
        LabelOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLabelTransformations(ModelBuilder modelBuilder)
    {
        var transformation = modelBuilder.Entity<LabelTransformationRow>();
        transformation.ToTable("LabelTransformations", Schema, table =>
        {
            table.HasCheckConstraint("CK_LabelTransformations_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_LabelTransformations_Kind", "[Kind] IN (1, 2)");
            table.HasCheckConstraint("CK_LabelTransformations_Text",
                "LEN([Reason]) > 0 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([PayloadHash]) = 64");
        });
        transformation.HasKey(x => x.Id);
        transformation.HasAlternateKey(x => new { x.CompanyId, x.Id });
        transformation.Property(x => x.Id).ValueGeneratedNever();
        transformation.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        transformation.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        transformation.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        transformation.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        transformation.HasIndex(x => new { x.CompanyId, x.OccurredAtUtc, x.Id });
        transformation.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        transformation.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        ConfigureLabelTransformationInput(modelBuilder.Entity<LabelTransformationInputRow>());
        ConfigureLabelTransformationOutput(modelBuilder.Entity<LabelTransformationOutputRow>());
    }

    private void ConfigureLabelTransformationInput(EntityTypeBuilder<LabelTransformationInputRow> entity)
    {
        entity.ToTable("LabelTransformationInputs", Schema, table =>
        {
            table.HasCheckConstraint("CK_LabelTransformationInputs_Sequence", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_LabelTransformationInputs_State", "[LabelVersion] >= 1 AND [QuantityKg] > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.TransformationId, x.Sequence });
        entity.Property(x => x.QuantityKg).HasPrecision(18, 3);
        entity.HasIndex(x => new { x.CompanyId, x.TransformationId, x.LabelId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.LabelId, x.TransformationId });
        TransformationOf(entity);
        LabelOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLabelTransformationOutput(EntityTypeBuilder<LabelTransformationOutputRow> entity)
    {
        entity.ToTable("LabelTransformationOutputs", Schema, table =>
        {
            table.HasCheckConstraint("CK_LabelTransformationOutputs_Sequence", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_LabelTransformationOutputs_State", "[LabelVersion] >= 1 AND [QuantityKg] > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.TransformationId, x.Sequence });
        entity.Property(x => x.QuantityKg).HasPrecision(18, 3);
        entity.HasIndex(x => new { x.CompanyId, x.TransformationId, x.LabelId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.LabelId, x.TransformationId });
        TransformationOf(entity);
        LabelOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private static void TransformationOf<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class, IInventoryTenantRow
    {
        entity.HasOne<LabelTransformationRow>().WithMany()
            .HasForeignKey("CompanyId", "TransformationId")
            .HasPrincipalKey(nameof(LabelTransformationRow.CompanyId), nameof(LabelTransformationRow.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void ConfigureMovements(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MovementRow>();
        entity.ToTable("Movements", Schema, table =>
        {
            table.HasCheckConstraint("CK_Movements_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND ([SourceLineId] IS NULL OR [SourceLineId] <> '00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint("CK_Movements_Kind", "[Kind] BETWEEN 1 AND 9");
            table.HasCheckConstraint("CK_Movements_SourceKind", "LEN([SourceKind]) > 0");
            table.HasCheckConstraint("CK_Movements_Cost",
                "([CostStatus] IN (1, 2) AND [CostAmount] IS NULL AND [CostCurrencyId] IS NULL) OR "
                + "([CostStatus] = 3 AND [CostAmount] IS NOT NULL AND [CostAmount] >= 0 AND [CostCurrencyId] IS NOT NULL "
                + "AND [CostCurrencyId] <> '00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint("CK_Movements_PayloadHash", "LEN([PayloadHash]) = 64");
            table.HasCheckConstraint("CK_Movements_SourceGrain",
                "([SourceLineId] IS NULL AND [SourceGrain] = 1) OR ([SourceLineId] IS NOT NULL AND [SourceGrain] = 2)");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.SourceKind).HasMaxLength(64).IsRequired();
        entity.Property(x => x.CostAmount).HasPrecision(38, 18);
        entity.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.Kind, x.SourceKind, x.SourceId })
            .IsUnique()
            .HasFilter("[SourceLineId] IS NULL")
            .HasDatabaseName("UX_Movements_Source");
        entity.HasIndex(x => new { x.CompanyId, x.Kind, x.SourceKind, x.SourceId, x.SourceLineId })
            .IsUnique()
            .HasFilter("[SourceLineId] IS NOT NULL")
            .HasDatabaseName("UX_Movements_SourceLine");
        entity.HasIndex(x => new { x.CompanyId, x.RecordedAtUtc, x.Id });
        entity.HasOne<SourceGrainRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.Kind, x.SourceKind, x.SourceId, x.SourceGrain })
            .HasPrincipalKey(x => new { x.CompanyId, x.Kind, x.SourceKind, x.SourceId, x.Grain })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// One row per (company, kind, source): the grain its first movement fixed. The key holds one
    /// grain per source; the alternate key with the grain is what movements reference.
    /// </summary>
    private void ConfigureSourceGrains(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SourceGrainRow>();
        entity.ToTable("SourceGrains", Schema, table =>
        {
            table.HasCheckConstraint("CK_SourceGrains_Grain", "[Grain] IN (1, 2)");
            table.HasCheckConstraint("CK_SourceGrains_Identity",
                "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND LEN([SourceKind]) > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.Kind, x.SourceKind, x.SourceId });
        entity.HasAlternateKey(x => new { x.CompanyId, x.Kind, x.SourceKind, x.SourceId, x.Grain });
        entity.Property(x => x.SourceKind).HasMaxLength(64).IsRequired();
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureMovementLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MovementLineRow>();
        entity.ToTable("MovementLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_MovementLines_LineNumber", "[LineNumber] >= 1");
            table.HasCheckConstraint("CK_MovementLines_Enums",
                "[Direction] IN (1, 2) AND [OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2)");
            table.HasCheckConstraint("CK_MovementLines_Quantity", "[QuantityKg] > 0");
        });
        entity.HasKey(x => new { x.CompanyId, x.MovementId, x.LineNumber });
        entity.Property(x => x.QuantityKg).HasPrecision(18, 3);
        entity.HasIndex(x => new { x.CompanyId, x.LabelId });
        entity.HasOne<MovementRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MovementId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        LabelOf(entity);
        LocationOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureBalances(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<BalanceRow>();
        entity.ToTable("Balances", Schema, table =>
        {
            table.HasCheckConstraint("CK_Balances_Enums",
                "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2)");
            table.HasCheckConstraint("CK_Balances_Quantity", "[QuantityKg] >= 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.QuantityKg).HasPrecision(18, 3);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new
            {
                x.CompanyId, x.LabelId, x.OwnerKind, x.OwnerId, x.CustodianKind, x.CustodianId, x.LocationId,
                x.ProductId, x.Block,
            })
            .IsUnique()
            .HasDatabaseName("UX_Balances_Position");
        LabelOf(entity);
        LocationOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// Physical allocations are separate from stock movements (DEC-STK-003). This table stores the
    /// exact free position and optimistic version required by LOG-5. New physical-sales allocations
    /// carry their immutable demand facts; rows predating that command remain readable with null demand.
    /// </summary>
    private void ConfigureAllocations(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AllocationRow>();
        entity.ToTable("Allocations", Schema, table =>
        {
            table.HasCheckConstraint("CK_Allocations_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OwnerId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CustodianId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [LocationId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [LabelId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Allocations_State",
                "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] = 1 "
                + "AND [Status] IN (1, 2, 3) AND [Version] >= 1");
            table.HasCheckConstraint("CK_Allocations_Quantity",
                "[AllocatedKg] > 0 AND [RemainingKg] >= 0 AND [RemainingKg] <= [AllocatedKg] "
                + "AND (([Status] = 1 AND [RemainingKg] > 0) OR ([Status] IN (2, 3) AND [RemainingKg] = 0))");
            table.HasCheckConstraint("CK_Allocations_Demand",
                "([DemandKind] IS NULL AND [DemandId] IS NULL AND [DemandLineId] IS NULL "
                + "AND [DemandProductId] IS NULL AND [DemandProductVersion] IS NULL AND [Reason] IS NULL "
                + "AND [ReservedUntilUtc] IS NULL) "
                + "OR ([DemandKind] IS NOT NULL AND [DemandKind] = 'sales.order-line' "
                + "AND [DemandId] IS NOT NULL AND [DemandId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [DemandLineId] IS NOT NULL AND [DemandLineId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [DemandProductId] IS NOT NULL AND [DemandProductId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [DemandProductVersion] IS NOT NULL AND LEN([DemandProductVersion]) > 0 "
                + "AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");
            table.HasCheckConstraint("CK_Allocations_Release",
                "([Status] = 3 AND [ReleasedAtUtc] IS NOT NULL AND [ReleaseReason] IS NOT NULL "
                + "AND LEN(LTRIM(RTRIM([ReleaseReason]))) > 0) "
                + "OR ([Status] <> 3 AND [ReleasedAtUtc] IS NULL AND [ReleaseReason] IS NULL)");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.AllocatedKg).HasPrecision(18, 3);
        entity.Property(x => x.RemainingKg).HasPrecision(18, 3);
        entity.Property(x => x.DemandKind).HasMaxLength(64).IsUnicode(false);
        entity.Property(x => x.DemandProductVersion).HasMaxLength(128).IsUnicode(false);
        entity.Property(x => x.Reason).HasMaxLength(1000);
        entity.Property(x => x.ReleaseReason).HasMaxLength(1000);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new
        {
            x.CompanyId, x.LabelId, x.OwnerKind, x.OwnerId, x.CustodianKind, x.CustodianId, x.LocationId, x.Block, x.Status,
        }).HasDatabaseName("IX_Allocations_ActivePosition");
        entity.HasIndex(x => new { x.CompanyId, x.DemandKind, x.DemandId, x.DemandLineId, x.Status })
            .HasDatabaseName("IX_Allocations_Demand");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        LabelOf(entity);
        LocationOf(entity);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// INV-AL-1 change history: append-only (the write guard refuses any update or delete), both allocations and both
    /// labels named by (company, id), one change per source allocation version. Kind 1 = resize (source = target),
    /// 2 = move (source released, target new).
    /// </summary>
    private void ConfigureAllocationChanges(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AllocationChangeRow>();
        entity.ToTable("AllocationChanges", Schema, table =>
        {
            table.HasCheckConstraint("CK_AllocationChanges_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceAllocationId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [TargetAllocationId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceLabelId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [TargetLabelId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_AllocationChanges_Kind",
                "([Kind] = 1 AND [SourceAllocationId] = [TargetAllocationId] AND [SourceLabelId] = [TargetLabelId] "
                + "AND [TargetRemainingKg] <> [SourceRemainingKg]) "
                + "OR ([Kind] = 2 AND [SourceAllocationId] <> [TargetAllocationId] AND [SourceLabelId] <> [TargetLabelId])");
            table.HasCheckConstraint("CK_AllocationChanges_State",
                "[SourceVersion] >= 1 AND [SourceRemainingKg] > 0 AND [TargetRemainingKg] > 0");
            table.HasCheckConstraint("CK_AllocationChanges_Text",
                "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([ChangedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.SourceRemainingKg).HasPrecision(18, 3);
        entity.Property(x => x.TargetRemainingKg).HasPrecision(18, 3);
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.ChangedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.SourceAllocationId, x.SourceVersion })
            .IsUnique()
            .HasDatabaseName("UX_AllocationChanges_SourceVersion");
        entity.HasIndex(x => new { x.CompanyId, x.TargetAllocationId })
            .HasDatabaseName("IX_AllocationChanges_Target");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<AllocationRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceAllocationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<AllocationRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.TargetAllocationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<LabelRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceLabelId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<LabelRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.TargetLabelId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureOutboxMessages(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<InventoryOutboxRow>();
        entity.ToTable("OutboxMessages", Schema, table =>
        {
            table.HasCheckConstraint("CK_OutboxMessages_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [AggregateId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OutboxMessages_Envelope",
                "LEN([EventType]) > 0 AND [SchemaVersion] >= 1 AND LEN([SourceOperation]) > 0 "
                + "AND LEN([AggregateType]) > 0 AND [AggregateVersion] >= 1 AND [Sequence] >= 1 "
                + "AND LEN([CorrelationId]) > 0 AND ISJSON([PayloadJson]) = 1");
        });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.EventType).HasMaxLength(160).IsUnicode(false).IsRequired();
        entity.Property(x => x.SourceOperation).HasMaxLength(128).IsUnicode(false).IsRequired();
        entity.Property(x => x.AggregateType).HasMaxLength(160).IsUnicode(false).IsRequired();
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2").IsRequired();
        entity.Property(x => x.CausationId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
        entity.HasIndex(x => new { x.CompanyId, x.EventType, x.AggregateId, x.Sequence })
            .IsUnique().HasDatabaseName("UX_OutboxMessages_AggregateSequence");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// INV-OPEN-1: the count document and its counted pieces. A count is at one of its company's
    /// locations; every line names a label of the company and its one receipt movement (the line id is
    /// the movement id), so no line exists without its posted receipt. Append-only.
    /// </summary>
    private void ConfigureOpeningCounts(ModelBuilder modelBuilder)
    {
        var count = modelBuilder.Entity<OpeningCountRow>();
        count.ToTable("OpeningCounts", Schema, table =>
        {
            table.HasCheckConstraint("CK_OpeningCounts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [LocationId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_OpeningCounts_Text",
                "LEN([SheetReference]) > 0 AND LEN([ContentHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        count.HasKey(x => x.Id);
        count.HasAlternateKey(x => new { x.CompanyId, x.Id });
        count.Property(x => x.Id).ValueGeneratedNever();
        count.Property(x => x.SheetReference).HasMaxLength(128).IsRequired();
        count.Property(x => x.ContentHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        count.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        count.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        count.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        LocationOf(count);
        count.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<OpeningCountLineRow>();
        line.ToTable("OpeningCountLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_OpeningCountLines_LineNumber", "[LineNumber] >= 1");
            table.HasCheckConstraint("CK_OpeningCountLines_Quantity", "[QuantityKg] > 0");
        });
        line.HasKey(x => new { x.CompanyId, x.CountId, x.LineNumber });
        line.Property(x => x.QuantityKg).HasPrecision(18, 3);
        line.HasIndex(x => new { x.CompanyId, x.LineId }).IsUnique();
        line.HasOne<OpeningCountRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        line.HasOne<MovementRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LineId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        LabelOf(line);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// ADR-011 commands of the company (INV-OPEN-1 part 2): one row per (company, command, key hash), the
    /// actor, the canonical payload hash and the result a replay repeats. Append-only.
    /// </summary>
    private void ConfigureCommands(ModelBuilder modelBuilder)
    {
        var command = modelBuilder.Entity<InventoryCommandRow>();
        command.ToTable("Commands", Schema, table =>
        {
            table.HasCheckConstraint("CK_Commands_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([PayloadHash]) = 64");
            table.HasCheckConstraint("CK_Commands_Text",
                "LEN([CommandName]) > 0 AND LEN([ActorId]) > 0 AND LEN([CorrelationId]) > 0 AND ISJSON([ResultJson]) = 1");
        });
        command.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        command.Property(x => x.CommandName).HasMaxLength(128).IsUnicode(false);
        command.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false);
        command.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        command.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        command.Property(x => x.ResultJson).HasColumnType("nvarchar(max)").IsRequired();
        command.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        command.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        command.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// ST-05 / AT-037 (DEC-STK-007): a count of one stocked label at its stored position, and at most one decision
    /// on it. A count names its company's label and location; an approval names the adjustment movement it posted
    /// (both by (company, id)), a rejection none. Both are append-only: history is never deleted.
    /// </summary>
    private void ConfigureStockCounts(ModelBuilder modelBuilder)
    {
        var count = modelBuilder.Entity<StockCountRow>();
        count.ToTable("StockCounts", Schema, table =>
        {
            table.HasCheckConstraint("CK_StockCounts_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OwnerId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CustodianId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockCounts_State",
                "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2) AND [LabelVersion] >= 1 "
                + "AND [BookKg] > 0 AND [CountedKg] >= 0");
            table.HasCheckConstraint("CK_StockCounts_Text",
                "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 "
                + "AND LEN([CorrelationId]) > 0");
        });
        count.HasKey(x => x.Id);
        count.HasAlternateKey(x => new { x.CompanyId, x.Id });
        count.Property(x => x.Id).ValueGeneratedNever();
        count.Property(x => x.BookKg).HasPrecision(18, 3);
        count.Property(x => x.CountedKg).HasPrecision(18, 3);
        count.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        count.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        count.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        count.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        count.HasIndex(x => new { x.CompanyId, x.LabelId, x.RecordedAtUtc, x.Id });
        count.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        LabelOf(count);
        LocationOf(count);
        count.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var decision = modelBuilder.Entity<StockCountDecisionRow>();
        decision.ToTable("StockCountDecisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_StockCountDecisions_Decision",
                "([Decision] = 1 AND [MovementId] IS NOT NULL "
                + "AND [MovementId] <> '00000000-0000-0000-0000-000000000000') "
                + "OR ([Decision] = 2 AND [MovementId] IS NULL)");
            table.HasCheckConstraint("CK_StockCountDecisions_Text",
                "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([DecidedBy]) > 0 "
                + "AND LEN([CorrelationId]) > 0");
        });
        decision.HasKey(x => new { x.CompanyId, x.CountId });
        decision.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        decision.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        decision.Property(x => x.DecidedBy).HasMaxLength(128).IsRequired();
        decision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        decision.HasIndex(x => new { x.CompanyId, x.MovementId })
            .IsUnique()
            .HasFilter("[MovementId] IS NOT NULL");
        decision.HasOne<StockCountRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        decision.HasOne<MovementRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MovementId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        decision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// ST-02 scrap write-off: a scrap of one stocked label at its stored position, and at most one decision on it. A
    /// scrap names its company's label and location; an approval names the scrap movement it posted (both by
    /// (company, id)), a rejection none. The scrap is positive and never more than the book. Both are append-only.
    /// </summary>
    private void ConfigureStockScraps(ModelBuilder modelBuilder)
    {
        var scrap = modelBuilder.Entity<StockScrapRow>();
        scrap.ToTable("StockScraps", Schema, table =>
        {
            table.HasCheckConstraint("CK_StockScraps_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [OwnerId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [CustodianId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_StockScraps_State",
                "[OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2) AND [LabelVersion] >= 1 "
                + "AND [BookKg] > 0 AND [ScrapKg] > 0 AND [ScrapKg] <= [BookKg]");
            table.HasCheckConstraint("CK_StockScraps_Text",
                "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 "
                + "AND LEN([CorrelationId]) > 0");
        });
        scrap.HasKey(x => x.Id);
        scrap.HasAlternateKey(x => new { x.CompanyId, x.Id });
        scrap.Property(x => x.Id).ValueGeneratedNever();
        scrap.Property(x => x.BookKg).HasPrecision(18, 3);
        scrap.Property(x => x.ScrapKg).HasPrecision(18, 3);
        scrap.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        scrap.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        scrap.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        scrap.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        scrap.HasIndex(x => new { x.CompanyId, x.LabelId, x.RecordedAtUtc, x.Id });
        scrap.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        LabelOf(scrap);
        LocationOf(scrap);
        scrap.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var decision = modelBuilder.Entity<StockScrapDecisionRow>();
        decision.ToTable("StockScrapDecisions", Schema, table =>
        {
            table.HasCheckConstraint("CK_StockScrapDecisions_Decision",
                "([Decision] = 1 AND [MovementId] IS NOT NULL "
                + "AND [MovementId] <> '00000000-0000-0000-0000-000000000000') "
                + "OR ([Decision] = 2 AND [MovementId] IS NULL)");
            table.HasCheckConstraint("CK_StockScrapDecisions_Text",
                "LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN([PayloadHash]) = 64 AND LEN([DecidedBy]) > 0 "
                + "AND LEN([CorrelationId]) > 0");
        });
        decision.HasKey(x => new { x.CompanyId, x.ScrapId });
        decision.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        decision.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        decision.Property(x => x.DecidedBy).HasMaxLength(128).IsRequired();
        decision.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        decision.HasIndex(x => new { x.CompanyId, x.MovementId })
            .IsUnique()
            .HasFilter("[MovementId] IS NOT NULL");
        decision.HasOne<StockScrapRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ScrapId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        decision.HasOne<MovementRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.MovementId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        decision.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureInventoryTransfers(ModelBuilder modelBuilder)
    {
        var transfer = modelBuilder.Entity<InventoryTransferRow>();
        transfer.ToTable("Transfers", Schema, table =>
        {
            table.HasCheckConstraint("CK_Transfers_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceLocationId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [DestinationLocationId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceCustodianId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [DestinationCustodianId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Transfers_State",
                "[Version] >= 1 AND [Status] BETWEEN 1 AND 4 AND [SourceCustodianKind] IN (1, 2) "
                + "AND [DestinationCustodianKind] IN (1, 2) AND NOT ([SourceLocationId] = [DestinationLocationId] "
                + "AND [SourceCustodianKind] = [DestinationCustodianKind] AND [SourceCustodianId] = [DestinationCustodianId])");
            table.HasCheckConstraint("CK_Transfers_Text",
                "LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        transfer.HasKey(x => x.Id);
        transfer.HasAlternateKey(x => new { x.CompanyId, x.Id });
        transfer.Property(x => x.Id).ValueGeneratedNever();
        transfer.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        transfer.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        transfer.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        transfer.Property(x => x.RowVersion).IsRowVersion();
        transfer.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);
        transfer.HasOne<OrganizationLocationReference>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceLocationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        transfer.HasOne<OrganizationLocationReference>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DestinationLocationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        transfer.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var line = modelBuilder.Entity<InventoryTransferLineRow>();
        line.ToTable("TransferLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_TransferLines_Identity",
                "[LineId] <> '00000000-0000-0000-0000-000000000000' AND [LabelId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_TransferLines_State",
                "[Sequence] >= 1 AND [ExpectedLabelVersion] >= 1 AND [PlannedKg] > 0");
        });
        line.HasKey(x => new { x.CompanyId, x.TransferId, x.LineId });
        line.Property(x => x.PlannedKg).HasPrecision(18, 3);
        line.HasIndex(x => new { x.CompanyId, x.TransferId, x.Sequence }).IsUnique();
        line.HasIndex(x => new { x.CompanyId, x.TransferId, x.LabelId }).IsUnique();
        TransferOf(line);
        LabelOf(line);
        line.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var dispatch = modelBuilder.Entity<InventoryTransferDispatchRow>();
        dispatch.ToTable("TransferDispatches", Schema, table =>
        {
            table.HasCheckConstraint("CK_TransferDispatches_Identity",
                "[MovementId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND ([DocumentLineId] IS NULL OR [DocumentLineId] <> '00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint("CK_TransferDispatches_Text",
                "LEN([DocumentKind]) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        dispatch.HasKey(x => new { x.CompanyId, x.TransferId });
        dispatch.Property(x => x.DocumentKind).HasMaxLength(64).IsRequired();
        dispatch.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        dispatch.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        dispatch.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        dispatch.HasIndex(x => new { x.CompanyId, x.MovementId }).IsUnique();
        TransferOf(dispatch);
        MovementOf(dispatch);
        dispatch.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var dispatchedLine = modelBuilder.Entity<InventoryTransferDispatchLineRow>();
        dispatchedLine.ToTable("TransferDispatchLines", Schema, table =>
            table.HasCheckConstraint("CK_TransferDispatchLines_State",
                "[LabelVersionAfterDispatch] >= 2 AND [ActualKg] > 0"));
        dispatchedLine.HasKey(x => new { x.CompanyId, x.TransferId, x.LineId });
        dispatchedLine.Property(x => x.ActualKg).HasPrecision(18, 3);
        dispatchedLine.Property(x => x.DifferenceKg).HasPrecision(18, 3);
        TransferLineOf(dispatchedLine);
        dispatchedLine.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var bucket = modelBuilder.Entity<InventoryTransferBucketRow>();
        bucket.ToTable("TransferBuckets", Schema, table =>
        {
            table.HasCheckConstraint("CK_TransferBuckets_Identity",
                "[OwnerId] <> '00000000-0000-0000-0000-000000000000' AND [CustodianId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [LabelId] <> '00000000-0000-0000-0000-000000000000' AND [ProductId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_TransferBuckets_State",
                "[Sequence] >= 1 AND [OwnerKind] IN (1, 2) AND [CustodianKind] IN (1, 2) AND [Block] IN (1, 2) AND [QuantityKg] > 0");
        });
        bucket.HasKey(x => new { x.CompanyId, x.TransferId, x.LineId, x.Sequence });
        bucket.Property(x => x.QuantityKg).HasPrecision(18, 3);
        bucket.HasIndex(x => new { x.CompanyId, x.TransferId, x.LineId, x.Block }).IsUnique();
        TransferLineOf(bucket);
        LabelOf(bucket);
        LocationOf(bucket);
        bucket.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);

        var acceptance = modelBuilder.Entity<InventoryTransferAcceptanceRow>();
        acceptance.ToTable("TransferAcceptances", Schema, table =>
        {
            table.HasCheckConstraint("CK_TransferAcceptances_Identity",
                "[MovementId] <> '00000000-0000-0000-0000-000000000000' AND [DocumentId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND ([DocumentLineId] IS NULL OR [DocumentLineId] <> '00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint("CK_TransferAcceptances_Text",
                "LEN([DocumentKind]) > 0 AND LEN([PayloadHash]) = 64 AND LEN([RecordedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        acceptance.HasKey(x => new { x.CompanyId, x.TransferId, x.LineId });
        acceptance.Property(x => x.DocumentKind).HasMaxLength(64).IsRequired();
        acceptance.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        acceptance.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        acceptance.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        acceptance.HasIndex(x => new { x.CompanyId, x.MovementId }).IsUnique();
        TransferLineOf(acceptance);
        MovementOf(acceptance);
        acceptance.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private static void TransferOf<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class, IInventoryTenantRow
    {
        entity.HasOne<InventoryTransferRow>().WithMany()
            .HasForeignKey("CompanyId", "TransferId")
            .HasPrincipalKey(nameof(InventoryTransferRow.CompanyId), nameof(InventoryTransferRow.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void TransferLineOf<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class, IInventoryTenantRow
    {
        entity.HasOne<InventoryTransferLineRow>().WithMany()
            .HasForeignKey("CompanyId", "TransferId", "LineId")
            .HasPrincipalKey(nameof(InventoryTransferLineRow.CompanyId), nameof(InventoryTransferLineRow.TransferId),
                nameof(InventoryTransferLineRow.LineId))
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void MovementOf<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class, IInventoryTenantRow
    {
        entity.HasOne<MovementRow>().WithMany()
            .HasForeignKey("CompanyId", "MovementId")
            .HasPrincipalKey(nameof(MovementRow.CompanyId), nameof(MovementRow.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>A row of one label, bound by (company, label) so it can never name another company's label.</summary>
    private static void LabelOf<TRow>(EntityTypeBuilder<TRow> entity)
        where TRow : class, IInventoryTenantRow
    {
        entity.HasOne<LabelRow>().WithMany()
            .HasForeignKey("CompanyId", "LabelId")
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>A row at one location, bound by (company, location) to Organization's alternate key.</summary>
    private static void LocationOf<TRow>(EntityTypeBuilder<TRow> entity)
        where TRow : class, IInventoryTenantRow
    {
        entity.HasOne<OrganizationLocationReference>().WithMany()
            .HasForeignKey("CompanyId", "LocationId")
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void ApplyWriteGuards()
    {
        InventoryAuditEventTenantGuard.Validate(this, companyContext);

        foreach (var entry in ChangeTracker.Entries<IInventoryTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Stok kayıtları silinmez; düzeltme yeni bir harekettir.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Stok kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Stok kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    LabelRow => [nameof(LabelRow.Version), nameof(LabelRow.UpdatedAtUtc)],
                    BalanceRow => [nameof(BalanceRow.QuantityKg)],
                    AllocationRow =>
                    [
                        // INV-AL-1 change: an in-place resize re-states AllocatedKg (consumed part kept exactly).
                        nameof(AllocationRow.AllocatedKg),
                        nameof(AllocationRow.RemainingKg), nameof(AllocationRow.Status),
                        nameof(AllocationRow.Version), nameof(AllocationRow.UpdatedAtUtc),
                        nameof(AllocationRow.ReleasedAtUtc), nameof(AllocationRow.ReleaseReason),
                    ],
                    InventoryTransferRow =>
                    [
                        nameof(InventoryTransferRow.Version), nameof(InventoryTransferRow.Status),
                        nameof(InventoryTransferRow.UpdatedAtUtc),
                    ],
                    _ => [],
                };
                var changed = entry.Properties
                    .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                    .Select(property => property.Metadata.Name)
                    .Where(name => !allowed.Contains(name))
                    .ToArray();
                if (changed.Length > 0 || (allowed.Length == 0))
                {
                    throw new InvalidOperationException(
                        "Stok kaydının yalnız izinli alanı değişir; hareketler, satırlar, baskılar ve belge ekleri değişmez.");
                }
            }
        }
    }
}
