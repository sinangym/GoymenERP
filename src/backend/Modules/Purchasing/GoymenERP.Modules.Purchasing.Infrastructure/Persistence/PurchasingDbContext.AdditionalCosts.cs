using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingDbContext
{
    internal DbSet<PurchaseAdditionalCostRow> AdditionalCosts => Set<PurchaseAdditionalCostRow>();
    internal DbSet<PurchaseAdditionalCostAttemptRow> AdditionalCostAttempts => Set<PurchaseAdditionalCostAttemptRow>();
    internal const string AdditionalCostSourceCheck =
        "([SourceKind]=1 AND [NormalOrderId] IS NOT NULL AND [NormalOrderId]=[OrderId] AND [ServiceOrderId] IS NULL "
        + "AND [ServiceLineId] IS NULL AND [ServiceAcceptanceId] IS NULL "
        + "AND (([NormalLineId] IS NULL AND [NormalAcceptanceId] IS NULL) OR ([NormalLineId] IS NOT NULL AND [NormalAcceptanceId] IS NOT NULL))) OR "
        + "([SourceKind]=2 AND [ServiceOrderId] IS NOT NULL AND [ServiceOrderId]=[OrderId] AND [NormalOrderId] IS NULL "
        + "AND [NormalLineId] IS NULL AND [NormalAcceptanceId] IS NULL "
        + "AND (([ServiceLineId] IS NULL AND [ServiceAcceptanceId] IS NULL) OR ([ServiceLineId] IS NOT NULL AND [ServiceAcceptanceId] IS NOT NULL)))";
    private void ConfigureAdditionalCosts(ModelBuilder builder)
    {
        var cost = builder.Entity<PurchaseAdditionalCostRow>();
        cost.ToTable("AdditionalCosts", Schema, table =>
        { table.HasCheckConstraint("CK_AdditionalCosts_Source", AdditionalCostSourceCheck); table.HasCheckConstraint("CK_AdditionalCosts_Json", "ISJSON([SnapshotJson])=1"); });
        cost.HasKey(x => new { x.CompanyId, x.CostId }); cost.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)");
        cost.HasIndex(x => new { x.CompanyId, x.OrderId, x.RecordedAtUtc, x.CostId });
        cost.HasOne<PurchaseOrderRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.NormalOrderId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        cost.HasOne<PurchaseServiceOrderRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ServiceOrderId }).OnDelete(DeleteBehavior.Restrict);
        cost.HasOne<PurchaseAcceptanceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.NormalOrderId, x.NormalLineId, x.NormalAcceptanceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.OrderId, x.LineId, x.AcceptanceId }).OnDelete(DeleteBehavior.Restrict);
        cost.HasOne<PurchaseServiceAcceptanceRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ServiceOrderId, x.ServiceLineId, x.ServiceAcceptanceId })
            .OnDelete(DeleteBehavior.Restrict);
        cost.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        var attempt = builder.Entity<PurchaseAdditionalCostAttemptRow>();
        attempt.ToTable("AdditionalCostAttempts", Schema, table => table.HasCheckConstraint("CK_AdditionalCostAttempts_Result",
            "([SnapshotJson] IS NOT NULL AND ISJSON([SnapshotJson])=1 AND [RefusalJson] IS NULL) OR "
            + "([SnapshotJson] IS NULL AND [RefusalJson] IS NOT NULL AND ISJSON([RefusalJson])=1)"));
        attempt.HasKey(x => new { x.CompanyId, x.KeyHash }); attempt.Property(x => x.KeyHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).UseCollation(IdentityCollation);
        attempt.Property(x => x.ActorId).HasMaxLength(128).UseCollation(IdentityCollation); attempt.Property(x => x.CorrelationId).HasMaxLength(128);
        attempt.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)"); attempt.Property(x => x.RefusalJson).HasColumnType("nvarchar(max)");
        attempt.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        ConfigureAdditionalCostInvoiceBindings(builder);
    }
    private void GuardAdditionalCosts()
    {
        GuardAdditionalCostInvoiceBindings();
        foreach (var entry in ChangeTracker.Entries<PurchaseAdditionalCostRow>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Ek masraf faktörü değiştirilemez.");
            if (entry.State != EntityState.Added) continue;
            var value = JsonSerializer.Deserialize<PurchaseAdditionalCostState>(entry.Entity.SnapshotJson, PurchaseAdditionalCostStoreV1.Json)
                ?? throw new InvalidOperationException("Masraf görüntüsü yok.");
            PurchaseAdditionalCost.Validate(value); var row = entry.Entity;
            if (value.CompanyId != row.CompanyId || value.CostId != row.CostId || value.OrderId != row.OrderId || (int)value.SourceKind != row.SourceKind
                || value.RecordedAtUtc != row.RecordedAtUtc || value.AcceptanceId != (row.NormalAcceptanceId ?? row.ServiceAcceptanceId)
                || value.AcceptanceLineId != (row.NormalLineId ?? row.ServiceLineId))
                throw new InvalidOperationException("Masraf görüntüsü kapsamı uyuşmuyor.");
        }
        foreach (var entry in ChangeTracker.Entries<PurchaseAdditionalCostAttemptRow>())
            if (entry.State is EntityState.Modified or EntityState.Deleted) throw new InvalidOperationException("Ek masraf komutu geçmişi değiştirilemez.");
    }
}
internal sealed class PurchaseAdditionalCostRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; } public Guid CostId { get; set; } public Guid OrderId { get; set; } public int SourceKind { get; set; }
    public Guid? NormalOrderId { get; set; } public Guid? NormalLineId { get; set; } public Guid? NormalAcceptanceId { get; set; }
    public Guid? ServiceOrderId { get; set; } public Guid? ServiceLineId { get; set; } public Guid? ServiceAcceptanceId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; } public string SnapshotJson { get; set; } = "";
}
internal sealed class PurchaseAdditionalCostAttemptRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; } public string KeyHash { get; set; } = ""; public string RequestHash { get; set; } = "";
    public string ActorId { get; set; } = ""; public string CorrelationId { get; set; } = "";
    public string? SnapshotJson { get; set; } public string? RefusalJson { get; set; }
}
