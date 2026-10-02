namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// A unit binding as the Product owner's snapshot names it (company, Settings unit, profile,
/// profile version as invariant text). Identities and text only; Sales validates nothing about
/// the unit itself — that is Settings' answer, carried separately as <see cref="UnitProfileSnapshot"/>.
/// </summary>
public sealed record UnitBindingSnapshot(Guid CompanyId, Guid UnitId, Guid ProfileId, string ProfileVersion);

/// <summary>
/// What the Product owner answered for one selection (#257 CONTRACT_V1 "persist owner snapshot …
/// all StockUnit/SalesUnit/OrderUnit bindings"): retained with the line inside the same
/// consistency boundary. It authorizes nothing and is never rebuilt from Draft/Sellable/Complete.
/// </summary>
public sealed record ProductSnapshot(
    Guid ProductId,
    string ProductVersion,
    string Code,
    string Name,
    DateOnly EffectiveOn,
    UnitBindingSnapshot StockUnit,
    UnitBindingSnapshot SalesUnit,
    UnitBindingSnapshot OrderUnit);

/// <summary>
/// The Settings owner's unit profile for the line's order unit, as the port publishes it and asks
/// consumers to keep it: identity, dimension, the owner's raw <see cref="ProfileScale"/> and the
/// applied <see cref="Scale"/> (min(profile, Ö2's cap) — the PUR-3 / LOG-3 / MFG-3 shape), and the
/// owner's validity (<see cref="IsActive"/>, <see cref="EffectiveFrom"/>, <see cref="EffectiveThrough"/>).
/// The domain trusts none of the derived parts: it derives the applied scale from
/// <see cref="ProfileScale"/> itself (<see cref="SalesOrderDraft.AppliedScale"/>) and refuses a
/// snapshot whose <see cref="Scale"/> says otherwise (ÜRÜN #306 P3-1 / CARİ P2-1), and it refuses
/// a profile that is inactive or not effective on the business date (CARİ #306 P2-2, the PUR-3
/// verifier's rule) — the Settings port's obligation, enforced where the snapshot is consumed.
/// </summary>
public sealed record UnitProfileSnapshot(
    Guid CompanyId,
    Guid UnitId,
    string UnitCode,
    string DimensionCode,
    Guid ProfileId,
    string ProfileVersion,
    bool IsKilogram,
    int ProfileScale,
    int Scale,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough);

/// <summary>
/// The Settings owner's money profile for the draft's currency on the business date, as answered
/// for the Sales operation <see cref="SalesOrderDraft.MoneyOperation"/>. Mandatory even for an
/// unpriced draft (#257); <see cref="EffectiveOn"/> must be the business date and
/// <see cref="Operation"/> must be the Sales draft operation — an answer for another operation is
/// another answer. The owner's validity travels with it (<see cref="IsActive"/>,
/// <see cref="EffectiveFrom"/>, <see cref="EffectiveThrough"/>) and the draft refuses a profile not
/// in effect on the business date, the same obligation the unit side holds (CARİ #306 P3-R1).
/// <see cref="Scale"/> is the owner's money scale, kept as published; Sales applies it to nothing
/// in this slice (prices are unvalidated text, T28) and chooses no rounding. Today the approved
/// provider answers Unavailable for money, so no real draft can carry one; a draft without it is
/// never stored.
/// </summary>
public sealed record MoneyProfileSnapshot(
    Guid CompanyId,
    Guid CurrencyId,
    string Operation,
    Guid ProfileId,
    string ProfileVersion,
    DateOnly EffectiveOn,
    bool IsActive,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    int Scale);

/// <summary>
/// A nullable owner reference the client supplied (delivery address, contact, responsible,
/// payment terms, source strategy, package). Stored as given — id and expected version text —
/// and NOT validated by its owner in this slice (#257's owner-binding list; dependency_wait).
/// </summary>
public sealed record OwnerReference(Guid Id, string ExpectedVersion);

