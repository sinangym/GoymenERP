using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Domain.Invoices;

/// <summary>
/// The lifecycle of a subcontract service invoice draft (F2). This slice records <see cref="Draft"/> only; the internal approval,
/// the official match and the finalisation of a service invoice join the invoice flow in a later slice — never a silent jump.
/// </summary>
public enum SalesSubcontractServiceInvoiceState
{
    Draft = 1,
}

/// <summary>
/// The header of a subcontract service invoice: one customer (party + company account) of the subcontract service order, the
/// order's currency at its money profile's scale, and the server business day the draft was prepared on (SAT-D86: the official
/// invoice date comes with the match).
/// </summary>
public sealed record SalesSubcontractServiceInvoiceHeader(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    Guid MoneyProfileId,
    string MoneyProfileVersion,
    int AmountScale,
    DateOnly BusinessDate);

/// <summary>
/// The delivery weighing the invoice is keyed by (F2 "separate key": loading instruction + weighing + subcontract order; LOJ-D9,
/// SAT-D309): the weighing revision and instruction version the Logistics owner answered, and its net kg text verbatim.
/// </summary>
public sealed record SalesSubcontractServiceInvoiceSource(
    Guid SalesOrderId,
    Guid LoadingInstructionId,
    Guid WeighingId,
    int WeighingRevision,
    long InstructionVersion,
    string NetKgText);

/// <summary>
/// One service line input (LOJ-D4: the invoice carries a service line only): the subcontract order line, the price basis revision
/// with its commercial fields (the kg price, SAT-D195) and tax snapshot, and the billed quantity — the delivery weighing's net kg
/// (SAT-D309/LOJ-D9; scrap is not billed, so nothing is deducted from it).
/// </summary>
public sealed record SalesSubcontractServiceLineInput(
    Guid LineId,
    Guid SalesOrderLineId,
    int PriceBasisRevision,
    Guid ProductId,
    Guid UnitId,
    decimal Quantity,
    string QuantityText,
    CommercialDraftFields Commercial,
    SalesLineTaxSnapshot Tax);

/// <summary>A stored service line: the input and the calculator's frozen amounts.</summary>
public sealed class SalesSubcontractServiceInvoiceLine
{
    internal SalesSubcontractServiceInvoiceLine(int lineNo, SalesSubcontractServiceLineInput input, decimal netUnitPrice,
        decimal vatPercent, decimal? withholdingShare, SalesInvoiceAmountSet amounts)
    {
        LineNo = lineNo >= 1 ? lineNo : throw new ArgumentOutOfRangeException(nameof(lineNo));
        Input = input;
        NetUnitPrice = netUnitPrice;
        VatPercent = vatPercent;
        WithholdingShare = withholdingShare;
        Amounts = amounts;
    }

    public Guid Id => Input.LineId;

    public int LineNo { get; }

    public SalesSubcontractServiceLineInput Input { get; }

    public decimal NetUnitPrice { get; }

    public decimal VatPercent { get; }

    public decimal? WithholdingShare { get; }

    public SalesInvoiceAmountSet Amounts { get; }
}

/// <summary>
/// F2 — a service invoice DRAFT from a subcontract service order (#66 m.4, URT-D16, SAT-D195, SAT-D309/D312, LOJ-D4/D9/D12): the
/// amount is the delivery weighing's net kg × the line's approved kg price, from <see cref="SalesInvoiceCalculator"/> once, at
/// creation, and frozen (the screen computes nothing). One company (server-resolved, immutable), one customer, one currency,
/// one delivery weighing of one loading instruction for one subcontract order (the store's unique index refuses a second invoice
/// of the same key). The document number is a technical display identifier (Ö8), never the official invoice number.
/// </summary>
public sealed class SalesSubcontractServiceInvoice
{
    public const int MaximumLines = SalesOrderDraft.MaximumLines;
    public const int MaximumDocumentNoLength = SalesOrderDraft.MaximumDocumentNoLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;
    public const int MaximumQuantityTextLength = 128;
    public const int MaximumMoneyProfileVersionLength = 128;

    private readonly List<SalesSubcontractServiceInvoiceLine> lines = [];

    private SalesSubcontractServiceInvoice(Guid id, Guid companyId, string documentNo, SalesSubcontractServiceInvoiceHeader header,
        SalesSubcontractServiceInvoiceSource source, string createdBy, DateTimeOffset createdAtUtc, long version)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        DocumentNo = RequireText(documentNo, MaximumDocumentNoLength, nameof(documentNo));
        Header = header;
        Source = source;
        CreatedBy = RequireText(createdBy, MaximumActorLength, nameof(createdBy));
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Version = version >= 1 ? version : throw new ArgumentOutOfRangeException(nameof(version));
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public string DocumentNo { get; }

