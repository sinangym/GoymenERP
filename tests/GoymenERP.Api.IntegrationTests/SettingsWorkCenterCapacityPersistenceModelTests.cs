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
/// URT-D89: the persistent work center capacity table — keyed by (company, work center, version) with the company query
/// filter, one row per profile, the capacity and buffer checks equal to the published units and positive values, the column
/// precisions equal to the published input bounds, nothing defaulted, a pure DDL Up and a forward-fix Down that refuses first
/// (THROW 51333) while any version exists.
/// </summary>
public sealed class SettingsWorkCenterCapacityPersistenceModelTests
{
    [Fact]
    public void A_work_center_capacity_version_is_keyed_by_company_work_center_and_version_with_its_checks()
    {
        using var context = CreateDbContext();
        var capacity = Entity<WorkCenterCapacityVersionRow>(context);

        Assert.Equal(("settings", "WorkCenterCapacityVersions"), (capacity.GetSchema(), capacity.GetTableName()));
        Assert.Equal(["CompanyId", "WorkCenterId", "Version"],
            capacity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var profile = Assert.Single(capacity.GetIndexes());
        Assert.True(profile.IsUnique);
        Assert.Equal(["ProfileId"], profile.Properties.Select(property => property.Name));
        var checks = Checks(capacity);
        Assert.Equal(4, checks.Count);
        Assert.Equal(SettingsDbContext.WorkCenterCapacityCheck, checks["CK_SettingsWorkCenterCapacityVersions_Capacity"]);
        Assert.Equal(SettingsDbContext.WorkCenterSafetyBufferCheck, checks["CK_SettingsWorkCenterCapacityVersions_SafetyBuffer"]);
        Assert.Contains("[WorkCenterId] <>", checks["CK_SettingsWorkCenterCapacityVersions_Identity"], StringComparison.Ordinal);
        Assert.Contains("[Reason]", checks["CK_SettingsWorkCenterCapacityVersions_Recorded"], StringComparison.Ordinal);
        Assert.Single(capacity.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_columns_hold_the_published_bounds_and_nothing_is_defaulted()
    {
        using var context = CreateDbContext();
        var capacity = Entity<WorkCenterCapacityVersionRow>(context);

        var value = capacity.FindProperty(nameof(WorkCenterCapacityVersionRow.InputCapacityKgPerHour))!;
        Assert.False(value.IsNullable);
        Assert.Equal(WorkCenterCapacityInputLimitsV1.CapacityMaximumIntegerDigits + WorkCenterCapacityInputLimitsV1.CapacityMaximumScale,
            value.GetPrecision());
        Assert.Equal(WorkCenterCapacityInputLimitsV1.CapacityMaximumScale, value.GetScale());

        var buffer = capacity.FindProperty(nameof(WorkCenterCapacityVersionRow.SafetyBufferValue))!;
        Assert.True(buffer.IsNullable);
        Assert.Equal(
            WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumIntegerDigits + WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumScale,
            buffer.GetPrecision());
        Assert.Equal(WorkCenterCapacityInputLimitsV1.SafetyBufferMaximumScale, buffer.GetScale());
        Assert.True(capacity.FindProperty(nameof(WorkCenterCapacityVersionRow.SafetyBufferUnit))!.IsNullable);
        Assert.Equal(WorkCenterCapacityInputLimitsV1.ReasonMaximumLength,
            capacity.FindProperty(nameof(WorkCenterCapacityVersionRow.Reason))!.GetMaxLength());

        Assert.All(capacity.GetProperties(), property =>
        {
            Assert.Null(property.GetDefaultValueSql());
            Assert.Null(property.GetDefaultValue());
        });
    }

    [Fact]
    public void The_buffer_check_holds_exactly_the_published_units()
    {
        Assert.All(WorkCenterSafetyBufferUnitsV1.All,
            unit => Assert.Contains($"'{unit}'", SettingsDbContext.WorkCenterSafetyBufferCheck, StringComparison.Ordinal));
        Assert.Equal(WorkCenterSafetyBufferUnitsV1.All.Count,
            SettingsDbContext.WorkCenterSafetyBufferCheck.Count(character => character == '\'') / 2);
        Assert.Contains("[SafetyBufferValue] IS NULL AND [SafetyBufferUnit] IS NULL", SettingsDbContext.WorkCenterSafetyBufferCheck,
            StringComparison.Ordinal);
        Assert.Contains("[SafetyBufferValue] > 0", SettingsDbContext.WorkCenterSafetyBufferCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void The_migration_Up_is_pure_DDL_and_its_Down_refuses_first_while_any_work_center_capacity_version_exists()
    {
        var migration = new AddWorkCenterCapacityVersions();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var created = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal(("settings", "WorkCenterCapacityVersions"), (created.Schema, created.Name));
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51333", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[WorkCenterCapacityVersions]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<DropTableOperation>());
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
