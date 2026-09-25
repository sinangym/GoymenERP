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
public sealed record CommercialDraftFields(string? UnitPrice, Guid? PriceUnitId, string? DiscountPercent)
{
    public static CommercialDraftFields None { get; } = new(null, null, null);
}

/// <summary>K3 Head B: one line's new commercial fields in a commercial revision, addressed by the client line id.</summary>
public sealed record LineCommercialInput(Guid LineId, CommercialDraftFields Commercial);

/// <summary>A line as it stood in an approved revision: identity, quantity text and the commercial fields, kept verbatim.</summary>
public sealed record RevisionLineSnapshot(
    Guid LineId, int LineNo, Guid ProductId, string ProductVersion, Guid OrderUnitId, string QuantityText,
    CommercialDraftFields Commercial);

/// <summary>
/// K3 (Sinan 5748258299): an APPROVED revision archived the moment its price or discount was changed —
/// the approval facts, the money profile it was approved under and every line's commercial values as
/// they were. The previous approved snapshot is never deleted; the draft goes on as the next revision.
/// </summary>
public sealed record ApprovedRevisionSnapshot(
    int Revision, ApprovalFacts Approval, Guid MoneyProfileId, string MoneyProfileVersion, int MoneyScale,
    DateTimeOffset ArchivedAtUtc, string ArchivedBy, IReadOnlyList<RevisionLineSnapshot> Lines);
