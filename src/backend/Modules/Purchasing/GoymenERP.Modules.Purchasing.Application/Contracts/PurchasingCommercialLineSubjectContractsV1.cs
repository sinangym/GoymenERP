using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// The identity grain of a commercial Purchasing line. Quantity, unit, price and every
/// physical or financial effect are separate contracts. This type does not authorize
/// a Product, Service or Consumable and is not an owner eligibility answer.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PurchasingCommercialLineSubjectKindV1>))]
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
public sealed record PurchasingCommercialLineSubjectReferenceV1(
    PurchasingCommercialLineSubjectKindV1 Kind,
    Guid Id,
    string ExpectedVersion);

/// <summary>
/// Owner-produced immutable presentation snapshot for the addressed subject.
/// It deliberately contains no quantity, unit, price, stock effect or money effect.
/// </summary>
public sealed record PurchasingCommercialLineSubjectSnapshotV1(
    PurchasingCommercialLineSubjectKindV1 Kind,
    Guid Id,
    string Version,
    string Code,
    string Name);
