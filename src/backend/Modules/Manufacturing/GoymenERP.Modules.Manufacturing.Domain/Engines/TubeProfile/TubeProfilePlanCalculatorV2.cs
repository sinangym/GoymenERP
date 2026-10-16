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

    private const decimal MillimetresPerMetre = 1000m;

    public static TubeProfilePlanCalculationV2 Calculate(TubeProfilePlanSpecV1 spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var totalMetres = spec.TargetPieceCount * spec.TargetLengthMm / MillimetresPerMetre;
        var quantity = EngineResult<TubeProfilePlanQuantityV2>.Computed(
            new TubeProfilePlanQuantityV2(FormulaVersion, spec.TargetPieceCount, totalMetres));

        var package = spec.PackageStandardSnapshot is { } packageStandard
            ? Package(spec.TargetPieceCount, packageStandard.PiecesPerPackage)
            : EngineResult<TubeProfilePlanPackageV2>.Unavailable([PackageStandardParameter]);

        var duration = spec.LineSpeedSnapshot is { } speed
            ? EngineResult<TubeProfilePlanDurationV2>.Computed(
                new TubeProfilePlanDurationV2(FormulaVersion, totalMetres / speed.MetresPerMinute))
            : EngineResult<TubeProfilePlanDurationV2>.Unavailable([LineSpeedParameter]);

        var mass = spec.VerifiedUnitKgSnapshot is { } verifiedUnitKg
            ? EngineResult<TubeProfilePlanMassV2>.Computed(
                new TubeProfilePlanMassV2(FormulaVersion, spec.TargetPieceCount * verifiedUnitKg.UnitKg))
            : EngineResult<TubeProfilePlanMassV2>.Unavailable([VerifiedUnitKgParameter]);

        return new TubeProfilePlanCalculationV2(quantity, package, duration, mass);
    }

    private static EngineResult<TubeProfilePlanPackageV2> Package(int targetPieces, int piecesPerPackage)
    {
        var fullPackages = targetPieces / piecesPerPackage;
        var remainderPieces = targetPieces % piecesPerPackage;
        var suggestedPackageCount = fullPackages + (remainderPieces > 0 ? 1 : 0);
        return EngineResult<TubeProfilePlanPackageV2>.Computed(
            new TubeProfilePlanPackageV2(FormulaVersion, fullPackages, remainderPieces, suggestedPackageCount));
    }
}
