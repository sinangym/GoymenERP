using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence;

// Not connected to IdentityDbContext or a migration until the exact #104 admission.
public static class ProductMasterAssignmentModel
{
    public static void ConfigureProductMasterAssignment(this ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ProductMasterAssignment>();
        entity.ToTable("ProductMasterAssignments", "identity", table =>
        {
            table.HasCheckConstraint("CK_ProductMasterAssignments_Operation", "[Operation] IN (1, 2, 3, 4, 5, 6)");
            table.HasCheckConstraint("CK_ProductMasterAssignments_Identities",
                "[Id] <> '00000000-0000-0000-0000-000000000000' AND [UserId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [RoleId] <> '00000000-0000-0000-0000-000000000000' AND [DecisionId] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_ProductMasterAssignments_Validity",
                "DATEPART(TZOFFSET, [ValidFromUtc]) = 0 AND ([ValidUntilUtc] IS NULL OR "
                + "([ValidUntilUtc] > [ValidFromUtc] AND DATEPART(TZOFFSET, [ValidUntilUtc]) = 0))");
            table.HasCheckConstraint("CK_ProductMasterAssignments_Revocation",
                "([IsActive] = 1 AND [RevocationDecisionId] IS NULL AND [RevocationDecisionReference] IS NULL AND [RevokedAtUtc] IS NULL) OR "
                + "([IsActive] = 0 AND [RevocationDecisionId] IS NOT NULL AND [RevocationDecisionId] <> [DecisionId] "
                + "AND [RevocationDecisionId] <> '00000000-0000-0000-0000-000000000000' "
                + "AND [RevocationDecisionReference] IS NOT NULL AND [RevokedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET, [RevokedAtUtc]) = 0)");
        });
        entity.HasKey(x => x.Id);
        entity.HasIndex(x => new { x.UserId, x.Operation }).IsUnique();
        entity.HasIndex(x => x.DecisionId).IsUnique();
        entity.HasIndex(x => x.RevocationDecisionId).IsUnique().HasFilter("[RevocationDecisionId] IS NOT NULL");
        entity.Property(x => x.DecisionReference).HasMaxLength(500).UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.RevocationDecisionReference).HasMaxLength(500).UseCollation("Latin1_General_100_BIN2");
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.IssuerUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<RoleDefinition>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ExternalIdentityLink>().WithMany().HasForeignKey(x => x.IssuerExternalIdentityId).OnDelete(DeleteBehavior.Restrict);
    }
}
