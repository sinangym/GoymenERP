using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Work.UnitTests;

public sealed class WorkPriceListMakerExceptionTests
{
    [Theory]
    [InlineData("product", "product.price-list-version.v1", false)]
    [InlineData("Product", "product.price-list-version.v1", true)]
    [InlineData("finance", "product.price-list-version.v1", true)]
    [InlineData("product", "product.package-standard-version.v1", true)]
    [InlineData("product", "product.price-list-version.v2", true)]
    [InlineData("inventory", "inventory.stock-count.v1", true)]
    [InlineData("finance", "finance.bank-account-iban-change.v1", true)]
    public void Rol_D18_is_an_exact_owner_kind_exception_without_erasing_creator_or_editor_metadata(string module, string kind, bool excluded)
    {
        var item = new WorkInboxItemV1(module, kind, Guid.NewGuid(), "SENTETIK", DateTimeOffset.UtcNow, "/SENTETIK", "SENTETIK-maker", [], [], ["SENTETIK-editor"]);
        Assert.Equal(excluded, item.Excludes("SENTETIK-maker"));
        Assert.Equal(excluded, item.Excludes("SENTETIK-editor"));
        Assert.False(item.Excludes("SENTETIK-other"));
        Assert.Equal("SENTETIK-maker", item.PreparedBy); Assert.Equal(["SENTETIK-editor"], item.ExcludedDeciders);
    }
}
