using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Approved_physical_returns_reopen_the_order_remainder_without_rewriting_gross_delivery_facts()
    {
        var world = DeliveryWorld();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("progress-return-create"))).Detail;
        var id = created.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(id, KgUnit, 1.25m, .75m)]));
        var owner = new FakeCustomerReturns(order => new(Company, order,
            [new(id, KgUnit, .1m), new(id, KgUnit, .15m)]));
        world.CustomerReturns = owner;
        var attempts = world.Store.Attempts;
        var envelopes = world.Store.Envelopes.Count;

        var line = Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines);

        Assert.Equal(("1.25", "1.25", "0.75", "0.25", "0"),
            (line.Ordered, line.Shipped, line.Delivered, line.Remaining, line.Excess));
        Assert.Equal([created.Draft.Id], owner.Asked);
        Assert.Equal((attempts, envelopes), (world.Store.Attempts, world.Store.Envelopes.Count));
        Assert.Empty(world.Store.Transitions);
        Assert.Empty(world.Store.Refusals);

        // A fresh owner read, not a stored Sales copy or an invoiced-quantity adjustment.
        owner.Answer = order => new(Company, order, [new(id, KgUnit, .5m)]);
        Assert.Equal("0.5", Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines).Remaining);
    }

    [Fact]
    public async Task Approved_returns_correct_excess_using_the_existing_net_delivered_calculator()
    {
        var world = DeliveryWorld();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("progress-excess-create"))).Detail;
        var id = created.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(id, KgUnit, 1.5m, 1.5m)]));
        world.CustomerReturns = new FakeCustomerReturns(order => new(Company, order, [new(id, KgUnit, .5m)]));

        var line = Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines);

        Assert.Equal(("1.5", "1.5", "0.25", "0"), (line.Shipped, line.Delivered, line.Remaining, line.Excess));
    }

    [Fact]
    public async Task Missing_or_failed_return_evidence_does_not_become_a_zero_return_remainder()
    {
        var world = DeliveryWorld();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("progress-no-return-owner"))).Detail;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(created.Lines[0].LineId, KgUnit, 1.25m, 1.25m)]));
        world.CustomerReturns = null;
        Assert.Equal((503, SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable), Status(
            await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id))));
        world.CustomerReturns = new FakeCustomerReturns(_ => throw new InvalidOperationException("SENTETIK return owner outage"));
        Assert.Equal((503, SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable), Status(
            await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id))));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    [InlineData("line")]
    [InlineData("unit")]
    [InlineData("scale")]
    [InlineData("above-shipped")]
    public async Task Untrusted_return_totals_are_not_released_as_an_order_remainder(string fault)
    {
        var world = DeliveryWorld();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("progress-invalid-return"))).Detail;
        var id = created.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(id, KgUnit, 1m, 1m)]));
        world.CustomerReturns = new FakeCustomerReturns(order => new(fault == "company" ? Guid.NewGuid() : Company,
            fault == "order" ? Guid.NewGuid() : order,
            [new(fault == "line" ? Guid.NewGuid() : id, fault == "unit" ? Guid.NewGuid() : KgUnit,
                fault == "scale" ? .1001m : fault == "above-shipped" ? 1.001m : .1m)]));

        Assert.Equal((503, SalesDraftProblemCodesV1.ReferenceProviderInvalid), Status(
            await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id))));
    }

    [Theory]
    [InlineData("company", 403)]
    [InlineData("actor", 403)]
    [InlineData("permission", 403)]
    [InlineData("unauthenticated", 401)]
    public async Task Return_owner_completion_is_fenced_by_the_original_actor_company_and_current_read_authority(string drift, int status)
    {
        var world = DeliveryWorld();
        var company = new SwitchingCompany(Company);
        world.CompanyOverride = company;
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("progress-return-fence"))).Detail;
        var id = created.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(id, KgUnit, 1.25m, 1.25m)]));
        var owner = new FakeCustomerReturns(order =>
        {
            if (drift == "company") company.Set(Guid.NewGuid());
            else if (drift == "actor") world.User.Id = Guid.NewGuid().ToString("D");
            else if (drift == "unauthenticated") world.User.Authenticated = false;
            else world.Authority.Denied.Add(ReadPermission);
            return new(Company, order, [new(id, KgUnit, .25m)]);
        });
        world.CustomerReturns = owner;

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id));

        Assert.Equal(status, refusal.Status);
        Assert.Single(owner.Asked);
    }
}
