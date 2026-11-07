using System.Numerics;
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
/// No business rounding or tolerance is applied. Exact rational formula results are materialized as CLR
/// decimals using deterministic round-to-nearest, ties-to-even; values outside the non-zero decimal range refuse.
/// </summary>
public static class ColdRollingEngineV2
{
    public const string FormulaVersion = "GDC-CR/2";

    private const decimal SquareMillimetresPerSquareMetre = 1_000_000m;
    private const string ArithmeticParameter = "coldRollingPassArithmetic";
    private static readonly BigInteger MaxDecimalCoefficient = (BigInteger.One << 96) - BigInteger.One;

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

            if (decimal.Round(wipKg, 3) != wipKg)
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
        var lines = new List<ColdRollingPassLineResultV2>(spec.Lines.Count);
        foreach (var line in spec.Lines)
        {
            if (!TryEvaluate(
                    [mass, SquareMillimetresPerSquareMetre],
                    [line.InputThicknessMm, line.InputWidthMm, densityValue],
                    out var inputMetres)
                || !TryEvaluate(
                    [mass, SquareMillimetresPerSquareMetre],
                    [line.OutputThicknessMm, line.OutputWidthMm, densityValue],
                    out var outputMetres)
                || !TryEvaluate(
                    [line.InputThicknessMm - line.OutputThicknessMm, 100m],
                    [line.InputThicknessMm],
                    out var reductionPercent)
                || !TryEvaluate(
                    [line.InputThicknessMm, line.InputWidthMm],
                    [line.OutputThicknessMm, line.OutputWidthMm],
                    out var elongationRatio))
            {
                return EngineResult<ColdRollingPassResultV2>.Refused(
                    EngineCodes.ArithmeticUnrepresentable,
                    ArithmeticParameter);
            }

            lines.Add(new ColdRollingPassLineResultV2(
                line.LineId,
                line.SourceSequence,
                line.InputThicknessMm,
                line.InputWidthMm,
                line.OutputThicknessMm,
                line.OutputWidthMm,
                inputMetres,
                outputMetres,
                reductionPercent,
                elongationRatio));
        }

        return EngineResult<ColdRollingPassResultV2>.Computed(new ColdRollingPassResultV2(
            FormulaVersion,
            spec.PassRecipeId,
            spec.Version,
            mass,
            densityValue,
            lines.AsReadOnly()));
    }

    private static bool TryEvaluate(
        IReadOnlyList<decimal> numeratorFactors,
        IReadOnlyList<decimal> denominatorFactors,
        out decimal value)
    {
        var numerator = BigInteger.One;
        var denominator = BigInteger.One;

        foreach (var factor in numeratorFactors)
        {
            AppendFactor(factor, ref numerator, ref denominator);
        }

        foreach (var factor in denominatorFactors)
        {
            AppendFactor(factor, ref denominator, ref numerator);
        }

        var common = BigInteger.GreatestCommonDivisor(numerator, denominator);
        numerator /= common;
        denominator /= common;

        if (numerator > MaxDecimalCoefficient * denominator
            || numerator * BigInteger.Pow(10, 28) < denominator)
        {
            value = default;
            return false;
        }

        for (var scale = 28; scale >= 0; scale--)
        {
            var scaledNumerator = numerator * BigInteger.Pow(10, scale);
            var coefficient = BigInteger.DivRem(scaledNumerator, denominator, out var remainder);
            var twiceRemainder = remainder * 2;
            if (twiceRemainder > denominator
                || (twiceRemainder == denominator && !coefficient.IsEven))
            {
                coefficient++;
            }

            if (coefficient <= 0 || coefficient > MaxDecimalCoefficient)
            {
                continue;
            }

            while (scale > 0 && coefficient % 10 == 0)
            {
                coefficient /= 10;
                scale--;
            }

            value = new decimal(
                (int)(uint)(coefficient & uint.MaxValue),
                (int)(uint)((coefficient >> 32) & uint.MaxValue),
                (int)(uint)((coefficient >> 64) & uint.MaxValue),
                isNegative: false,
                (byte)scale);
            return true;
        }

        value = default;
        return false;
    }

    private static void AppendFactor(decimal factor, ref BigInteger numerator, ref BigInteger denominator)
    {
        var bits = decimal.GetBits(factor);
        var coefficient = (BigInteger)(uint)bits[0]
            | ((BigInteger)(uint)bits[1] << 32)
            | ((BigInteger)(uint)bits[2] << 64);
        var scale = (bits[3] >> 16) & 0x7F;

        numerator *= coefficient;
        denominator *= BigInteger.Pow(10, scale);
    }
}
