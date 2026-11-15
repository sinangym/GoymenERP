using System.Globalization;
using System.Text;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// BS-9 / KLN-D48: the measures one "Ebat/kalınlık" row needs, per section kind (<see cref="ProductDimensionSectionKindsV1"/>).
/// ONE rule table feeds both this 400 answer before the save and the database check
/// (<see cref="ProductPriceListDbContext.DimensionShapeCheck"/>, built by <see cref="SqlCheck"/>), so the two cannot drift.
/// A measure is never rounded here: a value with more decimals than its column holds is refused, not stored rounded (URN-D8 —
/// inputs stay at full precision). A text field is never trimmed or re-cased here either: a value that does not match is refused.
/// </summary>
internal static class ProductDimensionShape
{
    internal sealed record Measures(
        string SectionKind,
        decimal? ThicknessMm,
        decimal? WidthMm,
        decimal? LengthMm,
        decimal? HeightMm,
        decimal? OuterDiameterMm,
        decimal? WallThicknessMm,
        decimal? KgPerMetre,
        string? ProfileSeries = null,
        string? ProfileSize = null,
        decimal? MeshSpacingMm = null,
        string? SurfaceFinish = null);

    internal enum Presence
    {
        Forbidden,
        Optional,
        Required,
    }

    /// <summary>A geometric fit both sides hold: the C# test and the same comparison in SQL.</summary>
    internal sealed record Fit(Func<Measures, bool> Holds, string Sql, string Refusal);

    /// <summary>
    /// One section kind: the presence of each field and its fits. <c>Profile</c> is the structural series AND size designation
    /// together; <c>Refusal</c> is the Turkish detail when a field is missing or extra.
    /// </summary>
    internal sealed record Rule(
        string SectionKind,
        Presence Thickness,
        Presence Width,
        Presence Length,
        Presence Height,
        Presence Diameter,
        Presence Wall,
        Presence Mesh,
        Presence KgPerMetre,
        Presence Profile,
        Presence Surface,
        string Refusal,
        IReadOnlyList<Fit> Fits);

    private const Presence F = Presence.Forbidden;
    private const Presence O = Presence.Optional;
    private const Presence R = Presence.Required;

    private static readonly Fit WallInDiameter = new(m => m.WallThicknessMm!.Value * 2 <= m.OuterDiameterMm!.Value,
        "2 * [WallThicknessMm] <= [OuterDiameterMm]", "Et kalınlığının iki katı dış çaptan büyük olamaz.");

    private static readonly Fit WallInWidth = new(m => m.WallThicknessMm!.Value * 2 <= m.WidthMm!.Value,
        "2 * [WallThicknessMm] <= [WidthMm]", "Et kalınlığının iki katı profilin eninden büyük olamaz.");

    private static readonly Fit WallInHeight = new(m => m.WallThicknessMm!.Value * 2 <= m.HeightMm!.Value,
        "2 * [WallThicknessMm] <= [HeightMm]", "Et kalınlığının iki katı profilin yüksekliğinden büyük olamaz.");

    private static readonly Fit ThicknessBelowWidth = new(m => m.ThicknessMm!.Value < m.WidthMm!.Value,
        "[ThicknessMm] < [WidthMm]", "Kalınlık kanat/en ölçüsünden küçük olmalıdır.");

    private static readonly Fit ThicknessBelowHeight = new(m => m.ThicknessMm!.Value < m.HeightMm!.Value,
        "[ThicknessMm] < [HeightMm]", "Kalınlık ikinci kanat/yükseklik ölçüsünden küçük olmalıdır.");

    private static readonly Fit ThicknessUpToWidth = new(m => m.ThicknessMm!.Value <= m.WidthMm!.Value,
        "[ThicknessMm] <= [WidthMm]", "Kalınlık enden büyük olamaz.");

    private static readonly Fit WireBelowSpacing = new(m => m.OuterDiameterMm!.Value < m.MeshSpacingMm!.Value,
        "[OuterDiameterMm] < [MeshSpacingMm]", "Tel çapı göz aralığından küçük olmalıdır.");

    private const string SheetText = "kalınlık, en ve boy girilir; diğer ölçüler boş kalır.";
    private const string RoundTubeText =
        "dış çap ve et kalınlığı girilir; metre ağırlığı isteğe bağlıdır, diğer ölçüler boş kalır.";
    private const string ProfileText = "en, yükseklik, et kalınlığı ve metre ağırlığı girilir; diğer ölçüler boş kalır.";
    private const string LengthText = "boy isteğe bağlıdır, diğer ölçüler boş kalır.";

