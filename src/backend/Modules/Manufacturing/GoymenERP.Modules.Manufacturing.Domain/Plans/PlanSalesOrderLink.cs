namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// Üretim U2-b / API-16 (SAT-D219): the approved sales order line a plan supplies, as Sales answered for it when the link
/// was made — the order, the line, its customer party and account, its product and the line revision — and who linked
/// it, when. Provenance only: Manufacturing never asks Sales again to rebuild it, and the plan's content does not change.
/// </summary>
public sealed record PlanSalesOrderLink(
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid CustomerPartyId,
    Guid CustomerAccountId,
    Guid ProductId,
    int LineRevision,
    string LinkedBy,
    DateTimeOffset LinkedAtUtc)
{
    /// <summary>The longest linking actor identity the store keeps (the actor id column's length).</summary>
    public const int LinkedByMaximumLength = 128;

    /// <summary>Every identity set, a non-negative line revision and a trimmed, bounded actor without control characters.</summary>
    public bool IsWellFormed() =>
        SalesOrderId != Guid.Empty && SalesOrderLineId != Guid.Empty && CustomerPartyId != Guid.Empty
        && CustomerAccountId != Guid.Empty && ProductId != Guid.Empty && LineRevision >= 0
        && LinkedBy is { Length: > 0 and <= LinkedByMaximumLength } actor && actor.Trim() == actor
        && !actor.Any(char.IsControl);
}
