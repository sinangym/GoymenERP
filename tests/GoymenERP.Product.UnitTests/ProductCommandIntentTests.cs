using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductCommandIntentTests
{
    private static readonly Guid Group = Guid.Parse("6ab7edca-52ed-41b3-a2ca-6328cecc34a9");
    private static readonly Guid Product = Guid.Parse("5680b466-bdfa-420c-a8f1-1ba827f9d052");
    private const string Key = "test-product-attempt-1";

    [Fact]
    public void Equivalent_name_and_reason_whitespace_preserves_the_attempt()
    {
        var first = ProductCommandIntent.CreateDraft(Key, new("  Sac  ", Group));
        var same = ProductCommandIntent.CreateDraft(Key, new("Sac", Group));
        Assert.Equal(first.RequestHash, same.RequestHash);
        var edit = ProductCommandIntent.EditDraft(Key, Product, Edit("{\"name\":\"  Sac  \"}", "  Düzeltme  "));
        var sameEdit = ProductCommandIntent.EditDraft(Key, Product, Edit("{\"name\":\"Sac\"}"));
        Assert.Equal(edit.RequestHash, sameEdit.RequestHash);
    }

    [Fact]
    public void Reordered_nested_attribute_objects_preserve_hash_but_array_order_does_not()
    {
        var first = CreateWithAttributes("{\"b\":2,\"a\":{\"y\":[1,2],\"x\":3}}");
        var reordered = CreateWithAttributes("{\"a\":{\"x\":3,\"y\":[1,2]},\"b\":2}");
        var changed = CreateWithAttributes("{\"a\":{\"x\":3,\"y\":[2,1]},\"b\":2}");
        Assert.Equal(first.RequestHash, reordered.RequestHash);
        Assert.NotEqual(first.RequestHash, changed.RequestHash);
    }

    [Theory]
    [InlineData("{\"familyId\":null}")]
    [InlineData("{\"references\":{\"trackingPolicyId\":null}}")]
    [InlineData("{\"references\":{\"units\":{\"purchase\":null}}}")]
    [InlineData("{\"roles\":{\"sellable\":{\"value\":false,\"provenance\":\"manual\"}}}")]
    public void Explicit_changes_never_alias_an_omitted_edit(string changes)
    {
        var omitted = ProductCommandIntent.EditDraft(Key, Product, Edit("{}"));
        var explicitChange = ProductCommandIntent.EditDraft(Key, Product, Edit(changes));
        Assert.NotEqual(omitted.RequestHash, explicitChange.RequestHash);
    }

    [Fact]
    public void Target_version_and_reason_are_bound_without_converting_versions_to_double()
    {
        var request = Edit("{\"name\":\"Yeni\"}") with { ExpectedVersion = "9007199254740993" };
        var original = ProductCommandIntent.EditDraft(Key, Product, request);
        Assert.Contains("9007199254740993", original.CanonicalRequestJson);
        Assert.NotEqual(original.RequestHash,
            ProductCommandIntent.EditDraft(Key, Group, request).RequestHash);
        Assert.NotEqual(original.RequestHash,
            ProductCommandIntent.EditDraft(Key, Product, request with { ExpectedVersion = "9007199254740992" }).RequestHash);
        Assert.NotEqual(original.RequestHash,
            ProductCommandIntent.EditDraft(Key, Product, request with { Reason = "Başka gerekçe" }).RequestHash);
    }

    [Fact]
    public void Snapshot_is_frozen_before_a_callers_attribute_dictionary_changes()
    {
        var attributes = new Dictionary<string, JsonElement> { ["thickness"] = Element("1.23000000000000000001") };
        var request = new CreateProductDraftRequest("Sac", Group, References: new(Attributes: attributes));
        var frozen = ProductCommandIntent.CreateDraft(Key, request);
        attributes["thickness"] = Element("2");
        Assert.Contains("1.23000000000000000001", frozen.CanonicalRequestJson);
        Assert.NotEqual(frozen.RequestHash, ProductCommandIntent.CreateDraft(Key, request).RequestHash);
        Assert.DoesNotContain(Key, frozen.ToString());
        Assert.DoesNotContain("thickness", frozen.ToString());
    }

    [Fact]
    public void Duplicate_attribute_members_are_rejected_instead_of_hashed_ambiguously()
    {
        var exception = Assert.Throws<ProductValidationException>(() => CreateWithAttributes("{\"x\":1,\"x\":2}"));
        Assert.Contains(exception.Errors, error => error.Code == "invalid_metadata");
    }

    [Theory]
    [InlineData(" key")]
    [InlineData("key,another")]
    [InlineData("anahtar-ç")]
    public void Invalid_raw_key_is_not_trimmed_or_accepted(string key)
    {
        Assert.Throws<ProductValidationException>(() => ProductCommandIntent.CreateDraft(key, new("Sac", Group)));
    }

    [Fact]
    public void Different_key_changes_only_the_key_hash()
    {
        var one = ProductCommandIntent.CreateDraft(Key, new("Sac", Group));
        var two = ProductCommandIntent.CreateDraft("test-product-attempt-2", new("Sac", Group));
        Assert.NotEqual(one.KeyHash, two.KeyHash);
        Assert.Equal(one.RequestHash, two.RequestHash);
        Assert.Matches("^[0-9A-F]{64}$", one.RequestHash);
        Assert.Matches("^[0-9A-F]{64}$", one.KeyHash);
    }

    [Fact]
    public void Controls_in_name_or_reason_are_rejected_before_replay_equivalence()
    {
        Assert.Throws<ProductValidationException>(() => ProductCommandIntent.CreateDraft(Key, new("\nSac", Group)));
        Assert.Throws<ProductValidationException>(() => ProductCommandIntent.EditDraft(Key, Product, Edit("{}", "\nDüzeltme")));
    }

    private static ProductCommandIntent CreateWithAttributes(string json) => ProductCommandIntent.CreateDraft(Key,
        new("Sac", Group, References: new(Attributes: new Dictionary<string, JsonElement> { ["profile"] = Element(json) })));

    private static JsonElement Element(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static EditProductDraftRequest Edit(string changes, string reason = "Düzeltme") => new("1",
        JsonSerializer.Deserialize<ProductDraftChanges>(changes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!, reason);
}
