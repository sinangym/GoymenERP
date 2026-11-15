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
/// F-029 S5/S6: the persistent quote loss reason list / default validity and e-Fatura scenario code list / default tables —
/// company inside every key, one row per profile, entries as restrict children of their list version with unique ids and
/// codes per list, binary-collated codes, the company query filter on every table, the transfer marker accepting both new
/// families, a pure DDL Up and a forward-fix Down that refuses first (THROW 51312) while any row of the two families exists.
/// </summary>
public sealed class SettingsSalesQuoteAndEInvoicePolicyPersistenceModelTests
{
    private const string BinaryCollation = "Latin1_General_100_BIN2";

    [Fact]
    public void The_transfer_marker_accepts_the_quote_and_einvoice_scenario_families()
    {
        Assert.Contains("'SalesQuotePolicy'", SettingsDbContext.SalesPolicyFamilyCheck, StringComparison.Ordinal);
        Assert.Contains("'EInvoiceScenario'", SettingsDbContext.SalesPolicyFamilyCheck, StringComparison.Ordinal);
        Assert.Contains("'DeliveryTolerance'", SettingsDbContext.SalesPolicyFamilyCheck, StringComparison.Ordinal);
        Assert.Contains("'ApprovalAmountThreshold'", SettingsDbContext.SalesPolicyFamilyCheck, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(SalesQuoteLossReasonListVersionRow), "SalesQuoteLossReasonListVersions")]
    [InlineData(typeof(SalesQuoteDefaultValidityVersionRow), "SalesQuoteDefaultValidityVersions")]
    [InlineData(typeof(EInvoiceScenarioCodeListVersionRow), "EInvoiceScenarioCodeListVersions")]
    [InlineData(typeof(EInvoiceScenarioDefaultVersionRow), "EInvoiceScenarioDefaultVersions")]
    public void A_version_header_is_keyed_by_company_and_version_with_one_row_per_profile(Type rowType, string table)
    {
        using var context = CreateDbContext();
        var header = Entity(context, rowType);

        Assert.Equal(("settings", table), (header.GetSchema(), header.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], header.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var profile = Assert.Single(header.GetIndexes());
        Assert.True(profile.IsUnique);
        Assert.Equal(["CompanyId", "ProfileId"], profile.Properties.Select(property => property.Name));
        var checks = Checks(header);
        Assert.Equal(SettingsDbContext.SalesPolicySourceKindCheck, checks[$"CK_Settings{table}_Source"]);
        Assert.Contains("[Version] > 0", checks[$"CK_Settings{table}_Identity"], StringComparison.Ordinal);
        Assert.Contains("[Reason]", checks[$"CK_Settings{table}_Recorded"], StringComparison.Ordinal);
        Assert.True(header.FindProperty("DecisionReference")!.IsNullable);
        Assert.NotNull(header.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void A_loss_reason_entry_is_a_restrict_child_of_its_list_with_unique_ids_and_binary_codes_per_list()
    {
        using var context = CreateDbContext();
        var entry = Entity(context, typeof(SalesQuoteLossReasonEntryRow));

        Assert.Equal(("settings", "SalesQuoteLossReasonEntries"), (entry.GetSchema(), entry.GetTableName()));
        Assert.Equal(["CompanyId", "ListVersion", "Position"], entry.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var uniques = entry.GetIndexes().Where(index => index.IsUnique)
            .Select(index => string.Join(",", index.Properties.Select(property => property.Name)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["CompanyId,ListVersion,Code", "CompanyId,ListVersion,ReasonId"], uniques);
        var parent = Assert.Single(entry.GetForeignKeys());
        Assert.Equal(typeof(SalesQuoteLossReasonListVersionRow), parent.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        Assert.Equal(BinaryCollation, entry.FindProperty("Code")!.GetCollation());
        var checks = Checks(entry);
        Assert.Contains("[ReasonId] <>", checks["CK_SettingsSalesQuoteLossReasonEntries_Identity"], StringComparison.Ordinal);
        Assert.Contains("DATALENGTH([Code])", checks["CK_SettingsSalesQuoteLossReasonEntries_Code"], StringComparison.Ordinal);
        Assert.Contains("DATALENGTH([Label])", checks["CK_SettingsSalesQuoteLossReasonEntries_Label"], StringComparison.Ordinal);
        Assert.NotNull(entry.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void A_default_validity_is_a_positive_day_count()
    {
        using var context = CreateDbContext();
        var validity = Entity(context, typeof(SalesQuoteDefaultValidityVersionRow));

        Assert.Equal("[ValidityDays] > 0", Checks(validity)["CK_SettingsSalesQuoteDefaultValidityVersions_Days"]);
    }

    [Fact]
    public void A_scenario_code_is_a_restrict_child_of_its_list_unique_per_list_binary_and_shape_checked()
    {
        using var context = CreateDbContext();
        var code = Entity(context, typeof(EInvoiceScenarioCodeRow));

        Assert.Equal(("settings", "EInvoiceScenarioCodes"), (code.GetSchema(), code.GetTableName()));
        Assert.Equal(["CompanyId", "ListVersion", "Position"], code.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var unique = Assert.Single(code.GetIndexes());
        Assert.True(unique.IsUnique);
        Assert.Equal(["CompanyId", "ListVersion", "Code"], unique.Properties.Select(property => property.Name));
        var parent = Assert.Single(code.GetForeignKeys());
        Assert.Equal(typeof(EInvoiceScenarioCodeListVersionRow), parent.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, parent.DeleteBehavior);
        var property = code.FindProperty("Code")!;
        Assert.Equal(BinaryCollation, property.GetCollation());
        Assert.Equal(EInvoiceScenarioCodeFormatV1.MaximumLength, property.GetMaxLength());
        Assert.False(property.IsUnicode());
        Assert.Equal(SettingsDbContext.EInvoiceScenarioCodeShapeCheck, Checks(code)["CK_SettingsEInvoiceScenarioCodes_Code"]);
        Assert.NotNull(code.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void A_default_scenario_code_is_binary_and_shape_checked()
    {
        using var context = CreateDbContext();
        var scenarioDefault = Entity(context, typeof(EInvoiceScenarioDefaultVersionRow));

        var property = scenarioDefault.FindProperty("ScenarioCode")!;
        Assert.Equal(BinaryCollation, property.GetCollation());
        Assert.Equal(EInvoiceScenarioCodeFormatV1.MaximumLength, property.GetMaxLength());
        Assert.False(property.IsNullable);
        Assert.Equal(SettingsDbContext.EInvoiceScenarioDefaultShapeCheck,
            Checks(scenarioDefault)["CK_SettingsEInvoiceScenarioDefaultVersions_Code"]);
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_quote_or_scenario_row_exists()
    {
        var migration = new AddSalesQuoteAndEInvoiceScenarioPolicies();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(6, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51312", guard.Sql, StringComparison.Ordinal);
        Assert.All(new[]
            {
                "SalesQuoteLossReasonListVersions", "SalesQuoteLossReasonEntries", "SalesQuoteDefaultValidityVersions",
                "EInvoiceScenarioCodeListVersions", "EInvoiceScenarioCodes", "EInvoiceScenarioDefaultVersions",
                "SalesPolicyBaselineTransfers",
            },
            table => Assert.Contains($"[settings].[{table}]", guard.Sql, StringComparison.Ordinal));
        Assert.Contains("'SalesQuotePolicy'", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("'EInvoiceScenario'", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(6, migration.DownOperations.OfType<DropTableOperation>().Count());
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
