using GoymenERP.Modules.Product.Domain.Products;
using ProductCard = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductDraftTests
{
    [Fact]
    public void Basic_create_is_draft_even_without_activation_completeness()
    {
        var id = Guid.NewGuid();
        var clock = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.FromHours(3));
        var product = new ProductCard(id, ProductCode.FromSequence("SAC", 42), "  Siyah sac  ", clock);

        Assert.Equal(id, product.Id);
        Assert.Equal("Siyah sac", product.Name);
        Assert.Equal(ProductLifecycle.Draft, product.Lifecycle);
        Assert.Equal(1, product.Version);
        Assert.Equal(TimeSpan.Zero, product.CreatedAtUtc.Offset);
        Assert.Equal(clock, product.CreatedAtUtc);
    }

    [Fact]
    public void Default_code_cannot_create_an_unidentified_card()
    {
        Assert.Throws<ArgumentNullException>(() => new ProductCard(
            Guid.NewGuid(), default, "Sac", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Missing_identity_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ProductCard(
            Guid.Empty, ProductCode.FromSequence("SAC", 1), "Sac", DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_name_cannot_create_a_card(string name)
    {
        Assert.Throws<ArgumentException>(() => new ProductCard(
            Guid.NewGuid(), ProductCode.FromSequence("SAC", 1), name, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Unallocated_sequence_is_rejected(long sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductCode.FromSequence("SAC", sequence));
    }

    [Fact]
    public void Code_allocation_uses_the_supplied_group_without_a_default_group()
    {
        Assert.Equal("SAC-42", ProductCode.FromSequence(" sac ", 42).Value);
        Assert.Equal("BORU-42", ProductCode.FromSequence("BORU", 42).Value);
        Assert.Throws<ArgumentException>(() => ProductCode.FromSequence("SAC-1", 2));
    }
}
