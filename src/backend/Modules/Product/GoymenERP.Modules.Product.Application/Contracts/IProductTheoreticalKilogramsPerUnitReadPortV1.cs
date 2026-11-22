namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>
/// AK-1 (<c>docs/delivery/ADET_KG_PAKETI_v1.md</c>; SAT-D437, KLN-D119): contract facts of the Product owner's theoretical
/// kilograms-per-unit read.
/// </summary>
public static class ProductTheoreticalKilogramsPerUnitContractV1
{
    public const int Version = 1;
}

public enum ProductTheoreticalKilogramsPerUnitOutcomeV1
{
    /// <summary>The card and its sources give a positive kilograms-per-unit figure for the unit.</summary>
    Found = 1,

    /// <summary>
    /// The card is readable but gives no figure for the unit: the unit is not on the card, the card has no dimensions or
    /// weight-table row for it, the unit has no usable company profile, or no unit conversion policy is in force.
    /// </summary>
    NoSource = 2,

    /// <summary>The card's sources contradict each other (e.g. two weight-table rows); refused, never one picked.</summary>
    Refused = 3,

    NotFound = 4,

    Denied = 5,

    /// <summary>A source cannot be read or answered malformed; refused, never guessed.</summary>
    Unavailable = 6,
}

/// <summary>Why a readable card gives no figure (<see cref="ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource"/> or
/// <see cref="ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused"/>).</summary>
public static class ProductTheoreticalKilogramsPerUnitCodesV1
{
    /// <summary>The unit is not one of the card's unit references (stock, purchase, sales, price, production, package).</summary>
    public const string UnitNotOnCard = "product_kg_per_unit_unit_not_on_card";

    /// <summary>The unit is on the card, but its dimensions, weight table, unit profile or policy give no figure.</summary>
    public const string NoSource = "product_kg_per_unit_no_source";

    /// <summary>The card's weight-table rows disagree (BS-13); the conflict is shown, never resolved by a pick.</summary>
    public const string WeightConflict = "product_kg_per_unit_weight_conflict";

    /// <summary>The underlying source answered unreadable or inconsistent with the request.</summary>
    public const string Unavailable = "product_kg_per_unit_unavailable";
}

/// <summary>
/// The source and version evidence a found figure was computed from, so a consumer can pin it as a snapshot (SAT-D437:
/// the figure is recorded on the document at approval and does not follow later card edits).
/// <list type="bullet">
/// <item><c>ProductVersion</c>: the card version read (KLN-D67, current card by product id);</item>
/// <item><c>FormulaVersion</c>: the shared deterministic calculator's version (<c>ProductUnitConversionCalculatorV1</c>);</item>
/// <item><c>DimensionVersion</c>, <c>WeightTableVersion</c>, <c>WeightTableSource</c>: the card dimension row and the
/// kg/m table row (BS-9 / BS-13 / URN-D8), when used;</item>
/// <item><c>PolicyId</c>, <c>PolicyVersion</c>, <c>DensityGramsPerCubicCentimetre</c>: the Settings unit conversion policy
/// in force on the date (KIM-D37), when one is in force;</item>
/// <item><c>UnitProfileId</c>, <c>UnitProfileVersion</c>: the company's Settings unit profile for the unit;</item>
/// <item><c>UnitSemanticsVersion</c>: the Settings standard unit semantics version.</item>
/// </list>
/// </summary>
public sealed record ProductTheoreticalKilogramsPerUnitEvidenceV1(
    Guid CompanyId,
    Guid ProductId,
    Guid UnitId,
    ProductPhysicalUnitKindV1 UnitKind,
    string ProductVersion,
    string FormulaVersion,
    long? DimensionVersion,
    long? WeightTableVersion,
    string? WeightTableSource,
    Guid? PolicyId,
    long? PolicyVersion,
    decimal? DensityGramsPerCubicCentimetre,
    Guid UnitProfileId,
    long UnitProfileVersion,
    string UnitSemanticsVersion);

