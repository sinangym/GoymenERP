using Microsoft.EntityFrameworkCore;
namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingDbContext
{
    internal DbSet<CreditMovementProposalRow> CreditMovementProposals => Set<CreditMovementProposalRow>();
    private void ConfigureCreditMovementProposals(ModelBuilder model)
    {
        var row = model.Entity<CreditMovementProposalRow>();
        row.ToTable("CreditMovementProposals", Schema, table =>
        {
            table.HasCheckConstraint("CK_CreditMovementProposals_Source", "ISJSON([SourceJson]) = 1 AND LEN([SourceHash]) = 64 AND ISJSON([WaitingReasonsJson]) = 1");
            table.HasCheckConstraint("CK_CreditMovementProposals_Mapping", "([MappingStatus] = N'MappingMissing' AND [MappingVersion] IS NULL) OR ([MappingStatus] = N'MappingDetailsPending' AND [MappingVersion] > 0)");
        });
        row.HasKey(x => new { x.CompanyId, x.Id });
        row.HasIndex(x => new { x.CompanyId, x.MovementId, x.IsReversal }).IsUnique();
        row.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength().UseCollation(IdentityCollation);
        row.Property(x => x.MappingStatus).HasMaxLength(32);
        row.HasQueryFilter(x => x.CompanyId == companyContext.CompanyId);
        row.HasOne<OrganizationCompanyReference>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}
