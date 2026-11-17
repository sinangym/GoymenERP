namespace GoymenERP.Modules.Sales.Application.Contracts;

public sealed record SalesSubcontractTaxRevisionV1(string ExpectedVersion, Guid LineId, SalesDraftLineTaxChoiceV1 Tax, string Reason);
public sealed record SalesSubcontractTaxOptionsV1(string Version, DateOnly BusinessDate, string BuyerSituation, SalesTaxOptionsResponse Options);
public interface ISalesSubcontractTaxServiceV1
{
    Task<SalesSubcontractTaxOptionsV1> GetSubcontractTaxOptionsAsync(Guid draftId, CancellationToken token = default);
    Task<SalesOrderDraftResponse> ReviseSubcontractTaxAsync(Guid draftId, SalesSubcontractTaxRevisionV1 request,
        SalesCommandMetadata metadata, CancellationToken token = default);
}
