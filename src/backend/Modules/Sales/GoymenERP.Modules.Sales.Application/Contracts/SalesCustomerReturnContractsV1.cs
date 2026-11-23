using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// API-40 — müşteri iade faturası (EK57 SAT-D288…D291, EK58 SAT-D292…D294; Sinan 2026-11-10). Sales records the CUSTOMER's return
// invoice against a finalised sales invoice (never issues one), computes its amounts with the backend calculator, and reports the
// finalised return to Finance through ISalesCustomerReturnFinancePortV1 (SAT-D291 (4), SAT-D294). No stock movement is written and
// no other module's table is touched (SAT-D288, SAT-D293 (2)).

/// <summary>The versioned command names the customer return attempt record is keyed by (ADR-011 §2).</summary>
public static class SalesCustomerReturnCommands
{
    /// <summary>API-40: POST /api/v1/sales/customer-returns (SAT-D288).</summary>
    public const string CreateV1 = "sales.customer-return.create.v1";

    /// <summary>API-40: POST /api/v1/sales/customer-returns/{id}/submission (SAT-D290 (2)).</summary>
    public const string SubmitV1 = "sales.customer-return.submit.v1";

    /// <summary>API-40: POST /api/v1/sales/customer-returns/{id}/approval (SAT-D290 (3): never the preparer or the submitter).</summary>
    public const string ApproveV1 = "sales.customer-return.approve.v1";

    /// <summary>API-40: POST /api/v1/sales/customer-returns/{id}/finalization (SAT-D291, SAT-D294).</summary>
    public const string FinalizeV1 = "sales.customer-return.finalize.v1";

    /// <summary>API-40: POST /api/v1/sales/customer-returns/{id}/rejection (SAT-D290 (2, 3): the invoice's flow; never the preparer or the submitter).</summary>
    public const string RejectV1 = "sales.customer-return.reject.v1";

    /// <summary>API-40: POST /api/v1/sales/customer-returns/{id}/cancellation (SAT-D290 (2): before approval only).</summary>
    public const string CancelV1 = "sales.customer-return.cancel.v1";

    public static bool IsCustomerReturnCommand(string? name) =>
        name is CreateV1 or SubmitV1 or ApproveV1 or FinalizeV1 or RejectV1 or CancelV1;
}

/// <summary>Routes and technical bounds of API-40 (not policies).</summary>
public static class SalesCustomerReturnApiV1
{
    public const string CustomerReturnsPath = "/api/v1/sales/customer-returns";
    public const string CustomerReturnPath = "/api/v1/sales/customer-returns/{id}";
    public const string SubmissionPath = "/api/v1/sales/customer-returns/{id}/submission";
    public const string ApprovalPath = "/api/v1/sales/customer-returns/{id}/approval";
    public const string FinalizationPath = "/api/v1/sales/customer-returns/{id}/finalization";
    public const string RejectionPath = "/api/v1/sales/customer-returns/{id}/rejection";
    public const string CancellationPath = "/api/v1/sales/customer-returns/{id}/cancellation";

    /// <summary>SAT-S2 (FNS-D81): the outstanding installments of the original invoice and Finance's nearest-due prefill.</summary>
    public const string InstallmentsPath = "/api/v1/sales/customer-returns/{id}/installments";

    public const int CreatedStatus = 201;

    public const int CommandStatus = 200;

    /// <summary>Technical bound (not a policy): the most returns one list read answers (newest first).</summary>
    public const int MaximumListed = 200;

    /// <summary>Technical bound (not a policy): the longest quantity text a line takes.</summary>
    public const int MaximumQuantityTextLength = 32;

    /// <summary>Technical bound (not a policy): the most installments one finalisation selects (FNS-D81).</summary>
    public const int MaximumAllocations = 200;

    /// <summary>Technical bound (not a policy): the longest amount text an installment selection takes.</summary>
    public const int MaximumAmountTextLength = 32;
}

