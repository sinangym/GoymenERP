using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Sales.Domain;
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

    /// <summary>API-33: POST /api/v1/sales/invoices/{id}/submission (preparer sends the draft to approval).</summary>
    public const string SubmitV1 = "sales.invoice.submit.v1";

    /// <summary>API-33: POST /api/v1/sales/invoices/{id}/approval (SAT-D88/D204: the approver is not the preparer).</summary>
    public const string ApproveV1 = "sales.invoice.approve.v1";

    /// <summary>API-33: POST /api/v1/sales/invoices/{id}/rejection (a reason; the shares are released).</summary>
    public const string RejectV1 = "sales.invoice.reject.v1";

    /// <summary>API-33: POST /api/v1/sales/invoices/{id}/cancellation (a reason; the shares are released).</summary>
    public const string CancelV1 = "sales.invoice.cancel.v1";

    /// <summary>API-35: POST /api/v1/sales/invoices/{id}/external-match (the approved invoice against the official document, SAT-D89).</summary>
    public const string MatchV1 = "sales.invoice.match.v1";

    /// <summary>API-36: POST /api/v1/sales/invoices/{id}/finalization (the matched invoice is finalised; Finance is then offered the open items, SAT-D208).</summary>
    public const string FinalizeV1 = "sales.invoice.finalize.v1";

    /// <summary>API-36: POST /api/v1/sales/invoices/{id}/finance-admission (re-drives the Finance hand-off of a finalised invoice that still waits, SAT-D208).</summary>
    public const string ResendFinanceV1 = "sales.invoice.finance-resend.v1";

    public static bool IsInvoiceCommand(string? name) =>
        name is CreateV1 or SubmitV1 or ApproveV1 or RejectV1 or CancelV1 or MatchV1 or FinalizeV1 or ResendFinanceV1;
}

/// <summary>Technical bounds of the invoice routes (not policies).</summary>
public static class SalesInvoiceApiV1
{
    /// <summary>The Logistics owner's read bound per customer (the API-31 bound): shares beyond it are not reachable in one draft.</summary>
    public const int MaximumShares = SalesDispatchApiV1.MaximumCandidateShares;

    public const int MaximumPageSize = 100;

    public const int DefaultPageSize = 50;

    /// <summary>API-09: the most invoices the order's "Faturalar" tab answers in one read (newest first; <c>hasMore</c> says more exist).</summary>
    public const int MaximumDraftInvoices = 100;

    public const string DraftInvoicesPath = "/api/v1/sales/drafts/{id}/invoices";

    public const string InvoicesPath = "/api/v1/sales/invoices";

    public const int CreatedStatus = 201;

    public const string SubmissionPath = "/api/v1/sales/invoices/{id}/submission";
    public const string ApprovalPath = "/api/v1/sales/invoices/{id}/approval";
    public const string RejectionPath = "/api/v1/sales/invoices/{id}/rejection";
    public const string CancellationPath = "/api/v1/sales/invoices/{id}/cancellation";
    public const string ExternalMatchPath = "/api/v1/sales/invoices/{id}/external-match";
    public const string FinalizationPath = "/api/v1/sales/invoices/{id}/finalization";
    public const string FinanceAdmissionPath = "/api/v1/sales/invoices/{id}/finance-admission";

    /// <summary>The four state commands answer the invoice after the command with 200 (the same key again too, with the replay headers).</summary>
    public const int CommandStatus = 200;

    /// <summary>
    /// API-34 (SAT-D236/D244/D248): the bulk export of the finalised invoices of a date range as ONE file for Accounting. A read
    /// (GET): it writes nothing, opens no official connection (DEC-INT-001, DEC-FIN-003) and stores no file.
    /// </summary>
    public const string ExportPath = "/api/v1/sales/invoices/export";

    /// <summary>API-34: the Excel workbook format (SAT-D236 (1)); the only format served now.</summary>
    public const string ExportFormatXlsx = "xlsx";

    public const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>API-34 technical bound (not a policy): the most invoices one export answers; more is 422, nothing is cut silently.</summary>
    public const int MaximumExportInvoices = 500;

