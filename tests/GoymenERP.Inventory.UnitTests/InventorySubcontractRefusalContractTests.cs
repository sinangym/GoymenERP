using GoymenERP.Modules.Inventory.Application;
using Xunit;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventorySubcontractRefusalContractTests
{
    [Theory]
    [InlineData(400, InventorySubcontractDeliveryContractV1.Invalid)]
    [InlineData(404, InventorySubcontractDeliveryContractV1.SourceMismatch)]
    [InlineData(409, InventorySubcontractDeliveryContractV1.OwnerMismatch)]
    [InlineData(409, InventorySubcontractDeliveryContractV1.SourceMismatch)]
    [InlineData(503, InventorySubcontractDeliveryContractV1.Unavailable)]
    public void Subcontract_refusals_remain_typed_without_widening_the_normal_sale_contract(int status, string code)
    {
        var refusal = InventoryDefiniteShipmentException.RefusedSubcontract(status, code);
        Assert.Equal(status, refusal.Status); Assert.Equal(code, refusal.Code);
        Assert.Throws<InvalidOperationException>(() => InventoryDefiniteShipmentException.Refused(status, code));
    }

    [Theory]
    [InlineData(200, InventorySubcontractDeliveryContractV1.Unavailable)]
    [InlineData(409, InventorySubcontractDeliveryContractV1.Invalid)]
    [InlineData(422, InventorySubcontractDeliveryContractV1.OwnerMismatch)]
    public void An_unregistered_subcontract_status_code_pair_never_becomes_a_business_success(int status, string code) =>
        Assert.Throws<InvalidOperationException>(() => InventoryDefiniteShipmentException.RefusedSubcontract(status, code));
}