/// <summary>The stable problem codes of API-40 besides the shared draft codes and the domain's <c>sales_customer_return_*</c> rules.</summary>
public static class SalesCustomerReturnProblemCodesV1
{
    /// <summary>
    /// 503 (not recorded): no Finance API-11 v2 acceptance port is wired (a host without the Finance composition), so the
    /// finalisation is refused before anything is read or written — the receivable effect is never skipped silently (SAT-D294).
    /// </summary>
    public const string FinancePortUnavailable = "sales_customer_return_finance_port_unavailable";

    /// <summary>422 (not recorded): the original invoice is not this company's invoice (another company's is invisible, SAT-D288 (2)).</summary>
    public const string InvoiceNotFound = "sales_customer_return_invoice_not_found";

    /// <summary>422 (not recorded): Finance refused the reversal; the return stays approved and nothing is committed.</summary>
    public const string FinanceRefused = "sales_customer_return_finance_refused";

    /// <summary>
    /// 422 (not recorded): FNS-D81 — a partial return reduces exactly the installments the user chose; finalising one without a
    /// selection is refused (Finance's automatic nearest-due order, FNS-D54, is never applied to a Sales return). The screen reads
    /// <see cref="SalesCustomerReturnApiV1.InstallmentsPath"/> for the prefill and sends it (changed or not) as <c>allocations</c>.
    /// </summary>
    public const string InstallmentsRequired = "sales_customer_return_installments_required";

    /// <summary>409 (recorded under the key): the preparer or the submitter approves or rejects the return (SAT-D290 (3)).</summary>
    public const string SelfApproval = SalesRuleCodes.CustomerReturnSelfApproval;

    /// <summary>409 (not recorded): the customer's document number is already recorded for this customer.</summary>
    public const string DocumentDuplicate = SalesRuleCodes.CustomerReturnDocumentDuplicate;

    /// <summary>422 (not recorded): a return without a shipment link exceeds the invoiced-not-shipped balance (SAT-D293 (4)).</summary>
    public const string UnshippedBalanceExceeded = SalesRuleCodes.CustomerReturnUnshippedBalanceExceeded;
}

/// <summary>One chosen installment of a partial return (FNS-D81): the original invoice's open item and the exact part it takes.</summary>
public sealed record SalesCustomerReturnAllocationRequest(Guid OpenItemId, string Amount);

/// <summary>
/// API-40 finalisation body: the expected version and, for a partial return over several installments, the user's installment
/// selection (FNS-D81). <see cref="Allocations"/> is absent (null) when Finance answers that no selection applies (the return
/// covers the whole outstanding amount); an empty list is malformed.
/// </summary>
public sealed record FinalizeSalesCustomerReturnRequest(
    string ExpectedVersion,
    IReadOnlyList<SalesCustomerReturnAllocationRequest>? Allocations = null);

/// <summary>One outstanding installment of the original invoice (nearest due first) and the part Finance's prefill puts on it.</summary>
public sealed record SalesCustomerReturnInstallmentView(Guid OpenItemId, DateOnly DueOn, string OriginalAmount, string OutstandingAmount,
    string PrefillAmount);

/// <summary>
/// The installment read of an approved return (FNS-D81): <see cref="Amount"/> is the return amount Finance computed against,
/// <see cref="SelectionAllowed"/> is true exactly when the return is partial (the finalisation then needs <c>allocations</c>),
/// <see cref="AppliedAmount"/> reduces outstanding amounts and <see cref="AdvanceAmount"/> stays as the customer's advance.
/// </summary>
public sealed record SalesCustomerReturnInstallmentsResponse(Guid CustomerReturnId, string Version, string Amount, bool SelectionAllowed,
    string AppliedAmount, string AdvanceAmount, IReadOnlyList<SalesCustomerReturnInstallmentView> Installments);

