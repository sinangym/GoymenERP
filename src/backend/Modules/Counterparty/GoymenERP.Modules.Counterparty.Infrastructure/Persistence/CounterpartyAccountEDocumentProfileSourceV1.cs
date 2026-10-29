using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// SAT-D102: the Counterparty owner's read of a company account's e-belge profile (<see
/// cref="ICounterpartyAccountEDocumentProfileSourceV1"/>). Fail-closed: a context that is not the requested company, a
/// missing company-wide <c>counterparty.parties.read</c>, or any failure of the owner read is never "no profile"; the
/// scenario is resolved through the Settings port only (override if listed, else the Settings default, else
/// Unresolved) and a Settings read that fails is <c>Unavailable</c>, never a guessed code.
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

            var row = await dbContext.CompanyPartyAccounts.AsNoTracking()
                .Where(account => account.CompanyId == companyId && account.Id == companyAccountId)
                .Select(account => new
                {
                    account.Id,
                    account.CompanyId,
                    account.PartyId,
                    account.IsActive,
                    account.Version,
                    account.EDocumentStatus,
                    account.EDocumentStatusCheckedAtUtc,
                    account.EDocumentStatusCheckedByUserId,
                    account.EInvoiceScenarioOverrideCode,
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (!RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Denied);
            }

            if (row is null)
            {
                return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.NotFound);
            }

            if (row.Version <= 0)
            {
                return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Unavailable);
            }

            var (_, _, effective) = await CounterpartyEInvoiceScenarioSettings.ResolveAsync(
                eInvoiceScenarioPolicy, companyId, row.EInvoiceScenarioOverrideCode, effectiveOn, cancellationToken);
            if (!RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountEDocumentProfileOutcomeV1.Denied);
            }

            return new CounterpartyAccountEDocumentProfileAnswerV1(
                CounterpartyAccountEDocumentProfileOutcomeV1.Found,
                new CounterpartyAccountEDocumentProfileV1(
                    row.CompanyId,
                    row.Id,
                    row.PartyId,
                    row.IsActive,
                    CounterpartyCommandService.ToContract(row.EDocumentStatus),
                    row.EDocumentStatusCheckedAtUtc,
                    row.EDocumentStatusCheckedByUserId,
                    row.EInvoiceScenarioOverrideCode,
                    effectiveOn,
                    effective,
                    row.Version.ToString(CultureInfo.InvariantCulture)));
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
