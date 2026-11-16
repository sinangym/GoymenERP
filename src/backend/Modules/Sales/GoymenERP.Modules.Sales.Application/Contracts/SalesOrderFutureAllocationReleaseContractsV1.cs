using System.Data;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// X1 (KLN-D28, SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §X1): the release of every active FUTURE supply allocation
/// (Inventory's, F-017) of a cancelled order's lines, in the cancellation's own SQL transaction. <see cref="LineIds"/> are the
/// order's line ids in ascending order; <see cref="ReasonText"/> is <see cref="SalesOrderFutureAllocationReleaseTextsV1.CancelledOrder"/>
/// (Inventory's release reason is bounded; the user's cancellation reason stays in the Sales cancellation record). The envelope is
/// the Sales command's own: its actor, permission, correlation, causation and Idempotency-Key go unchanged to the owner's audit.
/// </summary>
public sealed record SalesOrderFutureAllocationReleaseRequestV1(
    Guid OrderId,
    IReadOnlyList<Guid> LineIds,
    string ReasonText,
    SalesDraftWriteEnvelope Envelope);

/// <summary>
/// The host participant that releases a cancelled order's future supply allocations through the owner's release port
/// (Sales and Inventory do not reference each other). It runs inside the Sales store's transaction (the same connection), so the
/// release and the cancellation commit together or not at all. Lock order (F-017 §3.2): every sales line of the order first,
/// ascending, then every distinct purchase line ascending. Each allocation is released whole with the owner's expected version.
/// <para>
/// A refusal is <see cref="SalesFutureAllocationReleaseException"/>: <see cref="SalesFutureAllocationReleaseException.Terminal"/>
/// refusals (the owner's version, state or selection conflict) are the key's recorded answer; every other refusal (a busy line,
/// the owner's concurrent change, an unreadable owner) is not recorded — the retry asks again. A composition without this
/// participant keeps the fail-closed rule: an order with a future allocation is not cancelled.
/// </para>
/// </summary>
public interface ISalesOrderFutureAllocationReleaseParticipantV1
{
    Task ReleaseForCancelledOrderAsync(IDbTransaction transaction, SalesOrderFutureAllocationReleaseRequestV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary>The participant's refusal: the HTTP status and the owner's problem code, carried unchanged into the Sales answer.</summary>
public sealed class SalesFutureAllocationReleaseException(int status, string code, bool terminal, Exception? inner = null)
    : Exception(code, inner)
{
    public int Status { get; } = status;

    public string Code { get; } = code;

    /// <summary>True when the refusal is the key's terminal answer (recorded and replayed); false when the retry asks again.</summary>
    public bool Terminal { get; } = terminal;
}

/// <summary>The fixed release reason text (Inventory's own audit); the user's cancellation reason stays with Sales.</summary>
public static class SalesOrderFutureAllocationReleaseTextsV1
{
    public const string CancelledOrder =
        "Onaylı satış siparişi iptal edildi; satıra bağlı gelecek tedarik ayırması aynı işlemde bırakıldı (KLN-D28).";
}
