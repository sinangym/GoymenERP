namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>Finance-selected legacy AR receipt identity, resolved exclusively by the Sales owner.</summary>
public sealed record FinanceSalesInvoiceIdentityRequestV1(Guid InvoiceId, long OriginalFinalizedVersion);
public sealed record FinanceSalesInvoiceIdentityV1(Guid CompanyId, Guid InvoiceId, long OriginalFinalizedVersion,
    Guid AdmissionId, Guid PartyId, Guid CompanyAccountId, string OfficialInvoiceNo, string Ettn,
    Guid FinalizedEventId, string SourceVersion);
public sealed record FinanceSalesInvoiceIdentityBatchV1(Guid CompanyId, bool Available,
    IReadOnlyList<FinanceSalesInvoiceIdentityV1> Items, string? Reason = null);
public interface IFinanceSalesInvoiceIdentityReadPortV1
{
    Task<FinanceSalesInvoiceIdentityBatchV1> ReadAsync(Guid companyId,
        IReadOnlyCollection<FinanceSalesInvoiceIdentityRequestV1> invoices,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