/// <summary>One requested return line: the original invoice line and the returned quantity as exact invariant text.</summary>
public sealed record CreateSalesCustomerReturnLineRequest(Guid OriginalInvoiceLineId, string Quantity);

/// <summary>
/// API-40 request body: the original FINALISED sales invoice (mandatory, SAT-D288 (2)), the customer's document number and date
/// (the customer issued it, SAT-D288 (1)), the lines and optionally the shipment the return is linked to (SAT-D288 (3)). No amount,
/// price or rate is taken: they are copied from the original line (SAT-D289 (1), SAT-D293 (3)).
/// </summary>
public sealed record CreateSalesCustomerReturnRequest(
    Guid OriginalInvoiceId,
    string CustomerDocumentNo,
    DateOnly CustomerDocumentDate,
    IReadOnlyList<CreateSalesCustomerReturnLineRequest> Lines,
    Guid? ShipmentId = null);

/// <summary>Amounts as exact invariant text at the currency's scale (SAT-D292): the stored types and the matrah + KDV − tevkifat drift.</summary>
public sealed record SalesCustomerReturnAmountsView(string NetAmount, string VatAmount, string WithheldVatAmount, string LineTotalAmount,
    string DerivedPayableAmount, string DriftAmount);

public sealed record SalesCustomerReturnLineView(
    int LineNo,
    Guid OriginalInvoiceLineId,
    int OriginalLineNo,
    Guid ShipmentId,
    Guid ProductId,
    Guid UnitId,
    string Quantity,
    string NetUnitPrice,
    string VatPercent,
    string? WithholdingShare,
    bool IsLastReturn,
    bool NeedsReview,
    SalesCustomerReturnAmountsView Amounts,
    SalesCustomerReturnAmountsView? FunctionalAmounts);

/// <summary>
/// One customer return as the screens see it. Amounts are prices: every API-40 read and command also needs
/// <c>sales.orders.price.read</c>. <see cref="State"/> is <c>Draft</c>, <c>PendingApproval</c>, <c>Approved</c>, <c>Finalized</c>,
/// <c>Rejected</c> or <c>Cancelled</c>; the rejection and cancellation facts carry the optional reason.
/// </summary>
public sealed record SalesCustomerReturnView(
    Guid Id,
    string State,
    string Version,
    Guid OriginalInvoiceId,
    string OriginalDocumentNo,
    Guid? ShipmentId,
    string CustomerDocumentNo,
    DateOnly CustomerDocumentDate,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    int AmountScale,
    string? Rate,
    Guid? FunctionalCurrencyId,
    int? FunctionalScale,
    SalesCustomerReturnAmountsView Totals,
    SalesCustomerReturnAmountsView? FunctionalTotals,
    bool NeedsReview,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? FinalizedBy,
    DateTimeOffset? FinalizedAtUtc,
    string? RejectedBy,
    DateTimeOffset? RejectedAtUtc,
    string? RejectionReason,
    string? CancelledBy,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<SalesCustomerReturnLineView> Lines);

public sealed record SalesCustomerReturnSummaryView(
    Guid Id,
    string State,
    string Version,
    Guid OriginalInvoiceId,
    string OriginalDocumentNo,
    string CustomerDocumentNo,
    DateOnly CustomerDocumentDate,
    Guid PartyId,
    Guid CurrencyId,
    string LineTotalAmount,
    bool IsShipmentLinked,
    bool NeedsReview,
    int LineCount,
    DateTimeOffset CreatedAtUtc);

public sealed record SalesCustomerReturnListResponse(IReadOnlyList<SalesCustomerReturnSummaryView> Items);

/// <summary>The command's answer to the route: the return, whether it was replayed, and the first correlation (replay header).</summary>
public sealed record SalesCustomerReturnCommandResult(SalesCustomerReturnView CustomerReturn, bool IsReplay, string OriginalCorrelationId);

