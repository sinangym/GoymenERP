using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Engines.ColdRolling;

/// <summary>The explicit WIP mass, density and versioned pass recipe required by GDC-CR/2.</summary>
public sealed record ColdRollingPassInputV2(
    decimal? WipKg,
    decimal? DensityKgPerCubicMetre,
    ColdRollingPassSpec? PassSpec);

/// <summary>One ideal, lossless pass calculation; identity and source order are preserved.</summary>
public sealed record ColdRollingPassLineResultV2(
    Guid LineId,
    int SourceSequence,
    decimal InputThicknessMm,
    decimal InputWidthMm,
    decimal OutputThicknessMm,
    decimal OutputWidthMm,
    decimal InputMetres,
    decimal OutputMetres,
    decimal ReductionPercent,
    decimal ElongationRatio);

/// <summary>GDC-CR/2 result for one versioned pass recipe. It creates no stock or production movement.</summary>
public sealed record ColdRollingPassResultV2(
    string FormulaVersion,
    Guid PassRecipeId,
    string PassRecipeVersion,
    decimal WipKg,
    decimal DensityKgPerCubicMetre,
    IReadOnlyList<ColdRollingPassLineResultV2> Lines);

/// <summary>
/// Pure multi-pass cold-rolling calculator selected by D-M014=A. Every pass uses the same explicit WIP
/// mass and density. No loss, scrap, speed, force, tolerance, setup, quality or machine default exists.
/// All decimal results are unrounded.
/// </summary>
public static class ColdRollingEngineV2
{
    public const string FormulaVersion = "GDC-CR/2";

    private const decimal SquareMillimetresPerSquareMetre = 1_000_000m;

    public static EngineResult<ColdRollingPassResultV2> Roll(ColdRollingPassInputV2 input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var missing = new List<string>();
        if (input.WipKg is null)
        {
            missing.Add("wipKg");
        }

        if (input.DensityKgPerCubicMetre is null)
        {
            missing.Add("densityKgPerCubicMetre");
        }

        if (input.PassSpec is null)
        {
            missing.Add("passSpec");
        }

        if (input.WipKg is { } wipKg)
        {
            if (wipKg <= 0m)
            {
                return EngineResult<ColdRollingPassResultV2>.Refused(EngineCodes.ValueNotPositive, "wipKg");
            }

            if (DecimalScale(wipKg) > 3)
            {
                return EngineResult<ColdRollingPassResultV2>.Refused(EngineCodes.PrecisionExceeded, "wipKg");
            }
        }

        if (input.DensityKgPerCubicMetre is { } density && density <= 0m)
        {
            return EngineResult<ColdRollingPassResultV2>.Refused(
                EngineCodes.ValueNotPositive, "densityKgPerCubicMetre");
        }

        if (missing.Count > 0)
        {
            return EngineResult<ColdRollingPassResultV2>.Unavailable(missing);
        }

        var mass = input.WipKg!.Value;
        var densityValue = input.DensityKgPerCubicMetre!.Value;
        var spec = input.PassSpec!;
        var lines = spec.Lines.Select(line =>
        {
            var inputMetres = Metres(mass, line.InputThicknessMm, line.InputWidthMm, densityValue);
            var outputMetres = Metres(mass, line.OutputThicknessMm, line.OutputWidthMm, densityValue);
            return new ColdRollingPassLineResultV2(
                line.LineId,
                line.SourceSequence,
                line.InputThicknessMm,
                line.InputWidthMm,
                line.OutputThicknessMm,
                line.OutputWidthMm,
                inputMetres,
                outputMetres,
                (line.InputThicknessMm - line.OutputThicknessMm) * 100m / line.InputThicknessMm,
                outputMetres / inputMetres);
        }).ToArray();

        return EngineResult<ColdRollingPassResultV2>.Computed(new ColdRollingPassResultV2(
            FormulaVersion,
            spec.PassRecipeId,
            spec.Version,
            mass,
            densityValue,
            Array.AsReadOnly(lines)));
    }

    private static decimal Metres(decimal massKg, decimal thicknessMm, decimal widthMm, decimal densityKgPerCubicMetre) =>
        massKg * SquareMillimetresPerSquareMetre / (thicknessMm * widthMm * densityKgPerCubicMetre);

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;
}
