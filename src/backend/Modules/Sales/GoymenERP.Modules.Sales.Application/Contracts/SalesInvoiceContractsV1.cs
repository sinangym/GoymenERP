using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// S5 API-32 (Satış hazırlık planı v1 §3.3) — Fatura çekirdeği 1: the sales invoice draft. SAT-D7 (manual flow, no "Fatura Kes"),
// SAT-D11/D75/D76 (amounts), EK42 SAT-D223…D226 (TL equivalent), SAT-D90/D170/D172 (rate), SAT-D101 (one customer, currency,
// rate and due date per invoice), SAT-D94/AT-055 (a share is invoiced at most once), SAT-D103/D114/D115/D121 (invoicing gate),
// SAT-D124/D129 (instalments), SAT-D135/D147/D186 (tax waits), SAT-D202 (Muhasebe, Muhasebe Müdürü, Genel Müdür, Admin prepare).

/// <summary>The versioned command names the invoice attempt record is keyed by (ADR-011 §2).</summary>
public static class SalesInvoiceCommands
{
    /// <summary>API-32: POST /api/v1/sales/invoices.</summary>
    public const string CreateV1 = "sales.invoice.create.v1";

    public static bool IsInvoiceCommand(string? name) => name is CreateV1;
}

/// <summary>Technical bounds of the invoice routes (not policies).</summary>
public static class SalesInvoiceApiV1
{
    /// <summary>The Logistics owner's read bound per customer (the API-31 bound): shares beyond it are not reachable in one draft.</summary>
    public const int MaximumShares = SalesDispatchApiV1.MaximumCandidateShares;

    public const int MaximumPageSize = 100;

    public const int DefaultPageSize = 50;

    public const string InvoicesPath = "/api/v1/sales/invoices";

    public const int CreatedStatus = 201;
}

/// <summary>One requested share: the shipment of an approved dispatch note line (AT-055: a shipment is on one active note).</summary>
public sealed record SalesInvoiceShareRequest(Guid ShipmentId);

/// <summary>API-32 body: the customer account and the whole shipment shares to invoice together (1–500, each once).</summary>
public sealed record CreateSalesInvoiceRequest(Guid CompanyAccountId, IReadOnlyList<SalesInvoiceShareRequest> Shares);

/// <summary>The strict reader of the create body: unknown members, a null body or a malformed share is a 400 (never repaired).</summary>
public static class SalesInvoiceJson
{
    public static CreateSalesInvoiceRequest ReadCreate(string json)
    {
        var request = JsonSerializer.Deserialize<CreateSalesInvoiceRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        if (request.Shares is null || request.Shares.Any(share => share is null))
        {
            throw new JsonException("Invalid share list.");
        }

        return request;
    }
}

/// <summary>The create's answer to the route: the invoice, whether it was replayed, and the first correlation (replay header).</summary>
public sealed record SalesInvoiceCommandResult(SalesInvoiceDetailView Invoice, bool IsReplay, string OriginalCorrelationId);

