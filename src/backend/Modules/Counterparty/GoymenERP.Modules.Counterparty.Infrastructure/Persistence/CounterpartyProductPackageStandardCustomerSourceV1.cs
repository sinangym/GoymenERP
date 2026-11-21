using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>SAT-D382: actual active Customer-role evidence; display Found is insufficient.</summary>
internal sealed class CounterpartyProductPackageStandardCustomerSourceV1(CounterpartyDbContext database,
    ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority)
    : ICounterpartyProductPackageStandardCustomerSourceV1
{
    private const int MaximumIds = CounterpartyProductPackageStandardCustomerContractV1.MaximumPartyCount;
    private const string Source = CounterpartyProductPackageStandardCustomerContractV1.SourceVersion;
    public async Task<CounterpartyProductPackageStandardCustomerAnswerV1> ReadAuthorizedAsync(
        CounterpartyProductPackageStandardCustomerRequestV1 request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null || request.PartyIds is null || request.PartyIds.Count > MaximumIds
            || request.PartyIds.Any(id => id == Guid.Empty) || request.PartyIds.Distinct().Count() != request.PartyIds.Count
            || !Current(request.CompanyId, request.ActorId)) return Closed(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied);
        var companyId = request.CompanyId; var actorId = request.ActorId; var ids = request.PartyIds.Order().ToArray();
        try
        {
            if (!await FreshAsync(companyId, actorId, cancellationToken)) return Closed(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied);
            if (ids.Length == 0) return new(CounterpartyProductPackageStandardCustomerOutcomeV1.Found, companyId, actorId, [], Source);
            if (!database.Database.IsRelational()) return Closed(CounterpartyProductPackageStandardCustomerOutcomeV1.Unavailable);
            // One SQL statement, both revisions, explicit company restriction, no filter bypass or tracked cache.
            var rows = await (from account in database.CompanyPartyAccounts.AsNoTracking()
                              join party in database.Parties.AsNoTracking() on account.PartyId equals party.Id
                              where account.CompanyId == companyId && ids.Contains(account.PartyId)
                                  && account.IsActive && party.IsActive && (account.Roles & PartyRole.Customer) == PartyRole.Customer
                              orderby party.Id
                              select new { CompanyId = account.CompanyId, PartyId = party.Id, CompanyAccountId = account.Id,
                                  PartyVersion = party.Version, AccountVersion = account.Version, PartyIsActive = party.IsActive,
                                  AccountIsActive = account.IsActive, HasCustomerRole = (account.Roles & PartyRole.Customer) == PartyRole.Customer,
                                  party.Code, party.LegalName, party.TradeName })
                .Take(MaximumIds + 1).ToArrayAsync(cancellationToken);
            if (!await FreshAsync(companyId, actorId, cancellationToken)) return Closed(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied);
            if (rows.Length > ids.Length || rows.Select(row => row.PartyId).Distinct().Count() != rows.Length
                || rows.Any(row => row.CompanyId != companyId || !ids.Contains(row.PartyId) || row.CompanyAccountId == Guid.Empty
                    || row.PartyVersion < 1 || row.AccountVersion < 1 || !row.PartyIsActive || !row.AccountIsActive || !row.HasCustomerRole
                    || string.IsNullOrWhiteSpace(row.Code.Value) || string.IsNullOrWhiteSpace(row.LegalName)))
                return Closed(CounterpartyProductPackageStandardCustomerOutcomeV1.Unavailable);
            return new(CounterpartyProductPackageStandardCustomerOutcomeV1.Found, companyId, actorId,
                Array.AsReadOnly(rows.Select(row => new CounterpartyProductPackageStandardCustomerV1(row.CompanyId, row.PartyId,
                    row.CompanyAccountId, row.PartyVersion.ToString(CultureInfo.InvariantCulture), row.AccountVersion.ToString(CultureInfo.InvariantCulture),
                    row.PartyIsActive, row.AccountIsActive, row.HasCustomerRole, row.Code.Value, row.LegalName, row.TradeName)).ToArray()), Source);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Closed(CounterpartyProductPackageStandardCustomerOutcomeV1.Denied); }
        catch { return Closed(Current(companyId, actorId) ? CounterpartyProductPackageStandardCustomerOutcomeV1.Unavailable
            : CounterpartyProductPackageStandardCustomerOutcomeV1.Denied); }
    }

    private async Task<bool> FreshAsync(Guid companyId, string actorId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!Current(companyId, actorId)) return false;
        var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.ProductPackageStandardsManage, companyId, token);
        token.ThrowIfCancellationRequested();
        if (!Current(companyId, actorId)) return false;
        if (scope.IsGranted && scope.IsCompanyWide) return true;
        scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.ProductPackageStandardsApprove, companyId, token);
        token.ThrowIfCancellationRequested();
        return Current(companyId, actorId) && scope.IsGranted && scope.IsCompanyWide;
    }
    private bool Current(Guid companyId, string actorId) => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
        && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actorId) && user.UserId == actorId && user.CanAccessCompany(companyId);
    private static CounterpartyProductPackageStandardCustomerAnswerV1 Closed(CounterpartyProductPackageStandardCustomerOutcomeV1 outcome) =>
        new(outcome, Guid.Empty, string.Empty, [], Source);
}
