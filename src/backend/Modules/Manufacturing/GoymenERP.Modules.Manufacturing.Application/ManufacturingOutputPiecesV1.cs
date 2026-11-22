using System.Globalization;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// AK-7 (URT-D112): one counted output of the order read — the declared and net piece count, the net weighed kg, the
/// theoretical kg per piece captured at declaration and the deviation of the real kg per piece from it
/// (<see cref="OutputPieceDeviationCalculatorV1"/>). The deviation figures are null when no net piece is left. No
/// threshold is applied (URT-D51 (5) open): <see cref="DeviationThresholdApplied"/> is always false.
/// </summary>
public sealed record OutputPieceViewV1(
    Guid OutputId,
    Guid ParentRollId,
    string OutputClass,
    int DeclaredPieceCount,
    int NetPieceCount,
    string NetKg,
    string TheoreticalKgPerPiece,
    string? RealKgPerPiece,
    string? DeviationKgPerPiece,
    string? DeviationPercent,
    string FormulaVersion,
    bool DeviationThresholdApplied);

/// <summary>AK-7 (URT-D112): refusal codes of the piece side of an output declaration that the command adds.</summary>
public static class ManufacturingOutputPieceCodesV1
{
    /// <summary>
    /// The product's card names the piece unit but gives no theoretical kg per piece (no dimension, weight-table row,
    /// unit profile or policy), or its weight-table rows disagree. A counted output is refused fail-closed until the card
    /// gives the figure; nothing is guessed.
    /// </summary>
    public const string KilogramsPerPieceSourceMissing = "manufacturing_output_piece_kg_source_missing";

