using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesInvoiceShipmentSourceContractTests
{
    [Fact]
    public void An_invoice_create_claims_whole_shipments_and_refuses_client_quantity_overrides()
    {
        var accountId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var request = SalesInvoiceJson.ReadCreate(JsonSerializer.Serialize(new
        {
            companyAccountId = accountId,
            shares = new[] { new { shipmentId } },
        }));
        Assert.Equal(accountId, request.CompanyAccountId);
        Assert.Equal(shipmentId, Assert.Single(request.Shares).ShipmentId);

        // SAT-D94/AT-055: the request identifies an entire share; it cannot introduce a partial claim or relabel invoice kg
        // as the Inventory movement's physical kg. The source quantity is read from the frozen dispatch allocation instead.
        foreach (var field in new[] { "quantity", "invoicedQuantity", "physicalKilograms" })
        {
            var json = JsonSerializer.Serialize(new
            {
                companyAccountId = accountId,
                shares = new[] { new Dictionary<string, object> { ["shipmentId"] = shipmentId, [field] = "0.5" } },
            });
            Assert.Throws<JsonException>(() => SalesInvoiceJson.ReadCreate(json));
        }
    }
}
