using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using ProductEntity = GoymenERP.Modules.Product.Domain.Products.Product;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductPersistenceBoundaryTests
{
    [Fact]
    public void Serialized_edit_state_rehydrates_the_same_identity_roles_and_concurrency_version()
    {
        var product = Draft();
        product.EditBasic(1, "Düzeltilmiş sac", null, Guid.NewGuid(), product.Roles,
            "Ad düzeltildi", product.CreatedAtUtc.AddMinutes(1));
        var record = ProductStoredRecord.Capture(product, new());
        var restoredRecord = JsonSerializer.Deserialize<ProductStoredRecord>(JsonSerializer.Serialize(record))!;
        var restored = restoredRecord.RestoreProduct();
        Assert.Equal(product.Id, restored.Id);
        Assert.Equal(product.Code, restored.Code);
        Assert.Equal(product.Name, restored.Name);
        Assert.Equal(product.FamilyId, restored.FamilyId);
        Assert.Equal(product.UpdatedAtUtc, restored.UpdatedAtUtc);
        Assert.Equal(2, restored.Version);
        Assert.False(restored.Roles.Sellable.Value);
        Assert.Null(restored.Roles.Purchasable.Value);
        Assert.Equal(ProductRoleProvenance.Derived, restored.Roles.ProductionOutput.Provenance);
        Assert.Throws<ProductVersionConflictException>(() => restored.EditBasic(1, "Stale", null, null,
            restored.Roles, "Stale", restored.UpdatedAtUtc));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(99, false)]
    public void Corrupt_stored_role_pair_cannot_become_an_unknown_or_false_value(int provenance, bool? value)
    {
        var record = ProductStoredRecord.Capture(Draft(), new()) with { Sellable = new(value, provenance) };
        Assert.Throws<InvalidOperationException>(() => record.RestoreProduct());
    }

    [Fact]
    public void Stored_reference_snapshot_is_detached_from_mutable_attributes()
    {
        var attributes = new Dictionary<string, JsonElement>
        {
            ["thickness"] = JsonSerializer.Deserialize<JsonElement>("1.23000000000000000001"),
        };
        var unit = Guid.NewGuid();
        var references = new ProductReferences(Guid.NewGuid(), "9007199254740993", attributes,
            new(Stock: unit, Purchase: Guid.NewGuid()), Guid.NewGuid());
        var record = ProductStoredRecord.Capture(Draft(), references);
        attributes.Clear();
        var stored = record.RestoreReferences();
        Assert.Equal("1.23000000000000000001", stored.Attributes!["thickness"].GetRawText());
        Assert.Equal("9007199254740993", stored.TemplateVersion);
        Assert.Equal(unit, stored.Units!.Stock);
        Assert.NotEqual(stored.Units.Stock, stored.Units.Purchase);
        Assert.Equal(references.TrackingPolicyId, stored.TrackingPolicyId);
        Assert.DoesNotContain("thickness", record.ToString());
    }

    [Theory]
    [InlineData("null", typeof(InvalidOperationException))]
    [InlineData("{\"stockQuantity\":42}", typeof(JsonException))]
    [InlineData("{broken", typeof(JsonException))]
    public void Missing_or_malformed_stored_references_are_never_reported_as_empty(string json, Type errorType)
    {
        var record = ProductStoredRecord.Capture(Draft(), new()) with { ReferencesJson = json };
        Assert.Throws(errorType, () => record.RestoreReferences());
    }

    [Fact]
    public void Legacy_code_registration_preserves_storage_compatible_identity_and_rejects_lossy_unicode()
    {
        var row = ProductRow.FromRecord(ProductStoredRecord.Capture(Draft(), new()));

        var registration = ProductCodeRegistration.Legacy(row, " OLD-É-€ ");

        Assert.Equal("OLD-É-€", registration.Code);
        Assert.Throws<ArgumentException>(() => ProductCodeRegistration.Legacy(row, "OLD-旧"));
    }

    private static ProductEntity Draft() => ProductEntity.CreateDraft(Guid.NewGuid(), ProductCode.FromSequence("TEST", 1),
        "Sac", Guid.NewGuid(), null, null,
        new(ProductRoleState.NotEvaluated, ProductRoleState.Manual(false), ProductRoleState.Manual(true),
            ProductRoleState.Derived(true)), DateTimeOffset.Parse("2026-09-12T15:00:00Z"));
}
