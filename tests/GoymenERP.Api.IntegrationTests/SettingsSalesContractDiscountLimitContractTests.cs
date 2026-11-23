using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsSalesContractDiscountLimitContractTests
{
    [Fact]
    public void The_generated_Settings_registry_migrations_are_ordered_and_the_snapshot_matches_the_current_model()
    {
        using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, new CompanyContext());
        Assert.Equal(["20261116181354_AddCollateralTypeCatalog", "20261116182730_AddSalesContractDiscountLimit",
                "20261116220424_AddDocumentNumberSeriesAndOperationalPolicies", "20261117001828_AddManufacturingProfileBaselineTransfers",
                "20261117085524_AddPurchaseInvoiceVariancePolicies", "20261117111242_AddWarehouseTransferScalePolicies",
                "20261117200733_AddFinanceDocumentNumberSeries", "20261117220044_AddFinancePolicyFamilies",
                "20261120101534_HardenWorkCenterSafetyBufferNullPairs", "20261122181548_AddWorkApprovalDurationPolicies",
                "20261122202227_AddLegalDeadlineCalendars", "20261122202350_AddAiGatewayPolicyAndBudgetLedger",
                "20261122224127_Wp2WorkDigestTimes"], database.Database.GetMigrations().TakeLast(13));
        Assert.False(database.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_generated_additive_migration_has_one_nullable_column_no_backfill_and_a_first_unsafe_Down_guard()
    {
        var migration = new AddSalesContractDiscountLimit();
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal(("settings", "CompanyPolicyVersions", "PercentValue", "decimal(7,4)", true),
            (column.Schema, column.Table, column.Name, column.ColumnType, column.IsNullable));
        Assert.Null(column.DefaultValue); Assert.Null(column.DefaultValueSql);
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation or InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51942", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("sales.contract-discount-limit", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[PercentValue] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        var rateCheck = migration.UpOperations.OfType<AddCheckConstraintOperation>().Single(check => check.Name == "CK_SettingsCollateralTypeVersions_RateText");
        Assert.Equal(SettingsDbContext.CollateralRateCheck, rateCheck.Sql);
    }

    [Fact]
    public void The_new_key_is_separate_nullable_exact_and_has_no_seed_or_default()
    {
        using var database = new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, new CompanyContext());
        var row = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CompanyPolicyVersionRow))!;
        var percent = row.FindProperty(nameof(CompanyPolicyVersionRow.PercentValue))!;
        Assert.True(percent.IsNullable); Assert.Equal(7, percent.GetPrecision()); Assert.Equal(4, percent.GetScale());
        Assert.Null(percent.GetDefaultValue()); Assert.Null(percent.GetDefaultValueSql()); Assert.Empty(row.GetSeedData());
        Assert.Contains("'sales.contract-discount-limit'", row.GetCheckConstraints().Single(check => check.Name == "CK_SettingsCompanyPolicyVersions_Key").Sql, StringComparison.Ordinal);
        var shape = row.GetCheckConstraints().Single(check => check.Name == "CK_SettingsCompanyPolicyVersions_Value").Sql;
        Assert.Contains("[PercentValue] IS NOT NULL AND [PercentValue] >= 0 AND [PercentValue] <= 100", shape, StringComparison.Ordinal);
        // Every non-percent key family pins the percent column to NULL: boolean, whole days, the W1-A approval durations, the
        // WP2 digest times (YOL-D25) and the tax defaults.
        Assert.Equal(5, shape.Split("[PercentValue] IS NULL", StringSplitOptions.None).Length - 1);
        Assert.Equal(ErpPermissions.SettingsSalesContractDiscountLimitManage, SettingsSalesContractDiscountLimitPermissionsV1.Manage);
        Assert.True(ErpPermissions.IsKnown(SettingsSalesContractDiscountLimitPermissionsV1.Manage));
        Assert.True(ErpPermissions.RequiresCompanyWideScope(SettingsSalesContractDiscountLimitPermissionsV1.Manage));
        Assert.False(ErpPermissions.IsCritical(SettingsSalesContractDiscountLimitPermissionsV1.Manage));
        Assert.NotEqual(ErpPermissions.SettingsSalesDiscountLimitsManage, SettingsSalesContractDiscountLimitPermissionsV1.Manage);
    }

    [Fact]
    public async Task The_Sales_adapter_distinguishes_missing_zero_denied_unavailable_and_malformed_answers()
    {
        var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
        var provider = new Source { Answer = new(TestCompanyIds.CompanyA, null, 0) };
        var host = new SettingsSalesContractDiscountLimitHost(provider, company);
        var day = new DateOnly(2026, 11, 16);
        Assert.Null(await host.GetContractDiscountLimitPercentAsync(day));
        provider.Answer = new(TestCompanyIds.CompanyA, 0, 1);
        Assert.Equal(0m, await host.GetContractDiscountLimitPercentAsync(day));
        provider.Failure = new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.AccessDenied);
        Assert.True((await Assert.ThrowsAsync<SalesContractDiscountLimitSourceException>(() => host.GetContractDiscountLimitPercentAsync(day))).IsDenied);
        provider.Failure = new SettingsPrerequisiteException(CompanyPolicyProblemCodesV1.ProviderUnavailable);
        Assert.False((await Assert.ThrowsAsync<SalesContractDiscountLimitSourceException>(() => host.GetContractDiscountLimitPercentAsync(day))).IsDenied);
        provider.Failure = null;
        foreach (var bad in new[] { new SettingsSalesContractDiscountLimitV1(TestCompanyIds.CompanyB, 5, 1), new(TestCompanyIds.CompanyA, 0, 0), new(TestCompanyIds.CompanyA, null, 1), new(TestCompanyIds.CompanyA, 100.0001m, 1) })
        {
            provider.Answer = bad;
            Assert.False((await Assert.ThrowsAsync<SalesContractDiscountLimitSourceException>(() => host.GetContractDiscountLimitPercentAsync(day))).IsDenied);
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.GetContractDiscountLimitPercentAsync(day, cancelled.Token));
    }

    private sealed class Source : ISettingsSalesContractDiscountLimitProviderV1
    {
        public required SettingsSalesContractDiscountLimitV1 Answer { get; set; }
        public Exception? Failure { get; set; }
        public Task<SettingsSalesContractDiscountLimitV1> GetContractDiscountLimitAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Failure is null ? Task.FromResult(Answer) : Task.FromException<SettingsSalesContractDiscountLimitV1>(Failure);
    }
}
