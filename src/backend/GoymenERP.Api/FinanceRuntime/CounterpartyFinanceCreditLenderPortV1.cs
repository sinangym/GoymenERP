using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>The user selects an actual Cari account; the host reads it through its authorized owner API.</summary>
public sealed class CounterpartyFinanceCreditLenderPortV1(ICompanyContext companyContext, ICurrentUser currentUser,
    ICounterpartyReadService counterparties) : IFinanceCreditLenderPortV1
{
    public async Task<FinanceCreditLenderAnswerV1> ReadAsync(Guid companyId, Guid partyId, Guid companyPartyAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = currentUser.UserId;
        FinanceCreditLenderAnswerV1 Answer(FinanceCreditLenderStatusV1 status) =>
            new(companyId, partyId, companyPartyAccountId, status, null, null, null);
        bool Current() => companyId != Guid.Empty && companyContext.IsResolved && companyContext.CompanyId == companyId
            && currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && currentUser.UserId == actor
            && currentUser.CanAccessCompany(companyId);
        if (!Current() || partyId == Guid.Empty || companyPartyAccountId == Guid.Empty)
            return Answer(FinanceCreditLenderStatusV1.Unavailable);

        CounterpartyDetailView? party;
        try { party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Answer(FinanceCreditLenderStatusV1.Unavailable);
        }
        if (!Current()) return Answer(FinanceCreditLenderStatusV1.Unavailable);
        if (party is null || party.Id != partyId) return Answer(FinanceCreditLenderStatusV1.NotFound);
        var accounts = party.CompanyAccounts.Where(x => x.Id == companyPartyAccountId && x.CompanyId == companyId).ToArray();
        if (accounts.Length != 1) return Answer(FinanceCreditLenderStatusV1.NotFound);
        var account = accounts[0];
        if (!party.IsActive || !account.IsActive) return Answer(FinanceCreditLenderStatusV1.Inactive);
        if (!Version(party.Version, out var partyVersion) || !Version(account.Version, out var accountVersion)
            || string.IsNullOrWhiteSpace(party.LegalName)) return Answer(FinanceCreditLenderStatusV1.Unavailable);
        return new(companyId, partyId, companyPartyAccountId, FinanceCreditLenderStatusV1.Available,
            party.LegalName, partyVersion, accountVersion);
    }

    private static bool Version(string text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0
        && value.ToString(CultureInfo.InvariantCulture) == text;
}

public static class FinanceCreditRuntimeExtensions
{
    public static IServiceCollection AddFinanceCreditLenderOwnerRead(this IServiceCollection services)
    {
        services.AddScoped<IFinanceCreditAccountingPeriodPortV1, FinanceCreditAccountingPeriodPortV1>();
        return services.AddScoped<IFinanceCreditLenderPortV1, CounterpartyFinanceCreditLenderPortV1>();
    }
}
