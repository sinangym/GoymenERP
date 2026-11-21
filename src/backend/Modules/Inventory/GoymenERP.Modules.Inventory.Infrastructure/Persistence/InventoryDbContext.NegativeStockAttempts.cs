using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryDbContext
{
    internal const string NegativeStockAttemptStateCheck =
        "[RequestedKg] > 0 AND [AvailableKg] < [RequestedKg] AND [MissingKg] = [RequestedKg] - [AvailableKg] AND [LineNumber] >= 1";
    internal const string NegativeStockAttemptBoundaryCheck = "[ObservedCountBoundaryVersion] IS NULL OR [ObservedCountBoundaryVersion] >= 0";
    internal const string NegativeStockResolutionBoundaryCheck = "[CountBoundaryVersion] IS NULL OR [CountBoundaryVersion] >= 1";
    internal const string NegativeStockValidationGuardVersionCheck = "[Version] >= 1";
    internal const string NegativeStockCountBoundaryVersionCheck = "[Version] >= 1";
    internal DbSet<NegativeStockAttemptRow> NegativeStockAttempts => Set<NegativeStockAttemptRow>();
    internal DbSet<NegativeStockAttemptResolutionRow> NegativeStockAttemptResolutions => Set<NegativeStockAttemptResolutionRow>();
    internal DbSet<NegativeStockValidationGuardRow> NegativeStockValidationGuards => Set<NegativeStockValidationGuardRow>();
    internal DbSet<NegativeStockCountBoundaryRow> NegativeStockCountBoundaries => Set<NegativeStockCountBoundaryRow>();

    private void ConfigureNegativeStockAttempts(ModelBuilder model)
    {
        var attempt = model.Entity<NegativeStockAttemptRow>();
        attempt.ToTable("NegativeStockAttempts", Schema, table =>
            table.HasCheckConstraint("CK_InventoryNegativeStockAttempts_State", NegativeStockAttemptStateCheck));
        attempt.HasKey(row => row.Id);
        attempt.HasAlternateKey(row => new { row.CompanyId, row.Id });
        attempt.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        // This exact prefix also serializes count resolution with a fresh refusal recorder under Serializable.
        attempt.HasIndex(row => new { row.CompanyId, row.ProductId, row.LocationId, row.Id });
        attempt.HasIndex(row => new { row.CompanyId, row.MovementId, row.ActorId, row.KeyHash }).IsUnique();
        attempt.HasIndex(row => new { row.CompanyId, row.RefusedAtUtc, row.Id });
        attempt.Property(row => row.ActorId).HasMaxLength(128).IsRequired();
        attempt.Property(row => row.KeyHash).HasMaxLength(64).IsRequired();
        attempt.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
        attempt.Property(row => row.PermissionCode).HasMaxLength(128).IsRequired();
        attempt.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        attempt.Property(row => row.SourceChannel).HasMaxLength(128).IsRequired();
        attempt.Property(row => row.LabelCode).HasMaxLength(128).IsRequired();
        attempt.Property(row => row.SourceKind).HasMaxLength(128).IsRequired();
        attempt.Property(row => row.RequestedKg).HasPrecision(18, 3);
        attempt.Property(row => row.AvailableKg).HasPrecision(18, 3);
        attempt.Property(row => row.MissingKg).HasPrecision(18, 3);
        // Deliberately no FK to physical rows: they may have been staged by the rolled-back command.
        var resolution = model.Entity<NegativeStockAttemptResolutionRow>();
        resolution.ToTable("NegativeStockAttemptResolutions", Schema);
        resolution.HasKey(row => new { row.CompanyId, row.AttemptId });
        resolution.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        resolution.Property(row => row.DecidedBy).HasMaxLength(128).IsRequired();
        resolution.HasOne<NegativeStockAttemptRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, Id = row.AttemptId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        resolution.HasOne<StockCountRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, Id = row.StockCountId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);

        attempt.ToTable("NegativeStockAttempts", Schema, table => table.HasCheckConstraint(
            "CK_InventoryNegativeStockAttempts_Boundary", NegativeStockAttemptBoundaryCheck));
        resolution.ToTable("NegativeStockAttemptResolutions", Schema, table => table.HasCheckConstraint(
            "CK_InventoryNegativeStockAttemptResolutions_Boundary", NegativeStockResolutionBoundaryCheck));
        var guard = model.Entity<NegativeStockValidationGuardRow>();
        guard.ToTable("NegativeStockValidationGuards", Schema, table => table.HasCheckConstraint(
            "CK_InventoryNegativeStockValidationGuards_Version", NegativeStockValidationGuardVersionCheck));
        guard.HasKey(row => row.CompanyId);
        guard.Property(row => row.Version).IsConcurrencyToken();
        guard.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        var boundary = model.Entity<NegativeStockCountBoundaryRow>();
        boundary.ToTable("NegativeStockCountBoundaries", Schema, table => table.HasCheckConstraint(
            "CK_InventoryNegativeStockCountBoundaries_Version", NegativeStockCountBoundaryVersionCheck));
        boundary.HasKey(row => new { row.CompanyId, row.ProductId, row.LocationId, row.Version });
        boundary.HasAlternateKey(row => new { row.CompanyId, row.StockCountId, row.Version });
        boundary.HasIndex(row => new { row.CompanyId, row.StockCountId }).IsUnique();
        boundary.Property(row => row.DecidedBy).HasMaxLength(128).IsRequired();
        boundary.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        boundary.HasOne<StockCountRow>().WithMany().HasForeignKey(row => new { row.CompanyId, Id = row.StockCountId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        boundary.HasOne<StockCountDecisionRow>().WithMany().HasForeignKey(row => new { row.CompanyId, CountId = row.StockCountId })
            .OnDelete(DeleteBehavior.Restrict);
        resolution.HasOne<NegativeStockCountBoundaryRow>().WithMany()
            .HasForeignKey(row => new { row.CompanyId, row.StockCountId, Version = row.CountBoundaryVersion })
            .HasPrincipalKey(row => new { row.CompanyId, row.StockCountId, row.Version }).OnDelete(DeleteBehavior.Restrict);
    }
}
