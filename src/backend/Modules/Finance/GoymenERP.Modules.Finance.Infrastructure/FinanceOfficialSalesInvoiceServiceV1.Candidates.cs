using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1 : IFinanceOfficialSalesInvoiceCandidatesV1
{
    public async Task<ReadResult<FinanceOfficialSalesInvoiceCurrenciesV1>> ReadCurrenciesAsync(
        FinanceContext context, CancellationToken cancellationToken)
    {
        if (await ReadFenceAsync<FinanceOfficialSalesInvoiceCurrenciesV1>(context, Guid.Empty, cancellationToken) is { } before) return before;
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try { candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken)); }
        catch (SettingsPrerequisiteException exception)
        {
            if (await ReadFenceAsync<FinanceOfficialSalesInvoiceCurrenciesV1>(context, Guid.Empty, cancellationToken) is { } changed) return changed;
            return new(exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
        if (await ReadFenceAsync<FinanceOfficialSalesInvoiceCurrenciesV1>(context, Guid.Empty, cancellationToken) is { } after) return after;
        return new(ReadOutcome.Found, new(context.CompanyId, candidates.Select(x => new FinanceOfficialSalesInvoiceCurrencyV1(x.CurrencyId, x.Code, x.DisplayLabel)).ToArray()));
    }

    private async Task<FinanceOfficialSalesInvoiceResultV1?> CustomerRefusalAsync(FinanceContext context,
        FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, Guid partyId, CancellationToken cancellationToken)
    {
        ReadOutcome answer;
        try { answer = await customers.CheckCompanyPartyAsync(context, partyId, cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { answer = ReadOutcome.Unavailable; }
        if (await FenceAsync(context, action, invoiceId, cancellationToken) is { } changed) return changed;
        return answer switch
        {
            ReadOutcome.Found => null,
            ReadOutcome.Denied => Refused(context, CommandOutcome.Denied),
            ReadOutcome.NotFound => Refused(context, CommandOutcome.ValidationFailed, Issue("customerPartyId", "customer_party_not_in_company")),
            _ => Refused(context, CommandOutcome.Unavailable, Issue("customerPartyId", "customer_owner_unavailable")),
        };
    }
}
