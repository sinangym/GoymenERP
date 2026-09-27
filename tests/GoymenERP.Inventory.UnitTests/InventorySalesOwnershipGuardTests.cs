using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventorySalesOwnershipGuardTests
{
    private static readonly Guid Company = InventoryFixture.CompanyA;

    [Fact]
    public void Only_stock_the_company_both_owns_and_holds_passes_the_Sales_gate()
    {
        Assert.True(InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld(
            StockPartyKind.Company, Company, StockPartyKind.Company, Company, Company));
    }

    [Theory]
    [InlineData("customer-owned, company-held")]
    [InlineData("company-owned, fason-held")]
    [InlineData("customer-owned, fason-held")]
    [InlineData("another company owns")]
    [InlineData("another company holds")]
    [InlineData("unresolved company")]
    public void Any_other_owner_or_custodian_is_refused(string shape)
    {
        var (ownerKind, ownerId, custodianKind, custodianId, company) = shape switch
        {
            "customer-owned, company-held" =>
                (StockPartyKind.Counterparty, InventoryFixture.CustomerC, StockPartyKind.Company, Company, Company),
            "company-owned, fason-held" =>
                (StockPartyKind.Company, Company, StockPartyKind.Counterparty, InventoryFixture.FasonF, Company),
            "customer-owned, fason-held" =>
                (StockPartyKind.Counterparty, InventoryFixture.CustomerC, StockPartyKind.Counterparty,
                    InventoryFixture.FasonF, Company),
            "another company owns" =>
                (StockPartyKind.Company, InventoryFixture.CompanyB, StockPartyKind.Company, Company, Company),
            "another company holds" =>
                (StockPartyKind.Company, Company, StockPartyKind.Company, InventoryFixture.CompanyB, Company),
            _ => (StockPartyKind.Company, Guid.Empty, StockPartyKind.Company, Guid.Empty, Guid.Empty),
        };

        Assert.False(InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld(
            ownerKind, ownerId, custodianKind, custodianId, company));
    }

    [Fact]
    public void Refusal_codes_are_named_and_on_the_existing_closed_channels()
    {
        Assert.Equal("inventory_allocation_owner_not_company", InventoryAllocationCodesV1.OwnerNotCompany);
        Assert.Equal("inventory_definite_shipment_owner_not_company", InventoryDefiniteShipmentCodesV1.OwnerNotCompany);
        Assert.True(InventoryDefiniteShipmentCodesV1.IsDefined(409, InventoryDefiniteShipmentCodesV1.OwnerNotCompany));
        Assert.Equal((409, InventoryDefiniteShipmentCodesV1.OwnerNotCompany),
            (InventoryDefiniteShipmentException.Refused(409, InventoryDefiniteShipmentCodesV1.OwnerNotCompany).Status,
                InventoryDefiniteShipmentException.Refused(409, InventoryDefiniteShipmentCodesV1.OwnerNotCompany).Code));
    }
}
