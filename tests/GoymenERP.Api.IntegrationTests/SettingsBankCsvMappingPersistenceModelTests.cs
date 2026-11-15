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
/// FNS-D52 (F-014): the persistent per-bank CSV column mapping table — company and bank key inside the key, one row per
/// profile, an exact (binary) bank key collation, the check constraints equal to the DbContext constants and to the published
/// code lists and limits, the company query filter, a pure DDL Up and a forward-fix Down that refuses first (THROW 51527)
/// while any version exists.
/// </summary>
public sealed class SettingsBankCsvMappingPersistenceModelTests
{
    [Fact]
    public void A_bank_csv_mapping_version_is_keyed_by_company_bank_and_version_with_one_row_per_profile_and_its_checks()
    {
        using var context = CreateDbContext();
        var mapping = Entity<BankCsvMappingVersionRow>(context);

        Assert.Equal(("settings", "BankCsvMappingVersions"), (mapping.GetSchema(), mapping.GetTableName()));
        Assert.Equal(["CompanyId", "BankKey", "Version"], mapping.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var profile = Assert.Single(mapping.GetIndexes());
        Assert.True(profile.IsUnique);
        Assert.Equal(["CompanyId", "ProfileId"], profile.Properties.Select(property => property.Name));
        var checks = Checks(mapping);
        Assert.Equal(SettingsDbContext.BankCsvMappingIdentityCheck, checks["CK_SettingsBankCsvMappingVersions_Identity"]);
        Assert.Equal(SettingsDbContext.BankCsvMappingCodesCheck, checks["CK_SettingsBankCsvMappingVersions_Codes"]);
        Assert.Equal(SettingsDbContext.BankCsvMappingDateFormatCheck, checks["CK_SettingsBankCsvMappingVersions_DateFormat"]);
        Assert.Equal(SettingsDbContext.BankCsvMappingLayoutCheck, checks["CK_SettingsBankCsvMappingVersions_Layout"]);
        Assert.Equal(SettingsDbContext.BankCsvMappingColumnsCheck, checks["CK_SettingsBankCsvMappingVersions_Columns"]);
        Assert.Contains("[Reason]", checks["CK_SettingsBankCsvMappingVersions_Recorded"], StringComparison.Ordinal);
        var bankKey = mapping.FindProperty("BankKey")!;
        Assert.Equal(BankCsvMappingInputLimitsV1.BankKeyMaximumLength, bankKey.GetMaxLength());
        Assert.False(bankKey.IsUnicode());
        Assert.Equal(SettingsDbContext.BankCsvMappingKeyCollation, bankKey.GetCollation());
        Assert.Equal(BankCsvMappingInputLimitsV1.BankDisplayNameMaximumLength,
            mapping.FindProperty("BankDisplayName")!.GetMaxLength());
        Assert.Equal(BankCsvMappingInputLimitsV1.ReasonMaximumLength, mapping.FindProperty("Reason")!.GetMaxLength());
        Assert.True(mapping.FindProperty("SignedAmountPositiveMeans")!.IsNullable);
        Assert.False(mapping.FindProperty("TransactionDateColumn")!.IsNullable);
        Assert.False(mapping.FindProperty("DescriptionColumn")!.IsNullable);
        Assert.All(new[] { "ValueDateColumn", "AmountColumn", "InflowColumn", "OutflowColumn", "BalanceColumn",
                "ReferenceColumn", "CurrencyColumn" },
            name => Assert.True(mapping.FindProperty(name)!.IsNullable, name));
        Assert.All(mapping.GetProperties(), property => Assert.Null(property.GetDefaultValueSql()));
        Assert.NotNull(mapping.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public void The_checks_hold_exactly_the_published_code_lists_and_limits()
    {
        AssertList(BankCsvMappingStatesV1.All, "[State] IN (", SettingsDbContext.BankCsvMappingCodesCheck);
        AssertList(BankCsvDelimitersV1.All, "[Delimiter] IN (", SettingsDbContext.BankCsvMappingCodesCheck);
        AssertList(BankCsvDecimalSeparatorsV1.All, "[DecimalSeparator] IN (", SettingsDbContext.BankCsvMappingCodesCheck);
        AssertList(BankCsvThousandsSeparatorsV1.All, "[ThousandsSeparator] IN (", SettingsDbContext.BankCsvMappingCodesCheck);
        AssertList(BankCsvDateFormatsV1.All, "[DateFormat] IN (", SettingsDbContext.BankCsvMappingDateFormatCheck);
        AssertList(BankCsvSignedAmountDirectionsV1.All, "[SignedAmountPositiveMeans] IN (",
            SettingsDbContext.BankCsvMappingLayoutCheck);
        Assert.All(BankCsvAmountLayoutsV1.All, code =>
            Assert.Contains($"[AmountLayout] = '{code}'", SettingsDbContext.BankCsvMappingLayoutCheck, StringComparison.Ordinal));
        Assert.Equal(BankCsvAmountLayoutsV1.All.Count,
            SettingsDbContext.BankCsvMappingLayoutCheck.Split("[AmountLayout] = ", StringSplitOptions.None).Length - 1);

        var skip = BankCsvMappingInputLimitsV1.MaximumSkipLeadingRows.ToString(CultureInfo.InvariantCulture);
        Assert.Contains($"[SkipLeadingRows] BETWEEN 0 AND {skip}", SettingsDbContext.BankCsvMappingCodesCheck,
            StringComparison.Ordinal);
        var maximumColumn = BankCsvMappingInputLimitsV1.MaximumColumnNumber.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(9, SettingsDbContext.BankCsvMappingColumnsCheck.Split($"BETWEEN 1 AND {maximumColumn}").Length - 1);
        var keyLength = BankCsvMappingInputLimitsV1.BankKeyMaximumLength.ToString(CultureInfo.InvariantCulture);
        Assert.Contains($"LEN([BankKey]) BETWEEN 1 AND {keyLength}", SettingsDbContext.BankCsvMappingIdentityCheck,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_bank_csv_mapping_version_exists()
    {
        var migration = new AddBankCsvMappingVersions();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var created = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(("settings", "BankCsvMappingVersions"), (created.Schema, created.Name));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51527", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[BankCsvMappingVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<DropTableOperation>());
    }

    /// <summary>The check's <c>IN (…)</c> list after <paramref name="prefix"/> is exactly <paramref name="codes"/>, in order.</summary>
    private static void AssertList(IReadOnlyList<string> codes, string prefix, string check)
    {
        var start = check.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, prefix);
        start += prefix.Length;
        var end = check.IndexOf(')', start);
        Assert.Equal(string.Join(",", codes.Select(code => $"'{code}'")), check[start..end]);
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
