using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Xunit;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventorySubcontractDeliveryContractTests
{
    [Fact]
    public void Customer_owner_and_company_custody_are_separate_from_the_company_account()
    {
        var company = Guid.NewGuid(); var customer = Guid.NewGuid(); var account = Guid.NewGuid();
        Assert.True(InventorySubcontractOwnershipGuardV1.IsCustomerOwnedAndHeld(StockPartyKind.Counterparty, customer,
            StockPartyKind.Company, company, company, customer));
        Assert.False(InventorySubcontractOwnershipGuardV1.IsCustomerOwnedAndHeld(StockPartyKind.Counterparty, account,
            StockPartyKind.Company, company, company, customer));
        Assert.False(InventorySubcontractOwnershipGuardV1.IsCustomerOwnedAndHeld(StockPartyKind.Counterparty, customer,
            StockPartyKind.Counterparty, customer, company, customer));
        Assert.False(InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld(StockPartyKind.Counterparty, customer,
            StockPartyKind.Company, company, company));
        Assert.True(InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld(StockPartyKind.Company, company,
            StockPartyKind.Company, company, company));
    }

    [Theory]
    [InlineData("")][InlineData("0")][InlineData("1.0000")][InlineData("1.0")][InlineData("1,5")]
    [InlineData("-1")][InlineData("1e3")][InlineData("+1")][InlineData(" 1")][InlineData("1000000000000000")]
    public void Noncanonical_or_nonpositive_kilograms_are_refused_without_rounding(string input)
    { Assert.Equal(400, Assert.Throws<InventoryDefiniteShipmentException>(() => InventorySubcontractDeliveryContractV1.Quantity(input)).Status); }

    [Theory]
    [InlineData("1")][InlineData("0.001")][InlineData("999999999999999.999")]
    public void Canonical_kilograms_keep_the_exact_owner_value(string input)
    { Assert.Equal(input, InventorySubcontractDeliveryContractV1.Quantity(input).ToString()); }

    [Theory]
    [InlineData("0")][InlineData("01")][InlineData(" 1")][InlineData("1.0")][InlineData("9223372036854775808")]
    public void Expected_versions_are_positive_canonical_int64(string input)
    { Assert.Equal(400, Assert.Throws<InventoryDefiniteShipmentException>(() => InventorySubcontractDeliveryContractV1.Version(input)).Status); }

    [Fact]
    public void Missing_or_foreign_source_and_unknown_provenance_are_unavailable_owner_proofs()
    {
        var source = Source();
        foreach (var invalid in new[] { source with { CompanyId = Guid.NewGuid() }, source with { LabelId = Guid.NewGuid() },
                     source with { SalesOrderVersion = 0 }, source with { ManufacturingOrderVersion = 0 },
                     source with { ManufacturingPlanVersion = 0 }, source with { CustomerAccountId = Guid.Empty },
                     source with { SourceVersion = "unknown" }, source with { OutputNetKg = "1.0" } })
            Assert.Equal(503, Assert.Throws<InventoryDefiniteShipmentException>(() =>
                InventorySubcontractDeliveryContractV1.Source(invalid, source.CompanyId, source.LabelId)).Status);
        InventorySubcontractDeliveryContractV1.Source(source, source.CompanyId, source.LabelId);
    }

    [Fact]
    public void Dedicated_source_and_event_never_name_normal_sales_definite_shipment_or_money()
    {
        Assert.NotEqual(InventoryAllocationDemandKindsV1.SalesOrderLine, InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine);
        Assert.NotEqual(InventoryDefiniteShipmentOutboxV1.EventType, InventorySubcontractDeliveryContractV1.EventType);
        var json = JsonSerializer.Serialize(Source(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var forbidden in new[] { "price", "cost", "amount", "currency", "discount", "tax" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    private static InventorySubcontractDeliverySourceV1 Source() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), 4, 2, Guid.NewGuid(), "product.v1", Guid.NewGuid(), 7, Guid.NewGuid(), 3, Guid.NewGuid(), Guid.NewGuid(), "10",
        InventorySubcontractDeliveryContractV1.SourceVersion);
}
