using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Domain.CustomerReturns;

/// <summary>
/// The lifecycle of a recorded customer return invoice (API-40; SAT-D290 (2)): <c>Draft → PendingApproval → Approved → Finalized</c>,
/// the sales invoice's flow. Only a finalised return counts as a prior return (SAT-D292 (6)) and decrements a balance (SAT-D293 (1)).
/// Rejection and cancellation are not part of this slice (follow-up); nothing is deleted.
/// </summary>
public enum SalesCustomerReturnState
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Finalized = 4,
}

/// <summary>One requested return line: the original invoice line and the returned quantity (everything else is copied).</summary>
public sealed record SalesCustomerReturnLineInput(Guid OriginalInvoiceLineId, decimal Quantity);

/// <summary>
/// A stored return line: the original invoice line it returns (with the shipment that line took), the copied unit, net unit price
/// (after the original discount), VAT rate and withholding share (SAT-D289 (1), SAT-D293 (3)), the quantity, whether it was the
/// line's last return when computed, and the calculator's amounts (SAT-D292).
/// </summary>
public sealed record SalesCustomerReturnLine(
    int LineNo,
    Guid OriginalInvoiceLineId,
    int OriginalLineNo,
    Guid ShipmentId,
    Guid ProductId,
    Guid UnitId,
    decimal Quantity,
    decimal NetUnitPrice,
    decimal VatPercent,
    decimal? WithholdingShare,
    bool IsLastReturn,
    SalesCustomerReturnAmounts Amounts,
    SalesCustomerReturnAmounts? Functional,
    bool NeedsReview);

/// <summary>
/// The header copied from the original invoice: the customer, the currency at the money profile's scale and, for a foreign-currency
/// invoice, the locked invoice rate with the functional currency and scale (SAT-D172, SAT-D289 (1): never re-read).
/// </summary>
public sealed record SalesCustomerReturnHeader(
    Guid OriginalInvoiceId,
    string OriginalDocumentNo,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    int AmountScale,
    decimal? Rate,
    Guid? FunctionalCurrencyId,
    int? FunctionalScale);

/// <summary>Who submitted, approved and finalised a return, and when.</summary>
public sealed record SalesCustomerReturnFacts(
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? FinalizedBy,
    DateTimeOffset? FinalizedAtUtc)
{
    public static SalesCustomerReturnFacts None { get; } = new(null, null, null, null, null, null);
}

/// <summary>
/// A customer return invoice as Sales records it (API-40; SAT-D288…SAT-D294):
/// <list type="bullet">
/// <item><b>The customer's document (SAT-D288 (1)):</b> the ERP never issues it; the customer's document number and date are recorded.</item>
/// <item><b>Original invoice (SAT-D288 (2)):</b> a FINALISED sales invoice of the same company; mandatory.</item>
/// <item><b>Shipment (SAT-D288 (3)):</b> optional; when given it must be a shipment of the original invoice and every line must be of it.</item>
/// <item><b>Amounts (SAT-D289, SAT-D292):</b> <see cref="SalesCustomerReturnCalculator"/> only, against the finalised prior returns;
/// recomputed at finalisation and refused if they changed meanwhile.</item>
/// <item><b>Balance (SAT-D293 (4)):</b> a return without a shipment link must fit the invoiced-not-shipped balance.</item>
/// <item><b>Approval (SAT-D290):</b> the preparer and the submitter never approve.</item>
/// <item><b>No stock (SAT-D293 (2))</b> and no other module's table: the finance effect is reported through the Finance port (SAT-D294).</item>
/// </list>
/// </summary>
public sealed class SalesCustomerReturn
{
    public const int MaximumLines = SalesOrderDraft.MaximumLines;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;
    public const int MaximumCustomerDocumentNoLength = 64;

    private readonly List<SalesCustomerReturnLine> lines = [];

