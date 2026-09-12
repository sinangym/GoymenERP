using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using ProductCard = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductStoredSnapshotTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ProductLifecycle.Active, "Active")]
    [InlineData(ProductLifecycle.Inactive, "Inactive")]
    public void Existing_lifecycle_and_high_version_are_restored_for_history_reads(ProductLifecycle status, string wire)
    {
        var product = Restore(status, 9007199254740993L);
        var view = ProductDraftView.FromProduct(product);
        Assert.Equal(wire, view.Lifecycle);
        Assert.Equal("9007199254740993", view.Version);
        Assert.Throws<InvalidOperationException>(() => product.EditBasic(product.Version, "Yeni", null, null,
            ProductRoles.NotEvaluated, "İzin verilmeyen temel edit", CreatedAt.AddDays(2)));
        Assert.Equal(9007199254740993L, product.Version);
    }

    [Theory]
    [InlineData((ProductLifecycle)3, 1)]
    [InlineData(ProductLifecycle.Draft, 0)]
    public void Invalid_stored_status_or_version_is_rejected_before_projection(ProductLifecycle status, long version)
    {
        Assert.Throws<InvalidOperationException>(() => Restore(status, version));
    }

    [Fact]
    public void Invalid_stored_code_is_rejected_as_validation_not_null_dereference()
    {
        Assert.Null(default(ProductCode).Value);
        Assert.Throws<ArgumentNullException>(() => ProductCard.Restore(Guid.NewGuid(), null!, "Sac", Guid.NewGuid(),
            null, null, ProductRoles.NotEvaluated, ProductLifecycle.Draft, 1, CreatedAt, CreatedAt));
    }

    [Fact]
    public void Version_overflow_cannot_partially_change_product()
    {
        var product = Restore(ProductLifecycle.Draft, long.MaxValue);
        Assert.Throws<OverflowException>(() => product.EditBasic(long.MaxValue, "Değişmesin", null, null,
            ProductRoles.NotEvaluated, "Düzeltme", CreatedAt.AddDays(2)));
        Assert.Equal("Sac", product.Name);
        Assert.Equal(long.MaxValue, product.Version);
    }

    private static ProductCard Restore(ProductLifecycle status, long version) => ProductCard.Restore(
        Guid.NewGuid(), "SAC-1", "Sac", Guid.NewGuid(), null, null, ProductRoles.NotEvaluated,
        status, version, CreatedAt, CreatedAt.AddDays(1));
}
