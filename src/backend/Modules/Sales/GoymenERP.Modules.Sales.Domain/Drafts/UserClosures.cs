namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D32 / SAT-D168: the recorded USER closure of an order that was "kapanmaya hazır" (every status axis complete) — the
/// commercial revision that was closed, who closed it and when. No reason (SAT-D215: "gerekçe zorunlu değil"; the order is
/// complete, nothing is cut) and no closed remainder: a ready order has nothing left to deliver. Recorded once; the approval
/// facts, the lines (their quantities included), the archived revisions and the delivery schedules stay exactly as they were.
/// A user closure and a force-closure (No 30 / SAT-D12, <see cref="ForceClosureFacts"/>) share the lifecycle state
/// <see cref="SalesDraftState.Closed"/> but are separate records — an order carries at most one of them (reversible
/// implementation detail, EK37 "Sınırlar").
/// </summary>
public sealed record UserClosureFacts(int Revision, string ClosedBy, DateTimeOffset ClosedAtUtc);