    /// <summary>The Product owner's theoretical kg per piece could not be read (not wired, unreadable or inconsistent).</summary>
    public const string SourceUnavailable = "manufacturing_output_piece_source_unavailable";
}

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// AK-7 (URT-D112): asks the Product owner whether the output's product is counted in pieces and, when it is, captures
    /// the theoretical kg per piece with its source evidence. The piece unit is the Settings standard identity
    /// (<see cref="SettingsUnitIdentitiesV1.Piece"/>):
    /// <list type="bullet">
    /// <item>found: counted — <paramref name="pieceCount"/> is required
    /// (<see cref="ManufacturingRuleCodes.OutputPiecesRequired"/>);</item>
    /// <item>no source because the unit is not on the card: not counted — a piece count is refused
    /// (<see cref="ManufacturingRuleCodes.OutputPiecesNotApplicable"/>);</item>
    /// <item>no source otherwise, or refused (weight conflict): counted without a figure — refused fail-closed
    /// (<see cref="ManufacturingOutputPieceCodesV1.KilogramsPerPieceSourceMissing"/>);</item>
    /// <item>not found, denied, unavailable, no port, or an answer that does not belong to the request: unavailable
    /// (<see cref="ManufacturingOutputPieceCodesV1.SourceUnavailable"/>).</item>
    /// </list>
    /// </summary>
    private async Task<(OutputPieces? Pieces, ManufacturingApiResult<OrderViewV1>? Refusal)> ReadOutputPiecesAsync(
        Guid productId, int? pieceCount, DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        if (productKilogramsPerPiece is null || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return (null, PieceSourceUnavailable());
        }

        var companyId = companyContext.CompanyId;
        var answer = await productKilogramsPerPiece.ReadAsync(productId, SettingsUnitIdentitiesV1.Piece, effectiveOn,
            cancellationToken);
        if (answer is null || answer.ContractVersion != ProductTheoreticalKilogramsPerUnitContractV1.Version
            || !companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            return (null, PieceSourceUnavailable());
        }

        switch (answer.Outcome)
        {
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource
                when answer.Code == ProductTheoreticalKilogramsPerUnitCodesV1.UnitNotOnCard:
                return pieceCount is null
                    ? (null, null)
                    : throw new ManufacturingRuleException(ManufacturingRuleCodes.OutputPiecesNotApplicable,
                        "Bu ürün adetle izlenmiyor; çıktı adet taşımaz.");
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource:
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused:
                return (null, new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Unprocessable, null, false,
                    ManufacturingOutputPieceCodesV1.KilogramsPerPieceSourceMissing));
            case ProductTheoreticalKilogramsPerUnitOutcomeV1.Found:
                break;
            default:
                return (null, PieceSourceUnavailable());
        }

        if (answer is not { KilogramsPerUnit: { } kilogramsPerPiece, Evidence: { } evidence }
            || evidence.CompanyId != companyId || evidence.ProductId != productId
            || evidence.UnitId != SettingsUnitIdentitiesV1.Piece || evidence.UnitKind != ProductPhysicalUnitKindV1.Piece)
        {
            return (null, PieceSourceUnavailable());
        }

        if (pieceCount is not { } count)
        {
            throw new ManufacturingRuleException(ManufacturingRuleCodes.OutputPiecesRequired,
                "Bu ürün adetle izleniyor; çıktı adet ve tartılan kg ile beyan edilir.");
        }

        OutputTheoreticalKilogramsPerPiece theoretical;
        try
        {
            theoretical = new OutputTheoreticalKilogramsPerPiece(kilogramsPerPiece, TheoreticalSourceVersion(evidence));
        }
        catch (ArgumentException)
        {
            // A figure finer than the stored scale, or evidence too long to keep, is never cut: unavailable.
            return (null, PieceSourceUnavailable());
        }

        return (new OutputPieces(new PieceCount(count), theoretical), null);
    }

    /// <summary>
    /// AK-7: the snapshot marker of the captured figure — every source version it was computed from, in a fixed order
    /// (the same marker the Inventory kg/unit adapter keeps).
    /// </summary>
    internal static string TheoreticalSourceVersion(ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence) => string.Join(';',
        "product:" + evidence.ProductVersion,
        "formula:" + evidence.FormulaVersion,
        "dimension:" + VersionText(evidence.DimensionVersion),
        "weight-table:" + VersionText(evidence.WeightTableVersion),
        "policy:" + (evidence.PolicyId is { } policy ? policy.ToString("D") + "/" + VersionText(evidence.PolicyVersion) : "-"),
        "unit-profile:" + evidence.UnitProfileId.ToString("D") + "/" + VersionText(evidence.UnitProfileVersion),
        "unit-semantics:" + evidence.UnitSemanticsVersion);

    private static string VersionText(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static ManufacturingApiResult<OrderViewV1> PieceSourceUnavailable() =>
        new(ManufacturingApiStatus.Unavailable, null, false, ManufacturingOutputPieceCodesV1.SourceUnavailable);

    /// <summary>AK-7: the counted outputs of the order read, in declaration order; null when the order has none.</summary>
    internal static IReadOnlyList<OutputPieceViewV1>? OutputPieceViews(ProductionOrder order)
    {
        var counted = order.Outputs.Where(output => output.Pieces is not null).ToList();
        if (counted.Count == 0)
        {
            return null;
        }

        return
        [
            .. counted.Select(output =>
            {
                var pieces = output.Pieces!;
                var netMass = order.NetMassOf(output.OutputId);
                var netPieces = order.NetPiecesOf(output.OutputId)!.Value;
                var deviation = OutputPieceDeviationCalculatorV1.Calculate(netMass, netPieces,
                    pieces.Theoretical.KilogramsPerPiece);
                return new OutputPieceViewV1(output.OutputId, output.ParentRollId, output.Class.ToString(),
                    pieces.Count.Value, netPieces.Value, Text(netMass.Value), Text(pieces.Theoretical.KilogramsPerPiece),
                    deviation is null ? null : Text(deviation.RealKilogramsPerPiece),
                    deviation is null ? null : Text(deviation.DeviationKilogramsPerPiece),
                    deviation is null ? null : Text(deviation.DeviationPercent),
                    OutputPieceDeviationCalculatorV1.FormulaVersion, DeviationThresholdApplied: false);
            }),
        ];
    }
}
