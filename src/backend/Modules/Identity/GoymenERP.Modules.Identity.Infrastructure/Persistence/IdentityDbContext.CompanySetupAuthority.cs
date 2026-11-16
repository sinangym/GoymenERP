using GoymenERP.Modules.Identity.Domain.CompanySetup;
using GoymenERP.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence;

public sealed partial class IdentityDbContext
{
    internal const string CompanySetupGroupOperationCheck = "[Operation] IN (1, 2)";
    internal const string CompanySetupGroupIdentityCheck = "[Id] <> '00000000-0000-0000-0000-000000000000' AND [UserId] <> '00000000-0000-0000-0000-000000000000' AND [DecisionId] <> '00000000-0000-0000-0000-000000000000' AND LEN([DecisionReference]) > 0";
    internal const string CompanySetupGroupValidityCheck = "DATEPART(TZOFFSET, [ValidFromUtc]) = 0 AND ([ValidUntilUtc] IS NULL OR ([ValidUntilUtc] > [ValidFromUtc] AND DATEPART(TZOFFSET, [ValidUntilUtc]) = 0))";
    internal const string CompanySetupGroupRevocationCheck = "([IsActive] = 1 AND [RevocationDecisionId] IS NULL AND [RevocationDecisionReference] IS NULL AND [RevokedAtUtc] IS NULL) OR ([IsActive] = 0 AND [RevocationDecisionId] IS NOT NULL AND [RevocationDecisionId] <> [DecisionId] AND [RevocationDecisionId] <> '00000000-0000-0000-0000-000000000000' AND [RevocationDecisionReference] IS NOT NULL AND LEN([RevocationDecisionReference]) > 0 AND [RevokedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET, [RevokedAtUtc]) = 0)";
    public DbSet<CompanySetupGroupAssignment> CompanySetupGroupAssignments => Set<CompanySetupGroupAssignment>();
    private static void ConfigureCompanySetupAuthority(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CompanySetupGroupAssignment>();
        entity.ToTable("CompanySetupGroupAssignments", "identity", table =>
        {
            table.HasCheckConstraint("CK_CompanySetupGroup_Operation", CompanySetupGroupOperationCheck);
            table.HasCheckConstraint("CK_CompanySetupGroup_Identity", CompanySetupGroupIdentityCheck);
            table.HasCheckConstraint("CK_CompanySetupGroup_Validity", CompanySetupGroupValidityCheck);
            table.HasCheckConstraint("CK_CompanySetupGroup_Revocation", CompanySetupGroupRevocationCheck);
        });
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.UserId, x.Operation }).IsUnique().HasFilter("[IsActive] = 1");
        entity.HasIndex(x => x.DecisionId).IsUnique();
        entity.Property(x => x.DecisionReference).HasMaxLength(500).UseCollation(BinaryCollation).IsRequired();
        entity.Property(x => x.RevocationDecisionReference).HasMaxLength(500).UseCollation(BinaryCollation);
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
    private void ApplyCompanySetupAuthorityWriteGuards()
    {
        foreach (var entry in ChangeTracker.Entries<CompanySetupGroupAssignment>())
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Group authority history cannot be deleted.");
            if (entry.State != EntityState.Modified) continue;
            if (!entry.Property(x => x.IsActive).OriginalValue || entry.Entity.IsActive
                || entry.Properties.Any(property => property.IsModified && property.Metadata.Name is not
                    (nameof(CompanySetupGroupAssignment.IsActive) or nameof(CompanySetupGroupAssignment.RevocationDecisionId)
                    or nameof(CompanySetupGroupAssignment.RevocationDecisionReference) or nameof(CompanySetupGroupAssignment.RevokedAtUtc))))
                throw new InvalidOperationException("A group authority is immutable except one explicit revocation.");
        }
    }
}
