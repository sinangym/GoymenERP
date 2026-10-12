using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public sealed record CounterpartyPageQuery(
    string? Query,
    string? AddressCity,
    string? AddressDistrict,
    CounterpartyAddressType? AddressType,
    PartyKind? Kind,
    PartyRole Roles,
    CounterpartyRoleMatch RoleMatch,
    CounterpartyRecordState PartyState,
    CounterpartyRecordState AccountState,
    CounterpartySort Sort,
    CounterpartySortDirection Direction,
    int PageSize,
    string? Cursor,
    bool IncludeCount);

public sealed record CounterpartyReadRequest(
    CounterpartyPageQuery Query,
    string ActorId,
    string SessionBinding);

public sealed record CounterpartyListAccount(
    Guid Id,
    Guid CompanyId,
    PartyRole Roles,
    bool IsActive,
    long Version);

public sealed record CounterpartyListItem(
    Guid Id,
    string Code,
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    bool IsActive,
    long Version,
    IReadOnlyList<CounterpartyListAccount> CompanyAccounts);

public sealed record CounterpartyPage(
    IReadOnlyList<CounterpartyListItem> Items,
    string? NextCursor,
    bool HasMore,
    long? TotalCount,
    string? CountScope);

public sealed record CounterpartyPageExecution(
    CounterpartyPageResult Result,
    CounterpartyPage? Page)
{
    public static CounterpartyPageExecution Succeeded(CounterpartyPage page) =>
        new(CounterpartyPageResult.Succeeded, page);

    public static CounterpartyPageExecution Forbidden() =>
        new(CounterpartyPageResult.Forbidden, null);

    public static CounterpartyPageExecution InvalidCursor() =>
        new(CounterpartyPageResult.InvalidCursor, null);

    public static CounterpartyPageExecution ServiceUnavailable() =>
        new(CounterpartyPageResult.ServiceUnavailable, null);
}

public enum CounterpartyPageResult
{
    Succeeded = 1,
    Forbidden = 2,
    InvalidCursor = 3,
    ServiceUnavailable = 4,
}

public enum CounterpartyRoleMatch
{
    Any = 1,
    All = 2,
}

public enum CounterpartyAddressType
{
    Billing = 1,
    Delivery = 2,
    Operational = 3,
}

public enum CounterpartyRecordState
{
    Active = 1,
    Inactive = 2,
    All = 3,
}

public enum CounterpartySort
{
    Code = 1,
    LegalName = 2,
    UpdatedAt = 3,
}

public enum CounterpartySortDirection
{
    Ascending = 1,
    Descending = 2,
}

public interface ICounterpartyCursorCodec
{
    bool IsAvailable { get; }

    string Protect(CounterpartyCursorState state);

    bool TryUnprotect(string cursor, out CounterpartyCursorState state);
}

public sealed record CounterpartyCursorState(
    int Version,
    string BindingHash,
    CounterpartySort Sort,
    string? PrimaryText,
    string? SecondaryText,
    long? PrimaryUtcTicks,
    Guid PartyId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);
