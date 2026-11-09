namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D16 / SAT-D17 / SAT-D168 (EK24) / SAT-D214–D216 (EK37): one recorded closure of an order line with a shortfall — which
/// line, the revision whose measured quantity the shortfall was judged against, that measured quantity (the realization basis,
/// verbatim), the SHIPPED total at the closure (the delivery owner's total, exact, plain invariant text in the line's order
/// unit), the remainder counted as cancelled ("iptal edilen kalan": measured minus shipped, exact), the SAT-D15 band of the
/// shortfall at the closure, who closed and when. At most one per line, append-only: a closure is never changed or deleted and a
/// closed line cannot ship again (SAT-D215). No reason (SAT-D215: "gerekçe zorunlu değil").
/// </summary>
public sealed record LineClosureFacts(
    Guid LineId,
    int BasisRevision,
    string Ordered,
    string ShippedAtClosure,
    string CancelledRemainder,
    DeliveryToleranceBand Band,
    string ClosedBy,
    DateTimeOffset ClosedAtUtc);

/// <summary>
/// The caller's input for one line closure, read by the caller from the owners and judged by the realization calculator BEFORE
/// the domain runs (the shortfall is in the within, warn or approval band; the block band never closes a line, SAT-D17): the
/// line, the realization basis revision and its measured quantity text, the delivery owner's shipped total (exact, in the line's
/// order unit), the band and the actors the delivery owner names as having made the line's shipments (SAT-D216). An empty or
/// unknown set of shippers is never an input: the caller refuses before (fail-closed).
/// </summary>
public sealed record LineClosureInput(
    Guid LineId,
    int BasisRevision,
    string Ordered,
    decimal Shipped,
    DeliveryToleranceBand Band,
    IReadOnlyCollection<string> ShippedBy);
