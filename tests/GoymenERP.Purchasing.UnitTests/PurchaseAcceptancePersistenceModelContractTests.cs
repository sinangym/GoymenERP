using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseAcceptancePersistenceModelContractTests
{
    [Fact]
    public void Acceptance_share_has_tenant_idempotency_order_and_quantity_guards()
    {
        var company = new CompanyContext();
        company.Set(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var options = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GoymenERP_Purchasing_Model_Only;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var context = new PurchasingDbContext(options, company);

        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseAcceptanceRow));

        Assert.NotNull(entity);
        Assert.Equal("PurchaseAcceptanceShares", entity!.GetTableName());
        Assert.Equal(PurchasingDbContext.Schema, entity.GetSchema());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(PurchaseAcceptanceRow.CompanyId), nameof(PurchaseAcceptanceRow.IdempotencyKeyHash)]));
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Name == "CK_PurchaseAcceptanceShares_Quantity");
        Assert.Contains(entity.GetCheckConstraints(), constraint => constraint.Name == "CK_PurchaseAcceptanceShares_Hashes");
        var orderLine = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PurchaseOrderLineRow));
        Assert.NotNull(orderLine);
        Assert.Contains(orderLine!.GetKeys(), key => key.Properties.Select(property => property.Name).SequenceEqual(
            [nameof(PurchaseOrderLineRow.CompanyId), nameof(PurchaseOrderLineRow.OrderId), nameof(PurchaseOrderLineRow.LineId)]));
        Assert.Contains(entity.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType == orderLine
            && foreignKey.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(PurchaseAcceptanceRow.CompanyId), nameof(PurchaseAcceptanceRow.OrderId), nameof(PurchaseAcceptanceRow.LineId)]));
    }
}
