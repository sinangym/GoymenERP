using System.Numerics;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Engines.TubeProfile;

/// <summary>The authoritative target quantity and its exact total length.</summary>
public sealed record TubeProfilePlanQuantityV2(string FormulaVersion, int TargetPieces, decimal TotalMetres);

/// <summary>A grouping suggestion only; it is not a physical package, label, output or scrap fact.</summary>
public sealed record TubeProfilePlanPackageV2(
    string FormulaVersion,
    int FullPackages,
    int RemainderPieces,
    int SuggestedPackageCount);

/// <summary>Running time only; setup, changeover, planned stops and cleaning remain outside it.</summary>
public sealed record TubeProfilePlanDurationV2(string FormulaVersion, decimal RunMinutes);

/// <summary>Theoretical target mass from an owner-verified unit kg, kept unrounded.</summary>
public sealed record TubeProfilePlanMassV2(string FormulaVersion, decimal TheoreticalKg);

/// <summary>
/// The four independent GDC-TUB/2 answers. Missing optional sources affect only their own answer;
/// the required target quantity and total metres always remain visible.
/// </summary>
public sealed record TubeProfilePlanCalculationV2(
    EngineResult<TubeProfilePlanQuantityV2> Quantity,
    EngineResult<TubeProfilePlanPackageV2> Package,
    EngineResult<TubeProfilePlanDurationV2> Duration,
    EngineResult<TubeProfilePlanMassV2> TheoreticalMass);

/// <summary>
/// D-M015=A pure plan calculator. GDC-TUB/1 is deliberately unchanged: version 2 makes target
/// pieces authoritative and treats package grouping, speed and unit kg as separate snapshot inputs.
/// </summary>
public static class TubeProfilePlanCalculatorV2
{
    public const string FormulaVersion = "GDC-TUB/2";
    public const string PackageStandardParameter = "packageStandard";
    public const string LineSpeedParameter = "lineSpeed";
    public const string VerifiedUnitKgParameter = "verifiedUnitKg";
    public const string ArithmeticNotRepresentableCode = "tube_profile_plan_arithmetic_not_representable";
    public const string TotalMetresResult = "totalMetres";
    public const string RunMinutesResult = "runMinutes";
    public const string TheoreticalKgResult = "theoreticalKg";

    private const decimal MillimetresPerMetre = 1000m;
    private static readonly BigInteger DecimalMaximumCoefficient = (BigInteger.One << 96) - BigInteger.One;

    public static TubeProfilePlanCalculationV2 Calculate(TubeProfilePlanSpecV1 spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var quantity = Quantity(spec, out var totalMetres);

        var package = spec.PackageStandardSnapshot is { } packageStandard
            ? Package(spec.TargetPieceCount, packageStandard.PiecesPerPackage)
            : EngineResult<TubeProfilePlanPackageV2>.Unavailable([PackageStandardParameter]);

        var duration = Duration(spec, totalMetres);

        var mass = Mass(spec);

        return new TubeProfilePlanCalculationV2(quantity, package, duration, mass);
    }

    private static EngineResult<TubeProfilePlanQuantityV2> Quantity(
        TubeProfilePlanSpecV1 spec,
        out decimal? totalMetres)
    {
        // TargetLengthMm has at most three decimal places, so reducing the millimetre scale first is exact.
        // It also avoids an intermediate overflow when the final metre value is representable.
        var scaledLength = spec.TargetLengthMm / MillimetresPerMetre;
        if (TryExactProduct(scaledLength, spec.TargetPieceCount, out var metres))
        {
            totalMetres = metres;
            return EngineResult<TubeProfilePlanQuantityV2>.Computed(
                new TubeProfilePlanQuantityV2(FormulaVersion, spec.TargetPieceCount, metres));
        }

        totalMetres = null;
        return EngineResult<TubeProfilePlanQuantityV2>.Refused(
            ArithmeticNotRepresentableCode, TotalMetresResult);
    }

