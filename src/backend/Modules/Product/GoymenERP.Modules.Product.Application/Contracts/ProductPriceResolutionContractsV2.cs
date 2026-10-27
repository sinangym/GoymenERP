using GoymenERP.Modules.Product.Domain.PriceLists;

namespace GoymenERP.Modules.Product.Application.Contracts;

/// <summary>The SAT-D9 tier a list price came from, in chain order.</summary>
public enum ProductPriceTierV2
{
    Agreement = 1,
    Customer = 2,
    CustomerGroup = 3,
    Company = 4,
}

public enum ProductPriceTierStatusV2
{
    Matched = 1,
    Ambiguous = 2,
    NoMatch = 3,
    NotRequested = 4,
    Unavailable = 5,
    NotReached = 6,
}

public enum ProductPriceResolutionOutcomeV2
{
    /// <summary>Exactly one list of the highest answering tier prices the line: <see cref="ProductPriceResolutionV2.Reference"/>.</summary>
    Resolved = 1,

    /// <summary>Several lists of the highest answering tier price it: the candidates, none picked (which wins is not decided).</summary>
    Ambiguous = 2,

    /// <summary>No tier prices the line: no reference, no zero, no default.</summary>
    NoPrice = 3,
}

/// <summary>Whether the Product owner can give the line a package standard snapshot (master §7.3, U-07).</summary>
public enum ProductPackageStandardStatusV2
{
    /// <summary>No package-standard master exists yet: nothing is snapshotted and nothing is invented.</summary>
    Unavailable = 1,
}

/// <summary>The customer a line is priced for: the Cari party and its company account in the server-resolved company.</summary>
public sealed record ProductPriceCustomerV2(Guid PartyId, Guid CompanyAccountId);

/// <summary>
/// One line to price: the product in the exact price unit (no conversion), the quantity in that unit, the currency's ISO
/// code (matched exactly, never converted) and the business date (SAT-D10: a Europe/Istanbul calendar day —
/// <see cref="ProductPriceBusinessDayV2.DateOf"/> turns an instant into it). <paramref name="Customer"/> null leaves the
/// customer tier unasked.
/// </summary>
public sealed record ProductPriceResolutionRequestV2(Guid ProductId, Guid PriceUnitId, decimal Quantity, string CurrencyCode,
    DateOnly On, ProductPriceCustomerV2? Customer);

/// <summary>A list price as V1 carries it, plus the tier it answered at and the version's own identity.</summary>
public sealed record ProductPriceReferenceV2(
    ProductPriceTierV2 Tier,
    Guid PriceListId,
    string PriceListName,
    Guid VersionId,
    int VersionNo,
    DateOnly ValidFrom,
    DateOnly? ValidThrough,
    Guid ProductId,
    Guid PriceUnitId,
    string CurrencyCode,
    string Channel,
    string VatBasis,
    decimal UnitPrice,
    decimal? FloorUnitPrice,
    decimal LowerQuantity,
    decimal? UpperQuantity);

/// <summary>One tier as the chain met it; <paramref name="Reason"/> is a closed code for NotRequested / Unavailable.</summary>
public sealed record ProductPriceTierEvaluationV2(ProductPriceTierV2 Tier, ProductPriceTierStatusV2 Status, string? Reason);

public sealed record ProductPackageStandardSnapshotV2(ProductPackageStandardStatusV2 Status, string Reason);

/// <summary>
/// The owner's answer for one line: the outcome, the business date it was evaluated on and its time zone, the tier that
/// answered, the single reference (Resolved only), the candidates (the reference, or every list of an ambiguous tier;
/// empty on NoPrice), every tier in chain order with what it found, and the package standard snapshot.
/// </summary>
public sealed record ProductPriceResolutionV2(
    ProductPriceResolutionOutcomeV2 Outcome,
    DateOnly EffectiveOn,
    string BusinessTimeZone,
    ProductPriceTierV2? MatchedTier,
    ProductPriceReferenceV2? Reference,
    IReadOnlyList<ProductPriceReferenceV2> Candidates,
    IReadOnlyList<ProductPriceTierEvaluationV2> Tiers,
    ProductPackageStandardSnapshotV2 PackageStandard);

public static class ProductPriceResolutionReasonsV2
{
    public const string AgreementSalesContractMissing = PriceTierChain.AgreementSalesContractMissing;
    public const string CustomerGroupCariFieldMissing = PriceTierChain.CustomerGroupCariFieldMissing;
    public const string CustomerNotGiven = PriceTierChain.CustomerNotGiven;
    public const string PackageStandardOwnerMissing = "package_standard_owner_missing";
}

/// <summary>SAT-D10: the Europe/Istanbul business day of an instant (UTC stays the storage clock).</summary>
public static class ProductPriceBusinessDayV2
{
    public const string TimeZoneId = PriceListBusinessDay.TimeZoneId;

    public static DateOnly DateOf(DateTimeOffset instant) => PriceListBusinessDay.DateOf(instant);
}

/// <summary>
/// MOD-02 (SAT-D9 / SAT-D10): Product's price tier chain for one Sales order line — agreement &gt; customer-specific &gt;
/// customer group &gt; company — for the server-resolved company. V1 (<see cref="IProductQuantityPriceReferenceReaderV1"/>)
/// stays as it was for company-wide lists. Like V1 the port carries no authority: the consumer measures its own price
/// names before asking. No company context is refused before any read.
/// </summary>
public interface IProductQuantityPriceReferenceReaderV2
{
    Task<ProductPriceResolutionV2> ResolveAsync(ProductPriceResolutionRequestV2 request, CancellationToken cancellationToken = default);
}
