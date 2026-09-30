namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// Partial delivery (user decision 2026-09-30): what happens to the UNDELIVERED remainder of a partly delivered line.
/// <see cref="Cancel"/> ends the line at what the delivery owner has loaded (the delivered and loaded part is kept);
/// <see cref="Revise"/> sets a new ordered quantity, never below that loaded part.
/// </summary>
public enum RemainderChangeKind
{
    Cancel = 1,
    Revise = 2,
}

/// <summary>A remainder change is requested, then approved or rejected by an approver; nothing changes on the line before the approval.</summary>
public enum RemainderChangeState
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>The line's remainder as the approved changes left it; <see cref="Open"/> until one was approved.</summary>
public enum LineRemainderState
{
    Open = 1,
    Revised = 2,
    Cancelled = 3,
}

/// <summary>
/// The delivery owner's (Logistics) recorded totals of ONE order line in the line's own order unit at one moment, read through
/// the Sales-declared loading port: loaded ≥ shipped ≥ delivered. The loaded total is the floor — a remainder change never
/// takes the line below what is delivered or already on a vehicle. Kept with the request and with the decision as history.
/// </summary>
public sealed record RemainderFloor(decimal Loaded, decimal Shipped, decimal Delivered);

/// <summary>
/// One requested remainder change as recorded: its number on the order (1, 2, …), the commercial revision it was asked
/// against, the line, the kind, the line's quantity text when asked, the requested quantity (Revise only; the text kept
/// verbatim), the required reason, who asked and when, and the owner's floor at that moment. Append-only.
/// </summary>
public sealed record RemainderChangeRequest(
    int ChangeNo,
    int Revision,
    Guid LineId,
    int LineNo,
    RemainderChangeKind Kind,
    string OrderedQuantityText,
    decimal? RequestedQuantity,
    string? RequestedQuantityText,
    string Reason,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    RemainderFloor Floor);

/// <summary>
/// The decision on a remainder change: approved (with the line's resulting quantity and the owner's floor read afresh at the
/// decision) or rejected (an optional reason, nothing changes on the line). Recorded once; never rewritten.
/// </summary>
public sealed record RemainderChangeDecision(
    RemainderChangeState Outcome,
    string DecidedBy,
    DateTimeOffset DecidedAtUtc,
    string? Reason,
    decimal? ResultQuantity,
    string? ResultQuantityText,
    RemainderFloor? Floor);

/// <summary>A remainder change: the request and, once decided, the decision.</summary>
public sealed record RemainderChangeFacts(RemainderChangeRequest Request, RemainderChangeDecision? Decision)
{
    public RemainderChangeState State => Decision?.Outcome ?? RemainderChangeState.Pending;
}