    private static EngineResult<TubeProfilePlanDurationV2> Duration(
        TubeProfilePlanSpecV1 spec,
        decimal? totalMetres)
    {
        if (spec.LineSpeedSnapshot is not { } speed)
        {
            return EngineResult<TubeProfilePlanDurationV2>.Unavailable([LineSpeedParameter]);
        }

        if (totalMetres is { } metres)
        {
            try
            {
                var runMinutes = metres / speed.MetresPerMinute;
                return runMinutes == 0m
                    ? UnrepresentableDuration()
                    : EngineResult<TubeProfilePlanDurationV2>.Computed(
                        new TubeProfilePlanDurationV2(FormulaVersion, runMinutes));
            }
            catch (OverflowException)
            {
                return UnrepresentableDuration();
            }
        }

        // Quantity may be outside decimal's exact range while duration is representable. Prefer cancelling the
        // count against speed exactly; otherwise divide the already-scaled length before the exact multiplication.
        var scaledLength = spec.TargetLengthMm / MillimetresPerMetre;
        if (TryCancelledDuration(scaledLength, spec.TargetPieceCount, speed.MetresPerMinute, out var independentMinutes)
            || TryPerPieceDuration(scaledLength, spec.TargetPieceCount, speed.MetresPerMinute, out independentMinutes))
        {
            return EngineResult<TubeProfilePlanDurationV2>.Computed(
                new TubeProfilePlanDurationV2(FormulaVersion, independentMinutes));
        }

        return UnrepresentableDuration();
    }

    private static EngineResult<TubeProfilePlanMassV2> Mass(TubeProfilePlanSpecV1 spec)
    {
        if (spec.VerifiedUnitKgSnapshot is not { } verifiedUnitKg)
        {
            return EngineResult<TubeProfilePlanMassV2>.Unavailable([VerifiedUnitKgParameter]);
        }

        if (TryExactProduct(verifiedUnitKg.UnitKg, spec.TargetPieceCount, out var theoreticalKg))
        {
            return EngineResult<TubeProfilePlanMassV2>.Computed(
                new TubeProfilePlanMassV2(FormulaVersion, theoreticalKg));
        }

        return EngineResult<TubeProfilePlanMassV2>.Refused(
            ArithmeticNotRepresentableCode, TheoreticalKgResult);
    }

    private static EngineResult<TubeProfilePlanDurationV2> UnrepresentableDuration() =>
        EngineResult<TubeProfilePlanDurationV2>.Refused(
            ArithmeticNotRepresentableCode, RunMinutesResult);

    private static bool TryCancelledDuration(
        decimal scaledLength,
        int targetPieces,
        decimal speed,
        out decimal runMinutes)
    {
        try
        {
            var countOverSpeed = targetPieces / speed;
            if (countOverSpeed != 0m
                && TryExactProduct(countOverSpeed, speed, out var recoveredCount)
                && recoveredCount == targetPieces
                && TryExactProduct(scaledLength, countOverSpeed, out runMinutes)
                && runMinutes != 0m)
            {
                return true;
            }
        }
        catch (OverflowException)
        {
            // The other safe order can still be representable when this intermediate is not.
        }

        runMinutes = 0m;
        return false;
    }

    private static bool TryPerPieceDuration(
        decimal scaledLength,
        int targetPieces,
        decimal speed,
        out decimal runMinutes)
    {
        runMinutes = 0m;
        try
        {
            var perPieceMinutes = scaledLength / speed;
            return perPieceMinutes != 0m
                && TryExactProduct(perPieceMinutes, targetPieces, out runMinutes)
                && runMinutes != 0m;
        }
        catch (OverflowException)
        {
            runMinutes = 0m;
            return false;
        }
    }

    private static bool TryExactProduct(decimal left, decimal right, out decimal product)
    {
        var leftBits = decimal.GetBits(left);
        var rightBits = decimal.GetBits(right);
        var coefficient = Coefficient(leftBits) * Coefficient(rightBits);
        var scale = Scale(leftBits) + Scale(rightBits);

        while (scale > 0 && coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }

        if (scale > 28 || coefficient > DecimalMaximumCoefficient)
        {
            product = 0m;
            return false;
        }

        var low = unchecked((int)(uint)(coefficient & uint.MaxValue));
        var middle = unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue));
        var high = unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue));
        var negative = (leftBits[3] < 0) != (rightBits[3] < 0);
        product = new decimal(low, middle, high, negative, (byte)scale);
        return true;
    }

    private static BigInteger Coefficient(IReadOnlyList<int> bits) =>
        ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];

    private static int Scale(IReadOnlyList<int> bits) => (bits[3] >> 16) & 0x7F;

    private static EngineResult<TubeProfilePlanPackageV2> Package(int targetPieces, int piecesPerPackage)
    {
        var fullPackages = targetPieces / piecesPerPackage;
        var remainderPieces = targetPieces % piecesPerPackage;
        var suggestedPackageCount = fullPackages + (remainderPieces > 0 ? 1 : 0);
        return EngineResult<TubeProfilePlanPackageV2>.Computed(
            new TubeProfilePlanPackageV2(FormulaVersion, fullPackages, remainderPieces, suggestedPackageCount));
    }
}