    public SalesSubcontractServiceInvoiceHeader Header { get; }

    public SalesSubcontractServiceInvoiceSource Source { get; }

    public SalesSubcontractServiceInvoiceState State { get; private set; } = SalesSubcontractServiceInvoiceState.Draft;

    /// <summary>Optimistic concurrency: 1 at creation.</summary>
    public long Version { get; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public IReadOnlyList<SalesSubcontractServiceInvoiceLine> Lines => lines.AsReadOnly();

    /// <summary>The totals: exact sums of the frozen lines (never re-rounded).</summary>
    public SalesInvoiceAmountSet Totals => SalesInvoiceCalculator.Sum([.. lines.Select(line => line.Amounts)], Header.CurrencyId,
        Header.AmountScale);

    /// <summary>
    /// Records a draft. The lines are checked here (unique line and order line ids, positive quantities, kg-priced); the amounts
    /// are the calculator's (a line without a price, a price in another unit or a tax without a source is refused, never a zero
    /// amount). The billed quantities must add up to the weighing's net kg exactly: the delivery weighing is billed whole, never
    /// partly and never twice (SAT-D309).
    /// </summary>
    public static SalesSubcontractServiceInvoice Create(Guid id, Guid companyId, string documentNo,
        SalesSubcontractServiceInvoiceHeader header, SalesSubcontractServiceInvoiceSource source,
        IReadOnlyList<SalesSubcontractServiceLineInput> inputs, decimal netKg, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(inputs);
        RequireHeader(header);
        RequireSource(source);
        if (inputs.Count == 0)
        {
            throw new SalesRuleException(SalesRuleCodes.InvoiceWithoutLines, "Satırsız fason hizmet faturası taslağı kaydedilmez.");
        }

        if (inputs.Count > MaximumLines)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManyLines, $"Bir fatura taslağı en fazla {MaximumLines} satır taşır.");
        }

