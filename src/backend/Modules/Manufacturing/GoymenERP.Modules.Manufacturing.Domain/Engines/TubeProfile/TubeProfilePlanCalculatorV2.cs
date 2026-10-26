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

        var quantity = Quantity(spec);

        var package = spec.PackageStandardSnapshot is { } packageStandard
            ? Package(spec.TargetPieceCount, packageStandard.PiecesPerPackage)
            : EngineResult<TubeProfilePlanPackageV2>.Unavailable([PackageStandardParameter]);

        var duration = Duration(spec);

        var mass = Mass(spec);

        return new TubeProfilePlanCalculationV2(quantity, package, duration, mass);
    }

    private static EngineResult<TubeProfilePlanQuantityV2> Quantity(TubeProfilePlanSpecV1 spec)
    {
        var lengthBits = decimal.GetBits(spec.TargetLengthMm);
        var numerator = Coefficient(lengthBits) * spec.TargetPieceCount;
        var denominator = (BigInteger)MillimetresPerMetre * PowerOfTen(Scale(lengthBits));
        if (TryMaterializeExactRational(numerator, denominator, out var metres))
        {
            return EngineResult<TubeProfilePlanQuantityV2>.Computed(
                new TubeProfilePlanQuantityV2(FormulaVersion, spec.TargetPieceCount, metres));
        }

        return EngineResult<TubeProfilePlanQuantityV2>.Refused(
            ArithmeticNotRepresentableCode, TotalMetresResult);
    }

    private static EngineResult<TubeProfilePlanDurationV2> Duration(TubeProfilePlanSpecV1 spec)
    {
        if (spec.LineSpeedSnapshot is not { } speed)
        {
            return EngineResult<TubeProfilePlanDurationV2>.Unavailable([LineSpeedParameter]);
        }

        var lengthBits = decimal.GetBits(spec.TargetLengthMm);
        var speedBits = decimal.GetBits(speed.MetresPerMinute);
        var numerator = Coefficient(lengthBits)
            * spec.TargetPieceCount
            * PowerOfTen(Scale(speedBits));
        var denominator = (BigInteger)MillimetresPerMetre
            * Coefficient(speedBits)
            * PowerOfTen(Scale(lengthBits));
        if (TryMaterializeExactRational(numerator, denominator, out var runMinutes))
        {
            return EngineResult<TubeProfilePlanDurationV2>.Computed(
                new TubeProfilePlanDurationV2(FormulaVersion, runMinutes));
        }

        return UnrepresentableDuration();
    }

    private static EngineResult<TubeProfilePlanMassV2> Mass(TubeProfilePlanSpecV1 spec)
    {
        if (spec.VerifiedUnitKgSnapshot is not { } verifiedUnitKg)
        {
            return EngineResult<TubeProfilePlanMassV2>.Unavailable([VerifiedUnitKgParameter]);
        }

        var unitKgBits = decimal.GetBits(verifiedUnitKg.UnitKg);
        var numerator = Coefficient(unitKgBits) * spec.TargetPieceCount;
        var denominator = PowerOfTen(Scale(unitKgBits));
        if (TryMaterializeExactRational(numerator, denominator, out var theoreticalKg))
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

    private static bool TryMaterializeExactRational(
        BigInteger numerator,
        BigInteger denominator,
        out decimal value)
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

        while (scale > 0 && coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }

        var coefficientMagnitude = BigInteger.Abs(coefficient);
        if (scale > 28 || coefficientMagnitude > DecimalMaximumCoefficient)
        {
            value = 0m;
            return false;
        }

        var low = unchecked((int)(uint)(coefficientMagnitude & uint.MaxValue));
        var middle = unchecked((int)(uint)((coefficientMagnitude >> 32) & uint.MaxValue));
        var high = unchecked((int)(uint)((coefficientMagnitude >> 64) & uint.MaxValue));
        value = new decimal(low, middle, high, coefficient.Sign < 0, (byte)scale);
        return true;
    }

    private static BigInteger PowerOfTen(int scale) => BigInteger.Pow(10, scale);

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
