using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesCommercialLineSubjectContractTests
{
    private static readonly Guid SubjectId = Guid.Parse("11000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(SalesCommercialLineSubjectKindV1.Product, "Product")]
    [InlineData(SalesCommercialLineSubjectKindV1.Service, "Service")]
    [InlineData(SalesCommercialLineSubjectKindV1.Consumable, "Consumable")]
    public void Reference_round_trips_one_discriminated_identity(
        SalesCommercialLineSubjectKindV1 kind,
        string wireKind)
    {
        var reference = new SalesCommercialLineSubjectReferenceV1(kind, SubjectId, "subject-v7");

        var json = JsonSerializer.Serialize(reference, SalesDraftJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(wireKind, document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(SubjectId, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("subject-v7", document.RootElement.GetProperty("expectedVersion").GetString());
        Assert.Equal(3, document.RootElement.EnumerateObject().Count());

        Assert.Equal(reference,
            JsonSerializer.Deserialize<SalesCommercialLineSubjectReferenceV1>(json, SalesDraftJson.Options));
    }

    [Theory]
    [InlineData(SalesCommercialLineSubjectKindV1.Product, "Product")]
    [InlineData(SalesCommercialLineSubjectKindV1.Service, "Service")]
    [InlineData(SalesCommercialLineSubjectKindV1.Consumable, "Consumable")]
    public void Snapshot_strict_json_round_trips_owner_presentation(
        SalesCommercialLineSubjectKindV1 kind,
        string wireKind)
    {
        var snapshot = new SalesCommercialLineSubjectSnapshotV1(
            kind, SubjectId, "subject-v8", "SUB-01", "Subject one");

        var json = JsonSerializer.Serialize(snapshot, SalesDraftJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal(wireKind, document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(SubjectId, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("subject-v8", document.RootElement.GetProperty("version").GetString());
        Assert.Equal("SUB-01", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("Subject one", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(5, document.RootElement.EnumerateObject().Count());

        Assert.Equal(snapshot,
            JsonSerializer.Deserialize<SalesCommercialLineSubjectSnapshotV1>(json, SalesDraftJson.Options));
    }

    [Fact]
    public void Discriminator_rejects_numeric_and_undefined_values_for_read_and_write()
    {
        var numericJson = "{\"kind\":1,\"id\":\"" + SubjectId + "\",\"expectedVersion\":\"subject-v7\"}";
        var unknownJson = "{\"kind\":\"Unknown\",\"id\":\"" + SubjectId
            + "\",\"expectedVersion\":\"subject-v7\"}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SalesCommercialLineSubjectReferenceV1>(
            numericJson,
            SalesDraftJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SalesCommercialLineSubjectReferenceV1>(
            unknownJson,
            SalesDraftJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            (SalesCommercialLineSubjectKindV1)999,
            SalesDraftJson.Options));
    }

    [Fact]
    public void Identity_rejects_empty_id_blank_version_and_undefined_kind()
    {
        Assert.Throws<ArgumentException>(() => new SalesCommercialLineSubjectReferenceV1(
            SalesCommercialLineSubjectKindV1.Product, Guid.Empty, "subject-v7"));
        Assert.Throws<ArgumentException>(() => new SalesCommercialLineSubjectReferenceV1(
            SalesCommercialLineSubjectKindV1.Product, SubjectId, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SalesCommercialLineSubjectReferenceV1(
            (SalesCommercialLineSubjectKindV1)999, SubjectId, "subject-v7"));

        Assert.Throws<ArgumentException>(() => new SalesCommercialLineSubjectSnapshotV1(
            SalesCommercialLineSubjectKindV1.Service, Guid.Empty, "subject-v8", "SUB-01", "Subject one"));
        Assert.Throws<ArgumentException>(() => new SalesCommercialLineSubjectSnapshotV1(
            SalesCommercialLineSubjectKindV1.Service, SubjectId, "", "SUB-01", "Subject one"));
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
