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
/// SAT-D474 / SAT-D477: a weight-table conflict whose two directions are known is approved with the ENTERED direction's figure;
/// the reversed figure and the difference are the approver's warning, written into the snapshot's source proof (and so into the
/// approval audit and every read: <see cref="SalesKilogramsPerUnitTableConflictV1"/>).
/// Anything that is not a definite "no value" or such a resolvable conflict — the owner not composed or unreachable, an
/// unavailable, a refused answer without both directions, not-found or denied answer, a malformed or mismatching answer —
/// still refuses the approval (409
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
                && Pinnable(figure, evidence, scope, line))
            {
                snapshots.Add(new SalesLineKilogramsPerUnitSnapshot(line.Id, draft.Revision, line.Product.ProductId,
                    line.OrderUnit.UnitId, figure / 1.000000000000000000000000000m, SourceProof(evidence, day),
                    // Replaced by the approval instant inside the save (PinKilogramsPerUnit requires it).
                    timeProvider.GetUtcNow().ToUniversalTime()));
                continue;
            }

            // SAT-D474 / SAT-D477: the kg/m table holds the profile in both directions with different values. The approval goes on
            // with the ENTERED direction's figure; the reversed one and the difference are the approver's warning, kept in the
            // snapshot's source proof (so the approval audit and every later read carry it).
            if (answer is { Outcome: ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused,
                    Code: ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict, TableConflict: { } conflict }
                && conflict.Selection == ProductTheoreticalKilogramsPerUnitTableConflictV1.EnteredOrientation
                && Pinnable(conflict.SelectedKilogramsPerUnit, conflict.Evidence, scope, line)
                && conflict.OtherKilogramsPerUnit > 0m && conflict.OtherKilogramsPerUnit != conflict.SelectedKilogramsPerUnit
                && conflict.SelectedKgPerMetre > 0m && conflict.OtherKgPerMetre > 0m)
            {
                snapshots.Add(new SalesLineKilogramsPerUnitSnapshot(line.Id, draft.Revision, line.Product.ProductId,
                    line.OrderUnit.UnitId, conflict.SelectedKilogramsPerUnit / 1.000000000000000000000000000m,
                    SourceProof(conflict.Evidence, day, conflict), timeProvider.GetUtcNow().ToUniversalTime()));
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

    /// <summary>The owner's figure belongs to this line and fits the snapshot (SAT-D443: a piece figure only, at the stored scale).</summary>
    private static bool Pinnable(decimal figure, ProductTheoreticalKilogramsPerUnitEvidenceV1? evidence, Scope scope, SalesOrderDraftLine line) =>
        evidence is not null && evidence.CompanyId == scope.CompanyId && evidence.ProductId == line.Product.ProductId
        && evidence.UnitId == line.OrderUnit.UnitId
        && evidence.UnitKind == ProductPhysicalUnitKindV1.Piece
        && figure > 0m
        && decimal.Round(figure, SalesLineKilogramsPerUnitSnapshot.KilogramsPerUnitScale, MidpointRounding.AwayFromZero) == figure
        && figure <= SalesLineKilogramsPerUnitSnapshot.MaximumKilogramsPerUnit;

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

    private static string SourceProof(ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence, DateOnly day,
        ProductTheoreticalKilogramsPerUnitTableConflictV1? conflict = null) =>
        conflict is null ? SourceProofJson(evidence, day, null) : SourceProofJson(evidence, day, new
        {
            selection = conflict.Selection,
            selectedKilogramsPerUnit = Text(conflict.SelectedKilogramsPerUnit),
            otherKilogramsPerUnit = Text(conflict.OtherKilogramsPerUnit),
            differenceKilogramsPerUnit = Text(conflict.OtherKilogramsPerUnit - conflict.SelectedKilogramsPerUnit),
            selectedKgPerMetre = Text(conflict.SelectedKgPerMetre),
            otherKgPerMetre = Text(conflict.OtherKgPerMetre),
            tableCorrectionRequired = true,
        });

    private static string Text(decimal value) => (value / 1.000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    /// <summary>SAT-D474 / SAT-D477: the table conflict the line's figure was resolved from (null: none recorded).</summary>
    internal static SalesKilogramsPerUnitTableConflictV1? KilogramsPerUnitConflict(SalesOrderDraft draft, Guid lineId)
    {
        if (draft.KilogramsPerUnitOf(lineId) is not { } snapshot) return null;
        using var document = JsonDocument.Parse(snapshot.SourceProof);
        if (!document.RootElement.TryGetProperty("tableConflict", out var conflict) || conflict.ValueKind != JsonValueKind.Object) return null;
        // A recorded conflict always carries every member as a string; anything else is an unreadable proof (fail closed).
        string Read(string name) => conflict.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String
            ? member.GetString()!
            : throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        return new SalesKilogramsPerUnitTableConflictV1(Read("selection"), Read("selectedKilogramsPerUnit"), Read("otherKilogramsPerUnit"),
            Read("differenceKilogramsPerUnit"), Read("selectedKgPerMetre"), Read("otherKgPerMetre"));
    }

    /// <summary>A proof without a table conflict keeps its previous shape byte for byte; a conflict is one trailing member.</summary>
    private static string SourceProofJson(ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence, DateOnly day, object? tableConflict)
    {
        var proof = BaseProof(evidence, day);
        if (tableConflict is null) return proof;
        var node = System.Text.Json.Nodes.JsonNode.Parse(proof)!.AsObject();
        node["tableConflict"] = JsonSerializer.SerializeToNode(tableConflict);
        return node.ToJsonString();
    }

    private static string BaseProof(ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence, DateOnly day) =>
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
