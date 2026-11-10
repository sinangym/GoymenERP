using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- SAT-D251..SAT-D255 (EK50) / API-11: the order source plan --------------------------------------------------------------
//
// SAT-D251: "Serbest stok yeterliyse 'stok', değilse ürün tipine göre 'üretim' veya 'satın alma' önerilir; kullanıcı
// değiştirebilir." SAT-D252: "parçaların toplamı satır miktarına eşit olmak zorunda." SAT-D253: "teslime kadar serbest"; kısmen
// teslim edilmiş satırda "teslim edilen miktar kilitli kalır". SAT-D254: "Uyarıyla izin ver". SAT-D255: "tahsis edilen miktar
// planda kendiliğinden 'stok' görünür. Planda 'stok'u ayrıca girmek gerekmez."

/// <summary>
/// API-11: POST …/{id}/source-plan — the expected draft version (text), the explicit confirmation of the linked-supply warning
/// (SAT-D254) and the planned lines. Strict: every member present, no other member. The STOCK and LOCKED parts are never sent
/// (SAT-D255 / SAT-D253): the server derives them at the command from the allocation and delivery owners.
/// </summary>
public sealed record SalesDraftSourcePlanRequest(string ExpectedVersion, bool ConfirmLinkedSupply,
    IReadOnlyList<SalesDraftSourcePlanLineRequestV1> Lines);

/// <summary>One planned line: the line and the non-stock parts the user entered (exact non-negative decimal text, line unit).</summary>
public sealed record SalesDraftSourcePlanLineRequestV1(Guid LineId, string Production, string Purchasing, string Subcontract);

/// <summary>The strict wire reader of the source plan save (the line closure's shape rules).</summary>
public static class SalesDraftSourcePlanJson
{
    /// <summary>A save names at most this many lines (a technical bound of the body, never a business rule).</summary>
    public const int MaximumLines = 500;

    public static SalesDraftSourcePlanRequest Read(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftSourcePlanRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        if (string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > 19
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > MaximumLines)
            throw new JsonException("Invalid lines.");
        var seen = new HashSet<Guid>();
        foreach (var line in request.Lines)
        {
            if (line is null || line.LineId == Guid.Empty || !seen.Add(line.LineId))
                throw new JsonException("Invalid line.");
            if (line.Production is null || line.Purchasing is null || line.Subcontract is null)
                throw new JsonException("Invalid part.");
            SalesDraftJson.RequireDecimalShape(line.Production, positive: false);
            SalesDraftJson.RequireDecimalShape(line.Purchasing, positive: false);
            SalesDraftJson.RequireDecimalShape(line.Subcontract, positive: false);
        }

        return request;
    }
}

/// <summary>SAT-D251: the suggestion words on the wire.</summary>
public static class SalesSourcePlanSuggestionsV1
{
    public const string Stock = "Stock";
    public const string Production = "Production";
    public const string Purchasing = "Purchasing";

    /// <summary>"kullanılamıyor": a fact the rule needs cannot be read or is undecided; no suggestion is invented.</summary>
    public const string Unavailable = "Unavailable";

    public static IReadOnlyList<string> All { get; } = [Stock, Production, Purchasing, Unavailable];
}

/// <summary>SAT-D251: why the suggestion is what it is.</summary>
public static class SalesSourcePlanSuggestionReasonsV1
{
    public const string AllocationCoversRemainder = "AllocationCoversRemainder";
    public const string FreeStockEnough = "FreeStockEnough";
    public const string FreeStockUnavailable = "FreeStockUnavailable";
    public const string SupplyTypeUnmapped = "SupplyTypeUnmapped";
    public const string LineDelivered = "LineDelivered";
    public const string DeliveryUnreadable = "DeliveryUnreadable";
    public const string AllocationUnreadable = "AllocationUnreadable";
    public const string UnitUnsupported = "UnitUnsupported";

    public static IReadOnlyList<string> All { get; } =
    [
        AllocationCoversRemainder, FreeStockEnough, FreeStockUnavailable, SupplyTypeUnmapped, LineDelivered, DeliveryUnreadable,
        AllocationUnreadable, UnitUnsupported,
    ];
}

/// <summary>SAT-D255: whether the stock part could be derived from the allocation owner ("okunamadı" otherwise, never zero).</summary>
public static class SalesSourcePlanStockStatusesV1
{
    public const string Derived = "Derived";
    public const string Unreadable = "Unreadable";

    public static IReadOnlyList<string> All { get; } = [Derived, Unreadable];
}

