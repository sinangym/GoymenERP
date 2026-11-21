using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// AK-1 (SAT-D437, KLN-D119): the Product owner's theoretical kilograms per unit. It is a view over the owner's existing
/// B-07 source (<see cref="IProductPurchaseUnitConversionV1"/>, implemented by <see cref="ProductPurchaseUnitConversionService"/>):
/// the card's unit references, dimension row, kg/m weight table, the Settings unit profiles, unit semantics and the
/// density/rounding policy, combined by the shared calculator (<c>ProductUnitConversionCalculatorV1</c>). This class adds no
/// formula and no rounding; it selects the requested unit, checks the answer belongs to the request and the resolved company,
/// and hands over the figure with its version evidence. Every missing or contradictory input is a no-source / refused /
/// unavailable answer (fail-closed), never a zero.
/// </summary>
public sealed class ProductTheoreticalKilogramsPerUnitReadPort(IProductPurchaseUnitConversionV1 source, ICompanyContext company)
    : IProductTheoreticalKilogramsPerUnitReadPortV1
{
    public async Task<ProductTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (productId == Guid.Empty) throw new ArgumentException("The product is named.", nameof(productId));
        if (unitId == Guid.Empty) throw new ArgumentException("The unit is named.", nameof(unitId));
        if (!company.IsResolved || company.CompanyId == Guid.Empty)
            return ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied);
        var companyId = company.CompanyId;

        var read = await source.ReadAsync(productId, effectiveOn, cancellationToken);
        if (!company.IsResolved || company.CompanyId != companyId)
            return ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied);
        switch (read?.Outcome)
        {
            case ProductPurchaseUnitConversionOutcomeV1.Available:
                break;
            case ProductPurchaseUnitConversionOutcomeV1.NotFound:
                return ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NotFound);
            case ProductPurchaseUnitConversionOutcomeV1.Denied:
                return ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied);
            default:
                return Unavailable();
        }

        var card = read.Source;
        if (card is null || card.ContractVersion != ProductPurchaseUnitConversionContractV1.Version
            || card.CompanyId != companyId || card.ProductId != productId || card.Units is null)
            return Unavailable();

        var matches = card.Units.Where(unit => unit is not null && unit.UnitId == unitId).ToArray();
        if (matches.Length == 0)
            return ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource,
                ProductTheoreticalKilogramsPerUnitCodesV1.UnitNotOnCard);
        if (matches.Length != 1) return Unavailable();

        var requested = matches[0];
        if (requested.KgPerUnit is not > 0m)
        {
            // BS-13: a weight-table conflict on a formula unit is shown as a refusal, never resolved by picking a row.
            return requested.Kind is ProductPhysicalUnitKindV1.Metre or ProductPhysicalUnitKindV1.Piece
                && string.Equals(card.Reason, ProductPurchaseUnitConversionService.WeightConflictReason, StringComparison.Ordinal)
                ? ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused,
                    ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict)
                : ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource,
                    ProductTheoreticalKilogramsPerUnitCodesV1.NoSource);
        }

        // A figure exists only for a usable profile and a found unit meaning; anything less is not a provable figure.
        if (requested.Kind is not { } kind || !Enum.IsDefined(kind) || requested.UnitProfileId is not { } profileId
            || profileId == Guid.Empty || requested.UnitProfileVersion is not >= 1
            || string.IsNullOrWhiteSpace(requested.UnitSourceVersion) || string.IsNullOrWhiteSpace(card.ProductVersion)
            || string.IsNullOrWhiteSpace(card.FormulaVersion)
            || (card.PolicyId is null) != (card.PolicyVersion is null)
            || (card.PolicyId is null) != (card.PolicyDensityGramsPerCubicCentimetre is null)
            || card.PolicyId == Guid.Empty || card.PolicyVersion is < 1 || card.PolicyDensityGramsPerCubicCentimetre is <= 0m)
            return Unavailable();

        var evidence = new ProductTheoreticalKilogramsPerUnitEvidenceV1(companyId, productId, unitId, kind, card.ProductVersion,
            card.FormulaVersion, card.DimensionVersion, card.WeightTableVersion, card.WeightTableSource, card.PolicyId,
            card.PolicyVersion, card.PolicyDensityGramsPerCubicCentimetre, profileId, requested.UnitProfileVersion.Value,
            requested.UnitSourceVersion);
        return ProductTheoreticalKilogramsPerUnitAnswerV1.Found(requested.KgPerUnit.Value, evidence);
    }

    private static ProductTheoreticalKilogramsPerUnitAnswerV1 Unavailable() =>
        ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable,
            ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable);
}