/// <summary>
/// The answer for one product and unit of the server-resolved company on one date. Only
/// <see cref="ProductTheoreticalKilogramsPerUnitOutcomeV1.Found"/> carries a positive figure and its evidence; every other
/// outcome carries neither (never a zero, a default or a guess). <see cref="Code"/> explains a no-source, refused or
/// unavailable answer.
/// </summary>
public sealed record ProductTheoreticalKilogramsPerUnitAnswerV1
{
    private ProductTheoreticalKilogramsPerUnitAnswerV1(ProductTheoreticalKilogramsPerUnitOutcomeV1 outcome,
        decimal? kilogramsPerUnit, ProductTheoreticalKilogramsPerUnitEvidenceV1? evidence, string? code)
    {
        Outcome = outcome;
        KilogramsPerUnit = kilogramsPerUnit;
        Evidence = evidence;
        Code = code;
    }

    public int ContractVersion => ProductTheoreticalKilogramsPerUnitContractV1.Version;

    public ProductTheoreticalKilogramsPerUnitOutcomeV1 Outcome { get; }

    public decimal? KilogramsPerUnit { get; }

    public ProductTheoreticalKilogramsPerUnitEvidenceV1? Evidence { get; }

    public string? Code { get; }

    public static ProductTheoreticalKilogramsPerUnitAnswerV1 Found(decimal kilogramsPerUnit,
        ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (kilogramsPerUnit <= 0m)
            throw new ArgumentOutOfRangeException(nameof(kilogramsPerUnit), "A found figure is positive.");
        if (evidence.CompanyId == Guid.Empty || evidence.ProductId == Guid.Empty || evidence.UnitId == Guid.Empty
            || !Enum.IsDefined(evidence.UnitKind) || string.IsNullOrWhiteSpace(evidence.ProductVersion)
            || string.IsNullOrWhiteSpace(evidence.FormulaVersion) || evidence.UnitProfileId == Guid.Empty
            || evidence.UnitProfileVersion < 1 || string.IsNullOrWhiteSpace(evidence.UnitSemanticsVersion)
            || (evidence.PolicyId is null) != (evidence.PolicyVersion is null)
            || evidence.PolicyId == Guid.Empty || evidence.PolicyVersion is < 1
            || (evidence.PolicyId is null && evidence.DensityGramsPerCubicCentimetre is not null)
            || evidence.DensityGramsPerCubicCentimetre is <= 0m)
            throw new ArgumentException("A found figure names its complete source evidence.", nameof(evidence));
        return new(ProductTheoreticalKilogramsPerUnitOutcomeV1.Found, kilogramsPerUnit, evidence, null);
    }

    public static ProductTheoreticalKilogramsPerUnitAnswerV1 Without(ProductTheoreticalKilogramsPerUnitOutcomeV1 outcome,
        string? code = null)
    {
        if (outcome == ProductTheoreticalKilogramsPerUnitOutcomeV1.Found || !Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome), "Only a found figure carries a value.");
        var needsCode = outcome is ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource
            or ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused or ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable;
        if (needsCode == string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("No-source, refused and unavailable answers carry a code; the others do not.", nameof(code));
        return new(outcome, null, null, code);
    }
}

/// <summary>
/// Read port V1, owned and implemented by Product (AK-1): how many kilograms one unit (piece, metre, tonne, kg) of the
/// product weighs in theory on <c>effectiveOn</c>, computed from the card's dimensions / kg/m weight table and the Settings
/// density and rounding policy through the shared deterministic calculator (<c>ProductUnitConversionCalculatorV1</c>); no new
/// formula, no typed coefficient, no default. A product without a kilogram source answers
/// <see cref="ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource"/> and the consumer refuses (fail-closed).
/// <para>
/// Like the other Product reference reads it carries no authority: the consumer measures its own permission; the read
/// requires current company membership and reads only the server-resolved company. It creates nothing. Consumers: Inventory
/// (<c>IInventoryKilogramsPerUnitSourceV1</c> host adapter, AK-1); Sales/Purchasing order approval snapshots (SAT-D437,
/// AK-3/AK-4) and Manufacturing output (AK-7) in later slices.
/// </para>
/// </summary>
public interface IProductTheoreticalKilogramsPerUnitReadPortV1
{
    Task<ProductTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}
