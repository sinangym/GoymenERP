using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D437 (AK-4): the theoretical kilograms per unit of a non-kilogram (piece) line is read from the Product owner
/// (<see cref="IProductTheoreticalKilogramsPerUnitReadPortV1"/>) at the internal approval and pinned on the document in the same
/// save. Only lines that need it are read (a new line, or a line whose product or unit changed since its last figure; the
/// SAT-D269/D271 re-pin rule); kilogram lines are never read. SAT-D473: when the owner READ the card and says it gives no figure
/// (<see cref="ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource"/>) the approval goes on and that line keeps NO figure — its kg
/// price and piece margin read "cannot be calculated" (golden M11/K13), and a later card fix never moves the approved document.
/// Anything that is not a definite "no value" — the owner not composed or unreachable, an unavailable, refused (contradictory
/// sources), not-found or denied answer, a malformed or mismatching answer — still refuses the approval (409
/// <see cref="SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable"/>, not recorded): "no value" is not "could not read".
/// Never a default, a zero or a guess.
/// </summary>
public sealed partial class SalesDraftService
{
    private async Task<IReadOnlyList<SalesLineKilogramsPerUnitSnapshot>> PrepareKilogramsPerUnitAsync(Scope scope, string? permission,
        SalesOrderDraft draft, CancellationToken cancellationToken)
    {
        var needed = draft.Lines.OrderBy(line => line.LineNo).Where(draft.NeedsKilogramsPerUnit).ToList();
        if (needed.Count == 0) return [];
        var day = ServerBusinessDay();
        var snapshots = new List<SalesLineKilogramsPerUnitSnapshot>(needed.Count);
        var refusals = new List<SalesDraftFieldError>();
        foreach (var line in needed)
        {
            var field = string.Create(CultureInfo.InvariantCulture, $"lines[{draft.Lines.ToList().IndexOf(line)}].orderUnitId");
            var answer = await ReadKilogramsPerUnitAsync(scope, permission, line, day, cancellationToken);
            if (answer is { Outcome: ProductTheoreticalKilogramsPerUnitOutcomeV1.Found, KilogramsPerUnit: { } figure, Evidence: { } evidence }
                && evidence.CompanyId == scope.CompanyId && evidence.ProductId == line.Product.ProductId
                && evidence.UnitId == line.OrderUnit.UnitId
                // SAT-D443: the figure is pinned for a piece line only; a metre (or any other kind) answer is never a piece figure.
                && evidence.UnitKind == ProductPhysicalUnitKindV1.Piece
                && decimal.Round(figure, SalesLineKilogramsPerUnitSnapshot.KilogramsPerUnitScale, MidpointRounding.AwayFromZero) == figure
                && figure <= SalesLineKilogramsPerUnitSnapshot.MaximumKilogramsPerUnit)
            {
                snapshots.Add(new SalesLineKilogramsPerUnitSnapshot(line.Id, draft.Revision, line.Product.ProductId,
                    line.OrderUnit.UnitId, figure / 1.000000000000000000000000000m, SourceProof(evidence, day),
                    // Replaced by the approval instant inside the save (PinKilogramsPerUnit requires it).
                    timeProvider.GetUtcNow().ToUniversalTime()));
                continue;
            }

            // SAT-D473: a readable card without a figure is approved with this line's figure left empty.
            if (answer is { Outcome: ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, KilogramsPerUnit: null }) continue;

            refusals.Add(new(field, answer is { Code: { } code } && answer.Code.StartsWith("product_kg_per_unit_", StringComparison.Ordinal)
                ? code
                : SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable));
        }

        if (refusals.Count > 0)
            throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.KilogramsPerUnitUnavailable, refusals);
        return snapshots;
    }

    private async Task<ProductTheoreticalKilogramsPerUnitAnswerV1?> ReadKilogramsPerUnitAsync(Scope scope, string? permission,
        SalesOrderDraftLine line, DateOnly day, CancellationToken cancellationToken)
    {
        if (kilogramsPerUnitSource is null) return null;
        ProductTheoreticalKilogramsPerUnitAnswerV1? answer;
        try
        {
            answer = await kilogramsPerUnitSource.ReadAsync(line.Product.ProductId, line.OrderUnit.UnitId, day, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            answer = null;
        }

        // A slow owner read can revoke the command's name; nothing is pinned on a stale authority.
        await RequireStillAsync(scope, permission, cancellationToken);
        return answer is { ContractVersion: ProductTheoreticalKilogramsPerUnitContractV1.Version } ? answer : null;
    }

    private static string SourceProof(ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence, DateOnly day) =>
        JsonSerializer.Serialize(new
        {
            contractVersion = ProductTheoreticalKilogramsPerUnitContractV1.Version,
            effectiveOn = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            unitKind = evidence.UnitKind.ToString(),
            productVersion = evidence.ProductVersion,
            formulaVersion = evidence.FormulaVersion,
            dimensionVersion = evidence.DimensionVersion,
            weightTableVersion = evidence.WeightTableVersion,
            weightTableSource = evidence.WeightTableSource,
            policyId = evidence.PolicyId,
            policyVersion = evidence.PolicyVersion,
            densityGramsPerCubicCentimetre = evidence.DensityGramsPerCubicCentimetre,
            unitProfileId = evidence.UnitProfileId,
            unitProfileVersion = evidence.UnitProfileVersion,
            unitSemanticsVersion = evidence.UnitSemanticsVersion,
        });

    /// <summary>The line's recorded figure as invariant text for reads (null: none recorded; SAT-D437 TK7).</summary>
    internal static string? KilogramsPerUnitText(SalesOrderDraft draft, Guid lineId) =>
        draft.KilogramsPerUnitOf(lineId) is { } snapshot
            ? (snapshot.KilogramsPerUnit / 1.000000000000000000000000000m).ToString(CultureInfo.InvariantCulture)
            : null;
}
