using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ManufacturingReopeningPersistenceTests
{
    [Fact]
    public async Task Reopenings_and_every_close_generation_have_composite_company_keys_and_history_is_append_only()
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        await using var context = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer("Server=unopened.invalid;Database=synthetic;Integrated Security=true;Encrypt=false").Options, company);
        var model = context.GetService<IDesignTimeModel>().Model;
        var reopening = model.FindEntityType(typeof(OrderReopeningRow))!;
        Assert.Equal(["CompanyId", "OrderId", "ReopenedOrderVersion"], reopening.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(reopening.GetDeclaredQueryFilters());
        Assert.Equal(ManufacturingDbContext.OrderReopeningCheck, Assert.Single(reopening.GetCheckConstraints()).Sql);
        Assert.Equal(["CompanyId", "OrderId", "OrderVersion"], model.FindEntityType(typeof(OrderClosureDecisionRow))!.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var row = new OrderReopeningRow { CompanyId = company.CompanyId, OrderId = Guid.NewGuid(), ReopenedOrderVersion = 3, Reason = "Synthetic reason" };
        context.OrderReopenings.Attach(row); row.Reason = "Changed immutable reason";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }
}
