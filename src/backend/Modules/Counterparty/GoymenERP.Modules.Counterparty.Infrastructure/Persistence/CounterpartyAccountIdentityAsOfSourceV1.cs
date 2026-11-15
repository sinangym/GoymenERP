using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// KLN-D38 (4): the Counterparty owner's read of the identity an account's party carried on a document date (<see
/// cref="ICounterpartyAccountIdentityAsOfSourceV1"/>). Fail-closed like the e-belge profile source: a context that is not
/// the requested company, a missing company-wide <c>counterparty.parties.read</c>, or any owner failure is never "no
/// identity"; a dated history with no value for the date is <c>Unavailable</c>, never guessed. Reads only.
/// </summary>
public sealed class CounterpartyAccountIdentityAsOfSourceV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorizationService)
    : ICounterpartyAccountIdentityAsOfSourceV1
{
    public async Task<CounterpartyAccountIdentityAsOfAnswerV1> GetAuthorizedAsync(
        Guid companyId,
        Guid companyAccountId,
        DateOnly documentDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || companyAccountId == Guid.Empty || !IsCurrent(companyId))
        {
            return Answer(CounterpartyAccountIdentityAsOfOutcomeV1.Denied);
        }

        var actorId = currentUser.UserId!;
        try
        {
            var scope = await authorizationService.GetPermissionScopeAsync(
                ErpPermissions.CounterpartyPartiesRead, companyId, cancellationToken);
            if (!scope.IsGranted || !scope.IsCompanyWide || !RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountIdentityAsOfOutcomeV1.Denied);
            }

            var account = await dbContext.CompanyPartyAccounts.AsNoTracking()
                .Where(candidate => candidate.CompanyId == companyId && candidate.Id == companyAccountId)
                .Select(candidate => new { candidate.Id, candidate.CompanyId, candidate.PartyId, candidate.IsActive, candidate.Version })
                .SingleOrDefaultAsync(cancellationToken);
            if (!RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountIdentityAsOfOutcomeV1.Denied);
            }

            if (account is null || account.Version <= 0)
            {
                return Answer(account is null
                    ? CounterpartyAccountIdentityAsOfOutcomeV1.NotFound
                    : CounterpartyAccountIdentityAsOfOutcomeV1.Unavailable);
            }

            var party = await dbContext.Parties.AsNoTracking()
                .Where(candidate => candidate.Id == account.PartyId)
                .Select(candidate => new { candidate.Kind, candidate.TaxIdentityNumber, candidate.LegalName })
                .SingleOrDefaultAsync(cancellationToken);
            if (party is null)
            {
                return Answer(CounterpartyAccountIdentityAsOfOutcomeV1.Unavailable);
            }

            var revisions = await dbContext.PartyIdentityRevisions.AsNoTracking()
                .Where(revision => revision.PartyId == account.PartyId)
                .ToListAsync(cancellationToken);
            if (!TryResolve(revisions, PartyIdentityAttribute.TaxIdentityNumber, documentDate, party.TaxIdentityNumber,
                    out var taxIdentityNumber)
                || !TryResolve(revisions, PartyIdentityAttribute.LegalName, documentDate, party.LegalName,
                    out var legalName)
                || legalName is null)
            {
                return Answer(CounterpartyAccountIdentityAsOfOutcomeV1.Unavailable);
            }

            var status = string.IsNullOrEmpty(taxIdentityNumber)
                ? EDocumentStatus.Unknown
                : await ReadStatusAsOfAsync(taxIdentityNumber, documentDate, cancellationToken);
            var successor = await dbContext.PartySuccessions.AsNoTracking()
                .Where(link => link.CompanyId == companyId && link.PredecessorPartyId == account.PartyId)
                .Select(link => new { link.SuccessorPartyId, link.EffectiveOn })
                .SingleOrDefaultAsync(cancellationToken);
            if (!RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountIdentityAsOfOutcomeV1.Denied);
            }

            return new CounterpartyAccountIdentityAsOfAnswerV1(
                CounterpartyAccountIdentityAsOfOutcomeV1.Found,
                new CounterpartyAccountIdentityAsOfV1(
                    account.CompanyId,
                    account.Id,
                    account.PartyId,
                    party.Kind,
                    account.IsActive,
                    documentDate,
                    taxIdentityNumber,
                    legalName,
                    CounterpartyCommandService.ToContract(status),
                    successor?.SuccessorPartyId,
                    successor?.EffectiveOn));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Answer(CounterpartyAccountIdentityAsOfOutcomeV1.Denied);
        }
        catch (Exception)
        {
            return Answer(CounterpartyAccountIdentityAsOfOutcomeV1.Unavailable);
        }
    }

    /// <summary>
    /// The attribute's value on the date: the current value while the party has no revision of it; otherwise the dated
    /// history's value. False when a history exists but has no value for that date.
    /// </summary>
    private static bool TryResolve(
        IReadOnlyCollection<PartyIdentityRevision> revisions,
        PartyIdentityAttribute attribute,
        DateOnly documentDate,
        string? currentValue,
        out string? value)
    {
        if (!revisions.Any(revision => revision.Attribute == attribute))
        {
            value = currentValue;
            return true;
        }

        value = PartyIdentityAsOf.Resolve(revisions, attribute, documentDate)?.Value;
        return value is not null;
    }

    /// <summary>
    /// The e-belge status of <paramref name="taxIdentityNumber"/> on the date: the dated status history when the profile has
    /// one; otherwise the current status only when it was checked on or before that business day; else Unknown.
    /// </summary>
    private async Task<EDocumentStatus> ReadStatusAsOfAsync(
        string taxIdentityNumber, DateOnly documentDate, CancellationToken cancellationToken)
    {
        var profile = await dbContext.CustomerEDocumentProfiles.AsNoTracking()
            .Where(row => row.TaxIdentityNumber == taxIdentityNumber)
            .Select(row => new { row.Id, row.EDocumentStatus, row.EDocumentStatusCheckedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return EDocumentStatus.Unknown;
        }

        var statusRevisions = await dbContext.CustomerEDocumentStatusRevisions.AsNoTracking()
            .Where(revision => revision.ProfileId == profile.Id)
            .ToListAsync(cancellationToken);
        if (statusRevisions.Count != 0)
        {
            return CustomerEDocumentStatusRevision.StatusAsOf(statusRevisions, documentDate);
        }

        return profile.EDocumentStatusCheckedAtUtc is { } checkedAt
            && CounterpartyBusinessDay.DateOf(checkedAt) <= documentDate
                ? profile.EDocumentStatus
                : EDocumentStatus.Unknown;
    }

    private bool IsCurrent(Guid companyId) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    private bool RemainsCurrent(Guid companyId, string actorId) =>
        IsCurrent(companyId) && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal);

    private static CounterpartyAccountIdentityAsOfAnswerV1 Answer(CounterpartyAccountIdentityAsOfOutcomeV1 outcome) =>
        new(outcome, null);
}
