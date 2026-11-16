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
/// API-98/B-14 (KIM-D49, KIM-D51): the reference list tables — an entry keyed by company and entry id with a code unique per
/// company and kind, its insert-only versions keyed by version, both under the company query filter, only the opened kinds in
/// the kind check, nothing defaulted or seeded; the migration is a pure DDL Up and a forward-fix Down that refuses first
/// (THROW 51886) while any entry or version exists.
/// </summary>
public sealed class SettingsReferenceListPersistenceModelTests
{
    [Fact]
    public void A_reference_list_entry_is_keyed_by_company_and_id_and_its_code_is_unique_per_company_and_kind()
    {
        using var context = CreateDbContext();
        var entry = Entity<ReferenceListEntryRow>(context);

        Assert.Equal(("settings", "ReferenceListEntries"), (entry.GetSchema(), entry.GetTableName()));
        Assert.Equal(["CompanyId", "EntryId"], entry.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var unique = Assert.Single(entry.GetIndexes(), index => index.IsUnique);
        Assert.Equal(["CompanyId", "Kind", "Code"], unique.Properties.Select(property => property.Name));
        var checks = Checks(entry);
        Assert.Equal(4, checks.Count);
        Assert.Equal(SettingsDbContext.ReferenceListKindCheck, checks["CK_SettingsReferenceListEntries_Kind"]);
        Assert.Equal(SettingsDbContext.ReferenceListCodeCheck, checks["CK_SettingsReferenceListEntries_Code"]);
        Assert.Contains("[CompanyId] <>", checks["CK_SettingsReferenceListEntries_Identity"], StringComparison.Ordinal);
        Assert.Single(entry.GetDeclaredQueryFilters());
        Assert.Equal(SettingsReferenceListInputLimitsV1.CodeMaximumLength,
            entry.FindProperty(nameof(ReferenceListEntryRow.Code))!.GetMaxLength());
    }

    [Fact]
    public void The_kind_check_holds_exactly_the_opened_lists_and_not_the_operation_type_list()
    {
        foreach (var kind in SettingsReferenceListKindsV1.Enabled)
        {
            Assert.Contains($"'{SettingsReferenceListKindsV1.CodeOf(kind)}'", SettingsDbContext.ReferenceListKindCheck,
                StringComparison.Ordinal);
        }

        Assert.Equal(SettingsReferenceListKindsV1.Enabled.Count,
            SettingsDbContext.ReferenceListKindCheck.Count(character => character == '\'') / 2);
        Assert.DoesNotContain("OperationType", SettingsDbContext.ReferenceListKindCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_list_entry_version_is_keyed_by_company_entry_and_version_and_belongs_to_its_entry()
    {
        using var context = CreateDbContext();
        var version = Entity<ReferenceListEntryVersionRow>(context);

        Assert.Equal(("settings", "ReferenceListEntryVersions"), (version.GetSchema(), version.GetTableName()));
        Assert.Equal(["CompanyId", "EntryId", "Version"],
            version.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(ValueGenerated.Never, version.FindProperty(nameof(ReferenceListEntryVersionRow.Version))!.ValueGenerated);
        var foreignKey = Assert.Single(version.GetForeignKeys());
        Assert.Equal(typeof(ReferenceListEntryRow), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(["CompanyId", "EntryId"], foreignKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        var checks = Checks(version);
        Assert.Equal(3, checks.Count);
        Assert.Contains("[Version] > 0", checks["CK_SettingsReferenceListEntryVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[Name]", checks["CK_SettingsReferenceListEntryVersions_Name"], StringComparison.Ordinal);
        Assert.Contains("[Reason]", checks["CK_SettingsReferenceListEntryVersions_Recorded"], StringComparison.Ordinal);
        Assert.Single(version.GetDeclaredQueryFilters());
        Assert.Equal(SettingsReferenceListInputLimitsV1.NameMaximumLength,
            version.FindProperty(nameof(ReferenceListEntryVersionRow.Name))!.GetMaxLength());
        Assert.Equal(SettingsReferenceListInputLimitsV1.ReasonMaximumLength,
            version.FindProperty(nameof(ReferenceListEntryVersionRow.Reason))!.GetMaxLength());
    }

    [Fact]
    public void Nothing_is_defaulted_in_either_table()
    {
        using var context = CreateDbContext();

        Assert.All(
            Entity<ReferenceListEntryRow>(context).GetProperties()
                .Concat(Entity<ReferenceListEntryVersionRow>(context).GetProperties()),
            property =>
            {
                Assert.Null(property.GetDefaultValueSql());
                Assert.Null(property.GetDefaultValue());
            });
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_entry_or_version_exists()
    {
        var migration = new AddReferenceListEntries();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(
            ["ReferenceListEntries", "ReferenceListEntryVersions"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(created => created.Name).Order(StringComparer.Ordinal));
        Assert.All(migration.UpOperations.OfType<CreateTableOperation>(), created => Assert.Equal("settings", created.Schema));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51886", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[ReferenceListEntries]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[ReferenceListEntryVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(2, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

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
