namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// One immutable shipment line of an approved, non-cancelled dispatch note. Order identity stays on
/// every line because one loading instruction may contain lines from more than one Sales order.
/// This is physical source evidence only: it deliberately carries no price, tax, amount, due date or
/// claim about a remaining invoiceable quantity.
/// </summary>
public sealed record ApprovedDispatchInvoiceSourceLineV1(
    int LineNo,
    Guid SourceOrderId,
    Guid SourceOrderLineId,
    Guid ShipmentId,
    Guid ProductId,
    Guid UnitId,
    string ShippedQuantity);

/// <summary>
/// Company-scoped source facts of one approved and still-active dispatch note. <paramref name="InstructionVersion"/>
/// is the real durable aggregate version; the dispatch note has no separate version. The system number and approval
/// timestamp are the values persisted by the dispatch-note approval transition. An e-Dispatch number remains optional.
/// </summary>
public sealed record ApprovedDispatchInvoiceSourceV1(
    Guid CompanyId,
    Guid DispatchNoteId,
    Guid InstructionId,
    long InstructionVersion,
    Guid CustomerPartyId,
    Guid CompanyAccountId,
    string SystemNumber,
    string? EDispatchNumber,
    DateTimeOffset ApprovedAtUtc,
    IReadOnlyList<ApprovedDispatchInvoiceSourceLineV1> Lines);

/// <summary>
/// Logistics-owned read for a future native invoice producer. It reuses the current loading-instruction read
/// authority and exposes no browser route, permission, price calculation, consumption ledger or write operation.
/// Draft, cancelled, foreign-company and unknown notes are all not found.
/// </summary>
public interface IApprovedDispatchInvoiceSourceV1
{
    Task<ApprovedDispatchInvoiceSourceV1> GetApprovedDispatchInvoiceSourceAsync(
        Guid dispatchNoteId,
        CancellationToken cancellationToken = default);
}
