using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SettingsPurchaseInvoiceVariancePolicyPersistenceModelTests
{
    [Fact]
    public void Invoice_policy_migration_is_DDL_without_seed_and_Down_refuses_before_any_history_is_dropped()
    {
        using var context = Context();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains(context.Database.GetMigrations(), name => name.EndsWith("_AddPurchaseInvoiceVariancePolicies", StringComparison.Ordinal));
        var migration = new AddPurchaseInvoiceVariancePolicies();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation or InsertDataOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.Equal("PurchaseInvoiceVariancePolicies", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51985", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[settings].[PurchaseInvoiceVariancePolicies]", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public void Published_policy_is_company_keyed_exact_precision_filtered_and_has_no_automatic_values()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseInvoiceVariancePolicyRow))!;
        Assert.Equal(("settings", "PurchaseInvoiceVariancePolicies"), (entity.GetSchema(), entity.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Equal(3, entity.GetCheckConstraints().Count());
        Assert.Equal(SettingsDbContext.PurchaseInvoiceVarianceIdentityCheck,
            entity.GetCheckConstraints().Single(x => x.Name == "CK_PurchaseInvoiceVariancePolicies_Identity").Sql);
        Assert.Equal(SettingsDbContext.PurchaseInvoiceVarianceValuesCheck,
            entity.GetCheckConstraints().Single(x => x.Name == "CK_PurchaseInvoiceVariancePolicies_Values").Sql);
        Assert.Equal(SettingsDbContext.PurchaseInvoiceVariancePublicationCheck,
            entity.GetCheckConstraints().Single(x => x.Name == "CK_PurchaseInvoiceVariancePolicies_Publication").Sql);
        foreach (var name in new[] { "AmountTolerance", "RateTolerance" })
        {
            var property = entity.FindProperty(name)!;
            Assert.Equal((24, 6), (property.GetPrecision(), property.GetScale()));
            Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
            Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
        }
    }

    [Fact]
    public void A_published_version_cannot_be_rewritten_or_deleted_even_before_opening_a_database()
    {
        using var context = Context();
        var row = new PurchaseInvoiceVariancePolicyRow { CompanyId = TestCompanyIds.CompanyA, Version = 1,
            PolicyId = Guid.NewGuid(), AmountTolerance = 0, RateTolerance = 0, PublishedBy = "SENTETIK",
            PublishedAtUtc = DateTimeOffset.UtcNow, Reason = "SENTETIK" };
        context.Attach(row);
        row.AmountTolerance = 1;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        context.ChangeTracker.Clear();
        context.Attach(row); context.Remove(row);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    private static SettingsDbContext Context()
    {
        var company = new CompanyContext(); company.Set(TestCompanyIds.CompanyA);
        return new(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer("Server=invalid;Database=SENTETIK;Integrated Security=true;TrustServerCertificate=true").Options, company);
    }
}
