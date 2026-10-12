using System.Globalization;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

public sealed class CounterpartyManufacturingPartyAccountSourceV1(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICounterpartyReadService counterparties)
    : ICounterpartyManufacturingPartyAccountSourceV1
{
    private const string SessionBinding = "counterparty.manufacturing-party-accounts.v1";
    private const int MaximumLimit = 100;
    private const PartyRole ManufacturingRoles = PartyRole.Customer | PartyRole.Tolling;

    public async Task<CounterpartyManufacturingPartyAccountSearchAnswerV1> SearchAuthorizedAsync(
        CounterpartyManufacturingPartyAccountSearchV1 query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent())
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Denied);
        }

        var canonicalization = TryCanonicalize(query, out var canonicalQuery);
        if (canonicalization == QueryCanonicalization.InvalidCursor)
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.InvalidCursor);
        }

        if (canonicalization != QueryCanonicalization.Succeeded)
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        CounterpartyPageExecution execution;
        try
        {
            execution = await counterparties.GetAuthorizedPageAsync(
                new CounterpartyReadRequest(
                    new CounterpartyPageQuery(
                        canonicalQuery.Query,
                        AddressCity: null,
                        AddressDistrict: null,
                        AddressType: null,
                        Kind: null,
                        ManufacturingRoles,
                        CounterpartyRoleMatch.Any,
                        CounterpartyRecordState.All,
                        CounterpartyRecordState.All,
                        CounterpartySort.Code,
                        CounterpartySortDirection.Ascending,
                        canonicalQuery.Limit,
                        canonicalQuery.Cursor,
                        IncludeCount: false),
                    actorId,
                    SessionBinding),
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable);
        }

        if (!RemainsCurrent(companyId, actorId))
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Denied);
        }

        if (execution.Result == CounterpartyPageResult.Forbidden)
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Denied);
        }

        if (execution.Result == CounterpartyPageResult.InvalidCursor)
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.InvalidCursor);
        }

        if (execution.Result != CounterpartyPageResult.Succeeded || execution.Page is null)
        {
            return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable);
        }

        var items = new List<CounterpartyManufacturingPartyAccountCandidateV1>(execution.Page.Items.Count);
        var partyIds = new HashSet<Guid>();
        var accountIds = new HashSet<Guid>();
        foreach (var party in execution.Page.Items)
        {
            var accounts = party.CompanyAccounts
                .Where(account => account.CompanyId == companyId && (account.Roles & ManufacturingRoles) != 0)
                .ToArray();
            if (accounts.Length != 1
                || party.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(party.Code)
                || string.IsNullOrWhiteSpace(party.LegalName)
                || !TryFormatVersion(party.Version, out var partyVersion)
                || !partyIds.Add(party.Id))
            {
                return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable);
            }

            var account = accounts[0];
            if (account.Id == Guid.Empty
                || !TryFormatVersion(account.Version, out var accountVersion)
                || !accountIds.Add(account.Id))
            {
                return Search(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable);
            }

            items.Add(new CounterpartyManufacturingPartyAccountCandidateV1(
                party.Id,
                account.Id,
                companyId,
                party.Code,
                party.LegalName,
                party.TradeName,
                party.IsActive,
                account.IsActive,
                account.Roles.HasFlag(PartyRole.Customer),
                account.Roles.HasFlag(PartyRole.Tolling),
                partyVersion,
                accountVersion));
        }

        return new CounterpartyManufacturingPartyAccountSearchAnswerV1(
            CounterpartyManufacturingPartyAccountOutcomeV1.Found,
            items,
            execution.Page.NextCursor,
            execution.Page.HasMore);
    }

    public async Task<CounterpartyManufacturingPartyAccountAnswerV1> GetAuthorizedAsync(
        Guid partyId,
        Guid companyAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent() || partyId == Guid.Empty || companyAccountId == Guid.Empty)
        {
            return Exact(CounterpartyManufacturingPartyAccountOutcomeV1.Denied);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Exact(CounterpartyManufacturingPartyAccountOutcomeV1.Denied);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Exact(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable);
        }

        if (!RemainsCurrent(companyId, actorId))
        {
            return Exact(CounterpartyManufacturingPartyAccountOutcomeV1.Denied);
        }

        if (party is null || party.Id != partyId)
        {
            return Exact(CounterpartyManufacturingPartyAccountOutcomeV1.NotFound);
        }

        var accounts = party.CompanyAccounts
            .Where(account => account.Id == companyAccountId && account.CompanyId == companyId)
            .ToArray();
        if (accounts.Length == 0)
        {
            return Exact(CounterpartyManufacturingPartyAccountOutcomeV1.NotFound);
        }

        if (accounts.Length != 1
            || !TryOpaqueVersion(party.Version, out var partyVersion)
            || !TryOpaqueVersion(accounts[0].Version, out var accountVersion))
        {
            return Exact(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable);
        }

        var account = accounts[0];
        return new CounterpartyManufacturingPartyAccountAnswerV1(
            CounterpartyManufacturingPartyAccountOutcomeV1.Found,
            new CounterpartyManufacturingPartyAccountV1(
                party.Id,
                account.Id,
                account.CompanyId,
                party.IsActive,
                account.IsActive,
                account.Roles.HasFlag(PartyRole.Customer),
                account.Roles.HasFlag(PartyRole.Tolling),
                partyVersion,
                accountVersion));
    }

    private bool IsCurrent() =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId);

    private bool RemainsCurrent(Guid companyId, string actorId) =>
        IsCurrent()
        && companyContext.CompanyId == companyId
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal);

    private static QueryCanonicalization TryCanonicalize(
        CounterpartyManufacturingPartyAccountSearchV1? query,
        out CounterpartyManufacturingPartyAccountSearchV1 canonical)
    {
        canonical = null!;
        if (query is null || query.Limit is <= 0 or > MaximumLimit)
        {
            return QueryCanonicalization.InvalidQuery;
        }

        if (query.Cursor is not null
            && (query.Cursor.Length == 0
                || !string.Equals(query.Cursor, query.Cursor.Trim(), StringComparison.Ordinal)))
        {
            return QueryCanonicalization.InvalidCursor;
        }

        string? normalizedQuery = null;
        if (query.Query is not null)
        {
            normalizedQuery = query.Query.Trim().Normalize(NormalizationForm.FormC);
            if (normalizedQuery.Length == 0)
            {
                normalizedQuery = null;
            }
            else if (normalizedQuery.EnumerateRunes().Count() > 200)
            {
                return QueryCanonicalization.InvalidQuery;
            }
        }

        canonical = query with { Query = normalizedQuery };
        return QueryCanonicalization.Succeeded;
    }

    private static bool TryFormatVersion(long value, out string version)
    {
        version = value.ToString(CultureInfo.InvariantCulture);
        return value > 0;
    }

    private static bool TryOpaqueVersion(string value, out string version)
    {
        version = value;
        return !string.IsNullOrEmpty(value);
    }

    private static CounterpartyManufacturingPartyAccountSearchAnswerV1 Search(
        CounterpartyManufacturingPartyAccountOutcomeV1 outcome) => new(outcome, [], null, false);

    private static CounterpartyManufacturingPartyAccountAnswerV1 Exact(
        CounterpartyManufacturingPartyAccountOutcomeV1 outcome) => new(outcome, null);

    private enum QueryCanonicalization
    {
        Succeeded,
        InvalidQuery,
        InvalidCursor,
    }
}
