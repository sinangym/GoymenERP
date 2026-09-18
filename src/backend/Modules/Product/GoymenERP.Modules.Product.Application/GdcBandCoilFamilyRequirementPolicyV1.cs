using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Modules.Product.Application;

/// <summary>
/// The first accepted family rule set (Business #265 5722701085, Sinan Ö1/Ö2): GDC band/coil,
/// kg→kg. A card of this family passes only when all of these hold:
/// <list type="bullet">
/// <item>nominal thickness &gt; 0 mm and nominal width &gt; 0 mm, each at most three decimals
/// (Ö2) — a fourth decimal is refused, never rounded;</item>
/// <item>a quality from the owner's controlled list;</item>
/// <item>a coating status from the owner's controlled list. "Kaplamasız" is a valid, stated
/// status; a missing or null status is not "uncoated" and is refused;</item>
/// <item>the sales unit is the stock unit: the first path is kg→kg with no conversion (whether
/// the stock unit is kilogram is Settings' answer, checked by the eligibility read).</item>
/// </list>
/// Colour is meaningful only for a painted family; no colour is required here. Lifecycle, the
/// sellable role, the Settings profiles and the authority fences are the eligibility read's; a
/// positive result needs all of them together.
/// <para>
/// Fail-closed: a card of any other family, or no authorised definition, is 503
/// <c>product_family_policy_unavailable</c> — never "no errors". A malformed definition is 503
/// <c>product_family_policy_invalid</c>. Nothing here guesses a family, field, quality or status.
/// </para>
/// </summary>
public sealed class GdcBandCoilFamilyRequirementPolicyV1(IGdcBandCoilFamilyDefinitionSourceV1 source)
    : IProductFamilyRequirementPolicyV1
{
    public const string InvalidDefinitionCode = "product_family_policy_invalid";
    public const int MaximumMillimetreDecimals = 3;

    public async Task<IReadOnlyList<ProductFieldError>> ValidateAsync(ProductCardView card,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = await source.GetAsync(cancellationToken)
            ?? throw new ProductOperationException(InvalidDefinitionCode, 503);
        RequireWellFormed(definition);

        if (card.Product.FamilyId != definition.FamilyId)
        {
            // No accepted rule set covers this card's family.
            throw new ProductOperationException(UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode, 503);
        }

        var errors = new List<ProductFieldError>();
        var attributes = card.References.Attributes;
        Measure(attributes, definition.ThicknessFieldId, errors);
        Measure(attributes, definition.WidthFieldId, errors);
        Controlled(attributes, definition.QualityFieldId, definition.QualityReferences, errors);
        Controlled(attributes, definition.CoatingFieldId, definition.CoatingStatusReferences, errors);

        // kg→kg needs both bindings: a missing one is not a pass (SİPARİŞ #300 P3-1).
        var units = card.References.Units;
        var stock = units?.Stock is Guid stockId && stockId != Guid.Empty ? stockId : (Guid?)null;
        var sales = units?.Sales is Guid salesId && salesId != Guid.Empty ? salesId : (Guid?)null;
        if (stock is null)
        {
            errors.Add(new("references.units.stock", ProductFamilyPolicyCodesV1.Required));
        }

        if (sales is null)
        {
            errors.Add(new("references.units.sales", ProductFamilyPolicyCodesV1.Required));
        }
        else if (stock is not null && stock != sales)
        {
            errors.Add(new("references.units.sales", ProductFamilyPolicyCodesV1.UnitPathMismatch));
        }

        return errors;
    }

    private static void Measure(IReadOnlyDictionary<string, JsonElement>? attributes, string fieldId,
        List<ProductFieldError> errors)
    {
        var field = Field(fieldId);
        if (!TryGet(attributes, fieldId, out var value))
        {
            errors.Add(new(field, ProductFamilyPolicyCodesV1.Required));
            return;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var millimetres) || millimetres <= 0m)
        {
            errors.Add(new(field, ProductFamilyPolicyCodesV1.InvalidValue));
            return;
        }

        if (decimal.Round(millimetres, MaximumMillimetreDecimals, MidpointRounding.ToZero) != millimetres)
        {
            errors.Add(new(field, ProductFamilyPolicyCodesV1.PrecisionExceeded));
        }
    }

    private static void Controlled(IReadOnlyDictionary<string, JsonElement>? attributes, string fieldId,
        IReadOnlySet<string> allowed, List<ProductFieldError> errors)
    {
        var field = Field(fieldId);
        if (!TryGet(attributes, fieldId, out var value))
        {
            errors.Add(new(field, ProductFamilyPolicyCodesV1.Required));
            return;
        }

        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } reference || !allowed.Contains(reference))
        {
            errors.Add(new(field, ProductFamilyPolicyCodesV1.NotControlled));
        }
    }

    /// <summary>Missing, JSON null and undefined are all "no value": an unknown is never an answer.</summary>
    private static bool TryGet(IReadOnlyDictionary<string, JsonElement>? attributes, string fieldId, out JsonElement value)
    {
        value = default;
        return attributes is not null && attributes.TryGetValue(fieldId, out value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    private static string Field(string fieldId) => $"references.attributes.{fieldId}";

    private static void RequireWellFormed(GdcBandCoilFamilyDefinitionV1 definition)
    {
        string[] fields = [definition.ThicknessFieldId, definition.WidthFieldId, definition.QualityFieldId, definition.CoatingFieldId];
        var malformed = definition.FamilyId == Guid.Empty
            || fields.Any(string.IsNullOrWhiteSpace)
            || fields.Distinct(StringComparer.Ordinal).Count() != fields.Length
            || definition.QualityReferences is not { Count: > 0 }
            || definition.CoatingStatusReferences is not { Count: > 0 }
            || definition.QualityReferences.Any(string.IsNullOrWhiteSpace)
            || definition.CoatingStatusReferences.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(definition.UncoatedStatusReference)
            || !definition.CoatingStatusReferences.Contains(definition.UncoatedStatusReference);
        if (malformed)
        {
            throw new ProductOperationException(InvalidDefinitionCode, 503);
        }
    }
}
