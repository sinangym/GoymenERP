using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KIM-D34: the persistent group-wide login attempt limit table — the version number is the key (one record for the group, no
/// company in the key and no company query filter), one row per profile, the checks equal to the published partition names and
/// bounds, a pure DDL Up and a forward-fix Down that refuses first (THROW 51331) while any version exists.
/// </summary>
public sealed class SettingsLoginRateLimitPersistenceModelTests
{
    [Fact]
    public void A_login_rate_limit_version_is_keyed_by_version_alone_with_one_row_per_profile_and_its_checks()
    {
        using var context = CreateDbContext();
        var limit = Entity<LoginRateLimitVersionRow>(context);

        Assert.Equal(("settings", "LoginRateLimitVersions"), (limit.GetSchema(), limit.GetTableName()));
        Assert.Equal(["Version"], limit.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var profile = Assert.Single(limit.GetIndexes());
        Assert.True(profile.IsUnique);
        Assert.Equal(["ProfileId"], profile.Properties.Select(property => property.Name));
        var checks = Checks(limit);
        Assert.Equal(SettingsDbContext.LoginRateLimitPartitionCheck, checks["CK_SettingsLoginRateLimitVersions_Partition"]);
        Assert.Equal(SettingsDbContext.LoginRateLimitBoundsCheck, checks["CK_SettingsLoginRateLimitVersions_Bounds"]);
        Assert.Contains("[RecordedInCompanyId] <>", checks["CK_SettingsLoginRateLimitVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[Reason]", checks["CK_SettingsLoginRateLimitVersions_Recorded"], StringComparison.Ordinal);
        Assert.Equal(LoginRateLimitInputLimitsV1.ReasonMaximumLength, limit.FindProperty("Reason")!.GetMaxLength());
        Assert.Null(limit.FindProperty("CompanyId"));
        Assert.Empty(limit.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_checks_hold_exactly_the_published_partition_names_and_bounds()
    {
        Assert.All(LoginRateLimitPartitionsV1.All,
            code => Assert.Contains($"'{code}'", SettingsDbContext.LoginRateLimitPartitionCheck, StringComparison.Ordinal));
        Assert.Equal(LoginRateLimitPartitionsV1.All.Count,
            SettingsDbContext.LoginRateLimitPartitionCheck.Count(character => character == ',') + 1);
        Assert.Contains(
            $"[PermitLimit] BETWEEN {Invariant(LoginRateLimitInputLimitsV1.MinimumPermitLimit)} AND {Invariant(LoginRateLimitInputLimitsV1.MaximumPermitLimit)}",
            SettingsDbContext.LoginRateLimitBoundsCheck, StringComparison.Ordinal);
        Assert.Contains(
            $"[WindowSeconds] BETWEEN {Invariant(LoginRateLimitInputLimitsV1.MinimumWindowSeconds)} AND {Invariant(LoginRateLimitInputLimitsV1.MaximumWindowSeconds)}",
            SettingsDbContext.LoginRateLimitBoundsCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_login_rate_limit_version_exists()
    {
        var migration = new AddLoginRateLimitVersions();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var created = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(("settings", "LoginRateLimitVersions"), (created.Schema, created.Name));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51331", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[LoginRateLimitVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<DropTableOperation>());
    }

    [Fact]
    public void The_trusted_proxy_list_is_a_required_bounded_ascii_column_added_by_a_pure_DDL_migration_with_a_forward_fix_Down()
    {
        using var context = CreateDbContext();
        var proxies = Entity<LoginRateLimitVersionRow>(context).FindProperty(nameof(LoginRateLimitVersionRow.TrustedProxies));

        Assert.NotNull(proxies);
        Assert.False(proxies.IsNullable);
        Assert.False(proxies.IsUnicode());
        Assert.Equal(LoginRateLimitInputLimitsV1.TrustedProxiesStorageMaximumLength, proxies.GetMaxLength());

        var migration = new AddLoginRateLimitTrustedProxies();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var added = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal(("settings", "LoginRateLimitVersions", "TrustedProxies"), (added.Schema, added.Table, added.Name));
        Assert.Equal(string.Empty, added.DefaultValue);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51734", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[TrustedProxies] <> ''", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<DropColumnOperation>());
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static IEntityType Entity<T>(SettingsDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped.");

    private static Dictionary<string, string> Checks(IEntityType entity) =>
        entity.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);

    private static SettingsDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True")
            .Options, new CompanyContext());
}