    /// <summary>The field list of every section kind (STK-S37 slice spec, after 19.26), in <see cref="ProductDimensionSectionKindsV1.All"/> order.</summary>
    internal static IReadOnlyList<Rule> Rules { get; } =
    [
        Sheet(ProductDimensionSectionKindsV1.Sheet, "Sac/levha için " + SheetText),
        new(ProductDimensionSectionKindsV1.Coil, R, R, F, F, F, F, F, F, F, F,
            "Rulo/bant için kalınlık ve en girilir; diğer ölçüler boş kalır.", []),
        Tube(ProductDimensionSectionKindsV1.RoundTube, F, "Yuvarlak boru için " + RoundTubeText),
        Box(ProductDimensionSectionKindsV1.Profile, F, "Profil için " + ProfileText),
        new(ProductDimensionSectionKindsV1.EqualAngle, R, R, O, F, F, F, F, F, F, F,
            "Eşit kanatlı köşebent için kanat (en) ve kalınlık girilir; " + LengthText, [ThicknessBelowWidth]),
        new(ProductDimensionSectionKindsV1.UnequalAngle, R, R, O, R, F, F, F, F, F, F,
            "Eşit olmayan kanatlı köşebent için iki kanat (en, yükseklik) ve kalınlık girilir; " + LengthText,
            [ThicknessBelowWidth, ThicknessBelowHeight]),
        new(ProductDimensionSectionKindsV1.FlatBar, R, R, O, F, F, F, F, F, F, F,
            "Lama için en ve kalınlık girilir; " + LengthText, [ThicknessUpToWidth]),
        new(ProductDimensionSectionKindsV1.NarrowFlatBar, R, R, O, F, F, F, F, F, F, F,
            "Silme için en ve kalınlık girilir; " + LengthText, [ThicknessUpToWidth]),
        new(ProductDimensionSectionKindsV1.RoundBar, F, F, O, F, R, F, F, F, F, F,
            "Dolu yuvarlak (mil) için çap girilir; " + LengthText, []),
        new(ProductDimensionSectionKindsV1.SquareBar, F, R, O, F, F, F, F, F, F, F,
            "Dolu kare için kenar (en) girilir; " + LengthText, []),
        new(ProductDimensionSectionKindsV1.HexBar, F, R, O, F, F, F, F, F, F, F,
            "Altıgen için anahtar ağzı (en) girilir; " + LengthText, []),
        new(ProductDimensionSectionKindsV1.TeeBar, R, R, O, R, F, F, F, F, F, F,
            "T demiri için en, yükseklik ve kalınlık girilir; " + LengthText, [ThicknessBelowWidth, ThicknessBelowHeight]),
        new(ProductDimensionSectionKindsV1.StructuralProfile, F, F, R, F, F, F, F, F, R, F,
            "Yapısal profil için standart seri, ölçü adı ve boy girilir; metre ağırlığı elle girilmez, standart tablodan gelir.",
            []),
        new(ProductDimensionSectionKindsV1.Rebar, F, F, O, F, R, F, F, F, F, F,
            "İnşaat demiri için çap girilir; " + LengthText, []),
        new(ProductDimensionSectionKindsV1.WireRod, F, F, F, F, R, F, F, F, F, F,
            "Filmaşin için çap girilir; diğer ölçüler boş kalır.", []),
        new(ProductDimensionSectionKindsV1.WeldedMesh, F, R, R, F, R, F, R, F, F, F,
            "Hasır çelik için tel çapı, göz aralığı, en ve boy girilir; diğer ölçüler boş kalır.", [WireBelowSpacing]),
        Sheet(ProductDimensionSectionKindsV1.ChequeredSheet, "Baklavalı sac için " + SheetText),
        Sheet(ProductDimensionSectionKindsV1.PerforatedSheet, "Delikli sac için " + SheetText),
        Sheet(ProductDimensionSectionKindsV1.CorrugatedSheet, "Trapez/oluklu sac için " + SheetText),
        Tube(ProductDimensionSectionKindsV1.ErwTube, F, "ERW boru için " + RoundTubeText),
        Box(ProductDimensionSectionKindsV1.ErwProfile, F, "ERW profil için " + ProfileText),
        Tube(ProductDimensionSectionKindsV1.PaintedErwTube, R,
            "Boyalı ERW boru için dış çap, et kalınlığı ve yüzey (boya/renk) girilir; metre ağırlığı isteğe bağlıdır."),
        Box(ProductDimensionSectionKindsV1.PaintedErwProfile, R,
            "Boyalı ERW profil için en, yükseklik, et kalınlığı, metre ağırlığı ve yüzey (boya/renk) girilir."),
    ];

