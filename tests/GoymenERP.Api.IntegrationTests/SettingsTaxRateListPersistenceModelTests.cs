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
/// F-029 S9 (SAT-D181): the persistent VAT rate / withholding ratio list tables — company inside every key, one list row per
/// kind (its version the concurrency token), whole versions with restrict entries and insert-only withdrawals, binary-collated
/// codes, an exact percent, the company query filter on every table, a pure DDL Up and a forward-fix Down that refuses first
/// (THROW 51377) while any rate list row exists.
/// </summary>
public sealed class SettingsTaxRateListPersistenceModelTests
{
    private const string BinaryCollation = "Latin1_General_100_BIN2";

    [Theory]
    [InlineData(typeof(TaxRateListRow), "TaxRateLists", new[] { "CompanyId", "Kind" })]
    [InlineData(typeof(TaxRateListVersionRow), "TaxRateListVersions", new[] { "CompanyId", "Kind", "ListVersion" })]
    [InlineData(typeof(TaxRateListEntryRow), "TaxRateListEntries", new[] { "CompanyId", "Kind", "ListVersion", "Code" })]
    [InlineData(typeof(TaxRateListWithdrawalRow), "TaxRateListWithdrawals", new[] { "CompanyId", "Kind", "ListVersion" })]
    public void Every_rate_list_table_is_keyed_inside_the_company_binary_by_kind_and_company_filtered(Type rowType, string table,
        string[] key)
    {
        using var context = CreateDbContext();
        var entity = Entity(context, rowType);

        Assert.Equal(("settings", table), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal(key, entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var kind = entity.FindProperty("Kind")!;
        Assert.Equal(BinaryCollation, kind.GetCollation());
        Assert.False(kind.IsUnicode());
        Assert.NotNull(entity.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void The_list_row_is_the_edit_fence_of_its_kind_and_admits_only_the_two_known_kinds()
    {
        using var context = CreateDbContext();
        var list = Entity(context, typeof(TaxRateListRow));

        Assert.True(list.FindProperty("Version")!.IsConcurrencyToken);
        var checks = Checks(list);
        Assert.Equal($"[Kind] IN ('{TaxRateListKindsV1.VatRate}', '{TaxRateListKindsV1.WithholdingRatio}')",
            checks["CK_SettingsTaxRateLists_Kind"]);
        Assert.Contains("[CompanyId] <>", checks["CK_SettingsTaxRateLists_Company"], StringComparison.Ordinal);
        Assert.Equal("[Version] > 0 AND [LastListVersion] > 0", checks["CK_SettingsTaxRateLists_Version"]);
    }

    [Fact]
    public void An_entry_is_a_restrict_child_of_its_version_with_an_exact_percent_and_the_kind_rule_in_the_database()
    {
        using var context = CreateDbContext();
        var entry = Entity(context, typeof(TaxRateListEntryRow));

        var parent = Assert.Single(entry.GetForeignKeys());
        Assert.Equal(typeof(TaxRateListVersionRow), parent.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        var code = entry.FindProperty("Code")!;
        Assert.Equal((BinaryCollation, TaxRateListInputLimitsV1.CodeMaximumLength), (code.GetCollation(), code.GetMaxLength()));
        var percent = entry.FindProperty("Percent")!;
        Assert.Equal((7, 4), (percent.GetPrecision(), percent.GetScale()));
        Assert.Equal(TaxMasterInputLimitsV1.PercentMaximumScale, percent.GetScale());
        var taxCode = entry.FindProperty("TaxCode")!;
        Assert.True(taxCode.IsNullable);
        Assert.Equal((BinaryCollation, TaxMasterInputLimitsV1.ListCodeMaximumLength), (taxCode.GetCollation(), taxCode.GetMaxLength()));
        Assert.Equal(TaxMasterInputLimitsV1.ListEntryNameMaximumLength, entry.FindProperty("Name")!.GetMaxLength());
        var checks = Checks(entry);
        Assert.Equal("LEN([Code]) BETWEEN 1 AND 16 AND [Code] NOT LIKE '%[^A-Z0-9/]%'", checks["CK_SettingsTaxRateListEntries_Code"]);
        Assert.Equal("[Percent] >= 0 AND [Percent] <= 100", checks["CK_SettingsTaxRateListEntries_Percent"]);
        Assert.Equal("([Kind] = 'VatRate' AND [TaxCode] IS NULL) OR ([Kind] = 'WithholdingRatio' AND [Percent] > 0)",
            checks["CK_SettingsTaxRateListEntries_KindRule"]);
        Assert.Equal("[TaxCode] IS NULL OR (LEN([TaxCode]) BETWEEN 1 AND 16 AND [TaxCode] NOT LIKE '%[^A-Z0-9]%')",
            checks["CK_SettingsTaxRateListEntries_TaxCode"]);
    }

    [Fact]
    public void A_withdrawal_names_its_version_and_only_a_later_replacement_both_restrict()
    {
        using var context = CreateDbContext();
        var withdrawal = Entity(context, typeof(TaxRateListWithdrawalRow));

        var parents = withdrawal.GetForeignKeys()
            .Select(foreignKey => (
                foreignKey.PrincipalEntityType.ClrType,
                string.Join(",", foreignKey.Properties.Select(property => property.Name)),
                foreignKey.DeleteBehavior))
            .OrderBy(parent => parent.Item2, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                (typeof(TaxRateListVersionRow), "CompanyId,Kind,ListVersion", DeleteBehavior.Restrict),
                (typeof(TaxRateListVersionRow), "CompanyId,Kind,ReplacedByListVersion", DeleteBehavior.Restrict),
            ],
            parents);
        Assert.True(withdrawal.FindProperty("ReplacedByListVersion")!.IsNullable);
        Assert.Equal("[ReplacedByListVersion] IS NULL OR [ReplacedByListVersion] > [ListVersion]",
            Checks(withdrawal)["CK_SettingsTaxRateListWithdrawals_Replacement"]);
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_rate_list_row_exists()
    {
        var migration = new AddTaxRateLists();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(
            ["TaxRateListEntries", "TaxRateListVersions", "TaxRateListWithdrawals", "TaxRateLists"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51377", guard.Sql, StringComparison.Ordinal);
        Assert.All(new[] { "TaxRateLists", "TaxRateListVersions", "TaxRateListEntries", "TaxRateListWithdrawals" },
            table => Assert.Contains($"[settings].[{table}]", guard.Sql, StringComparison.Ordinal));
        Assert.Equal(4, migration.DownOperations.OfType<DropTableOperation>().Count());
    }

    private static IEntityType Entity(SettingsDbContext context, Type rowType) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(rowType)
        ?? throw new InvalidOperationException($"{rowType.Name} is not mapped.");

    private static Dictionary<string, string> Checks(IEntityType entity) =>
        entity.GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);

    private static SettingsDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True")
            .Options, new CompanyContext());
}