/// <summary>The warnings of a plan line (shown; none of them changes a recorded plan).</summary>
public static class SalesSourcePlanWarningsV1
{
    /// <summary>SAT-D255: the active allocation now differs from the stock part of the recorded plan ("plan ile tahsis uyumsuz").</summary>
    public const string PlanAllocationMismatch = "PlanAllocationMismatch";

    /// <summary>SAT-D253: the delivered (locked) part now differs from the one the recorded plan was balanced with.</summary>
    public const string PlanDeliveryMismatch = "PlanDeliveryMismatch";

    /// <summary>The line quantity now differs from the one the recorded plan was balanced with.</summary>
    public const string PlanQuantityMismatch = "PlanQuantityMismatch";

    /// <summary>SAT-D254: an open production or purchasing document is linked to the line (or to the whole order).</summary>
    public const string LinkedSupplyOpen = "LinkedSupplyOpen";

    /// <summary>SAT-D254: a link owner cannot be read; a save is refused until it can.</summary>
    public const string LinkedSupplyUnreadable = "LinkedSupplyUnreadable";

    /// <summary>SAT-D253: the delivery owner cannot be read; a save is refused.</summary>
    public const string DeliveryUnreadable = "DeliveryUnreadable";

    /// <summary>SAT-D255: the allocation owner cannot be read ("okunamadı"); a save is refused.</summary>
    public const string AllocationUnreadable = "AllocationUnreadable";

    /// <summary>The line's unit is not the kilogram (NEEDS-SINAN conversion); a save is refused.</summary>
    public const string UnitUnsupported = "UnitUnsupported";

    /// <summary>No plan was recorded for the line yet.</summary>
    public const string NoPlan = "NoPlan";

    public static IReadOnlyList<string> All { get; } =
    [
        PlanAllocationMismatch, PlanDeliveryMismatch, PlanQuantityMismatch, LinkedSupplyOpen, LinkedSupplyUnreadable,
        DeliveryUnreadable, AllocationUnreadable, UnitUnsupported, NoPlan,
    ];
}

/// <summary>Why a line's plan cannot be saved now (null when it can).</summary>
public static class SalesSourcePlanLockReasonsV1
{
    public const string OrderState = "OrderState";
    public const string LineClosed = "LineClosed";
    public const string LineDelivered = "LineDelivered";
    public const string DeliveryUnreadable = "DeliveryUnreadable";
    public const string AllocationUnreadable = "AllocationUnreadable";
    public const string UnitUnsupported = "UnitUnsupported";

    public static IReadOnlyList<string> All { get; } =
        [OrderState, LineClosed, LineDelivered, DeliveryUnreadable, AllocationUnreadable, UnitUnsupported];
}

/// <summary>
/// API-11: GET …/{id}/source-plan under the draft read. The order's version (the next save's expected version), its revision,
/// whether a save is possible at all (<paramref name="Writable"/>: order-level), each supply kind's link status (SAT-D254, the
/// cancellation preview's vocabulary) and every active line.
/// </summary>
public sealed record SalesOrderDraftSourcePlanResponse(
    string Version,
    string Revision,
    bool Writable,
    IReadOnlyList<SalesDraftSourcePlanLinkedSupplyV1> LinkedSupply,
    IReadOnlyList<SalesDraftSourcePlanLineV1> Lines);

/// <summary>One supply kind (<see cref="SalesLinkedSupplyKindsV1"/>) and its status (<see cref="SalesCancellationImpactStatusesV1"/>).</summary>
public sealed record SalesDraftSourcePlanLinkedSupplyV1(string Kind, string Status);

/// <summary>
/// One active line of the plan read: its quantity (line unit), the locked part (shipped total; null when the delivery owner is
/// unreadable), the remainder (quantity − locked), the stock part derived from the allocation (null and
/// <c>Unreadable</c> when it cannot be derived — never zero), the suggestion and its reason, the current recorded plan (null when
/// none), whether this line can be saved now and why not, and the warnings.
/// </summary>
public sealed record SalesDraftSourcePlanLineV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Quantity,
    string? Locked,
    string? Remaining,
    string StockStatus,
    string? Stock,
    string Suggestion,
    string SuggestionReason,
    SalesDraftSourcePlanSavedV1? Saved,
    bool Writable,
    string? LockReason,
    IReadOnlyList<string> Warnings);

/// <summary>The current recorded plan of a line (exact quantity texts of the line unit) and who recorded it at which version.</summary>
public sealed record SalesDraftSourcePlanSavedV1(
    string DraftVersion,
    string LineQuantity,
    string Locked,
    string Stock,
    string Production,
    string Purchasing,
    string Subcontract,
    bool LinkedSupplyConfirmed,
    string SavedBy,
    DateTimeOffset SavedAtUtc);