    private static Rule Sheet(string kind, string refusal) => new(kind, R, R, R, F, F, F, F, F, F, F, refusal, []);

    private static Rule Tube(string kind, Presence surface, string refusal) =>
        new(kind, F, F, F, F, R, R, F, O, F, surface, refusal, [WallInDiameter]);

    private static Rule Box(string kind, Presence surface, string refusal) =>
        new(kind, F, R, F, R, F, R, F, R, F, surface, refusal, [WallInWidth, WallInHeight]);

    /// <summary>Null when the request is a valid row of a known section kind; otherwise the reason (Turkish, for the detail).</summary>
    internal static string? Refusal(SetProductDimensionRequestV1 request, out Measures? measures)
    {
        ArgumentNullException.ThrowIfNull(request);
        measures = null;
        var rule = Rules.SingleOrDefault(candidate => string.Equals(candidate.SectionKind, request.SectionKind, StringComparison.Ordinal));
        if (rule is null)
        {
            return "Kesit türü tanımlı kesit türlerinden biri olmalıdır.";
        }

        if (!IsMillimetre(request.ThicknessMm) || !IsMillimetre(request.WidthMm) || !IsMillimetre(request.LengthMm)
            || !IsMillimetre(request.HeightMm) || !IsMillimetre(request.OuterDiameterMm) || !IsMillimetre(request.WallThicknessMm)
            || !IsMillimetre(request.MeshSpacingMm))
        {
            return $"Ölçüler sıfırdan büyük ve en fazla {ProductDimensionInputLimitsV1.MillimetreDecimals} ondalıklı milimetre olmalıdır.";
        }

        if (!IsKgPerMetre(request.KgPerMetre))
        {
            return $"Metre ağırlığı sıfırdan büyük ve en fazla {ProductDimensionInputLimitsV1.KgPerMetreDecimals} ondalıklı olmalıdır.";
        }

        if (request.ProfileSeries is not null && !ProductProfileSeriesV1.IsKnown(request.ProfileSeries))
        {
            return "Yapısal profil serisi UPN, NPI, IPE, HEA, HEB veya HEM olmalıdır.";
        }

        if (request.ProfileSize is not null && !IsProfileSize(request.ProfileSize))
        {
            return $"Profil ölçü adı en fazla {ProductDimensionInputLimitsV1.ProfileSizeMaximumLength} karakter; büyük harf, rakam ve nokta olmalıdır.";
        }

        if (request.SurfaceFinish is not null && !IsSurfaceFinish(request.SurfaceFinish))
        {
            return $"Yüzey (boya/renk) boşlukla başlamayan/bitmeyen, en fazla {ProductDimensionInputLimitsV1.SurfaceFinishMaximumLength} karakterlik bir metin olmalıdır.";
        }

        var candidate = new Measures(rule.SectionKind, request.ThicknessMm, request.WidthMm, request.LengthMm, request.HeightMm,
            request.OuterDiameterMm, request.WallThicknessMm, request.KgPerMetre, request.ProfileSeries, request.ProfileSize,
            request.MeshSpacingMm, request.SurfaceFinish);
        if (!Matches(rule, candidate))
        {
            return rule.Refusal;
        }

        if (rule.Fits.FirstOrDefault(fit => !fit.Holds(candidate)) is { } failed)
        {
            return failed.Refusal;
        }

        measures = candidate with
        {
            ThicknessMm = Canonical(candidate.ThicknessMm),
            WidthMm = Canonical(candidate.WidthMm),
            LengthMm = Canonical(candidate.LengthMm),
            HeightMm = Canonical(candidate.HeightMm),
            OuterDiameterMm = Canonical(candidate.OuterDiameterMm),
            WallThicknessMm = Canonical(candidate.WallThicknessMm),
            KgPerMetre = Canonical(candidate.KgPerMetre),
            MeshSpacingMm = Canonical(candidate.MeshSpacingMm),
        };
        return null;
    }

