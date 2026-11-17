using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Bootstrap;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Outbox;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-1, ID-2 and ID-3 persistent model without a database: the local credential table, the bootstrap marker, the
/// outbox, the credential command journal, the read-only company reference, the access grant workflow, their keys and
/// the forward-fix Down of their migrations. What SQL Server enforces is proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class IdentityPersistenceModelTests
{
    [Fact]
    public void The_identity_migrations_include_Audit_ownership_and_company_setup_and_the_snapshot_matches_the_model()
    {
        using var context = Context();

        Assert.Equal(
            ["20261114164716_AddLocalCredentials", "20261115144305_AddIdentityBootstrapState", "20261115170730_AddAccessGrantRequestWorkflow",
                "20261116164010_RemoveIdentityAuditTableMapping", "20261116185533_AddCompanySetupInboxAndOutboxPosition", "20261116205256_AddExplicitCompanySetupAuthority",
                "20261117004612_AddLocalTwoFactorSecrets", "20261117082918_AddProductCogsClassificationAuthority", "20261117160905_AddOperatorTwoFactorRecoveryAttempts"],
            context.Database.GetMigrations().TakeLast(9));
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Null(context.Model.FindEntityType("GoymenERP.Modules.Audit.Domain.Events.AuditEvent"));
        // Audit was excluded from Identity migrations already. The generated ownership
        // snapshot update neither creates nor drops the Audit owner's table or data.
        var ownership = new RemoveIdentityAuditTableMapping();
        Assert.Empty(ownership.UpOperations);
        Assert.Empty(ownership.DownOperations);
    }

    [Fact]
    public void The_local_credential_is_keyed_by_the_user_and_unique_by_the_binary_normalized_user_name()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LocalCredential))!;

        Assert.Equal(("identity", "LocalCredentials"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal([nameof(LocalCredential.UserId)], entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(UserAccount), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsUnique);

        var unique = entity.GetIndexes().Single(index => index.GetDatabaseName() == "UX_LocalCredentials_NormalizedUserName");
        Assert.True(unique.IsUnique);
        Assert.Null(unique.GetFilter());
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty(nameof(LocalCredential.NormalizedUserName))!.GetCollation());
        Assert.Equal((LocalUserName.MaxLength, LocalUserName.MaxLength, 512),
            (entity.FindProperty(nameof(LocalCredential.UserName))!.GetMaxLength(),
                entity.FindProperty(nameof(LocalCredential.NormalizedUserName))!.GetMaxLength(),
                entity.FindProperty(nameof(LocalCredential.PasswordHash))!.GetMaxLength()));
        Assert.True(entity.FindProperty(nameof(LocalCredential.Version))!.IsConcurrencyToken);
        Assert.False(entity.FindProperty(nameof(LocalCredential.FailedAccessCount))!.IsConcurrencyToken);
        Assert.Equal(
            [nameof(LocalCredential.LockoutEndUtc), nameof(LocalCredential.TemporaryPasswordConsumedAtUtc),
                nameof(LocalCredential.TemporaryPasswordExpiresAtUtc), nameof(LocalCredential.TemporaryPasswordIssuedAtUtc)],
            entity.GetProperties().Where(property => property.IsNullable).Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        var check = Assert.Single(entity.GetCheckConstraints());
        Assert.Equal(("CK_LocalCredentials_Counters", IdentityDbContext.LocalCredentialCountersCheck), (check.Name, check.Sql));
    }

    [Fact]
    public void The_local_credential_migration_is_pure_DDL_up_and_refuses_its_Down_first_while_a_credential_exists()
    {
        var migration = new AddLocalCredentials();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var create = Assert.IsType<CreateTableOperation>(migration.UpOperations[0]);
        Assert.Equal(("identity", "LocalCredentials"), (create.Schema, create.Name));

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM [identity].[LocalCredentials])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51280", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        var drop = Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations.Skip(1)));
        Assert.Equal(("identity", "LocalCredentials"), (drop.Schema, drop.Name));
    }

    [Fact]
    public void The_bootstrap_marker_is_a_single_row_keyed_by_a_fixed_id_pointing_at_the_founder()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IdentityBootstrapState))!;

        Assert.Equal(("identity", "IdentityBootstrapState"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal([nameof(IdentityBootstrapState.Id)], entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(ValueGenerated.Never, entity.FindProperty(nameof(IdentityBootstrapState.Id))!.ValueGenerated);
        var check = Assert.Single(entity.GetCheckConstraints());
        Assert.Equal(("CK_IdentityBootstrapState_Singleton", IdentityDbContext.BootstrapSingletonCheck), (check.Name, check.Sql));
        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(UserAccount), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal([nameof(IdentityBootstrapState.FounderUserId)], foreignKey.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal([nameof(LocalCredentialCommand.OriginalCorrelationId)], entity.GetProperties().Where(property => property.IsNullable).Select(property => property.Name));
        Assert.Equal(128, entity.FindProperty(nameof(LocalCredentialCommand.OriginalCorrelationId))!.GetMaxLength());
    }

    [Fact]
    public void The_identity_outbox_checks_its_identity_and_json_payload_and_is_ordered_by_recording()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IdentityOutboxMessage))!;

        Assert.Equal(("identity", "OutboxMessages"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal(ValueGenerated.Never, entity.FindProperty(nameof(IdentityOutboxMessage.Id))!.ValueGenerated);
        Assert.Equal(
            [("CK_IdentityOutbox_Identity", IdentityDbContext.OutboxIdentityCheck), ("CK_IdentityOutbox_Payload", "ISJSON([PayloadJson]) = 1")],
            entity.GetCheckConstraints().Select(check => (check.Name!, check.Sql)).OrderBy(pair => pair.Item1, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            [nameof(IdentityOutboxMessage.CausationId), nameof(IdentityOutboxMessage.CompanyId)],
            entity.GetProperties().Where(property => property.IsNullable).Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty(nameof(IdentityOutboxMessage.EventType))!.GetCollation());
        Assert.Equal("nvarchar(max)", entity.FindProperty(nameof(IdentityOutboxMessage.PayloadJson))!.GetColumnType());
        Assert.Equal(
            ["IX_IdentityOutbox_Recorded", "IX_OutboxMessages_CompanyId_Position"],
            entity.GetIndexes().Select(index => index.GetDatabaseName()!).Order(StringComparer.Ordinal).ToArray());
        var recorded = Assert.Single(entity.GetIndexes(), index => index.GetDatabaseName() == "IX_IdentityOutbox_Recorded");
        Assert.Equal("IX_IdentityOutbox_Recorded", recorded.GetDatabaseName());
        Assert.False(recorded.IsUnique);
        Assert.Equal(
            [nameof(IdentityOutboxMessage.RecordedAtUtc), nameof(IdentityOutboxMessage.Id)],
            recorded.Properties.Select(property => property.Name).ToArray());
        var cursor = Assert.Single(entity.GetIndexes(), index => index.GetDatabaseName() == "IX_OutboxMessages_CompanyId_Position");
        Assert.False(cursor.IsUnique);
        Assert.Null(cursor.GetFilter());
        Assert.Equal(
            [nameof(IdentityOutboxMessage.CompanyId), nameof(IdentityOutboxMessage.Position)],
            cursor.Properties.Select(property => property.Name).ToArray());
        var position = entity.FindProperty(nameof(IdentityOutboxMessage.Position))!;
        Assert.Equal(typeof(ulong), position.ClrType);
        Assert.False(position.IsNullable);
        Assert.True(position.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, position.ValueGenerated);
        Assert.Equal("rowversion", position.GetColumnType());
        Assert.Empty(entity.GetForeignKeys());
    }

    [Fact]
    public void The_credential_command_journal_is_unique_per_user_operation_and_binary_idempotency_key()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LocalCredentialCommand))!;

        Assert.Equal(("identity", "LocalCredentialCommands"), (entity.GetSchema(), entity.GetTableName()));
        var check = Assert.Single(entity.GetCheckConstraints());
        Assert.Equal(("CK_LocalCredentialCommands_Shape", IdentityDbContext.CredentialCommandCheck), (check.Name, check.Sql));
        var unique = Assert.Single(entity.GetIndexes(), index => index.IsUnique);
        Assert.Equal("UX_LocalCredentialCommands_UserOperationKey", unique.GetDatabaseName());
        Assert.Equal(
            [nameof(LocalCredentialCommand.UserId), nameof(LocalCredentialCommand.Operation), nameof(LocalCredentialCommand.IdempotencyKey)],
            unique.Properties.Select(property => property.Name).ToArray());
        Assert.Null(unique.GetFilter());
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty(nameof(LocalCredentialCommand.IdempotencyKey))!.GetCollation());
        Assert.Equal("Latin1_General_100_BIN2", entity.FindProperty(nameof(LocalCredentialCommand.Operation))!.GetCollation());
        var fingerprint = entity.FindProperty(nameof(LocalCredentialCommand.RequestFingerprint))!;
        Assert.Equal(("binary(32)", true), (fingerprint.GetColumnType(), fingerprint.IsFixedLength()));
        var foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(LocalCredential), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal([nameof(LocalCredentialCommand.OriginalCorrelationId)], entity.GetProperties().Where(property => property.IsNullable).Select(property => property.Name));
        Assert.Equal(128, entity.FindProperty(nameof(LocalCredentialCommand.OriginalCorrelationId))!.GetMaxLength());
    }

    [Fact]
    public void Optional_two_factor_secrets_are_protected_unique_per_credential_and_have_forward_fix_Down()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LocalTwoFactorSecret))!;
        Assert.Equal(("identity", "LocalTwoFactorSecrets"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal([nameof(LocalTwoFactorSecret.UserId)], entity.FindPrimaryKey()!.Properties.Select(row => row.Name));
        var foreign = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(LocalCredential), foreign.PrincipalEntityType.ClrType);
        Assert.True(foreign.IsUnique); Assert.Equal(DeleteBehavior.Restrict, foreign.DeleteBehavior);
        Assert.Equal(4000, entity.FindProperty(nameof(LocalTwoFactorSecret.ProtectedSecret))!.GetMaxLength());
        Assert.Equal("nvarchar(max)", entity.FindProperty(nameof(LocalTwoFactorSecret.RecoveryHashesJson))!.GetColumnType());
        Assert.True(entity.FindProperty(nameof(LocalTwoFactorSecret.Version))!.IsConcurrencyToken);
        Assert.Equal(IdentityDbContext.TwoFactorStateCheck, Assert.Single(entity.GetCheckConstraints()).Sql);
        Assert.Null(entity.FindProperty(nameof(LocalTwoFactorSecret.RecoveryCodeCount)));
        var migration = new AddLocalTwoFactorSecrets();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51967", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[LocalTwoFactorSecrets]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[TwoFactorEnabled] = 1", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[OriginalCorrelationId] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public void The_company_reference_reads_the_organization_table_and_is_excluded_from_identity_migrations()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IdentityCompanyReference))!;

        Assert.Equal(("organization", "Companies"), (entity.GetSchema(), entity.GetTableName()));
        Assert.True(entity.IsTableExcludedFromMigrations());
    }

    [Fact]
    public void The_bootstrap_migration_is_pure_DDL_up_and_refuses_its_Down_first_while_identity_rows_exist()
    {
        var migration = new AddIdentityBootstrapState();

        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(
            ["IdentityBootstrapState", "LocalCredentialCommands", "OutboxMessages"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(create => create.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(migration.UpOperations.OfType<CreateTableOperation>(), create => Assert.Equal("identity", create.Schema));
        Assert.DoesNotContain(migration.UpOperations, operation => operation is CreateTableOperation { Schema: "organization" });
        Assert.DoesNotContain(migration.DownOperations, operation => operation is DropTableOperation { Schema: "organization" });

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("EXISTS (SELECT 1 FROM [identity].[IdentityBootstrapState])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("EXISTS (SELECT 1 FROM [identity].[OutboxMessages])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("EXISTS (SELECT 1 FROM [identity].[LocalCredentialCommands])", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("THROW 51399", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(
            ["IdentityBootstrapState", "LocalCredentialCommands", "OutboxMessages"],
            migration.DownOperations.OfType<DropTableOperation>().Select(drop => drop.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void The_access_grant_request_carries_the_ID_3_checks_one_pending_request_and_one_resulting_assignment()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(AccessGrantRequest))!;

        Assert.Equal(("identity", "AccessGrantRequests"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal(
            [
                ("CK_AccessGrantRequests_ApprovalMode", IdentityDbContext.AccessGrantApprovalModeCheck),
                ("CK_AccessGrantRequests_DecisionReason", IdentityDbContext.AccessGrantDecisionReasonCheck),
                ("CK_AccessGrantRequests_DecisionState", IdentityDbContext.AccessGrantDecisionStateCheck),
                ("CK_AccessGrantRequests_ResultingAssignment", IdentityDbContext.AccessGrantResultingAssignmentCheck),
                ("CK_AccessGrantRequests_SecondApproval", IdentityDbContext.AccessGrantSecondApprovalCheck),
                ("CK_AccessGrantRequests_Validity", IdentityDbContext.AccessGrantValidityCheck),
            ],
            entity.GetCheckConstraints().Select(check => (check.Name!, check.Sql)).OrderBy(pair => pair.Item1, StringComparer.Ordinal).ToArray());

        var pending = entity.GetIndexes().Single(index => index.GetDatabaseName() == "UX_AccessGrantRequests_Pending");
        Assert.True(pending.IsUnique);
        Assert.Equal(IdentityDbContext.AccessGrantPendingFilter, pending.GetFilter());
        Assert.Equal(
            [nameof(AccessGrantRequest.CompanyId), nameof(AccessGrantRequest.TargetUserId), nameof(AccessGrantRequest.RoleId)],
            pending.Properties.Select(property => property.Name).ToArray());
        var resulting = entity.GetIndexes().Single(index => index.GetDatabaseName() == "UX_AccessGrantRequests_ResultingRoleAssignment");
        Assert.True(resulting.IsUnique);
        Assert.Equal("[ResultingRoleAssignmentId] IS NOT NULL", resulting.GetFilter());

        var toAssignment = Assert.Single(entity.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(RoleAssignment));
        Assert.Equal(
            [nameof(AccessGrantRequest.CompanyId), nameof(AccessGrantRequest.ResultingRoleAssignmentId)],
            toAssignment.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(
            [nameof(RoleAssignment.CompanyId), nameof(RoleAssignment.Id)],
            toAssignment.PrincipalKey.Properties.Select(property => property.Name).ToArray());
        Assert.Equal(DeleteBehavior.Restrict, toAssignment.DeleteBehavior);

        Assert.True(entity.FindProperty(nameof(AccessGrantRequest.Version))!.IsConcurrencyToken);
        Assert.Equal(AccessGrantRequest.ReasonMaxLength, entity.FindProperty(nameof(AccessGrantRequest.Reason))!.GetMaxLength());
        var reasonCode = entity.FindProperty(nameof(AccessGrantRequest.DecisionReasonCode))!;
        Assert.Equal((AccessGrantRequest.DecisionReasonCodeMaxLength, "Latin1_General_100_BIN2"),
            (reasonCode.GetMaxLength(), reasonCode.GetCollation()));
    }

    [Fact]
    public void The_role_assignment_revocation_names_who_and_when_and_points_at_a_user()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RoleAssignment))!;

        var check = Assert.Single(entity.GetCheckConstraints(), check => check.Name == "CK_RoleAssignments_Revocation");
        Assert.Equal(IdentityDbContext.RoleAssignmentRevocationCheck, check.Sql);
        var revokedBy = Assert.Single(entity.GetForeignKeys(),
            key => key.Properties.Select(property => property.Name).SequenceEqual([nameof(RoleAssignment.RevokedByUserId)]));
        Assert.Equal(typeof(UserAccount), revokedBy.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, revokedBy.DeleteBehavior);
        Assert.True(entity.FindProperty(nameof(RoleAssignment.RevokedByUserId))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(RoleAssignment.RevokedAtUtc))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(RoleAssignment.Version))!.IsConcurrencyToken);
    }

    [Fact]
    public void The_access_grant_workflow_migration_backfills_old_approvals_before_its_checks_and_refuses_its_Down_first()
    {
        var migration = new AddAccessGrantRequestWorkflow();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is CreateTableOperation or DropTableOperation);
        var backfill = Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("SET [ApprovalMode] = N'SecondApproval'", backfill.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Status] = N'Approved' AND [ApprovalMode] IS NULL", backfill.Sql, StringComparison.Ordinal);
        var up = migration.UpOperations.ToList();
        var addApprovalMode = up.FindIndex(operation => operation is AddColumnOperation { Name: "ApprovalMode" });
        var backfillAt = up.IndexOf(backfill);
        var approvalModeCheck = up.FindIndex(
            operation => operation is AddCheckConstraintOperation { Name: "CK_AccessGrantRequests_ApprovalMode" });
        Assert.InRange(addApprovalMode, 0, backfillAt - 1);
        Assert.True(backfillAt < approvalModeCheck);
        Assert.All(migration.UpOperations.Concat(migration.DownOperations).OfType<ITableMigrationOperation>(),
            operation => Assert.Equal("identity", operation.Schema));

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51484", guard.Sql, StringComparison.Ordinal);
        foreach (var lost in new[]
                 {
                     "[RevokedByUserId] IS NOT NULL", "[RevokedAtUtc] IS NOT NULL", "[Status] = N'Withdrawn'",
                     "[ApprovalMode] = N'FounderDirect'", "[DecisionReasonCode] IS NOT NULL", "[Reason] IS NOT NULL",
                     "[ResultingRoleAssignmentId] IS NOT NULL", "[ValidFromUtc] IS NOT NULL", "[ValidUntilUtc] IS NOT NULL",
                 })
        {
            Assert.Contains(lost, guard.Sql, StringComparison.Ordinal);
        }

        // A second-person approval is reconstructible by the Up backfill, so it never blocks Down.
        Assert.DoesNotContain("N'SecondApproval'", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Equal(
            ["ApprovalMode", "DecisionReasonCode", "Reason", "ResultingRoleAssignmentId", "RevokedAtUtc", "RevokedByUserId",
                "ValidFromUtc", "ValidUntilUtc"],
            migration.DownOperations.OfType<DropColumnOperation>().Select(drop => drop.Name).Order(StringComparer.Ordinal).ToArray());
    }

    private static IdentityDbContext Context() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options);
}
