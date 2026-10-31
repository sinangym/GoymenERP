using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// SAT-D102 / SAT-D119: the Counterparty owner's read of an account's customer e-belge profile (<see
/// cref="ICounterpartyAccountEDocumentProfileSourceV1"/>). The account is resolved to its party's VKN and the one
/// group-shared <see cref="CustomerEDocumentProfile"/> is read. Fail-closed: a context that is not the requested
/// company, a missing company-wide <c>counterparty.parties.read</c>, or any owner failure is never "no profile"; the
/// scenario is resolved through the Settings port only (override if listed, else the Settings default, else
/// Unresolved) and a Settings read that fails is <c>Unavailable</c>. A party with no VKN is Found with
/// <c>TaxIdentityNumberPresent = false</c>, status Unknown and an Unresolved scenario.
/// </summary>
public sealed class CounterpartyAccountEDocumentProfileSourceV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorizationService,
    ISettingsEInvoiceScenarioPolicyProviderV1? eInvoiceScenarioPolicy = null)
    : ICounterpartyAccountEDocumentProfileSourceV1
{
    public async Task<CounterpartyAccountEDocumentProfileAnswerV1> GetAuthorizedAsync(
        Guid companyId,
        Guid companyAccountId,
        DateOnly effectiveOn,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || companyAccountId == Guid.Empty || !IsCurrent(companyId))
        {
            return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Denied);
        }

        var actorId = currentUser.UserId!;
        try
        {
            var scope = await authorizationService.GetPermissionScopeAsync(
                ErpPermissions.CounterpartyPartiesRead, companyId, cancellationToken);
            if (!scope.IsGranted || !scope.IsCompanyWide || !RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Denied);
            }

            // The account must be the requested company's (existence + company fence). Its party's VKN is the shared
            // key; the profile row is group-wide (no company filter) and reached only through this company's account.
            var account = await dbContext.CompanyPartyAccounts.AsNoTracking()
                .Where(candidate => candidate.CompanyId == companyId && candidate.Id == companyAccountId)
                .Select(candidate => new { candidate.Id, candidate.CompanyId, candidate.PartyId, candidate.IsActive, candidate.Version })
                .SingleOrDefaultAsync(cancellationToken);
            if (!RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Denied);
            }

            if (account is null || account.Version <= 0)
            {
                return Answer(account is null
                    ? CounterpartyAccountEDocumentProfileOutcomeV1.NotFound
                    : CounterpartyAccountEDocumentProfileOutcomeV1.Unavailable);
            }

            var taxIdentityNumber = await dbContext.Parties.AsNoTracking()
                .Where(party => party.Id == account.PartyId)
                .Select(party => party.TaxIdentityNumber)
                .SingleOrDefaultAsync(cancellationToken);
            var profile = string.IsNullOrEmpty(taxIdentityNumber)
                ? null
                : await dbContext.CustomerEDocumentProfiles.AsNoTracking()
                    .Where(row => row.TaxIdentityNumber == taxIdentityNumber)
                    .Select(row => new
                    {
                        row.Id,
                        row.EDocumentStatus,
                        row.EDocumentStatusCheckedAtUtc,
                        row.EInvoiceScenarioOverrideCode,
                        row.Version,
                    })
                    .SingleOrDefaultAsync(cancellationToken);
            if (!RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Denied);
            }

            var status = profile?.EDocumentStatus ?? EDocumentStatus.Unknown;
            var overrideCode = profile?.EInvoiceScenarioOverrideCode;
            var (_, _, effective) = await CounterpartyEInvoiceScenarioSettings.ResolveAsync(
                eInvoiceScenarioPolicy, companyId, overrideCode, effectiveOn, cancellationToken);
            if (!RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Denied);
            }

            return new CounterpartyAccountEDocumentProfileAnswerV1(
                CounterpartyAccountEDocumentProfileOutcomeV1.Found,
                new CounterpartyAccountEDocumentProfileV1(
                    account.CompanyId,
                    account.Id,
                    account.PartyId,
                    account.IsActive,
                    !string.IsNullOrEmpty(taxIdentityNumber),
                    CounterpartyCommandService.ToContract(status),
                    profile?.EDocumentStatusCheckedAtUtc,
                    overrideCode,
                    effectiveOn,
                    effective,
                    (profile?.Version ?? 0L).ToString(CultureInfo.InvariantCulture),
                    string.IsNullOrEmpty(taxIdentityNumber)
                        ? null
                        : CounterpartyEDocumentProfileToken.Compute(taxIdentityNumber, profile?.Id)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Denied);
        }
        catch (Exception)
        {
            return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Unavailable);
        }
    }

    private bool IsCurrent(Guid companyId) =>
        currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    private bool RemainsCurrent(Guid companyId, string actorId) =>
        IsCurrent(companyId) && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal);

    private static CounterpartyAccountEDocumentProfileAnswerV1 Answer(CounterpartyAccountEDocumentProfileOutcomeV1 outcome) =>
        new(outcome, null);
}