    private SalesCustomerReturn(Guid id, Guid companyId, SalesCustomerReturnHeader header, Guid? shipmentId, string customerDocumentNo,
        DateOnly customerDocumentDate, string createdBy, DateTimeOffset createdAtUtc, long version)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        Header = header ?? throw new ArgumentNullException(nameof(header));
        ShipmentId = shipmentId is { } shipment ? Identity.Require(shipment, nameof(shipmentId)) : null;
        CustomerDocumentNo = customerDocumentNo;
        CustomerDocumentDate = customerDocumentDate;
        CreatedBy = RequireActor(createdBy);
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
        Version = version >= 1 ? version : throw new ArgumentOutOfRangeException(nameof(version));
    }

    public Guid Id { get; }

    /// <summary>The server-resolved company; never taken from the request and never changed.</summary>
    public Guid CompanyId { get; }

    public SalesCustomerReturnHeader Header { get; }

    /// <summary>The optional shipment link (SAT-D288 (3)); null for a return without a shipment link.</summary>
    public Guid? ShipmentId { get; }

    public bool IsShipmentLinked => ShipmentId is not null;

    public string CustomerDocumentNo { get; }

    public DateOnly CustomerDocumentDate { get; }

    public SalesCustomerReturnState State { get; private set; } = SalesCustomerReturnState.Draft;

    public SalesCustomerReturnFacts Facts { get; private set; } = SalesCustomerReturnFacts.None;

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Optimistic concurrency: 1 at creation, +1 on every command; a refused command moves nothing.</summary>
    public long Version { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<SalesCustomerReturnLine> Lines => lines.AsReadOnly();

    /// <summary>The document totals: the sums of the lines, type by type (SAT-D292 (3): no document-level rounding).</summary>
    public SalesCustomerReturnAmounts Totals =>
        SalesCustomerReturnAmounts.Sum(lines.Select(line => line.Amounts), Header.CurrencyId, Header.AmountScale);

    /// <summary>The TL totals of a foreign-currency return (sums of the lines); null for TL.</summary>
    public SalesCustomerReturnAmounts? FunctionalTotals => Header.FunctionalCurrencyId is { } functional
        ? SalesCustomerReturnAmounts.Sum(lines.Select(line => line.Functional!), functional, Header.FunctionalScale!.Value)
        : null;

    public bool NeedsReview => lines.Any(line => line.NeedsReview);

    /// <summary>
    /// Records a draft against a finalised invoice. <paramref name="priors"/> are the FINALISED earlier returns per original line id
    /// (SAT-D292 (6)); a line without an entry has none.
    /// </summary>
    public static SalesCustomerReturn Create(Guid id, Guid companyId, SalesInvoice invoice, Guid? shipmentId, string? customerDocumentNo,
        DateOnly customerDocumentDate, IReadOnlyList<SalesCustomerReturnLineInput> inputs,
        IReadOnlyDictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>> priors, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(priors);
        if (invoice.CompanyId != companyId)
        {
            throw new InvalidOperationException("Orijinal fatura başka şirketin kaydıdır.");
        }

        if (invoice.State != SalesInvoiceState.Finalized)
        {
            throw new SalesRuleException(SalesRuleCodes.CustomerReturnInvoiceNotFinalized,
                "İade faturası yalnız kesinleşmiş satış faturasına bağlanır.");
        }

        var documentNo = RequireDocumentNo(customerDocumentNo);
        if (customerDocumentDate == default)
        {
            throw new SalesRuleException(SalesRuleCodes.CustomerReturnDocumentInvalid, "Müşterinin iade faturası tarihi gereklidir.");
        }

        if (shipmentId is { } shipment && invoice.Lines.All(line => line.Share.ShipmentId != shipment))
        {
            throw new SalesRuleException(SalesRuleCodes.CustomerReturnShipmentNotOnInvoice, "Sevk orijinal faturanın sevki değildir.");
        }

        if (inputs.Count == 0 || inputs.Count > MaximumLines)
        {
            throw new SalesRuleException(SalesRuleCodes.CustomerReturnLinesInvalid,
                $"Bir iade faturası 1 ile {MaximumLines} arasında satır taşır.");
        }

        var rate = invoice.Header.Rate;
        var header = new SalesCustomerReturnHeader(invoice.Id, invoice.DocumentNo, invoice.Header.PartyId, invoice.Header.CompanyAccountId,
            invoice.Header.CurrencyId, invoice.Header.AmountScale, rate?.Rate, rate?.FunctionalCurrencyId, rate?.FunctionalScale);
        var customerReturn = new SalesCustomerReturn(id, companyId, header, shipmentId, documentNo, customerDocumentDate, createdBy, now, 1);
        var seen = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!seen.Add(input.OriginalInvoiceLineId))
            {
                throw new SalesRuleException(SalesRuleCodes.CustomerReturnLinesInvalid, "Aynı fatura satırı bir iadede iki kez yer alamaz.",
                    input.OriginalInvoiceLineId);
            }

            var original = invoice.Lines.FirstOrDefault(line => line.Id == input.OriginalInvoiceLineId)
                ?? throw new SalesRuleException(SalesRuleCodes.CustomerReturnLineNotOnInvoice, "Satır orijinal faturanın satırı değildir.",
                    input.OriginalInvoiceLineId);
            if (shipmentId is { } linked && original.Share.ShipmentId != linked)
            {
                throw new SalesRuleException(SalesRuleCodes.CustomerReturnShipmentNotOnInvoice,
                    "Sevke bağlı iadenin her satırı o sevkin fatura satırı olmalıdır.", original.Id);
            }

            customerReturn.lines.Add(Compute(customerReturn.lines.Count + 1, original, input.Quantity, PriorsOf(priors, original.Id),
                customerReturn.IsShipmentLinked));
        }

        return customerReturn;
    }

    /// <summary>Sends the draft for approval: <c>Draft → PendingApproval</c> (the submitter is recorded).</summary>
    public void Submit(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireState(SalesCustomerReturnState.Draft, "Yalnız taslak iade onaya gönderilir.");
        State = SalesCustomerReturnState.PendingApproval;
        Facts = Facts with { SubmittedBy = actor, SubmittedAtUtc = now.ToUniversalTime() };
        Advance(now);
    }

    /// <summary>SAT-D290 (3): <c>PendingApproval → Approved</c> by someone who neither prepared nor submitted the return.</summary>
    public void Approve(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireState(SalesCustomerReturnState.PendingApproval, "Yalnız onay bekleyen iade onaylanır.");
        if (string.Equals(actor, CreatedBy, StringComparison.Ordinal) || string.Equals(actor, Facts.SubmittedBy, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.CustomerReturnSelfApproval, "İadeyi hazırlayan ya da onaya gönderen onaylayamaz.");
        }

        State = SalesCustomerReturnState.Approved;
        Facts = Facts with { ApprovedBy = actor, ApprovedAtUtc = now.ToUniversalTime() };
        Advance(now);
    }

    /// <summary>
    /// <c>Approved → Finalized</c>. Every line is computed again against the finalised returns of now (SAT-D292 (6)): a quantity
    /// beyond the remaining one is refused (SAT-D293 (3)), amounts that differ from the approved ones are refused (the approver saw
    /// other numbers; record a new return), and a return without a shipment link must still fit the balance (SAT-D293 (4)).
    /// </summary>
    public void Finalize(long expectedVersion, string actorId, DateTimeOffset now, SalesInvoice invoice,
        IReadOnlyDictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>> priors)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(priors);
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireState(SalesCustomerReturnState.Approved, "Yalnız onaylı iade kesinleşir.");
        if (invoice.Id != Header.OriginalInvoiceId || invoice.CompanyId != CompanyId || invoice.State != SalesInvoiceState.Finalized)
        {
            throw new InvalidOperationException("İadenin orijinal faturası okunamadı.");
        }

        foreach (var line in lines)
        {
            var original = invoice.Lines.FirstOrDefault(candidate => candidate.Id == line.OriginalInvoiceLineId)
                ?? throw new InvalidOperationException("İade satırının orijinal fatura satırı okunamadı.");
            var recomputed = Compute(line.LineNo, original, line.Quantity, PriorsOf(priors, original.Id), IsShipmentLinked);
            if (recomputed != line)
            {
                throw new SalesRuleException(SalesRuleCodes.CustomerReturnAmountsChanged,
                    "Aynı satırın başka bir iadesi kesinleşti; onaylanan tutarlar artık geçerli değil.", line.OriginalInvoiceLineId);
            }
        }

        State = SalesCustomerReturnState.Finalized;
        Facts = Facts with { FinalizedBy = actor, FinalizedAtUtc = now.ToUniversalTime() };
        Advance(now);
    }

    /// <summary>
    /// Rebuilds a stored return: the amounts are taken as stored, but each line must be internally consistent (currencies, TL present
    /// exactly on a foreign-currency return) and the facts must match the state. A corrupt row is refused, never repaired.
    /// </summary>
    public static SalesCustomerReturn Restore(Guid id, Guid companyId, SalesCustomerReturnHeader header, Guid? shipmentId,
        string customerDocumentNo, DateOnly customerDocumentDate, SalesCustomerReturnState state, SalesCustomerReturnFacts facts,
        string createdBy, DateTimeOffset createdAtUtc, long version, DateTimeOffset updatedAtUtc,
        IReadOnlyList<SalesCustomerReturnLine> storedLines)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(storedLines);
        var customerReturn = new SalesCustomerReturn(id, companyId, header, shipmentId, RequireDocumentNo(customerDocumentNo),
            customerDocumentDate, createdBy, createdAtUtc, version)
        {
            State = state,
            Facts = facts,
            UpdatedAtUtc = updatedAtUtc.ToUniversalTime(),
        };
        var functional = header.FunctionalCurrencyId is not null;
        if (!Enum.IsDefined(state) || storedLines.Count is 0 or > MaximumLines || !FactsAreConsistent(state, createdBy, facts)
            || version < (int)state || functional != (header.FunctionalScale is not null) || functional != (header.Rate is not null))
        {
            throw new InvalidOperationException("Kayıtlı iade faturası durumu ya da karar bilgisi okunamadı.");
        }

        var seen = new HashSet<Guid>();
        for (var index = 0; index < storedLines.Count; index++)
        {
            var line = storedLines[index];
            if (line is null || line.LineNo != index + 1 || !seen.Add(line.OriginalInvoiceLineId) || line.Quantity <= 0m
                || shipmentId is { } linked && line.ShipmentId != linked
                || !InCurrency(line.Amounts, header.CurrencyId, header.AmountScale)
                || functional != (line.Functional is not null)
                || line.Functional is { } tl && !InCurrency(tl, header.FunctionalCurrencyId!.Value, header.FunctionalScale!.Value))
            {
                throw new InvalidOperationException("Kayıtlı iade satırı tutarları birbirini tutmuyor.");
            }

            customerReturn.lines.Add(line);
        }

        return customerReturn;
    }

    private static SalesCustomerReturnLine Compute(int lineNo, SalesInvoiceLine original, decimal quantity,
        IReadOnlyList<SalesCustomerReturnPrior> priors, bool isShipmentLinked)
    {
        var result = SalesCustomerReturnCalculator.Calculate(SalesCustomerReturnOriginalLine.Of(original), priors, quantity);

        // Invoice lines take whole shipped shares (SAT-D94), so the line's shipped quantity is its invoiced quantity.
        SalesCustomerReturnBalance.RequireFits(isShipmentLinked, quantity, original.Share.Quantity, original.Share.Quantity, priors,
            original.Id);
        return new SalesCustomerReturnLine(lineNo, original.Id, original.LineNo, original.Share.ShipmentId, original.Share.ProductId,
            original.Share.UnitId, result.Quantity, original.NetUnitPrice, original.VatPercent, original.WithholdingShare,
            result.IsLastReturn, result.Amounts, result.Functional, result.NeedsReview);
    }

    private static IReadOnlyList<SalesCustomerReturnPrior> PriorsOf(IReadOnlyDictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>> priors,
        Guid lineId) => priors.TryGetValue(lineId, out var list) ? list : [];

    private static bool InCurrency(SalesCustomerReturnAmounts amounts, Guid currencyId, int scale) =>
        new[] { amounts.Net, amounts.Vat, amounts.Withholding, amounts.LineTotal }
            .All(money => money.CurrencyId == currencyId && money.Scale == scale);

    private static bool FactsAreConsistent(SalesCustomerReturnState state, string createdBy, SalesCustomerReturnFacts facts)
    {
        var submitted = facts.SubmittedBy is not null && facts.SubmittedAtUtc is not null;
        var submittedNone = facts.SubmittedBy is null && facts.SubmittedAtUtc is null;
        var approved = facts.ApprovedBy is not null && facts.ApprovedAtUtc is not null
            && !string.Equals(facts.ApprovedBy, createdBy, StringComparison.Ordinal)
            && !string.Equals(facts.ApprovedBy, facts.SubmittedBy, StringComparison.Ordinal);
        var approvedNone = facts.ApprovedBy is null && facts.ApprovedAtUtc is null;
        var finalized = facts.FinalizedBy is not null && facts.FinalizedAtUtc is not null;
        var finalizedNone = facts.FinalizedBy is null && facts.FinalizedAtUtc is null;
        return state switch
        {
            SalesCustomerReturnState.Draft => submittedNone && approvedNone && finalizedNone,
            SalesCustomerReturnState.PendingApproval => submitted && approvedNone && finalizedNone,
            SalesCustomerReturnState.Approved => submitted && approved && finalizedNone,
            SalesCustomerReturnState.Finalized => submitted && approved && finalized,
            _ => false,
        };
    }

    private static string RequireDocumentNo(string? value)
    {
        var text = value?.Trim();
        return !string.IsNullOrEmpty(text) && text.Length <= MaximumCustomerDocumentNoLength
            ? text
            : throw new SalesRuleException(SalesRuleCodes.CustomerReturnDocumentInvalid,
                $"Müşterinin iade faturası numarası gereklidir ve en fazla {MaximumCustomerDocumentNoLength} karakterdir.");
    }

    private static string RequireActor(string actorId) =>
        !string.IsNullOrWhiteSpace(actorId) && actorId.Length <= MaximumActorLength
            ? actorId
            : throw new ArgumentException("Kullanıcı kimliği boş olamaz ve sınırı aşamaz.", nameof(actorId));

    private void RequireState(SalesCustomerReturnState expected, string message)
    {
        if (State != expected)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, message);
        }
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new SalesVersionConflictException(expectedVersion, Version);
        }
    }

    private void Advance(DateTimeOffset now)
    {
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }
}
