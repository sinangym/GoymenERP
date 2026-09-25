namespace GoymenERP.Modules.Sales.Application.Contracts;

public interface ISalesReferenceReadService
{
    // Session binding must come from server authentication, never the HTTP query.
    Task<SalesReferenceReadResult<SalesAccountOptionsV1>> GetAccountsAsync(
        SalesAccountQueryV1 query, string sessionBinding, CancellationToken cancellationToken = default);
    Task<SalesReferenceReadResult<SalesCurrencyReferenceV1>> GetCurrencyAsync(
        SalesCurrencyQueryV1 query, CancellationToken cancellationToken = default);
    Task<SalesReferenceReadResult<SalesProductReferenceV1>> GetProductAsync(
        SalesProductQueryV1 query, CancellationToken cancellationToken = default);

    // SALES-A2a: the two LIST reads the create form's selects need — the Cari owner's active delivery
    // addresses and contacts of the chosen account, and the Settings owner's currency candidates of the
    // company, each revalidated for the Sales operation on the business date. Read-only; nothing here is
    // a default or a price.
    Task<SalesReferenceReadResult<SalesContactInformationOptionsV1>> GetContactInformationAsync(
        SalesContactInformationQueryV1 query, CancellationToken cancellationToken = default);
    Task<SalesReferenceReadResult<SalesCurrencyOptionsV1>> GetCurrenciesAsync(
        SalesCurrencyListQueryV1 query, CancellationToken cancellationToken = default);
}

public sealed record SalesAccountQueryV1(string? Q = null, int PageSize = 50, string? Cursor = null);
public sealed record SalesCurrencyQueryV1(Guid CurrencyId, DateOnly EffectiveOn);
public sealed record SalesProductQueryV1(Guid ProductId, string ExpectedProductVersion,
    Guid OrderUnitId, DateOnly EffectiveOn);
public sealed record SalesReferenceReadResult<T>(int StatusCode, T? Value, string? Code) where T : class;
public sealed record SalesContactInformationQueryV1(Guid PartyId, Guid CompanyAccountId);
public sealed record SalesCurrencyListQueryV1(DateOnly EffectiveOn);

public sealed record SalesAccountOptionV1(Guid PartyId, Guid CompanyAccountId,
    string Label, string PartyVersion, string AccountVersion);
public sealed record SalesAccountOptionsV1(Guid CompanyId,
    IReadOnlyList<SalesAccountOptionV1> Accounts, string? NextCursor);
public sealed record SalesCurrencyReferenceV1(Guid CompanyId, DateOnly EffectiveOn,
    Guid CurrencyId, string Code, string Label, Guid ProfileId, string ProfileVersion);
public sealed record SalesUnitReferenceV1(Guid UnitId, string Label, Guid ProfileId, string ProfileVersion);

/// <summary>SALES-A2a: the Cari owner's ACTIVE addresses and contacts of one party account, with the versions a draft stores.</summary>
public sealed record SalesContactInformationOptionsV1(Guid CompanyId, Guid PartyId, Guid CompanyAccountId, string AccountVersion,
    IReadOnlyList<SalesAddressOptionV1> Addresses, IReadOnlyList<SalesContactOptionV1> Contacts);
/// <summary><paramref name="Text"/> is the owner's address joined for display ("Line1, Line2, District City PostalCode, CountryCode"; empty parts left out).</summary>
public sealed record SalesAddressOptionV1(Guid Id, string Version, string Purpose, string Text, string CountryCode,
    string Line1, string? Line2, string? City, string? District, string? PostalCode);
public sealed record SalesContactOptionV1(Guid Id, string Version, string DisplayName, string? JobTitle, string Purpose);
/// <summary>SALES-A2a: the company's currencies usable for a sales draft on <paramref name="EffectiveOn"/> (Settings candidates, each revalidated).</summary>
public sealed record SalesCurrencyOptionsV1(Guid CompanyId, DateOnly EffectiveOn, IReadOnlyList<SalesCurrencyReferenceV1> Currencies);
public sealed record SalesProductReferenceV1(Guid CompanyId, DateOnly EffectiveOn,
    Guid ProductId, string ProductVersion, string Label, IReadOnlyList<SalesUnitReferenceV1> OrderUnits);

// Server composition only. These values are accepted Identity/Settings bindings,
// not UI capability IDs or a new permission catalogue. Missing binding denies.
public sealed record SalesReferenceReadBindings(
    string? AccountsPermission = null, string? CurrencyPermission = null,
    string? ProductPermission = null, string? MoneyOperation = null);
