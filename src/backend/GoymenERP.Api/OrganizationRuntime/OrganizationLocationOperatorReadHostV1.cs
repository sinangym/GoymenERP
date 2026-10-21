using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.OrganizationRuntime;

internal sealed class OrganizationLocationOperatorReadHostV1(
    IOrganizationService organization,
    IOrganizationLocationStatusReaderV1 locationStatus,
    ICounterpartyOrganizationLocationOperatorReadSourceV1 counterparties,
    ICompanyContext companyContext,
    ICurrentUser currentUser)
    : IOrganizationLocationOperatorReadHostV1
{
    public async Task<OrganizationLocationOperatorReadResultV1<OrganizationLocationOperatorContextV1>>
        ReadDisplayAsync(
            Guid locationId,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (locationId == Guid.Empty || !TryIdentity(out var companyId, out var actorId))
        {
            return Unavailable<OrganizationLocationOperatorContextV1>();
        }

        try
        {
            var before = await organization.GetLocationAsync(locationId, cancellationToken);
            if (before is null)
            {
                return NotFound<OrganizationLocationOperatorContextV1>();
            }

            if (before.OperatorPartyId is not { } partyId)
            {
                var after = await organization.GetLocationAsync(locationId, cancellationToken);
                if (after is null)
                {
                    return NotFound<OrganizationLocationOperatorContextV1>();
                }

                if (after.Version != before.Version || after.OperatorPartyId is not null)
                {
                    return Unavailable<OrganizationLocationOperatorContextV1>();
                }

                return Success(new OrganizationLocationOperatorContextV1(locationId, null));
            }

            var answer = await counterparties.ReadAuthorizedDisplaysAsync(
                new CounterpartyOrganizationLocationOperatorDisplayRequestV1(
                    companyId,
                    actorId,
                    [new CounterpartyOrganizationLocationOperatorBindingV1(locationId, partyId)]),
                cancellationToken);
            if (answer.Outcome == CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied)
            {
                return NotFound<OrganizationLocationOperatorContextV1>();
            }

            if (answer.Outcome != CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success
                || answer.Items.Count != 1)
            {
                return Unavailable<OrganizationLocationOperatorContextV1>();
            }

            var item = answer.Items[0];
            if (item.LocationId != locationId
                || item.PartyId != partyId
                || !ValidDisplay(item.Code, item.LegalName))
            {
                return Unavailable<OrganizationLocationOperatorContextV1>();
            }

            var after = await organization.GetLocationAsync(locationId, cancellationToken);
            if (after is null)
            {
                return NotFound<OrganizationLocationOperatorContextV1>();
            }

            if (after.Version != before.Version || after.OperatorPartyId != partyId)
            {
                return Unavailable<OrganizationLocationOperatorContextV1>();
            }

            return Success(new OrganizationLocationOperatorContextV1(
                locationId,
                new OrganizationLocationOperatorDisplayV1(
                    item.PartyId,
                    item.Code,
                    item.LegalName,
                    item.TradeName,
                    item.IsCurrentlyEligible)));
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound<OrganizationLocationOperatorContextV1>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable<OrganizationLocationOperatorContextV1>();
        }
    }

    public async Task<OrganizationLocationOperatorReadResultV1<OrganizationLocationOperatorCandidatePageV1>>
        SearchCandidatesAsync(
            Guid locationId,
            OrganizationLocationOperatorCandidateQueryV1 query,
            string sessionBinding,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (locationId == Guid.Empty
            || query is null
            || query.PageSize is < 1 or > 25
            || !Canonical(sessionBinding)
            || !TryIdentity(out var companyId, out var actorId))
        {
            return Unavailable<OrganizationLocationOperatorCandidatePageV1>();
        }

        try
        {
            var before = await locationStatus.ReadAsync(companyId, locationId, cancellationToken);
            if (before is null)
            {
                return NotFound<OrganizationLocationOperatorCandidatePageV1>();
            }

            var answer = await counterparties.SearchAuthorizedCandidatesAsync(
                new CounterpartyOrganizationLocationOperatorCandidateRequestV1(
                    companyId,
                    actorId,
                    sessionBinding,
                    locationId,
                    query.Q,
                    query.PageSize,
                    query.Cursor),
                cancellationToken);
            switch (answer.Outcome)
            {
                case CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied:
                    return NotFound<OrganizationLocationOperatorCandidatePageV1>();
                case CounterpartyOrganizationLocationOperatorReadOutcomeV1.InvalidCursor:
                    return InvalidCursor<OrganizationLocationOperatorCandidatePageV1>();
                case CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable:
                    return Unavailable<OrganizationLocationOperatorCandidatePageV1>();
                case CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success:
                    break;
                default:
                    return Unavailable<OrganizationLocationOperatorCandidatePageV1>();
            }

            if (!ValidCandidates(answer, query.PageSize))
            {
                return Unavailable<OrganizationLocationOperatorCandidatePageV1>();
            }

            var after = await locationStatus.ReadAsync(companyId, locationId, cancellationToken);
            if (after is null)
            {
                return NotFound<OrganizationLocationOperatorCandidatePageV1>();
            }

            if (after != before)
            {
                return Unavailable<OrganizationLocationOperatorCandidatePageV1>();
            }

            return Success(new OrganizationLocationOperatorCandidatePageV1(
                answer.Items.Select(item => new OrganizationLocationOperatorCandidateV1(
                    item.PartyId,
                    item.Code,
                    item.LegalName,
                    item.TradeName)).ToArray(),
                answer.NextCursor,
                answer.HasMore));
        }
        catch (OrganizationCompanyContextMismatchException)
        {
            return Unavailable<OrganizationLocationOperatorCandidatePageV1>();
        }
        catch (UnauthorizedAccessException)
        {
            return NotFound<OrganizationLocationOperatorCandidatePageV1>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable<OrganizationLocationOperatorCandidatePageV1>();
        }
    }

    private bool TryIdentity(out Guid companyId, out string actorId)
    {
        companyId = companyContext.CompanyId;
        actorId = currentUser.UserId ?? string.Empty;
        return companyContext.IsResolved
            && companyId != Guid.Empty
            && currentUser.IsAuthenticated
            && currentUser.CanAccessCompany(companyId)
            && Canonical(actorId);
    }

    private static bool ValidCandidates(
        CounterpartyOrganizationLocationOperatorCandidateAnswerV1 answer,
        int pageSize)
    {
        if (answer.Items.Count > pageSize
            || answer.HasMore != (answer.NextCursor is not null)
            || answer.NextCursor is not null && !Canonical(answer.NextCursor))
        {
            return false;
        }

        var parties = new HashSet<Guid>();
        return answer.Items.All(item =>
            item.PartyId != Guid.Empty
            && ValidDisplay(item.Code, item.LegalName)
            && parties.Add(item.PartyId));
    }

    private static bool ValidDisplay(string code, string legalName) =>
        Canonical(code) && Canonical(legalName);

    private static bool Canonical(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static OrganizationLocationOperatorReadResultV1<T> Success<T>(T resource) =>
        new(OrganizationLocationOperatorReadOutcomeV1.Success, resource);

    private static OrganizationLocationOperatorReadResultV1<T> NotFound<T>() =>
        new(OrganizationLocationOperatorReadOutcomeV1.NotFound, default);

    private static OrganizationLocationOperatorReadResultV1<T> InvalidCursor<T>() =>
        new(OrganizationLocationOperatorReadOutcomeV1.InvalidCursor, default);

    private static OrganizationLocationOperatorReadResultV1<T> Unavailable<T>() =>
        new(OrganizationLocationOperatorReadOutcomeV1.Unavailable, default);
}
