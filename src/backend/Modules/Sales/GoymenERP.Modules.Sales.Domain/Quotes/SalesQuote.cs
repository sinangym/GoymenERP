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

/// <summary>
/// SAT-D25: the entry of the company's closed Settings loss reason list a loss chose, as a snapshot of what was shown — its
/// stable id and code, its label and the Settings list version it came from (DEC-SET-001: the applied version and value are
/// kept; a later list change does not rewrite the quote).
/// </summary>
public sealed record SalesQuoteLossReason(Guid ReasonId, string Code, string Label, Guid ProfileId, long ProfileVersion);

/// <summary>
/// The recorded loss, who recorded it and when. <see cref="ListReason"/> is the SAT-D25 list entry; <see cref="Reason"/> is
/// the free-text explanation (required when the entry asks for one, otherwise optional). A loss recorded before SAT-D25 (the
/// retired v1 command) has no list entry and its free text is its reason.
/// </summary>
public sealed record SalesQuoteLoss(string? Reason, string LostBy, DateTimeOffset LostAtUtc, SalesQuoteLossReason? ListReason = null);

/// <summary>SAT-D44 (1): one recorded revision of the quote — who wrote it (Rev.0: who created the quote), when, and its validity date.</summary>
public sealed record SalesQuoteRevisionRecord(int Revision, DateOnly? ValidUntil, string RevisedBy, DateTimeOffset RevisedAtUtc);

/// <summary>
/// SAT-D24 / SAT-D44: the manager approval that lets an EXPIRED quote be converted — for exactly one revision (its validity
/// date is the one approved), recorded with the server business day it was given on, who gave it, when, and an optional note.
/// A later revision needs its own approval.
/// </summary>
public sealed record SalesQuoteExpiryApproval(int Revision, DateOnly ValidUntil, DateOnly BusinessDay, string ApprovedBy,
    DateTimeOffset ApprovedAtUtc, string? Note);

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
/// <item><b>Loss:</b> <see cref="RecordLoss"/> closes an open quote with its SAT-D25 list reason (and explanation).</item>
/// <item><b>Expiry (SAT-D24 / SAT-D44):</b> a quote whose validity date precedes the SERVER's business day is expired; it is
/// converted only after <see cref="ApproveExpiredConversion"/> recorded an approval for its current revision.</item>
/// <item><b>Conversion (No 162):</b> <see cref="Convert"/> records the one order the quote became; the order itself is built
/// by the draft's own path with the quote's price/quantity snapshot.</item>
/// <item><b>Version:</b> the concurrency token; 1 at creation, +1 per recorded revision, expiry approval, loss or conversion.</item>
/// </list>
/// </summary>
public sealed class SalesQuote
{
    public const int MaximumLines = SalesOrderDraft.MaximumLines;
    /// <summary>
    /// A TECHNICAL bound, not a business policy: No 163 lets a quote cover N requests of one customer and names no limit. The
    /// bound only keeps one command's payload, its per-request owner reads and the SQL <c>IN</c> list of the conversion's
    /// in-save check finite; it is the same 100 as the Sales list page bound (the most requests a client can see on one page
    /// of the request pool to pick from). Raising it is a technical change, not a decision.
    /// </summary>
    public const int MaximumRequests = 100;
    public const int MaximumNoteLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumReasonLength = SalesOrderDraft.MaximumNoteLength;
    public const int MaximumDocumentNoLength = SalesOrderDraft.MaximumDocumentNoLength;
    public const int MaximumActorLength = SalesOrderDraft.MaximumActorLength;

    /// <summary>The stored snapshot bounds of a SAT-D25 list entry (the Settings catalog refuses longer values).</summary>
    public const int MaximumLossReasonCodeLength = 64;
    public const int MaximumLossReasonLabelLength = 200;

    private readonly List<SalesQuoteLine> lines = [];
    private readonly List<Guid> requestIds = [];
    private readonly List<SalesQuoteRevisionRecord> revisions = [];
    private readonly List<SalesQuoteExpiryApproval> expiryApprovals = [];
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

