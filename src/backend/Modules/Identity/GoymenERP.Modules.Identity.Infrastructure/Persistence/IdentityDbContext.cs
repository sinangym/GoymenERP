using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<ExternalIdentityLink> ExternalIdentities => Set<ExternalIdentityLink>();

    public DbSet<RoleDefinition> Roles => Set<RoleDefinition>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    public DbSet<RoleAssignmentScope> RoleAssignmentScopes => Set<RoleAssignmentScope>();

    public DbSet<AccessGrantRequest> AccessGrantRequests => Set<AccessGrantRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureExternalIdentity(modelBuilder);
        ConfigureRole(modelBuilder);
        ConfigureRolePermission(modelBuilder);
        ConfigureRoleAssignment(modelBuilder);
        ConfigureRoleAssignmentScope(modelBuilder);
        ConfigureAccessGrantRequest(modelBuilder);
        modelBuilder.ConfigureProductMasterAssignment();
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserAccount>();
        entity.ToTable("Users", "identity");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Email).HasMaxLength(320);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => x.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL");
    }

    private static void ConfigureExternalIdentity(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ExternalIdentityLink>();
        entity.ToTable("ExternalIdentities", "identity");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Issuer)
            .HasMaxLength(500)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        entity.Property(x => x.Subject)
            .HasMaxLength(500)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        entity.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureRole(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RoleDefinition>();
        entity.ToTable("Roles", "identity");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => x.Code).IsUnique();
        entity.HasMany(x => x.Permissions)
            .WithOne()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureRolePermission(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RolePermission>();
        entity.ToTable("RolePermissions", "identity");
        entity.HasKey(x => new { x.RoleId, x.PermissionCode });
        entity.Property(x => x.PermissionCode).HasMaxLength(160).IsRequired();
        entity.HasIndex(x => x.PermissionCode);
    }

    private static void ConfigureRoleAssignment(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RoleAssignment>();
        entity.ToTable(
            "RoleAssignments",
            "identity",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_RoleAssignments_CompanyId_NotEmpty",
                    "[CompanyId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint(
                    "CK_RoleAssignments_Validity",
                    "[ValidUntilUtc] IS NULL OR [ValidUntilUtc] > [ValidFromUtc]");
            });
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.CompanyId, x.Id });
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => new { x.UserId, x.CompanyId, x.Status });
        entity.HasIndex(x => new { x.RoleId, x.CompanyId });
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<RoleDefinition>()
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasMany(x => x.Scopes)
            .WithOne()
            .HasForeignKey(x => new { x.CompanyId, x.RoleAssignmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(x => x.Scopes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureRoleAssignmentScope(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RoleAssignmentScope>();
        entity.ToTable(
            "RoleAssignmentScopes",
            "identity",
            table => table.HasCheckConstraint(
                "CK_RoleAssignmentScopes_CompanyScope",
                "[ScopeType] <> N'Company' OR [ScopeId] = [CompanyId]"));
        entity.HasKey(x => new { x.RoleAssignmentId, x.ScopeType, x.ScopeId });
        entity.Property(x => x.ScopeType).HasConversion<string>().HasMaxLength(32);
        entity.HasIndex(x => new { x.ScopeType, x.ScopeId });
    }

    private static void ConfigureAccessGrantRequest(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccessGrantRequest>();
        entity.ToTable(
            "AccessGrantRequests",
            "identity",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_AccessGrantRequests_SecondApproval",
                    "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR "
                    + "([DecidedByUserId] <> [RequestedByUserId] AND "
                    + "[DecidedByUserId] <> [TargetUserId])");
                table.HasCheckConstraint(
                    "CK_AccessGrantRequests_DecisionState",
                    "([Status] = N'Pending' AND [DecidedByUserId] IS NULL "
                    + "AND [DecidedAtUtc] IS NULL) OR "
                    + "([Status] IN (N'Approved', N'Rejected') "
                    + "AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.RequestedAtUtc });
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<RoleDefinition>()
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
