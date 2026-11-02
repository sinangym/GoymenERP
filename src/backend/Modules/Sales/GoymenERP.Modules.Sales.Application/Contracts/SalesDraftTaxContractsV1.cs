using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- MOD-03 → Sales: the order's tax (SAT-D73…D76, D82, D93, D97, D105, D133, D139, D140, D146…D149, D158) -----------------
//
// The order header carries the buyer's tax situation (SAT-D140: chosen per order; SAT-D158: a code of the Settings buyer situation
// list in force on the order's business date) with, where the situation needs them, the exemption code and the buyer's document
// (SAT-D147). Every line carries the tax the Settings line tax port (ISettingsLineTaxResolverV1) resolved for it at its create, a
// commercial revision or a buyer situation change — or the salesperson's change of its basis with a reason (SAT-D93/D97), chosen
// ONLY from Settings lists. Totals (API-03) and the risk read (SAT-D46) compute from these snapshots with the one calculator.

/// <summary>
/// SAT-D140 / SAT-D147: the order's buyer tax situation on the wire — the Settings buyer situation code, the exemption code chosen
/// from the exemption list (only for a situation whose effect is "KDV %0 with an exemption code") and the buyer's document number
/// and date (only for a situation that requires a document; both or neither; it may be added later — the invoice waits until then).
/// </summary>
public sealed record SalesDraftBuyerTaxV1(
    string Situation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExemptionCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DocumentNo = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? DocumentDate = null);

/// <summary>
/// SAT-D93 / SAT-D97: a line's tax basis changed at create — a buyer situation of the Settings list (required), an item type default
/// (<c>Steel</c> / <c>Scrap</c> / <c>Service</c>; omitted = the product's own), an exemption code from the exemption list (only for a
/// "KDV %0 with an exemption code" situation) and the required reason. A free rate or code is never accepted.
/// </summary>
/// <remarks>SAT-D181 (EK26): <c>vatRateCode</c> / <c>withholdingRatioCode</c> pick an entry (by its code) of the Settings VAT rate
/// list and withholding ratio list in force; the percent is the list's.</remarks>
public sealed record SalesDraftLineTaxOverrideV1(
    string BuyerSituation,
    string Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ItemType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExemptionCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VatRateCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WithholdingRatioCode = null);

/// <summary>
/// SAT-D93 / SAT-D97 / SAT-D105: a line's tax choice in a commercial revision. <c>mode</c> <c>Default</c>: the Settings default for
/// the product's item type and the order's buyer situation (any earlier change is dropped; no other member). <c>Override</c>: the
/// basis as <see cref="SalesDraftLineTaxOverrideV1"/> (buyer situation and reason required). Omitted (null): the line keeps its
/// current choice, re-resolved for this revision.
/// </summary>
public sealed record SalesDraftLineTaxChoiceV1(
    string Mode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BuyerSituation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ItemType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExemptionCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VatRateCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WithholdingRatioCode = null);

/// <summary>The modes of <see cref="SalesDraftLineTaxChoiceV1"/>.</summary>
public static class SalesDraftLineTaxModesV1
{
    public const string Default = "Default";
    public const string Override = "Override";
}

/// <summary>Who changed a line's tax basis, when and why (SAT-D97: audited; approved with the order's normal approval).</summary>
public sealed record SalesDraftLineTaxOverrideViewV1(
    string? ItemType, string BuyerSituation, string? ExemptionCode, string Reason, string ChangedBy, DateTimeOffset ChangedAtUtc,
    string? VatRateCode, string? WithholdingRatioCode);

