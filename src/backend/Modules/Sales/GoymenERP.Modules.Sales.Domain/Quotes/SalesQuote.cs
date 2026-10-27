using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Quotes;

/// <summary>
/// The lifecycle of a sales quote (plan §3.2 API-22…24; delegation No 162, 163): <see cref="Open"/> while it is offered and
/// may be revised; <see cref="Converted"/> once its order exists (No 162: one order per quote, terminal); <see cref="Lost"/>
/// once the loss is recorded with its reason (master §7.2 "Kayıp/fırsat analizi"; terminal). Nothing is deleted.
/// </summary>
public enum SalesQuoteState
{
    Open = 1,
    Converted = 2,
    Lost = 3,
}

/// <summary>The fixed header of a quote: the customer, the currency its prices are in and the business date its snapshots were taken for.</summary>
public sealed record SalesQuoteHeader(Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate);

/// <summary>
/// The revisable terms of a quote revision: an optional validity date (no default is chosen — none is decided), the requested
/// delivery date and a note. Every revision records its own terms; earlier revisions stay as they were.
/// </summary>
public sealed record SalesQuoteTerms(DateOnly? ValidUntil = null, DateOnly? RequestedDeliveryDate = null, string? Note = null);

/// <summary>
/// One quoted line: the owners' snapshots, the exact quantity, and the quoted price as exact text (unit price + price unit,
/// optional discount percent). No total, VAT or rounding is computed here (SAT-D11 belongs to the backend calculator of
/// the order, API-03).
/// </summary>
public sealed record SalesQuoteLineInput(
    Guid LineId,
    ProductSnapshot Product,
    UnitProfileSnapshot OrderUnit,
    string QuantityText,
    decimal Quantity,
    string? UnitPrice = null,
    Guid? PriceUnitId = null,
    string? DiscountPercent = null,
    DateOnly? RequestedDeliveryDate = null,
    string? Note = null);

/// <summary>One line of the quote's current revision.</summary>
public sealed class SalesQuoteLine
{
    internal SalesQuoteLine(int lineNo, SalesQuoteLineInput input, Quantity quantity)
    {
        Id = input.LineId;
        LineNo = lineNo;
        Product = input.Product;
        OrderUnit = input.OrderUnit;
        Quantity = quantity;
        QuantityText = input.QuantityText;
        UnitPrice = input.UnitPrice;
        PriceUnitId = input.PriceUnitId;
        DiscountPercent = input.DiscountPercent;
        RequestedDeliveryDate = input.RequestedDeliveryDate;
        Note = input.Note;
    }

    public Guid Id { get; }

    public int LineNo { get; }

    public ProductSnapshot Product { get; }

    public UnitProfileSnapshot OrderUnit { get; }

    public Quantity Quantity { get; }

    public string QuantityText { get; }

    public string? UnitPrice { get; }

    public Guid? PriceUnitId { get; }

    public string? DiscountPercent { get; }

    public DateOnly? RequestedDeliveryDate { get; }

    public string? Note { get; }
}

/// <summary>The recorded loss: the free-text reason (no reason list is decided), who recorded it and when.</summary>
public sealed record SalesQuoteLoss(string Reason, string LostBy, DateTimeOffset LostAtUtc);

/// <summary>The recorded conversion: the one order the quote became (No 162), who converted it and when.</summary>
public sealed record SalesQuoteConversion(Guid DraftId, string ConvertedBy, DateTimeOffset ConvertedAtUtc);

/// <summary>
/// A sales quote (S-01 "Teklif"; plan §3.2 API-22…24): one company, one customer, one currency, one business date; 1–500
/// priced product lines under the order draft's own line rules (owner snapshots, Ö2) and the order's commercial shape
/// (unit price + price unit among the product's three unit bindings, scale within the money profile, discount 0..100 and
/// only with a price). No 163: it may cover several requests of the SAME customer (the caller checks the requests).
/// <list type="bullet">
/// <item><b>Revision:</b> <see cref="Revise"/> replaces the lines and terms of an open quote as the next revision
/// (Rev.0, Rev.1, …); the earlier revisions are kept by the store, never rewritten.</item>
/// <item><b>Loss:</b> <see cref="RecordLoss"/> closes an open quote with its reason.</item>
/// <item><b>Conversion (No 162):</b> <see cref="Convert"/> records the one order the quote became; the order itself is built
/// by the draft's own path with the quote's price/quantity snapshot.</item>
/// <item><b>Version:</b> the concurrency token; 1 at creation, +1 per recorded revision, loss or conversion.</item>
/// </list>
/// </summary>
public sealed class SalesQuote
{
    public const int MaximumLines = SalesOrderDraft.MaximumLines;
    public const int MaximumRequests = 100;
    public const int MaximumNoteLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumReasonLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumDocumentNoLength = SalesOrderDraft.MaximumDocumentNoLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;