/// <summary>The strict reader of the API-40 bodies: unknown members, a null body, an empty id or a malformed quantity is a 400.</summary>
public static class SalesCustomerReturnJson
{
    /// <summary>The digits of the largest version (<see cref="long.MaxValue"/>), the shared state command's bound.</summary>
    private const int MaximumVersionDigits = 19;

    public static CreateSalesCustomerReturnRequest ReadCreate(string json)
    {
        var request = JsonSerializer.Deserialize<CreateSalesCustomerReturnRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        if (request.OriginalInvoiceId == Guid.Empty || request.ShipmentId == Guid.Empty || request.CustomerDocumentNo is null
            || request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > SalesCustomerReturn.MaximumLines)
        {
            throw new JsonException("Invalid customer return.");
        }

        foreach (var line in request.Lines)
        {
            if (line is null || line.OriginalInvoiceLineId == Guid.Empty || line.Quantity is null
                || line.Quantity.Length > SalesCustomerReturnApiV1.MaximumQuantityTextLength)
            {
                throw new JsonException("Invalid customer return line.");
            }

            SalesDraftJson.RequireDecimalShape(line.Quantity, positive: true);
        }

        return request;
    }

    /// <summary>
    /// The finalisation body: the expected version and the optional installment selection (FNS-D81). An empty or oversized list,
    /// an empty or repeated open item, or an amount that is not a positive exact decimal is a 400; a reason is an unknown member.
    /// </summary>
    public static FinalizeSalesCustomerReturnRequest ReadFinalize(string json)
    {
        var request = JsonSerializer.Deserialize<FinalizeSalesCustomerReturnRequest>(json, SalesDraftJson.Options)
            ?? throw new JsonException("Request body is required.");
        RequireFinalize(request);
        return request;
    }

    /// <summary>The checks of <see cref="ReadFinalize"/>, repeated by the service for a direct caller.</summary>
    internal static void RequireFinalize(FinalizeSalesCustomerReturnRequest request)
    {
        // The expected version shape is the shared state command's: a positive integer text of at most 19 digits.
        if (request is null || string.IsNullOrEmpty(request.ExpectedVersion) || request.ExpectedVersion.Length > MaximumVersionDigits
            || !Regex.IsMatch(request.ExpectedVersion, "\\A[1-9][0-9]*\\z"))
        {
            throw new JsonException("Invalid expected version.");
        }

        if (request.Allocations is null)
        {
            return;
        }

        if (request.Allocations.Count == 0 || request.Allocations.Count > SalesCustomerReturnApiV1.MaximumAllocations)
        {
            throw new JsonException("Invalid installment selection.");
        }

        var seen = new HashSet<Guid>();
        foreach (var allocation in request.Allocations)
        {
            if (allocation is null || allocation.OpenItemId == Guid.Empty || !seen.Add(allocation.OpenItemId)
                || allocation.Amount is null || allocation.Amount.Length > SalesCustomerReturnApiV1.MaximumAmountTextLength)
            {
                throw new JsonException("Invalid installment.");
            }

            SalesDraftJson.RequireDecimalShape(allocation.Amount, positive: true);
        }
    }

    /// <summary>Submission and approval carry the expected version only (a reason is an unknown member).</summary>
    public static SalesDraftStateCommandRequest ReadStateCommand(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: false);

    /// <summary>Rejection and cancellation carry the expected version and an optional reason (the invoice's body).</summary>
    public static SalesDraftStateCommandRequest ReadDecisionCommand(string json) => SalesDraftJson.ReadStateCommand(json, allowReason: true);

    /// <summary>The exact quantity of a validated line text (shape checked by <see cref="ReadCreate"/>).</summary>
    public static decimal Quantity(string text) => decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}

