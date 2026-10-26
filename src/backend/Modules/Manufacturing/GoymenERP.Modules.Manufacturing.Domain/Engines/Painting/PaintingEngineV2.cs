using System.Collections.ObjectModel;
using System.Numerics;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Engines.Painting;

/// <summary>The theoretical answer for one source-ordered paint layer.</summary>
public sealed record PaintingLayerCalculationV2(
    Guid LayerId,
    decimal DryFilmLitres,
    decimal TheoreticalWetLitres,
    decimal? TheoreticalPaintKilograms);

/// <summary>
/// The pure plan answer. Actual application, cleaning consumption, measured scrap/spill, unused return,
/// purchased kilograms, Inventory movement and cost remain separate facts outside this contract.
/// </summary>
public sealed record PaintingPlanCalculationV2(
    string FormulaVersion,
    IReadOnlyList<PaintingLayerCalculationV2> OrderedLayers,
    decimal TotalDryFilmLitres,
    decimal TotalTheoreticalWetPaintLitres,
    decimal PlannedCleaningLitres,
    decimal TotalPlannedLitres,
    decimal? TotalTheoreticalPaintKilograms);

/// <summary>
/// D-M016=A pure calculator. GDC-PNT/1 remains unchanged. Version 2 uses source-ordered layers and
/// applies transfer efficiency exactly once; it has no scrap factor and makes no realization entry.
/// </summary>
public static class PaintingEngineV2
{
    public const string FormulaVersion = "GDC-PNT/2";
    public const string ArithmeticNotRepresentableCode = "painting_plan_arithmetic_not_representable";
    public const string LayersParameter = "orderedLayers";
    public const string CleaningParameter = "plannedCleaningLitres";

    private const decimal SquareMetreMicronsPerLitre = 1000m;
    private static readonly BigInteger DecimalMaximumCoefficient = (BigInteger.One << 96) - BigInteger.One;

    public static EngineResult<PaintingPlanCalculationV2> Calculate(PaintingPlanSpecV1 spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.OrderedLayers.Count == 0)
        {
            return EngineResult<PaintingPlanCalculationV2>.Unavailable([LayersParameter]);
        }

        var missing = MissingParameters(spec);
        if (missing.Count > 0)
        {
            return EngineResult<PaintingPlanCalculationV2>.Unavailable(missing);
        }

        if (spec.PlannedCleaningLitres < 0m)
        {
            return EngineResult<PaintingPlanCalculationV2>.Refused(EngineCodes.ValueNegative, CleaningParameter);
        }

        var calculatedLayers = new List<PaintingLayerCalculationV2>(spec.OrderedLayers.Count);
        decimal totalDry = 0m;
        decimal totalWet = 0m;
        decimal totalKilograms = 0m;
        var everyLayerHasDensity = true;

        for (var index = 0; index < spec.OrderedLayers.Count; index++)
        {
            var layer = spec.OrderedLayers[index];
            var prefix = $"orderedLayers[{index}]";
            var ratioRefusal = ValidatePositiveRatios(layer, prefix);
            if (ratioRefusal is not null)
            {
                return ratioRefusal;
            }

            if (!TryExactRational(
                    [layer.Surface!.AreaSquareMetres!.Value, layer.DryFilmMicrons!.Value],
                    [SquareMetreMicronsPerLitre],
                    out var dryLitres))
            {
                return ArithmeticRefusal($"{prefix}.dryFilmLitres");
            }

            if (!TryExactRational(
                    [layer.Surface.AreaSquareMetres.Value, layer.DryFilmMicrons.Value],
                    [SquareMetreMicronsPerLitre, layer.VolumetricSolidsRatio!.Value, layer.TransferEfficiency!.Value],
                    out var wetLitres))
            {
                return ArithmeticRefusal($"{prefix}.theoreticalWetLitres");
            }

            decimal? kilograms = null;
            if (layer.Density is { } density)
            {
                if (density.KilogramsPerLitre <= 0m)
                {
                    return EngineResult<PaintingPlanCalculationV2>.Refused(
                        EngineCodes.ValueNotPositive, $"{prefix}.density.kilogramsPerLitre");
                }

                if (!TryExactRational(
                        [layer.Surface.AreaSquareMetres.Value, layer.DryFilmMicrons.Value, density.KilogramsPerLitre],
                        [SquareMetreMicronsPerLitre, layer.VolumetricSolidsRatio.Value, layer.TransferEfficiency.Value],
                        out var exactKilograms))
                {
                    return ArithmeticRefusal($"{prefix}.theoreticalPaintKilograms");
                }

                kilograms = exactKilograms;
            }
            else
            {
                everyLayerHasDensity = false;
            }

            if (!TryAdd(totalDry, dryLitres, out totalDry)
                || !TryAdd(totalWet, wetLitres, out totalWet)
                || kilograms is { } knownKilograms && !TryAdd(totalKilograms, knownKilograms, out totalKilograms))
            {
                return ArithmeticRefusal("totals");
            }

            calculatedLayers.Add(new PaintingLayerCalculationV2(layer.LayerId, dryLitres, wetLitres, kilograms));
        }

        if (!TryAdd(totalWet, spec.PlannedCleaningLitres!.Value, out var totalPlanned))
        {
            return ArithmeticRefusal("totalPlannedLitres");
        }

