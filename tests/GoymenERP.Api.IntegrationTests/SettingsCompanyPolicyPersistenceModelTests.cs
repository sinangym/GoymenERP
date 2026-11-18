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
/// B-13 (ADR-SET-002) and FNS-D97: the company policy registry and the credit account mapping tables — keyed by company, key
/// (or purpose) and version under the company query filter, the published keys in the key check, nothing defaulted, a pure DDL
/// Up and a forward-fix Down that refuses first (THROW 51853) while any version exists.
/// </summary>
public sealed class SettingsCompanyPolicyPersistenceModelTests
{
    [Fact]
    public void A_company_policy_version_is_keyed_by_company_key_and_version_with_its_checks()
    {
        using var context = CreateDbContext();
        var policy = Entity<CompanyPolicyVersionRow>(context);

        Assert.Equal(("settings", "CompanyPolicyVersions"), (policy.GetSchema(), policy.GetTableName()));
        Assert.Equal(["CompanyId", "PolicyKey", "Version"],
            policy.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var checks = Checks(policy);
        Assert.Equal(5, checks.Count);
        Assert.Contains("[CompanyId] <>", checks["CK_SettingsCompanyPolicyVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[BooleanValue]", checks["CK_SettingsCompanyPolicyVersions_Value"], StringComparison.Ordinal);
        Assert.Contains("[ExemptionCode]", checks["CK_SettingsCompanyPolicyVersions_Codes"], StringComparison.Ordinal);
        Assert.Contains("[Reason]", checks["CK_SettingsCompanyPolicyVersions_Recorded"], StringComparison.Ordinal);
        Assert.Single(policy.GetDeclaredQueryFilters());
        Assert.Equal(CompanyPolicyKeysV1.MaximumLength,
            policy.FindProperty(nameof(CompanyPolicyVersionRow.PolicyKey))!.GetMaxLength());
        Assert.Equal(CompanyPolicyInputLimitsV1.ReasonMaximumLength,
            policy.FindProperty(nameof(CompanyPolicyVersionRow.Reason))!.GetMaxLength());
    }

    [Fact]
    public void The_key_check_holds_exactly_the_published_policy_keys()
    {
        using var context = CreateDbContext();
        var keyCheck = Checks(Entity<CompanyPolicyVersionRow>(context))["CK_SettingsCompanyPolicyVersions_Key"];

        Assert.All(CompanyPolicyKeysV1.All, key => Assert.Contains($"'{key}'", keyCheck, StringComparison.Ordinal));
        Assert.Equal(CompanyPolicyKeysV1.All.Count, keyCheck.Count(character => character == '\'') / 2);
    }

    [Fact]
    public void A_credit_account_mapping_version_is_keyed_by_company_purpose_and_version_with_its_checks()
    {
        using var context = CreateDbContext();
        var mapping = Entity<FinanceCreditAccountMappingVersionRow>(context);

        Assert.Equal(("settings", "FinanceCreditAccountMappingVersions"), (mapping.GetSchema(), mapping.GetTableName()));
        Assert.Equal(["CompanyId", "Purpose", "Version"],
            mapping.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var checks = Checks(mapping);
        Assert.Equal(2, checks.Count);
        Assert.All(Enum.GetValues<FinanceCreditAccountPurposeV1>(), purpose => Assert.Contains(
            ((int)purpose).ToString(System.Globalization.CultureInfo.InvariantCulture),
            checks["CK_SettingsFinanceCreditAccountMappingVersions_Identity"], StringComparison.Ordinal));
        Assert.Contains("[LedgerAccountCode]", checks["CK_SettingsFinanceCreditAccountMappingVersions_Recorded"],
            StringComparison.Ordinal);
        Assert.Single(mapping.GetDeclaredQueryFilters());
    }

    [Fact]
    public void Nothing_is_defaulted_in_either_table()
    {
        using var context = CreateDbContext();

        Assert.All(
            Entity<CompanyPolicyVersionRow>(context).GetProperties()
                .Concat(Entity<FinanceCreditAccountMappingVersionRow>(context).GetProperties()),
            property =>
            {
                Assert.Null(property.GetDefaultValueSql());
                Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
            });
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_policy_or_mapping_version_exists()
    {
        var migration = new AddCompanyPolicyVersions();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(
            ["CompanyPolicyVersions", "FinanceCreditAccountMappingVersions"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(created => created.Name).Order(StringComparer.Ordinal));
        Assert.All(migration.UpOperations.OfType<CreateTableOperation>(), created => Assert.Equal("settings", created.Schema));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51853", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[CompanyPolicyVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[FinanceCreditAccountMappingVersions]", guard.Sql, StringComparison.Ordinal);
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