/// <summary>
/// One line's tax as stored: <c>hasSnapshot</c> false (written before the tax wiring, or no Settings tax port answered) with
/// <c>rateState</c> <c>NoSource</c> and <c>unavailableReason</c> <c>tax_snapshot_missing</c>. Otherwise the Settings port's answer
/// verbatim — <c>origin</c> (<c>Default</c> / <c>Override</c>), the date it was resolved for, the basis (buyer situation, item type),
/// <c>state</c> and <c>reason</c>, the VAT part (<c>Resolved</c> + percent, <c>Exempt</c>, <c>ZeroRated</c>), the exemption code part,
/// the withholding part (<c>Resolved</c> + the withheld share of the VAT in percent, or <c>NotApplied</c>) and its code part, the
/// Settings record it came from — and Sales' reading of it: <c>rateState</c> <c>Computed</c> when the totals can use it, else
/// <c>NoSource</c> with <c>unavailableReason</c>; <c>invoiceBlockers</c> what makes the invoice wait (codes, a missing buyer
/// document; API-32's gate). Percents are exact invariant text.
/// </summary>
public sealed record SalesDraftLineTaxV1(
    Guid LineId,
    int LineNo,
    bool HasSnapshot,
    string? Origin,
    DateOnly? ResolvedOn,
    string? BuyerSituation,
    string? ItemType,
    string? State,
    string? Reason,
    string? VatState,
    string? VatPercent,
    string? ExemptionCodeState,
    string? ExemptionCode,
    string? ExemptionCodeReason,
    string? WithholdingState,
    string? WithholdingPercent,
    string? WithholdingCodeState,
    string? WithholdingCode,
    string? WithholdingCodeReason,
    string? BuyerAdjustment,
    bool BuyerDocumentRequired,
    string? SettingsSource,
    Guid? SettingsRecordId,
    long? SettingsRecordVersion,
    long? BuyerSituationListVersion,
    string RateState,
    string? UnavailableReason,
    IReadOnlyList<string> InvoiceBlockers,
    SalesDraftLineTaxOverrideViewV1? Override);

/// <summary>
/// <c>GET /api/v1/sales/drafts/{id}/tax</c> under the draft read AND the price read (SAT-D150: the Sevkiyatçı sees no tax either):
/// the order's buyer tax (null = written before the tax wiring) and every current line's tax, at <c>draftVersion</c> / <c>revision</c>.
/// </summary>
public sealed record SalesOrderDraftTaxResponse(
    string DraftVersion,
    string Revision,
    DateOnly BusinessDate,
    SalesDraftBuyerTaxV1? BuyerTax,
    IReadOnlyList<SalesDraftLineTaxV1> Lines);

/// <summary>One buyer situation of the Settings list in force, with its tax effect as data (SAT-D158).</summary>
public sealed record SalesTaxBuyerSituationOptionV1(string Code, string Name, bool WithholdingApplies, string VatMode, bool DocumentRequired);

/// <summary>One code of a Settings code list in force.</summary>
public sealed record SalesTaxCodeOptionV1(string Code, string Name);

/// <summary>
/// SAT-D181: one entry of a Settings rate list in force — its code, name and exact percent (invariant text) and, for a withholding
/// ratio, the GİB withholding code it is invoiced under (null = not mapped yet; the invoice waits).
/// </summary>
public sealed record SalesTaxRateOptionV1(string Code, string Name, string Percent, string? TaxCode);

/// <summary>
/// <c>GET /api/v1/sales/tax-options?on=yyyy-MM-dd</c> under the draft read: the choices the order screens offer for <c>on</c> (the
/// order's business date) — the Settings buyer situation list in force, the exemption code list in force and the item types (each
/// list <c>Resolved</c>, or <c>Unresolved</c> with the Settings reason — empty until Sinan / the müşavir enter it, SAT-D148: then the
/// screen offers nothing and says why). Reference data, no amounts.
/// </summary>
public sealed record SalesTaxOptionsResponse(
    DateOnly On,
    string BuyerSituationsState,
    string? BuyerSituationsReason,
    long? BuyerSituationListVersion,
    IReadOnlyList<SalesTaxBuyerSituationOptionV1> BuyerSituations,
    string ExemptionCodesState,
    string? ExemptionCodesReason,
    IReadOnlyList<SalesTaxCodeOptionV1> ExemptionCodes,
    IReadOnlyList<string> ItemTypes,
    // SAT-D181 (EK26): the VAT rate and withholding ratio lists a line change picks from (each Resolved, or Unresolved with the
    // Settings reason — empty until entered, SAT-D148).
    string VatRatesState,
    string? VatRatesReason,
    IReadOnlyList<SalesTaxRateOptionV1> VatRates,
    string WithholdingRatiosState,
    string? WithholdingRatiosReason,
    IReadOnlyList<SalesTaxRateOptionV1> WithholdingRatios);

