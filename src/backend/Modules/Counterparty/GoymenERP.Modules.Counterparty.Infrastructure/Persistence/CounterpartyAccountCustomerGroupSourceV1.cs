using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// SAT-D57: the Counterparty owner's read of a company account's customer group (<see
/// cref="ICounterpartyAccountCustomerGroupSourceV1"/>). Fail-closed: a context that is not the requested company, a
/// missing company-wide <c>counterparty.parties.read</c>, or any failure of the owner read is never "no group". A
/// passive group's existing assignment is answered Found with its id (SAT-D77: it keeps pricing).
/// </summary>
public sealed class CounterpartyAccountCustomerGroupSourceV1(
    CounterpartyDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorizationService authorizationService)
    : ICounterpartyAccountCustomerGroupSourceV1
{
    public async Task<CounterpartyAccountCustomerGroupAnswerV1> GetAuthorizedAsync(
        Guid companyId,
        Guid companyAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || companyAccountId == Guid.Empty || !IsCurrent(companyId))
        {
            return Answer(CounterpartyAccountCustomerGroupOutcomeV1.Denied);
        }

        var actorId = currentUser.UserId!;
        try
        {
            var scope = await authorizationService.GetPermissionScopeAsync(
                ErpPermissions.CounterpartyPartiesRead, companyId, cancellationToken);
            if (!scope.IsGranted || !scope.IsCompanyWide || !RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountCustomerGroupOutcomeV1.Denied);
            }

            var row = await (
                    from account in dbContext.CompanyPartyAccounts.AsNoTracking()
                    where account.CompanyId == companyId && account.Id == companyAccountId
                    join customerGroup in dbContext.CustomerGroups.AsNoTracking()
                        on new { account.CompanyId, GroupId = account.CustomerGroupId }
                        equals new { customerGroup.CompanyId, GroupId = (Guid?)customerGroup.Id } into groups
                    from customerGroup in groups.DefaultIfEmpty()
                    select new
                    {
                        account.Id,
                        account.CompanyId,
                        account.PartyId,
                        account.IsActive,
                        account.Version,
                        account.CustomerGroupId,
                        GroupId = (Guid?)customerGroup!.Id,
                        GroupIsActive = (bool?)customerGroup!.IsActive,
                    })
                .SingleOrDefaultAsync(cancellationToken);
            if (!RemainsCurrent(companyId, actorId))
            {
                return Answer(CounterpartyAccountCustomerGroupOutcomeV1.Denied);
            }

            if (row is null)
            {
                return Answer(CounterpartyAccountCustomerGroupOutcomeV1.NotFound);
            }

            // A group id the owner cannot read back is "could not be read", never "no group" (SAT-D58).
            if (row.CustomerGroupId != row.GroupId || row.Version <= 0)
            {
                return Answer(CounterpartyAccountCustomerGroupOutcomeV1.Unavailable);
            }

            return new CounterpartyAccountCustomerGroupAnswerV1(
                CounterpartyAccountCustomerGroupOutcomeV1.Found,
                new CounterpartyAccountCustomerGroupV1(
                    row.CompanyId,
                    row.Id,
                    row.PartyId,
                    row.IsActive,
                    row.CustomerGroupId,
                    row.CustomerGroupId is null ? null : row.GroupIsActive,
                    row.Version.ToString(CultureInfo.InvariantCulture)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Answer(CounterpartyAccountCustomerGroupOutcomeV1.Denied);
        }
        catch (Exception)
        {
            return Answer(CounterpartyAccountCustomerGroupOutcomeV1.Unavailable);
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

    private static CounterpartyAccountCustomerGroupAnswerV1 Answer(CounterpartyAccountCustomerGroupOutcomeV1 outcome) =>
        new(outcome, null);
}
