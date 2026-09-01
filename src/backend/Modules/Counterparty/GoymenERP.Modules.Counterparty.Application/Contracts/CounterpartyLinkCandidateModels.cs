using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public sealed record CounterpartyLinkCandidateQuery(
    string? Query,
    int PageSize,
    string? Cursor);

public sealed record CounterpartyLinkCandidateRequest(
    CounterpartyLinkCandidateQuery Query,
    string ActorId,
    string SessionBinding);

public sealed record CounterpartyLinkCandidateSourceAccount(
    Guid Id,
    Guid CompanyId,
    PartyRole Roles,
    bool IsActive,
    string Version);

public sealed record CounterpartyLinkCandidateItem(
    Guid Id,
    string Code,
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    bool IsActive,
    IReadOnlyList<CounterpartyLinkCandidateSourceAccount> VisibleSourceAccounts);

public sealed record CounterpartyLinkCandidatePage(
    IReadOnlyList<CounterpartyLinkCandidateItem> Items,
    string? NextCursor,
    bool HasMore);

public sealed record CounterpartyLinkCandidateExecution(
    CounterpartyLinkCandidateResult Result,
    CounterpartyLinkCandidatePage? Page)
{
    public static CounterpartyLinkCandidateExecution Succeeded(
        CounterpartyLinkCandidatePage page) =>
        new(CounterpartyLinkCandidateResult.Succeeded, page);

    public static CounterpartyLinkCandidateExecution Forbidden() =>
        new(CounterpartyLinkCandidateResult.Forbidden, null);

    public static CounterpartyLinkCandidateExecution InvalidCursor() =>
        new(CounterpartyLinkCandidateResult.InvalidCursor, null);

    public static CounterpartyLinkCandidateExecution ServiceUnavailable() =>
        new(CounterpartyLinkCandidateResult.ServiceUnavailable, null);
}

public enum CounterpartyLinkCandidateResult
{
    Succeeded = 1,
    Forbidden = 2,
    InvalidCursor = 3,
    ServiceUnavailable = 4,
}

public interface ICounterpartyLinkCandidateReadService
{
    Task<CounterpartyLinkCandidateExecution> GetAuthorizedPageAsync(
        CounterpartyLinkCandidateRequest request,
        CancellationToken cancellationToken = default);
}
