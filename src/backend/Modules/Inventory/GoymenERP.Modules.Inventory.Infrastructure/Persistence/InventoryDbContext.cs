using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Inventory's own tables in the <c>inventory</c> schema (ADR-INV-001 option A, ADR-002).
/// <list type="bullet">
/// <item>Every row is a tenant row of the company holding the stock: a global query filter on the
/// server-resolved company, and a write guard that refuses any row of another company, any
/// company change and any write without a resolved company.</item>
/// <item>A label is unique by (company, code) only; there is no group-wide code index, so no
/// code can reveal another company's stock.</item>
/// <item>A location is referenced by (company, location) against Organization's alternate key, so
/// a row can never point at another company's location; a company by its id.</item>
/// <item>Movements, their lines, prints and document attachments are append-only; a balance
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

    internal DbSet<LabelRow> Labels => Set<LabelRow>();

    internal DbSet<LabelPrintRow> LabelPrints => Set<LabelPrintRow>();

    internal DbSet<LabelDocumentRow> LabelDocuments => Set<LabelDocumentRow>();

    internal DbSet<MovementRow> Movements => Set<MovementRow>();

    internal DbSet<SourceGrainRow> SourceGrains => Set<SourceGrainRow>();

    internal DbSet<MovementLineRow> MovementLines => Set<MovementLineRow>();

    internal DbSet<BalanceRow> Balances => Set<BalanceRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureOrganizationReferences(modelBuilder);
        ConfigureLabels(modelBuilder);
        ConfigureLabelPrints(modelBuilder);
        ConfigureLabelDocuments(modelBuilder);
        ConfigureSourceGrains(modelBuilder);
        ConfigureMovements(modelBuilder);
        ConfigureMovementLines(modelBuilder);
        ConfigureBalances(modelBuilder);
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

    private void ConfigureLabels(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LabelRow>();
        entity.ToTable("Labels", Schema, table =>
        {
            table.HasCheckConstraint("CK_Labels_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [ProductId] <> '00000000-0000-0000-0000-000000000000' AND [LotId] <> '00000000-0000-0000-0000-000000000000'");
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
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.ProductId });
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId)
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

    private void ConfigureMovements(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MovementRow>();
        entity.ToTable("Movements", Schema, table =>
        {
            table.HasCheckConstraint("CK_Movements_Identity",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [CompanyId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [SourceId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND ([SourceLineId] IS NULL OR [SourceLineId] <> '00000000-0000-0000-0000-000000000000')");
            table.HasCheckConstraint("CK_Movements_Kind", "[Kind] BETWEEN 1 AND 4");
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
