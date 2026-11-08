using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>
/// The lifecycle of a sales invoice. API-32 records <see cref="Draft"/>; API-33 adds the internal approval
/// (<see cref="PendingApproval"/>, <see cref="Approved"/>, <see cref="Rejected"/>) and the pre-approval <see cref="Cancelled"/>;
/// the official-document match (API-35) and the finalisation (API-36) add their states in their own slices — never a silent jump.
/// <c>Draft → PendingApproval → Approved | Rejected</c>; <c>Draft | PendingApproval → Cancelled</c>. Rejected and Cancelled are
/// terminal and release the shipment shares (SAT-D94); a rejected draft is corrected as a new draft.
/// </summary>
public enum SalesInvoiceState
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
}

/// <summary>
/// The approval and cancellation facts of an invoice (API-33): who submitted it and when, who approved or rejected it (never the
/// preparer, SAT-D88/D204) with the optional free-text reason, and who cancelled it before approval with the optional reason.
/// </summary>
public sealed record SalesInvoiceDecisionFacts(
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason,
    string? CancelledBy,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason)
{
    public static SalesInvoiceDecisionFacts None { get; } = new(null, null, null, null, null, null, null, null);
}

/// <summary>
/// The invoice's rate (SAT-D90 / SAT-D170 rate day, SAT-D172: written on the invoice as a number when the rate is added and never
/// re-read; SAT-D226: the TL equivalent uses exactly this rate): the TCMB selling rate of the bulletin day the resolver answered,
/// the functional currency (TL) and its money scale.
/// </summary>
public sealed record SalesInvoiceRateSnapshot(decimal Rate, DateOnly RateDay, DateOnly BulletinDate, Guid FunctionalCurrencyId,
    int FunctionalScale);

/// <summary>
/// The invoice header: one customer (party + company account), one currency at the money profile's scale (the price basis order's
/// profile), the server business day the draft was prepared on (the official invoice date comes with the match, SAT-D86), and the
/// rate for a foreign-currency invoice (null for TL).
/// </summary>
public sealed record SalesInvoiceHeader(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    Guid MoneyProfileId,
    string MoneyProfileVersion,
    int AmountScale,
    DateOnly BusinessDate,
    SalesInvoiceRateSnapshot? Rate);

/// <summary>
/// One shipment share an invoice line takes (MOD-06, AT-055 per share, SAT-D94): the approved dispatch note line, its shipment and
/// the Sales order line it serves, the price basis revision (SAT-D103/D114/D115 gate) with its commercial fields and tax snapshot,
/// and the WHOLE shipped quantity (partial quantities, SAT-D87, are a later slice).
/// </summary>
public sealed record SalesInvoiceShareInput(
    Guid LineId,
    Guid ShipmentId,
    Guid DispatchNoteId,
    string DispatchNoteNumber,
    int NoteLineNo,
    DateOnly ShipmentDate,
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    int PriceBasisRevision,
    Guid ProductId,
    Guid UnitId,
    decimal Quantity,
    string QuantityText,
    CommercialDraftFields Commercial,
    SalesLineTaxSnapshot Tax);

/// <summary>One instalment of the invoice's payable amount (SAT-D124: the last takes the remainder) with its due date (SAT-D129).</summary>
public sealed record SalesInvoiceInstalment(int SequenceNo, DateOnly DueDate, string SharePercent, Money Amount);

/// <summary>A stored invoice line: the share, the frozen amounts (invoice currency) and the TL amounts of a foreign-currency invoice.</summary>
public sealed class SalesInvoiceLine
{
    internal SalesInvoiceLine(int lineNo, SalesInvoiceShareInput share, decimal netUnitPrice, decimal vatPercent, decimal? withholdingShare,
        SalesInvoiceAmountSet amounts, SalesInvoiceFunctionalLine? functional)
    {
        LineNo = lineNo >= 1 ? lineNo : throw new ArgumentOutOfRangeException(nameof(lineNo));
        Share = share;
        NetUnitPrice = netUnitPrice;
        VatPercent = vatPercent;
        WithholdingShare = withholdingShare;
        Amounts = amounts;
        Functional = functional;
    }

    public Guid Id => Share.LineId;

    public int LineNo { get; }

    public SalesInvoiceShareInput Share { get; }

    public decimal NetUnitPrice { get; }

    public decimal VatPercent { get; }

    public decimal? WithholdingShare { get; }

    public SalesInvoiceAmountSet Amounts { get; }

    public SalesInvoiceFunctionalLine? Functional { get; }
}

