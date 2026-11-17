using Microsoft.EntityFrameworkCore;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingDbContext
{
    internal DbSet<OrderReopeningRow> OrderReopenings => Set<OrderReopeningRow>();
    internal const string OrderReopeningCheck = "[ClosedOrderVersion] >= 2 AND [ReopenedOrderVersion] = [ClosedOrderVersion] + 1 AND [ClosedByUserId] <> '00000000-0000-0000-0000-000000000000' AND [ReopenedByUserId] <> '00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0";

    private void ConfigureReopenings(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<OrderReopeningRow>();
        row.ToTable("OrderReopenings", Schema, table => table.HasCheckConstraint("CK_OrderReopenings_Transition", OrderReopeningCheck));
        row.HasKey(item => new { item.CompanyId, item.OrderId, item.ReopenedOrderVersion });
        row.HasIndex(item => new { item.CompanyId, item.OrderId, item.ClosedOrderVersion }).IsUnique();
        row.Property(item => item.Reason).HasMaxLength(ProductionPlan.DecisionReasonMaximumLength).IsRequired();
        row.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
        row.HasOne<OrderRow>().WithMany().HasForeignKey(item => new { item.CompanyId, item.OrderId })
            .HasPrincipalKey(item => new { item.CompanyId, item.Id }).OnDelete(DeleteBehavior.Restrict);
        row.HasQueryFilter(item => item.CompanyId == companyContext.CompanyId);
    }
}

internal sealed class OrderReopeningRow : IManufacturingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public long ClosedOrderVersion { get; set; }
    public Guid ClosedByUserId { get; set; }
    public DateTimeOffset ClosedAtUtc { get; set; }
    public long ReopenedOrderVersion { get; set; }
    public Guid ReopenedByUserId { get; set; }
    public DateTimeOffset ReopenedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}
