using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-1 persistent model without a database: the local credential table, its keys and the forward-fix Down of
/// its migration. What SQL Server enforces is proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class IdentityPersistenceModelTests
{
    [Fact]
    public void The_ID_1_migration_is_the_last_one_and_the_snapshot_matches_the_model()
    {
        using var context = Context();

        Assert.Equal(["20260928120000_AllowProductLifecycleAssignments", "20261114164716_AddLocalCredentials"],
            context.Database.GetMigrations().TakeLast(2));
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

    private static IdentityDbContext Context() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options);
}
