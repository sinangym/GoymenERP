using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchasingCommercialLineSubjectContractTests
{
    [Theory]
    [InlineData(PurchasingCommercialLineSubjectKindV1.Product, "Product")]
    [InlineData(PurchasingCommercialLineSubjectKindV1.Service, "Service")]
    [InlineData(PurchasingCommercialLineSubjectKindV1.Consumable, "Consumable")]
    public void Reference_round_trips_one_discriminated_identity(
        PurchasingCommercialLineSubjectKindV1 kind,
        string wireKind)
    {
        var id = Guid.Parse("11000000-0000-0000-0000-000000000001");
        var reference = new PurchasingCommercialLineSubjectReferenceV1(kind, id, "subject-v7");

        var json = JsonSerializer.Serialize(reference, PurchasingJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(wireKind, document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(id, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("subject-v7", document.RootElement.GetProperty("expectedVersion").GetString());
        Assert.Equal(3, document.RootElement.EnumerateObject().Count());

        Assert.Equal(reference,
            JsonSerializer.Deserialize<PurchasingCommercialLineSubjectReferenceV1>(json, PurchasingJson.Options));
    }

    [Fact]
    public void Reference_is_strict_and_does_not_carry_commercial_or_effect_fields()
    {
        var properties = typeof(PurchasingCommercialLineSubjectReferenceV1)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ExpectedVersion", "Id", "Kind"], properties);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PurchasingCommercialLineSubjectReferenceV1>(
            """
            {
              "kind": "Service",
              "id": "11000000-0000-0000-0000-000000000001",
              "expectedVersion": "subject-v7",
              "stockEffect": "Receipt"
            }
            """,
            PurchasingJson.Options));
    }

    [Fact]
    public void Snapshot_contains_only_owner_identity_and_presentation()
    {
        var properties = typeof(PurchasingCommercialLineSubjectSnapshotV1)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Code", "Id", "Kind", "Name", "Version"], properties);
    }

    [Fact]
    public void Legacy_product_lines_remain_product_only()
    {
        foreach (var type in new[] { typeof(PurchaseRequestLineRequest), typeof(PurchaseOrderLineRequest) })
        {
            var properties = type.GetProperties()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("ProductId", properties);
            Assert.Contains("ExpectedProductVersion", properties);
            Assert.DoesNotContain("Subject", properties);
        }
    }
}
