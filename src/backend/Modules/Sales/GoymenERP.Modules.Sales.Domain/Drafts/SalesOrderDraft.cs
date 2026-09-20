using System.Globalization;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

public enum SalesDraftKind
{
    Order = 1,
}

public enum SalesDraftState
{
    Draft = 1,
}

/// <summary>
/// The INTERNAL APPROVAL axis of a draft (SALES-A1; DEC-SAL-001, DURUM_MAKINELERI "İç onay:
/// Hazırlanıyor / Onay bekliyor / Onaylı / Reddedildi"). A separate axis from the lifecycle
/// (<see cref="SalesDraftState"/>: Draft), from the commercial revision, and from sourcing,
/// fulfilment and customer confirmation, none of which this slice opens. Transitions:
/// <c>Preparing → PendingApproval</c> (<see cref="SalesOrderDraft.Submit"/>),
/// <c>PendingApproval → Approved</c> (<see cref="SalesOrderDraft.Approve"/>),
/// <c>PendingApproval → Rejected</c> (<see cref="SalesOrderDraft.Reject"/>), and
/// <c>Rejected → PendingApproval</c> again (the same Rev.0; a correction of the lines is a revision,
/// SALES-A2). An approved draft changes only through a revision (DEC-SAL-002, SALES-A2).
/// </summary>
public enum InternalApprovalState
{
    Preparing = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
}

/// <summary>
/// Who submitted and who decided, when, and the rejection reason: the audit facts of the approval
/// axis, carried by the draft and stored with it. Null means "has not happened": a draft in
/// <see cref="InternalApprovalState.Preparing"/> has none; a submitted one has the submission; a
/// decided one has both. The reason is free text the rejecting actor may give (an audit reason, not a
/// policy); it is never required and never invented.
/// </summary>
public sealed record ApprovalFacts(
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? RejectionReason)
{
    public static ApprovalFacts None { get; } = new(null, null, null, null, null);
}

/// <summary>The header facts of a draft as the client sent them; identities only, validated by their owners at command time.</summary>
public sealed record SalesDraftHeader(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    DateOnly BusinessDate,
    DateOnly? RequestedDeliveryDate = null,
    string? Note = null,
    OwnerReference? DeliveryAddress = null,
    OwnerReference? Contact = null,
    OwnerReference? Responsible = null,
    OwnerReference? PaymentTerms = null,
    OwnerReference? SourceStrategy = null,
    OwnerReference? Package = null);

/// <summary>Everything a line needs at <see cref="SalesOrderDraft.AddLine"/>; the owner snapshots come from the ports, the rest from the client.</summary>
public sealed record SalesDraftLineInput(
    Guid LineId,
    ProductSnapshot Product,
    UnitProfileSnapshot OrderUnit,
    string QuantityText,
    decimal Quantity,
    DateOnly? RequestedDeliveryDate = null,
    string? Note = null,
    CommercialDraftFields? Commercial = null,
    IReadOnlyList<DeliverySliceInput>? RequestedDeliverySlices = null);

public sealed record DeliverySliceInput(Guid SliceId, Guid OrderUnitId, string QuantityText, decimal Quantity, DateOnly RequestedDeliveryDate);

/// <summary>
/// A sales order draft (T03, SALES-P1): one company, one customer (Party + CompanyPartyAccount),
/// one currency, one business date, 1–500 lines. It is born <see cref="SalesDraftState.Draft"/>
/// at commercial revision 0 and stays there in this slice: approval, revision and cancellation
/// are T33; prices, discounts, taxes and rates are T28 and are retained only as text.
/// <list type="bullet">
/// <item><b>Version ≠ revision:</b> <see cref="Version"/> is the optimistic-concurrency token every
/// mutation names (the Purchasing / Logistics / Manufacturing pattern); <see cref="Revision"/> is
/// the commercial revision (<c>Rev.0</c>) and does not move here.</item>
/// <item><b>Owner snapshots:</b> a line carries what the Product and Settings owners answered for
/// this company on this business date; the draft refuses a snapshot of another company or another
/// date, and an order unit the Product snapshot does not bind as the order unit.</item>
/// <item><b>Ö2:</b> the quantity's dimension must be the unit profile's; decimals beyond
/// min(profile scale, 3) are refused, never rounded; the client's text is kept verbatim.</item>
/// <item><b>Delivery plan:</b> slices are same-unit, positive, unique, at most 500, and never add
/// up to more than the line; a shortfall is "unplanned", never "delivered".</item>
/// <item><b>Money:</b> the draft carries the Settings money profile snapshot for its currency;
/// there is no draft without one (#257: currency mandatory even unpriced). Today the approved
/// provider has none, so no real draft is stored — the store fails closed before this type.</item>
/// </list>
/// Nothing here authorizes; company and actor are the server's, and the document number is a
/// technical, company-scoped identifier, not an official document (Ö8).
/// </summary>
public sealed class SalesOrderDraft
{
    /// <summary>
    /// The Sales operation identifier the Settings money port is asked with for a draft's currency.
    /// A technical mapping of the Sales owner (Business #254 5654539215), in the Finance pattern
    /// (<c>finance.manual-bank-receipt.v1</c>). It chooses no rate, scale or date.
    /// </summary>
    public const string MoneyOperation = "sales.order-draft.v1";