/// <summary>The canonical contents of the API-40 commands for the replay identity (ADR-011 §3).</summary>
public static class SalesCustomerReturnHash
{
    public static string Compute(CreateSalesCustomerReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = SalesCustomerReturnCommands.CreateV1,
            request.OriginalInvoiceId,
            request.ShipmentId,
            request.CustomerDocumentNo,
            request.CustomerDocumentDate,
            lines = request.Lines.Select(line => new { line.OriginalInvoiceLineId, line.Quantity }).ToArray(),
        }, SalesDraftJson.Options));
    }

    public static string Compute(string commandName, Guid customerReturnId, SalesDraftStateCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = commandName,
            customerReturnId,
            request.ExpectedVersion,
        }, SalesDraftJson.Options));
    }

    /// <summary>
    /// Finalisation: without a selection the content is exactly the state command's (earlier keys keep their hash); with one, the
    /// selection in its submitted order is part of the content, so the same key with another selection is a 409.
    /// </summary>
    public static string ComputeFinalize(Guid customerReturnId, FinalizeSalesCustomerReturnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Allocations is null)
        {
            return Compute(SalesCustomerReturnCommands.FinalizeV1, customerReturnId, new SalesDraftStateCommandRequest(request.ExpectedVersion));
        }

        return Hash(JsonSerializer.Serialize(new
        {
            command = SalesCustomerReturnCommands.FinalizeV1,
            customerReturnId,
            request.ExpectedVersion,
            allocations = request.Allocations.Select(item => new { item.OpenItemId, item.Amount }).ToArray(),
        }, SalesDraftJson.Options));
    }

    /// <summary>Rejection and cancellation: the reason is part of the content, so the same key with another reason is a 409.</summary>
    public static string ComputeDecision(string commandName, Guid customerReturnId, SalesDraftStateCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new
        {
            command = commandName,
            customerReturnId,
            request.ExpectedVersion,
            request.Reason,
        }, SalesDraftJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}

/// <summary>What the store did for an API-40 command: the return, recorded now or replayed, and the first correlation.</summary>
public sealed record SalesCustomerReturnStoreResult(SalesCustomerReturn CustomerReturn, SalesDraftWriteOutcome Outcome,
    string OriginalCorrelationId);

/// <summary>What an API-40 command key already holds: the stored return or the recorded terminal refusal, never both.</summary>
public sealed record SalesCustomerReturnReplay(SalesCustomerReturn? CustomerReturn, SalesDraftTerminalRefusal? Refusal,
    string OriginalCorrelationId);

/// <summary>
/// The customer return half of the Sales store (the ADR-011 §2–§8 attempt rules in the return's own attempt table): one save writes
/// the return, the attempt and the audit event. The unique customer document index refuses a second return with the same customer
/// document number of the same customer, and the unique finalisation sequence index of the original invoice refuses a finalisation
/// that computed against stale finalised returns — both as the save's concurrent change. Another company's returns are invisible.
/// </summary>
public interface ISalesCustomerReturnStore
{
    /// <summary>The stored answer or recorded refusal of the key, null when the key is new; another content or actor is a 409.</summary>
    Task<SalesCustomerReturnReplay?> FindCustomerReturnReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    /// <summary>Records a new draft return in one save (the row, the attempt, the audit); <paramref name="build"/> gets the commit instant.</summary>
    Task<SalesCustomerReturnStoreResult> StoreCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<DateTimeOffset, SalesCustomerReturn> build, SalesDraftStateAudit audit, CancellationToken cancellationToken = default);

    /// <summary>ADR-011 §7: records a terminal refusal of an API-40 state command under its key.</summary>
    Task RecordCustomerReturnRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid customerReturnId,
        SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submission, approval, rejection or cancellation of the company's stored return in one save (the row, the attempt, the audit). <paramref name="mutate"/>
    /// is the domain's decision; a refusal writes nothing. Null when the return does not exist here.
    /// </summary>
    Task<SalesCustomerReturnStoreResult?> DecideCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid customerReturnId, Action<SalesCustomerReturn, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalises the company's approved return in one save (state, version, the finalisation sequence, the attempt, the audit). The
    /// sequence is <paramref name="finalizedBefore"/> + 1 per original invoice: the domain computed against exactly that many finalised
    /// returns, so a return finalised meanwhile makes this save the concurrent change. Null when the return does not exist here.
    /// </summary>
    Task<SalesCustomerReturnStoreResult?> FinalizeCustomerReturnAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Guid customerReturnId, int finalizedBefore, Action<SalesCustomerReturn, DateTimeOffset> finalize, SalesDraftStateAudit audit,
        CancellationToken cancellationToken = default);

    Task<SalesCustomerReturn?> FindCustomerReturnAsync(Guid customerReturnId, CancellationToken cancellationToken = default);

    /// <summary>The company's returns, optionally of one original invoice, newest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<SalesCustomerReturn>> ListCustomerReturnsAsync(Guid? originalInvoiceId, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>SAT-D292 (6): the company's FINALISED returns of one original invoice (the priors of every calculation).</summary>
    Task<IReadOnlyList<SalesCustomerReturn>> ListFinalizedCustomerReturnsAsync(Guid originalInvoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>Whether the company already recorded a return with this customer document number for this customer.</summary>
    Task<bool> CustomerDocumentExistsAsync(Guid partyId, string customerDocumentNo, CancellationToken cancellationToken = default);
}

