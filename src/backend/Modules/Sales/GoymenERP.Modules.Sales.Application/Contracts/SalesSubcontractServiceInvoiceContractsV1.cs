using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// F2 (SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1 §F2) — the subcontract service invoice draft: #66 m.4, URT-D16, SAT-D195
// (kg price), SAT-D309/LOJ-D9 (the fee kilograms are the delivery weighing's net kg; scrap is not billed), SAT-D312/LOJ-D12 (the
// share follows the declared kilograms; no weighing, no invoice; a correction after invoicing is a difference invoice), LOJ-D4
// (a service line only). The Logistics owner answers the net kilograms through a host adapter
// (ISalesSubcontractDeliveryWeighingReadPortV1); the amounts are SalesInvoiceCalculator's only.

/// <summary>The versioned command name the subcontract service invoice attempt record is keyed by (ADR-011 §2).</summary>
public static class SalesSubcontractServiceInvoiceCommands
{
    /// <summary>F2: POST /api/v1/sales/subcontract-service-invoices.</summary>
    public const string CreateV1 = "sales.subcontract-service-invoice.create.v1";
}

/// <summary>The routes and technical bounds of the subcontract service invoice (not policies).</summary>
public static class SalesSubcontractServiceInvoiceApiV1
{
    public const string InvoicesPath = "/api/v1/sales/subcontract-service-invoices";

    public const int CreatedStatus = 201;
}

/// <summary>
/// The create body: the subcontract service order and the delivery weighing of its loading instruction (the F2 "separate key").
/// The server reads the net kilograms and the price basis itself; the client sends no quantity, price or amount.
/// </summary>
public sealed record CreateSalesSubcontractServiceInvoiceRequest(Guid SalesOrderId, Guid LoadingInstructionId, Guid WeighingId);

/// <summary>The strict reader of the create body (shape only; the service decides the rules).</summary>
public static class SalesSubcontractServiceInvoiceJson
{
    public static CreateSalesSubcontractServiceInvoiceRequest ReadCreate(string json) =>
        JsonSerializer.Deserialize<CreateSalesSubcontractServiceInvoiceRequest>(json, SalesDraftJson.Options)
        ?? throw new JsonException("Request body is required.");
}

