using System.Data.Common;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Api.Endpoints;

/// <summary>Rereads the actual Logistics owner after the Sales invoice range has been locked.
/// Disposal releases only the read context; the caller retains its transaction and its locks.</summary>
public sealed class SalesSubcontractWeighingCommitReadHost : ISalesSubcontractWeighingCommitReadPortV1
{
    public async Task<SalesSubcontractDeliveryWeighingReadV1> ReadAsync(DbConnection connection, DbTransaction transaction,
        Guid companyId, Guid instructionId, Guid weighingId, CancellationToken cancellationToken = default)
    {
        await using var owner = await LogisticsSubcontractDeliveryReadSession.EnlistAsync(
            connection, transaction, companyId, cancellationToken);
        return await new SalesSubcontractDeliveryWeighingHost(owner).ReadAsync(
            companyId, instructionId, weighingId, cancellationToken);
    }
}
