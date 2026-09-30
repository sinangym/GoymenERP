using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// AT-018 / AT-021: the line's pricing detail beside K3's three commercial columns - the price-list reference and the
/// discount-amount / surcharge adjustments - as ONE JSON column (<c>PricingJson</c>), null when the line has none (every
/// line stored before this slice). Strict both ways: an unknown member, a missing required member or a malformed value
/// is a corrupt row and is refused, never repaired. The net is never stored (the calculator derives it).
/// </summary>
internal static class SalesLinePricingJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string? Serialize(CommercialDraftFields commercial)
    {
        ArgumentNullException.ThrowIfNull(commercial);
        return commercial.HasPricingDetail
            ? JsonSerializer.Serialize(new StoredPricing(commercial.Reference, commercial.DiscountAmount, commercial.SurchargePercent,
                commercial.SurchargeAmount), Json)
            : null;
    }

    public static CommercialDraftFields Restore(string? unitPrice, Guid? priceUnitId, string? discountPercent, string? pricingJson)
    {
        if (pricingJson is null)
        {
            return new CommercialDraftFields(unitPrice, priceUnitId, discountPercent);
        }

        StoredPricing? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredPricing>(pricingJson, Json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Kayıtlı satır fiyat ayrıntısı okunamadı.", exception);
        }

        if (stored is null || (stored.Reference is null && stored.DiscountAmount is null && stored.SurchargePercent is null
            && stored.SurchargeAmount is null))
        {
            throw new InvalidOperationException("Kayıtlı satır fiyat ayrıntısı boş olamaz.");
        }

        return new CommercialDraftFields(unitPrice, priceUnitId, discountPercent, stored.Reference, stored.DiscountAmount,
            stored.SurchargePercent, stored.SurchargeAmount);
    }

    private sealed record StoredPricing(PriceReferenceSnapshot? Reference, string? DiscountAmount, string? SurchargePercent,
        string? SurchargeAmount);
}