        var lineIds = new HashSet<Guid>();
        var orderLines = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            RequireLine(input);
            if (!lineIds.Add(input.LineId) || !orderLines.Add(input.SalesOrderLineId))
            {
                throw new SalesRuleException(SalesRuleCodes.DuplicateLineId, "Aynı satır kimliği ikinci kez verilemez.");
            }
        }

        if (netKg <= 0m || inputs.Sum(input => input.Quantity) != netKg)
        {
            throw new ArgumentException("Fason hizmet satırlarının kg toplamı teslim tartımının net kg'ına eşit olmalıdır.", nameof(inputs));
        }

        var calculatorInputs = inputs.Select((input, index) => new SalesInvoiceLineInput(input.LineId, index + 1, input.UnitId,
            input.Quantity, input.Commercial, input.Tax)).ToList();
        var amounts = SalesInvoiceCalculator.Calculate(calculatorInputs, header.CurrencyId, header.AmountScale, conversion: null);
        if (!amounts.IsComputed)
        {
            throw amounts.State switch
            {
                SalesInvoiceAmountsState.TaxUnavailable => new SalesRuleException(SalesRuleCodes.InvoiceTaxUnavailable,
                    "Bir satırın vergisi kaynak bekliyor; fason hizmet faturası taslağı kaydedilmez."),
                SalesInvoiceAmountsState.AmountOutOfRange => new SalesRuleException(SalesRuleCodes.InvoiceAmountOutOfRange,
                    "Fatura tutarı tam olarak tutulamıyor; taslak kaydedilmez."),
                _ => new SalesRuleException(SalesRuleCodes.InvoiceLineUnpriced,
                    "Bir satırın kg fiyatı yok ya da sipariş biriminde değil; fason hizmet faturası taslağı kaydedilmez."),
            };
        }

        var invoice = new SalesSubcontractServiceInvoice(id, companyId, documentNo, header, source, createdBy, now, version: 1);
        var byLine = amounts.Lines.ToDictionary(line => line.LineId);
        for (var index = 0; index < inputs.Count; index++)
        {
            var computed = byLine[inputs[index].LineId];
            invoice.lines.Add(new SalesSubcontractServiceInvoiceLine(index + 1, inputs[index], computed.NetUnitPrice,
                computed.VatPercent, computed.WithholdingShare, computed.Amounts));
        }

        return invoice;
    }

    /// <summary>
    /// Rebuilds a stored invoice from its rows: the frozen amounts are taken as stored (never recomputed), but every line must be
    /// internally consistent (gross = net + VAT, payable = gross − withholding) and in the invoice currency. A corrupt row is
    /// refused, never repaired.
    /// </summary>
    public static SalesSubcontractServiceInvoice Restore(Guid id, Guid companyId, string documentNo,
        SalesSubcontractServiceInvoiceHeader header, SalesSubcontractServiceInvoiceSource source, SalesSubcontractServiceInvoiceState state,
        long version, string createdBy, DateTimeOffset createdAtUtc, IReadOnlyList<SalesSubcontractServiceInvoiceLine> storedLines)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(storedLines);
        RequireHeader(header);
        RequireSource(source);
        var invoice = new SalesSubcontractServiceInvoice(id, companyId, documentNo, header, source, createdBy, createdAtUtc, version)
        {
            State = state,
        };
        if (!Enum.IsDefined(state) || storedLines.Count is 0 or > MaximumLines)
        {
            throw new InvalidOperationException("Kayıtlı fason hizmet faturası satırları okunamadı.");
        }

        for (var index = 0; index < storedLines.Count; index++)
        {
            var line = storedLines[index];
            if (line.LineNo != index + 1 || line.Amounts.Net.CurrencyId != header.CurrencyId
                || SalesInvoiceCalculator.Set(line.Amounts.Net, line.Amounts.Vat, line.Amounts.Withholding) != line.Amounts)
            {
                throw new InvalidOperationException("Kayıtlı fason hizmet faturası satırı tutarları birbirini tutmuyor.");
            }

            invoice.lines.Add(line);
        }

        return invoice;
    }

    /// <summary>Builds a stored line (the store's restore path); the amounts are checked by <see cref="Restore"/>.</summary>
    public static SalesSubcontractServiceInvoiceLine StoredLine(int lineNo, SalesSubcontractServiceLineInput input, decimal netUnitPrice,
        decimal vatPercent, decimal? withholdingShare, SalesInvoiceAmountSet amounts)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(amounts);
        RequireLine(input);
        return new SalesSubcontractServiceInvoiceLine(lineNo, input, netUnitPrice, vatPercent, withholdingShare, amounts);
    }

    private static void RequireHeader(SalesSubcontractServiceInvoiceHeader header)
    {
        Identity.Require(header.PartyId, nameof(header.PartyId));
        Identity.Require(header.CompanyAccountId, nameof(header.CompanyAccountId));
        Identity.Require(header.CurrencyId, nameof(header.CurrencyId));
        Identity.Require(header.MoneyProfileId, nameof(header.MoneyProfileId));
        RequireText(header.MoneyProfileVersion, MaximumMoneyProfileVersionLength, nameof(header.MoneyProfileVersion));
        if (header.AmountScale is < 0 or > Money.MaximumScale)
        {
            throw new ArgumentOutOfRangeException(nameof(header), "Tutar ölçeği geçersiz.");
        }
    }

    private static void RequireSource(SalesSubcontractServiceInvoiceSource source)
    {
        Identity.Require(source.SalesOrderId, nameof(source.SalesOrderId));
        Identity.Require(source.LoadingInstructionId, nameof(source.LoadingInstructionId));
        Identity.Require(source.WeighingId, nameof(source.WeighingId));
        RequireText(source.NetKgText, MaximumQuantityTextLength, nameof(source.NetKgText));
        if (source.WeighingRevision < 1 || source.InstructionVersion < 1)
        {
            throw new ArgumentException("Teslim tartımının revizyonu ya da talimat sürümü geçersiz.", nameof(source));
        }
    }

    private static void RequireLine(SalesSubcontractServiceLineInput input)
    {
        Identity.Require(input.LineId, nameof(input.LineId));
        Identity.Require(input.SalesOrderLineId, nameof(input.SalesOrderLineId));
        Identity.Require(input.ProductId, nameof(input.ProductId));
        Identity.Require(input.UnitId, nameof(input.UnitId));
        RequireText(input.QuantityText, MaximumQuantityTextLength, nameof(input.QuantityText));
        ArgumentNullException.ThrowIfNull(input.Commercial);
        ArgumentNullException.ThrowIfNull(input.Tax);
        if (input.Quantity <= 0m || input.PriceBasisRevision < 0)
        {
            throw new ArgumentException("Fason hizmet satırının miktarı ya da revizyonu geçersiz.", nameof(input));
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
