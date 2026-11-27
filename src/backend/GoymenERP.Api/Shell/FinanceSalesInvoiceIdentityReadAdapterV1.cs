using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Shell;

/// <summary>Price-free owner connection; Finance chooses the scoped legacy receipt candidates.</summary>
public sealed class FinanceSalesInvoiceIdentityReadAdapterV1(ISalesFinalizedInvoiceIdentityReadPortV1 sales)
    : IFinanceSalesInvoiceIdentityReadPortV1
{
    public async Task<FinanceSalesInvoiceIdentityBatchV1> ReadAsync(Guid companyId,
        IReadOnlyCollection<FinanceSalesInvoiceIdentityRequestV1> invoices,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        await freshConsumerFence(cancellationToken);
        var answer = await sales.ReadAsync(companyId, invoices.Select(x =>
            new SalesFinalizedInvoiceIdentityRequestV1(x.InvoiceId, x.OriginalFinalizedVersion)).ToArray(),
            freshConsumerFence, cancellationToken);
        await freshConsumerFence(cancellationToken);
        if (!answer.Available || answer.CompanyId != companyId || answer.Items.Count != invoices.Count
            || answer.Items.Select(x => x.InvoiceId).Distinct().Count() != invoices.Count
            || answer.Items.Any(x => x.CompanyId != companyId || x.AdmissionId == Guid.Empty || x.PartyId == Guid.Empty
                || x.CompanyAccountId == Guid.Empty || x.FinalizedEventId == Guid.Empty || string.IsNullOrWhiteSpace(x.SourceVersion)
                || !invoices.Contains(new(x.InvoiceId, x.OriginalFinalizedVersion))))
            return new(companyId, false, [], "sales_identity_unavailable");
        return new(companyId, true, answer.Items.Select(x => new FinanceSalesInvoiceIdentityV1(x.CompanyId,
            x.InvoiceId, x.OriginalFinalizedVersion, x.AdmissionId, x.PartyId, x.CompanyAccountId,
            x.OfficialInvoiceNo, x.Ettn, x.FinalizedEventId, x.SourceVersion)).ToArray());
    }
}
