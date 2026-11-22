using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// AK-7 (URT-D112): the deviation of a counted output's real kilograms per piece from the theoretical figure captured
/// when it was declared. One deterministic calculator; the order read and the screen show its answer and never compute it
/// again.
/// <list type="bullet">
/// <item>real kg/piece = net weighed kg ÷ net piece count — the mass-balance engine's BAL-006
/// (<see cref="MassBalanceEngineV1.ActualUnitMass"/>, unrounded);</item>
/// <item>deviation kg/piece = real kg/piece − theoretical kg/piece;</item>
/// <item>deviation percent = deviation kg/piece ÷ theoretical kg/piece × 100.</item>
/// </list>
/// Every figure is computed exactly and rounded once, at the end, half away from zero (the house display rule, SAT-D436):
/// kilograms per piece to <see cref="Kilograms.Scale"/> decimals (the kilogram's own scale) and the percent to
/// <see cref="PercentScale"/> decimals (ASSUMPTION-AK7-1: display scale, reversible). An output with no net pieces left
/// (fully corrected) has no deviation. No threshold is applied: the deviation threshold is open (URT-D51 (5)).
/// </summary>
public static class OutputPieceDeviationCalculatorV1
{
    public const string FormulaVersion = "manufacturing.output-piece-deviation.v1";

    /// <summary>ASSUMPTION-AK7-1: decimals of the shown deviation percent.</summary>
    public const int PercentScale = 2;

    private const decimal PercentFactor = 100m;

    public static OutputPieceDeviationV1? Calculate(Kilograms netMass, PieceCount netPieces, decimal theoreticalKilogramsPerPiece)
    {
        if (theoreticalKilogramsPerPiece <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(theoreticalKilogramsPerPiece), theoreticalKilogramsPerPiece,
                "Teorik kg/adet pozitif olmalıdır.");
        }

        if (netPieces.Value == 0)
        {
            return null;
        }

        var unit = MassBalanceEngineV1.ActualUnitMass(new ActualUnitMassInputV1(netMass.Value, netPieces.Value));
        var real = unit.Value?.ActualUnitKg
            ?? throw new ArgumentException("Net adedi olan çıktının net kütlesi pozitif olmalıdır.", nameof(netMass));
        var deviation = real - theoreticalKilogramsPerPiece;
        return new OutputPieceDeviationV1(
            decimal.Round(real, Kilograms.Scale, MidpointRounding.AwayFromZero),
            decimal.Round(deviation, Kilograms.Scale, MidpointRounding.AwayFromZero),
            decimal.Round(deviation / theoreticalKilogramsPerPiece * PercentFactor, PercentScale,
                MidpointRounding.AwayFromZero));
    }
}

/// <summary>AK-7: the calculator's answer, each figure rounded once (see <see cref="OutputPieceDeviationCalculatorV1"/>).</summary>
public sealed record OutputPieceDeviationV1(
    decimal RealKilogramsPerPiece, decimal DeviationKilogramsPerPiece, decimal DeviationPercent);
