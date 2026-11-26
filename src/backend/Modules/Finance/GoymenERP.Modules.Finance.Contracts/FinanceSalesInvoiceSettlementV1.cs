using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<FinanceSalesInvoiceSettlementStatusV1>))]
public enum FinanceSalesInvoiceSettlementStatusV1 { Unknown = 0, Open = 1, Settled = 2 }

/// <summary>Price-free owner evidence for the entire invoice, including installments shared by multiple orders.
/// ScopeId is the actual admission batch; SourceVersion is the admitted invoice version, not a settlement version.
/// Unknown never establishes financial closure.</summary>
public sealed record FinanceSalesInvoiceSettlementV1(
    Guid InvoiceId, FinanceSalesInvoiceSettlementStatusV1 Status, Guid? ScopeId, string? SourceVersion);

public sealed record FinanceSalesInvoiceSettlementBatchV1(
    Guid CompanyId, IReadOnlyList<FinanceSalesInvoiceSettlementV1> Invoices);

/// <summary>In-process Finance owner read. The consumer supplies authoritative finalized Sales invoice IDs and
/// freshly checks its own read/close authority through the required fence. Context must match server company/actor.
/// All admitted installments must be closed by valid recorded allocations/accepted return credits. Bank statement
/// reconciliation is independent. Missing, cancelled, changed or unreadable proof fails closed; no amounts are exposed.
/// This observation is not a lock or a durable acceptance token; consumers must re-read at their command boundary.</summary>
public interface IFinanceSalesInvoiceSettlementPortV1
{
    Task<ReadResult<FinanceSalesInvoiceSettlementBatchV1>> ReadAsync(
        FinanceContext context, IReadOnlyList<Guid> invoiceIds,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
