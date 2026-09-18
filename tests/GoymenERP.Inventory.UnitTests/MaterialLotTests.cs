using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Lots;
using static GoymenERP.Inventory.UnitTests.InventoryFixture;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>The minimal lot record (DEC-PRD-003; Business #66 5734060341). Synthetic identities only.</summary>
public sealed class MaterialLotTests
{
    [Fact]
    public void A_lot_is_one_company_and_one_product_with_heat_and_coil_only_when_known()
    {
        var lot = new MaterialLot(Lot1, CompanyA, BandProduct);

        Assert.Equal((Lot1, CompanyA, BandProduct), (lot.Id, lot.CompanyId, lot.ProductId));
        // Unknown is null: never an empty text, a zero or a generated value (Ö3).
        Assert.Null(lot.HeatNumber);
        Assert.Null(lot.CoilNumber);
    }

    [Fact]
    public void Heat_and_coil_are_kept_as_given_trimmed_and_blank_means_unknown()
    {
        var known = new MaterialLot(Lot1, CompanyA, BandProduct, heatNumber: " SYN-HEAT ", coilNumber: "SYN-COIL");
        var blank = new MaterialLot(Lot1, CompanyA, BandProduct, heatNumber: "   ", coilNumber: "");

        Assert.Equal(("SYN-HEAT", "SYN-COIL"), (known.HeatNumber, known.CoilNumber));
        Assert.Equal(((string?)null, (string?)null), (blank.HeatNumber, blank.CoilNumber));
    }

    [Theory]
    [InlineData("lot")]
    [InlineData("company")]
    [InlineData("product")]
    public void Every_identity_is_required(string missing)
    {
        var exception = Assert.Throws<InventoryRuleException>(() => new MaterialLot(
            missing == "lot" ? Guid.Empty : Lot1,
            missing == "company" ? Guid.Empty : CompanyA,
            missing == "product" ? Guid.Empty : BandProduct));

        Assert.Equal(InventoryRuleCodes.MissingReference, exception.Code);
    }
}
