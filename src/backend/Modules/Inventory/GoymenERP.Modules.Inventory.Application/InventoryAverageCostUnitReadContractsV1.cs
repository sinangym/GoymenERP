namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// KLN-D85 (STK-SC33-a): the company-wide moving-average purchase cost of one product read in a unit other than the stock
/// kilogram (piece, metre…). The figure converted is the company-wide answer (<see cref="IInventoryCompanyWideAverageCostReadPortV1"/>,
/// KLN-D83 / KLN-D84); the conversion uses the Settings unit conversion policy in force on the caller's date (KIM-D37). A
/// missing policy refuses the conversion (fail-closed); there is no product-card coefficient field and no default. Contract
/// version 1 facts are pinned by <c>InventoryAverageCostUnitReadPortTests</c>.
/// </summary>
public static class InventoryAverageCostUnitReadContractV1
{
    public const int Version = 1;

    /// <summary>Purchase receipts only (SAT-D263), as the location read.</summary>
    public const string CostBasis = InventoryCompanyWideAverageCostReadContractV1.CostBasis;

    /// <summary>The figure converted is the company-wide one.</summary>
    public const string Aggregation = InventoryCompanyWideAverageCostReadContractV1.Aggregation;

    /// <summary>As the company-wide read: the company's base currency.</summary>
    public const string CostCurrencyBasis = InventoryCompanyWideAverageCostReadContractV1.CostCurrencyBasis;

    /// <summary>KLN-D85: the one conversion source is the Settings unit conversion policy (KIM-D37).</summary>
    public const string ConversionSource = "settings-unit-conversion-policy";

    /// <summary>SAT-D265 (2): the cost per unit carries at most the internal scale.</summary>
    public const int InternalScale = InventoryCompanyWideAverageCostReadContractV1.InternalScale;
}

public enum InventoryAverageCostUnitReadStatusV1
{
    /// <summary>The cost per requested unit is known.</summary>
    Value = 1,

    /// <summary>The company-wide figure itself is "hesaplanamadı" (KLN-D83): nothing to convert.</summary>
    Unreadable = 2,

    /// <summary>No location holds stock of the product: nothing to convert.</summary>
    NoSource = 3,

    /// <summary>The company-wide figure is known but cannot be converted into the unit (KLN-D85, fail-closed).</summary>
    ConversionRefused = 4,
}

/// <summary>Why a known company-wide figure is not converted. Never a zero, never a guess.</summary>
public static class InventoryAverageCostUnitConversionRefusalCodesV1
{
    /// <summary>The Settings unit identities cannot be read (not registered or unreadable).</summary>
    public const string UnitIdentityUnavailable = "unit_identity_unavailable";

    /// <summary>Settings knows no unit with the requested id.</summary>
    public const string UnitUnknown = "unit_unknown";

    /// <summary>KLN-D85 / KIM-D37: no unit conversion policy is in force on the date (none entered yet, or the date is earlier).</summary>
    public const string PolicyNotFound = "unit_conversion_policy_not_found";

    /// <summary>The policy cannot be read (provider missing, access refused, store unreadable, malformed or another company's).</summary>
    public const string PolicyUnreadable = "unit_conversion_policy_unreadable";

    /// <summary>The product's facts give no kilograms per unit for the requested unit.</summary>
    public const string KilogramsPerUnitNoSource = "kilograms_per_unit_no_source";

    /// <summary>The source of the product's kilograms per unit cannot be read.</summary>
    public const string KilogramsPerUnitUnavailable = "kilograms_per_unit_unavailable";

    /// <summary>The coefficient rounds to zero at the policy's decimals.</summary>
    public const string CoefficientNotPositive = "unit_coefficient_not_positive";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        UnitIdentityUnavailable, UnitUnknown, PolicyNotFound, PolicyUnreadable, KilogramsPerUnitNoSource,
        KilogramsPerUnitUnavailable, CoefficientNotPositive,
    };
}

/// <summary>
/// The answer for one product of the server-resolved company in one unit. <see cref="CompanyWide"/> is the per-kilogram
/// answer it was computed from (its sources are the snapshot marker). A value carries the cost per unit and the
/// coefficient; a kilogram unit is the per-kilogram figure as is (coefficient 1, no policy read); a converted value also
/// names the policy version used. A refusal carries its code; unreadable and no-source carry the per-kilogram answer only.
/// </summary>
public sealed record InventoryAverageCostUnitReadAnswerV1
{
    private InventoryAverageCostUnitReadAnswerV1(
        InventoryCompanyWideAverageCostReadAnswerV1 companyWide, Guid unitId, InventoryAverageCostUnitReadStatusV1 status,
        decimal? costPerUnit, decimal? kilogramsPerUnitCoefficient, string? conversionRefusalCode, Guid? policyProfileId,
        long? policyVersion)
    {
        CompanyWide = companyWide;
        UnitId = unitId;
        Status = status;
        CostPerUnit = costPerUnit;
        KilogramsPerUnitCoefficient = kilogramsPerUnitCoefficient;
        ConversionRefusalCode = conversionRefusalCode;
        PolicyProfileId = policyProfileId;
        PolicyVersion = policyVersion;
    }

