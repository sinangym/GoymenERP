namespace GoymenERP.Modules.Sales.Domain.Drafts;

// ---- SAT-D251..SAT-D255 (EK50) / API-11: the order source plan ----------------------------------------------------------------
//
// DEC-SAL-004: "Sipariş satırı fiziksel stok, onaylı alış, üretim planı, dış tedarik veya karma kaynakla karşılanabilir."
// SAT-D251: the system suggests (one deterministic backend rule, here); SAT-D252: a mixed line is split by quantity and the parts
// add up to the line quantity; SAT-D253: free until delivery (no revision, no re-approval), the delivered part locked; SAT-D254:
// an open linked supply warns and needs an explicit confirmation; SAT-D255: the stock part is derived from the Inventory
// allocation, never entered, and the plan reserves nothing.

/// <summary>SAT-D251: the source the system suggests for a line (a suggestion only; it records and reserves nothing).</summary>
public enum SourcePlanSuggestion
{
    /// <summary>The line's remainder is (or can be) met from stock.</summary>
    Stock = 1,

    /// <summary>Production, by the product type (never suggested while the product-type mapping is undecided).</summary>
    Production = 2,

    /// <summary>Purchasing, by the product type (never suggested while the product-type mapping is undecided).</summary>
    Purchasing = 3,

    /// <summary>"kullanılamıyor": a fact the rule needs cannot be read or is not decided; no suggestion is invented.</summary>
    Unavailable = 4,
}

/// <summary>Why the rule answered what it answered (shown beside the suggestion).</summary>
public enum SourcePlanSuggestionReason
{
    /// <summary>The line's own active allocation already covers its undelivered remainder.</summary>
    AllocationCoversRemainder = 1,

    /// <summary>The free stock (the line's own allocation NOT counted as free) covers what is still needed.</summary>
    FreeStockEnough = 2,

    /// <summary>No free-stock read is available to Sales (no Inventory port on main): no stock suggestion is made.</summary>
    FreeStockUnavailable = 3,

    /// <summary>Free stock is short and the product type → production / purchasing mapping is not decided (NEEDS-SINAN).</summary>
    SupplyTypeUnmapped = 4,

    /// <summary>The line is fully delivered; nothing is left to plan.</summary>
    LineDelivered = 5,

    /// <summary>The delivery owner could not be read.</summary>
    DeliveryUnreadable = 6,

    /// <summary>The allocation owner could not be read.</summary>
    AllocationUnreadable = 7,

    /// <summary>The line's order unit is not the kilogram (allocation kilograms are not converted, NEEDS-SINAN).</summary>
    UnitUnsupported = 8,
}

/// <summary>One suggestion and its reason.</summary>
public sealed record SourcePlanSuggestionResult(SourcePlanSuggestion Suggestion, SourcePlanSuggestionReason Reason);

/// <summary>
/// One line of a source plan as the caller derived it at the command: the line, the LOCKED part (the delivery owner's shipped
/// total; SAT-D253 (3), not attributed to any source), the STOCK part (the line's active Inventory allocation; SAT-D255, never
/// entered) and the parts the user entered (production, purchasing, subcontract). All in the line's order unit (kilograms only
/// while the unit conversion is undecided).
/// </summary>
public sealed record SourcePlanLineInput(Guid LineId, decimal Locked, decimal Stock, decimal Production, decimal Purchasing, decimal Subcontract);

/// <summary>The plan the command records: the listed lines and whether the user confirmed the linked-supply warning (SAT-D254).</summary>
public sealed record SourcePlanInput(IReadOnlyList<SourcePlanLineInput> Lines, bool LinkedSupplyConfirmed);

/// <summary>
/// One recorded plan line (append-only: a new save is a new row at the draft's new version; the current plan of a line is its
/// row with the highest <paramref name="DraftVersion"/>). <paramref name="LineQuantity"/> is the line quantity the parts were
/// balanced against at the save, so a later quantity change is visible as a mismatch, never silently re-balanced.
/// </summary>
public sealed record SourcePlanLineFacts(
    Guid LineId,
    int LineNo,
    decimal LineQuantity,
    decimal Locked,
    decimal Stock,
    decimal Production,
    decimal Purchasing,
    decimal Subcontract,
    bool LinkedSupplyConfirmed,
    string SavedBy,
    DateTimeOffset SavedAtUtc,
    long DraftVersion);

/// <summary>What one save recorded: one fact per listed line, all at the same new draft version.</summary>
public sealed record SourcePlanFacts(IReadOnlyList<SourcePlanLineFacts> Lines);

/// <summary>
/// SAT-D251 / SAT-D252: the source plan's ONE deterministic rule (the screen never computes a suggestion or a sum). Pure: no
/// owner is read here; the caller passes what it read (null = unreadable / not available).
/// </summary>
public static class SourcePlanCalculator
{
    /// <summary>
    /// SAT-D251 suggestion for one line. Reviewer note (c), stated and tested: the line's OWN active allocation is subtracted
    /// from what the line still needs and is never counted as free stock (no double count) — <c>need = (quantity − shipped) −
    /// ownAllocated</c>; a need at or below zero is "stock" (the allocation covers it); otherwise the free stock (which, by the
    /// Inventory owner's own definition, excludes every allocation) must cover the need. Unreadable inputs answer
    /// <see cref="SourcePlanSuggestion.Unavailable"/>; a short free stock answers Unavailable too while the product type →
    /// production/purchasing mapping is undecided. Subcontract is never suggested (SAT-D251).
    /// </summary>
    public static SourcePlanSuggestionResult Suggest(decimal lineQuantity, decimal? shipped, decimal? ownAllocated, decimal? freeStock)
    {
        if (lineQuantity <= 0m || shipped < 0m || ownAllocated < 0m || freeStock < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(lineQuantity), "Kaynak önerisi negatif olmayan miktarlarla hesaplanır.");
        }

        if (shipped is not { } delivered)
        {
            return new(SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.DeliveryUnreadable);
        }

        if (delivered >= lineQuantity)
        {
            return new(SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.LineDelivered);
        }

        if (ownAllocated is not { } allocated)
        {
            return new(SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.AllocationUnreadable);
        }

        var need = lineQuantity - delivered - allocated;
        if (need <= 0m)
        {
            return new(SourcePlanSuggestion.Stock, SourcePlanSuggestionReason.AllocationCoversRemainder);
        }

        if (freeStock is not { } free)
        {
            return new(SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.FreeStockUnavailable);
        }

        return free >= need
            ? new(SourcePlanSuggestion.Stock, SourcePlanSuggestionReason.FreeStockEnough)
            : new(SourcePlanSuggestion.Unavailable, SourcePlanSuggestionReason.SupplyTypeUnmapped);
    }

    /// <summary>
    /// SAT-D252 / SAT-D253 (3) / reviewer note (a): the ONE equality of a save — <c>locked + stock + production + purchasing +
    /// subcontract == lineQuantity</c>. On a partially delivered line this is exactly "the remainder's parts add up to the
    /// undelivered quantity" (locked = delivered); no second equality is checked.
    /// </summary>
    public static bool IsBalanced(decimal lineQuantity, decimal locked, decimal stock, decimal production, decimal purchasing,
        decimal subcontract) =>
        checked(locked + stock + production + purchasing + subcontract) == lineQuantity;
}
