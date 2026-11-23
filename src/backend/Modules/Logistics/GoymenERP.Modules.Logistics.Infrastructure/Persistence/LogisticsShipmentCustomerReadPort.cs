using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// STK-S33: the Logistics-owned answer to <see cref="ILogisticsShipmentCustomerReadPortV1"/>. It reads Logistics's own
/// company-filtered instruction lines joined to their instruction (explicit company on both sides), untracked, and creates
/// nothing.
/// </summary>
internal sealed class LogisticsShipmentCustomerReadPort(LogisticsDbContext dbContext, ICompanyContext companyContext)
    : ILogisticsShipmentCustomerReadPortV1
{
    public async Task<IReadOnlyList<LogisticsShipmentCustomerV1>> ReadByInstructionLinesAsync(
        IReadOnlyCollection<Guid> instructionLineIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instructionLineIds);
        cancellationToken.ThrowIfCancellationRequested();
        var companyId = RequireCompany();
        if (instructionLineIds.Count > ILogisticsShipmentCustomerReadPortV1.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(instructionLineIds),
                $"En fazla {ILogisticsShipmentCustomerReadPortV1.MaximumBatchSize} talimat satırı birlikte okunur.");
        }

        var ids = instructionLineIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await (
                from line in dbContext.Lines.AsNoTracking()
                join instruction in dbContext.Instructions.AsNoTracking()
                    on new { line.CompanyId, Id = line.InstructionId } equals new { instruction.CompanyId, instruction.Id }
                where line.CompanyId == companyId && instruction.CompanyId == companyId && ids.Contains(line.LineId)
                orderby line.LineId
                select new LogisticsShipmentCustomerV1(
                    line.LineId,
                    instruction.Id,
                    instruction.CustomerPartyId,
                    line.SourceOrderId,
                    line.SourceOrderLineId))
            .ToListAsync(cancellationToken);
    }

    private Guid RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Sevk müşterisi okuması yalnız çözümlenmiş şirket bağlamında yapılır.");
        }

        return companyContext.CompanyId;
    }
}
