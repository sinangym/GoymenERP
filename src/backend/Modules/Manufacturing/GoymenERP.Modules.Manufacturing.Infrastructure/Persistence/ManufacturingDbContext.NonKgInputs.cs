using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingDbContext
{
    internal DbSet<NonKgInputRow> NonKgInputs => Set<NonKgInputRow>();
    private void ConfigureNonKgInputs(ModelBuilder model)
    {
        var row = model.Entity<NonKgInputRow>();
        row.ToTable("NonKgInputFacts", Schema, table => table.HasCheckConstraint("CK_NonKgInputFacts_Shape",
            "[Revision] > 0 AND [Kind] IN (1,2) AND [Unit] IN (1,2,3) AND ([Kind] <> 1 OR [Unit] = 1) "
            + "AND LEN([Item]) > 0 AND LEN([Quantity]) > 0 AND LEN([Reason]) > 0 "
            + "AND ([ReplacesFactId] IS NULL OR [ReplacesFactId] <> [FactId]) AND ([Quantity] <> '0' OR [ReplacesFactId] IS NOT NULL)"));
        row.HasKey(item => new { item.CompanyId, item.OrderId, item.Revision });
        row.HasAlternateKey(item => new { item.CompanyId, item.OrderId, item.FactId });
        row.HasIndex(item => new { item.CompanyId, item.FactId }).IsUnique();
        row.HasIndex(item => new { item.CompanyId, item.OrderId, item.ReplacesFactId }).IsUnique().HasFilter("[ReplacesFactId] IS NOT NULL");
        row.Property(item => item.Item).HasMaxLength(200).IsRequired();
        row.Property(item => item.Quantity).HasMaxLength(60).IsUnicode(false).IsRequired(); // Preserve the exact decimal; no storage rounding.
        row.Property(item => item.Reason).HasMaxLength(1000).IsRequired();
        row.Property(item => item.RecordedBy).HasMaxLength(128).IsRequired();
        row.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
        row.HasOne<OrderRow>().WithMany().HasForeignKey(item => new { item.CompanyId, item.OrderId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<NonKgInputRow>().WithMany().HasForeignKey(item => new { item.CompanyId, item.OrderId, item.ReplacesFactId })
            .HasPrincipalKey(item => new { item.CompanyId, item.OrderId, item.FactId }).OnDelete(DeleteBehavior.Restrict);
        row.HasQueryFilter(item => item.CompanyId == companyContext.CompanyId);
    }
}

internal sealed class NonKgInputRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public Guid FactId { get; set; }
    public long Revision { get; set; }
    public int Kind { get; set; }
    public string Item { get; set; } = "";
    public int Unit { get; set; }
    public string Quantity { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid? ReplacesFactId { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = "";
    public string CorrelationId { get; set; } = "";
}
