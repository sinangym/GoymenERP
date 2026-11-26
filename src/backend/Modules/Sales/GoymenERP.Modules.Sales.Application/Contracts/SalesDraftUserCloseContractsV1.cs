namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- SAT-D32 / SAT-D168 / SAT-D215: the user closure of a "kapanmaya hazır" order ------------------------------------------
//
// SAT-D32 (EK3, verbatim): "Tüm eksenler tamamlansa da sipariş kendiliğinden kapanmaz; 'kapanmaya hazır' işareti çıkar ve
// kullanıcı kapatır." SAT-D168 (EK24): the closure is its own name (`sales.orders.close`; who holds it — Satış Uzmanı, Satış
// Müdürü, GM, Admin per SAT-D168/D188/D190 — is a human gate, no role receives it here). SAT-D215 (EK37): no reason is required.
// The precondition is the status read's own `readyToClose`, recomputed inside the save from the same calculation; a closure is
// never decided on a stale flag. An uncomposed owner axis stays NoSource; missing or invalid proof never completes it.
// SAT-D221 (EK40) requires every axis. SAT-D478/479 define real source shares and valid invoice settlement; bank matching
// remains separate. Force-close (SAT-D12) retains its own rules and is not a replacement for normal closure.

/// <summary>SAT-D32: POST …/{id}/close — the expected version (text) only; no reason (SAT-D215), no other member.</summary>
public sealed record SalesDraftCloseRequest(string ExpectedVersion);

/// <summary>The recorded user closure: the revision closed, who closed it and when. No reason, no remainder.</summary>
public sealed record SalesDraftUserClosureV1(string Revision, string ClosedBy, DateTimeOffset ClosedAtUtc);

/// <summary>GET …/{id}/close — the recorded user closure, or null while the order is not user-closed (under the draft read).</summary>
public sealed record SalesOrderDraftUserClosureResponse(SalesDraftUserClosureV1? UserClosure);

/// <summary>
/// SAT-D32/SAT-D478/479: host composition of the source, invoicing and financial owner observations.
/// An absent composition is NoSource; malformed or unreadable evidence never completes an axis. The real caller's
/// authorization is preserved across owner awaits; neither an owner answer nor the composition grants authority.
/// </summary>
public interface ISalesOrderOwnerAxesSourceV1
{
    Task<SalesOrderOwnerAxesV1?> ReadOrderOwnerAxesAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>SAT-D478/479: the real Sales snapshot and caller's fresh read/close fence.
    /// Existing synthetic sources retain the old seam; production composition requires this overload.</summary>
    async Task<SalesOrderOwnerAxesV1?> ReadOrderOwnerAxesAsync(SalesOrderClosureQueryV1 query,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        await freshConsumerFence(cancellationToken);
        var answer = await ReadOrderOwnerAxesAsync(query.OrderId, cancellationToken);
        await freshConsumerFence(cancellationToken);
        return answer;
    }
}

/// <summary>Internal Sales-owned identities and the already measured shipment totals; no prices or client authority.</summary>
public sealed record SalesOrderClosureQueryV1(Guid CompanyId, Guid OrderId, Guid PartyId, Guid CompanyAccountId,
    bool IsSubcontractService, IReadOnlyList<SalesOrderClosureLineV1> Lines);

public sealed record SalesOrderClosureLineV1(Guid LineId, Guid ProductId, Guid UnitId, bool UnitIsKilogram,
    string? ShippedQuantity);

/// <summary>The three composed owner axes of one order.</summary>
public sealed record SalesOrderOwnerAxesV1(Guid CompanyId, Guid OrderId, SalesOrderOwnerAxisV1 Source, SalesOrderOwnerAxisV1 Invoicing,
    SalesOrderOwnerAxisV1 FinancialClosure);

/// <summary>One owner axis: its wire state and whether the owner itself calls that state complete.</summary>
public sealed record SalesOrderOwnerAxisV1(string State, bool Complete);
