namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// AK-3 (<c>docs/delivery/ADET_KG_PAKETI_v1.md</c>; SAT-D437, KIM-D47): the Purchasing-owned boundary a piece-unit order line's
/// theoretical kilograms per piece is read through at approval. The host implements it over the Product owner's
/// <c>IProductTheoreticalKilogramsPerUnitReadPortV1</c>; Purchasing keeps no product data beyond the recorded figure and its source
/// fingerprint. When the host registers no implementation an order with a piece line is refused 503
/// (<see cref="PurchasingProblemCodesV1.TheoreticalKilogramsUnavailable"/>); kilogram-only orders never call it.
/// </summary>
public interface IPurchaseTheoreticalKilogramsPerUnitPortV1
{
    Task<PurchaseTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default);
}

public enum PurchaseTheoreticalKilogramsPerUnitOutcomeV1
{
    /// <summary>The owner cannot answer: 503, nothing recorded.</summary>
    Unavailable = 0,

    /// <summary>The actor/company may not read the product: 403.</summary>
    Denied = 1,

    /// <summary>The product gives no kilogram figure for the unit (no source, contradicting sources or not found): fail-closed 422.</summary>
    NoSource = 2,

    /// <summary>A positive figure with its source fingerprint.</summary>
    Found = 3,
}

/// <summary>
/// The answer for one product and unit. Only <see cref="PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Found"/> carries a positive
/// <see cref="KilogramsPerUnit"/> and a <see cref="SourceVersion"/>; <see cref="Code"/> names why a no-source answer has none.
/// </summary>
public sealed record PurchaseTheoreticalKilogramsPerUnitAnswerV1(PurchaseTheoreticalKilogramsPerUnitOutcomeV1 Outcome,
    decimal? KilogramsPerUnit, string? SourceVersion, string? Code)
{
    public static PurchaseTheoreticalKilogramsPerUnitAnswerV1 Found(decimal kilogramsPerUnit, string sourceVersion) =>
        new(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Found, kilogramsPerUnit, sourceVersion, null);

    public static PurchaseTheoreticalKilogramsPerUnitAnswerV1 Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1 outcome,
        string? code = null) =>
        outcome == PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Found
            ? throw new ArgumentOutOfRangeException(nameof(outcome), "Only a found figure carries a value.")
            : new(outcome, null, null, code);
}
