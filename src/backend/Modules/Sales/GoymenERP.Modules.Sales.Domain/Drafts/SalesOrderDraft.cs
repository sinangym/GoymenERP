using System.Globalization;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

public enum SalesDraftKind
{
    Order = 1,
}

/// <summary>
/// The LIFECYCLE axis of an order (DEC-SAL-001: a separate axis from the internal approval; DURUM_MAKINELERI "Yaşam
/// döngüsü: Taslak / Aktif / Kapalı / İptal"). Only the two states this code can reach exist: <see cref="Draft"/> (every
/// order, approved or not — "Aktif"/"Kapalı" are not derived here) and <see cref="Cancelled"/> (DEC-SAL-005: an approved
/// order cancelled after its impact preview; terminal, nothing deleted).
/// </summary>
public enum SalesDraftState
{
    Draft = 1,

    /// <summary>
    /// DEC-SAL-005 / AT-007: the approved order was cancelled with a reason. Terminal: every mutation entry refuses it.
    /// The approval axis keeps its facts (who approved stays who approved); the lines, revisions and schedules stay.
    /// </summary>
    Cancelled = 2,
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

    /// <summary>
    /// D2 (Sinan 5749046401): the preparer took back their own not-yet-approved draft — a terminal state of the axis
    /// with its own command and audit trail; nothing is deleted, the lines and the revisions stay as they were.
    /// The decision columns record who withdrew and when (and an optional reason): the STATE says it was a
    /// withdrawal, not an approval or a rejection.
    /// </summary>
    Withdrawn = 5,
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

/// <summary>
/// The customer acceptance of one commercial revision. Existing orders do not require this gate;
/// new orders do. A later revision leaves these immutable facts attached to their accepted revision
/// and therefore makes <see cref="SalesOrderDraft.IsCustomerAccepted"/> false until that revision is accepted.
/// </summary>
public sealed record CustomerAcceptanceFacts(int Revision, string AcceptedBy, DateTimeOffset AcceptedAtUtc);

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
public sealed partial class SalesOrderDraft
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
        MoneyProfileSnapshot money, DateTimeOffset now, bool customerAcceptanceRequired)
    {
        Id = Identity.Require(id, nameof(id));
        CompanyId = Identity.Require(companyId, nameof(companyId));
        DocumentNo = RequireDocumentNo(documentNo);
        Header = header;
        Money = money;
        CustomerAcceptanceRequired = customerAcceptanceRequired;
        CreatedAtUtc = now.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }

    /// <summary>The server-resolved company; never taken from the request and never changed.</summary>
    public Guid CompanyId { get; }

    /// <summary>Technical, company-scoped display identifier assigned by the store on first commit. Not an official document number (Ö8); no gap-free guarantee.</summary>
    public string DocumentNo { get; }

    public SalesDraftKind Kind => SalesDraftKind.Order;

    /// <summary>The lifecycle axis: <see cref="SalesDraftState.Draft"/> until a recorded cancellation (DEC-SAL-005).</summary>
    public SalesDraftState State { get; private set; } = SalesDraftState.Draft;

    /// <summary>DEC-SAL-005: the recorded cancellation (who, when, why, which revision); null while the order is not cancelled.</summary>
    public CancellationFacts? Cancellation { get; private set; }

    /// <summary>The technical bound of a cancellation reason: the note's, as the rejection reason's.</summary>
    public const int MaximumCancellationReasonLength = MaximumNoteLength;

    /// <summary>The internal approval axis (SALES-A1); born <see cref="InternalApprovalState.Preparing"/>.</summary>
    public InternalApprovalState ApprovalState { get; private set; } = InternalApprovalState.Preparing;

    /// <summary>The approval facts as they stand: nothing until a submission, then who/when, then the decision.</summary>
    public ApprovalFacts Approval { get; private set; } = ApprovalFacts.None;

    /// <summary>Cutover flag: false for pre-Q9 rows; true for orders created after Q9 deployment.</summary>
    public bool CustomerAcceptanceRequired { get; }

    /// <summary>The latest recorded acceptance; its revision is retained when a later revision supersedes it.</summary>
    public CustomerAcceptanceFacts? CustomerAcceptance { get; private set; }

    /// <summary>Whether downstream consumers may treat the current revision as an approved customer order.</summary>
    public bool IsCustomerAccepted => !CustomerAcceptanceRequired || CustomerAcceptance?.Revision == Revision;

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

    /// <summary>
    /// Optimistic concurrency; starts at 1 and advances by one per RECORDED mutation. A refused command (state conflict,
    /// stale version, rule) records an attempt in the store and moves NOTHING here — the version after a refusal is the
    /// version before it. Ladder authors: create = 1, + one per line added, + one per recorded transition/revision;
    /// count refusals as zero (D2 lost one run to each direction of that mistake).
    /// </summary>
    public long Version { get; private set; } = 1;

    /// <summary>The header facts; only its requested delivery date may move, through <see cref="RescheduleDelivery"/> (DEC-SAL-003).</summary>
    public SalesDraftHeader Header { get; private set; }

    private readonly List<DeliveryScheduleSnapshot> deliverySchedules = [];

    /// <summary>Schedule 0: the delivery dates as the draft was created; captured before the first recorded change.</summary>
    private DeliveryScheduleSnapshot? originalSchedule;

    /// <summary>
    /// DEC-SAL-003: the recorded delivery-schedule changes, oldest first (schedule 1, 2, ...); empty until a planner moved a
    /// date. Each holds the complete dates after its change; the draft's current dates are the last one's.
    /// </summary>
    public IReadOnlyList<DeliveryScheduleSnapshot> DeliverySchedules => deliverySchedules.AsReadOnly();

    /// <summary>The schedule history as shown: schedule 0 (as created) followed by every recorded change.</summary>
    public IReadOnlyList<DeliveryScheduleSnapshot> DeliveryScheduleHistory =>
        [originalSchedule ?? CurrentSchedule(0, null, null), .. deliverySchedules];

    public MoneyProfileSnapshot Money { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<SalesOrderDraftLine> Lines => lines.AsReadOnly();

    public static SalesOrderDraft Create(Guid id, Guid companyId, string documentNo, SalesDraftHeader header,
        MoneyProfileSnapshot money, DateTimeOffset now, bool customerAcceptanceRequired = false)
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

        return new SalesOrderDraft(id, companyId, documentNo, header, money, now, customerAcceptanceRequired);
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

        RequirePricingDetail(commercial, line.Id);
    }

    /// <summary>
    /// AT-018 / AT-021: the reference and the visible adjustments - each kind is a percent OR an amount; an amount is exact
    /// text within the money profile's scale (never rounded); a surcharge percent is 0..100; a discount amount never takes
    /// the net below zero; the reference is in the line's price unit; and, when the reference carries a floor, the net
    /// (<see cref="SalesLinePriceCalculator"/>) is not below it (AT-021, fail-closed: no exception approver is decided).
    /// Whether the reference is the Product owner's current answer is the caller's check (it reads the owner).
    /// </summary>
    private void RequirePricingDetail(CommercialDraftFields commercial, Guid lineId)
    {
        RequireRepresentable(commercial.UnitPrice, SalesRuleCodes.PriceValueUnrepresentable,
            "Birim fiyat decimal türünde kayıpsız temsil edilemiyor.", lineId);
        RequireRepresentable(commercial.DiscountPercent, SalesRuleCodes.DiscountPercentUnrepresentable,
            "İskonto yüzdesi decimal türünde kayıpsız temsil edilemiyor.", lineId);
        RequireRepresentable(commercial.DiscountAmount, SalesRuleCodes.DiscountAmountUnrepresentable,
            "İskonto tutarı decimal türünde kayıpsız temsil edilemiyor.", lineId);
        RequireRepresentable(commercial.SurchargePercent, SalesRuleCodes.SurchargePercentUnrepresentable,
            "Vade farkı yüzdesi decimal türünde kayıpsız temsil edilemiyor.", lineId);
        RequireRepresentable(commercial.SurchargeAmount, SalesRuleCodes.SurchargeAmountUnrepresentable,
            "Vade farkı tutarı decimal türünde kayıpsız temsil edilemiyor.", lineId);

        if (!commercial.HasPricingDetail)
        {
            RequireCalculable(commercial, lineId);
            return;
        }

        if (commercial.UnitPrice is not { } price)
        {
            throw new SalesRuleException(SalesRuleCodes.AdjustmentWithoutPrice,
                "Referans fiyat, iskonto tutarı ve vade farkı bir birim fiyat olmadan verilemez.");
        }

        if ((commercial.DiscountPercent is not null && commercial.DiscountAmount is not null)
            || (commercial.SurchargePercent is not null && commercial.SurchargeAmount is not null))
        {
            throw new SalesRuleException(SalesRuleCodes.AdjustmentKindConflict,
                "İskonto ve vade farkı ya yüzde ya tutar olarak verilir; ikisi birden verilmez.");
        }

        foreach (var amount in new[] { commercial.DiscountAmount, commercial.SurchargeAmount })
        {
            if (amount is not null && DecimalPlaces(amount) > Money.Scale)
            {
                throw new SalesRuleException(SalesRuleCodes.AdjustmentScaleExceeded,
                    $"Tutar en fazla {Money.Scale} ondalık taşıyabilir; fazlası yuvarlanmaz, reddedilir.");
            }
        }

        if (commercial.SurchargePercent is { } surcharge && !IsPercent(surcharge))
        {
            throw new SalesRuleException(SalesRuleCodes.SurchargeOutOfRange, "Vade farkı yüzdesi 0 ile 100 arasında olmalıdır.");
        }

        if (commercial.DiscountAmount is { } discountAmount
            && SalesLinePriceCalculator.Parse(discountAmount) > SalesLinePriceCalculator.Parse(price))
        {
            throw new SalesRuleException(SalesRuleCodes.DiscountExceedsPrice, "İskonto tutarı birim fiyatı aşamaz.");
        }

        if (commercial.Reference is { } reference)
        {
            if (reference.PriceUnitId != commercial.PriceUnitId)
            {
                throw new SalesRuleException(SalesRuleCodes.PriceReferenceMismatch,
                    "Referans liste fiyatı satırın fiyat biriminde olmalıdır.");
            }

            ValidateFloorException(commercial, reference);
        }

        RequireCalculable(commercial, lineId);
    }

    private static void RequireRepresentable(string? value, string code, string message, Guid lineId)
    {
        if (value is not null && !SalesLinePriceCalculator.TryParseExact(value, out _))
        {
            throw new SalesRuleException(code, message, lineId);
        }
    }

    private static void RequireCalculable(CommercialDraftFields commercial, Guid lineId)
    {
        try
        {
            _ = SalesLinePriceCalculator.NetUnitPrice(commercial);
        }
        catch (OverflowException)
        {
            throw new SalesRuleException(SalesRuleCodes.PriceCalculationOverflow,
                "Fiyat, iskonto ve vade farkı hesabı decimal sınırını aşıyor.", lineId);
        }
    }

    private static void ValidateFloorException(CommercialDraftFields commercial, PriceReferenceSnapshot reference)
    {
        var net = SalesLinePriceCalculator.NetUnitPrice(commercial);
        var belowFloor = reference.FloorUnitPrice is { } floor && net is { } value
            && value < SalesLinePriceCalculator.Parse(floor);
        if (!belowFloor)
        {
            if (commercial.FloorException is not null)
            {
                throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "Taban altı olmayan fiyat istisna kaydı taşıyamaz.");
            }
            return;
        }

        if (commercial.FloorException is not { } exception)
        {
            return;
        }

        if (exception.PriceListId != reference.PriceListId || exception.PriceListVersionNo != reference.VersionNo
            || exception.PriceUnitId != commercial.PriceUnitId || exception.UnitPrice != commercial.UnitPrice
            || exception.NetUnitPrice != SalesLinePriceCalculator.Format(net!.Value)
            || exception.FloorUnitPrice != reference.FloorUnitPrice
            || string.IsNullOrWhiteSpace(exception.RequestedBy)
            || (exception.IsApproved && string.Equals(exception.RequestedBy, exception.ApprovedBy, StringComparison.Ordinal)))
        {
            throw new SalesRuleException(SalesRuleCodes.InternalInvariant, "Taban fiyat istisnası bağlı olduğu ticari değerlerle uyuşmuyor.");
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
        RequirePriceFloorExceptionsApproved();
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
    /// Whether the submitter may decide their own draft is NOT the domain's question: D2 (Sinan 5749046401)
    /// makes it a persona-based authority rule measured by the service before this transition (the price-write
    /// fence's shape) — the earlier "no maker≠checker" reading of 5737989917 item 22 is superseded for Satış
    /// Destek by that decision and unchanged for every other persona. Here only the state is decided.
    /// </summary>
    public void Approve(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequirePriceFloorExceptionsApproved();
        if (ApprovalState != InternalApprovalState.PendingApproval)
        {
            throw StateConflict("Yalnız onay bekleyen taslak onaylanır.");
        }

        ApprovalState = InternalApprovalState.Approved;
        Approval = Approval with { DecidedBy = actor, DecidedAtUtc = now.ToUniversalTime(), RejectionReason = null };
        Touch(now);
    }

    /// <summary>
    /// Records the customer's acceptance of the current internally approved commercial revision. This is a
    /// separate authority action: internal approval does not confer it. It has no stock, allocation or money
    /// effect; downstream consumers open only after this fact exists for the current revision.
    /// </summary>
    public void RecordCustomerAcceptance(long expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireNotCancelled("İptal edilmiş sipariş için müşteri kabulü kaydedilmez.");
        if (ApprovalState != InternalApprovalState.Approved)
        {
            throw StateConflict("Müşteri kabulü yalnız iç onaylı siparişe kaydedilir.");
        }

        if (!CustomerAcceptanceRequired)
        {
            throw StateConflict("Bu geçiş öncesi sipariş için ayrı müşteri kabulü aranmaz.");
        }

        if (CustomerAcceptance?.Revision == Revision)
        {
            throw StateConflict("Siparişin güncel revizyonu zaten müşteri tarafından kabul edilmiş.");
        }

        CustomerAcceptance = new CustomerAcceptanceFacts(Revision, actor, now.ToUniversalTime());
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

    /// <summary>
    /// D2 (Sinan 5749046401): the preparer withdraws their own NEVER-approved draft —
    /// <c>Preparing | PendingApproval | Rejected → Withdrawn</c>, and only at <c>Revision 0</c> (a rejected draft is
    /// still "not yet approved"; ASSUMPTION-SALESD2-2, reversible). Refused (<see cref="SalesRuleCodes.StateConflict"/>):
    /// an APPROVED draft, and a draft with an APPROVED HISTORY — <see cref="ReviseCommercial"/> brings an approved draft
    /// back to <c>Preparing</c> at <c>Rev.N+1</c>, so the current state alone cannot say "never approved"; while it was
    /// approved its lines were answered to the consumers (the line reference port) and may have been linked by
    /// allocation, loading or fulfilment records Sales neither holds nor can see. The easy path therefore stops at
    /// <c>Revision != 0</c>: the archived snapshot and whatever the consumers hold stay untouched. Withdrawn is
    /// terminal: EVERY mutation entry refuses it (pinned by the population test). Who may withdraw (the draft's own
    /// preparer, the create name) is the service's fence; the reason is optional audit text, bounded like the
    /// rejection reason. No money or stock effect exists to undo — a draft never produced one.
    /// </summary>
    public void Withdraw(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is not null && trimmedReason.Length > MaximumRejectionReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.ApprovalReasonTooLong,
                $"Geri çekme nedeni en fazla {MaximumRejectionReasonLength} karakterdir.");
        }

        if (ApprovalState is not (InternalApprovalState.Preparing or InternalApprovalState.PendingApproval or InternalApprovalState.Rejected))
        {
            throw StateConflict("Yalnız henüz onaylanmamış taslak geri çekilir.");
        }

        if (Revision != 0)
        {
            throw StateConflict("Onaylanmış geçmişi olan taslak geri çekilmez; bağlı kayıtlar sahiplerinde durur.");
        }

        ApprovalState = InternalApprovalState.Withdrawn;
        Approval = Approval with { DecidedBy = actor, DecidedAtUtc = now.ToUniversalTime(), RejectionReason = trimmedReason };
        Touch(now);
    }

    // ---- cancellation of an approved order (DEC-SAL-005, AT-007) ---------------------------------

    /// <summary>
    /// DEC-SAL-005 (KILITLI): "Sipariş iptalinde aktif tahsis, bağlı satın alma/üretim, irsaliyesiz yükleme, faturalanmamış
    /// irsaliye ve eşleşmemiş avans/dekont etkisi gösterilir" — "Etki önizlemesi olmadan iptal yok". The domain half: the
    /// lifecycle moves <c>Draft → Cancelled</c> for an APPROVED order only, with a required reason. What the owners hold
    /// (allocations, loading, …) is NOT this aggregate's knowledge: the caller reads the impact through the owners' ports and
    /// refuses the cancellation while any effect exists (the fail-closed reading of an undecided rule, README) BEFORE this
    /// runs; nothing of another module is released or reversed here.
    /// <list type="bullet">
    /// <item><b>Only Approved:</b> a never-approved Rev.0 draft is withdrawn by its preparer (D2), not cancelled; a draft in
    /// re-approval (Rev.N back in Preparing/PendingApproval/Rejected) has no approved revision in force, and whether it may
    /// be cancelled is not decided — refused (<see cref="SalesRuleCodes.StateConflict"/>), never guessed.</item>
    /// <item><b>History stays (AT-007):</b> the approval facts, the revision, the lines, the archived revisions and the
    /// schedules do not move; only the lifecycle state, the cancellation facts and the version (one step) do.</item>
    /// <item><b>Terminal:</b> a cancelled order refuses every mutation entry, this one included.</item>
    /// </list>
    /// </summary>
    public CancellationFacts Cancel(long expectedVersion, string actorId, string? reason, CancellationImpactSnapshot impact,
        DateTimeOffset now)
    {
        var actor = RequireActor(actorId);
        var trimmedReason = RequireCancellable(expectedVersion, reason);
        RequireImpact(impact);
        var facts = new CancellationFacts(Revision, actor, now.ToUniversalTime(), trimmedReason, impact);
        State = SalesDraftState.Cancelled;
        Cancellation = facts;
        Touch(now);
        return facts;
    }

    /// <summary>
    /// The cancellation's own preconditions WITHOUT the transition — the expected version, the lifecycle and approval state
    /// and the reason — so the caller can refuse a stale or wrong-state command before it asks any owner for the impact.
    /// Returns the trimmed reason. <see cref="Cancel"/> runs the same checks again (its word is final).
    /// </summary>
    public string RequireCancellable(long expectedVersion, string? reason)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("Sipariş zaten iptal edilmiş.");
        if (ApprovalState != InternalApprovalState.Approved)
        {
            throw StateConflict("Yalnız onaylı sipariş iptal edilir; onaylanmamış taslak geri çekilir.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is null)
        {
            throw new SalesRuleException(SalesRuleCodes.CancellationReasonRequired, "İptal nedeni zorunludur.");
        }

        if (trimmedReason.Length > MaximumCancellationReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.CancellationReasonTooLong,
                $"İptal nedeni en fazla {MaximumCancellationReasonLength} karakterdir.");
        }

        return trimmedReason;
    }

    /// <summary>
    /// The store's rebuild of the recorded cancellation, after the lines, the revisions and the schedules and BEFORE the
    /// approval axis (whose version floor counts it as one step). It must name the current revision, an actor, a time and
    /// a reason within the bound; a stored cancellation that does not fit is refused, not repaired.
    /// </summary>
    public void RestoreCancellation(CancellationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireApprovalNotRestored();
        if (State != SalesDraftState.Draft || Cancellation is not null || facts.Revision != Revision
            || string.IsNullOrWhiteSpace(facts.CancelledBy) || facts.CancelledBy.Length > MaximumActorLength
            || string.IsNullOrWhiteSpace(facts.Reason) || facts.Reason.Length > MaximumCancellationReasonLength
            || !IsImpact(facts.Impact))
        {
            throw new InvalidOperationException("Kayıtlı iptal olguları taslakla uyuşmuyor.");
        }

        State = SalesDraftState.Cancelled;
        Cancellation = facts;
    }

    /// <summary>The impact the cancellation was decided on: a token and at least one source, each named once with a status.</summary>
    private static void RequireImpact(CancellationImpactSnapshot impact)
    {
        if (!IsImpact(impact))
        {
            throw new ArgumentException("İptal, kullanıcının gördüğü etki önizlemesiyle (belirteç ve kaynaklar) kaydedilir.", nameof(impact));
        }
    }

    private static bool IsImpact(CancellationImpactSnapshot? impact) =>
        impact is not null && !string.IsNullOrWhiteSpace(impact.Token) && impact.Token.Length <= 128
        && impact.Sources is { Count: > 0 }
        && impact.Sources.All(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Status))
        && impact.Sources.Select(entry => entry.Source).Distinct(StringComparer.Ordinal).Count() == impact.Sources.Count;

    private void RequireNotCancelled(string message)
    {
        if (State == SalesDraftState.Cancelled)
        {
            throw new SalesRuleException(SalesRuleCodes.StateConflict, $"{message} (yaşam döngüsü: {State}).");
        }
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
        // DEC-SAL-005: a cancelled order is terminal - no revision re-opens it for approval (a 409, like Withdrawn).
        RequireNotCancelled("İptal edilmiş siparişin fiyatı değiştirilmez.");
        if (State != SalesDraftState.Draft)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftNotDraft, "Yalnız taslak değiştirilebilir.");
        }

        if (ApprovalState == InternalApprovalState.PendingApproval)
        {
            throw StateConflict("Onay bekleyen taslağın fiyatı değiştirilmez; önce karar verilir.");
        }

        // D2: Withdrawn is terminal at every mutation entry — this one refused only PendingApproval before D2.
        if (ApprovalState == InternalApprovalState.Withdrawn)
        {
            throw StateConflict("Geri çekilmiş taslak değiştirilmez.");
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

            replacements.Add((line, PrepareFloorException(fields, actor, now)));
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

    /// <summary>
    /// Approves all current below-floor line snapshots as one keyed mutation. The approver must differ from every requester;
    /// reasons are required and bounded by the existing Sales approval-reason limit. No price is changed or recomputed here.
    /// </summary>
    public void ApprovePriceFloorExceptions(long expectedVersion, IReadOnlyList<LinePriceFloorExceptionApprovalInput> inputs,
        string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        ArgumentNullException.ThrowIfNull(inputs);
        RequireNotCancelled("İptal edilmiş siparişin fiyat istisnası onaylanmaz.");
        if (!LinesAreOpen)
        {
            throw StateConflict("Fiyat istisnası yalnız hazırlanan veya reddedilmiş taslakta onaylanır.");
        }

        var belowFloor = lines.Where(line => IsBelowFloor(line.Commercial)).ToArray();
        if (belowFloor.Length == 0 || inputs.Count != belowFloor.Length
            || inputs.Select(input => input.LineId).Distinct().Count() != inputs.Count
            || inputs.Any(input => belowFloor.All(line => line.Id != input.LineId)))
        {
            throw new SalesRuleException(SalesRuleCodes.PriceFloorExceptionLinesMismatch,
                "İstisna onayı mevcut taban altı satırların her birini tam bir kez adlandırır.");
        }

        var approvals = new List<(SalesOrderDraftLine Line, PriceFloorExceptionSnapshot Exception, string Reason)>(belowFloor.Length);
        foreach (var input in inputs)
        {
            var line = belowFloor.Single(candidate => candidate.Id == input.LineId);
            var exception = line.Commercial.FloorException
                ?? throw new SalesRuleException(SalesRuleCodes.PriceFloorExceptionPending, "Satırın onaylanabilir güncel istisna talebi yok.", line.Id);
            if (exception.IsApproved)
            {
                throw new SalesRuleException(SalesRuleCodes.RevisionNoChange, "Taban fiyat istisnası zaten onaylı.", line.Id);
            }
            if (string.Equals(exception.RequestedBy, actor, StringComparison.Ordinal))
            {
                throw new SalesRuleException(SalesRuleCodes.PriceFloorExceptionSameActor,
                    "Taban fiyat istisnasını isteyen aktör aynı istisnayı onaylayamaz.", line.Id);
            }
            var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
            if (reason is null)
            {
                throw new SalesRuleException(SalesRuleCodes.PriceFloorExceptionReasonRequired, "İstisna onay gerekçesi zorunludur.", line.Id);
            }
            if (reason.Length > MaximumRejectionReasonLength)
            {
                throw new SalesRuleException(SalesRuleCodes.ApprovalReasonTooLong,
                    $"İstisna onay gerekçesi en fazla {MaximumRejectionReasonLength} karakterdir.", line.Id);
            }
            approvals.Add((line, exception, reason));
        }

        foreach (var (line, exception, reason) in approvals)
        {
            line.ReplaceCommercial(line.Commercial with
            {
                FloorException = exception with { ApprovedBy = actor, ApprovedAtUtc = now.ToUniversalTime(), Reason = reason }
            });
        }
        Touch(now);
    }

    private static CommercialDraftFields PrepareFloorException(CommercialDraftFields commercial, string actor, DateTimeOffset now)
    {
        if (!IsBelowFloor(commercial))
        {
            return commercial with { FloorException = null };
        }
        var reference = commercial.Reference!;
        return commercial with
        {
            FloorException = new PriceFloorExceptionSnapshot(actor, now.ToUniversalTime(), reference.PriceListId, reference.VersionNo,
                commercial.PriceUnitId!.Value, commercial.UnitPrice!, SalesLinePriceCalculator.NetUnitPriceText(commercial)!,
                reference.FloorUnitPrice!)
        };
    }

    private static bool IsBelowFloor(CommercialDraftFields commercial) =>
        commercial.Reference?.FloorUnitPrice is { } floor && SalesLinePriceCalculator.NetUnitPrice(commercial) is { } net
        && net < SalesLinePriceCalculator.Parse(floor);

    private void RequirePriceFloorExceptionsApproved()
    {
        foreach (var line in lines)
        {
            RequirePriceReferenceQuantity(line, line.Quantity.Value);
        }

        if (lines.Any(line => IsBelowFloor(line.Commercial) && line.Commercial.FloorException?.IsApproved != true))
        {
            throw new SalesRuleException(SalesRuleCodes.PriceFloorExceptionPending,
                "Taban fiyatın altındaki her satır farklı yetkili tarafından gerekçeli onaylanmalıdır.");
        }
    }

    // ---- quantity revision (DEC-SAL-002, AT-023) -------------------------------------------------

    /// <summary>The technical bound of a revision reason: the note's, as the rejection and cancellation reasons'.</summary>
    public const int MaximumRevisionReasonLength = MaximumNoteLength;

    /// <summary>
    /// DEC-SAL-002 (KILITLI, verbatim): "Cari, ürün, miktar, fiyat, iskonto, para, vade ve risk istisnası değişirse yeni
    /// revizyon ve yeniden onay gerekir." — "Rev.0 korunur". AT-023: "Onaylı siparişte kritik satır değişikliği eski onayı
    /// geçersizleştirir." Replaces the ordered QUANTITY of every line — each current line named exactly once, no other — with
    /// a required reason, under the caller's quantity-write fence. The price revision's shape, step for step:
    /// <list type="bullet">
    /// <item>From <c>Preparing</c> or <c>Rejected</c> the quantities simply change (nothing approved is touched; the revision
    /// number stays). From <c>Approved</c> the approved revision is archived FIRST — its approval facts, money profile, every
    /// line's quantity text and commercial values as they were, and WHY it is superseded — and only then does the draft
    /// advance to the next revision, back to <c>Preparing</c> with no approval facts: the changed quantity re-enters
    /// approval, the previous approved snapshot is never deleted. <c>PendingApproval</c>, <c>Withdrawn</c> and a cancelled
    /// order are refused (<see cref="SalesRuleCodes.StateConflict"/>).</item>
    /// <item>Ö2 per line in its OWN order unit (never converted, never rounded); the text must be the value.</item>
    /// <item>The delivery plan is NOT re-split (DEC-SAL-003's dates-only update does not cover quantities, and no decision
    /// says how a plan follows a quantity): a new quantity below the line's planned slices is
    /// <see cref="SalesRuleCodes.SlicesExceedQuantity"/> on that line — a technical scope limit, not a policy.</item>
    /// <item>A revision that changes no line's value is refused (<see cref="SalesRuleCodes.RevisionNoChange"/>): an approval
    /// is never discarded for nothing. A value-equal text ("12.50" for 12.5) is no change and keeps the stored text.</item>
    /// </list>
    /// What other modules hold for the order (allocations, loading lines) is NOT this aggregate's knowledge: the caller reads
    /// the owners and refuses while any record exists, BEFORE this runs (the cancellation's fail-closed reading). Returns the
    /// archived snapshot when one was made (the store persists it in the same save as the draft), else null.
    /// </summary>
    public ApprovedRevisionSnapshot? ReviseQuantities(long expectedVersion, IReadOnlyList<LineQuantityInput> inputs, string actorId,
        string? reason, DateTimeOffset now)
    {
        var actor = RequireActor(actorId);
        var trimmedReason = RequireQuantityRevisable(expectedVersion, reason);
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Count != lines.Count || inputs.Any(input => input is null)
            || inputs.Select(input => input.LineId).Distinct().Count() != inputs.Count
            || inputs.Any(input => lines.All(line => line.Id != input.LineId)))
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionLinesMismatch,
                "Miktar revizyonu taslağın her satırını tam bir kez adlandırır; başka satır adlandıramaz.");
        }

        var replacements = new List<(SalesOrderDraftLine Line, Quantity Quantity, string Text)>(inputs.Count);
        foreach (var input in inputs)
        {
            var line = lines.Single(candidate => candidate.Id == input.LineId);
            Quantity quantity;
            try
            {
                quantity = RequireQuantity(input.Quantity, line.OrderUnit);
                RequireQuantityText(input.QuantityText, input.Quantity);
                var planned = line.RequestedDeliverySlices.Aggregate(Quantity.Zero(quantity.Unit), (sum, slice) => sum.Add(slice.Quantity));
                if (planned.IsGreaterThan(quantity))
                {
                    throw new SalesRuleException(SalesRuleCodes.SlicesExceedQuantity,
                        "Yeni miktar satırın termin dilimlerinin toplamından küçük; dilimler bu revizyonda yeniden bölünmez.");
                }
            }
            catch (SalesRuleException exception) when (exception.LineId is null)
            {
                // The same rule, now naming the line it is about (the caller answers lines[i].quantity).
                throw new SalesRuleException(exception.Code, exception.Message, line.Id);
            }

            replacements.Add((line, quantity, input.QuantityText));
        }

        foreach (var (line, quantity, _) in replacements)
        {
            RequirePriceReferenceQuantity(line, quantity.Value);
        }

        if (replacements.All(entry => entry.Line.Quantity.Value == entry.Quantity.Value))
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionNoChange,
                "Miktar revizyonu hiçbir satırın miktarını değiştirmiyor; onay boşuna düşürülmez.");
        }

        ApprovedRevisionSnapshot? archived = null;
        if (ApprovalState == InternalApprovalState.Approved)
        {
            // The approved revision as it stands, archived BEFORE anything of it changes — with the reason it is superseded.
            archived = new ApprovedRevisionSnapshot(Revision, Approval, Money.ProfileId, Money.ProfileVersion, Money.Scale,
                now.ToUniversalTime(), actor,
                [.. lines.Select(line => new RevisionLineSnapshot(line.Id, line.LineNo, line.Product.ProductId, line.Product.ProductVersion,
                    line.OrderUnit.UnitId, line.QuantityText, line.Commercial))],
                trimmedReason);
            archivedRevisions.Add(archived);
            Revision += 1;
            ApprovalState = InternalApprovalState.Preparing;
            Approval = ApprovalFacts.None;
        }

        foreach (var (line, quantity, text) in replacements)
        {
            // A value-equal text is no change: the stored text stays (Ö2 keeps the client's first spelling).
            if (line.Quantity.Value != quantity.Value)
            {
                line.ReplaceQuantity(quantity, text);
            }
        }

        Touch(now);
        return archived;
    }

    private static void RequirePriceReferenceQuantity(SalesOrderDraftLine line, decimal quantity)
    {
        if (line.Commercial.Reference is not { } reference) return;

        var lower = SalesLinePriceCalculator.Parse(reference.LowerQuantity);
        var upper = reference.UpperQuantity is null ? (decimal?)null : SalesLinePriceCalculator.Parse(reference.UpperQuantity);
        if (quantity < lower || upper is { } boundary && quantity >= boundary)
        {
            throw new SalesRuleException(SalesRuleCodes.PriceReferenceQuantityMismatch,
                "Yeni miktar seçili liste fiyatı kademesinin dışında; önce fiyat referansını açıkça revize edin.", line.Id);
        }
    }

    /// <summary>
    /// The quantity revision's own preconditions WITHOUT the change — the expected version, the lifecycle and approval state
    /// and the reason — so the caller can refuse a stale or wrong-state command before it asks any owner (Inventory,
    /// Logistics) what it holds. Returns the trimmed reason. <see cref="ReviseQuantities"/> runs the same checks again.
    /// </summary>
    public string RequireQuantityRevisable(long expectedVersion, string? reason)
    {
        RequireVersion(expectedVersion);
        // Not RequireDraft(): that guard closes the lines of an approved draft (DEC-SAL-002), and THIS command is the
        // revision that decision points to. DEC-SAL-005: a cancelled order is terminal (a 409, like Withdrawn).
        RequireNotCancelled("İptal edilmiş siparişin miktarı değiştirilmez.");
        if (State != SalesDraftState.Draft)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftNotDraft, "Yalnız taslak değiştirilebilir.");
        }

        if (ApprovalState == InternalApprovalState.PendingApproval)
        {
            throw StateConflict("Onay bekleyen taslağın miktarı değiştirilmez; önce karar verilir.");
        }

        if (ApprovalState == InternalApprovalState.Withdrawn)
        {
            throw StateConflict("Geri çekilmiş taslak değiştirilmez.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is null)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionReasonRequired, "Revizyon gerekçesi zorunludur.");
        }

        if (trimmedReason.Length > MaximumRevisionReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionReasonTooLong,
                $"Revizyon gerekçesi en fazla {MaximumRevisionReasonLength} karakterdir.");
        }

        return trimmedReason;
    }

    /// <summary>
    /// DEC-SAL-003 (KILITLI): "Termin üst bilgi, satır ve miktar dilimleriyle izlenir; termin güncellemesi planlama/sevkiyat
    /// rolündedir ve yeniden onay gerektirmez, ancak auditlidir." A planner moves the requested delivery dates of the
    /// header, of every line and of every slice the line already carries.
    /// <list type="bullet">
    /// <item><b>No re-approval:</b> the approval axis, its facts and the commercial revision do not move; only the
    /// concurrency version advances (one step), so consumers keyed on the revision (loading, allocation) are untouched.</item>
    /// <item><b>Dates only:</b> every current line is named exactly once and, for each, exactly its current slice ids; no
    /// slice is added, removed or re-split, no quantity changes. Re-splitting is not in the decision's text; it is a
    /// technical scope limit here, not a policy.</item>
    /// <item><b>Any state but Withdrawn:</b> the decision names no state; a withdrawn draft is terminal at every mutation
    /// entry (D2). A change that moves no date is refused (nothing empty is recorded).</item>
    /// </list>
    /// Returns the recorded schedule (the complete dates after the change) for the store to append.
    /// </summary>
    public DeliveryScheduleSnapshot RescheduleDelivery(long expectedVersion, DateOnly? requestedDeliveryDate,
        IReadOnlyList<LineScheduleInput> inputs, string actorId, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        ArgumentNullException.ThrowIfNull(inputs);
        // DEC-SAL-005: a cancelled order is terminal at every mutation entry, its schedule included (a 409, like Withdrawn).
        RequireNotCancelled("İptal edilmiş siparişin termini değiştirilmez.");
        if (State != SalesDraftState.Draft)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftNotDraft, "Yalnız taslak değiştirilebilir.");
        }

        if (ApprovalState == InternalApprovalState.Withdrawn)
        {
            throw StateConflict("Geri çekilmiş taslağın termini değiştirilmez.");
        }

        if (inputs.Count != lines.Count || inputs.Any(input => input is null)
            || inputs.Select(input => input.LineId).Distinct().Count() != inputs.Count
            || inputs.Any(input => lines.All(line => line.Id != input.LineId)))
        {
            throw new SalesRuleException(SalesRuleCodes.ScheduleLinesMismatch,
                "Termin güncellemesi taslağın her satırını tam bir kez adlandırır; başka satır adlandıramaz.");
        }

        var plan = new List<(SalesOrderDraftLine Line, DateOnly? Date, Dictionary<Guid, DateOnly> Slices)>(inputs.Count);
        foreach (var input in inputs)
        {
            var line = lines.Single(candidate => candidate.Id == input.LineId);
            if (input.Slices is null)
            {
                throw new ArgumentException("Termin güncellemesi her satır için dilim listesi ister (boş olabilir).", nameof(inputs));
            }

            var sliceDates = new Dictionary<Guid, DateOnly>(input.Slices.Count);
            foreach (var slice in input.Slices)
            {
                if (slice is null || !sliceDates.TryAdd(slice.SliceId, slice.RequestedDeliveryDate))
                {
                    throw new SalesRuleException(SalesRuleCodes.ScheduleSlicesMismatch,
                        "Termin güncellemesi satırın her dilimini tam bir kez adlandırır.", line.Id);
                }
            }

            if (sliceDates.Count != line.RequestedDeliverySlices.Count
                || line.RequestedDeliverySlices.Any(slice => !sliceDates.ContainsKey(slice.SliceId)))
            {
                throw new SalesRuleException(SalesRuleCodes.ScheduleSlicesMismatch,
                    "Termin güncellemesi yalnız tarihleri değiştirir: satırın mevcut dilimleri aynen adlandırılır; dilim eklenmez, silinmez, bölünmez.",
                    line.Id);
            }

            plan.Add((line, input.RequestedDeliveryDate, sliceDates));
        }

        var unchanged = Header.RequestedDeliveryDate == requestedDeliveryDate
            && plan.All(entry => entry.Line.RequestedDeliveryDate == entry.Date
                && entry.Line.RequestedDeliverySlices.All(slice => entry.Slices[slice.SliceId] == slice.RequestedDeliveryDate));
        if (unchanged)
        {
            throw new SalesRuleException(SalesRuleCodes.ScheduleNoChange, "Termin güncellemesi hiçbir tarihi değiştirmiyor.");
        }

        originalSchedule ??= CurrentSchedule(0, null, null);
        Header = Header with { RequestedDeliveryDate = requestedDeliveryDate };
        foreach (var (line, date, sliceDates) in plan)
        {
            line.Reschedule(date, sliceDates);
        }

        var recorded = CurrentSchedule(deliverySchedules.Count + 1, actor, now.ToUniversalTime());
        deliverySchedules.Add(recorded);
        Touch(now);
        return recorded;
    }

    /// <summary>
    /// The store's rebuild of one recorded delivery schedule, oldest first, BEFORE the approval axis (its version floor
    /// counts one step per schedule). The schedule must be the next number and name exactly the draft's lines and slices
    /// (ids, positions, quantity texts); its dates are then the draft's current dates. A stored schedule that does not fit
    /// is refused, not repaired.
    /// </summary>
    public void RestoreDeliverySchedule(DeliveryScheduleSnapshot schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        RequireApprovalNotRestored();
        if (schedule.ScheduleNo != deliverySchedules.Count + 1 || string.IsNullOrWhiteSpace(schedule.ChangedBy)
            || schedule.ChangedAtUtc is null || schedule.Lines is null || schedule.Lines.Count != lines.Count)
        {
            throw new InvalidOperationException("Kayıtlı termin geçmişi taslakla uyuşmuyor.");
        }

        var plan = new List<(SalesOrderDraftLine Line, LineScheduleSnapshot Stored)>(lines.Count);
        foreach (var line in lines)
        {
            var stored = schedule.Lines.SingleOrDefault(candidate => candidate?.LineId == line.Id);
            if (stored is null || stored.LineNo != line.LineNo || stored.Slices is null
                || stored.Slices.Count != line.RequestedDeliverySlices.Count)
            {
                throw new InvalidOperationException("Kayıtlı termin satırları taslağın satırlarıyla uyuşmuyor.");
            }

            for (var index = 0; index < line.RequestedDeliverySlices.Count; index++)
            {
                var slice = line.RequestedDeliverySlices[index];
                var storedSlice = stored.Slices.SingleOrDefault(candidate => candidate?.SliceNo == index + 1);
                if (storedSlice is null || storedSlice.SliceId != slice.SliceId
                    || !string.Equals(storedSlice.QuantityText, slice.QuantityText, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Kayıtlı termin dilimleri satırın dilimleriyle uyuşmuyor.");
                }
            }

            plan.Add((line, stored));
        }

        originalSchedule ??= CurrentSchedule(0, null, null);
        Header = Header with { RequestedDeliveryDate = schedule.RequestedDeliveryDate };
        foreach (var (line, stored) in plan)
        {
            line.Reschedule(stored.RequestedDeliveryDate,
                stored.Slices.ToDictionary(slice => slice.SliceId, slice => slice.RequestedDeliveryDate));
        }

        deliverySchedules.Add(schedule);
    }

    /// <summary>The draft's delivery dates as they stand now, as schedule <paramref name="scheduleNo"/>.</summary>
    private DeliveryScheduleSnapshot CurrentSchedule(int scheduleNo, string? changedBy, DateTimeOffset? changedAtUtc) =>
        new(scheduleNo, Header.RequestedDeliveryDate, changedBy, changedAtUtc,
            [.. lines.Select(line => new LineScheduleSnapshot(line.Id, line.LineNo, line.RequestedDeliveryDate,
                [.. line.RequestedDeliverySlices.Select((slice, index) =>
                    new SliceScheduleSnapshot(slice.SliceId, index + 1, slice.QuantityText, slice.RequestedDeliveryDate))]))]);

    /// <summary>The store's rebuild of the stored revision number (no transition; a negative number is refused).</summary>
    /// <summary>
    /// D2: who created the draft, as stored — restored by the store (the envelope's actor at the create), never set by
    /// a command; null on an aggregate that has not been stored yet. "One's own draft" is decided against this.
    /// </summary>
    public string? CreatedBy { get; private set; }

    /// <summary>The store's rebuild of the creator: once, non-blank, before the approval axis.</summary>
    public void RestoreCreator(string createdBy)
    {
        if (string.IsNullOrWhiteSpace(createdBy))
        {
            throw new ArgumentException("Kayıtlı oluşturan boş olamaz.", nameof(createdBy));
        }

        RequireApprovalNotRestored();
        if (CreatedBy is not null)
        {
            throw new InvalidOperationException("Oluşturan yalnız bir kez geri yüklenir.");
        }

        CreatedBy = createdBy.Trim();
    }

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
            || snapshot.Lines.Count == 0 || string.IsNullOrWhiteSpace(snapshot.ArchivedBy)
            || (snapshot.Reason is { } reason && (string.IsNullOrWhiteSpace(reason) || reason.Length > MaximumRevisionReasonLength)))
        {
            throw new InvalidOperationException("Kayıtlı revizyon geçmişi taslakla uyuşmuyor.");
        }

        archivedRevisions.Add(snapshot);
    }

    /// <summary>Restores the cutover-bound customer acceptance without creating a new event.</summary>
    public void RestoreCustomerAcceptance(CustomerAcceptanceFacts? facts)
    {
        if (!CustomerAcceptanceRequired)
        {
            if (facts is not null)
            {
                throw new InvalidOperationException("Müşteri kabulü aranmayan eski sipariş kabul olgusu taşıyamaz.");
            }

            return;
        }

        if (facts is not null && (facts.Revision < 0 || facts.Revision > Revision
            || string.IsNullOrWhiteSpace(facts.AcceptedBy) || facts.AcceptedBy.Length > MaximumActorLength))
        {
            throw new InvalidOperationException("Kayıtlı müşteri kabulü sipariş revizyonuyla uyuşmuyor.");
        }

        CustomerAcceptance = facts is null
            ? null
            : facts with { AcceptedBy = facts.AcceptedBy.Trim(), AcceptedAtUtc = facts.AcceptedAtUtc.ToUniversalTime() };
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
        ValidateRemainderRestore();

        var submitted = facts.SubmittedBy is not null && facts.SubmittedAtUtc is not null;
        var decided = facts.DecidedBy is not null && facts.DecidedAtUtc is not null;
        var consistent = state switch
        {
            InternalApprovalState.Preparing => facts == ApprovalFacts.None,
            InternalApprovalState.PendingApproval => submitted && facts.DecidedBy is null && facts.DecidedAtUtc is null && facts.RejectionReason is null,
            InternalApprovalState.Approved => submitted && decided && facts.RejectionReason is null,
            InternalApprovalState.Rejected => submitted && decided,
            // D2: withdrawn from Preparing (never submitted) or from PendingApproval/Rejected (submitted); the
            // decision columns carry the withdrawer; the reason is optional.
            InternalApprovalState.Withdrawn => decided && (submitted || (facts.SubmittedBy is null && facts.SubmittedAtUtc is null)),
            _ => false,
        };
        if (!consistent || (facts.RejectionReason is { } reason && (reason.Length == 0 || reason.Length > MaximumRejectionReasonLength))
            || facts.SubmittedBy is { Length: 0 } || facts.DecidedBy is { Length: 0 })
        {
            throw new InvalidOperationException("Kayıtlı onay olguları kayıtlı onay durumuyla uyuşmuyor.");
        }
        if (CustomerAcceptance?.Revision == Revision && state != InternalApprovalState.Approved)
        {
            throw new InvalidOperationException("Güncel revizyon müşteri kabulü yalnız onaylı siparişle birlikte geri yüklenir.");
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
            // D2: a withdrawal is one step from Preparing (no submission) — the floor is the WEAKER bound.
            InternalApprovalState.Withdrawn => 1,
            _ => 2,
        };
        var archivedTransitions = checked(3L * archivedRevisions.Count);
        // DEC-SAL-003: every recorded delivery-schedule change was exactly one step of its own (restored just before this).
        var scheduleTransitions = (long)deliverySchedules.Count;
        // DEC-SAL-005: a recorded cancellation was one step of its own, and only an APPROVED order is ever cancelled - a
        // stored cancellation beside any other approval state is refused, not repaired.
        if (State == SalesDraftState.Cancelled && state != InternalApprovalState.Approved)
        {
            throw new InvalidOperationException("Kayıtlı iptal yalnız onaylı bir siparişin yanında durur.");
        }

        var cancellationTransitions = State == SalesDraftState.Cancelled ? 1L : 0L;
        // Partial delivery (user decision 2026-09-30): every recorded remainder request was one step of its own, and so was
        // every decision on one (restored just before this).
        var remainderTransitions = RemainderTransitions;
        var customerAcceptanceTransitions = CustomerAcceptance is null ? 0L : 1L;
        var consistentVersion = storedVersion >= checked(Version + minimumTransitions + archivedTransitions + scheduleTransitions
            + cancellationTransitions + remainderTransitions + customerAcceptanceTransitions);
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