/// <summary>
/// Commercial fields retained verbatim as text and NOT validated (T28: price, discount, tax,
/// exchange rate are out of scope; the Settings money profile is Unavailable). Missing and null
/// both mean unknown; a literal zero is distinguishable from unknown but approves no free-of-charge
/// policy. The only rule held here is #257's pairing: a price needs its price unit and a price
/// unit needs a price.
/// </summary>
/// <para>
/// AT-018 / AT-021 (user decision 2026-09-30): the list price of a Product price list is carried as a REFERENCE beside
/// the line's own unit price - the default until the person changes it - and the adjustments stay visible: a discount as
/// a percent OR an amount, and a maturity surcharge (vade farkı) as a percent OR an amount. The net is never stored; it is
/// <see cref="SalesLinePriceCalculator"/>'s answer from these values.
/// </para>
public sealed record CommercialDraftFields(string? UnitPrice, Guid? PriceUnitId, string? DiscountPercent,
    PriceReferenceSnapshot? Reference = null, string? DiscountAmount = null, string? SurchargePercent = null,
    string? SurchargeAmount = null, PriceFloorExceptionSnapshot? FloorException = null)
{
    public static CommercialDraftFields None { get; } = new(null, null, null);

    /// <summary>Whether anything beyond K3's three fields is present (the reference or an AT-018 adjustment).</summary>
    public bool HasPricingDetail => Reference is not null || DiscountAmount is not null || SurchargePercent is not null
        || SurchargeAmount is not null || FloorException is not null;

    /// <summary>"Reference" when the unit price is the reference's list price, "Manual" when it was overwritten; null without a price.</summary>
    public string? PriceSource => UnitPrice is null ? null
        : Reference is not null && SalesLinePriceCalculator.SameValue(Reference.UnitPrice, UnitPrice) ? "Reference" : "Manual";
}

/// <summary>
/// AT-018 / AT-021: the Product price list price a line was priced from, copied at the revision (a later version of the list
/// never changes it): the list, its version and validity, the currency and VAT basis, the price unit, the list price and the
/// optional floor, all as exact text.
/// </summary>
public sealed record PriceReferenceSnapshot(Guid PriceListId, string PriceListName, int VersionNo, DateOnly ValidFrom, DateOnly? ValidThrough,
    string CurrencyCode, Guid PriceUnitId, string UnitPrice, string? FloorUnitPrice, string VatBasis,
    string LowerQuantity, string? UpperQuantity);

/// <summary>
/// The reasoned maker-checker exception for one exact below-floor commercial snapshot. Pending has no approver facts;
/// approved has all three. Any commercial revision replaces this snapshot, so approval never floats to another price,
/// list version or calculated net.
/// </summary>
public sealed record PriceFloorExceptionSnapshot(
    string RequestedBy, DateTimeOffset RequestedAtUtc, Guid PriceListId, int PriceListVersionNo, Guid PriceUnitId,
    string UnitPrice, string NetUnitPrice, string FloorUnitPrice,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAtUtc = null, string? Reason = null)
{
    public bool IsApproved => ApprovedBy is not null && ApprovedAtUtc is not null && Reason is not null;
}

/// <summary>One current below-floor line and the approver's required reason.</summary>
public sealed record LinePriceFloorExceptionApprovalInput(Guid LineId, string Reason);

/// <summary>K3 Head B: one line's new commercial fields in a commercial revision, addressed by the client line id.</summary>
public sealed record LineCommercialInput(Guid LineId, CommercialDraftFields Commercial);

/// <summary>
/// DEC-SAL-002: one line's new ordered quantity in a quantity revision, addressed by the client line id — the client's text
/// kept verbatim and its exact value (Ö2: validated against the line's own order unit, never rounded, never converted).
/// </summary>
public sealed record LineQuantityInput(Guid LineId, string QuantityText, decimal Quantity);

/// <summary>A line as it stood in an approved revision: identity, quantity text and the commercial fields, kept verbatim.</summary>
public sealed record RevisionLineSnapshot(
    Guid LineId, int LineNo, Guid ProductId, string ProductVersion, Guid OrderUnitId, string QuantityText,
    CommercialDraftFields Commercial);

