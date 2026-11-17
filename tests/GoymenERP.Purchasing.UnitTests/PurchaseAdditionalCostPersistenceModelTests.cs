using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseAdditionalCostPersistenceModelTests
{
    [Fact]
    public void Expense_facts_are_company_scoped_and_preserve_separate_real_goods_and_service_acceptance_foreign_keys()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(PurchaseAdditionalCostRow), typeof(PurchaseAdditionalCostAttemptRow) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Equal("purchasing", entity.GetSchema()); Assert.Single(entity.GetDeclaredQueryFilters());
            Assert.Contains("CompanyId", entity.FindPrimaryKey()!.Properties.Select(x => x.Name));
        }
        var fact = model.FindEntityType(typeof(PurchaseAdditionalCostRow))!;
        Assert.Contains(fact.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PurchaseAcceptanceRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "NormalOrderId", "NormalLineId", "NormalAcceptanceId"]));
        Assert.Contains(fact.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(PurchaseServiceAcceptanceRow)
            && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "ServiceOrderId", "ServiceLineId", "ServiceAcceptanceId"]));
        Assert.Equal(PurchasingDbContext.AdditionalCostSourceCheck, fact.GetCheckConstraints().Single(x => x.Name == "CK_AdditionalCosts_Source").Sql);
        Assert.DoesNotContain(fact.GetProperties(), x => x.Name is "Quantity" or "AverageCost" or "InventoryMovementId");
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Expense_history_is_append_only_and_Down_checks_all_history_before_the_first_drop()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var db = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var row = new PurchaseAdditionalCostRow { CompanyId = company.CompanyId, CostId = Guid.NewGuid(), OrderId = Guid.NewGuid(), SnapshotJson = "{}" };
        db.Attach(row); row.SnapshotJson = "{\"changed\":true}";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        var migration = new AddPurchaseAdditionalCosts();
        Assert.Empty(migration.UpOperations.OfType<SqlOperation>());
        Assert.Equal(2, migration.UpOperations.OfType<CreateTableOperation>().Count());
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51998", guard, StringComparison.Ordinal);
        Assert.Contains("[purchasing].[AdditionalCosts]", guard, StringComparison.Ordinal);
        Assert.Contains("[purchasing].[AdditionalCostAttempts]", guard, StringComparison.Ordinal);
        Assert.All(migration.DownOperations.Skip(1), x => Assert.IsType<DropTableOperation>(x));
    }
}
