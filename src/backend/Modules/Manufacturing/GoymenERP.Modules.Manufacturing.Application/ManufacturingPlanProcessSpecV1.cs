using System.Text.Json.Nodes;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Application;

// Üretim U2-b1 (API-12; URT-D25, URT-D26, URT-D27): the Tube / Profile, Paint and Cold Rolling values a plan enters
// itself. Lengths, perimeter, thickness and unit kg travel as lossless invariant text (Ö2) like every other measure here;
// counts are JSON integers like the slit and cut line counts. The package standard and the paint product would come from
// Product; until that module starts they are entered by hand ("ertelendi: Ürün başlayınca").

/// <summary>
/// The process values entered on a plan, all optional. Tube / Profile: <see cref="PackageCount"/>,
/// <see cref="PiecesPerPackage"/>, <see cref="PieceLengthMm"/>, <see cref="UnitKg"/>. Paint: <see cref="PackageCount"/>,
/// <see cref="PiecesPerPackage"/>, <see cref="PieceLengthMm"/> (the profile length), <see cref="PerimeterMm"/>,
/// <see cref="PaintDescription"/> (paint type / supplier, required on Paint) and <see cref="PaintColorCode"/>.
/// Cold Rolling: <see cref="TargetThicknessMm"/>. A value of another process is refused.
/// </summary>
public sealed record PlanProcessSpecRequestV1(
    int? PackageCount = null,
    int? PiecesPerPackage = null,
    string? PieceLengthMm = null,
    string? UnitKg = null,
    string? PerimeterMm = null,
    string? TargetThicknessMm = null,
    string? PaintDescription = null,
    string? PaintColorCode = null);

/// <summary>The plan's entered process values as lossless text; an absent value is null.</summary>
public sealed record PlanProcessSpecViewV1(
    int? PackageCount,
    int? PiecesPerPackage,
    string? PieceLengthMm,
    string? UnitKg,
    string? PerimeterMm,
    string? TargetThicknessMm,
    string? PaintDescription,
    string? PaintColorCode);

public sealed partial class ManufacturingCommandService
{
    /// <summary>The request field named when the process spec is refused.</summary>
    private const string ProcessSpecField = "processSpec";

    /// <summary>
    /// The grammar of the process spec: null when not supplied; <c>Invalid</c> for a non-positive count, a measure that is
    /// not lossless positive text within its scale, an empty or over-long text, or a spec without any value.
    /// </summary>
    private static (PlanProcessSpec? Value, bool Invalid) ParseProcessSpec(PlanProcessSpecRequestV1? request)
    {
        if (request is null)
        {
            return (null, false);
        }

        static (decimal? Value, bool Invalid) Measure(string? text, int scale)
        {
            if (text is null)
            {
                return (null, false);
            }

            return ParseKg(text) is { } value && value > 0m && value.Scale <= scale ? (value, false) : (null, true);
        }

        var pieceLength = Measure(request.PieceLengthMm, PlanProcessSpec.MeasureScale);
        var unitKg = Measure(request.UnitKg, PlanProcessSpec.UnitKgScale);
        var perimeter = Measure(request.PerimeterMm, PlanProcessSpec.MeasureScale);
        var targetThickness = Measure(request.TargetThicknessMm, PlanProcessSpec.MeasureScale);
        if (pieceLength.Invalid || unitKg.Invalid || perimeter.Invalid || targetThickness.Invalid)
        {
            return (null, true);
        }

        try
        {
            return (new PlanProcessSpec(request.PackageCount, request.PiecesPerPackage, pieceLength.Value,
                unitKg.Value, perimeter.Value, targetThickness.Value, request.PaintDescription,
                request.PaintColorCode), false);
        }
        catch (ArgumentException)
        {
            return (null, true);
        }
    }

    /// <summary>A process spec belongs only to the process whose values it carries (a Paint spec names its paint).</summary>
    private static bool ProcessSpecAllowed(PlanProcessSpec? spec, ManufacturingProcess process)
    {
        if (spec is null)
        {
            return true;
        }

        try
        {
            spec.RequireAllowedFor(process, ProcessSpecField);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// ADR-011 compatibility: a supplied process spec joins the request hash as one canonical member; an absent one keeps
    /// every request shape published before with its exact hash.
    /// </summary>
    private static object WithCanonicalProcessSpec(object canonical, PlanProcessSpec? spec)
    {
        if (spec is null)
        {
            return canonical;
        }

        var node = canonical as JsonObject ?? System.Text.Json.JsonSerializer.SerializeToNode(canonical, Json)!.AsObject();
        node["processSpec"] = System.Text.Json.JsonSerializer.SerializeToNode(ProcessSpecView(spec), Json);
        return node;
    }

    internal static PlanProcessSpecViewV1? ProcessSpecView(PlanProcessSpec? spec) => spec is null
        ? null
        : new PlanProcessSpecViewV1(spec.PackageCount, spec.PiecesPerPackage,
            spec.PieceLengthMm is { } pieceLength ? Text(pieceLength) : null,
            spec.UnitKg is { } unitKg ? Text(unitKg) : null,
            spec.PerimeterMm is { } perimeter ? Text(perimeter) : null,
            spec.TargetThicknessMm is { } thickness ? Text(thickness) : null,
            spec.PaintDescription, spec.PaintColorCode);
}
