namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- SAT-D32 / SAT-D168 / SAT-D215: the user closure of a "kapanmaya hazır" order ------------------------------------------
//
// SAT-D32 (EK3, verbatim): "Tüm eksenler tamamlansa da sipariş kendiliğinden kapanmaz; 'kapanmaya hazır' işareti çıkar ve
// kullanıcı kapatır." SAT-D168 (EK24): the closure is its own name (`sales.orders.close`; who holds it — Satış Uzmanı, Satış
// Müdürü, GM, Admin per SAT-D168/D188/D190 — is a human gate, no role receives it here). SAT-D215 (EK37): no reason is required.
// The precondition is the status read's own `readyToClose`, recomputed inside the save from the same calculation; a closure is
// never decided on a stale flag. While the source (MOD-05/API-11), invoicing (S5) and financial-closure (MOD-04) owners do not
// exist those axes are NoSource, so the flag — and the command — are refused (409 sales_order_not_ready_to_close).

/// <summary>SAT-D32: POST …/{id}/close — the expected version (text) only; no reason (SAT-D215), no other member.</summary>
public sealed record SalesDraftCloseRequest(string ExpectedVersion);

/// <summary>The recorded user closure: the revision closed, who closed it and when. No reason, no remainder.</summary>
public sealed record SalesDraftUserClosureV1(string Revision, string ClosedBy, DateTimeOffset ClosedAtUtc);

/// <summary>GET …/{id}/close — the recorded user closure, or null while the order is not user-closed (under the draft read).</summary>
public sealed record SalesOrderDraftUserClosureResponse(SalesDraftUserClosureV1? UserClosure);

/// <summary>
/// SAT-D32: the composition seam of the three status axes whose owners do not exist on main — source/allocation strategy
/// (MOD-05, API-11), invoicing (S5) and financial closure (MOD-04). NOTHING implements or registers it on main: the axes are
/// then <c>NoSource</c> and never complete, so <c>readyToClose</c> is false and the user closure is refused. When an owner
/// lands, its binding is reviewed here (which state is its complete one is that owner's answer, not a Sales default); until
/// then only tests bind a synthetic source to prove the ready path. A source that throws or answers a malformed value makes
/// the three axes <c>Unreadable</c> (never complete). Sales-owned; no other module implements or calls it today (SAT-D209).
/// </summary>
public interface ISalesOrderOwnerAxesSourceV1
{
    Task<SalesOrderOwnerAxesV1?> ReadOrderOwnerAxesAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>The three owner-less axes of one order as their owners answer them.</summary>
public sealed record SalesOrderOwnerAxesV1(Guid CompanyId, Guid OrderId, SalesOrderOwnerAxisV1 Source, SalesOrderOwnerAxisV1 Invoicing,
    SalesOrderOwnerAxisV1 FinancialClosure);

/// <summary>One owner axis: its wire state and whether the owner itself calls that state complete.</summary>
public sealed record SalesOrderOwnerAxisV1(string State, bool Complete);
