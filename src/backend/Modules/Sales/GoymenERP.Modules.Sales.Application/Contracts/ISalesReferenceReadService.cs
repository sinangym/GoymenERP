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
}

public sealed record SalesAccountQueryV1(string? Q = null, int PageSize = 50, string? Cursor = null);
public sealed record SalesCurrencyQueryV1(Guid CurrencyId, DateOnly EffectiveOn);
public sealed record SalesProductQueryV1(Guid ProductId, string ExpectedProductVersion,
    Guid OrderUnitId, DateOnly EffectiveOn);
public sealed record SalesReferenceReadResult<T>(int StatusCode, T? Value, string? Code) where T : class;

public sealed record SalesAccountOptionV1(Guid PartyId, Guid CompanyAccountId,
    string Label, string PartyVersion, string AccountVersion);
public sealed record SalesAccountOptionsV1(Guid CompanyId,
    IReadOnlyList<SalesAccountOptionV1> Accounts, string? NextCursor);
public sealed record SalesCurrencyReferenceV1(Guid CompanyId, DateOnly EffectiveOn,
    Guid CurrencyId, string Code, string Label, Guid ProfileId, string ProfileVersion);
public sealed record SalesUnitReferenceV1(Guid UnitId, string Label, Guid ProfileId, string ProfileVersion);
public sealed record SalesProductReferenceV1(Guid CompanyId, DateOnly EffectiveOn,
    Guid ProductId, string ProductVersion, string Label, IReadOnlyList<SalesUnitReferenceV1> OrderUnits);

// Server composition only. These values are accepted Identity/Settings bindings,
// not UI capability IDs or a new permission catalogue. Missing binding denies.
public sealed record SalesReferenceReadBindings(
    string? AccountsPermission = null, string? CurrencyPermission = null,
    string? ProductPermission = null, string? MoneyOperation = null);
