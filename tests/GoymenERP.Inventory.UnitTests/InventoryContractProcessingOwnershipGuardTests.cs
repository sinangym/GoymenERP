using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>LOJ-D2/D5 (STK-S3): a subcontract delivery ships only the delivering counterparty's goods held by the company.</summary>
public sealed class InventoryContractProcessingOwnershipGuardTests
{
    private static readonly Guid Company = InventoryFixture.CompanyA;
    private static readonly Guid Customer = InventoryFixture.CustomerC;

    [Fact]
    public void Only_the_counterpartys_own_goods_held_by_the_company_pass_the_subcontract_gate()
    {
        Assert.True(InventoryContractProcessingOwnershipGuardV1.IsCounterpartyOwnedAndCompanyHeld(
            StockPartyKind.Counterparty, Customer, StockPartyKind.Company, Company, Company, Customer));
    }

    [Theory]
    [InlineData("company-owned")]
    [InlineData("another counterparty owns")]
    [InlineData("held by a third party")]
    [InlineData("held by another company")]
    [InlineData("counterparty is the company")]
    [InlineData("unresolved company")]
    [InlineData("unresolved counterparty")]
    public void Any_other_owner_or_custodian_is_refused(string shape)
    {
        var (ownerKind, ownerId, custodianKind, custodianId, company, counterparty) = shape switch
        {
            "company-owned" =>
                (StockPartyKind.Company, Company, StockPartyKind.Company, Company, Company, Customer),
            "another counterparty owns" =>
                (StockPartyKind.Counterparty, InventoryFixture.FasonF, StockPartyKind.Company, Company, Company, Customer),
            "held by a third party" =>
                (StockPartyKind.Counterparty, Customer, StockPartyKind.Counterparty, InventoryFixture.FasonF, Company, Customer),
            "held by another company" =>
                (StockPartyKind.Counterparty, Customer, StockPartyKind.Company, InventoryFixture.CompanyB, Company, Customer),
            "counterparty is the company" =>
                (StockPartyKind.Counterparty, Company, StockPartyKind.Company, Company, Company, Company),
            "unresolved company" =>
                (StockPartyKind.Counterparty, Customer, StockPartyKind.Company, Guid.Empty, Guid.Empty, Customer),
            _ => (StockPartyKind.Counterparty, Guid.Empty, StockPartyKind.Company, Company, Company, Guid.Empty),
        };

        Assert.False(InventoryContractProcessingOwnershipGuardV1.IsCounterpartyOwnedAndCompanyHeld(
            ownerKind, ownerId, custodianKind, custodianId, company, counterparty));
    }

    [Fact]
    public void The_subcontract_gate_and_the_Sales_gate_never_both_pass_the_same_position()
    {
        Assert.False(InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld(
            StockPartyKind.Counterparty, Customer, StockPartyKind.Company, Company, Company));
        Assert.False(InventoryContractProcessingOwnershipGuardV1.IsCounterpartyOwnedAndCompanyHeld(
            StockPartyKind.Company, Company, StockPartyKind.Company, Company, Company, Customer));
    }
}
