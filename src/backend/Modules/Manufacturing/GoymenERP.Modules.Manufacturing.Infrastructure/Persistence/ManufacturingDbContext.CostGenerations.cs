using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingDbContext
{
    internal DbSet<CostGenerationRow> CostGenerations => Set<CostGenerationRow>();
    internal const string CostGenerationCheck = "[ClosedOrderVersion] >= 2 AND [Revision] >= 1 AND [State] BETWEEN 1 AND 4 "
        + "AND LEN([InventoryVersion]) > 0 AND LEN([InputFingerprint]) = 64 "
        + "AND ISJSON([EvidenceJson]) = 1 AND ISJSON([ResultJson]) = 1 AND LEN(LTRIM(RTRIM([Reason]))) > 0";

    private void ConfigureCostGenerations(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<CostGenerationRow>();
        row.ToTable("CostGenerations", Schema, table => table.HasCheckConstraint("CK_CostGenerations_Evidence", CostGenerationCheck));
        row.HasKey(item => new { item.CompanyId, item.GenerationId, item.Revision });
        row.HasIndex(item => new { item.CompanyId, item.OrderId, item.ClosedOrderVersion, item.Revision }).IsUnique();
        row.Property(item => item.InventoryVersion).HasMaxLength(128).IsRequired();
        row.Property(item => item.InputFingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
        row.Property(item => item.CalculatorVersion).HasMaxLength(128).IsRequired();
        row.Property(item => item.Reason).HasMaxLength(1000).IsRequired();
        row.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
        row.Property(item => item.RecordedBy).HasMaxLength(128).IsRequired();
        row.Property(item => item.EvidenceJson).IsRequired();
        row.Property(item => item.ResultJson).IsRequired();
        row.HasOne<OrderRow>().WithMany().HasForeignKey(item => new { item.CompanyId, item.OrderId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        row.HasQueryFilter(item => item.CompanyId == companyContext.CompanyId);
    }
}

internal sealed class CostGenerationRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public Guid GenerationId { get; set; }
    public long ClosedOrderVersion { get; set; }
    public long Revision { get; set; }
    public string InventoryVersion { get; set; } = string.Empty;
    public int State { get; set; }
    public string InputFingerprint { get; set; } = string.Empty;
    public string CalculatorVersion { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string RecordedBy { get; set; } = string.Empty;
}
