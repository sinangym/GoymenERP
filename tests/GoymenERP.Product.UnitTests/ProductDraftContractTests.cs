using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using ProductCard = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductDraftContractTests
{
    private static ProductCard Create(Guid id, ProductCode code, string name, DateTimeOffset now) =>
        ProductCard.CreateDraft(id, code, name, Guid.NewGuid(), null, null, ProductRoles.NotEvaluated, now);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("companyId", "\"b4fc7672-0b46-41ba-a017-393401ba61dd\"")]
    [InlineData("ownerCompanyId", "\"b4fc7672-0b46-41ba-a017-393401ba61dd\"")]
    [InlineData("lifecycle", "\"active\"")]
    [InlineData("code", "\"SAC-1\"")]
    [InlineData("version", "\"1\"")]
    [InlineData("isProductionInput", "true")]
    [InlineData("lotDefect", "\"surface\"")]
    public void Create_cannot_mass_assign_ownership_lifecycle_or_physical_fields(string field, string value)
    {
        var json = $$"""{"name":"Sac","groupId":"f4fc7672-0b46-41ba-a017-393401ba61dd","{{field}}":{{value}}} """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateProductDraftRequest>(json, JsonOptions));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"Sac\"}")]
    [InlineData("{\"groupId\":\"f4fc7672-0b46-41ba-a017-393401ba61dd\"}")]
    public void Create_requires_identity_input_fields(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateProductDraftRequest>(json, JsonOptions));
    }

    [Fact]
    public void Response_uses_string_version_and_explicit_draft_status()
    {
        var product = Create(Guid.NewGuid(), ProductCode.FromSequence("SAC", 42),
            "Sac", DateTimeOffset.UtcNow);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ProductDraftView.FromProduct(product), JsonOptions));

        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("version").ValueKind);
        Assert.Equal("1", json.RootElement.GetProperty("version").GetString());
        Assert.Equal("Draft", json.RootElement.GetProperty("lifecycle").GetString());
        Assert.False(json.RootElement.TryGetProperty("companyId", out _));
        Assert.False(json.RootElement.TryGetProperty("stockQuantity", out _));
    }
}
