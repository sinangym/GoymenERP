using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// MOD-03 → Sales: the order's buyer tax (<c>BuyerTaxJson</c> on the draft and on an archived revision's header) and a line's tax
/// snapshot (<c>TaxJson</c> on the line and on an archived revision's line) as JSON columns; null = none (every row written before
/// the tax wiring). Strict both ways, like <see cref="SalesLinePricingJson"/>: an unknown member, a missing required member or a
/// malformed value is a corrupt row and is refused, never repaired. Rates are kept as exact decimals.
/// </summary>
internal static class SalesTaxJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string? SerializeBuyer(SalesBuyerTax? buyer) => buyer is null
        ? null
        : JsonSerializer.Serialize(new StoredBuyerTax(buyer.Situation, buyer.ExemptionCode, buyer.DocumentNo, buyer.DocumentDate), Json);

    public static SalesBuyerTax? RestoreBuyer(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredBuyerTax>(json, Json)
                ?? throw new InvalidOperationException("Kayıtlı alıcı vergi durumu boş olamaz.");
            return SalesBuyerTax.Create(stored.Situation, stored.ExemptionCode, stored.DocumentNo, stored.DocumentDate);
        }
        catch (Exception exception) when (exception is JsonException or SalesRuleException)
        {
            throw new InvalidOperationException("Kayıtlı alıcı vergi durumu okunamadı.", exception);
        }
    }

    public static string? SerializeLine(SalesLineTaxSnapshot? tax) => tax is null
        ? null
        : JsonSerializer.Serialize(new StoredLineTax(tax.ProductId, tax.ResolvedOn, tax.BuyerSituation, tax.ItemType, tax.State, tax.Reason,
            tax.Source, tax.RecordId, tax.RecordVersion, tax.VatState, tax.VatPercent, tax.ExemptionCodeState, tax.ExemptionCode,
            tax.ExemptionCodeReason, tax.WithholdingState, tax.WithholdingPercent, tax.WithholdingCodeState, tax.WithholdingCode,
            tax.WithholdingCodeReason, tax.BuyerAdjustment, tax.BuyerSituationListVersion, tax.BuyerVatMode, tax.BuyerDocumentRequired,
            tax.Override is { } change
                ? new StoredOverride(change.ItemType, change.BuyerSituation, change.ExemptionCode, change.Reason, change.ChangedBy,
                    change.ChangedAtUtc)
                : null), Json);

    public static SalesLineTaxSnapshot? RestoreLine(string? json)
    {
        if (json is null)
        {
            return null;
        }

        StoredLineTax? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredLineTax>(json, Json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Kayıtlı satır vergisi okunamadı.", exception);
        }

        if (stored is null || stored.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(stored.State)
            || string.IsNullOrWhiteSpace(stored.BuyerSituation)
            || stored.Override is { } change && (string.IsNullOrWhiteSpace(change.Reason) || string.IsNullOrWhiteSpace(change.ChangedBy)
                || string.IsNullOrWhiteSpace(change.BuyerSituation)))
        {
            throw new InvalidOperationException("Kayıtlı satır vergisi eksik.");
        }

        return new SalesLineTaxSnapshot(stored.ProductId, stored.ResolvedOn, stored.BuyerSituation, stored.ItemType, stored.State,
            stored.Reason, stored.Source, stored.RecordId, stored.RecordVersion, stored.VatState, stored.VatPercent,
            stored.ExemptionCodeState, stored.ExemptionCode, stored.ExemptionCodeReason, stored.WithholdingState,
            stored.WithholdingPercent, stored.WithholdingCodeState, stored.WithholdingCode, stored.WithholdingCodeReason,
            stored.BuyerAdjustment, stored.BuyerSituationListVersion, stored.BuyerVatMode, stored.BuyerDocumentRequired,
            stored.Override is { } kept
                ? new SalesLineTaxOverride(kept.ItemType, kept.BuyerSituation, kept.ExemptionCode, kept.Reason, kept.ChangedBy,
                    kept.ChangedAtUtc)
                : null);
    }

    private sealed record StoredBuyerTax(string Situation, string? ExemptionCode, string? DocumentNo, DateOnly? DocumentDate);

    private sealed record StoredOverride(string? ItemType, string BuyerSituation, string? ExemptionCode, string Reason,
        string ChangedBy, DateTimeOffset ChangedAtUtc);

    private sealed record StoredLineTax(
        Guid ProductId, DateOnly ResolvedOn, string BuyerSituation, string? ItemType, string State, string? Reason, string? Source,
        Guid? RecordId, long? RecordVersion, string? VatState, decimal? VatPercent, string? ExemptionCodeState, string? ExemptionCode,
        string? ExemptionCodeReason, string? WithholdingState, decimal? WithholdingPercent, string? WithholdingCodeState,
        string? WithholdingCode, string? WithholdingCodeReason, string? BuyerAdjustment, long? BuyerSituationListVersion,
        string? BuyerVatMode, bool BuyerDocumentRequired, StoredOverride? Override);
}