    /// <summary>API-34 technical bound (not a policy): the longest date range of one export, both days included.</summary>
    public const int MaximumExportRangeDays = 366;
}

/// <summary>
/// API-33 cancel body. <paramref name="AcknowledgePaymentAllocationLift"/> is the explicit confirmation of Sinan S2 (SAT-D96): a
/// cancel that lifts payment allocations needs it. Finance owns the allocation; until its port exists no allocation is known to
/// Sales, so the field is accepted, hashed and has no further effect (next slice).
/// </summary>
public sealed record CancelSalesInvoiceRequest(string ExpectedVersion, string? Reason = null, bool AcknowledgePaymentAllocationLift = false);

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

/// <summary>The strict readers of the state-command bodies (shape only; the domain decides the rules).</summary>
public static class SalesInvoiceStateJson
{
    /// <summary>The match body: a version, the official number (1–64), the ETTN (canonical lowercase-or-uppercase UUID text), and the optional approval.</summary>
    public static MatchSalesInvoiceRequest ReadMatch(string json)
    {
        var request = JsonSerializer.Deserialize<MatchSalesInvoiceRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersion(request.ExpectedVersion);
        if (string.IsNullOrWhiteSpace(request.OfficialInvoiceNo) || request.OfficialInvoiceNo.Length > SalesInvoice.MaximumOfficialNoLength
            || request.OfficialInvoiceNo != request.OfficialInvoiceNo.Trim())
        {
            throw new JsonException("Invalid official invoice number.");
        }

        if (request.Ettn is null || request.Ettn.Length != SalesInvoice.EttnLength
            || !Guid.TryParseExact(request.Ettn, "D", out var ettn) || ettn == Guid.Empty)
        {
            throw new JsonException("Invalid ETTN.");
        }

        if (request.DifferenceReason is { Length: > SalesInvoice.MaximumReasonLength })
        {
            throw new JsonException("Invalid difference reason.");
        }

        return request;
    }

