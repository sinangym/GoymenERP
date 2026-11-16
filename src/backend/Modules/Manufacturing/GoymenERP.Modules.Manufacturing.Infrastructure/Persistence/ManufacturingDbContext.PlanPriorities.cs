using GoymenERP.Modules.Manufacturing.Domain.Plans;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

public sealed partial class ManufacturingDbContext
{
    internal const string PlanPriorityIdentityCheck = "[CompanyId] <> '00000000-0000-0000-0000-000000000000' AND [PlanId] <> '00000000-0000-0000-0000-000000000000' AND [Revision] > 0";
    internal const string PlanPriorityTextCheck = "LEN(LTRIM(RTRIM([RecordedBy]))) > 0 AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND LEN(LTRIM(RTRIM([CorrelationId]))) > 0";
    internal DbSet<PlanPriorityVersionRow> PlanPriorityVersions => Set<PlanPriorityVersionRow>();

    private void ConfigurePlanPriorityVersions(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PlanPriorityVersionRow>();
        entity.ToTable("PlanPriorityVersions", Schema, table =>
        {
            table.HasCheckConstraint("CK_PlanPriorityVersions_Identity", PlanPriorityIdentityCheck);
            table.HasCheckConstraint("CK_PlanPriorityVersions_Text", PlanPriorityTextCheck);
        });
        entity.HasKey(row => new { row.CompanyId, row.PlanId, row.Revision });
        entity.Property(row => row.RecordedBy).HasMaxLength(128).IsRequired();
        entity.Property(row => row.Reason).HasMaxLength(ProductionPlanPriorityV1.ReasonMaximumLength).IsRequired();
        entity.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasOne<PlanRow>().WithMany().HasForeignKey(row => new { row.CompanyId, row.PlanId })
            .HasPrincipalKey(row => new { row.CompanyId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasQueryFilter(row => row.CompanyId == companyContext.CompanyId);
        // The existing IManufacturingTenantRow guard makes every version company-bound and append-only.
    }
}
