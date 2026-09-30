using System.Text.Json;
using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// Partial delivery (user decision 2026-09-30): POST …/{id}/remainder-changes — ask to cancel (<c>kind</c> = "Cancel") or revise
/// (<c>kind</c> = "Revise", with the new ordered <c>quantity</c> in the line's own order unit) the UNDELIVERED remainder of one
/// partly delivered line, with a required reason. Strict: every member present (<c>quantity</c> null for a cancel), no other
/// member. Nothing changes on the line until an approver approves the request.
/// </summary>
public sealed record SalesDraftRemainderChangeRequest(string ExpectedVersion, Guid LineId, string Kind, string? Quantity, string Reason);

/// <summary>
/// POST …/{id}/remainder-changes/{changeNo}/approval and …/rejection — the expected concurrency version and, for a rejection
/// only, an optional reason. Strict.
/// </summary>
public sealed record SalesDraftRemainderDecisionRequest(string ExpectedVersion, string? Reason = null);

/// <summary>The kinds, states and floor statuses as they travel (exact names).</summary>
public static class SalesRemainderChangeNamesV1
{
    public const string Cancel = "Cancel";
    public const string Revise = "Revise";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    /// <summary>A line's remainder: open (no approved change), revised, or cancelled (nothing more is loadable).</summary>
    public const string Open = "Open";
    public const string Revised = "Revised";
    public const string Cancelled = "Cancelled";

    /// <summary>The delivery owner answered the line's recorded totals / could not be read (numbers null, never 0).</summary>
    public const string Readable = "Readable";
    public const string Unreadable = "Unreadable";
}

/// <summary>
/// One recorded remainder change: its number, the revision it was asked against, the line, the kind and state, the ordered
/// quantity when asked and the requested quantity (Revise), the reason, who asked and when, the delivery owner's loaded /
/// shipped / delivered totals at the request; and the decision — who, when, the rejection reason, the resulting quantity and
/// the owner's totals read afresh at the approval. Exact decimal text in the line's order unit.
/// </summary>
public sealed record SalesDraftRemainderChangeV1(
    int ChangeNo,
    string Revision,
    Guid LineId,
    int LineNo,
    string Kind,
    string State,
    string OrderedQuantity,
    string? RequestedQuantity,
    string Reason,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string LoadedAtRequest,
    string ShippedAtRequest,
    string DeliveredAtRequest,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason,
    string? ResultQuantity,
    string? LoadedAtDecision);

/// <summary>
/// A line as it stands for a remainder change: its current ordered quantity and remainder state and, read afresh from the
/// delivery owner, what is loaded / shipped / delivered and the smallest quantity the line may be revised to (the loaded total).
/// The owner's numbers are null when the owner could not be read (the response's <c>floorStatus</c> says so) — never 0.
/// </summary>
public sealed record SalesDraftRemainderLineV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Quantity,
    string Remainder,
    string? Loaded,
    string? Shipped,
    string? Delivered,
    string? Minimum);

/// <summary>GET …/{id}/remainder-changes: the draft version, every line's remainder facts and every recorded change, oldest first.</summary>
public sealed record SalesOrderDraftRemainderChangesResponse(
    string DraftVersion,
    string FloorStatus,
    IReadOnlyList<SalesDraftRemainderLineV1> Lines,
    IReadOnlyList<SalesDraftRemainderChangeV1> Items);

/// <summary>The strict wire readers of the remainder commands (the quantity revision's shape rules).</summary>
public static class SalesDraftRemainderJson
{
    public static SalesDraftRemainderChangeRequest ReadChange(string json)
    {
        var request = JsonSerializer.Deserialize<SalesDraftRemainderChangeRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersion(request.ExpectedVersion);
        if (request.LineId == Guid.Empty)
            throw new JsonException("Invalid line.");
        if (request.Reason is null || request.Reason.Length > 4000)
            throw new JsonException("Invalid reason.");
        switch (request.Kind)
        {
            case SalesRemainderChangeNamesV1.Cancel when request.Quantity is null:
                break;
            case SalesRemainderChangeNamesV1.Revise when request.Quantity is not null:
                SalesDraftJson.RequireDecimalShape(request.Quantity, positive: true);
                break;
            default:
                throw new JsonException("Invalid kind or quantity.");
        }

        return request;
    }

    public static SalesDraftRemainderDecisionRequest ReadDecision(string json, bool allowReason)
    {
        var request = JsonSerializer.Deserialize<SalesDraftRemainderDecisionRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersion(request.ExpectedVersion);
        if (!allowReason && request.Reason is not null)
            throw new JsonException("A reason is only given with a rejection.");
        if (request.Reason?.Length > 4000)
            throw new JsonException("Invalid reason.");
        return request;
    }

    private static void RequireVersion(string? version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 19 || !Regex.IsMatch(version, "\\A[1-9][0-9]*\\z"))
            throw new JsonException("Invalid expected version.");
    }
}
