using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

public sealed partial class InventoryDbContext
{
    internal DbSet<InventorySubcontractAllocationSourceRow> SubcontractAllocationSources => Set<InventorySubcontractAllocationSourceRow>();
    private void ConfigureSubcontractDelivery(ModelBuilder model)
    {
        var entity = model.Entity<InventorySubcontractAllocationSourceRow>();
        entity.ToTable("SubcontractAllocationSources", Schema, table => table.HasCheckConstraint("CK_SubcontractAllocationSources_Proof",
            "[CustomerPartyId] <> '00000000-0000-0000-0000-000000000000' AND [CustomerAccountId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND [SalesOrderId] <> '00000000-0000-0000-0000-000000000000' AND [SalesOrderLineId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND [SalesOrderVersion] > 0 AND [SalesLineRevision] >= 0 AND [ManufacturingOrderVersion] > 0 AND [ManufacturingPlanVersion] > 0 "
            + "AND [ManufacturingOrderId] <> '00000000-0000-0000-0000-000000000000' AND [ManufacturingPlanId] <> '00000000-0000-0000-0000-000000000000' "
            + "AND [OutputFactId] <> '00000000-0000-0000-0000-000000000000' AND [OutputNetKg] > 0 "
            + "AND ISJSON([SourceJson])=1 AND LEN([SourceHash])=64"));
        entity.HasKey(x => new { x.CompanyId, x.AllocationId });
        entity.Property(x => x.OutputNetKg).HasPrecision(18, 3);
        entity.Property(x => x.SourceJson).HasMaxLength(8000).IsRequired();
        entity.Property(x => x.SourceHash).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        entity.HasOne<AllocationRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AllocationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<LabelRow>().WithMany().HasForeignKey(x => new { x.CompanyId, x.LabelId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.CompanyId, x.SalesOrderId, x.SalesOrderLineId, x.LabelId });
        entity.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
    }
}

/// <summary>Immutable source proof of a customer-goods hold. A company-account identity never substitutes for its owner.</summary>
internal sealed class InventorySubcontractAllocationSourceRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid AllocationId { get; set; }
    public Guid LabelId { get; set; }
    public Guid CustomerPartyId { get; set; }
    public Guid CustomerAccountId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public long SalesOrderVersion { get; set; }
    public int SalesLineRevision { get; set; }
    public Guid ManufacturingOrderId { get; set; }
    public long ManufacturingOrderVersion { get; set; }
    public Guid ManufacturingPlanId { get; set; }
    public long ManufacturingPlanVersion { get; set; }
    public Guid OutputFactId { get; set; }
    public decimal OutputNetKg { get; set; }
    public string SourceJson { get; set; } = "";
    public string SourceHash { get; set; } = "";
}