    private readonly List<SalesQuoteLine> lines = [];
    private readonly List<Guid> requestIds = [];
    private bool restored;

    private SalesQuote(Guid id, Guid companyId, string documentNo, SalesQuoteHeader header, MoneyProfileSnapshot money,
        string createdBy, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        DocumentNo = RequireText(documentNo, MaximumDocumentNoLength, nameof(documentNo));
        Header = header;
        Money = money;
        CreatedBy = RequireText(createdBy, MaximumActorLength, nameof(createdBy));
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    /// <summary>Technical, company-scoped display identifier; not an official document number (Ö8).</summary>
    public string DocumentNo { get; }

    public SalesQuoteHeader Header { get; }

    /// <summary>The Settings money profile of the quote's currency on its business date, asked for the order operation: the prices become the order's (No 162).</summary>
    public MoneyProfileSnapshot Money { get; }

    /// <summary>No 163: the requests (of this customer) the quote covers; fixed at creation.</summary>
    public IReadOnlyList<Guid> RequestIds => requestIds.AsReadOnly();

    public SalesQuoteState State { get; private set; } = SalesQuoteState.Open;

    /// <summary>The current revision number (Rev.N); 0 at creation, +1 per <see cref="Revise"/>.</summary>
    public int Revision { get; private set; }

    public SalesQuoteTerms Terms { get; private set; } = new();

    public long Version { get; private set; } = 1;

    public SalesQuoteLoss? Loss { get; private set; }

    public SalesQuoteConversion? Conversion { get; private set; }

    public string CreatedBy { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>The lines of the current revision, in order.</summary>
    public IReadOnlyList<SalesQuoteLine> Lines => lines.AsReadOnly();

    public static SalesQuote Create(Guid id, Guid companyId, string documentNo, SalesQuoteHeader header, MoneyProfileSnapshot money,
        IReadOnlyList<Guid> requests, SalesQuoteTerms terms, IReadOnlyList<SalesQuoteLineInput> inputs, string createdBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(money);
        ArgumentNullException.ThrowIfNull(requests);
        Identity.Require(header.PartyId, nameof(header.PartyId));
        Identity.Require(header.CompanyAccountId, nameof(header.CompanyAccountId));
        Identity.Require(header.CurrencyId, nameof(header.CurrencyId));
        RequireMoney(companyId, header, money);
        if (requests.Count > MaximumRequests || requests.Any(request => request == Guid.Empty) || requests.Distinct().Count() != requests.Count)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteRequestsInvalid, "Teklifin talepleri tekrarsız ve sınır içinde olmalıdır.");
        }

        var quote = new SalesQuote(id, companyId, documentNo, header, money, createdBy, now);
        quote.requestIds.AddRange(requests.Order());
        quote.Apply(terms, inputs);
        return quote;
    }

    /// <summary>Replaces the lines and terms of an OPEN quote as the next revision; a converted or lost quote is a state conflict.</summary>
    public void Revise(long expectedVersion, SalesQuoteTerms terms, IReadOnlyList<SalesQuoteLineInput> inputs, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        RequireOpen("Yalnız açık teklif revize edilir");
        var revised = new List<SalesQuoteLine>(lines);
        var revisedTerms = Terms;
        try
        {
            Apply(terms, inputs);
        }
        catch
        {
            lines.Clear();
            lines.AddRange(revised);
            Terms = revisedTerms;
            throw;
        }

        Revision = checked(Revision + 1);
        Touch(now);
    }

