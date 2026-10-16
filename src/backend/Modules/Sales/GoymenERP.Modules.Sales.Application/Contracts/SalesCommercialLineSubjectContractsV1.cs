using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Sales.Application.Contracts;

public sealed class SalesCommercialLineSubjectKindJsonConverterV1
    : JsonStringEnumConverter<SalesCommercialLineSubjectKindV1>
{
    public SalesCommercialLineSubjectKindJsonConverterV1()
        : base(namingPolicy: null, allowIntegerValues: false)
    {
    }
}

/// <summary>
/// The identity grain of a commercial Sales line. Quantity, unit, price and every
/// physical or financial effect are separate contracts. This type does not authorize
/// a Product, Service or Consumable and is not an owner eligibility answer.
/// </summary>
[JsonConverter(typeof(SalesCommercialLineSubjectKindJsonConverterV1))]
public enum SalesCommercialLineSubjectKindV1
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
public sealed record SalesCommercialLineSubjectReferenceV1
{
    [JsonConstructor]
    public SalesCommercialLineSubjectReferenceV1(
        SalesCommercialLineSubjectKindV1 kind,
        Guid id,
        string expectedVersion)
    {
        RequireIdentity(kind, id, expectedVersion);
        Kind = kind;
        Id = id;
        ExpectedVersion = expectedVersion;
    }

    public SalesCommercialLineSubjectKindV1 Kind { get; }
    public Guid Id { get; }
    public string ExpectedVersion { get; }

    internal static void RequireIdentity(
        SalesCommercialLineSubjectKindV1 kind,
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
public sealed record SalesCommercialLineSubjectSnapshotV1
{
    [JsonConstructor]
    public SalesCommercialLineSubjectSnapshotV1(
        SalesCommercialLineSubjectKindV1 kind,
        Guid id,
        string version,
        string code,
        string name)
    {
        SalesCommercialLineSubjectReferenceV1.RequireIdentity(kind, id, version);
        Kind = kind;
        Id = id;
        Version = version;
        Code = code;
        Name = name;
    }

    public SalesCommercialLineSubjectKindV1 Kind { get; }
    public Guid Id { get; }
    public string Version { get; }
    public string Code { get; }
    public string Name { get; }
}
