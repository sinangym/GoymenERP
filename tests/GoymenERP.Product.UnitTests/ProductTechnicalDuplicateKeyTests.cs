using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// URN-D25 (Sinan 2026-11-21): the technical duplicate identity is the family plus the template's thickness, width, quality and
/// coating values, exactly. SENTETIK cards on the released GDC flat-coil definition.
/// </summary>
public sealed class ProductTechnicalDuplicateKeyTests
{
    private static readonly ProductFamilyDefinitionV2 Definition = ProductInitialTechnicalDefinitionV1.Definition();
    private static readonly Guid Coil = ProductInitialTechnicalDefinitionV1.HotRolledCoilFamilyId;
    private static readonly Guid ColdCoil = ProductInitialTechnicalDefinitionV1.ColdRolledCoilFamilyId;

    private static ProductReferences Card(object attributes) => new(Attributes:
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(attributes)));

    private static readonly object Base = new Dictionary<string, object>
    {
        ["thickness-mm"] = 2.5m, ["width-mm"] = 1250m, ["steel-grade"] = "S235JR", ["coating"] = "uncoated",
        ["producer-grade"] = "erdemir:3237",
    };

    private static object With(string key, object? value)
    {
        var copy = ((Dictionary<string, object>)Base).ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        copy[key] = value;
        return copy;
    }

    [Fact]
    public void Cards_of_one_family_with_the_same_five_values_share_a_key_whatever_else_differs()
    {
        var first = ProductTechnicalDuplicateKeyV1.Of(Coil, Card(Base), Definition);
        // Another producer grade and 2.50 written with a trailing zero: still the same five values.
        var second = ProductTechnicalDuplicateKeyV1.Of(Coil, Card(new Dictionary<string, object>
        {
            ["thickness-mm"] = 2.50m, ["width-mm"] = 1250.0m, ["steel-grade"] = "S235JR", ["coating"] = "uncoated",
            ["producer-grade"] = "tosyali:TOS3780",
        }), Definition);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("thickness-mm", "3")]
    [InlineData("width-mm", "1500")]
    [InlineData("steel-grade", "S275JR")]
    [InlineData("coating", "galvanized")]
    public void One_different_value_is_a_different_key(string key, string value)
    {
        object changed = key.EndsWith("-mm", StringComparison.Ordinal) ? decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : value;
        Assert.NotEqual(ProductTechnicalDuplicateKeyV1.Of(Coil, Card(Base), Definition),
            ProductTechnicalDuplicateKeyV1.Of(Coil, Card(With(key, changed)), Definition));
    }

    [Fact]
    public void Another_family_is_never_the_same_product_even_with_equal_values()
    {
        Assert.NotEqual(ProductTechnicalDuplicateKeyV1.Of(Coil, Card(Base), Definition),
            ProductTechnicalDuplicateKeyV1.Of(ColdCoil, Card(Base), Definition));
    }

    [Theory]
    [InlineData("thickness-mm")]
    [InlineData("width-mm")]
    [InlineData("steel-grade")]
    [InlineData("coating")]
    public void A_missing_or_blank_value_gives_no_key_so_nothing_is_matched_on_technical_grounds(string key)
    {
        Assert.Null(ProductTechnicalDuplicateKeyV1.Of(Coil, Card(With(key, null)), Definition));
        Assert.Null(ProductTechnicalDuplicateKeyV1.Of(Coil, Card(With(key, key.EndsWith("-mm", StringComparison.Ordinal) ? 0m : " ")),
            Definition));
    }

    [Fact]
    public void No_family_no_definition_or_an_unknown_family_gives_no_key()
    {
        Assert.Null(ProductTechnicalDuplicateKeyV1.Of(null, Card(Base), Definition));
        Assert.Null(ProductTechnicalDuplicateKeyV1.Of(Coil, Card(Base), null));
        Assert.Null(ProductTechnicalDuplicateKeyV1.Of(Guid.NewGuid(), Card(Base), Definition));
        Assert.Null(ProductTechnicalDuplicateKeyV1.Of(Coil, new ProductReferences(), Definition));
    }
}