    /// <summary>The same value without trailing zeros (1.50 = 1.5), so the idempotency hash does not depend on how it was typed.</summary>
    internal static decimal? Canonical(decimal? value) =>
        value is { } number ? number / 1.0000000000000000000000000000m : null;

    /// <summary>A structural profile size designation: 1..16 of ASCII capitals, digits and dots ("200", "100A", "22.5").</summary>
    internal static bool IsProfileSize(string? size) =>
        !string.IsNullOrEmpty(size) && size.Length <= ProductDimensionInputLimitsV1.ProfileSizeMaximumLength
        && size.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '.');

    /// <summary>
    /// The database form of <see cref="Rules"/>: one disjunct per kind, each naming every field as NULL / NOT NULL by its presence
    /// and its fits. Deterministic (the migration and the model snapshot hold its text).
    /// </summary>
    internal static string SqlCheck()
    {
        var builder = new StringBuilder();
        foreach (var rule in Rules)
        {
            if (builder.Length > 0)
            {
                builder.Append(" OR ");
            }

            var clauses = new List<string> { string.Create(CultureInfo.InvariantCulture, $"[SectionKind] = '{rule.SectionKind}'") };
            Add(clauses, "ThicknessMm", rule.Thickness);
            Add(clauses, "WidthMm", rule.Width);
            Add(clauses, "LengthMm", rule.Length);
            Add(clauses, "HeightMm", rule.Height);
            Add(clauses, "OuterDiameterMm", rule.Diameter);
            Add(clauses, "WallThicknessMm", rule.Wall);
            Add(clauses, "MeshSpacingMm", rule.Mesh);
            Add(clauses, "KgPerMetre", rule.KgPerMetre);
            Add(clauses, "ProfileSeries", rule.Profile);
            Add(clauses, "ProfileSize", rule.Profile);
            Add(clauses, "SurfaceFinish", rule.Surface);
            clauses.AddRange(rule.Fits.Select(fit => fit.Sql));
            builder.Append('(').AppendJoin(" AND ", clauses).Append(')');
        }

        return builder.ToString();
    }

    private static void Add(List<string> clauses, string column, Presence presence)
    {
        if (presence != Presence.Optional)
        {
            clauses.Add(presence == Presence.Required ? $"[{column}] IS NOT NULL" : $"[{column}] IS NULL");
        }
    }

    private static bool Matches(Rule rule, Measures m) =>
        Holds(rule.Thickness, m.ThicknessMm.HasValue) && Holds(rule.Width, m.WidthMm.HasValue)
        && Holds(rule.Length, m.LengthMm.HasValue) && Holds(rule.Height, m.HeightMm.HasValue)
        && Holds(rule.Diameter, m.OuterDiameterMm.HasValue) && Holds(rule.Wall, m.WallThicknessMm.HasValue)
        && Holds(rule.Mesh, m.MeshSpacingMm.HasValue) && Holds(rule.KgPerMetre, m.KgPerMetre.HasValue)
        && Holds(rule.Profile, m.ProfileSeries is not null) && Holds(rule.Profile, m.ProfileSize is not null)
        && Holds(rule.Surface, m.SurfaceFinish is not null);

    private static bool Holds(Presence presence, bool present) => presence switch
    {
        Presence.Forbidden => !present,
        Presence.Required => present,
        _ => true,
    };

    private static bool IsSurfaceFinish(string text) =>
        text.Length is > 0 and <= ProductDimensionInputLimitsV1.SurfaceFinishMaximumLength
        && !char.IsWhiteSpace(text[0]) && !char.IsWhiteSpace(text[^1]) && !text.Any(char.IsControl);

    private static bool IsMillimetre(decimal? value) =>
        value is not { } measure
        || (measure > 0 && measure < ProductDimensionInputLimitsV1.MillimetreExclusiveMaximum
            && decimal.Round(measure, ProductDimensionInputLimitsV1.MillimetreDecimals) == measure);

    internal static bool IsKgPerMetre(decimal? value) =>
        value is not { } kg
        || (kg > 0 && kg < ProductDimensionInputLimitsV1.KgPerMetreExclusiveMaximum
            && decimal.Round(kg, ProductDimensionInputLimitsV1.KgPerMetreDecimals) == kg);
}
