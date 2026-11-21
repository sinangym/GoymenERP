using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    private const string InvoiceQuantityToken = ISalesOrderInvoicedQuantityReadPortV1.SourceVersionPrefix
        + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Invoiced_and_uninvoiced_quantities_use_actual_finalized_line_evidence_and_net_physical_returns()
    {
        var world = DeliveryWorld();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("invoiced-progress-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(lineId, KgUnit, 1.25m, .5m)]));
        world.CustomerReturns = new FakeCustomerReturns(order => new(Company, order, [new(lineId, KgUnit, .25m)]));
        world.InvoicedQuantities = new FakeInvoicedQuantities(order => new(Company, order,
            SalesOrderInvoicedQuantityReadOutcomeV1.Available,
            [new(Guid.NewGuid(), Guid.NewGuid(), lineId, KgUnit, .4m, "Normal", new string('b', 64)),
                new(Guid.NewGuid(), Guid.NewGuid(), lineId, KgUnit, .1m, "Normal", new string('c', 64))], InvoiceQuantityToken));

        var line = Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines);

        Assert.Equal(("1.25", "0.5", "0.25"), (line.Shipped, line.Delivered, line.Remaining));
        Assert.Equal(new("0.5", "0.5", InvoiceQuantityToken), line.InvoiceProgress);
    }

    [Fact]
    public async Task A_complete_empty_invoice_source_proves_zero_but_an_absent_or_unavailable_source_never_does()
    {
        var world = DeliveryWorld();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("invoiced-empty-create"))).Detail;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(created.Lines[0].LineId, KgUnit, 1m, 1m)]));
        Assert.Null(Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines).InvoiceProgress);
        world.InvoicedQuantities = new FakeInvoicedQuantities(order => new(Company, order,
            SalesOrderInvoicedQuantityReadOutcomeV1.Unavailable, [], null));
        Assert.Null(Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines).InvoiceProgress);
        world.InvoicedQuantities = new FakeInvoicedQuantities(order => new(Company, order,
            SalesOrderInvoicedQuantityReadOutcomeV1.Available, [], InvoiceQuantityToken));
        Assert.Equal(new("0", "1", InvoiceQuantityToken),
            Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines).InvoiceProgress);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    [InlineData("unit")]
    [InlineData("line")]
    [InlineData("scale")]
    [InlineData("duplicate")]
    [InlineData("proof")]
    public async Task Malformed_invoice_evidence_leaves_nullable_quantities_without_changing_the_real_remainder(string fault)
    {
        var world = DeliveryWorld();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("invoiced-invalid-create"))).Detail;
        var id = created.Lines[0].LineId;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, [new(id, KgUnit, 1m, 1m)]));
        var item = new SalesOrderInvoicedQuantityLineV1(Guid.NewGuid(), Guid.NewGuid(), fault == "line" ? Guid.NewGuid() : id,
            fault == "unit" ? Guid.NewGuid() : KgUnit, fault == "scale" ? .1001m : .1m, "Normal", new string('b', 64));
        world.InvoicedQuantities = new FakeInvoicedQuantities(order => new(fault == "company" ? Guid.NewGuid() : Company,
            fault == "order" ? Guid.NewGuid() : order, SalesOrderInvoicedQuantityReadOutcomeV1.Available,
            fault == "duplicate" ? [item, item] : [item], fault == "proof" ? "unproven" : InvoiceQuantityToken));

        var line = Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines);

        Assert.Null(line.InvoiceProgress);
        Assert.Equal("0.25", line.Remaining);
    }

    [Fact]
    public async Task Invoice_evidence_is_fenced_and_legacy_nullable_proof_is_omitted_on_the_wire()
    {
        var world = DeliveryWorld();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("invoiced-fence-create"))).Detail;
        world.Deliveries = new FakeDeliveries(order => new(Company, order, []));
        var legacy = Assert.Single((await world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id)).Lines);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(json.RootElement.TryGetProperty("invoiceProgress", out _));
        world.InvoicedQuantities = new FakeInvoicedQuantities(order =>
        {
            world.User.Id = Guid.NewGuid().ToString("D");
            return new(Company, order, SalesOrderInvoicedQuantityReadOutcomeV1.Available, [], InvoiceQuantityToken);
        });
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().GetDraftDeliveryProgressAsync(created.Draft.Id))).Status);
    }

    private sealed class FakeInvoicedQuantities(Func<Guid, SalesOrderInvoicedQuantityReadV1> answer) : ISalesOrderInvoicedQuantityReadPortV1
    {
        public Task<SalesOrderInvoicedQuantityReadV1> ReadAsync(Guid companyId, Guid salesOrderId,
            Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default) => Task.FromResult(answer(salesOrderId));
    }
}
