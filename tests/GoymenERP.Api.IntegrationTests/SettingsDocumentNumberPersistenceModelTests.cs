using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsDocumentNumberPersistenceModelTests
{
    [Fact]
    public void The_number_series_and_operational_policy_migration_is_DDL_with_a_first_forward_fix_guard_and_matching_snapshot()
    {
        using var database = Create();
        Assert.Equal(new[] { "20261116220424_AddDocumentNumberSeriesAndOperationalPolicies", "20261117001828_AddManufacturingProfileBaselineTransfers", "20261117085524_AddPurchaseInvoiceVariancePolicies", "20261117111242_AddWarehouseTransferScalePolicies" },
            database.Database.GetMigrations().TakeLast(4));
        Assert.False(database.Database.HasPendingModelChanges());
        var migration = new AddDocumentNumberSeriesAndOperationalPolicies();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(new[] { "DocumentNumberCancellations", "DocumentNumberCounters", "DocumentNumberFormats", "DocumentNumberReservations" }, migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name).Order());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51954", guard.Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        foreach (var table in new[] { "DocumentNumberCancellations", "DocumentNumberCounters", "DocumentNumberFormats", "DocumentNumberReservations", "CompanyPolicyVersions" }) Assert.Contains("[settings].[" + table + "]", guard.Sql);
        Assert.Contains("manufacturing.include-scrap-cost", guard.Sql); Assert.Contains("work.notification-active-days", guard.Sql);
    }
    [Fact]
    public void Document_number_history_is_company_fenced_with_annual_and_document_uniqueness_and_no_defaults()
    {
        using var database = Create();
        var model = database.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(DocumentNumberFormatRow), typeof(DocumentNumberCounterRow), typeof(DocumentNumberReservationRow), typeof(DocumentNumberCancellationRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("settings", entity.GetSchema());
            Assert.Equal("CompanyId", entity.FindPrimaryKey()!.Properties[0].Name);
            Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.All(entity.GetProperties(), property => { Assert.Null(property.GetDefaultValueSql()); Assert.Null(property.GetDefaultValue()); });
        }
        var reservation = model.FindEntityType(typeof(DocumentNumberReservationRow))!;
        var unique = reservation.GetIndexes().Where(index => index.IsUnique).Select(index => string.Join(",", index.Properties.Select(property => property.Name))).ToArray();
        Assert.Contains("CompanyId,Series,Year,Sequence", unique);
        Assert.Contains("CompanyId,Number", unique);
        Assert.Contains("CompanyId,KeyHash", unique);
        Assert.Equal(2, reservation.GetForeignKeys().Count());
        Assert.True(model.FindEntityType(typeof(DocumentNumberCounterRow))!.FindProperty(nameof(DocumentNumberCounterRow.RowVersion))!.IsConcurrencyToken);
    }
    [Fact]
    public async Task Number_history_cannot_be_rewritten_deleted_or_written_under_a_foreign_company()
    {
        var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
        using var database = Create(company);
        var reservation = new DocumentNumberReservationRow { CompanyId = TestCompanyIds.CompanyB, Series = 4, DocumentId = Guid.NewGuid() };
        database.Add(reservation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        var format = new DocumentNumberFormatRow { CompanyId = TestCompanyIds.CompanyA, Series = 4, Version = 1, Prefix = "ALI", SequenceDigits = 6 };
        database.Attach(format); format.Prefix = "YENI";
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear(); database.Attach(format); database.Remove(format);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
        database.ChangeTracker.Clear();
        var counter = new DocumentNumberCounterRow { CompanyId = TestCompanyIds.CompanyA, Series = 4, Year = 2026, LastSequence = 5 };
        database.Attach(counter); counter.LastSequence = 3;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }
    private static SettingsDbContext Create(ICompanyContext? company = null) => new(new DbContextOptionsBuilder<SettingsDbContext>()
        .UseSqlServer("Server=model-only;Database=model-only;Trusted_Connection=True;TrustServerCertificate=True").Options, company ?? new CompanyContext());
}