    public const int MaximumLines = 500;
    public const int MaximumSlicesPerLine = 500;
    public const int MaximumNoteLength = 4000;
    public const int MaximumDocumentNoLength = 64;

    /// <summary>A rejection reason is audit text; it has the note's technical bound, not a policy of its own.</summary>
    public const int MaximumRejectionReasonLength = MaximumNoteLength;
    public const int MaximumActorLength = 128;

    private readonly List<SalesOrderDraftLine> lines = [];

    /// <summary>Set once the stored approval axis has been restored onto this instance; restoring is not a transition and runs once.</summary>
    private bool approvalRestored;

    private SalesOrderDraft(Guid id, Guid companyId, string documentNo, SalesDraftHeader header,
        MoneyProfileSnapshot money, DateTimeOffset now)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        DocumentNo = RequireDocumentNo(documentNo);
        Header = header;
        Money = money;
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }

    /// <summary>The server-resolved company; never taken from the request and never changed.</summary>
    public Guid CompanyId { get; }

    /// <summary>Technical, company-scoped display identifier assigned by the store on first commit. Not an official document number (Ö8); no gap-free guarantee.</summary>
    public string DocumentNo { get; }

    public SalesDraftKind Kind => SalesDraftKind.Order;

    public SalesDraftState State => SalesDraftState.Draft;

    /// <summary>The internal approval axis (SALES-A1); born <see cref="InternalApprovalState.Preparing"/>.</summary>
    public InternalApprovalState ApprovalState { get; private set; } = InternalApprovalState.Preparing;

    /// <summary>The approval facts as they stand: nothing until a submission, then who/when, then the decision.</summary>
    public ApprovalFacts Approval { get; private set; } = ApprovalFacts.None;

    /// <summary>
    /// Commercial revision; <c>Rev.N</c> on the wire, independent of <see cref="Version"/>. Born 0; advanced
    /// ONLY by <see cref="ReviseCommercial"/> on an approved draft (K3, Sinan 5748258299), never by a
    /// submission, an approval or a rejection.
    /// </summary>
    public int Revision { get; private set; }

    private readonly List<ApprovedRevisionSnapshot> archivedRevisions = [];

    /// <summary>The approved revisions this draft has left behind, oldest first; empty until an approved price was changed.</summary>
    public IReadOnlyList<ApprovedRevisionSnapshot> ArchivedRevisions => archivedRevisions.AsReadOnly();

    /// <summary>Whether the lines may still change: only while the draft is being prepared or was rejected (DEC-SAL-002).</summary>
    public bool LinesAreOpen => ApprovalState is InternalApprovalState.Preparing or InternalApprovalState.Rejected;

    /// <summary>Optimistic concurrency; starts at 1 and advances by one per recorded mutation.</summary>
    public long Version { get; private set; } = 1;

    public SalesDraftHeader Header { get; }

    public MoneyProfileSnapshot Money { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<SalesOrderDraftLine> Lines => lines.AsReadOnly();

    public static SalesOrderDraft Create(Guid id, Guid companyId, string documentNo, SalesDraftHeader header,
        MoneyProfileSnapshot money, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(money);
        Identity.Require(header.PartyId, nameof(header.PartyId));
        Identity.Require(header.CompanyAccountId, nameof(header.CompanyAccountId));
        Identity.Require(header.CurrencyId, nameof(header.CurrencyId));
        RequireNote(header.Note);
        foreach (var reference in new[] { header.DeliveryAddress, header.Contact, header.Responsible, header.PaymentTerms, header.SourceStrategy, header.Package })
        {
            if (reference is not null && (reference.Id == Guid.Empty || string.IsNullOrWhiteSpace(reference.ExpectedVersion)))
            {
                throw new ArgumentException("Sahip referansı kimlik ve beklenen sürüm ister.", nameof(header));
            }
        }

        if (money.CompanyId != companyId)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotCompanyMismatch, "Para profili başka şirketin.");
        }

        if (money.CurrencyId != header.CurrencyId || money.ProfileId == Guid.Empty || string.IsNullOrWhiteSpace(money.ProfileVersion))
        {
            throw new ArgumentException("Para profili taslağın para birimini adlandırmalı ve tam olmalıdır.", nameof(money));
        }

        if (money.EffectiveOn != header.BusinessDate)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotDateMismatch, "Para profili taslağın iş tarihi için alınmamış.");
        }

        if (!string.Equals(money.Operation, MoneyOperation, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotOperationMismatch,
                "Para profili taslağın operasyonu için değil başka bir operasyon için alınmış.");
        }

        if (money.Scale < 0 || (money.EffectiveThrough is { } moneyThrough && moneyThrough < money.EffectiveFrom))
        {
            throw new ArgumentException("Para profili snapshot'ı kendiyle çelişiyor.", nameof(money));
        }

        // The Settings port's validity obligation, on the money side as on the unit side (CARİ #306 P3-R1).
        if (!money.IsActive || header.BusinessDate < money.EffectiveFrom
            || (money.EffectiveThrough is { } until && header.BusinessDate > until))
        {
            throw new SalesRuleException(SalesRuleCodes.MoneyProfileNotInEffect,
                "Para profili iş tarihinde etkin değil.");
        }

        return new SalesOrderDraft(id, companyId, documentNo, header, money, now);
    }

    /// <summary>
    /// Adds a line from the client's input and the owners' snapshots. The aggregate is the single
    /// authority: draft state, line count and identity, snapshot company/date/unit consistency,
    /// quantity dimension and scale, note length, commercial pairing, and the delivery plan.
    /// </summary>
    public SalesOrderDraftLine AddLine(long expectedVersion, SalesDraftLineInput input, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Product);
        ArgumentNullException.ThrowIfNull(input.OrderUnit);
        RequireDraft();
        Identity.Require(input.LineId, nameof(input.LineId));
        if (lines.Count >= MaximumLines)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManyLines, $"Bir taslak en fazla {MaximumLines} satır taşır.");
        }

        if (lines.Any(line => line.Id == input.LineId))
        {
            throw new SalesRuleException(SalesRuleCodes.DuplicateLineId, "Aynı satır kimliği ikinci kez eklenemez.");
        }

        RequireNote(input.Note);
        RequireSnapshots(input.Product, input.OrderUnit);
        var quantity = RequireQuantity(input.Quantity, input.OrderUnit);
        RequireQuantityText(input.QuantityText, input.Quantity);
        var commercial = input.Commercial ?? CommercialDraftFields.None;
        RequireCommercial(commercial, input.Product);

        var slices = RequireSlices(input.RequestedDeliverySlices, input.OrderUnit, quantity);
        var line = new SalesOrderDraftLine(input.LineId, lines.Count + 1, input.Product, input.OrderUnit, quantity,
            input.QuantityText, input.RequestedDeliveryDate, input.Note, commercial, slices);
        lines.Add(line);
        Touch(now);
        return line;
    }

    // ---- manual price and discount (K3, Sinan 5748258299) ------------------------------------------

    /// <summary>
    /// The commercial fields of a line, validated for mathematical validity ONLY (K3): a unit price is
    /// exact text with at most the approved money profile's decimals (Ö2: never rounded), zero or more
    /// (a zero price is an explicit price; refusing it would invent a minimum), and comes with a price
    /// unit that is one of the product owner's three unit bindings; a discount is a percentage in
    /// 0..100, kept as text, and needs a price to apply to. No minimum price, margin floor or maximum
    /// discount threshold exists before a price list, and none is invented here. Null stays null:
    /// an un-priced line is not free and not zero. Nothing is computed from these values in this slice.
    /// Who may WRITE them is the caller's fence (the separate price-write permission), never this class.
    /// </summary>
    private void RequireCommercial(CommercialDraftFields commercial, ProductSnapshot product)
    {
        if ((commercial.UnitPrice is null) != (commercial.PriceUnitId is null))
        {
            throw new SalesRuleException(SalesRuleCodes.PriceUnitPairing,
                "Birim fiyat kendi fiyat birimini ister; fiyat birimi fiyatsız verilemez.");
        }

        if (commercial.UnitPrice is { } price)
        {
            if (DecimalPlaces(price) > Money.Scale)
            {
                throw new SalesRuleException(SalesRuleCodes.PriceScaleExceeded,
                    $"Birim fiyat en fazla {Money.Scale} ondalık taşıyabilir; fazlası yuvarlanmaz, reddedilir.");
            }

            var priceUnit = commercial.PriceUnitId!.Value;
            if (priceUnit != product.StockUnit.UnitId && priceUnit != product.SalesUnit.UnitId && priceUnit != product.OrderUnit.UnitId)
            {
                throw new SalesRuleException(SalesRuleCodes.PriceUnitUnsupported,
                    "Fiyat birimi, ürün sahibinin stok, satış ya da sipariş birimlerinden biri olmalıdır; Sales birim uydurmaz.");
            }
        }

        if (commercial.DiscountPercent is { } discount)
        {
            if (commercial.UnitPrice is null)
            {
                throw new SalesRuleException(SalesRuleCodes.DiscountWithoutPrice,
                    "İskonto uygulanacak bir birim fiyat olmadan verilemez.");
            }

            if (!IsPercent(discount))
            {
                throw new SalesRuleException(SalesRuleCodes.DiscountOutOfRange,
                    "İskonto yüzdesi 0 ile 100 arasında olmalıdır.");
            }
        }
    }

    /// <summary>The decimals of a plain decimal text ("12.50" → 2, "12" → 0); the wire has already required the shape.</summary>
    public static int DecimalPlaces(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var point = text.IndexOf('.', StringComparison.Ordinal);
        return point < 0 ? 0 : text.Length - point - 1;
    }

    /// <summary>
    /// Whether a plain decimal text is within 0..100, decided lexically: more than three integer digits
    /// (leading zeros aside) is out of range before any conversion, so no text can overflow a decimal.
    /// </summary>
    public static bool IsPercent(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integerPart = (point < 0 ? text : text[..point]).TrimStart('0');
        if (integerPart.Length > 3)
        {
            return false;
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            && value >= 0m && value <= 100m;
    }

    /// <summary>A draft is stored only with at least one line (#257: 1–500 lines).</summary>
    public void RequireStorable()
    {
        if (lines.Count == 0)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftWithoutLines, "Satırsız taslak kaydedilmez.");
        }
    }

    // ---- internal approval (SALES-A1) ------------------------------------------------------------

    /// <summary>
    /// Sends the draft for internal approval: <c>Preparing → PendingApproval</c>, or
    /// <c>Rejected → PendingApproval</c> again (the same Rev.0; the lines are what they are — a
    /// correction is a revision, SALES-A2). Any other state is <see cref="SalesRuleCodes.StateConflict"/>.
    /// The submitter is recorded; who may submit is the caller's fence (the draft's create permission).
    /// </summary>
    public void Submit(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireStorable();
        if (ApprovalState is not (InternalApprovalState.Preparing or InternalApprovalState.Rejected))
        {
            throw StateConflict("Yalnız hazırlanan ya da reddedilmiş taslak onaya gönderilir.");
        }

        ApprovalState = InternalApprovalState.PendingApproval;
        Approval = new ApprovalFacts(actor, now.ToUniversalTime(), null, null, null);
        Touch(now);
    }

    /// <summary>
    /// Approves a pending draft: <c>PendingApproval → Approved</c>. The approved draft's lines are then
    /// closed (DEC-SAL-002); nothing else changes — no allocation, loading, stock or money effect is
    /// produced here (those are the consumers' own records, read through the line reference port).
    /// The submitter approving their own draft is not refused (Business 5737989917: no maker≠checker).
    /// </summary>
    public void Approve(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        if (ApprovalState != InternalApprovalState.PendingApproval)
        {
            throw StateConflict("Yalnız onay bekleyen taslak onaylanır.");
        }

        ApprovalState = InternalApprovalState.Approved;
        Approval = Approval with { DecidedBy = actor, DecidedAtUtc = now.ToUniversalTime(), RejectionReason = null };
        Touch(now);
    }

    /// <summary>
    /// Rejects a pending draft: <c>PendingApproval → Rejected</c>, with an optional free-text reason
    /// (audit text, bounded by <see cref="MaximumRejectionReasonLength"/>, never required). The draft
    /// keeps Rev.0 and its lines; it may be submitted again as it is, or corrected by a revision (A2).
    /// </summary>
    public void Reject(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is not null && trimmedReason.Length > MaximumRejectionReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.ApprovalReasonTooLong,
                $"Ret nedeni en fazla {MaximumRejectionReasonLength} karakterdir.");
        }

        if (ApprovalState != InternalApprovalState.PendingApproval)
        {
            throw StateConflict("Yalnız onay bekleyen taslak reddedilir.");
        }

        ApprovalState = InternalApprovalState.Rejected;
        Approval = Approval with { DecidedBy = actor, DecidedAtUtc = now.ToUniversalTime(), RejectionReason = trimmedReason };
        Touch(now);
    }

    // ---- commercial revision (K3 Head B, Sinan 5748258299) --------------------------------------

    /// <summary>
    /// Replaces the commercial fields (unit price, price unit, discount) of EVERY line — each current line
    /// named exactly once, no other — under the caller's price-write fence. From <c>Preparing</c> or
    /// <c>Rejected</c> the fields simply change (nothing approved is touched; the revision number stays).
    /// From <c>Approved</c> the approved revision is archived FIRST — its approval facts, money profile and
    /// every line's values as they were — and only then does the draft advance to the next revision, back
    /// to <c>Preparing</c> with no approval facts: a changed price re-enters approval, and the previous
    /// approved snapshot is never deleted. From <c>PendingApproval</c> it is refused: the pending decision
    /// is made first (ASSUMPTION-SALESP-3). A revision that changes no value is refused: an approval is
    /// never discarded for nothing (ASSUMPTION-SALESP-5) — the SAME command resent under the same key is
    /// the caller's replay, decided before this method runs, never this refusal. Returns the archived
    /// snapshot when one was made (the store persists it in the same save as the draft), else null.
    /// </summary>
    public ApprovedRevisionSnapshot? ReviseCommercial(long expectedVersion, IReadOnlyList<LineCommercialInput> inputs, string actorId,
        DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        ArgumentNullException.ThrowIfNull(inputs);
        // Not RequireDraft(): that guard closes the lines of an approved draft (DEC-SAL-002), and THIS command is the
        // revision that decision points to. Only the lifecycle state is required here; the approval axis is decided below.
        if (State != SalesDraftState.Draft)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftNotDraft, "Yalnız taslak değiştirilebilir.");
        }

        if (ApprovalState == InternalApprovalState.PendingApproval)
        {
            throw StateConflict("Onay bekleyen taslağın fiyatı değiştirilmez; önce karar verilir.");
        }

        // Every current line exactly once, nothing else: the revision is of the whole commercial surface.
        if (inputs.Count != lines.Count || inputs.Select(input => input.LineId).Distinct().Count() != inputs.Count
            || inputs.Any(input => lines.All(line => line.Id != input.LineId)))
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionLinesMismatch,
                "Fiyat revizyonu taslağın her satırını tam bir kez adlandırır; başka satır adlandıramaz.");
        }

        var replacements = new List<(SalesOrderDraftLine Line, CommercialDraftFields Fields)>(inputs.Count);
        foreach (var input in inputs)
        {
            var line = lines.Single(candidate => candidate.Id == input.LineId);
            var fields = input.Commercial ?? CommercialDraftFields.None;
            try
            {
                RequireCommercial(fields, line.Product);
            }
            catch (SalesRuleException exception)
            {
                // The same rule, now naming the line it is about (the caller answers lines[i].<field>).
                throw new SalesRuleException(exception.Code, exception.Message, line.Id);
            }

            replacements.Add((line, fields));
        }

        if (replacements.All(pair => pair.Line.Commercial == pair.Fields))
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionNoChange,
                "Fiyat revizyonu hiçbir değeri değiştirmiyor; onay boşuna düşürülmez.");
        }

        ApprovedRevisionSnapshot? archived = null;
        if (ApprovalState == InternalApprovalState.Approved)
        {
            // The approved revision as it stands, archived BEFORE anything of it changes.
            archived = new ApprovedRevisionSnapshot(Revision, Approval, Money.ProfileId, Money.ProfileVersion, Money.Scale,
                now.ToUniversalTime(), actor,
                [.. lines.Select(line => new RevisionLineSnapshot(line.Id, line.LineNo, line.Product.ProductId, line.Product.ProductVersion,
                    line.OrderUnit.UnitId, line.QuantityText, line.Commercial))]);
            archivedRevisions.Add(archived);
            Revision += 1;
            ApprovalState = InternalApprovalState.Preparing;
            Approval = ApprovalFacts.None;
        }

        foreach (var (line, fields) in replacements)
        {
            line.ReplaceCommercial(fields);
        }

        Touch(now);
        return archived;
    }

    /// <summary>The store's rebuild of the stored revision number (no transition; a negative number is refused).</summary>
    public void RestoreRevision(int revision)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "Revizyon numarası negatif olamaz.");
        }

        RequireApprovalNotRestored();
        Revision = revision;
    }

    /// <summary>
    /// FİNANS (#374 review, P3): the approval restore's version floor and its history check read the archived count,
    /// so the revision number and the archived history must be restored BEFORE the approval axis. The store calls
    /// in that order; this makes the order an invariant rather than a convention — restored the other way round,
    /// the floor would drop to the lines' version and a wrong stored version would clear it with no symptom.
    /// </summary>
    private void RequireApprovalNotRestored()
    {
        if (approvalRestored)
        {
            throw new InvalidOperationException("Revizyon numarası ve arşivlenmiş revizyonlar onay ekseninden ÖNCE geri yüklenir.");
        }
    }

    /// <summary>
    /// The store's rebuild of one archived approved revision. Each must be older than the current revision
    /// and appended in ascending order without a gap or a repeat; a stored history that does not fit is
    /// refused, not repaired.
    /// </summary>
    public void RestoreArchivedRevision(ApprovedRevisionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireApprovalNotRestored();
        var expected = archivedRevisions.Count == 0 ? 0 : archivedRevisions[^1].Revision + 1;
        if (snapshot.Revision != expected || snapshot.Revision >= Revision
            || snapshot.Approval.DecidedBy is null || snapshot.Approval.DecidedAtUtc is null || snapshot.Approval.RejectionReason is not null
            || snapshot.Lines.Count == 0 || string.IsNullOrWhiteSpace(snapshot.ArchivedBy))
        {
            throw new InvalidOperationException("Kayıtlı revizyon geçmişi taslakla uyuşmuyor.");
        }

        archivedRevisions.Add(snapshot);
    }

    /// <summary>
    /// The store's rebuild of a stored draft: after the lines were added again through the domain,
    /// the stored approval axis is restored as it was, without a transition. The facts must fit the
    /// state exactly (a stored row that says "approved" without a decision is refused, not repaired),
    /// and the stored version must be at least what the lines and the state's minimum transitions
    /// account for; the stored version is then the draft's version.
    /// </summary>
    public void RestoreApproval(InternalApprovalState state, ApprovalFacts facts, long storedVersion, DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        // Restoring is not a transition: it runs once, onto a freshly rebuilt draft. A Preparing restore leaves the
        // axis where it was born, so the state alone cannot tell a rebuilt draft from a restored one; the flag does.
        if (approvalRestored || ApprovalState != InternalApprovalState.Preparing || Approval != ApprovalFacts.None)
        {
            throw new InvalidOperationException("Onay ekseni yalnız hazırlanan bir kurulum üzerine ve yalnız bir kez geri yüklenir.");
        }

        var submitted = facts.SubmittedBy is not null && facts.SubmittedAtUtc is not null;
        var decided = facts.DecidedBy is not null && facts.DecidedAtUtc is not null;
        var consistent = state switch
        {
            InternalApprovalState.Preparing => facts == ApprovalFacts.None,
            InternalApprovalState.PendingApproval => submitted && facts.DecidedBy is null && facts.DecidedAtUtc is null && facts.RejectionReason is null,
            InternalApprovalState.Approved => submitted && decided && facts.RejectionReason is null,
            InternalApprovalState.Rejected => submitted && decided,
            _ => false,
        };
        if (!consistent || (facts.RejectionReason is { } reason && (reason.Length == 0 || reason.Length > MaximumRejectionReasonLength))
            || facts.SubmittedBy is { Length: 0 } || facts.DecidedBy is { Length: 0 })
        {
            throw new InvalidOperationException("Kayıtlı onay olguları kayıtlı onay durumuyla uyuşmuyor.");
        }

        // The lines' version is a FLOOR the transitions raise: a submitted draft has had at least one, a decided
        // one at least two — and (K3 Head B) every archived approved revision carried its own submission, its
        // approval and the commercial write that archived it, three steps each, restored just before this call.
        // ÜRÜN #338 P3-1 pinned the Preparing case as EXACT ("a version raised outside the application does not
        // restore silently"), which held while nothing but the approval axis moved a Preparing draft. K3 made a
        // commercial write on a Preparing or Rejected draft a legitimate step (ReviseCommercial → Touch) that no
        // stored fact counts, so a Preparing draft may sit above its lines' version by those writes. WHAT IS LOST:
        // the exact check can no longer distinguish a version raised outside the application from a legitimate
        // commercial write on a Preparing draft — K3 made the latter a movement with no stored counter. The check
        // is therefore the same floor as the other states, declared here, in the flipped unit row and in the PR.
        // Exactness would need a persisted commercial-write counter on the header (schema, migration, pins, its
        // own real-SQL evidence) — a later slice owned by SİPARİŞ (the Sales lane), not this one.
        var minimumTransitions = state switch
        {
            InternalApprovalState.Preparing => 0,
            InternalApprovalState.PendingApproval => 1,
            _ => 2,
        };
        var archivedTransitions = checked(3L * archivedRevisions.Count);
        var consistentVersion = storedVersion >= checked(Version + minimumTransitions + archivedTransitions);
        if (!consistentVersion)
        {
            throw new InvalidOperationException("Kayıtlı sürüm, satırların ve onay geçişlerinin hesabıyla uyuşmuyor.");
        }

        // ORG (#374 P3): the read layer's declared job is to refuse, not repair — and it checked only half of the
        // history invariant. RestoreArchivedRevision pins the LOWER bound (contiguous from 0, each below Revision,
        // so Revision >= count); nothing pinned the UPPER one, so a stored header at Revision 3 beside ONE archived
        // row restored silently with a short history — a list missing a row reads like a correct list. The store
        // loads every archive row of the draft (no paging, no filter, single and list paths alike), so on every
        // legitimate restore Revision == archived count: the premise the migration's Down rests on ("a header above
        // Revision 0 exists only beside an archived row"), now held on the read path as the write path measures it.
        if (Revision != archivedRevisions.Count)
        {
            throw new InvalidOperationException("Kayıtlı revizyon numarası arşivlenmiş revizyon sayısıyla uyuşmuyor.");
        }

        ApprovalState = state;
        Approval = facts;
        Version = storedVersion;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
        approvalRestored = true;
    }

    private static string RequireActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Aktör kimliği boş olamaz.", nameof(actorId));
        }

        var actor = actorId.Trim();
        return actor.Length <= MaximumActorLength
            ? actor
            : throw new ArgumentException($"Aktör kimliği en fazla {MaximumActorLength} karakterdir.", nameof(actorId));
    }

    private SalesRuleException StateConflict(string message) =>
        new(SalesRuleCodes.StateConflict, $"{message} (durum: {ApprovalState}).");

    private void RequireSnapshots(ProductSnapshot product, UnitProfileSnapshot orderUnit)
    {
        if (product.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(product.ProductVersion)
            || string.IsNullOrWhiteSpace(product.Code) || string.IsNullOrWhiteSpace(product.Name)
            || product.StockUnit is null || product.SalesUnit is null || product.OrderUnit is null)
        {
            throw new ArgumentException("Ürün snapshot'ı eksik.", nameof(product));
        }

        if (orderUnit.UnitId == Guid.Empty || orderUnit.ProfileId == Guid.Empty || string.IsNullOrWhiteSpace(orderUnit.UnitCode)
            || string.IsNullOrWhiteSpace(orderUnit.DimensionCode) || string.IsNullOrWhiteSpace(orderUnit.ProfileVersion)
            || orderUnit.ProfileScale < 0 || orderUnit.Scale < 0
            || (orderUnit.EffectiveThrough is { } through && through < orderUnit.EffectiveFrom))
        {
            throw new ArgumentException("Birim profili snapshot'ı eksik ya da kendiyle çelişiyor.", nameof(orderUnit));
        }

        // Only kilogram (weight) and a count dimension have a Ö2 rule; anything else is refused,
        // never mapped to a guessed unit. A kilogram flag on a non-weight dimension is malformed.
        if (orderUnit.IsKilogram != IsWeight(orderUnit.DimensionCode))
        {
            throw new ArgumentException("Birim profili snapshot'ının kg bayrağı boyutuyla çelişiyor.", nameof(orderUnit));
        }

        if (!orderUnit.IsKilogram && !IsCount(orderUnit.DimensionCode))
        {
            throw new SalesRuleException(SalesRuleCodes.UnitUnsupported,
                "Sipariş birimi ne kilogram ne adet boyutunda; Sales bunun için birim uydurmaz.");
        }

        // The Settings port's validity obligation, enforced where the snapshot is consumed (PUR-3's verifier rule).
        if (!orderUnit.IsActive || Header.BusinessDate < orderUnit.EffectiveFrom
            || (orderUnit.EffectiveThrough is { } until && Header.BusinessDate > until))
        {
            throw new SalesRuleException(SalesRuleCodes.UnitProfileNotInEffect,
                "Sipariş birimi profili iş tarihinde etkin değil.");
        }

        // The snapshot's stated applied scale must be what the domain derives; a snapshot that
        // says "3" over a profile of 1 (or "5" over Ö2's 3) is malformed, not a wider permission.
        if (orderUnit.Scale != AppliedScale(orderUnit))
        {
            throw new ArgumentException("Birim profili snapshot'ının uygulanan ölçeği min(profil, Ö2) ile uyuşmuyor.", nameof(orderUnit));
        }

        foreach (var binding in new[] { product.StockUnit, product.SalesUnit, product.OrderUnit })
        {
            if (binding.CompanyId != CompanyId)
            {
                throw new SalesRuleException(SalesRuleCodes.SnapshotCompanyMismatch, "Ürün birim bağı başka şirketin.");
            }
        }

        if (orderUnit.CompanyId != CompanyId)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotCompanyMismatch, "Birim profili başka şirketin.");
        }

        if (product.EffectiveOn != Header.BusinessDate)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotDateMismatch, "Ürün snapshot'ı taslağın iş tarihi için alınmamış.");
        }

        if (product.OrderUnit.UnitId != orderUnit.UnitId || product.OrderUnit.ProfileId != orderUnit.ProfileId
            || !string.Equals(product.OrderUnit.ProfileVersion, orderUnit.ProfileVersion, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.ProductUnitMismatch,
                "Seçilen sipariş birimi, ürün sahibinin sipariş birimi bağıyla aynı değil.");
        }
    }

    /// <summary>The scale the domain applies: min(owner's profile scale, Ö2's own cap for the dimension). Derived here, never taken from the snapshot's word (ÜRÜN #306 P3-1).</summary>
    public static int AppliedScale(UnitProfileSnapshot orderUnit)
    {
        ArgumentNullException.ThrowIfNull(orderUnit);
        var unit = orderUnit.IsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
        return Math.Min(orderUnit.ProfileScale, Quantity.AllowedScale(unit));
    }

    private static Quantity RequireQuantity(decimal value, UnitProfileSnapshot orderUnit)
    {
        var unit = orderUnit.IsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Satır miktarı sıfırdan büyük olmalıdır.");
        }

        var applied = AppliedScale(orderUnit);
        if (Quantity.SignificantDecimals(value) > applied)
        {
            throw new SalesRuleException(SalesRuleCodes.QuantityScaleExceeded,
                $"Miktar en fazla {applied} ondalık taşıyabilir; fazlası yuvarlanmaz, reddedilir.");
        }

        return Quantity.Positive(value, unit);
    }

    private static IReadOnlyList<DeliverySlice> RequireSlices(IReadOnlyList<DeliverySliceInput>? inputs,
        UnitProfileSnapshot orderUnit, Quantity quantity)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return [];
        }

        if (inputs.Count > MaximumSlicesPerLine)
        {
            throw new SalesRuleException(SalesRuleCodes.TooManySlices, $"Bir satır en fazla {MaximumSlicesPerLine} termin dilimi taşır.");
        }

        var ids = new HashSet<Guid>();
        var slices = new List<DeliverySlice>(inputs.Count);
        var planned = Quantity.Zero(quantity.Unit);
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            Identity.Require(input.SliceId, nameof(input.SliceId));
            if (!ids.Add(input.SliceId))
            {
                throw new SalesRuleException(SalesRuleCodes.DuplicateSliceId, "Aynı dilim kimliği ikinci kez verilemez.");
            }

            if (input.OrderUnitId != orderUnit.UnitId)
            {
                throw new SalesRuleException(SalesRuleCodes.SliceUnitMismatch, "Termin dilimi satırın biriminde olmalıdır; dönüşüm yok.");
            }

            var sliceQuantity = RequireQuantity(input.Quantity, orderUnit);
            RequireQuantityText(input.QuantityText, input.Quantity);
            planned = planned.Add(sliceQuantity);
            if (planned.IsGreaterThan(quantity))
            {
                throw new SalesRuleException(SalesRuleCodes.SlicesExceedQuantity, "Termin dilimleri satır miktarını aşamaz.");
            }

            slices.Add(new DeliverySlice(input.SliceId, sliceQuantity, input.QuantityText, input.RequestedDeliveryDate));
        }

        return slices;
    }

    /// <summary>
    /// The client's quantity text is kept verbatim, but it must BE the quantity: the same decimal
    /// value in plain invariant notation (#257's wire shape; ÜRÜN #306 P3-3). "12.500" and 12.5m
    /// agree; "12.5" and 12.51m, or "1,2", do not.
    /// </summary>
    private static void RequireQuantityText(string? text, decimal value)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !decimal.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            || parsed != value)
        {
            throw new ArgumentException("Miktar metni miktarın kendisiyle uyuşmuyor.", nameof(text));
        }
    }

    private static bool IsWeight(string dimensionCode) => string.Equals(dimensionCode, "weight", StringComparison.Ordinal);

    private static bool IsCount(string dimensionCode) => string.Equals(dimensionCode, "count", StringComparison.Ordinal);

    private static void RequireNote(string? note)
    {
        if (note is not null && note.Length > MaximumNoteLength)
        {
            throw new SalesRuleException(SalesRuleCodes.NoteTooLong, $"Not en fazla {MaximumNoteLength} karakterdir.");
        }
    }

    private static string RequireDocumentNo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Belge numarası boş olamaz.", nameof(value));
        }

        var documentNo = value.Trim();
        return documentNo.Length <= MaximumDocumentNoLength
            ? documentNo
            : throw new ArgumentException($"Belge numarası en fazla {MaximumDocumentNoLength} karakterdir.", nameof(value));
    }

    private void RequireDraft()
    {
        if (State != SalesDraftState.Draft)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftNotDraft, "Yalnız taslak değiştirilebilir.");
        }

        // DEC-SAL-002: a submitted or approved draft does not change its lines; a change is a revision (A2).
        if (!LinesAreOpen)
        {
            throw StateConflict("Onay bekleyen ya da onaylı taslağın satırları değişmez; değişiklik yeni bir revizyondur.");
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
}