        return EngineResult<PaintingPlanCalculationV2>.Computed(new PaintingPlanCalculationV2(
            FormulaVersion,
            new ReadOnlyCollection<PaintingLayerCalculationV2>(calculatedLayers),
            totalDry,
            totalWet,
            spec.PlannedCleaningLitres.Value,
            totalPlanned,
            everyLayerHasDensity ? totalKilograms : null));
    }

    private static List<string> MissingParameters(PaintingPlanSpecV1 spec)
    {
        var missing = new List<string>();
        for (var index = 0; index < spec.OrderedLayers.Count; index++)
        {
            var layer = spec.OrderedLayers[index];
            var prefix = $"orderedLayers[{index}]";
            AddIfNull(missing, layer.Material, $"{prefix}.material");
            AddIfNull(missing, layer.Colour, $"{prefix}.colour");
            AddIfNull(missing, layer.Surface, $"{prefix}.surface");
            if (layer.Surface is { AreaSquareMetres: null })
            {
                missing.Add($"{prefix}.surface.areaSquareMetres");
            }

            AddIfNull(missing, layer.DryFilmMicrons, $"{prefix}.dryFilmMicrons");
            AddIfNull(missing, layer.VolumetricSolidsRatio, $"{prefix}.volumetricSolidsRatio");
            AddIfNull(missing, layer.TransferEfficiency, $"{prefix}.transferEfficiency");
        }

        AddIfNull(missing, spec.PlannedCleaningLitres, CleaningParameter);
        return missing;
    }

    private static void AddIfNull<T>(ICollection<string> missing, T? value, string parameter)
    {
        if (value is null)
        {
            missing.Add(parameter);
        }
    }

    private static EngineResult<PaintingPlanCalculationV2>? ValidatePositiveRatios(
        PaintingLayerSnapshotV1 layer,
        string prefix)
    {
        if (layer.Surface!.AreaSquareMetres <= 0m)
        {
            return ValueNotPositive($"{prefix}.surface.areaSquareMetres");
        }

        if (layer.DryFilmMicrons <= 0m)
        {
            return ValueNotPositive($"{prefix}.dryFilmMicrons");
        }

        if (layer.VolumetricSolidsRatio <= 0m)
        {
            return ValueNotPositive($"{prefix}.volumetricSolidsRatio");
        }

        if (layer.VolumetricSolidsRatio > 1m)
        {
            return RatioAboveOne($"{prefix}.volumetricSolidsRatio");
        }

        if (layer.TransferEfficiency <= 0m)
        {
            return ValueNotPositive($"{prefix}.transferEfficiency");
        }

        return layer.TransferEfficiency > 1m
            ? RatioAboveOne($"{prefix}.transferEfficiency")
            : null;
    }

    private static EngineResult<PaintingPlanCalculationV2> ValueNotPositive(string parameter) =>
        EngineResult<PaintingPlanCalculationV2>.Refused(EngineCodes.ValueNotPositive, parameter);

    private static EngineResult<PaintingPlanCalculationV2> RatioAboveOne(string parameter) =>
        EngineResult<PaintingPlanCalculationV2>.Refused(EngineCodes.RatioAboveOne, parameter);

    private static EngineResult<PaintingPlanCalculationV2> ArithmeticRefusal(string parameter) =>
        EngineResult<PaintingPlanCalculationV2>.Refused(ArithmeticNotRepresentableCode, parameter);

    private static bool TryAdd(decimal left, decimal right, out decimal value)
    {
        try
        {
            value = checked(left + right);
            return true;
        }
        catch (OverflowException)
        {
            value = 0m;
            return false;
        }
    }

    private static bool TryExactRational(
        IReadOnlyList<decimal> numeratorFactors,
        IReadOnlyList<decimal> denominatorFactors,
        out decimal value)
    {
        var numerator = BigInteger.One;
        var denominator = BigInteger.One;
        foreach (var factor in numeratorFactors)
        {
            var bits = decimal.GetBits(factor);
            numerator *= Coefficient(bits);
            denominator *= PowerOfTen(Scale(bits));
        }

        foreach (var factor in denominatorFactors)
        {
            var bits = decimal.GetBits(factor);
            numerator *= PowerOfTen(Scale(bits));
            denominator *= Coefficient(bits);
        }

        return TryMaterializeExactRational(numerator, denominator, out value);
    }

    private static bool TryMaterializeExactRational(BigInteger numerator, BigInteger denominator, out decimal value)
    {
        if (denominator <= BigInteger.Zero)
        {
            value = 0m;
            return false;
        }

        var greatestCommonDivisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        numerator /= greatestCommonDivisor;
        denominator /= greatestCommonDivisor;

        var powersOfTwo = 0;
        while (denominator % 2 == BigInteger.Zero)
        {
            denominator /= 2;
            powersOfTwo++;
        }

        var powersOfFive = 0;
        while (denominator % 5 == BigInteger.Zero)
        {
            denominator /= 5;
            powersOfFive++;
        }

        if (denominator != BigInteger.One)
        {
            value = 0m;
            return false;
        }

        var scale = Math.Max(powersOfTwo, powersOfFive);
        var coefficient = numerator
            * BigInteger.Pow(2, scale - powersOfTwo)
            * BigInteger.Pow(5, scale - powersOfFive);

        while (scale > 0 && coefficient % 10 == BigInteger.Zero)
        {
            coefficient /= 10;
            scale--;
        }

        var magnitude = BigInteger.Abs(coefficient);
        if (scale > 28 || magnitude > DecimalMaximumCoefficient)
        {
            value = 0m;
            return false;
        }

        var low = unchecked((int)(uint)(magnitude & uint.MaxValue));
        var middle = unchecked((int)(uint)((magnitude >> 32) & uint.MaxValue));
        var high = unchecked((int)(uint)((magnitude >> 64) & uint.MaxValue));
        value = new decimal(low, middle, high, coefficient.Sign < 0, (byte)scale);
        return true;
    }

    private static BigInteger PowerOfTen(int scale) => BigInteger.Pow(10, scale);

    private static BigInteger Coefficient(IReadOnlyList<int> bits) =>
        ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];

    private static int Scale(IReadOnlyList<int> bits) => (bits[3] >> 16) & 0x7F;
}
