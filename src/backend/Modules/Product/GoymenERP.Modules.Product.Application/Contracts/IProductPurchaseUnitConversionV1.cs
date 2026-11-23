using GoymenERP.Modules.Product.Domain.UnitConversion;

namespace GoymenERP.Modules.Product.Application.Contracts;

public static class ProductPurchaseUnitConversionContractV1 { public const int Version = 1; }

public enum ProductPurchaseUnitConversionOutcomeV1
{
    Available = 1, NoSource = 2, Refused = 3, NotFound = 4, Denied = 5, Unavailable = 6,
}

public enum ProductPhysicalUnitKindV1 { Kilogram = 1, Tonne = 2, Metre = 3, Piece = 4 }

/// <summary>A real unit reference of the card. A null coefficient is explicit "çevrim yok".</summary>
public sealed record ProductPurchaseConversionUnitV1(Guid UnitId, ProductPhysicalUnitKindV1? Kind,
    decimal? KgPerUnit, string? UnitSourceVersion, Guid? UnitProfileId, long? UnitProfileVersion);

/// <summary>
/// B-07 / BS-9 / BS-13 / URN-D8: the current card and its versioned dimension/table/policy sources.
/// Fixed kg/ton scales are unit semantics, not an invented Product fixed-conversion definition.
/// DefaultPriceUnitId is the card's actual Price reference; it never falls back to another unit.
/// The consumer measures its own permission; this reference read requires current company membership.
/// PolicyDensityGramsPerCubicCentimetre is the density of the policy named by PolicyId/PolicyVersion (null without a
/// policy), so a consumer that reads the policy itself can prove both reads saw the same version (AK-1).
/// </summary>
public sealed record ProductPurchaseUnitConversionSourceV1(Guid CompanyId, Guid ProductId, string ProductVersion,
    Guid? DefaultPriceUnitId, UnitConversionClassV1 ConversionClass,
    IReadOnlyList<ProductPurchaseConversionUnitV1> Units, string FormulaVersion,
    long? DimensionVersion, long? WeightTableVersion, string? WeightTableSource,
    Guid? PolicyId, long? PolicyVersion, string? Reason, decimal? PolicyDensityGramsPerCubicCentimetre = null,
    ProductWeightTableOrientationConflictV1? OrientationConflict = null)
{
    public int ContractVersion => ProductPurchaseUnitConversionContractV1.Version;
}

public sealed record ProductPurchaseUnitConversionReadV1(ProductPurchaseUnitConversionOutcomeV1 Outcome,
    ProductPurchaseUnitConversionSourceV1? Source, string? Code = null);

/// <param name="ToUnitId">Null means kilograms, without guessing or hard-coding a kilogram identity.</param>
public sealed record ProductPurchaseUnitConversionRequestV1(Guid ProductId, decimal Quantity,
    Guid FromUnitId, Guid? ToUnitId, DateOnly EffectiveOn);

/// <summary>Whole-piece rounding uses the owner's calculator. Its unconverted kg remainder stays explicit.</summary>
public sealed record ProductPurchaseUnitConversionResultV1(ProductPurchaseUnitConversionOutcomeV1 Outcome,
    decimal? Quantity, decimal? InputKg, decimal? RemainderKg,
    ProductPurchaseUnitConversionSourceV1? Source, string? Code = null);

public sealed record ProductPurchaseWeighbridgeRequestV1(Guid ProductId, decimal Quantity, Guid UnitId,
    decimal WeighedKg, DateOnly EffectiveOn);
public sealed record ProductPurchaseWeighbridgeResultV1(ProductPurchaseUnitConversionOutcomeV1 Outcome,
    bool? WithinTolerance, decimal? TheoreticalKg, ProductPurchaseUnitConversionSourceV1? Source, string? Code = null);

public interface IProductPurchaseUnitConversionV1
{
    Task<ProductPurchaseUnitConversionReadV1> ReadAsync(Guid productId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default);

    Task<ProductPurchaseUnitConversionResultV1> ConvertAsync(ProductPurchaseUnitConversionRequestV1 request,
        CancellationToken cancellationToken = default);

    Task<ProductPurchaseWeighbridgeResultV1> CheckWeighbridgeAsync(ProductPurchaseWeighbridgeRequestV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// SAT-D477 (URN-D19): the kg/m table holds the card's profile measures in both directions with different values. The source
/// keeps answering the conflict (<see cref="ProductPurchaseUnitConversionSourceV1.Reason"/>, no unit figure); this optional
/// detail names the entered direction's kg/m (the card's width × height, the document value) and the reversed one, and each
/// formula unit's coefficient computed by the same calculator from each — never a new formula, never a pick of the larger.
/// </summary>
public sealed record ProductWeightTableOrientationConflictV1(decimal EnteredKgPerMetre, decimal ReversedKgPerMetre,
    IReadOnlyList<ProductWeightTableOrientationConflictUnitV1> Units);

/// <summary>One formula unit's coefficient from the entered and from the reversed direction's kg/m.</summary>
public sealed record ProductWeightTableOrientationConflictUnitV1(Guid UnitId, decimal EnteredKgPerUnit, decimal ReversedKgPerUnit);
