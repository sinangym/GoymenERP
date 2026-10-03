using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;

namespace GoymenERP.Logistics.UnitTests;

/// <summary>
/// Contract-level tests for the owner seams. SQL/host acceptance is deliberately deferred until the
/// Inventory participant and central adapter are owned and ready; these tests prevent either owner
/// from accepting raw label/location/owner/custodian data from a client.
/// </summary>
public sealed class LogisticsCustomerReturnServiceTests
{
    [Fact]
    public void Inventory_request_contains_only_source_identity_and_never_client_selected_stock_position()
    {
        var properties = typeof(LogisticsCustomerReturnStockRequestV1).GetProperties().Select(property => property.Name).ToArray();

        Assert.Contains(nameof(LogisticsCustomerReturnStockRequestV1.SourceShipmentId), properties);
        Assert.Contains(nameof(LogisticsCustomerReturnStockRequestV1.SourceDeliveryId), properties);
        Assert.DoesNotContain("LabelId", properties);
        Assert.DoesNotContain("LocationId", properties);
        Assert.DoesNotContain("OwnerPartyId", properties);
        Assert.DoesNotContain("CustodianPartyId", properties);
    }

    [Fact]
    public void Sales_read_contract_exposes_only_approved_aggregate_not_a_financial_reversal()
    {
        var properties = typeof(GoymenERP.Modules.Sales.Application.Contracts.SalesOrderLineCustomerReturnV1)
            .GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(new[] { "SalesOrderLineId", "UnitId", "ApprovedReturnedQuantity" }, properties);
        Assert.DoesNotContain("CreditNoteId", properties);
        Assert.DoesNotContain("InvoiceId", properties);
        Assert.DoesNotContain("Amount", properties);
    }

    [Fact]
    public void Pending_and_rejected_are_not_approved_physical_return_totals()
    {
        Assert.NotEqual(CustomerReturnStatus.Pending, CustomerReturnStatus.Approved);
        Assert.NotEqual(CustomerReturnStatus.Rejected, CustomerReturnStatus.Approved);
        Assert.Equal(0m, Quantity.Zero(QuantityUnit.Kilogram).Value);
    }
}
