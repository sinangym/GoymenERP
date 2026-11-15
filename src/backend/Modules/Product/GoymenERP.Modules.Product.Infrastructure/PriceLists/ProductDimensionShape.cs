using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// BS-9: the measures one "Ebat/kalınlık" row needs, per section kind (<see cref="ProductDimensionSectionKindsV1"/>). The same
/// rule is held by <see cref="ProductPriceListDbContext.DimensionShapeCheck"/> and
/// <see cref="ProductPriceListDbContext.DimensionMeasureCheck"/>; this copy only answers a person with a 400 before the save.
/// A measure is never rounded here: a value with more decimals than its column holds is refused, not stored rounded (URN-D8 —
/// inputs stay at full precision). URN-D16 (STK-S37R): the card never takes a kg/m — a profile's (and optionally a round tube's)
/// kg/m is the company's imported table value (<see cref="ProductProfileWeightTableService"/>), so the request has no such field.
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
        decimal? WallThicknessMm);

    /// <summary>Null when the request is a valid row of a known section kind; otherwise the reason (Turkish, for the detail).</summary>
    internal static string? Refusal(SetProductDimensionRequestV1 request, out Measures? measures)
    {
        ArgumentNullException.ThrowIfNull(request);
        measures = null;
        if (!ProductDimensionSectionKindsV1.IsKnown(request.SectionKind))
        {
            return "Kesit türü Sheet, Coil, RoundTube veya Profile olmalıdır.";
        }

        if (!IsMillimetre(request.ThicknessMm) || !IsMillimetre(request.WidthMm) || !IsMillimetre(request.LengthMm)
            || !IsMillimetre(request.HeightMm) || !IsMillimetre(request.OuterDiameterMm) || !IsMillimetre(request.WallThicknessMm))
        {
            return $"Ölçüler sıfırdan büyük ve en fazla {ProductDimensionInputLimitsV1.MillimetreDecimals} ondalıklı milimetre olmalıdır.";
        }

        var refusal = request.SectionKind switch
        {
            ProductDimensionSectionKindsV1.Sheet =>
                Requires(request, thickness: true, width: true, length: true, height: false, diameter: false, wall: false)
                    ? null
                    : "Sac/levha için kalınlık, en ve boy girilir; diğer ölçüler boş kalır.",
            ProductDimensionSectionKindsV1.Coil =>
                Requires(request, thickness: true, width: true, length: false, height: false, diameter: false, wall: false)
                    ? null
                    : "Rulo/bant için kalınlık ve en girilir; diğer ölçüler boş kalır.",
            ProductDimensionSectionKindsV1.RoundTube =>
                !Requires(request, thickness: false, width: false, length: false, height: false, diameter: true, wall: true)
                    ? "Yuvarlak boru için dış çap ve et kalınlığı girilir; diğer ölçüler boş kalır."
                    : WallFits(request.WallThicknessMm!.Value, request.OuterDiameterMm!.Value)
                        ? null
                        : "Et kalınlığının iki katı dış çaptan büyük olamaz.",
            _ =>
                !Requires(request, thickness: false, width: true, length: false, height: true, diameter: false, wall: true)
                    ? "Profil için en, yükseklik ve et kalınlığı girilir; diğer ölçüler boş kalır."
                    : WallFits(request.WallThicknessMm!.Value, request.WidthMm!.Value)
                      && WallFits(request.WallThicknessMm!.Value, request.HeightMm!.Value)
                        ? null
                        : "Et kalınlığının iki katı profilin eninden veya yüksekliğinden büyük olamaz.",
        };
        if (refusal is not null)
        {
            return refusal;
        }

        measures = new Measures(request.SectionKind!, Canonical(request.ThicknessMm), Canonical(request.WidthMm),
            Canonical(request.LengthMm), Canonical(request.HeightMm), Canonical(request.OuterDiameterMm),
            Canonical(request.WallThicknessMm));
        return null;
    }

    /// <summary>The same value without trailing zeros (1.50 = 1.5), so the idempotency hash does not depend on how it was typed.</summary>
    internal static decimal? Canonical(decimal? value) =>
        value is { } number ? number / 1.0000000000000000000000000000m : null;

    private static bool Requires(SetProductDimensionRequestV1 request, bool thickness, bool width, bool length, bool height,
        bool diameter, bool wall) =>
        request.ThicknessMm.HasValue == thickness && request.WidthMm.HasValue == width && request.LengthMm.HasValue == length
        && request.HeightMm.HasValue == height && request.OuterDiameterMm.HasValue == diameter
        && request.WallThicknessMm.HasValue == wall;

    /// <summary>A hollow section: the two walls fit inside the outer measure (equal = solid, as the F-C formula accepts).</summary>
    internal static bool WallFits(decimal wall, decimal outer) => wall + wall <= outer;

    internal static bool IsMillimetre(decimal? value) =>
        value is not { } measure
        || (measure > 0 && measure < ProductDimensionInputLimitsV1.MillimetreExclusiveMaximum
            && decimal.Round(measure, ProductDimensionInputLimitsV1.MillimetreDecimals) == measure);

    internal static bool IsKgPerMetre(decimal? value) =>
        value is not { } kg
        || (kg > 0 && kg < ProductDimensionInputLimitsV1.KgPerMetreExclusiveMaximum
            && decimal.Round(kg, ProductDimensionInputLimitsV1.KgPerMetreDecimals) == kg);
}
