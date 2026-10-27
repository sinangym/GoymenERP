namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SINAN_KARARLARI_2026-10-21 No 30 / SAT-D12: the recorded force-closure of an approved order — the commercial revision that
/// was closed, who closed it, when, the reason (required), the impact preview the user confirmed and, per line, the closed
/// ("iptal edilen") remainder. Recorded once; the approval facts, the lines (their quantities included), the archived
/// revisions and the delivery schedules stay exactly as they were. The closed remainder is a separate axis (DEC-SAL-001); no
/// stock or money event exists.
/// </summary>
public sealed record ForceClosureFacts(
    int Revision,
    string ClosedBy,
    DateTimeOffset ClosedAtUtc,
    string Reason,
    ForceClosureImpactSnapshot Impact,
    IReadOnlyList<ForceClosureLineSnapshot> Lines);

/// <summary>
/// The impact preview the force-closure was decided on, as the caller computed it from the owners' answers: its token (what
/// the user confirmed) and each source's status with whether it blocked. Opaque text to the domain, kept as history; the
/// domain only refuses to record a closure over a source the caller marked as blocking.
/// </summary>
public sealed record ForceClosureImpactSnapshot(string Token, IReadOnlyList<ForceClosureImpactEntry> Sources);

/// <summary>One impact source (e.g. <c>invoicing.uninvoiced-dispatch-notes</c>), its status and whether it blocked.</summary>
public sealed record ForceClosureImpactEntry(string Source, string Status, bool Blocks);

/// <summary>
/// The caller's input for one line: the delivery owner's (Logistics) SHIPPED total in the line's own order unit at the
/// closure, exact (Ö2, never rounded). The domain derives the closed remainder from it (ordered − shipped).
/// </summary>
public sealed record ForceClosureLineInput(Guid LineId, decimal Shipped);

/// <summary>
/// One line as closed: its identity and position, the ordered quantity text (the line's own, verbatim), the shipped total and
/// the closed remainder (ordered − shipped), both as plain invariant text in the line's order unit (Ö2: exact, never rounded,
/// trailing zeros dropped).
/// </summary>
public sealed record ForceClosureLineSnapshot(Guid LineId, int LineNo, string Ordered, string Shipped, string ClosedRemainder);