/// <summary>The canonical content of a subcontract service invoice create (the strict wire shape, in the client's order).</summary>
public static class SalesSubcontractServiceInvoiceRequestHash
{
    public static string Compute(CreateSalesSubcontractServiceInvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = JsonSerializer.Serialize(new { command = SalesSubcontractServiceInvoiceCommands.CreateV1, request },
            SalesDraftJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// The reasons a subcontract service invoice cannot be prepared now (409 <c>invoice_not_ready</c>, one field error per reason),
/// beside the order and tax reasons of <see cref="SalesInvoiceReasonCodesV1"/>. None is recorded under the key: each can change
/// (a weighing is recorded, a revision approved, a policy decided), so the same key asks again.
/// </summary>
public static class SalesSubcontractServiceInvoiceReasonCodesV1
{
    /// <summary>The order is not a subcontract service order (SAT-D175): its invoice is the dispatch-share invoice (API-32).</summary>
    public const string NotSubcontractOrder = "not_subcontract_order";

    /// <summary>LOJ-D12: no delivery weighing for the key (not recorded, another weighing, a cancelled instruction) — no invoice.</summary>
    public const string WeighingMissing = "weighing_missing";

    /// <summary>The weighed instruction serves no line of this order.</summary>
    public const string WeighingOrderMismatch = "weighing_order_mismatch";

    /// <summary>
    /// Retired by SAT-D376/D402 (the shared weighing is now split by theoretical kg); kept as a published V1 value so an older
    /// client still decodes it. The service no longer answers it.
    /// </summary>
    public const string SharePolicyPending = "subcontract_share_policy_pending";

    /// <summary>SAT-D376 (fail closed): a line of the shared weighing has no readable theoretical kg; the net kg is not split.</summary>
    public const string TheoreticalKgUnavailable = "subcontract_theoretical_kg_unavailable";

    /// <summary>SAT-D376/D402: a served line's whole-kg share would be zero or negative; it is never billed as zero.</summary>
    public const string ShareNotPositive = "subcontract_share_not_positive";

    /// <summary>A foreign-currency subcontract order: its rate day is not decided for the service invoice (NEEDS-SINAN).</summary>
    public const string CurrencyPending = "subcontract_currency_pending";

    /// <summary>
    /// The served order line is not in kilograms: the weighing's net kg is billed only on a kg line (SAT-D195 kg price); no unit
    /// conversion is assumed for a service fee.
    /// </summary>
    public const string LineNotKilogram = "subcontract_line_not_kilogram";

    /// <summary>The delivery weighing is already invoiced for this order (the F2 key is unique; a correction is a difference invoice).</summary>
    public const string WeighingAlreadyInvoiced = "subcontract_weighing_already_invoiced";
}

/// <summary>One service line of a subcontract service invoice: the order line, the billed net kg and the frozen amounts.</summary>
public sealed record SalesSubcontractServiceInvoiceLineView(
    Guid LineId,
    int LineNo,
    Guid SalesOrderLineId,
    string PriceBasisRevision,
    Guid ProductId,
    Guid UnitId,
    string Quantity,
    string NetUnitPrice,
    string VatPercent,
    string? WithholdingShare,
    SalesInvoiceAmountsView Amounts);

/// <summary>The subcontract service invoice draft as the screen shows it (amounts are the calculator's, frozen at creation).</summary>
public sealed record SalesSubcontractServiceInvoiceDetailView(
    Guid Id,
    string DocumentNo,
    string State,
    string Version,
    DateOnly BusinessDate,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    int AmountScale,
    Guid SalesOrderId,
    Guid LoadingInstructionId,
    Guid WeighingId,
    int WeighingRevision,
    string NetKg,
    SalesInvoiceAmountsView Totals,
    IReadOnlyList<SalesSubcontractServiceInvoiceLineView> Lines,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    bool IsReplay);

/// <summary>The command answer: the stored draft, whether it is a replay, and the first request's correlation id.</summary>
public sealed record SalesSubcontractServiceInvoiceCommandResult(SalesSubcontractServiceInvoiceDetailView Invoice, bool IsReplay,
    string OriginalCorrelationId);

/// <summary>The stored subcontract service invoice and how the store answered (recorded now or replayed).</summary>
public sealed record SalesSubcontractServiceInvoiceStoreResult(SalesSubcontractServiceInvoice Invoice, SalesDraftWriteOutcome Outcome,
    string OriginalCorrelationId);

/// <summary>The subcontract service invoice use cases (F2).</summary>
public interface ISalesSubcontractServiceInvoiceService
{
    Task<SalesSubcontractServiceInvoiceCommandResult> CreateSubcontractServiceInvoiceAsync(
        CreateSalesSubcontractServiceInvoiceRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesSubcontractServiceInvoiceDetailView> GetSubcontractServiceInvoiceAsync(Guid invoiceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The Sales store of subcontract service invoices: one save writes the invoice, its lines, the attempt and the audit event; the
/// unique key (company, loading instruction, weighing, order) refuses a second invoice of the same weighing for the same order
/// (a lost race surfaces as <see cref="SalesDraftStoreCodes.ConcurrentChange"/>).
/// </summary>
public interface ISalesSubcontractServiceInvoiceStore
{
    Task<SalesSubcontractServiceInvoiceStoreResult> StoreSubcontractServiceInvoiceAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, Func<string, SalesSubcontractServiceInvoice> build, CancellationToken cancellationToken = default);

    Task<SalesSubcontractServiceInvoiceStoreResult?> FindSubcontractServiceInvoiceReplayAsync(SalesDraftWriteEnvelope envelope,
        string requestHash, CancellationToken cancellationToken = default);

    Task<SalesSubcontractServiceInvoice?> FindSubcontractServiceInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>The invoice of the F2 key in the current company, if any (null otherwise).</summary>
    Task<SalesSubcontractServiceInvoice?> FindSubcontractServiceInvoiceByWeighingAsync(Guid salesOrderId, Guid loadingInstructionId,
        Guid weighingId, CancellationToken cancellationToken = default);
}

/// <summary>The fail-closed status of a delivery weighing read (anything but <see cref="Available"/> is "no net kilograms").</summary>
public enum SalesSubcontractDeliveryWeighingStatusV1
{
    Available = 1,

    /// <summary>No weighing for the key (LOJ-D12: no invoice); never zero.</summary>
    NoSource = 2,

    /// <summary>The owner cannot answer consistently now.</summary>
    Unavailable = 3,
}

/// <summary>
/// One instruction line the weighed delivery served: the Sales order line it is bound to, and the line's theoretical kilograms
/// (the owner's loaded kg of the line at weighing, exact invariant decimal text; SAT-D376 split weight). A null or unreadable
/// <paramref name="TheoreticalKg"/> stops a shared-weighing split (fail closed), never a zero.
/// </summary>
public sealed record SalesSubcontractDeliveryWeighingLineV1(Guid SourceOrderId, Guid SourceOrderLineId, Guid ProductId, Guid UnitId,
    string? TheoreticalKg = null);

/// <summary>
/// The delivery weighing facts Sales needs (Sales-owned shape; the host adapts the Logistics owner's F1 read): the net kilograms
/// as an exact invariant decimal text, the weighing revision and instruction version, and the instruction's lines.
/// </summary>
public sealed record SalesSubcontractDeliveryWeighingV1(
    string NetKg,
    int WeighingRevision,
    long InstructionVersion,
    IReadOnlyList<SalesSubcontractDeliveryWeighingLineV1> Lines);

/// <summary>One answer of <see cref="ISalesSubcontractDeliveryWeighingReadPortV1"/>; <see cref="Value"/> only when available.</summary>
public sealed record SalesSubcontractDeliveryWeighingReadV1(
    SalesSubcontractDeliveryWeighingStatusV1 Status,
    Guid CompanyId,
    Guid LoadingInstructionId,
    Guid WeighingId,
    SalesSubcontractDeliveryWeighingV1? Value);

/// <summary>
/// The Sales consumer seam of the delivery weighing (F1 → F2): read-only, company-scoped by the server-resolved company, carries
/// no authority (the Sales service fences around it), creates nothing, and answers <c>Available</c>, <c>NoSource</c> or
/// <c>Unavailable</c> — never a silent zero.
/// </summary>
public interface ISalesSubcontractDeliveryWeighingReadPortV1
{
    Task<SalesSubcontractDeliveryWeighingReadV1> ReadAsync(Guid companyId, Guid loadingInstructionId, Guid weighingId,
        CancellationToken cancellationToken = default);
}