    public int ContractVersion => InventoryAverageCostUnitReadContractV1.Version;

    public Guid CompanyId => CompanyWide.CompanyId;

    public Guid ProductId => CompanyWide.ProductId;

    public Guid UnitId { get; }

    public InventoryAverageCostUnitReadStatusV1 Status { get; }

    /// <summary>Only with <see cref="InventoryAverageCostUnitReadStatusV1.Value"/>: non-negative, at most 4 decimals.</summary>
    public decimal? CostPerUnit { get; }

    /// <summary>
    /// With <see cref="InventoryAverageCostUnitReadStatusV1.Value"/> (1 for a kilogram unit) and with an
    /// <see cref="InventoryAverageCostUnitConversionRefusalCodesV1.CoefficientNotPositive"/> refusal: the kilograms one unit
    /// weighs, as the policy rounds it.
    /// </summary>
    public decimal? KilogramsPerUnitCoefficient { get; }

    /// <summary>Only with <see cref="InventoryAverageCostUnitReadStatusV1.ConversionRefused"/>: one of <see cref="InventoryAverageCostUnitConversionRefusalCodesV1.All"/>.</summary>
    public string? ConversionRefusalCode { get; }

    /// <summary>The Settings policy version the conversion was computed with (null when no policy was used).</summary>
    public Guid? PolicyProfileId { get; }

    public long? PolicyVersion { get; }

    public string CostCurrencyBasis => InventoryAverageCostUnitReadContractV1.CostCurrencyBasis;

    public InventoryCompanyWideAverageCostReadAnswerV1 CompanyWide { get; }

    /// <summary>An unreadable or no-source company-wide answer: there is nothing to convert.</summary>
    public static InventoryAverageCostUnitReadAnswerV1 ForUnconvertible(
        InventoryCompanyWideAverageCostReadAnswerV1 companyWide, Guid unitId)
    {
        RequireIdentity(companyWide, unitId);
        var status = companyWide.Status switch
        {
            InventoryAverageCostReadStatusV1.Unreadable => InventoryAverageCostUnitReadStatusV1.Unreadable,
            InventoryAverageCostReadStatusV1.NoSource => InventoryAverageCostUnitReadStatusV1.NoSource,
            _ => throw new ArgumentException("A known company-wide figure is converted or refused.", nameof(companyWide)),
        };

        return new(companyWide, unitId, status, null, null, null, null, null);
    }

    /// <summary>The requested unit is the kilogram: the per-kilogram figure as is, coefficient 1, no policy read.</summary>
    public static InventoryAverageCostUnitReadAnswerV1 ForKilogram(
        InventoryCompanyWideAverageCostReadAnswerV1 companyWide, Guid unitId)
    {
        RequireKnown(companyWide, unitId);
        return new(companyWide, unitId, InventoryAverageCostUnitReadStatusV1.Value, companyWide.AverageCostPerKg,
            1m, null, null, null);
    }

    public static InventoryAverageCostUnitReadAnswerV1 ForConverted(
        InventoryCompanyWideAverageCostReadAnswerV1 companyWide, Guid unitId, decimal costPerUnit,
        decimal kilogramsPerUnitCoefficient, Guid policyProfileId, long policyVersion)
    {
        RequireKnown(companyWide, unitId);
        if (costPerUnit < 0m || Math.Round(costPerUnit, InventoryAverageCostUnitReadContractV1.InternalScale,
                MidpointRounding.AwayFromZero) != costPerUnit)
        {
            throw new ArgumentOutOfRangeException(nameof(costPerUnit), "The cost per unit is non-negative with at most 4 decimals.");
        }

        if (kilogramsPerUnitCoefficient <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(kilogramsPerUnitCoefficient), "A converted unit weighs a positive coefficient.");
        }

