using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-04 (DEC-SAL-008): an order's due terms as ONE JSON column (<c>DueTermsJson</c>), null when the order has none. The shape
/// is the wire's (<see cref="SalesDueTermsV1"/>: instalments with sequence, basis name, days and the share text). Strict both
/// ways: an unknown member, a missing member or terms the domain refuses are a corrupt row and are refused, never repaired.
/// </summary>
internal static class SalesDueTermsJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string? Serialize(SalesDueTerms? terms) =>
        terms is null ? null : JsonSerializer.Serialize(SalesDraftHeaderApiV1.ToWire(terms), Json);

    public static SalesDueTerms? Restore(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            var wire = JsonSerializer.Deserialize<SalesDueTermsV1>(json, Json)
                ?? throw new InvalidOperationException("Kayıtlı vade koşulu boş.");
            if (wire.Instalments is null || wire.Instalments.Any(instalment => instalment is null
                    || !SalesDueBasisNamesV1.TryParse(instalment.Basis, out _)))
            {
                throw new InvalidOperationException("Kayıtlı vade koşulu okunamadı.");
            }

            return SalesDraftHeaderApiV1.ToDomain(wire);
        }
        catch (Exception exception) when (exception is JsonException or SalesDueTermsException)
        {
            throw new InvalidOperationException("Kayıtlı vade koşulu okunamadı.", exception);
        }
    }
}
