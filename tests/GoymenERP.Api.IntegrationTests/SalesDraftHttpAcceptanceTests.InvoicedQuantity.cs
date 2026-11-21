using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesDraftHttpAcceptanceTests
{
    [Fact]
    public async Task Delivery_progress_publishes_nullable_server_invoice_quantities_without_prices_or_a_new_read_permission()
    {
        // Synthetic HTTP boundary evidence only; the real SQL owner evidence is covered by the relational fact sources.
        var draftId = Guid.NewGuid();
        var proof = ISalesOrderInvoicedQuantityReadPortV1.SourceVersionPrefix + new string('a', 64);
        var service = new StubDraftService
        {
            Progress = _ => Task.FromResult(new SalesOrderDraftDeliveryProgressResponse("5",
                [new(Guid.NewGuid(), 1, Guid.NewGuid(), "6", "4", "4", "2.25", "0", ["SENTETIK-shipper"],
                    new("1.5", "2.25", proof))])),
        };
        using var host = CreateHost(service);
        using var client = CreateClient(host);
        var path = $"{SalesEndpoints.DraftsPath}/{draftId:D}/delivery-progress";

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        var line = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lines").EnumerateArray());
        var quantity = line.GetProperty("invoiceProgress");
        Assert.Equal(["invoiced", "deliveredNotInvoiced", "sourceVersion"], quantity.EnumerateObject().Select(item => item.Name));
        Assert.Equal(("1.5", "2.25", proof), (quantity.GetProperty("invoiced").GetString(),
            quantity.GetProperty("deliveredNotInvoiced").GetString(), quantity.GetProperty("sourceVersion").GetString()));
        Assert.Equal("2.25", line.GetProperty("remaining").GetString());
        Assert.Equal([draftId], service.ProgressReads);
    }
}