        RequirePolicy(policyProfileId, policyVersion);
        return new(companyWide, unitId, InventoryAverageCostUnitReadStatusV1.Value, costPerUnit, kilogramsPerUnitCoefficient,
            null, policyProfileId, policyVersion);
    }

    public static InventoryAverageCostUnitReadAnswerV1 ForRefused(
        InventoryCompanyWideAverageCostReadAnswerV1 companyWide, Guid unitId, string refusalCode,
        decimal? kilogramsPerUnitCoefficient = null, Guid? policyProfileId = null, long? policyVersion = null)
    {
        RequireKnown(companyWide, unitId);
        if (refusalCode is null || !InventoryAverageCostUnitConversionRefusalCodesV1.All.Contains(refusalCode))
        {
            throw new ArgumentOutOfRangeException(nameof(refusalCode), "Unknown conversion refusal code.");
        }

        if ((kilogramsPerUnitCoefficient is not null)
            != (refusalCode == InventoryAverageCostUnitConversionRefusalCodesV1.CoefficientNotPositive))
        {
            throw new ArgumentException("Only a not-positive coefficient refusal carries its coefficient.",
                nameof(kilogramsPerUnitCoefficient));
        }

        if ((policyProfileId is null) != (policyVersion is null))
        {
            throw new ArgumentException("A policy is named with its version.", nameof(policyVersion));
        }

        if (policyProfileId is { } profileId)
        {
            RequirePolicy(profileId, policyVersion!.Value);
        }

        return new(companyWide, unitId, InventoryAverageCostUnitReadStatusV1.ConversionRefused, null,
            kilogramsPerUnitCoefficient, refusalCode, policyProfileId, policyVersion);
    }

    private static void RequireIdentity(InventoryCompanyWideAverageCostReadAnswerV1 companyWide, Guid unitId)
    {
        ArgumentNullException.ThrowIfNull(companyWide);
        if (unitId == Guid.Empty)
        {
            throw new ArgumentException("The unit is named.", nameof(unitId));
        }
    }

    private static void RequireKnown(InventoryCompanyWideAverageCostReadAnswerV1 companyWide, Guid unitId)
    {
        RequireIdentity(companyWide, unitId);
        if (companyWide.Status != InventoryAverageCostReadStatusV1.Value || companyWide.AverageCostPerKg is null)
        {
            throw new ArgumentException("Only a known company-wide figure is converted or refused.", nameof(companyWide));
        }
    }

    private static void RequirePolicy(Guid policyProfileId, long policyVersion)
    {
        if (policyProfileId == Guid.Empty)
        {
            throw new ArgumentException("The policy is named.", nameof(policyProfileId));
        }

        if (policyVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion));
        }
    }
}

public enum InventoryKilogramsPerUnitOutcomeV1
{
    /// <summary>The source cannot be read (not configured, denied or failing): refused, never guessed.</summary>
    Unavailable = 0,

    /// <summary>The product's facts give no kilograms per unit for this unit (no dimensions, unit of another dimension…).</summary>
    NoSource = 1,

    Found = 2,
}

/// <summary>
/// How many kilograms one unit of the product weighs. Only <see cref="InventoryKilogramsPerUnitOutcomeV1.Found"/> carries a
/// positive figure; <see cref="SourceVersion"/> names the product facts it was computed from (snapshot marker, optional).
/// </summary>
public sealed record InventoryKilogramsPerUnitV1(
    InventoryKilogramsPerUnitOutcomeV1 Outcome, decimal? KilogramsPerUnit, string? SourceVersion);

/// <summary>
/// KLN-D85 seam: the kilograms one unit (piece, metre…) of the product weighs, computed from the product's own facts
/// (dimensions, kg/m; URN-D11 / URN-D16) with the policy's density through the shared deterministic unit calculator. The
/// product card carries no separate coefficient (KLN-D85). Inventory owns no product facts and no unit formula, so the
/// implementation is the host's adapter over the Product owner's answer; Inventory's default is fail-closed
/// (<see cref="InventoryKilogramsPerUnitOutcomeV1.NoSource"/>) until that adapter exists.
/// </summary>
public interface IInventoryKilogramsPerUnitSourceV1
{
    Task<InventoryKilogramsPerUnitV1> ReadAsync(Guid productId, Guid unitId, decimal densityGramsPerCubicCentimetre,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read port V1, owned and implemented by Inventory: the company-wide moving-average purchase cost of one product of the
/// server-resolved company in the requested unit (KLN-D85), with the Settings unit conversion policy in force on
/// <c>policyDate</c> (the caller's business date; Inventory does not pick it). Like the company-wide read it carries no
/// authority, creates nothing, never throws for a missing figure or a refused conversion (those are statuses), and refuses
/// an unresolved company.
/// </summary>
public interface IInventoryAverageCostUnitReadPortV1
{
    Task<InventoryAverageCostUnitReadAnswerV1> ReadCompanyWideInUnitAsync(
        Guid productId, Guid unitId, DateOnly policyDate, CancellationToken cancellationToken = default);
}
