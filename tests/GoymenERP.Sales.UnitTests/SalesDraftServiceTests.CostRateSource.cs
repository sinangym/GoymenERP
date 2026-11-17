using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Cost_rate_source_reads_the_approved_zero_revision_and_order_day_without_writing_a_rate()
    {
        var world = new World();
        var approved = await PricedApprovedAsync(world, "cost-rate");
        var order = world.Store.Drafts.Single();
        var context = new CompanyContext(); context.Set(Company);
        var reader = new SalesOrderCostRateSourceReadPortV1(world.Store, context);
        var before = (order.Version, world.Store.Attempts, world.Store.Transitions.Count);
        var answer = await reader.GetAsync(Company, order.Id, order.Lines[0].Id, order.Revision);
        Assert.Equal(SalesOrderCostRateSourceOutcomeV1.Available, answer.Outcome);
        Assert.Equal(0, answer.Revision);
        Assert.Equal(order.Header.BusinessDate, answer.RateDay);
        Assert.Equal("TcmbForexSelling", answer.RateType);
        Assert.Equal(order.Header.CurrencyId, answer.OrderCurrencyId);
        Assert.Equal(before, (order.Version, world.Store.Attempts, world.Store.Transitions.Count));
        Assert.Equal(answer, await reader.GetAsync(Company, approved.Draft.Id, order.Lines[0].Id, 0));
        var line = order.Lines[0];
        var increased = line.Quantity.Value + 1m;
        order.ReviseQuantities(order.Version, [new(line.Id, increased.ToString(System.Globalization.CultureInfo.InvariantCulture), increased)],
            "SENTETIK", "SENTETIK quantity revision", order.UpdatedAtUtc.AddMinutes(1));
        var historical = await reader.GetAsync(Company, order.Id, line.Id, 0);
        Assert.Equal(SalesOrderCostRateSourceOutcomeV1.Available, historical.Outcome);
        Assert.Equal(answer.RateDay, historical.RateDay);
        Assert.NotEqual(answer.SourceVersion, historical.SourceVersion);
        Assert.Equal(SalesOrderCostRateSourceOutcomeV1.Unavailable, (await reader.GetAsync(Company, order.Id, line.Id, 1)).Outcome);
    }

    [Fact]
    public async Task Cost_rate_source_refuses_unapproved_wrong_line_wrong_revision_and_foreign_company()
    {
        var world = new World();
        await world.Service().CreateDraftAsync(Priced(discount: null), Metadata("cost-rate-draft"));
        var order = world.Store.Drafts.Single();
        var context = new CompanyContext(); context.Set(Company);
        var reader = new SalesOrderCostRateSourceReadPortV1(world.Store, context);
        var unapproved = await reader.GetAsync(Company, order.Id, order.Lines[0].Id, 0);
        Assert.Equal(SalesOrderCostRateSourceOutcomeV1.Unavailable, unapproved.Outcome);
        Assert.Null(unapproved.RateDay);
        Assert.Equal(SalesOrderCostRateSourceOutcomeV1.Unavailable, (await reader.GetAsync(Company, order.Id, Guid.NewGuid(), 0)).Outcome);
        Assert.Equal(SalesOrderCostRateSourceOutcomeV1.Unavailable, (await reader.GetAsync(Company, order.Id, order.Lines[0].Id, 5)).Outcome);
        Assert.Equal(SalesOrderCostRateSourceOutcomeV1.NotFound, (await reader.GetAsync(Company, Guid.NewGuid(), order.Lines[0].Id, 0)).Outcome);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetAsync(Guid.NewGuid(), order.Id, order.Lines[0].Id, 0));
    }
}
