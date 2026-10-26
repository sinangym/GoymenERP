using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed class LogisticsCustomerReturnSourceReader(LogisticsDbContext dbContext, ICompanyContext companyContext)
    : ILogisticsCustomerReturnSourceReadV1
{
    public async Task<LogisticsCustomerReturnSourceV1?> FindDeliveryAsync(Guid instructionId, Guid lineId, Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        if (instructionId == Guid.Empty || lineId == Guid.Empty || deliveryId == Guid.Empty)
        {
            return null;
        }

        var rows = await (from instruction in dbContext.Instructions.AsNoTracking()
                          join line in dbContext.Lines.AsNoTracking()
                              on new { instruction.CompanyId, InstructionId = instruction.Id }
                              equals new { line.CompanyId, line.InstructionId }
                          join delivery in dbContext.Shares.AsNoTracking()
                              on new { line.CompanyId, line.InstructionId, line.LineId }
                              equals new { delivery.CompanyId, delivery.InstructionId, delivery.LineId }
                          where instruction.Id == instructionId && line.LineId == lineId
                              && delivery.ShareId == deliveryId
                              && delivery.Kind == (int)LoadingShareKind.Delivered
                          select new
                          {
                              instruction.CompanyId,
                              instruction.Version,
                              line.SourceOrderId,
                              line.SourceOrderLineId,
                              line.UnitId,
                              line.UnitIsKilogram,
                              delivery.SourceShipmentId,
                              delivery.Quantity,
                          }).Take(2).ToArrayAsync(cancellationToken);

        if (rows.Length == 0)
        {
            return null;
        }

        if (rows.Length != 1 || rows[0].SourceShipmentId is not { } shipmentId || shipmentId == Guid.Empty)
        {
            throw new LogisticsCustomerReturnServiceException(503, LogisticsCustomerReturnCodesV1.SourceInvalid);
        }

        var row = rows[0];
        var unit = row.UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
        return new LogisticsCustomerReturnSourceV1(row.CompanyId, instructionId, row.Version, lineId,
            deliveryId, shipmentId, row.SourceOrderId, row.SourceOrderLineId, row.UnitId,
            Quantity.Positive(row.Quantity, unit));
    }

    private void RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new LogisticsCustomerReturnServiceException(403, "forbidden");
        }
    }
}

internal sealed class LogisticsSalesOrderCustomerReturnReadPort(LogisticsDbContext dbContext, ICompanyContext companyContext)
    : ISalesOrderCustomerReturnReadPortV1
{
    public async Task<SalesOrderCustomerReturnReadAnswerV1> ReadApprovedReturnsAsync(Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new InvalidOperationException("Customer-return reads require a resolved company context.");
        }

        var companyId = companyContext.CompanyId;
        if (salesOrderId == Guid.Empty)
        {
            return new(companyId, salesOrderId, []);
        }

        // Aggregated and ordered in SQL over translatable members; the contract record is built after the read (SQL Server
        // cannot translate an ORDER BY over a constructor projection of a grouping).
        var totals = await dbContext.CustomerReturns.AsNoTracking()
            .Where(row => row.SourceOrderId == salesOrderId && row.Status == (int)CustomerReturnStatus.Approved)
            .GroupBy(row => new { row.SourceOrderLineId, row.UnitId })
            .Select(group => new { group.Key.SourceOrderLineId, group.Key.UnitId, Quantity = group.Sum(row => row.Quantity) })
            .OrderBy(row => row.SourceOrderLineId)
            .ThenBy(row => row.UnitId)
            .ToArrayAsync(cancellationToken);
        var lines = totals
            .Select(row => new SalesOrderLineCustomerReturnV1(row.SourceOrderLineId, row.UnitId, row.Quantity))
            .ToArray();
        return new(companyId, salesOrderId, lines);
    }
}