public static class SalesDraftTaxApiV1
{
    public const string TaxPath = "/api/v1/sales/drafts/{id}/tax";
    public const string TaxOptionsPath = "/api/v1/sales/tax-options";

    /// <summary>The rate states of <see cref="SalesDraftLineTaxV1.RateState"/>.</summary>
    public const string RateComputed = "Computed";
    public const string RateNoSource = "NoSource";

    /// <summary>The buyer tax's wire shape (create and header revision): a non-empty situation, codes and document within bounds.</summary>
    internal static void RequireBuyerTaxShape(SalesDraftBuyerTaxV1 buyer)
    {
        if (string.IsNullOrEmpty(buyer.Situation) || buyer.Situation.Length > 64
            || buyer.ExemptionCode is { Length: 0 or > 16 } || buyer.DocumentNo is { Length: 0 or > 64 })
            throw new JsonException("Invalid buyer tax.");
    }

    /// <summary>A line override's wire shape: a non-empty situation and reason, bounded codes.</summary>
    internal static void RequireOverrideShape(string? buyerSituation, string? reason, string? itemType, string? exemptionCode,
        string? vatRateCode = null, string? withholdingRatioCode = null)
    {
        if (string.IsNullOrEmpty(buyerSituation) || buyerSituation.Length > 64 || reason is null || reason.Length > 4000
            || itemType is { Length: 0 or > 32 } || exemptionCode is { Length: 0 or > 16 }
            || vatRateCode is { Length: 0 or > 32 } || withholdingRatioCode is { Length: 0 or > 32 })
            throw new JsonException("Invalid line tax override.");
    }

    /// <summary>A commercial revision line's tax choice: a known mode; <c>Default</c> carries nothing else.</summary>
    internal static void RequireChoiceShape(SalesDraftLineTaxChoiceV1 choice)
    {
        switch (choice.Mode)
        {
            case SalesDraftLineTaxModesV1.Default:
                if (choice.BuyerSituation is not null || choice.Reason is not null || choice.ItemType is not null
                    || choice.ExemptionCode is not null || choice.VatRateCode is not null || choice.WithholdingRatioCode is not null)
                    throw new JsonException("A default tax choice carries no basis.");
                break;
            case SalesDraftLineTaxModesV1.Override:
                RequireOverrideShape(choice.BuyerSituation, choice.Reason, choice.ItemType, choice.ExemptionCode, choice.VatRateCode,
                    choice.WithholdingRatioCode);
                break;
            default:
                throw new JsonException("Unknown tax choice mode.");
        }
    }

    /// <summary>The <c>on</c> query of the options read: a strict <c>yyyy-MM-dd</c> date.</summary>
    public static bool TryParseOn(string? text, out DateOnly on)
    {
        on = default;
        return text is not null && Regex.IsMatch(text, "\\A[0-9]{4}-[0-9]{2}-[0-9]{2}\\z")
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out on);
    }
}

/// <summary>The tax read and options use cases (the draft service implements them).</summary>
public interface ISalesDraftTaxService
{
    Task<SalesOrderDraftTaxResponse> GetDraftTaxAsync(Guid draftId, CancellationToken cancellationToken = default);

    Task<SalesTaxOptionsResponse> GetTaxOptionsAsync(DateOnly on, CancellationToken cancellationToken = default);
}
