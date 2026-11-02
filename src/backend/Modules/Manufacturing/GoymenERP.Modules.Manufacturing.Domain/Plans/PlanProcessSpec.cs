namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>One plan-entered engine value as the approval snapshot captures it (source <c>Plan</c>, URT-D9).</summary>
public sealed record PlanEnteredValue(string Key, string Unit, decimal Number);

/// <summary>
/// Üretim U2-b1 (API-12; URT-D25, URT-D26, URT-D27): the values a Tube / Profile, Paint or Cold Rolling plan enters
/// itself for its engines. Every value is optional: one that is absent stays unknown and the engine names it (fail-closed),
/// never defaulted. The package standard and the paint product would come from Product; until that module starts they are
/// entered here by hand ("ertelendi: Ürün başlayınca"). Part of the plan's content: a Draft revision replaces it with the
/// rest, and the approval snapshot captures the numbers with source <c>Plan</c>.
/// <list type="bullet">
/// <item>Tube / Profile: package count, pieces per package (URT-D27: proposed later from the product, editable, captured),
/// piece length, unit kg.</item>
/// <item>Paint: package count, pieces per package, profile length (<see cref="PieceLengthMm"/>), perimeter, the paint
/// (type / supplier, required) and an optional color code (URT-D25).</item>
/// <item>Cold Rolling: the target thickness of the single pass (URT-D26).</item>
/// </list>
/// </summary>
public sealed record PlanProcessSpec
{
    public const int MeasureScale = 3;
    public const int UnitKgScale = 4;
    public const int PaintDescriptionMaximumLength = 128;
    public const int PaintColorCodeMaximumLength = 32;

    public const string PackageCountParameter = "packageCount";
    public const string PiecesPerPackageParameter = "piecesPerPackage";
    public const string PieceLengthParameter = "pieceLengthMm";
    public const string ProfileLengthParameter = "profileLengthMm";
    public const string UnitKgParameter = "unitKg";
    public const string PerimeterParameter = "perimeterMm";
    public const string TargetThicknessParameter = "targetThicknessMm";

    public const string CountUnit = "count";
    public const string MillimetreUnit = "mm";
    public const string KilogramUnit = "kg";

    public PlanProcessSpec(int? packageCount = null, int? piecesPerPackage = null, decimal? pieceLengthMm = null,
        decimal? unitKg = null, decimal? perimeterMm = null, decimal? targetThicknessMm = null,
        string? paintDescription = null, string? paintColorCode = null)
    {
        RequireCount(packageCount, nameof(packageCount));
        RequireCount(piecesPerPackage, nameof(piecesPerPackage));
        RequireMeasure(pieceLengthMm, MeasureScale, nameof(pieceLengthMm));
        RequireMeasure(unitKg, UnitKgScale, nameof(unitKg));
        RequireMeasure(perimeterMm, MeasureScale, nameof(perimeterMm));
        RequireMeasure(targetThicknessMm, MeasureScale, nameof(targetThicknessMm));
        PaintDescription = RequireText(paintDescription, PaintDescriptionMaximumLength, nameof(paintDescription));
        PaintColorCode = RequireText(paintColorCode, PaintColorCodeMaximumLength, nameof(paintColorCode));
        PackageCount = packageCount;
        PiecesPerPackage = piecesPerPackage;
        PieceLengthMm = pieceLengthMm;
        UnitKg = unitKg;
        PerimeterMm = perimeterMm;
        TargetThicknessMm = targetThicknessMm;
        if (IsEmpty)
        {
            throw new ArgumentException("Süreç ayrıntısı en az bir değer taşımalıdır.", nameof(packageCount));
        }
    }

    public int? PackageCount { get; }
    public int? PiecesPerPackage { get; }
    public decimal? PieceLengthMm { get; }
    public decimal? UnitKg { get; }
    public decimal? PerimeterMm { get; }
    public decimal? TargetThicknessMm { get; }
    public string? PaintDescription { get; }
    public string? PaintColorCode { get; }

    private bool IsEmpty => PackageCount is null && PiecesPerPackage is null && PieceLengthMm is null && UnitKg is null
                            && PerimeterMm is null && TargetThicknessMm is null && PaintDescription is null
                            && PaintColorCode is null;

    /// <summary>
    /// The values this process may carry; anything else is a caller error (the API refuses it as an invalid field first).
    /// A Paint spec names its paint.
    /// </summary>
    public void RequireAllowedFor(ManufacturingProcess process, string parameterName)
    {
        var allowed = process switch
        {
            ManufacturingProcess.TubeProfile => PerimeterMm is null && TargetThicknessMm is null
                                                && PaintDescription is null && PaintColorCode is null,
            ManufacturingProcess.Paint => UnitKg is null && TargetThicknessMm is null,
            ManufacturingProcess.ColdRolling => PackageCount is null && PiecesPerPackage is null && PieceLengthMm is null
                                                && UnitKg is null && PerimeterMm is null && PaintDescription is null
                                                && PaintColorCode is null,
            _ => false,
        };
        if (!allowed)
        {
            throw new ArgumentException("Süreç ayrıntısındaki değerler planın sürecine uymuyor.", parameterName);
        }

        if (process == ManufacturingProcess.Paint && PaintDescription is null)
        {
            throw new ArgumentException("Boyama ayrıntısı boya cinsini/tedarikçisini taşımalıdır.", parameterName);
        }
    }

    /// <summary>The numbers the engines read, by their own parameter names (a Paint piece length is the profile length).</summary>
    public IReadOnlyList<PlanEnteredValue> EngineValues(ManufacturingProcess process)
    {
        var values = new List<PlanEnteredValue>();
        void Add(string key, string unit, decimal? number)
        {
            if (number is { } value)
            {
                values.Add(new PlanEnteredValue(key, unit, value));
            }
        }

        Add(PackageCountParameter, CountUnit, PackageCount);
        Add(PiecesPerPackageParameter, CountUnit, PiecesPerPackage);
        Add(process == ManufacturingProcess.Paint ? ProfileLengthParameter : PieceLengthParameter, MillimetreUnit,
            PieceLengthMm);
        Add(UnitKgParameter, KilogramUnit, UnitKg);
        Add(PerimeterParameter, MillimetreUnit, PerimeterMm);
        Add(TargetThicknessParameter, MillimetreUnit, TargetThicknessMm);
        return values;
    }

    private static void RequireCount(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Adet sıfırdan büyük olmalıdır.");
        }
    }

    private static void RequireMeasure(decimal? value, int scale, string parameterName)
    {
        if (value is { } number && (number <= 0m || decimal.Round(number, scale) != number))
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                $"Değer sıfırdan büyük ve en fazla {scale} ondalık olmalıdır.");
        }
    }

    private static string? RequireText(string? value, int maximumLength, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length == 0 || text.Length > maximumLength || text.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"Metin 1–{maximumLength} karakter olmalı ve kontrol karakteri taşımamalıdır.", parameterName);
        }

        return text;
    }
}