    /// <summary>Master §7.2: records the loss of an OPEN quote with its free-text reason (required, never defaulted or cut).</summary>
    public void RecordLoss(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireText(actorId, MaximumActorLength, nameof(actorId));
        RequireOpen("Yalnız açık teklif kaybedildi olarak kaydedilir");
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteLossReasonRequired, "Teklif kaybı gerekçe ister.");
        }

        if (reason.Length > MaximumReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteLossReasonTooLong,
                $"Kayıp gerekçesi en fazla {MaximumReasonLength} karakterdir; fazlası kesilmez, reddedilir.");
        }

        State = SalesQuoteState.Lost;
        Loss = new SalesQuoteLoss(reason, actor, now.ToUniversalTime());
        Touch(now);
    }

    /// <summary>No 162: records the ONE order an OPEN quote became; a converted or lost quote is a state conflict.</summary>
    public void Convert(long expectedVersion, Guid draftId, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        Identity.Require(draftId, nameof(draftId));
        var actor = RequireText(actorId, MaximumActorLength, nameof(actorId));
        RequireOpen("Yalnız açık teklif siparişe dönüştürülür");
        State = SalesQuoteState.Converted;
        Conversion = new SalesQuoteConversion(draftId, actor, now.ToUniversalTime());
        Touch(now);
    }

    /// <summary>The state the conversion is checked against BEFORE the order is built (the same rules as <see cref="Convert"/>).</summary>
    public void RequireConvertible(long expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireOpen("Yalnız açık teklif siparişe dönüştürülür");
    }

    /// <summary>
    /// Restores the stored lifecycle onto a quote rebuilt through <see cref="Create"/> with its CURRENT revision's terms and
    /// lines: the facts must match the state (Open: none; Lost: the loss only; Converted: the conversion only) and the version
    /// must be at least the recorded steps. A corrupt row is refused, never repaired. Runs once.
    /// </summary>
    public void Restore(SalesQuoteState state, int revision, SalesQuoteLoss? loss, SalesQuoteConversion? conversion, long storedVersion,
        DateTimeOffset updatedAtUtc)
    {
        if (restored || Version != 1 || Revision != 0 || State != SalesQuoteState.Open)
        {
            throw new InvalidOperationException("Teklifin kayıtlı durumu yalnız bir kez ve yeni kurulmuş teklife geri yüklenir.");
        }

        var consistent = state switch
        {
            SalesQuoteState.Open => loss is null && conversion is null,
            SalesQuoteState.Lost => loss is { Reason.Length: > 0 and <= MaximumReasonLength } && conversion is null,
            SalesQuoteState.Converted => loss is null && conversion is { } converted && converted.DraftId != Guid.Empty,
            _ => false,
        };
        var steps = (long)revision + (state == SalesQuoteState.Open ? 0 : 1);
        if (!consistent || revision < 0 || storedVersion < 1 + steps)
        {
            throw new InvalidOperationException("Kayıtlı teklif durumu, revizyonu ya da sürümü kendiyle uyuşmuyor.");
        }

        State = state;
        Revision = revision;
        Loss = loss;
        Conversion = conversion;
        Version = storedVersion;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        restored = true;
    }

    private void Apply(SalesQuoteTerms terms, IReadOnlyList<SalesQuoteLineInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(inputs);
        RequireNote(terms.Note);
        if (terms.ValidUntil is { } validUntil && validUntil < Header.BusinessDate)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteValidUntilBeforeDate, "Geçerlilik tarihi teklif tarihinden önce olamaz.");
        }

        if (inputs.Count == 0)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteWithoutLines, "Satırsız teklif kaydedilmez.");
        }

        if (inputs.Count > MaximumLines)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManyLines, $"Bir teklif en fazla {MaximumLines} satır taşır.");
        }

        var ids = new HashSet<Guid>();
        var built = new List<SalesQuoteLine>(inputs.Count);
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(input.Product);
            ArgumentNullException.ThrowIfNull(input.OrderUnit);
            Identity.Require(input.LineId, nameof(input.LineId));
            if (!ids.Add(input.LineId))
            {
                throw new SalesRuleException(SalesRuleCodes.DuplicateLineId, "Aynı satır kimliği ikinci kez verilemez.");
            }

            RequireNote(input.Note);
            SalesOrderDraft.RequireLineSnapshots(CompanyId, Header.BusinessDate, input.Product, input.OrderUnit);
            var quantity = SalesOrderDraft.RequireQuantity(input.Quantity, input.OrderUnit);
            SalesOrderDraft.RequireQuantityText(input.QuantityText, input.Quantity);
            RequireCommercial(input);
            built.Add(new SalesQuoteLine(built.Count + 1, input, quantity));
        }

        lines.Clear();
        lines.AddRange(built);
        Terms = terms;
    }

    /// <summary>The order line's commercial shape (K3), mathematical validity only; no minimum, margin or discount limit is a rule here.</summary>
    private void RequireCommercial(SalesQuoteLineInput input)
    {
        if ((input.UnitPrice is null) != (input.PriceUnitId is null))
        {
            throw new SalesRuleException(SalesRuleCodes.PriceUnitPairing, "Birim fiyat kendi fiyat birimini ister; fiyat birimi fiyatsız verilemez.", input.LineId);
        }

        if (input.UnitPrice is { } price)
        {
            if (!SalesLinePriceCalculator.TryParseExact(price, out _))
            {
                throw new SalesRuleException(SalesRuleCodes.PriceValueUnrepresentable, "Birim fiyat kayıpsız temsil edilemiyor.", input.LineId);
            }

            if (SalesOrderDraft.DecimalPlaces(price) > Money.Scale)
            {
                throw new SalesRuleException(SalesRuleCodes.PriceScaleExceeded,
                    $"Birim fiyat en fazla {Money.Scale} ondalık taşıyabilir; fazlası yuvarlanmaz, reddedilir.", input.LineId);
            }

            var unit = input.PriceUnitId!.Value;
            if (unit != input.Product.StockUnit.UnitId && unit != input.Product.SalesUnit.UnitId && unit != input.Product.OrderUnit.UnitId)
            {
                throw new SalesRuleException(SalesRuleCodes.PriceUnitUnsupported,
                    "Fiyat birimi, ürün sahibinin stok, satış ya da sipariş birimlerinden biri olmalıdır.", input.LineId);
            }
        }

        if (input.DiscountPercent is { } discount)
        {
            if (input.UnitPrice is null)
            {
                throw new SalesRuleException(SalesRuleCodes.DiscountWithoutPrice, "İskonto bir birim fiyat olmadan verilemez.", input.LineId);
            }

            if (!SalesOrderDraft.IsPercent(discount) || !SalesLinePriceCalculator.TryParseExact(discount, out _))
            {
                throw new SalesRuleException(SalesRuleCodes.DiscountOutOfRange, "İskonto yüzdesi 0 ile 100 arasında olmalıdır.", input.LineId);
            }
        }
    }

    private static void RequireMoney(Guid companyId, SalesQuoteHeader header, MoneyProfileSnapshot money)
    {
        if (money.CompanyId != companyId)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotCompanyMismatch, "Para profili başka şirketin.");
        }

        if (money.CurrencyId != header.CurrencyId || money.ProfileId == Guid.Empty || string.IsNullOrWhiteSpace(money.ProfileVersion)
            || money.Scale < 0 || (money.EffectiveThrough is { } through && through < money.EffectiveFrom))
        {
            throw new ArgumentException("Para profili teklifin para birimini adlandırmalı ve tam olmalıdır.", nameof(money));
        }

        if (money.EffectiveOn != header.BusinessDate)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotDateMismatch, "Para profili teklifin iş tarihi için alınmamış.");
        }

        if (!string.Equals(money.Operation, SalesOrderDraft.MoneyOperation, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotOperationMismatch, "Para profili sipariş operasyonu için alınmamış.");
        }

        if (!money.IsActive || header.BusinessDate < money.EffectiveFrom || (money.EffectiveThrough is { } until && header.BusinessDate > until))
        {
            throw new SalesRuleException(SalesRuleCodes.MoneyProfileNotInEffect, "Para profili iş tarihinde etkin değil.");
        }
    }

    private void RequireOpen(string message)
    {
        if (State != SalesQuoteState.Open)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, $"{message} (durum: {State}).");
        }
    }

    private void RequireVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new SalesVersionConflictException(expectedVersion, Version);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = now.ToUniversalTime();
        Version = checked(Version + 1);
    }

    private static void RequireNote(string? note)
    {
        if (note is not null && note.Length > MaximumNoteLength)
        {
            throw new SalesRuleException(SalesRuleCodes.NoteTooLong, $"Not en fazla {MaximumNoteLength} karakterdir.");
        }
    }

    private static string RequireText(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Değer boş olamaz.", parameterName);
        }

        var text = value.Trim();
        return text.Length <= maximumLength ? text : throw new ArgumentException($"Değer en fazla {maximumLength} karakterdir.", parameterName);
    }
}
