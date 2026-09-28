using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// Logistics's own tables in the <c>logistics</c> schema (LOG-4a; ADR-002, ADR-009, ADR-011; the Sales
/// SALES-P2 shape). Every row is a tenant row of the instruction's company: a global query filter on the
/// server-resolved company and a write guard that refuses any row of another company, any company change,
/// any delete and any column move outside the axis a command may move (the header's status / version /
/// time; a line's four share totals and block flag; shares, weighbridge records and attempts append only). The attempt key
/// (company, command, key hash) is a primary key under a binary collation; the raw key is never stored.
/// The audit sidecar is Audit's table, written in the same save and filtered the same way.
/// </summary>
public sealed class LogisticsDbContext(DbContextOptions<LogisticsDbContext> options, ICompanyContext companyContext)
    : DbContext(options)
{
    public const string Schema = "logistics";
    public const string IdentityCollation = "Latin1_General_100_BIN2";
    private const string EmptyGuid = "'00000000-0000-0000-0000-000000000000'";

    internal DbSet<LoadingInstructionRow> Instructions => Set<LoadingInstructionRow>();

    internal DbSet<LoadingInstructionLineRow> Lines => Set<LoadingInstructionLineRow>();

    internal DbSet<LoadingShareRow> Shares => Set<LoadingShareRow>();

    internal DbSet<LoadingAttemptRow> Attempts => Set<LoadingAttemptRow>();

    internal DbSet<VehicleWeighingRow> Weighings => Set<VehicleWeighingRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var company = modelBuilder.Entity<OrganizationCompanyReference>();
        company.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        company.HasKey(x => x.Id);

        ConfigureInstructions(modelBuilder);
        ConfigureLines(modelBuilder);
        ConfigureShares(modelBuilder);
        ConfigureAttempts(modelBuilder);
        ConfigureWeighings(modelBuilder);
        modelBuilder.ConfigureAuditEvent(excludeFromMigrations: true);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyWriteGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyWriteGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ConfigureInstructions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LoadingInstructionRow>();
        entity.ToTable("LoadingInstructions", Schema, table =>
        {
            table.HasCheckConstraint("CK_LoadingInstructions_Identity",
                $"[Id] <> {EmptyGuid} AND [CompanyId] <> {EmptyGuid} AND [CustomerPartyId] <> {EmptyGuid} AND [CompanyAccountId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_LoadingInstructions_Status", "[Status] IN (1, 2)");
            table.HasCheckConstraint("CK_LoadingInstructions_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_LoadingInstructions_Text", "LEN([CreatedBy]) > 0 AND LEN([CorrelationId]) > 0");
        });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.EffectiveOn).HasColumnType("date");
        entity.Property(x => x.CreatedBy).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_LoadingInstructions_CompanyId_CreatedAtUtc_Id");
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_LoadingInstructions_CompanyId_Status_CreatedAtUtc_Id");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureLines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LoadingInstructionLineRow>();
        entity.ToTable("LoadingInstructionLines", Schema, table =>
        {
            table.HasCheckConstraint("CK_LoadingInstructionLines_LineNo", "[LineNo] >= 1");
            table.HasCheckConstraint("CK_LoadingInstructionLines_Identity",
                $"[LineId] <> {EmptyGuid} AND [SourceOrderId] <> {EmptyGuid} AND [SourceOrderLineId] <> {EmptyGuid} "
                + $"AND [ProductId] <> {EmptyGuid} AND [UnitId] <> {EmptyGuid} AND ([AllocationId] IS NULL OR [AllocationId] <> {EmptyGuid})");
            table.HasCheckConstraint("CK_LoadingInstructionLines_Revision", "[SourceOrderRevision] >= 0");
            // §7.4 / §13: instructed ≥ loaded ≥ shipped ≥ delivered ≥ 0, never counted twice.
            table.HasCheckConstraint("CK_LoadingInstructionLines_Shares",
                "[Instructed] > 0 AND [Loaded] >= 0 AND [Shipped] >= 0 AND [Delivered] >= 0 "
                + "AND [Loaded] <= [Instructed] AND [Shipped] <= [Loaded] AND [Delivered] <= [Shipped]");
        });
        entity.HasKey(x => new { x.CompanyId, x.InstructionId, x.LineNo });
        entity.HasIndex(x => new { x.CompanyId, x.InstructionId, x.LineId }).IsUnique().HasDatabaseName("UX_LoadingInstructionLines_LineId");
        entity.HasIndex(x => new { x.CompanyId, x.SourceOrderId, x.SourceOrderLineId }).HasDatabaseName("IX_LoadingInstructionLines_SourceOrderLine");
        foreach (var quantity in new[] { nameof(LoadingInstructionLineRow.Instructed), nameof(LoadingInstructionLineRow.Loaded),
                     nameof(LoadingInstructionLineRow.Shipped), nameof(LoadingInstructionLineRow.Delivered) })
        {
            entity.Property<decimal>(quantity).HasPrecision(18, 3);
        }

        entity.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureShares(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LoadingShareRow>();
        entity.ToTable("LoadingShares", Schema, table =>
        {
            table.HasCheckConstraint("CK_LoadingShares_ShareNo", "[ShareNo] >= 1");
            table.HasCheckConstraint("CK_LoadingShares_Identity", $"[ShareId] <> {EmptyGuid} AND [LineId] <> {EmptyGuid}");
            table.HasCheckConstraint("CK_LoadingShares_Quantity", "[Quantity] > 0");
            // A load names how it was measured; a delivery names its source shipment; a shipment names neither.
            table.HasCheckConstraint("CK_LoadingShares_Kind",
                "([Kind] = 1 AND [Measurement] IN (1, 2) AND [SourceShipmentId] IS NULL) "
                + "OR ([Kind] = 2 AND [Measurement] IS NULL AND [SourceShipmentId] IS NULL) "
                + $"OR ([Kind] = 3 AND [Measurement] IS NULL AND [SourceShipmentId] IS NOT NULL AND [SourceShipmentId] <> {EmptyGuid})");
        });
        entity.HasKey(x => new { x.CompanyId, x.InstructionId, x.ShareNo });
        entity.HasIndex(x => new { x.CompanyId, x.InstructionId, x.ShareId }).IsUnique().HasDatabaseName("UX_LoadingShares_ShareId");
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ConfigureAttempts(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LoadingAttemptRow>();
        entity.ToTable("LoadingAttempts", Schema, table =>
        {
            table.HasCheckConstraint("CK_LoadingAttempts_Identity",
                $"[CompanyId] <> {EmptyGuid} AND LEN([CommandName]) > 0 AND LEN([CorrelationId]) > 0 AND LEN([ActorId]) > 0");
            table.HasCheckConstraint("CK_LoadingAttempts_Hashes", "LEN([IdempotencyKeyHash]) = 64 AND LEN([RequestHash]) = 64");
            // ADR-011 §7: a stored result or a recorded terminal refusal, never both.
            table.HasCheckConstraint("CK_LoadingAttempts_Outcome",
                $"([Outcome] = 1 AND [InstructionId] IS NOT NULL AND [InstructionId] <> {EmptyGuid} AND [RefusalJson] IS NULL) "
                + "OR ([Outcome] = 2 AND [InstructionId] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson]) = 1)");
        });
        entity.HasKey(x => new { x.CompanyId, x.CommandName, x.IdempotencyKeyHash });
        entity.Property(x => x.CommandName).HasMaxLength(160).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        entity.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        // One create attempt per stored instruction; the mutation commands' attempts share the instruction id (the Sales lesson).
        entity.HasIndex(x => new { x.CompanyId, x.InstructionId }).IsUnique().HasDatabaseName("UX_LoadingAttempts_InstructionId")
            .HasFilter("[InstructionId] IS NOT NULL AND [CommandName] = N'" + LogisticsCommands.CreateV1 + "'");
        entity.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    /// <summary>
    /// AT-053: one manual weighbridge record per instruction (the key), append-only. SQL Server holds the calculation's
    /// shape too: the direction decides which weighing is the gross, the gross is strictly heavier (so the net is
    /// positive), net = gross − tare (WGT-001) and difference = net − loaded (WGT-002, signed). No tolerance column.
    /// </summary>
    private void ConfigureWeighings(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<VehicleWeighingRow>();
        entity.ToTable("VehicleWeighings", Schema, table =>
        {
            table.HasCheckConstraint("CK_VehicleWeighings_Identity", $"[WeighingId] <> {EmptyGuid} AND LEN([RecordedBy]) > 0");
            table.HasCheckConstraint("CK_VehicleWeighings_Direction", "[Direction] IN (1, 2)");
            table.HasCheckConstraint("CK_VehicleWeighings_Weights",
                "[FirstWeighingKg] > 0 AND [SecondWeighingKg] > 0 AND [NetKg] > 0 AND [LoadedKgAtWeighing] >= 0 AND [SharesBefore] >= 0");
            table.HasCheckConstraint("CK_VehicleWeighings_Net",
                "([Direction] = 1 AND [SecondWeighingKg] > [FirstWeighingKg] AND [NetKg] = [SecondWeighingKg] - [FirstWeighingKg]) "
                + "OR ([Direction] = 2 AND [FirstWeighingKg] > [SecondWeighingKg] AND [NetKg] = [FirstWeighingKg] - [SecondWeighingKg])");
            table.HasCheckConstraint("CK_VehicleWeighings_Difference", "[DifferenceKg] = [NetKg] - [LoadedKgAtWeighing]");
        });
        entity.HasKey(x => new { x.CompanyId, x.InstructionId });
        entity.HasIndex(x => new { x.CompanyId, x.WeighingId }).IsUnique().HasDatabaseName("UX_VehicleWeighings_WeighingId");
        foreach (var weight in new[] { nameof(VehicleWeighingRow.FirstWeighingKg), nameof(VehicleWeighingRow.SecondWeighingKg),
                     nameof(VehicleWeighingRow.NetKg), nameof(VehicleWeighingRow.LoadedKgAtWeighing), nameof(VehicleWeighingRow.DifferenceKg) })
        {
            entity.Property<decimal>(weight).HasPrecision(18, 3);
        }

        entity.Property(x => x.RecordedBy).HasMaxLength(128).IsRequired();
        entity.HasOne<LoadingInstructionRow>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.InstructionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }

    private void ApplyWriteGuards()
    {
        LogisticsAuditEventTenantGuard.Validate(this, companyContext);
        foreach (var entry in ChangeTracker.Entries<ILogisticsTenantRow>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Sevkiyat kayıtları silinmez; değişiklik yeni bir kayıttır.");
            }

            if (entry.Entity.CompanyId == Guid.Empty)
            {
                throw new InvalidOperationException("Sevkiyat kaydı CompanyId olmadan yazılamaz.");
            }

            if (!companyContext.IsResolved || entry.Entity.CompanyId != companyContext.CompanyId)
            {
                throw new InvalidOperationException("Sevkiyat kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.");
            }

            if (entry.State == EntityState.Modified)
            {
                string[] allowed = entry.Entity switch
                {
                    LoadingInstructionRow => [nameof(LoadingInstructionRow.Status), nameof(LoadingInstructionRow.UpdatedAtUtc)],
                    LoadingInstructionLineRow =>
                    [
                        nameof(LoadingInstructionLineRow.Loaded), nameof(LoadingInstructionLineRow.Shipped),
                        nameof(LoadingInstructionLineRow.Delivered), nameof(LoadingInstructionLineRow.IsBlocked),
                    ],
                    _ => [],
                };
                var changed = entry.Properties
                    .Where(property => property.IsModified && !property.Metadata.IsConcurrencyToken)
                    .Select(property => property.Metadata.Name)
                    .Where(name => !allowed.Contains(name))
                    .ToArray();
                if (changed.Length > 0 || allowed.Length == 0)
                {
                    throw new InvalidOperationException("Sevkiyat kaydının yalnız izinli alanı değişir; paylar ve deneme kayıtları eklenir, değişmez.");
                }
            }
        }
    }
}
