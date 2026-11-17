using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.Shell;

/// <summary>Historical official invoices retain their source party. This verifies company membership, not a new active/customer-role policy.</summary>
public sealed class CounterpartyFinanceOfficialInvoiceCustomerPortV1(ICompanyContext company, ICurrentUser user,
    ICounterpartyReadService counterparties) : IFinanceOfficialSalesInvoiceCustomerPortV1
{
    public async Task<ReadOutcome> CheckCompanyPartyAsync(FinanceContext context, Guid partyId, CancellationToken cancellationToken)
    {
        if (!Current(context) || partyId == Guid.Empty) return ReadOutcome.Denied;
        CounterpartyDetailView? source;
        try { source = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken); }
        catch (UnauthorizedAccessException) { return ReadOutcome.Denied; }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return Current(context) ? ReadOutcome.Unavailable : ReadOutcome.Denied; }
        if (!Current(context)) return ReadOutcome.Denied;
        if (source is null || source.Id != partyId) return ReadOutcome.NotFound;
        var accounts = source.CompanyAccounts.Where(x => x.CompanyId == context.CompanyId).ToArray();
        if (accounts.Length == 0) return ReadOutcome.NotFound;
        return accounts.Length == 1 && accounts[0].Id != Guid.Empty && PositiveVersion(accounts[0].Version) && PositiveVersion(source.Version)
            ? ReadOutcome.Found : ReadOutcome.Unavailable;
    }
    private bool Current(FinanceContext context) => company.IsResolved && company.CompanyId != Guid.Empty && company.CompanyId == context.CompanyId
        && user.IsAuthenticated && user.UserId == context.ActorId && !string.IsNullOrWhiteSpace(context.ActorId) && user.CanAccessCompany(context.CompanyId);
    private static bool PositiveVersion(string value) => long.TryParse(value, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0 && value == parsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
