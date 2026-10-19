using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchasingSubcontractPersistenceModelTests
{
    [Fact]
    public void Model_uses_company_scoped_immutable_lines_snapshots_attempts_and_rowversion()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        using var context = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var model = context.Model;

        var order = model.FindEntityType(typeof(PurchasingSubcontractOrderRow))!;
        Assert.Equal("SubcontractOrders", order.GetTableName());
        Assert.True(order.FindProperty(nameof(PurchasingSubcontractOrderRow.RowVersion))!.IsConcurrencyToken);
        Assert.True(order.FindProperty(nameof(PurchasingSubcontractOrderRow.Version))!.IsConcurrencyToken);
        Assert.Equal("SubcontractOrderLines", model.FindEntityType(typeof(PurchasingSubcontractOrderLineRow))!.GetTableName());
        Assert.Equal("SubcontractAdmissions", model.FindEntityType(typeof(PurchasingSubcontractAdmissionRow))!.GetTableName());
        var attempt = model.FindEntityType(typeof(PurchasingSubcontractAttemptRow))!;
        Assert.Equal("SubcontractAttempts", attempt.GetTableName());
        Assert.Equal([nameof(PurchasingSubcontractAttemptRow.CompanyId), nameof(PurchasingSubcontractAttemptRow.CommandName),
            nameof(PurchasingSubcontractAttemptRow.IdempotencyKeyHash)], attempt.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.All(new[] { order, attempt }, entity => Assert.NotNull(entity.GetQueryFilter()));
    }
}
