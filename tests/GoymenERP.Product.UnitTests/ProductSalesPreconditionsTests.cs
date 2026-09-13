using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductSalesPreconditionsTests
{
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid UnitId = Guid.NewGuid();
    private static ProductSalesSelectionV1 Selection => new(ProductId, "9007199254740993", UnitId, new(2026, 9, 13));

    [Theory]
    [InlineData("Draft")]
    [InlineData("Inactive")]
    [InlineData("unknown")]
    public void Complete_sellable_card_does_not_bypass_lifecycle(string lifecycle)
    {
        var card = Card();
        card = card with { Product = card.Product with { Lifecycle = lifecycle } };
        Assert.Contains(ProductSalesPreconditions.Check(card, Selection), e => e.Code == "lifecycle_conflict");
    }

    [Theory]
    [InlineData(null, "not-evaluated")]
    [InlineData(false, "manual")]
    [InlineData(false, "derived")]
    [InlineData(true, "unknown")]
    public void Active_card_requires_known_positive_sales_role(bool? value, string provenance)
    {
        var card = Card();
        card = card with { Product = card.Product with { Roles = card.Product.Roles with { Sellable = new(value, provenance) } } };
        Assert.Contains(ProductSalesPreconditions.Check(card, Selection), e => e.Code == "not_sellable");
    }

    [Fact]
    public void Adjacent_versions_above_javascript_precision_are_not_equal()
    {
        var errors = ProductSalesPreconditions.Check(Card(), Selection with { ExpectedProductVersion = "9007199254740992" });
        Assert.Contains(errors, e => e.Code == "version_conflict");
    }

    [Theory]
    [InlineData("01")]
    [InlineData("1e3")]
    [InlineData("9223372036854775808")]
    public void Malformed_versions_are_rejected(string version) => Assert.Contains(
        ProductSalesPreconditions.Check(Card(), Selection with { ExpectedProductVersion = version }), e => e.Code == "invalid_version");

    [Fact]
    public void Different_product_is_not_a_valid_selection() => Assert.Contains(
        ProductSalesPreconditions.Check(Card(), Selection with { ProductId = Guid.NewGuid() }), e => e.Field == "productId");

    [Fact]
    public void Complete_label_does_not_supply_missing_units()
    {
        var card = Card() with { References = new() };
        var errors = ProductSalesPreconditions.Check(card, Selection with { OrderUnitId = Guid.Empty });
        Assert.Contains(errors, e => e.Field == "references.units.stock");
        Assert.Contains(errors, e => e.Field == "references.units.sales");
        Assert.Contains(errors, e => e.Field == "orderUnitId");
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("derived")]
    public void Local_conditions_only_allow_next_owner_validation(string provenance)
    {
        var card = Card();
        card = card with { Product = card.Product with { Roles = card.Product.Roles with { Sellable = new(true, provenance) } } };
        Assert.Empty(ProductSalesPreconditions.Check(card, Selection));
        // No eligibility snapshot is produced and the original card remains unchanged.
        Assert.Equal("9007199254740993", card.Product.Version);
    }

    private static ProductCardView Card() => new(new(ProductId, "TEST-1", "Sac", Guid.NewGuid(), null, null,
        new(new(null, "not-evaluated"), new(true, "manual"), new(null, "not-evaluated"), new(null, "not-evaluated")),
        "Active", "9007199254740993", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        new(null, null, null, null), new("Complete", [])), new(Units: new(Stock: UnitId, Sales: UnitId)));
}
