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
/// The Settings owner's unit profile for the line's order unit on the business date, as the port
/// asks consumers to keep it: <see cref="ProfileScale"/> is the owner's raw scale, <see cref="Scale"/>
/// the applied one (min(profile, Ö2's cap)), the shape PUR-3 / LOG-3 / MFG-3 use. The domain does
/// not trust <see cref="Scale"/>: it derives the applied scale from <see cref="ProfileScale"/> itself
/// (<see cref="SalesOrderDraft.AppliedScale"/>) and refuses a snapshot whose <see cref="Scale"/> says
/// otherwise, so no snapshot can widen Ö2 (ÜRÜN #306 P3-1).
/// </summary>
public sealed record UnitProfileSnapshot(
    Guid CompanyId,
    Guid UnitId,
    string UnitCode,
    Guid ProfileId,
    string ProfileVersion,
    bool IsKilogram,
    int ProfileScale,
    int Scale);

/// <summary>
/// The Settings owner's money profile for the draft's currency on the business date. Mandatory
/// even for an unpriced draft (#257). Today the approved provider answers Unavailable for money,
/// so no real draft can carry one; a draft without it is never stored. No scale or rounding is
/// chosen here (T28).
/// </summary>
public sealed record MoneyProfileSnapshot(Guid CompanyId, Guid CurrencyId, Guid ProfileId, string ProfileVersion, DateOnly EffectiveOn);

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
