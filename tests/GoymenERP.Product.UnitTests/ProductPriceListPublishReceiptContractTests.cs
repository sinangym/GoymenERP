using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductPriceListPublishReceiptContractTests
{
    [Fact]
    public void Shared_publish_fingerprint_is_exactly_the_existing_native_serialization_without_public_receipt_fields()
    {
        var id = Guid.NewGuid(); var request = new PublishPriceListVersionRequestV1("7", 2);
        var original = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { command = ProductPriceListCommandsV1.PublishV1, priceListId = id, request }, ProductPriceListWire.Json)))).ToLowerInvariant();
        Assert.Equal(original, ProductPriceListWire.PublishRequestHash(id, request));
        var company = Guid.NewGuid(); var at = DateTimeOffset.UtcNow;
        var view = new PriceListViewV1(id, "SENTETIK", "TRY", "Sales", "ExcludingVat", "8", at, at, null, null, [], PriceListScopeViewV1.Company);
        var bare = new PriceListResultV1(view, false, "corr");
        var proven = bare with { PublishReceipt = new(company, id, Guid.NewGuid(), 2, "7", "8", ProductPriceListCommandsV1.PublishV1,
            "SENTETIK", new string('A', 64), original, at, "SENTETIK", "corr") };
        Assert.Equal(JsonSerializer.Serialize(bare, ProductPriceListWire.Json), JsonSerializer.Serialize(proven, ProductPriceListWire.Json));
    }
}