/// <summary>
/// A sales invoice draft (S5 API-32; SAT-D7 manual flow: the ERP prepares and checks, the official invoice is issued in the
/// external program — there is no "Fatura Kes" here). One company (server-resolved, immutable), one customer, one currency, one
/// rate, one due plan (SAT-D101), 1–500 whole shipment shares, each at most once on an active invoice (AT-055, SAT-D94). Amounts
/// come from <see cref="SalesInvoiceCalculator"/> once, at creation, and are frozen; the instalments split the payable amount
/// (SAT-D124). The document number is a technical display identifier (Ö8), never the official invoice number.
/// </summary>
public sealed partial class SalesInvoice
{
    public const int MaximumLines = SalesOrderDraft.MaximumLines;
    public const int MaximumDocumentNoLength = SalesOrderDraft.MaximumDocumentNoLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;
    public const int MaximumDispatchNumberLength = 64;
    public const int MaximumReasonLength = SalesOrderDraft.MaximumRejectionReasonLength;

    private readonly List<SalesInvoiceLine> lines = [];
    private readonly List<SalesInvoiceInstalment> instalments = [];

    private SalesInvoice(Guid id, Guid companyId, string documentNo, SalesInvoiceHeader header, string createdBy, DateTimeOffset createdAtUtc,
        long version)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        DocumentNo = RequireText(documentNo, MaximumDocumentNoLength, nameof(documentNo));
        Header = header;
        CreatedBy = RequireText(createdBy, MaximumActorLength, nameof(createdBy));
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Version = version >= 1 ? version : throw new ArgumentOutOfRangeException(nameof(version));
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public string DocumentNo { get; }

    public SalesInvoiceHeader Header { get; }

    public SalesInvoiceState State { get; private set; } = SalesInvoiceState.Draft;

    /// <summary>Optimistic concurrency: 1 at creation; every later command (API-33…36) advances it by one.</summary>
    public long Version { get; private set; }

    public SalesInvoiceDecisionFacts Decision { get; private set; } = SalesInvoiceDecisionFacts.None;

    /// <summary>
    /// True when the shares are held by this invoice (AT-055, SAT-D94): a draft, a pending or an approved invoice. A rejected or
    /// cancelled invoice has released them (the store stamps the line's <c>ReleasedAtUtc</c> in the same save).
    /// </summary>
    public bool HoldsShares => State is SalesInvoiceState.Draft or SalesInvoiceState.PendingApproval or SalesInvoiceState.Approved;

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<SalesInvoiceLine> Lines => lines.AsReadOnly();

    public IReadOnlyList<SalesInvoiceInstalment> Instalments => instalments.AsReadOnly();

    /// <summary>The invoice-currency totals: exact sums of the frozen lines (never re-rounded).</summary>
    public SalesInvoiceAmountSet Totals => SalesInvoiceCalculator.Sum([.. lines.Select(line => line.Amounts)], Header.CurrencyId,
        Header.AmountScale);

    /// <summary>The TL totals of a foreign-currency invoice (sums of the lines = sums of the VAT groups, SAT-D223); null for TL.</summary>
    public SalesInvoiceAmountSet? FunctionalTotals => Header.Rate is { } rate
        ? SalesInvoiceCalculator.Sum([.. lines.Select(line => line.Functional!.Amounts)], rate.FunctionalCurrencyId, rate.FunctionalScale)
        : null;

