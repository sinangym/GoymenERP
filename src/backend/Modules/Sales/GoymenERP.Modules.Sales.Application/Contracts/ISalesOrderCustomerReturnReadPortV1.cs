namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// Sales-declared read port answered by Logistics. It returns approved physical customer returns only;
/// pending/rejected requests are not deducted. The owner filters by the server-resolved company and
/// creates no stock, invoice, credit note or financial reversal while reading.
/// </summary>
public interface ISalesOrderCustomerReturnReadPortV1
{
    Task<SalesOrderCustomerReturnReadAnswerV1> ReadApprovedReturnsAsync(Guid salesOrderId,
        CancellationToken cancellationToken = default);
}

public sealed record SalesOrderCustomerReturnReadAnswerV1(
    Guid CompanyId,
    Guid SalesOrderId,
    IReadOnlyList<SalesOrderLineCustomerReturnV1> Lines);

public sealed record SalesOrderLineCustomerReturnV1(
    Guid SalesOrderLineId,
    Guid UnitId,
    decimal ApprovedReturnedQuantity);