/// <summary>The canonical content of an invoice create (the strict wire shape, in the client's order).</summary>
public static class SalesInvoiceRequestHash
{
    public static string Compute(CreateSalesInvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = JsonSerializer.Serialize(new { command = SalesInvoiceCommands.CreateV1, request }, SalesDraftJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// The reasons an invoice draft cannot be prepared now (409 <c>invoice_not_ready</c>, one field error per reason; the screen shows
/// each). None is recorded under the key: every one can change (a share is approved, a revision accepted, a rate published, a
/// tax value entered), so the same key asks again. Tax reasons of the order line's snapshot are passed verbatim beside these.
/// </summary>
public static class SalesInvoiceReasonCodesV1
{
    public const string ShareNotInvoiceable = "share_not_invoiceable";
    public const string ShareAlreadyInvoiced = "share_already_invoiced";
    public const string CustomerMismatch = "customer_mismatch";
    public const string OrderNotApproved = "order_not_approved";
    public const string OrderAwaitingReapproval = "order_awaiting_reapproval";
    public const string OrderAwaitingCustomerAcceptance = "order_awaiting_customer_acceptance";
    public const string OrderLineNotInBasis = "order_line_not_in_basis";
    public const string CurrencyMismatch = "currency_mismatch";
    public const string MoneyProfileMismatch = "money_profile_mismatch";
    public const string RateUnresolved = "rate_unresolved";
    public const string RateMismatch = "rate_mismatch";
    public const string DueTermsMissing = "due_terms_missing";
    public const string DueDatePending = "due_date_pending";
    public const string DueDateMismatch = "due_date_mismatch";
    public const string LineUnpriced = "line_unpriced";
    public const string TaxUnavailable = "tax_unavailable";

    /// <summary>SAT-D186: the buyer's exemption document is dated after the shipment — the Muhasebe Müdürü approval has no source yet.</summary>
    public const string ExemptionDocumentAfterShipment = "exemption_document_after_shipment";

    public const string AmountOutOfRange = "amount_out_of_range";

    /// <summary>SAT-D157: the payable amount cannot be split into the due plan's instalments.</summary>
    public const string DueAmountNotSplittable = "due_amount_not_splittable";
}

/// <summary>The invoice routes' problem codes beside the draft's (<see cref="SalesDraftProblemCodesV1"/>).</summary>
public static class SalesInvoiceProblemCodesV1
{
    /// <summary>409: the shares cannot be invoiced together now; the field errors name every reason (<see cref="SalesInvoiceReasonCodesV1"/>).</summary>
    public const string InvoiceNotReady = "invoice_not_ready";
}

/// <summary>One amount set as exact invariant text at the currency's scale.</summary>
public sealed record SalesInvoiceAmountsView(string NetAmount, string VatAmount, string WithheldVatAmount, string GrossAmount,
    string PayableAmount);

/// <summary>The invoice's rate (SAT-D172: written on the invoice) and the functional currency it converts to.</summary>
public sealed record SalesInvoiceRateView(string Rate, DateOnly RateDay, DateOnly BulletinDate, Guid FunctionalCurrencyId,
    int FunctionalScale);

/// <summary>One VAT rate group with its sums and, for a foreign-currency invoice, its TL totals (SAT-D223).</summary>
public sealed record SalesInvoiceVatGroupView(string VatPercent, int LineCount, SalesInvoiceAmountsView Amounts,
    SalesInvoiceAmountsView? Functional);

/// <summary>One instalment (SAT-D124/D129). Its TL equivalent (SAT-D227, EK43) is computed at finalisation (API-36, PR4), not in the draft; the draft stores the instalments, the invoice TL totals and the rate frozen.</summary>
public sealed record SalesInvoiceInstalmentView(int SequenceNo, DateOnly DueDate, string SharePercent, string Amount);

/// <summary>A line's TL amounts, the amount types that took the group difference and the Kontrol Merkezi flag (EK42).</summary>
public sealed record SalesInvoiceLineFunctionalView(SalesInvoiceAmountsView Amounts, IReadOnlyList<string> Adjusted, bool NeedsReview);

public sealed record SalesInvoiceLineView(
    Guid LineId,
    int LineNo,
    Guid ShipmentId,
    Guid DispatchNoteId,
    string DispatchNoteNumber,
    int NoteLineNo,
    DateOnly ShipmentDate,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    string PriceBasisRevision,
    Guid ProductId,
    Guid UnitId,
    string Quantity,
    string NetUnitPrice,
    string VatPercent,
    string? WithholdingShare,
    SalesInvoiceAmountsView Amounts,
    SalesInvoiceLineFunctionalView? Functional);

/// <summary>API-32 detail (and the create's answer). Amounts are prices: the read also needs <c>sales.orders.price.read</c>.</summary>
public sealed record SalesInvoiceDetailView(
    Guid Id,
    string DocumentNo,
    string State,
    string Version,
    DateOnly BusinessDate,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    int AmountScale,
    SalesInvoiceRateView? ExchangeRate,
    SalesInvoiceAmountsView Totals,
    SalesInvoiceAmountsView? FunctionalTotals,
    IReadOnlyList<SalesInvoiceVatGroupView> VatGroups,
    IReadOnlyList<SalesInvoiceInstalmentView> Instalments,
    IReadOnlyList<SalesInvoiceLineView> Lines,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    bool IsReplay = false);

public sealed record SalesInvoiceSummaryView(
    Guid Id,
    string DocumentNo,
    string State,
    string Version,
    DateOnly BusinessDate,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    string PayableAmount,
    string? FunctionalPayableAmount,
    int LineCount,
    DateTimeOffset CreatedAtUtc);

public sealed record SalesInvoiceListResponse(IReadOnlyList<SalesInvoiceSummaryView> Items, string? NextCursor);

public sealed record SalesInvoiceListQuery(int PageSize = SalesInvoiceApiV1.DefaultPageSize, string? Cursor = null);

/// <summary>What the store did for an invoice create: the invoice, recorded now or replayed, and the first correlation.</summary>
public sealed record SalesInvoiceStoreResult(SalesInvoice Invoice, SalesDraftWriteOutcome Outcome, string OriginalCorrelationId);

public sealed record SalesInvoicePage(IReadOnlyList<SalesInvoice> Items, string? NextCursor);

/// <summary>
/// The invoice half of the Sales store (the request/quote attempt rules, ADR-011 §2–§8, in the invoice's own attempt table): one
/// save writes the invoice, its lines, the attempt and the audit event; the unique active-share index refuses a second active
/// line of the same shipment (AT-055, SAT-D94) as the save's concurrent change. Another company's invoice is invisible.
/// </summary>
public interface ISalesInvoiceStore
{
    Task<SalesInvoiceStoreResult> StoreInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<string, SalesInvoice> build, CancellationToken cancellationToken = default);

    /// <summary>The stored answer of a key (same content and actor), null when the key is new; another content or actor is a 409.</summary>
    Task<SalesInvoiceStoreResult?> FindInvoiceReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        CancellationToken cancellationToken = default);

    Task<SalesInvoice?> FindInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    Task<SalesInvoicePage> ListInvoicesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>The shipments among <paramref name="shipmentIds"/> already on an active invoice line of this company.</summary>
    Task<IReadOnlySet<Guid>> FindInvoicedShipmentsAsync(IReadOnlyCollection<Guid> shipmentIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// API-32: prepare a sales invoice draft from approved shipment shares and read it back. Company, actor and authority are the
/// server's and measured afresh before anything is read, after every awaited owner read and right before the save. The create
/// needs <c>sales.invoices.write</c> AND <c>sales.orders.price.read</c> (the answer is prices); list and detail need
/// <c>sales.invoices.read</c> AND <c>sales.orders.price.read</c>.
/// </summary>
public interface ISalesInvoiceService
{
    Task<SalesInvoiceCommandResult> CreateInvoiceAsync(CreateSalesInvoiceRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<SalesInvoiceListResponse> GetInvoicesAsync(SalesInvoiceListQuery query, CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailView> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);
}
