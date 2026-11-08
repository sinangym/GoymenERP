namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D30 / SAT-D213 (EK37): one recorded approval of an approval-band over-delivery on one order line — which line, its
/// approval number on that line (1, 2, … in order), the measured quantity the over-delivery was judged against (the realization
/// basis of <see cref="BasisRevision"/>, verbatim), the SHIPPED total the approval covers (the delivery owner's total at the
/// approval, exact, plain invariant text in the line's order unit), who approved it and when. Append-only history: an approval is
/// never changed or deleted. SAT-D213: it covers the shipped total at the approval ("onay anındaki sevk miktarı"); when the
/// shipped total grows above it, the line awaits a NEW approval, recorded as the next number.
/// </summary>
public sealed record OverDeliveryApprovalFacts(
    Guid LineId,
    int ApprovalNo,
    int BasisRevision,
    string Ordered,
    string ApprovedShipped,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc);

/// <summary>
/// The caller's input for one over-delivery approval, read by the caller from the owners and judged by the realization
/// calculator BEFORE the domain runs (the line is <see cref="LineRealizationState.AwaitingApproval"/> under the Settings tolerance):
/// the line, the realization basis revision and its measured quantity text, the delivery owner's shipped total (exact, in the
/// line's order unit) and the actors the delivery owner names as having made the line's shipments (SAT-D216). An empty or unknown
/// set of shippers is never an input: the caller refuses before (fail-closed).
/// </summary>
public sealed record OverDeliveryApprovalInput(
    Guid LineId,
    int BasisRevision,
    string Ordered,
    decimal Shipped,
    IReadOnlyCollection<string> ShippedBy);
