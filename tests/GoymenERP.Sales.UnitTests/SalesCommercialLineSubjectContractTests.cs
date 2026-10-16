using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesCommercialLineSubjectContractTests
{
    [Theory]
    [InlineData(SalesCommercialLineSubjectKindV1.Product, "Product")]
    [InlineData(SalesCommercialLineSubjectKindV1.Service, "Service")]
    [InlineData(SalesCommercialLineSubjectKindV1.Consumable, "Consumable")]
    public void Reference_round_trips_one_discriminated_identity(
        SalesCommercialLineSubjectKindV1 kind,
        string wireKind)
    {
        var id = Guid.Parse("11000000-0000-0000-0000-000000000001");
        var reference = new SalesCommercialLineSubjectReferenceV1(kind, id, "subject-v7");

        var json = JsonSerializer.Serialize(reference, SalesDraftJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(wireKind, document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(id, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("subject-v7", document.RootElement.GetProperty("expectedVersion").GetString());
        Assert.Equal(3, document.RootElement.EnumerateObject().Count());

        Assert.Equal(reference,
            JsonSerializer.Deserialize<SalesCommercialLineSubjectReferenceV1>(json, SalesDraftJson.Options));
    }

    [Fact]
    public void Reference_is_strict_and_does_not_carry_commercial_or_effect_fields()
    {
        var properties = typeof(SalesCommercialLineSubjectReferenceV1)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ExpectedVersion", "Id", "Kind"], properties);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SalesCommercialLineSubjectReferenceV1>(
            """
            {
              "kind": "Service",
              "id": "11000000-0000-0000-0000-000000000001",
              "expectedVersion": "subject-v7",
              "quantity": "1"
            }
            """,
            SalesDraftJson.Options));
    }

    [Fact]
    public void Snapshot_contains_only_owner_identity_and_presentation()
    {
        var properties = typeof(SalesCommercialLineSubjectSnapshotV1)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Code", "Id", "Kind", "Name", "Version"], properties);
    }

    [Fact]
    public void Legacy_product_line_contract_remains_product_only()
    {
        var properties = typeof(SalesOrderDraftLineRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ProductId", properties);
        Assert.Contains("ExpectedProductVersion", properties);
        Assert.DoesNotContain("Subject", properties);
    }
}