    /// <summary>
    /// Records a draft. The shares are checked here (unique line and shipment ids, positive quantities, a known dispatch number);
    /// the amounts are the calculator's (a share without a price, a price in another unit or a tax without a source is refused,
    /// never a zero amount); the due plan's instalments split the payable amount (SAT-D124; a plan the amount cannot be split into
    /// is refused, SAT-D157).
    /// </summary>
    public static SalesInvoice Create(Guid id, Guid companyId, string documentNo, SalesInvoiceHeader header,
        IReadOnlyList<SalesInvoiceShareInput> shares, IReadOnlyList<SalesDueDate> dueDates, SalesDueTerms dueTerms, string createdBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(dueDates);
        ArgumentNullException.ThrowIfNull(dueTerms);
        RequireHeader(header);
        if (shares.Count == 0)
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceWithoutLines, "Satırsız fatura taslağı kaydedilmez.");
        }

        if (shares.Count > MaximumLines)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManyLines, $"Bir fatura taslağı en fazla {MaximumLines} satır taşır.");
        }

        var lineIds = new HashSet<Guid>();
        var shipments = new HashSet<Guid>();
        foreach (var share in shares)
        {
            ArgumentNullException.ThrowIfNull(share);
            RequireShare(share);
            if (!lineIds.Add(share.LineId))
            {
                throw new SalesRuleException(SalesRuleCodes.DuplicateLineId, "Aynı satır kimliği ikinci kez verilemez.");
            }

            if (!shipments.Add(share.ShipmentId))
            {
                throw new SalesRuleException(SalesRuleCodes.InvoiceShareDuplicated, "Aynı sevk payı bir faturada iki kez yer alamaz.");
            }
        }

        if (dueDates.Count != dueTerms.Instalments.Count || dueDates.Any(date => date is null || !date.IsResolved)
            || dueDates.Select(date => date.SequenceNo).SequenceEqual(dueTerms.Instalments.Select(instalment => instalment.SequenceNo)) is false)
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceDueDateMissing, "Faturanın her taksitinin vade tarihi bilinmelidir.");
        }

        var conversion = header.Rate is { } rate
            ? new SalesInvoiceFunctionalConversion(ExchangeRate.Of(header.CurrencyId, rate.FunctionalCurrencyId, rate.Rate), rate.FunctionalScale)
            : null;
        var inputs = shares.Select((share, index) => new SalesInvoiceLineInput(share.LineId, index + 1, share.UnitId, share.Quantity,
            share.Commercial, share.Tax)).ToList();
        var amounts = SalesInvoiceCalculator.Calculate(inputs, header.CurrencyId, header.AmountScale, conversion);
        if (!amounts.IsComputed)
        {
            throw amounts.State switch
            {
                SalesInvoiceAmountsState.TaxUnavailable => new SalesRuleException(SalesRuleCodes.InvoiceTaxUnavailable,
                    "Bir satırın vergisi kaynak bekliyor; fatura taslağı kaydedilmez."),
                SalesInvoiceAmountsState.AmountOutOfRange => new SalesRuleException(SalesRuleCodes.InvoiceAmountOutOfRange,
                    "Fatura tutarı tam olarak tutulamıyor; taslak kaydedilmez."),
                _ => new SalesRuleException(SalesRuleCodes.InvoiceLineUnpriced,
                    "Bir satırın fiyatı yok ya da sipariş biriminde değil; fatura taslağı kaydedilmez."),
            };
        }

        var invoice = new SalesInvoice(id, companyId, documentNo, header, createdBy, now, version: 1);
        var byLine = amounts.Lines.ToDictionary(line => line.LineId);
        for (var index = 0; index < shares.Count; index++)
        {
            var computed = byLine[shares[index].LineId];
            invoice.lines.Add(new SalesInvoiceLine(index + 1, shares[index], computed.NetUnitPrice, computed.VatPercent,
                computed.WithholdingShare, computed.Amounts, computed.Functional));
        }

        var split = SalesDueDateCalculator.SplitAmount(dueTerms, amounts.Totals!.Payable);
        for (var index = 0; index < split.Count; index++)
        {
            invoice.instalments.Add(new SalesInvoiceInstalment(split[index].SequenceNo, dueDates[index].DueDate!.Value,
                dueDates[index].SharePercent, split[index].Amount));
        }

        return invoice;
    }

    /// <summary>Sends the draft for internal approval: <c>Draft → PendingApproval</c> (the submitter is recorded).</summary>
    public void Submit(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireState(SalesInvoiceState.Draft, "Yalnız taslak fatura onaya gönderilir.");
        State = SalesInvoiceState.PendingApproval;
        Decision = Decision with { SubmittedBy = actor, SubmittedAtUtc = now.ToUniversalTime() };
        Advance();
    }

    /// <summary>
    /// Approves a pending invoice: <c>PendingApproval → Approved</c>. The approver is never the preparer or the submitter
    /// (SAT-D88/D204, <see cref="SalesRuleCodes.InvoiceSelfApproval"/>); the permission fence is the service's. Nothing else is
    /// produced here — the official match (API-35) and the finalisation (API-36) are later slices.
    /// </summary>
    public void Approve(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireState(SalesInvoiceState.PendingApproval, "Yalnız onay bekleyen fatura onaylanır.");
        RequireNotPreparer(actor);
        State = SalesInvoiceState.Approved;
        Decision = Decision with { DecidedBy = actor, DecidedAtUtc = now.ToUniversalTime(), DecisionReason = null };
        Advance();
    }

    /// <summary>
    /// Rejects a pending invoice: <c>PendingApproval → Rejected</c> with an optional free-text reason. Rejected is terminal and
    /// releases the shares; the correction is a new draft. The decider is never the preparer or the submitter.
    /// </summary>
    public void Reject(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        var text = RequireReason(reason);
        RequireState(SalesInvoiceState.PendingApproval, "Yalnız onay bekleyen fatura reddedilir.");
        RequireNotPreparer(actor);
        State = SalesInvoiceState.Rejected;
        Decision = Decision with { DecidedBy = actor, DecidedAtUtc = now.ToUniversalTime(), DecisionReason = text };
        Advance();
    }

    /// <summary>
    /// Cancels an invoice before its approval: <c>Draft | PendingApproval → Cancelled</c> (terminal; the shares are released).
    /// An approved invoice is not cancelled here: its official reverse record, the allocated-payment warning (SAT-D96) and the
    /// e-invoice warning belong to the later slice that has the Finance allocation port.
    /// </summary>
    public void Cancel(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        var text = RequireReason(reason);
        if (State is not (SalesInvoiceState.Draft or SalesInvoiceState.PendingApproval))
        {
            throw StateConflict("Yalnız onaya girmemiş ya da onay bekleyen fatura iptal edilir.");
        }

        State = SalesInvoiceState.Cancelled;
        Decision = Decision with { CancelledBy = actor, CancelledAtUtc = now.ToUniversalTime(), CancellationReason = text };
        Advance();
    }

    /// <summary>
    /// Rebuilds a stored invoice from its rows: the frozen amounts are taken as stored (never recomputed), but every line must be
    /// internally consistent (gross = net + VAT, payable = gross − withholding, TL present exactly on a foreign-currency invoice)
    /// and the instalments must add up to the payable total. A corrupt row is refused, never repaired.
    /// </summary>
    public static SalesInvoice Restore(Guid id, Guid companyId, string documentNo, SalesInvoiceHeader header, SalesInvoiceState state,
        long version, string createdBy, DateTimeOffset createdAtUtc, IReadOnlyList<SalesInvoiceLine> storedLines,
        IReadOnlyList<SalesInvoiceInstalment> storedInstalments, SalesInvoiceDecisionFacts? decision = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(storedLines);
        ArgumentNullException.ThrowIfNull(storedInstalments);
        var facts = decision ?? SalesInvoiceDecisionFacts.None;
        var invoice = new SalesInvoice(id, companyId, documentNo, header, createdBy, createdAtUtc, version) { State = state, Decision = facts };
        if (!Enum.IsDefined(state) || storedLines.Count is 0 or > MaximumLines || storedInstalments.Count == 0
            || !DecisionIsConsistent(state, createdBy, facts))
        {
            throw new InvalidOperationException("Kayıtlı fatura satırları, taksitleri ya da karar bilgisi okunamadı.");
        }

        for (var index = 0; index < storedLines.Count; index++)
        {
            var line = storedLines[index];
            if (line.LineNo != index + 1 || line.Amounts.Net.CurrencyId != header.CurrencyId
                || (header.Rate is null) != (line.Functional is null)
                || SalesInvoiceCalculator.Set(line.Amounts.Net, line.Amounts.Vat, line.Amounts.Withholding) != line.Amounts)
            {
                throw new InvalidOperationException("Kayıtlı fatura satırı tutarları birbirini tutmuyor.");
            }

            invoice.lines.Add(line);
        }

        invoice.instalments.AddRange(storedInstalments);
        var payable = invoice.Totals.Payable;
        if (storedInstalments.Aggregate(Money.Zero(payable.CurrencyId, payable.Scale), (sum, item) => sum.Add(item.Amount)) != payable)
        {
            throw new InvalidOperationException("Kayıtlı fatura taksitleri ödenecek tutarı tutmuyor.");
        }

        return invoice;
    }

    /// <summary>Builds a stored line (the store's restore path); the amounts are checked by <see cref="Restore"/>.</summary>
    public static SalesInvoiceLine StoredLine(int lineNo, SalesInvoiceShareInput share, decimal netUnitPrice, decimal vatPercent,
        decimal? withholdingShare, SalesInvoiceAmountSet amounts, SalesInvoiceFunctionalLine? functional)
    {
        RequireShare(share);
        return new SalesInvoiceLine(lineNo, share, netUnitPrice, vatPercent, withholdingShare, amounts, functional);
    }

    private static bool DecisionIsConsistent(SalesInvoiceState state, string createdBy, SalesInvoiceDecisionFacts facts)
    {
        var submitted = facts.SubmittedBy is not null && facts.SubmittedAtUtc is not null;
        var submittedNone = facts.SubmittedBy is null && facts.SubmittedAtUtc is null;
        var decided = facts.DecidedBy is not null && facts.DecidedAtUtc is not null;
        var decidedNone = facts.DecidedBy is null && facts.DecidedAtUtc is null && facts.DecisionReason is null;
        var cancelled = facts.CancelledBy is not null && facts.CancelledAtUtc is not null;
        var cancelledNone = facts.CancelledBy is null && facts.CancelledAtUtc is null && facts.CancellationReason is null;
        var decidedByOther = !decided || (!string.Equals(facts.DecidedBy, createdBy, StringComparison.Ordinal)
            && !string.Equals(facts.DecidedBy, facts.SubmittedBy, StringComparison.Ordinal));
        return state switch
        {
            SalesInvoiceState.Draft => submittedNone && decidedNone && cancelledNone,
            SalesInvoiceState.PendingApproval => submitted && decidedNone && cancelledNone,
            SalesInvoiceState.Approved => submitted && decided && facts.DecisionReason is null && cancelledNone && decidedByOther,
            SalesInvoiceState.Rejected => submitted && decided && cancelledNone && decidedByOther,
            SalesInvoiceState.Cancelled => cancelled && decidedNone,
            _ => false,
        };
    }

    private void RequireState(SalesInvoiceState expected, string message)
    {
        if (State != expected)
        {
            throw StateConflict(message);
        }
    }

    private void RequireNotPreparer(string actor)
    {
        if (string.Equals(actor, CreatedBy, StringComparison.Ordinal)
            || string.Equals(actor, Decision.SubmittedBy, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceSelfApproval,
                "Faturayı hazırlayan ya da onaya gönderen kişi aynı faturaya karar veremez; başkası karar verir.");
        }
    }

    private static string? RequireReason(string? reason)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return text is { Length: > MaximumReasonLength }
            ? throw new SalesRuleException(SalesRuleCodes.ApprovalReasonTooLong, $"Neden en fazla {MaximumReasonLength} karakterdir.")
            : text;
    }

    private static string RequireActor(string actorId) => RequireText(actorId, MaximumActorLength, nameof(actorId));

    private SalesRuleException StateConflict(string message) => new(SalesRuleCodes.StateConflict, $"{message} (durum: {State}).");

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new SalesVersionConflictException(expectedVersion, Version);
        }
    }

    private void Advance() => Version = checked(Version + 1);

    private static void RequireHeader(SalesInvoiceHeader header)
    {
        Identity.Require(header.PartyId, nameof(header.PartyId));
        Identity.Require(header.CompanyAccountId, nameof(header.CompanyAccountId));
        Identity.Require(header.CurrencyId, nameof(header.CurrencyId));
        Identity.Require(header.MoneyProfileId, nameof(header.MoneyProfileId));
        RequireText(header.MoneyProfileVersion, 128, nameof(header.MoneyProfileVersion));
        if (header.AmountScale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(header), "Tutar ölçeği geçersiz.");
        }

        if (header.Rate is { } rate && (rate.Rate <= 0m || rate.FunctionalCurrencyId == Guid.Empty
            || rate.FunctionalCurrencyId == header.CurrencyId || rate.FunctionalScale is < 0 or > Money.MaximumScale))
        {
            throw new ArgumentException("Fatura kuru geçersiz.", nameof(header));
        }
    }

    private static void RequireShare(SalesInvoiceShareInput share)
    {
        Identity.Require(share.LineId, nameof(share.LineId));
        Identity.Require(share.ShipmentId, nameof(share.ShipmentId));
        Identity.Require(share.DispatchNoteId, nameof(share.DispatchNoteId));
        Identity.Require(share.SalesOrderId, nameof(share.SalesOrderId));
        Identity.Require(share.SalesOrderLineId, nameof(share.SalesOrderLineId));
        Identity.Require(share.ProductId, nameof(share.ProductId));
        Identity.Require(share.UnitId, nameof(share.UnitId));
        RequireText(share.DispatchNoteNumber, MaximumDispatchNumberLength, nameof(share.DispatchNoteNumber));
        RequireText(share.QuantityText, 128, nameof(share.QuantityText));
        ArgumentNullException.ThrowIfNull(share.Commercial);
        ArgumentNullException.ThrowIfNull(share.Tax);
        if (share.Quantity <= 0m || share.NoteLineNo < 1 || share.PriceBasisRevision < 0)
        {
            throw new ArgumentException("Sevk payının miktarı, irsaliye satırı ya da revizyonu geçersiz.", nameof(share));
        }
    }

    private static string RequireText(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        var text = value.Trim();
        return text.Length <= maximumLength
            ? text
            : throw new ArgumentException($"Değer en fazla {maximumLength} karakterdir.", parameterName);
    }
}
