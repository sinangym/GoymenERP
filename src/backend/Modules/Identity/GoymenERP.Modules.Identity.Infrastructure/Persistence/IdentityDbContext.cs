using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Idempotency.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Bootstrap;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Outbox;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Identity.Infrastructure.Persistence;

public sealed partial class IdentityDbContext(DbContextOptions<IdentityDbContext> options,
    GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext? companyContext = null)
    : DbContext(options)
{
    /// <summary>ID-1: login counters are never negative and every stored hash names its format version.</summary>
    internal const string LocalCredentialCountersCheck = "[FailedAccessCount] >= 0 AND [HashFormatVersion] >= 1";

    internal const string BinaryCollation = "Latin1_General_100_BIN2";

    /// <summary>ID-2: the bootstrap marker has exactly one row; a second bootstrap fails on the key (spec "Veri modeli").</summary>
    internal const string BootstrapSingletonCheck = "[Id] = 1";

    internal const string OutboxIdentityCheck =
        "[Id] <> '00000000-0000-0000-0000-000000000000' AND [AggregateId] <> '00000000-0000-0000-0000-000000000000' "
        + "AND ([CompanyId] IS NULL OR [CompanyId] <> '00000000-0000-0000-0000-000000000000') "
        + "AND [SchemaVersion] >= 1 AND [AggregateVersion] >= 1 AND LEN([EventType]) > 0 AND LEN([CorrelationId]) > 0";

    internal const string CredentialCommandCheck =
        "[Id] <> '00000000-0000-0000-0000-000000000000' AND [ResultVersion] >= 1 "
        + "AND DATALENGTH([RequestFingerprint]) = 32 AND LEN([IdempotencyKey]) > 0";

    /// <summary>
    /// ID-3 (KIM-D1, KIM-D5): a Critical request is decided by a person who is neither the requester nor the target,
    /// except the requester's own withdrawal and the active founder's direct assignment (path a).
    /// </summary>
    internal const string AccessGrantSecondApprovalCheck =
        "[RequiresSecondApproval] = 0 OR [DecidedByUserId] IS NULL OR "
        + "([Status] = N'Withdrawn' AND [DecidedByUserId] = [RequestedByUserId]) OR "
        + "([Status] = N'Approved' AND [ApprovalMode] IN (N'FounderDirect', N'CompanyOpener') AND [DecidedByUserId] = [RequestedByUserId]) OR "
        + "([DecidedByUserId] <> [RequestedByUserId] AND [DecidedByUserId] <> [TargetUserId])";

    /// <summary>ID-3: a pending request has no decision; every decided state (Withdrawn included) names who and when.</summary>
    internal const string AccessGrantDecisionStateCheck =
        "([Status] = N'Pending' AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR "
        + "([Status] IN (N'Approved', N'Rejected', N'Withdrawn') "
        + "AND [DecidedByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)";

    /// <summary>ID-3: the approval mode is recorded exactly on an approved request.</summary>
    internal const string AccessGrantApprovalModeCheck =
        "([Status] = N'Approved' AND [ApprovalMode] IN (N'SecondApproval', N'FounderDirect', N'CompanyOpener')) OR "
        + "([Status] <> N'Approved' AND [ApprovalMode] IS NULL)";

    /// <summary>ID-3: a reject carries its reason code; no other state does.</summary>
    internal const string AccessGrantDecisionReasonCheck =
        "[DecisionReasonCode] IS NULL OR [Status] = N'Rejected'";

    /// <summary>ID-3: only an approval names the assignment it created.</summary>
    internal const string AccessGrantResultingAssignmentCheck =
        "[ResultingRoleAssignmentId] IS NULL OR [Status] = N'Approved'";

    /// <summary>ID-3: an end needs a start, and ends after it.</summary>
    internal const string AccessGrantValidityCheck =
        "[ValidUntilUtc] IS NULL OR ([ValidFromUtc] IS NOT NULL AND [ValidUntilUtc] > [ValidFromUtc])";

    /// <summary>ID-3: at most one pending request per target user and role in a company.</summary>
    internal const string AccessGrantPendingFilter = "[Status] = N'Pending'";

    /// <summary>ID-3: a revocation names who and when; only a revoked assignment carries it.</summary>
    internal const string RoleAssignmentRevocationCheck =
        "([RevokedByUserId] IS NULL AND [RevokedAtUtc] IS NULL) OR "
        + "([Status] = N'Revoked' AND [RevokedByUserId] IS NOT NULL AND [RevokedAtUtc] IS NOT NULL)";

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<ExternalIdentityLink> ExternalIdentities => Set<ExternalIdentityLink>();

    public DbSet<LocalCredential> LocalCredentials => Set<LocalCredential>();

    public DbSet<RoleDefinition> Roles => Set<RoleDefinition>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    public DbSet<RoleAssignmentScope> RoleAssignmentScopes => Set<RoleAssignmentScope>();

    public DbSet<AccessGrantRequest> AccessGrantRequests => Set<AccessGrantRequest>();

    public DbSet<IdentityBootstrapState> BootstrapStates => Set<IdentityBootstrapState>();

    public DbSet<IdentityOutboxMessage> OutboxMessages => Set<IdentityOutboxMessage>();

    public DbSet<LocalCredentialCommand> LocalCredentialCommands => Set<LocalCredentialCommand>();

    internal DbSet<IdentityCompanyReference> Companies => Set<IdentityCompanyReference>();

    /// <summary>ID-3 commands' idempotency rows (platform table, mapped here, never migrated by this context).</summary>
    internal DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureExternalIdentity(modelBuilder);
        ConfigureLocalCredential(modelBuilder);
        ConfigureRole(modelBuilder);
        ConfigureRolePermission(modelBuilder);
        ConfigureRoleAssignment(modelBuilder);
        ConfigureRoleAssignmentScope(modelBuilder);
        ConfigureAccessGrantRequest(modelBuilder);
        ConfigureBootstrapState(modelBuilder);
        ConfigureOutbox(modelBuilder);
        ConfigureLocalCredentialCommand(modelBuilder);
        ConfigureCompanyReference(modelBuilder);
        ConfigureCompanySetup(modelBuilder);
        modelBuilder.ConfigureProductMasterAssignment();
        modelBuilder.ConfigureCommandExecution(excludeFromMigrations: true);
    }

    private static void ConfigureBootstrapState(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IdentityBootstrapState>();
        entity.ToTable(
            "IdentityBootstrapState",
            "identity",
            table => table.HasCheckConstraint("CK_IdentityBootstrapState_Singleton", BootstrapSingletonCheck));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.FounderUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IdentityOutboxMessage>();
        entity.ToTable(
            "OutboxMessages",
            "identity",
            table =>
            {
                table.HasCheckConstraint("CK_IdentityOutbox_Identity", OutboxIdentityCheck);
                table.HasCheckConstraint("CK_IdentityOutbox_Payload", "ISJSON([PayloadJson]) = 1");
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.EventType)
            .HasMaxLength(IdentityOutboxMessage.EventTypeMaxLength)
            .UseCollation(BinaryCollation)
            .IsRequired();
        entity.Property(x => x.SourceOperation)
            .HasMaxLength(IdentityOutboxMessage.SourceOperationMaxLength)
            .UseCollation(BinaryCollation)
            .IsRequired();
        entity.Property(x => x.AggregateType)
            .HasMaxLength(IdentityOutboxMessage.AggregateTypeMaxLength)
            .UseCollation(BinaryCollation)
            .IsRequired();
        entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.Position).IsRowVersion().HasConversion<byte[]>();
        entity.HasIndex(x => new { x.CompanyId, x.Position });
        entity.Property(x => x.CorrelationId).HasMaxLength(IdentityOutboxMessage.CorrelationMaxLength).IsRequired();
        entity.Property(x => x.CausationId).HasMaxLength(IdentityOutboxMessage.CorrelationMaxLength);
        entity.HasIndex(x => new { x.RecordedAtUtc, x.Id }).HasDatabaseName("IX_IdentityOutbox_Recorded");
    }

    private static void ConfigureLocalCredentialCommand(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LocalCredentialCommand>();
        entity.ToTable(
            "LocalCredentialCommands",
            "identity",
            table => table.HasCheckConstraint("CK_LocalCredentialCommands_Shape", CredentialCommandCheck));
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Operation)
            .HasMaxLength(LocalCredentialCommand.OperationMaxLength)
            .UseCollation(BinaryCollation)
            .IsRequired();
        entity.Property(x => x.IdempotencyKey)
            .HasMaxLength(LocalCredentialCommand.IdempotencyKeyMaxLength)
            .UseCollation(BinaryCollation)
            .IsRequired();
        entity.Property(x => x.RequestFingerprint)
            .HasColumnType("binary(32)")
            .HasMaxLength(LocalCredentialCommand.FingerprintLength)
            .IsFixedLength()
            .IsRequired();
        entity.HasIndex(x => new { x.UserId, x.Operation, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_LocalCredentialCommands_UserOperationKey");
        entity.HasOne<LocalCredential>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCompanyReference(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<IdentityCompanyReference>();
        entity.ToTable("Companies", "organization", table => table.ExcludeFromMigrations());
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Code).HasMaxLength(64);
        entity.Property(x => x.LegalName).HasMaxLength(250);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UserAccount>();
        entity.ToTable("Users", "identity");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.DisplayName).HasMaxLength(UserAccount.DisplayNameMaxLength).IsRequired();
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

    private static void ConfigureLocalCredential(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LocalCredential>();
        entity.ToTable(
            "LocalCredentials",
            "identity",
            table => table.HasCheckConstraint("CK_LocalCredentials_Counters", LocalCredentialCountersCheck));
        entity.HasKey(x => x.UserId);
        entity.Property(x => x.UserId).ValueGeneratedNever();
        entity.Property(x => x.UserName).HasMaxLength(LocalUserName.MaxLength).IsRequired();
        entity.Property(x => x.NormalizedUserName)
            .HasMaxLength(LocalUserName.MaxLength)
            .UseCollation(BinaryCollation)
            .IsRequired();
        entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.HasIndex(x => x.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("UX_LocalCredentials_NormalizedUserName");
        entity.HasOne<UserAccount>()
            .WithOne()
            .HasForeignKey<LocalCredential>(x => x.UserId)
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
                table.HasCheckConstraint("CK_RoleAssignments_Revocation", RoleAssignmentRevocationCheck);
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
        entity.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(x => x.RevokedByUserId)
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
                table.HasCheckConstraint("CK_AccessGrantRequests_SecondApproval", AccessGrantSecondApprovalCheck);
                table.HasCheckConstraint("CK_AccessGrantRequests_DecisionState", AccessGrantDecisionStateCheck);
                table.HasCheckConstraint("CK_AccessGrantRequests_ApprovalMode", AccessGrantApprovalModeCheck);
                table.HasCheckConstraint("CK_AccessGrantRequests_DecisionReason", AccessGrantDecisionReasonCheck);
                table.HasCheckConstraint(
                    "CK_AccessGrantRequests_ResultingAssignment",
                    AccessGrantResultingAssignmentCheck);
                table.HasCheckConstraint("CK_AccessGrantRequests_Validity", AccessGrantValidityCheck);
            });
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Version).IsConcurrencyToken();
        entity.Property(x => x.ApprovalMode).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Reason).HasMaxLength(AccessGrantRequest.ReasonMaxLength);
        entity.Property(x => x.DecisionReasonCode)
            .HasMaxLength(AccessGrantRequest.DecisionReasonCodeMaxLength)
            .UseCollation(BinaryCollation);
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.RequestedAtUtc });
        entity.HasIndex(x => new { x.CompanyId, x.TargetUserId, x.RoleId })
            .IsUnique()
            .HasFilter(AccessGrantPendingFilter)
            .HasDatabaseName("UX_AccessGrantRequests_Pending");
        entity.HasIndex(x => x.ResultingRoleAssignmentId)
            .IsUnique()
            .HasFilter("[ResultingRoleAssignmentId] IS NOT NULL")
            .HasDatabaseName("UX_AccessGrantRequests_ResultingRoleAssignment");
        entity.HasOne<RoleAssignment>()
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ResultingRoleAssignmentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
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
