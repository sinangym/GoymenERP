using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.InventoryRuntime;

/// <summary>
/// AK-1 host adapter: Inventory's kilograms-per-unit seam (<see cref="IInventoryKilogramsPerUnitSourceV1"/>, KLN-D85) over the
/// Product owner's theoretical kg/unit read (<see cref="IProductTheoreticalKilogramsPerUnitReadPortV1"/>). It only adapts the
/// owner's DTO: no arithmetic, no rounding, no default. A found figure is handed over only when it belongs to the request and
/// the resolved company and was computed with the same policy density Inventory read for the same date; otherwise the answer
/// is unavailable. A card without a kilogram source is no-source (fail-closed).
/// </summary>
public sealed class ProductOwnerInventoryKilogramsPerUnitSource(IProductTheoreticalKilogramsPerUnitReadPortV1 product,
    ICompanyContext company) : IInventoryKilogramsPerUnitSourceV1
{
    public async Task<InventoryKilogramsPerUnitV1> ReadAsync(Guid productId, Guid unitId, DateOnly policyDate,
        decimal densityGramsPerCubicCentimetre, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (productId == Guid.Empty || unitId == Guid.Empty || densityGramsPerCubicCentimetre <= 0m
            || !company.IsResolved || company.CompanyId == Guid.Empty)
            return Unavailable();
        var companyId = company.CompanyId;
        var answer = await product.ReadAsync(productId, unitId, policyDate, cancellationToken);
        if (!company.IsResolved || company.CompanyId != companyId || answer is null
            || answer.ContractVersion != ProductTheoreticalKilogramsPerUnitContractV1.Version)
            return Unavailable();
        switch (answer.Outcome)
        {
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource:
                return new(InventoryKilogramsPerUnitOutcomeV1.NoSource, null, null);
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.Found:
                break;
            default:
                // Refused (contradictory card), not found, denied or unavailable: never a guess.
                return Unavailable();
        }

        if (answer is not { KilogramsPerUnit: > 0m, Evidence: { } evidence } || evidence.CompanyId != companyId
            || evidence.ProductId != productId || evidence.UnitId != unitId
            || (evidence.DensityGramsPerCubicCentimetre is { } density && density != densityGramsPerCubicCentimetre))
            return Unavailable();
        return new(InventoryKilogramsPerUnitOutcomeV1.Found, answer.KilogramsPerUnit, SourceVersion(evidence));
    }

    /// <summary>The snapshot marker of the figure: every source version it was computed from, in a fixed order.</summary>
    internal static string SourceVersion(ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence) => string.Join(';',
        "product:" + evidence.ProductVersion,
        "formula:" + evidence.FormulaVersion,
        "dimension:" + Text(evidence.DimensionVersion),
        "weight-table:" + Text(evidence.WeightTableVersion),
        "policy:" + (evidence.PolicyId is { } policy ? policy.ToString("D") + "/" + Text(evidence.PolicyVersion) : "-"),
        "unit-profile:" + evidence.UnitProfileId.ToString("D") + "/" + Text(evidence.UnitProfileVersion),
        "unit-semantics:" + evidence.UnitSemanticsVersion);

    private static string Text(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static InventoryKilogramsPerUnitV1 Unavailable() => new(InventoryKilogramsPerUnitOutcomeV1.Unavailable, null, null);
}