    /// <summary>SAT-D44 (1): every recorded revision (Rev.0 = the creation), in order — who created and who revised the quote.</summary>
    public IReadOnlyList<SalesQuoteRevisionRecord> Revisions => revisions.AsReadOnly();

    /// <summary>SAT-D24 / SAT-D44: every recorded expired-quote approval, in order (at most one per revision).</summary>
    public IReadOnlyList<SalesQuoteExpiryApproval> ExpiryApprovals => expiryApprovals.AsReadOnly();

    /// <summary>The expired-quote approval of the CURRENT revision, if one is recorded.</summary>
    public SalesQuoteExpiryApproval? CurrentExpiryApproval => expiryApprovals.LastOrDefault(approval => approval.Revision == Revision);

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
        quote.revisions.Add(new SalesQuoteRevisionRecord(0, quote.Terms.ValidUntil, quote.CreatedBy, quote.CreatedAtUtc));
        return quote;
    }

    /// <summary>
    /// Replaces the lines and terms of an OPEN quote as the next revision written by <paramref name="actorId"/>; a converted or
    /// lost quote is a state conflict. An expired-quote approval belongs to the revision it was given for: the new revision
    /// has none.
    /// </summary>
    public void Revise(long expectedVersion, string actorId, SalesQuoteTerms terms, IReadOnlyList<SalesQuoteLineInput> inputs, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireText(actorId, MaximumActorLength, nameof(actorId));
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
        revisions.Add(new SalesQuoteRevisionRecord(Revision, Terms.ValidUntil, actor, UpdatedAtUtc));
    }

    /// <summary>
    /// Master §7.2 / SAT-D25: records the loss of an OPEN quote with the chosen entry of the company's closed Settings list
    /// (<paramref name="reason"/>, already checked by the caller to be a selectable entry of the list in force) and the free-text
    /// <paramref name="explanation"/> — required when the list flags the entry as needing one
    /// (<paramref name="explanationRequired"/>), optional otherwise; never defaulted or cut.
    /// </summary>
    public void RecordLoss(long expectedVersion, string actorId, SalesQuoteLossReason reason, bool explanationRequired, string? explanation,
        DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireText(actorId, MaximumActorLength, nameof(actorId));
        ArgumentNullException.ThrowIfNull(reason);
        Identity.Require(reason.ReasonId, nameof(reason));
        Identity.Require(reason.ProfileId, nameof(reason));
        var code = RequireText(reason.Code, MaximumLossReasonCodeLength, nameof(reason));
        var label = RequireText(reason.Label, MaximumLossReasonLabelLength, nameof(reason));
        if (reason.ProfileVersion <= 0)
        {
            throw new ArgumentException("Kayıp nedeni listesinin sürümü pozitif olmalıdır.", nameof(reason));
        }

        RequireOpen("Yalnız açık teklif kaybedildi olarak kaydedilir");
        var text = string.IsNullOrWhiteSpace(explanation) ? null : explanation;
        if (text is null && explanationRequired)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteLossReasonRequired, "Seçilen kayıp nedeni açıklama ister.");
        }

        if (text is not null && text.Length > MaximumReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteLossReasonTooLong,
                $"Kayıp açıklaması en fazla {MaximumReasonLength} karakterdir; fazlası kesilmez, reddedilir.");
        }

        State = SalesQuoteState.Lost;
        Loss = new SalesQuoteLoss(text, actor, now.ToUniversalTime(), reason with { Code = code, Label = label });
        Touch(now);
    }

    /// <summary>
    /// SAT-D24 / SAT-D44: records the approval that lets this EXPIRED quote's CURRENT revision be converted. The quote must be
    /// open and expired on <paramref name="businessDay"/> — the server's Europe/Istanbul business day, never a client date
    /// (409 <see cref="SalesRuleCodes.QuoteNotExpired"/> otherwise) — and its current revision must not carry an approval yet
    /// (<see cref="SalesRuleCodes.QuoteExpiryAlreadyApproved"/>). SAT-D44 (2): whoever holds the approval permission may
    /// approve, the quote's creator or reviser included — an explicit exception to P6 for this approval only; the caller
    /// measures the permission. One version step; prices, lines and terms do not move.
    /// </summary>
    public void ApproveExpiredConversion(long expectedVersion, string actorId, DateOnly businessDay, string? note, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireText(actorId, MaximumActorLength, nameof(actorId));
        RequireOpen("Yalnız açık teklifin süresi geçmiş dönüşümü onaylanır");
        RequireNote(note);
        if (Terms.ValidUntil is not { } validUntil || validUntil >= businessDay)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteNotExpired, "Teklifin geçerlilik süresi dolmamış; onay gerekmez.");
        }

        if (CurrentExpiryApproval is not null)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteExpiryAlreadyApproved, "Teklifin bu revizyonu için süre onayı zaten verilmiş.");
        }

        Touch(now);
        expiryApprovals.Add(new SalesQuoteExpiryApproval(Revision, validUntil, businessDay, actor, UpdatedAtUtc,
            string.IsNullOrWhiteSpace(note) ? null : note));
    }

    /// <summary>Whether the quote's validity date precedes <paramref name="businessDay"/> (a quote without one never expires).</summary>
    public bool IsExpiredOn(DateOnly businessDay) => Terms.ValidUntil is { } validUntil && validUntil < businessDay;

    /// <summary>
    /// No 162: records the ONE order an OPEN quote became; a converted or lost quote is a state conflict and an expired one
    /// without an approval for its current revision is refused (<see cref="RequireValidOn"/>, SAT-D24) on
    /// <paramref name="businessDay"/> — the SERVER's business day, never the order's client-chosen business date.
    /// </summary>
    public void Convert(long expectedVersion, Guid draftId, string actorId, DateOnly businessDay, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        Identity.Require(draftId, nameof(draftId));
        var actor = RequireText(actorId, MaximumActorLength, nameof(actorId));
        RequireOpen("Yalnız açık teklif siparişe dönüştürülür");
        RequireValidOn(businessDay);
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
    /// SAT-D24: a quote whose validity date precedes <paramref name="businessDay"/> — the SERVER's Europe/Istanbul business day
    /// (SAT-D10), which the caller derives from its own clock; a client-sent date never decides it — is expired and is converted
    /// only with a recorded manager approval for its CURRENT revision (<see cref="ApproveExpiredConversion"/>). A quote without
    /// a validity date never expires (SAT-D39: the date is not mandatory); the validity date itself is the last valid day.
    /// </summary>
    public void RequireValidOn(DateOnly businessDay)
    {
        if (IsExpiredOn(businessDay) && CurrentExpiryApproval is null)
        {
            throw new SalesRuleException(SalesRuleCodes.QuoteExpired,
                "Teklifin geçerlilik süresi dolmuş; bu revizyon için yönetici onayı olmadan siparişe dönüştürülmez.");
        }
    }

    /// <summary>
    /// Restores the stored lifecycle onto a quote rebuilt through <see cref="Create"/> with its CURRENT revision's terms and
    /// lines: the facts must match the state (Open: none; Lost: the loss only; Converted: the conversion only) and the version
    /// must be at least the recorded steps. A corrupt row is refused, never repaired. Runs once.
    /// </summary>
    public void Restore(SalesQuoteState state, int revision, SalesQuoteLoss? loss, SalesQuoteConversion? conversion, long storedVersion,
        DateTimeOffset updatedAtUtc, IReadOnlyList<SalesQuoteRevisionRecord>? revisionHistory = null,
        IReadOnlyList<SalesQuoteExpiryApproval>? approvals = null)
    {
        if (restored || Version != 1 || Revision != 0 || State != SalesQuoteState.Open)
        {
            throw new InvalidOperationException("Teklifin kayıtlı durumu yalnız bir kez ve yeni kurulmuş teklife geri yüklenir.");
        }

        var consistent = state switch
        {
            SalesQuoteState.Open => loss is null && conversion is null,
            SalesQuoteState.Lost => IsConsistentLoss(loss) && conversion is null,
            SalesQuoteState.Converted => loss is null && conversion is { } converted && converted.DraftId != Guid.Empty,
            _ => false,
        };
        var history = revisionHistory ?? [];
        var approved = approvals ?? [];
        var historyConsistent = revisionHistory is null
            || (history.Count == revision + 1
                && history.Select((record, index) => record is not null && record.Revision == index
                    && !string.IsNullOrWhiteSpace(record.RevisedBy)).All(ok => ok)
                && history[^1].ValidUntil == Terms.ValidUntil);
        var approvalsConsistent = approved.All(approval => approval is not null && approval.Revision >= 0 && approval.Revision <= revision
                && !string.IsNullOrWhiteSpace(approval.ApprovedBy) && approval.ValidUntil < approval.BusinessDay)
            && approved.Select(approval => approval.Revision).Distinct().Count() == approved.Count
            && approved.Where(approval => approval.Revision == revision).All(approval => approval.ValidUntil == Terms.ValidUntil);
        var steps = (long)revision + approved.Count + (state == SalesQuoteState.Open ? 0 : 1);
        if (!consistent || !historyConsistent || !approvalsConsistent || revision < 0 || storedVersion < 1 + steps)
        {
            throw new InvalidOperationException("Kayıtlı teklif durumu, revizyonu ya da sürümü kendiyle uyuşmuyor.");
        }

        if (revisionHistory is not null)
        {
            revisions.Clear();
            revisions.AddRange(history);
        }

        expiryApprovals.AddRange(approved.OrderBy(approval => approval.Revision));
        State = state;
        Revision = revision;
        Loss = loss;
        Conversion = conversion;
        Version = storedVersion;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        restored = true;
    }

    /// <summary>
    /// A stored loss is either SAT-D25's (a list entry with a complete snapshot; the explanation optional but never blank) or a
    /// loss recorded before it (no entry; the free-text reason is its reason and is never blank).
    /// </summary>
    private static bool IsConsistentLoss(SalesQuoteLoss? loss) => loss switch
    {
        null => false,
        { ListReason: null } => loss.Reason is { Length: > 0 and <= MaximumReasonLength },
        { ListReason: { } entry } => entry.ReasonId != Guid.Empty && entry.ProfileId != Guid.Empty && entry.ProfileVersion > 0
            && entry.Code is { Length: > 0 and <= MaximumLossReasonCodeLength } && entry.Label is { Length: > 0 and <= MaximumLossReasonLabelLength }
            && (loss.Reason is null || loss.Reason.Length is > 0 and <= MaximumReasonLength),
    };

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

    /// <summary>
    /// The order line's commercial shape (K3), mathematical validity only; no minimum, margin or discount limit is a rule here.
    /// The rule itself is the ORDER's (<see cref="SalesOrderDraft.RequireLineCommercialFields"/>, one place): the quote's prices
    /// become the order's (No 162), so they are checked by the same code at the same money profile scale, and the price is
    /// taken as the shared TEK-01 <see cref="GoymenERP.BuildingBlocks.Monetary.Money"/> through TEK-03's
    /// <see cref="SalesLineMoney"/> (never rounded). Only the lossless-text check before it is the quote's own wire guard.
    /// </summary>
    private void RequireCommercial(SalesQuoteLineInput input)
    {
        if (input.UnitPrice is { } price && !SalesLinePriceCalculator.TryParseExact(price, out _))
        {
            throw new SalesRuleException(SalesRuleCodes.PriceValueUnrepresentable, "Birim fiyat kayıpsız temsil edilemiyor.", input.LineId);
        }

        var commercial = new CommercialDraftFields(input.UnitPrice, input.PriceUnitId, input.DiscountPercent);
        SalesOrderDraft.RequireLineCommercialFields(commercial, input.Product, Money.Scale, input.LineId);
        if (input.DiscountPercent is { } discount && !SalesLinePriceCalculator.TryParseExact(discount, out _))
        {
            throw new SalesRuleException(SalesRuleCodes.DiscountOutOfRange, "İskonto yüzdesi 0 ile 100 arasında olmalıdır.", input.LineId);
        }

        _ = SalesLineMoney.From(commercial, Header.CurrencyId, Money.Scale);
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
