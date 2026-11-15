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
/// ID-1 and ID-2 persistent model without a database: the local credential table, the bootstrap marker, the outbox,
/// the credential command journal, the read-only company reference, their keys and the forward-fix Down of their
/// migrations. What SQL Server enforces is proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class IdentityPersistenceModelTests
{
    [Fact]
    public void The_ID_2_migration_is_the_last_one_and_the_snapshot_matches_the_model()
    {
        using var context = Context();

        Assert.Equal(
            ["20260928120000_AllowProductLifecycleAssignments", "20261114164716_AddLocalCredentials", "20261115144305_AddIdentityBootstrapState"],
            context.Database.GetMigrations().TakeLast(3));
        Assert.False(context.Database.HasPendingModelChanges());
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
        Assert.DoesNotContain(entity.GetProperties(), property => property.IsNullable);
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
        var recorded = Assert.Single(entity.GetIndexes());
        Assert.Equal("IX_IdentityOutbox_Recorded", recorded.GetDatabaseName());
        Assert.False(recorded.IsUnique);
        Assert.Equal(
            [nameof(IdentityOutboxMessage.RecordedAtUtc), nameof(IdentityOutboxMessage.Id)],
            recorded.Properties.Select(property => property.Name).ToArray());
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
        Assert.DoesNotContain(entity.GetProperties(), property => property.IsNullable);
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

    private static IdentityDbContext Context() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options);
}
