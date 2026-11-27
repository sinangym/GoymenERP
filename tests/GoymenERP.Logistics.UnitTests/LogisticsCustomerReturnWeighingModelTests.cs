using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsCustomerReturnWeighingModelTests
{
    [Fact]
    public void Weighing_is_nullable_historical_evidence_bound_to_the_company_and_return_with_an_exact_parent_CAS_model()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer("Server=localhost;Database=SYNTHETIC_model_only;Integrated Security=true;TrustServerCertificate=true").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(CustomerReturnRow))!;
        Assert.Equal(["CompanyId", "CustomerReturnId"], model.FindPrimaryKey()!.Properties.Select(p => p.Name));
        foreach (var name in new[] { "WeighingId", "WeighingDirection", "FirstWeighingKg", "SecondWeighingKg", "NetWeighingKg", "WeighingRecordedBy", "WeighingRecordedAtUtc" })
            Assert.True(model.FindProperty(name)!.IsNullable);
        foreach (var name in new[] { "FirstWeighingKg", "SecondWeighingKg", "NetWeighingKg" })
            Assert.Equal((18, 3), (model.FindProperty(name)!.GetPrecision(), model.FindProperty(name)!.GetScale()));
        var index = Assert.Single(model.GetIndexes(), value => value.GetDatabaseName() == "UX_CustomerReturns_WeighingId");
        Assert.True(index.IsUnique); Assert.Equal(["CompanyId", "WeighingId"], index.Properties.Select(p => p.Name));
        Assert.Equal("[WeighingId] IS NOT NULL", index.GetFilter());
        Assert.Equal(LogisticsDbContext.CustomerReturnStateCheck, Assert.Single(model.GetCheckConstraints(), c => c.Name == "CK_CustomerReturns_State").Sql);
        Assert.Equal(LogisticsDbContext.CustomerReturnWeighingCheck, Assert.Single(model.GetCheckConstraints(), c => c.Name == "CK_CustomerReturns_Weighing").Sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Generated_migration_adds_only_nullable_weighing_facts_and_first_guards_every_fact_and_its_receipt_history_on_Down()
    {
        var migration = new AddCustomerReturnWeighing();
        Assert.Equal(11, migration.UpOperations.Count); Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToArray(); Assert.Equal(7, columns.Length);
        Assert.All(columns, column => { Assert.True(column.IsNullable); Assert.Null(column.DefaultValue); Assert.Null(column.DefaultValueSql); Assert.Equal(("logistics", "CustomerReturns"), (column.Schema, column.Table)); });
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>()); Assert.Equal(12, migration.DownOperations.Count);
        Assert.Contains("THROW 52353", guard.Sql); Assert.Contains("logistics.customer-return.record-weighing.v1", guard.Sql);
        foreach (var column in columns) Assert.Contains("[" + column.Name + "] IS NOT NULL", guard.Sql);
    }
}
