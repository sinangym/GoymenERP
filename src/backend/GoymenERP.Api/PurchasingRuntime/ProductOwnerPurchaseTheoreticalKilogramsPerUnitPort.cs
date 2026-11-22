using System.Security.Cryptography;
using System.Text;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>
/// AK-3 host adapter (SAT-D437): Purchasing's approval-time theoretical kg/piece boundary
/// (<see cref="IPurchaseTheoreticalKilogramsPerUnitPortV1"/>) over the Product owner's read
/// (<see cref="IProductTheoreticalKilogramsPerUnitReadPortV1"/>). It only adapts the owner's DTO: no arithmetic, no rounding, no
/// default. A found figure is handed over only when it belongs to the request and the resolved company; its source fingerprint is a
/// SHA-256 of the same ordered version list Inventory's AK-1 adapter records, so the two snapshots can be compared. No source,
/// contradicting sources and not found are no-source (fail-closed); denied stays denied; everything else is unavailable.
/// </summary>
public sealed class ProductOwnerPurchaseTheoreticalKilogramsPerUnitPort(IProductTheoreticalKilogramsPerUnitReadPortV1 product,
    ICompanyContext company) : IPurchaseTheoreticalKilogramsPerUnitPortV1
{
    /// <summary>The fingerprint's prefix; the full value is the prefix and 64 lower-case hex characters.</summary>
    public const string SourcePrefix = "product.kg-per-unit.v1/";

    public async Task<PurchaseTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (productId == Guid.Empty || unitId == Guid.Empty || !company.IsResolved || company.CompanyId == Guid.Empty)
            return Unavailable();
        var companyId = company.CompanyId;
        var answer = await product.ReadAsync(productId, unitId, effectiveOn, cancellationToken);
        if (!company.IsResolved || company.CompanyId != companyId || answer is null
            || answer.ContractVersion != ProductTheoreticalKilogramsPerUnitContractV1.Version)
            return Unavailable();
        switch (answer.Outcome)
        {
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource:
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused:
                return PurchaseTheoreticalKilogramsPerUnitAnswerV1.Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.NoSource, answer.Code);
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.NotFound:
                return PurchaseTheoreticalKilogramsPerUnitAnswerV1.Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.NoSource,
                    ProductTheoreticalKilogramsPerUnitCodesV1.NoSource);
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied:
                return PurchaseTheoreticalKilogramsPerUnitAnswerV1.Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Denied);
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.Found:
                break;
            default:
                return Unavailable();
        }

        if (answer is not { KilogramsPerUnit: > 0m, Evidence: { } evidence } || evidence.CompanyId != companyId
            || evidence.ProductId != productId || evidence.UnitId != unitId)
            return Unavailable();
        return PurchaseTheoreticalKilogramsPerUnitAnswerV1.Found(answer.KilogramsPerUnit.Value, SourceVersion(evidence));
    }

    internal static string SourceVersion(ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence) => SourcePrefix
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            ProductOwnerInventoryKilogramsPerUnitSource.SourceVersion(evidence)))).ToLowerInvariant();

    private static PurchaseTheoreticalKilogramsPerUnitAnswerV1 Unavailable() =>
        PurchaseTheoreticalKilogramsPerUnitAnswerV1.Without(PurchaseTheoreticalKilogramsPerUnitOutcomeV1.Unavailable);
}