    private static void RequireVersion(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 19 || !System.Text.RegularExpressions.Regex.IsMatch(text, "\\A[1-9][0-9]*\\z"))
        {
            throw new JsonException("Invalid expected version.");
        }
    }

    /// <summary>The submission and the approval carry the expected version only (a reason is an unknown member).</summary>
    public static SalesDraftStateCommandRequest ReadVersionOnly(string json)
    {
        var request = SalesDraftJson.ReadStateCommand(json, allowReason: false);
        return request;
    }

    /// <summary>The rejection takes the expected version and a reason.</summary>
    public static SalesDraftStateCommandRequest ReadWithReason(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: true);

    public static CancelSalesInvoiceRequest ReadCancel(string json)
    {
        var request = JsonSerializer.Deserialize<CancelSalesInvoiceRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        RequireVersion(request.ExpectedVersion);
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

/// <summary>The canonical content of an official match for the replay identity (ADR-011 §3); the ETTN is compared case-insensitively.</summary>
public static class SalesInvoiceMatchHash
{
    public static string Compute(Guid invoiceId, MatchSalesInvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = JsonSerializer.Serialize(new
        {
            command = SalesInvoiceCommands.MatchV1,
            invoiceId,
            request.ExpectedVersion,
            request.OfficialInvoiceNo,
            ettn = request.Ettn.ToLowerInvariant(),
            request.ApproveDifference,
            request.DifferenceReason,
        }, SalesDraftJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>The canonical content of an invoice state command for the replay identity (ADR-011 §3).</summary>
public static class SalesInvoiceStateHash
{
    public static string Compute(string commandName, Guid invoiceId, SalesDraftStateCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = commandName, invoiceId, request.ExpectedVersion, request.Reason },
            SalesDraftJson.Options));
    }

    public static string Compute(Guid invoiceId, CancelSalesInvoiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = SalesInvoiceCommands.CancelV1,
            invoiceId,
            request.ExpectedVersion,
            request.Reason,
            request.AcknowledgePaymentAllocationLift,
        }, SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

/// <summary>What an invoice state command's key already holds: the stored invoice or the recorded terminal refusal, never both.</summary>
public sealed record SalesInvoiceReplay(SalesInvoice? Invoice, SalesDraftTerminalRefusal? Refusal, string OriginalCorrelationId);

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

    /// <summary>409 (recorded under the key): the approver is the preparer or the submitter (SAT-D88/D204).</summary>
    public const string SelfApproval = SalesRuleCodes.InvoiceSelfApproval;

    /// <summary>409 (not recorded; the official data can change): the official document is not found by number and ETTN.</summary>
    public const string OfficialDocumentNotFound = "official_document_not_found";

    /// <summary>409 (not recorded): the official document differs in customer or currency (field errors name each).</summary>
    public const string OfficialMatchMismatch = "official_match_mismatch";

    /// <summary>409 (not recorded): the TL difference exceeds the tolerance and no approval was sent (SAT-D106).</summary>
    public const string DifferenceApprovalRequired = "difference_approval_required";

    /// <summary>409 (recorded): the official number or ETTN is already matched to an invoice of this company.</summary>
    public const string OfficialDocumentAlreadyMatched = "official_document_already_matched";

    /// <summary>503: the official-invoice provider cannot answer; nothing is matched (fail-closed).</summary>
    public const string OfficialInvoiceProviderUnavailable = "official_invoice_provider_unavailable";

    /// <summary>409 (not recorded; the match can still be made): the invoice is not matched with the official document yet (SAT-D89).</summary>
    public const string MatchRequired = SalesRuleCodes.InvoiceMatchRequired;

    /// <summary>409 (recorded): the recorded match carries a difference; finalisation stays blocked until it is resolved (SAT-D229).</summary>
    public const string MatchDifferenceOpen = SalesRuleCodes.InvoiceMatchDifferenceOpen;

    /// <summary>422: the foreign-currency instalments cannot be converted to TL without a negative or zero last remainder (SAT-D227, SAT-D233).</summary>
    public const string InstalmentFunctionalInvalid = SalesRuleCodes.InvoiceInstalmentFunctionalInvalid;

    /// <summary>422 (API-34): more finalised invoices in the range than one export answers; nothing is cut, the range is narrowed.</summary>
    public const string ExportTooLarge = "sales_invoice_export_too_large";

    public const string DifferenceReasonRequired = SalesRuleCodes.InvoiceMatchDifferenceReasonRequired;
    public const string DifferenceNotApplicable = SalesRuleCodes.InvoiceMatchDifferenceNotApplicable;
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
    SalesInvoiceDecisionView? Decision = null,
    string? PaymentAllocationWarning = null,
    bool IsReplay = false,
    SalesInvoiceMatchView? Match = null,
    SalesInvoiceFinalizationView? Finalization = null);

/// <summary>The Finance axis of a finalised invoice (SAT-D208): Sales commits first and never calls a waiting invoice admitted.</summary>
public static class SalesInvoiceFinanceStatusesV1
{
    public const string Waiting = "waiting";
    public const string Admitted = "admitted";
}

/// <summary>The Accounting axis (B-04b) has no source yet: Sales shows the fixed "no source" value, never an invented status.</summary>
public static class SalesInvoiceAccountingStatusesV1
{
    public const string NoSource = "no_source";
}

/// <summary>One finalised instalment as offered to Finance: its ids are fixed at finalisation; the TL amount exists for a foreign-currency invoice only (SAT-D227).</summary>
public sealed record SalesInvoiceFinalInstalmentView(int SequenceNo, DateOnly DueDate, string Amount, string? FunctionalAmount,
    Guid OpenItemId, Guid InstallmentId);

/// <summary>API-36 facts: who finalised, the Finance hand-off ledger and the plan offered. <see cref="AccountingStatus"/> is the B-04b axis (no source yet).</summary>
public sealed record SalesInvoiceFinalizationView(Guid AdmissionId, string FinalizedVersion, string FinalizedBy, DateTimeOffset FinalizedAtUtc,
    string FinanceStatus, DateTimeOffset? AdmittedAtUtc, int AttemptCount, DateTimeOffset? LastAttemptAtUtc, string? LastOutcome,
    string AccountingStatus, IReadOnlyList<SalesInvoiceFinalInstalmentView> Instalments);

/// <summary>API-33 facts: who sent it to approval, who decided (and why), who cancelled (and why). Absent members are null.</summary>
public sealed record SalesInvoiceDecisionView(string? SubmittedBy, DateTimeOffset? SubmittedAtUtc, string? DecidedBy,
    DateTimeOffset? DecidedAtUtc, string? DecisionReason, string? CancelledBy, DateTimeOffset? CancelledAtUtc, string? CancellationReason);

/// <summary>
/// API-32 list row (S-09). SAT-D238 (EK48): the list needs <c>sales.invoices.read</c> only; <c>sales.orders.price.read</c> is
/// ASKED, never required. <see cref="AmountDisclosure"/> is never absent: <c>Visible</c> → <see cref="PayableAmount"/> is the
/// exact text and <see cref="FunctionalPayableAmount"/> the TL text of a foreign-currency invoice (null for a TL invoice);
/// <c>Withheld</c> → both amounts are null BECAUSE the caller may not see them — a withheld amount is never zero and never empty.
/// </summary>
public sealed record SalesInvoiceSummaryView(
    Guid Id,
    string DocumentNo,
    string State,
    string Version,
    DateOnly BusinessDate,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    SalesPriceDisclosure AmountDisclosure,
    string? PayableAmount,
    string? FunctionalPayableAmount,
    int LineCount,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// API-09 (Sipariş 360 "Faturalar" sekmesi): one invoice that carries at least one line of the order. No amount is shown (the tab
/// is under <c>sales.drafts.read</c> only; the invoice's prices stay behind <c>sales.invoices.read</c> and
/// <c>sales.orders.price.read</c> on API-32). <c>OrderLineCount</c> counts this order's lines, <c>InvoiceLineCount</c> all of them.
/// <c>HoldsShares</c> is false once a rejection or cancellation released the shares.
/// </summary>
public sealed record SalesDraftInvoiceItemView(Guid Id, string DocumentNo, string State, DateOnly BusinessDate, int OrderLineCount,
    int InvoiceLineCount, bool HoldsShares, DateTimeOffset CreatedAtUtc);

public sealed record SalesDraftInvoicesResponse(IReadOnlyList<SalesDraftInvoiceItemView> Items, bool HasMore);

public sealed record SalesOrderInvoicePage(IReadOnlyList<SalesInvoice> Items, bool HasMore);

public sealed record SalesInvoiceListResponse(IReadOnlyList<SalesInvoiceSummaryView> Items, string? NextCursor);

public sealed record SalesInvoiceListQuery(int PageSize = SalesInvoiceApiV1.DefaultPageSize, string? Cursor = null);

/// <summary>
/// API-34 query: the format and the Europe/Istanbul business days of finalisation, both included (SAT-D244: date range, finalised
/// invoices only, one bulk file). Sales holds no official invoice date, so the day of finalisation is the range basis.
/// </summary>
public sealed record SalesInvoiceExportQuery(string? Format, DateOnly From, DateOnly To);

/// <summary>
/// API-34 (SAT-D248): one invoice line of the export — the shipment share, the order it came from, the price basis line's discount
/// and surcharge as entered (percent OR amount), and the frozen amounts as exact invariant text at the currency's scale.
/// </summary>
public sealed record SalesInvoiceExportLineV1(int LineNo, string DispatchNoteNumber, int NoteLineNo, DateOnly ShipmentDate,
    Guid SalesOrderId, string SalesOrderNo, Guid ProductId, Guid UnitId, string Quantity, string NetUnitPrice, string? DiscountPercent,
    string? DiscountAmount, string? SurchargePercent, string? SurchargeAmount, string VatPercent, string? WithholdingShare,
    SalesInvoiceAmountsView Amounts, SalesInvoiceAmountsView? FunctionalAmounts);

/// <summary>
/// API-34 (SAT-D236/D244/D248): one finalised invoice of the export — header, the official match, the day of finalisation, the
/// totals, the due plan offered to Finance (vade), every order number the invoice is tied to (SAT-D246) and its lines.
/// <see cref="OfficialVatFunctionalAmount"/> and <see cref="OfficialWithholdingFunctionalAmount"/> are the official document's VAT
/// and withholding TL equivalents as the API-35 match recorded them (SAT-D241), shown beside the invoice's own computed figures
/// (SAT-D279 (EK55)). The official document states them per invoice only, so they exist per invoice,
/// never per line; null when no SAT-D241 comparison is recorded — never filled with the computed value.
/// </summary>
public sealed record SalesInvoiceExportInvoiceV1(Guid Id, string DocumentNo, string? OfficialNo, string? Ettn, DateOnly FinalizedOn,
    DateOnly BusinessDate, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, int AmountScale, SalesInvoiceRateView? Rate,
    SalesInvoiceAmountsView Totals, SalesInvoiceAmountsView? FunctionalTotals, string? OfficialVatFunctionalAmount,
    string? OfficialWithholdingFunctionalAmount, IReadOnlyList<SalesInvoiceFinalInstalmentView> Instalments,
    IReadOnlyList<string> SalesOrderNos, IReadOnlyList<SalesInvoiceExportLineV1> Lines);

/// <summary>
/// API-34 answer of the service: the facts of the export, laid out into the file by the host composer (the PDF precedent: the
/// service reads and formats amounts with the one calculator's values; the host only lays them out with its resource texts).
/// </summary>
public sealed record SalesInvoiceExportV1(string Format, DateOnly From, DateOnly To, IReadOnlyList<SalesInvoiceExportInvoiceV1> Invoices);

/// <summary>API-34 store answer: the finalised invoices of the range (oldest finalisation first) and whether more exist.</summary>
public sealed record SalesInvoiceExportBatch(IReadOnlyList<SalesInvoice> Items, bool HasMore);

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

    /// <summary>The stored answer or recorded refusal of a state command key, null when the key is new; another content or actor is a 409.</summary>
    Task<SalesInvoiceReplay?> FindInvoiceStateReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    /// <summary>ADR-011 §7: records a terminal refusal of an invoice state command under its key.</summary>
    Task RecordInvoiceRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid invoiceId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);

    /// <summary>
    /// One state command on the company's stored invoice in one save (the invoice, its released shares, the attempt, the audit).
    /// <paramref name="mutate"/> is the domain's decision; a refusal writes nothing. Null when the invoice does not exist here.
    /// </summary>
    Task<SalesInvoiceStoreResult?> DecideInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid invoiceId, Action<SalesInvoice, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// API-35: records the official match of the company's approved invoice in one save (the match row, the attempt, the audit).
    /// <paramref name="record"/> is the domain's decision; a refusal writes nothing. Null when the invoice does not exist here.
    /// </summary>
    Task<SalesInvoiceStoreResult?> MatchInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid invoiceId,
        Func<SalesInvoice, DateTimeOffset, SalesInvoiceMatchFacts> record, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// API-36: finalises the company's matched invoice in one save (state and version, the finalisation row with its Waiting ledger, the
    /// outbox event, the attempt, the audit). <paramref name="finalize"/> is the domain's decision; a refusal writes nothing.
    /// </summary>
    Task<SalesInvoiceStoreResult?> FinalizeInvoiceAsync(SalesDraftWriteEnvelope envelope, string requestHash, Guid invoiceId,
        Action<SalesInvoice, DateTimeOffset> finalize, SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// API-36: records one Finance hand-off attempt of a finalised invoice in the ledger (no version step) in one save with its audit.
    /// With a <paramref name="commandName"/> (<see cref="SalesInvoiceCommands.ResendFinanceV1"/>) the key's attempt row is written too and an
    /// existing one replays; without it (the hand-off right after finalisation) the save has no attempt row.
    /// </summary>
    Task<SalesInvoiceStoreResult?> RecordFinanceAttemptAsync(SalesDraftWriteEnvelope envelope, string? requestHash, string? commandName,
        Guid invoiceId, Action<SalesInvoice, DateTimeOffset> record, SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    /// <summary>The invoice of this company already matched to the official number or the ETTN, null when neither is used (SAT-D89).</summary>
    Task<Guid?> FindMatchedInvoiceIdAsync(string officialNo, string ettn, CancellationToken cancellationToken = default);

    Task<SalesInvoice?> FindInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    Task<SalesInvoicePage> ListInvoicesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>
    /// API-09: the company's invoices with at least one line of <paramref name="salesOrderId"/> (released lines count: the invoice
    /// stays in the history), newest first, at most <paramref name="limit"/> and whether more exist.
    /// </summary>
    Task<SalesOrderInvoicePage> ListInvoicesOfOrderAsync(Guid salesOrderId, int limit, CancellationToken cancellationToken = default);

    /// <summary>The shipments among <paramref name="shipmentIds"/> already on an active invoice line of this company.</summary>
    Task<IReadOnlySet<Guid>> FindInvoicedShipmentsAsync(IReadOnlyCollection<Guid> shipmentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// API-34: this company's FINALISED invoices whose finalisation instant is in [<paramref name="fromUtc"/>,
    /// <paramref name="toUtcExclusive"/>), oldest finalisation first (then id), at most <paramref name="limit"/>, with lines, match
    /// and finalisation, and whether more exist.
    /// </summary>
    Task<SalesInvoiceExportBatch> ListFinalizedInvoicesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtcExclusive, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>API-34: the document numbers of this company's sales orders among <paramref name="orderIds"/> (another company's are absent).</summary>
    Task<IReadOnlyDictionary<Guid, string>> FindOrderDocumentNumbersAsync(IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken = default);
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

    /// <summary>API-33: sends the draft to approval. Needs <c>sales.invoices.write</c> and price read.</summary>
    Task<SalesInvoiceCommandResult> SubmitInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>API-33: approves (needs <c>sales.invoices.approve</c> and price read; the approver is not the preparer, SAT-D88/D204).</summary>
    Task<SalesInvoiceCommandResult> ApproveInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>API-33: rejects with a reason (needs <c>sales.invoices.approve</c> and price read); the shares are released.</summary>
    Task<SalesInvoiceCommandResult> RejectInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>API-33: cancels with a reason (needs <c>sales.invoices.write</c> and price read); the shares are released (SAT-D96 S2 payment effects: next slice).</summary>
    Task<SalesInvoiceCommandResult> CancelInvoiceAsync(Guid invoiceId, CancelSalesInvoiceRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// API-35 (SAT-D89/D106/D229): matches the approved invoice with the official document (number, ETTN, customer, currency exact;
    /// the TL difference within the configured tolerance, or approved with a reason). Needs <c>sales.invoices.match</c> and price
    /// read; approving an excess also needs <c>sales.invoices.approve</c> and is never the preparer's. Fails closed (503) while the
    /// official-invoice provider cannot answer.
    /// </summary>
    Task<SalesInvoiceCommandResult> MatchInvoiceAsync(Guid invoiceId, MatchSalesInvoiceRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// API-36 (SAT-D227, SAT-D208): finalises the matched invoice (needs <c>sales.invoices.finalize</c> and price read), computes the TL
    /// instalment split, commits "Finans kabulü bekliyor" and offers the open items to Finance under the key
    /// <c>sales-invoice:{id}:{version}</c>. A Finance answer other than success leaves the invoice finalised and waiting.
    /// </summary>
    Task<SalesInvoiceCommandResult> FinalizeInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>API-36 (SAT-D208): re-drives the Finance hand-off of a finalised invoice that still waits (the same admission id, a key per attempt).</summary>
    Task<SalesInvoiceCommandResult> ResendFinanceAdmissionAsync(Guid invoiceId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesInvoiceListResponse> GetInvoicesAsync(SalesInvoiceListQuery query, CancellationToken cancellationToken = default);

    Task<SalesInvoiceDetailView> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// API-09: the invoices of one order for its "Faturalar" tab. Needs <c>sales.drafts.read</c> only (no amounts are answered);
    /// another company's order is 404 and its invoices are never read.
    /// </summary>
    Task<SalesDraftInvoicesResponse> GetDraftInvoicesAsync(Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// API-34 (SAT-D236/D244/D248): the facts of the finalised invoices of a date range for the one Excel workbook for Accounting. Needs
    /// <c>sales.invoices.export</c> AND <c>sales.orders.price.read</c> (the file is prices; SAT-D238 (3): without the price read the
    /// export fails closed with 403, never an amount-less file). Writes nothing and opens no official connection.
    /// </summary>
    Task<SalesInvoiceExportV1> ExportInvoicesAsync(SalesInvoiceExportQuery query, CancellationToken cancellationToken = default);
}

/// <summary>One instalment as Sales offers it to the Finance owner (ids fixed at finalisation; TL amount only for a foreign-currency invoice).</summary>
public sealed record SalesFinanceInstalmentV1(Guid OpenItemId, Guid InstallmentId, int SequenceNo, DateOnly DueDate, string Amount,
    string? FunctionalAmount);

/// <summary>
/// One admission offer. Company and actor are the server's (the owner fences them against its own context); the key is
/// <c>sales-invoice:{invoiceId}:{invoiceVersion}</c> for the finaliser's first offer and carries the attempt number for a re-drive by another actor.
/// <para>
/// F-005 D5 / FNS-D19 (<c>SINAN_KARARLARI_2026-11-11_FINANS_F005.md</c>): <paramref name="OfficialInvoiceNo"/> and <paramref name="Ettn"/>
/// are the official number and ETTN of the matched official document (API-35), copied unchanged from the invoice's match so Finance can
/// refuse a second open receivable for the same official document. Both are optional and appended last (additive, still V1): a caller
/// that does not set them sends <c>null</c>, and the owner leaves a null value out of its idempotency fingerprint, so an offer made
/// before these fields existed replays under its key. Sales sets both, or neither, on every offer of a finalised invoice.
/// </para>
/// </summary>
public sealed record SalesFinanceAdmissionRequestV1(Guid CompanyId, string ActorId, string CorrelationId, string? CausationId,
    string IdempotencyKey, Guid AdmissionId, Guid InvoiceId, string InvoiceVersion, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId,
    string? ExchangeRate, IReadOnlyList<SalesFinanceInstalmentV1> Instalments, string? OfficialInvoiceNo = null, string? Ettn = null);

/// <summary>What Finance answered. Only <see cref="Admitted"/> moves Sales' ledger to admitted; every other answer leaves it waiting.</summary>
public enum SalesFinanceAdmissionOutcomeV1
{
    /// <summary>The port or the owner could not answer (fail-closed): nothing is known.</summary>
    Unavailable = 0,

    /// <summary>The open items exist in Finance (first admission, replay or reuse).</summary>
    Admitted = 1,

    /// <summary>Finance refused the content (validation, party, or an invoice admitted with other rows).</summary>
    Refused = 2,

    /// <summary>The key was used by another actor or payload; a re-drive uses a new key.</summary>
    KeyConflict = 3,

    /// <summary>The owner is not authorised now or lost a race; try again.</summary>
    Retry = 4,
}

public sealed record SalesFinanceAdmissionAnswerV1(SalesFinanceAdmissionOutcomeV1 Outcome, string Reason)
{
    public static SalesFinanceAdmissionAnswerV1 Unavailable { get; } = new(SalesFinanceAdmissionOutcomeV1.Unavailable, "unavailable");
}

/// <summary>
/// The Sales-declared port to the Finance open-item admission (MOD-04, F2a). Declared here by the consumer, implemented and registered
/// by Finance's host. It carries no authority of Sales (the finalisation permission is measured before and after by the service) and
/// is synchronous and idempotent: the same key and content replays. An unregistered port answers nothing (the invoice stays waiting).
/// </summary>
public interface ISalesFinanceAdmissionPortV1
{
    Task<SalesFinanceAdmissionAnswerV1> AdmitAsync(SalesFinanceAdmissionRequestV1 request, CancellationToken cancellationToken);
}