/// <summary>
/// API-40 (SAT-D288…D294): record, submit, approve and finalise a customer return invoice, and read one or a list. Company, actor and
/// authority are the server's and measured afresh before anything is read, after every awaited read and right before the single save.
/// <list type="bullet">
/// <item>Create, submit and cancel need <c>sales.returns.write</c>; approve and reject <c>sales.returns.approve</c> (never the preparer
/// or the submitter);
/// finalise <c>sales.returns.finalize</c>; the reads <c>sales.returns.read</c>.</item>
/// <item>The installment read (FNS-D81) needs <c>sales.returns.finalize</c>: it is the finalisation screen's question.</item>
/// <item>Every one of them also needs <c>sales.orders.price.read</c>: the answer carries the amounts.</item>
/// </list>
/// </summary>
public interface ISalesCustomerReturnService
{
    Task<SalesCustomerReturnCommandResult> CreateCustomerReturnAsync(CreateSalesCustomerReturnRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<SalesCustomerReturnCommandResult> SubmitCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesCustomerReturnCommandResult> ApproveCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesCustomerReturnCommandResult> RejectCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesCustomerReturnCommandResult> CancelCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesCustomerReturnCommandResult> FinalizeCustomerReturnAsync(Guid customerReturnId, FinalizeSalesCustomerReturnRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>FNS-D81: the outstanding installments of an APPROVED return's original invoice and Finance's nearest-due prefill.</summary>
    Task<SalesCustomerReturnInstallmentsResponse> GetCustomerReturnInstallmentsAsync(Guid customerReturnId,
        CancellationToken cancellationToken = default);

    Task<SalesCustomerReturnView> GetCustomerReturnAsync(Guid customerReturnId, CancellationToken cancellationToken = default);

    Task<SalesCustomerReturnListResponse> ListCustomerReturnsAsync(Guid? originalInvoiceId, CancellationToken cancellationToken = default);
}

/// <summary>Finance's answer to a customer return acceptance offer (mapped by the host from Finance's command outcome).</summary>
public enum SalesCustomerReturnFinanceOutcomeV1
{
    /// <summary>Finance recorded (or had already recorded) the reversing credit of this return; Sales may commit the finalisation.</summary>
    Admitted = 1,

    /// <summary>Finance refused the return (its reason code is answered); Sales commits nothing.</summary>
    Refused = 2,

    /// <summary>The same idempotency key was used with another payload at Finance; Sales commits nothing.</summary>
    KeyConflict = 3,

    /// <summary>Finance lost a concurrent write; the same command may be retried.</summary>
    Retry = 4,

    /// <summary>Finance's own authority fence denied the actor.</summary>
    Denied = 5,

    /// <summary>Finance could not answer (unmapped composition, missing data or a store failure).</summary>
    Unavailable = 6,
}

/// <summary>One chosen installment as Sales reports it to Finance (FNS-D81 → Finance's allocation, FNS-D44).</summary>
public sealed record SalesCustomerReturnFinanceAllocationV1(Guid OpenItemId, string Amount);

/// <summary>
/// SAT-D291 (4), SAT-D294 (3)/(4), API-11 v2: what Sales tells Finance about a finalising return — the actor and the request
/// identity (company, actor, correlation, causation, idempotency key: Finance fences and replays by them), the return document and
/// its version, the admitted original invoice and its version (Finance's expected version), the return amount (the sum of the
/// line totals, invoice currency, and its TL equivalent for a foreign-currency return), the business date and the user's
/// installment selection (FNS-D81; null when no selection applies). The open item / advance split is Finance's (SAT-D294 (4)).
/// <see cref="CustomerReturnId"/> is the idempotent acceptance identity: the same return offered again with the same payload
/// answers Finance's first answer.
/// </summary>
public sealed record SalesCustomerReturnFinanceOfferV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    Guid CustomerReturnId,
    string DocumentVersion,
    Guid OriginalInvoiceId,
    string OriginalInvoiceVersion,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    string Amount,
    Guid? FunctionalCurrencyId,
    string? FunctionalAmount,
    DateOnly BusinessDate,
    IReadOnlyList<SalesCustomerReturnFinanceAllocationV1>? Allocations);

public sealed record SalesCustomerReturnFinanceAnswerV1(Guid CustomerReturnId, SalesCustomerReturnFinanceOutcomeV1 Outcome, string? ReasonCode);

/// <summary>The installment question (FNS-D81): the return, its original invoice and version, and the return amount as exact text.</summary>
public sealed record SalesCustomerReturnInstallmentQueryV1(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    Guid CustomerReturnId,
    Guid OriginalInvoiceId,
    string OriginalInvoiceVersion,
    string Amount);

/// <summary>One outstanding installment as Finance answered it (nearest due first).</summary>
public sealed record SalesCustomerReturnFinanceInstallmentV1(Guid OpenItemId, DateOnly DueOn, string OriginalAmount,
    string OutstandingAmount, string PrefillAmount);

/// <summary>
/// Finance's installment answer: <see cref="Outcome"/> <see cref="SalesCustomerReturnFinanceOutcomeV1.Admitted"/> when the prefill
/// was computed, otherwise the refusal (with Finance's reason) or the fence's answer; amounts are null on a refusal.
/// </summary>
public sealed record SalesCustomerReturnInstallmentAnswerV1(
    SalesCustomerReturnFinanceOutcomeV1 Outcome,
    string? ReasonCode,
    bool SelectionAllowed,
    string? AppliedAmount,
    string? AdvanceAmount,
    IReadOnlyList<SalesCustomerReturnFinanceInstallmentV1> Installments);

/// <summary>
/// The Sales-owned consumer port towards Finance's customer return acceptance (API-11 v2, SAT-D291 (4), SAT-D294, FNS-D81). The
/// host adapts it onto <c>IFinanceInvoiceAcceptancePortV2</c> and <c>IFinanceCustomerReturnInstallmentReadPortV2</c>; a host
/// without that composition leaves it unregistered and the finalisation fails closed with
/// <see cref="SalesCustomerReturnProblemCodesV1.FinancePortUnavailable"/>.
/// </summary>
public interface ISalesCustomerReturnFinancePortV1
{
    Task<SalesCustomerReturnFinanceAnswerV1> OfferAsync(SalesCustomerReturnFinanceOfferV1 offer, CancellationToken cancellationToken = default);

    Task<SalesCustomerReturnInstallmentAnswerV1> ReadInstallmentsAsync(SalesCustomerReturnInstallmentQueryV1 query,
        CancellationToken cancellationToken = default);
}
