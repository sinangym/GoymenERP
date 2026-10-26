using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

public sealed class PurchasingCommercialLineSubjectKindJsonConverterV1
    : JsonConverter<PurchasingCommercialLineSubjectKindV1>
{
    public override PurchasingCommercialLineSubjectKindV1 Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Commercial line subject kind must be a string.");
        }

        return reader.GetString() switch
        {
            "Product" => PurchasingCommercialLineSubjectKindV1.Product,
            "Service" => PurchasingCommercialLineSubjectKindV1.Service,
            "Consumable" => PurchasingCommercialLineSubjectKindV1.Consumable,
            _ => throw new JsonException("Unknown commercial line subject kind."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        PurchasingCommercialLineSubjectKindV1 value,
        JsonSerializerOptions options)
    {
        var wireValue = value switch
        {
            PurchasingCommercialLineSubjectKindV1.Product => "Product",
            PurchasingCommercialLineSubjectKindV1.Service => "Service",
            PurchasingCommercialLineSubjectKindV1.Consumable => "Consumable",
            _ => throw new JsonException("Unknown commercial line subject kind."),
        };

        writer.WriteStringValue(wireValue);
    }
}

/// <summary>
/// The identity grain of a commercial Purchasing line. Quantity, unit, price and every
/// physical or financial effect are separate contracts. This type does not authorize
/// a Product, Service or Consumable and is not an owner eligibility answer.
/// </summary>
[JsonConverter(typeof(PurchasingCommercialLineSubjectKindJsonConverterV1))]
public enum PurchasingCommercialLineSubjectKindV1
{
    Product,
    Service,
    Consumable,
}

/// <summary>
/// A client-observed owner identity and version. The future owner port must re-read
/// current authority and version before a command stores its immutable snapshot.
/// Structurally, one discriminator addresses exactly one identity; there are no
/// parallel nullable Product, Service or Consumable ids.
/// </summary>
public sealed record PurchasingCommercialLineSubjectReferenceV1
{
    [JsonConstructor]
    public PurchasingCommercialLineSubjectReferenceV1(
        PurchasingCommercialLineSubjectKindV1 kind,
        Guid id,
        string expectedVersion)
    {
        RequireIdentity(kind, id, expectedVersion);
        Kind = kind;
        Id = id;
        ExpectedVersion = expectedVersion;
    }

    public PurchasingCommercialLineSubjectKindV1 Kind { get; }
    public Guid Id { get; }
    public string ExpectedVersion { get; }

    internal static void RequireIdentity(
        PurchasingCommercialLineSubjectKindV1 kind,
        Guid id,
        string version)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (id == Guid.Empty)
        {
            throw new ArgumentException("Subject identity is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Subject version is required.", nameof(version));
        }
    }
}

/// <summary>
/// Owner-produced immutable presentation snapshot for the addressed subject.
/// It deliberately contains no quantity, unit, price, stock effect or money effect.
/// </summary>
public sealed record PurchasingCommercialLineSubjectSnapshotV1
{
    [JsonConstructor]
    public PurchasingCommercialLineSubjectSnapshotV1(
        PurchasingCommercialLineSubjectKindV1 kind,
        Guid id,
        string version,
        string code,
        string name)
    {
        PurchasingCommercialLineSubjectReferenceV1.RequireIdentity(kind, id, version);
        Kind = kind;
        Id = id;
        Version = version;
        Code = code;
        Name = name;
    }

    public PurchasingCommercialLineSubjectKindV1 Kind { get; }
    public Guid Id { get; }
    public string Version { get; }
    public string Code { get; }
    public string Name { get; }
}
