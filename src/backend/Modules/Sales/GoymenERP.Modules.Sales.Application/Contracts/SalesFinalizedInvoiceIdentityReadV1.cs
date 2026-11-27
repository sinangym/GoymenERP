namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>Server-selected immutable finalization identity; contains no prices or current invoice state.</summary>
public sealed record SalesFinalizedInvoiceIdentityRequestV1(Guid InvoiceId, long OriginalFinalizedVersion);
public sealed record SalesFinalizedInvoiceIdentityV1(Guid CompanyId, Guid InvoiceId, long OriginalFinalizedVersion,
    Guid AdmissionId, Guid PartyId, Guid CompanyAccountId, string OfficialInvoiceNo, string Ettn,
    Guid FinalizedEventId, string SourceVersion);
public sealed record SalesFinalizedInvoiceIdentityBatchV1(Guid CompanyId, bool Available,
    IReadOnlyList<SalesFinalizedInvoiceIdentityV1> Items, string? Reason = null);

public interface ISalesFinalizedInvoiceIdentityReadPortV1
{
    Task<SalesFinalizedInvoiceIdentityBatchV1> ReadAsync(Guid companyId,
        IReadOnlyCollection<SalesFinalizedInvoiceIdentityRequestV1> invoices,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
