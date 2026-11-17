namespace GoymenERP.Modules.Finance.Contracts;

public sealed record FinanceOfficialSalesInvoiceCurrencyV1(Guid CurrencyId, string Code, string DisplayLabel);
public sealed record FinanceOfficialSalesInvoiceCurrenciesV1(Guid CompanyId,
    IReadOnlyList<FinanceOfficialSalesInvoiceCurrencyV1> Items);

/// <summary>The same Settings source and official-invoice read authority used by record/approval; no credit permission.</summary>
public interface IFinanceOfficialSalesInvoiceCandidatesV1
{
    Task<ReadResult<FinanceOfficialSalesInvoiceCurrenciesV1>> ReadCurrenciesAsync(
        FinanceContext context, CancellationToken cancellationToken);
}

/// <summary>Authorized Cari owner membership, not a guessed customer role or a new grant.</summary>
public interface IFinanceOfficialSalesInvoiceCustomerPortV1
{
    Task<ReadOutcome> CheckCompanyPartyAsync(FinanceContext context, Guid partyId, CancellationToken cancellationToken);
}
