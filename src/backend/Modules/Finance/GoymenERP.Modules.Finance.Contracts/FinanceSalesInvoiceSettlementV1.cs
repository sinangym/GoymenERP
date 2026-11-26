using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<FinanceSalesInvoiceSettlementStatusV1>))]
public enum FinanceSalesInvoiceSettlementStatusV1
{
    Unknown = 0, Open = 1, Settled = 2,
    /// <summary>The entire original invoice was reversed by the native official-invoice cancellation.
    /// This is not payment, allocation, settlement or permission to close an active invoice.</summary>
    OfficiallyReversed = 3,
}

/// <summary>Price-free owner evidence for the entire invoice, including installments shared by multiple orders.
/// ScopeId is the actual admission batch; SourceVersion is the admitted invoice version, not a settlement version.
/// Unknown never establishes financial closure. OfficiallyReversed identifies the admitted original scope/version,
/// separately from Settled; a consumer must explicitly match its own historical official cancellation.</summary>
public sealed record FinanceSalesInvoiceSettlementV1(
    Guid InvoiceId, FinanceSalesInvoiceSettlementStatusV1 Status, Guid? ScopeId, string? SourceVersion);

public sealed record FinanceSalesInvoiceSettlementBatchV1(
    Guid CompanyId, IReadOnlyList<FinanceSalesInvoiceSettlementV1> Invoices);

/// <summary>In-process Finance owner read. The consumer supplies authoritative finalized Sales invoice IDs and
/// freshly checks its own read/close authority through the required fence. Context must match server company/actor.
/// All admitted installments must be closed by valid recorded allocations/accepted return credits. Bank statement
/// reconciliation is independent. A complete native official reversal is a separate OfficiallyReversed outcome;
/// missing, partial, contradictory or unreadable proof remains Unknown. No amounts are exposed.
/// This observation is not a lock or a durable acceptance token; consumers must re-read at their command boundary.</summary>
public interface IFinanceSalesInvoiceSettlementPortV1
{
    Task<ReadResult<FinanceSalesInvoiceSettlementBatchV1>> ReadAsync(
        FinanceContext context, IReadOnlyList<Guid> invoiceIds,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