/// <summary>
/// K3 (Sinan 5748258299): an APPROVED revision archived the moment its price or discount was changed —
/// the approval facts, the money profile it was approved under and every line's commercial values as
/// they were. The previous approved snapshot is never deleted; the draft goes on as the next revision.
/// DEC-SAL-002: a quantity revision archives the same way; <see cref="Reason"/> is WHY this approved revision was
/// superseded — required by the quantity revision, null for a price revision (which carries no reason member).
/// </summary>
public sealed record ApprovedRevisionSnapshot(
    int Revision, ApprovalFacts Approval, Guid MoneyProfileId, string MoneyProfileVersion, int MoneyScale,
    DateTimeOffset ArchivedAtUtc, string ArchivedBy, IReadOnlyList<RevisionLineSnapshot> Lines, string? Reason = null,
    CustomerAcceptanceFacts? CustomerAcceptance = null);

/// <summary>DEC-SAL-003: one slice's new requested delivery date, addressed by the slice id the line already carries.</summary>
public sealed record SliceScheduleInput(Guid SliceId, DateOnly RequestedDeliveryDate);

/// <summary>
/// DEC-SAL-003: one line's new delivery dates — the line's own requested date (null = none) and a date for EACH of
/// its current slices. Dates only: the slice ids and quantities stay as they are.
/// </summary>
public sealed record LineScheduleInput(Guid LineId, DateOnly? RequestedDeliveryDate, IReadOnlyList<SliceScheduleInput> Slices);

/// <summary>A slice's dates in a delivery schedule: the stable identity, its position, its (unchanged) quantity text and the date.</summary>
public sealed record SliceScheduleSnapshot(Guid SliceId, int SliceNo, string QuantityText, DateOnly RequestedDeliveryDate);

/// <summary>A line's dates in a delivery schedule.</summary>
public sealed record LineScheduleSnapshot(Guid LineId, int LineNo, DateOnly? RequestedDeliveryDate, IReadOnlyList<SliceScheduleSnapshot> Slices);

/// <summary>
/// DEC-SAL-003 ("termin güncellemesi planlama/sevkiyat rolündedir ve yeniden onay gerektirmez, ancak auditlidir"): the
/// complete delivery dates of a draft after one recorded schedule change — the header's, every line's and every slice's —
/// with who changed them and when. Schedule 0 is the draft as created (no actor, no time); each recorded change is the
/// next number. Append-only: an earlier schedule is never rewritten.
/// </summary>
public sealed record DeliveryScheduleSnapshot(
    int ScheduleNo, DateOnly? RequestedDeliveryDate, string? ChangedBy, DateTimeOffset? ChangedAtUtc,
    IReadOnlyList<LineScheduleSnapshot> Lines);

/// <summary>
/// DEC-SAL-005 / AT-007: the recorded cancellation of an approved order — the commercial revision that was cancelled, who
/// cancelled it, when, and the reason (required). Recorded once; the approval facts, the lines, the archived revisions and
/// the delivery schedules stay exactly as they were ("İptal ve ters işlem geçmişi silmez"). What the owners held when it was
/// cancelled (the impact the user saw) is kept by the store beside it; the domain decides only the transition.
/// </summary>
public sealed record CancellationFacts(
    int Revision, string CancelledBy, DateTimeOffset CancelledAtUtc, string Reason, CancellationImpactSnapshot Impact);

/// <summary>
/// DEC-SAL-005: the impact preview the cancellation was decided on, as the caller computed it from the owners' answers —
/// its token (what the user confirmed) and each source's status (owner answered nothing / no source on main). Opaque text
/// to the domain: it is kept with the cancellation as history, never interpreted here.
/// </summary>
public sealed record CancellationImpactSnapshot(string Token, IReadOnlyList<CancellationImpactEntry> Sources);

/// <summary>One impact source (e.g. <c>inventory.active-allocations</c>) and the status it had when the order was cancelled.</summary>
public sealed record CancellationImpactEntry(string Source, string Status);
