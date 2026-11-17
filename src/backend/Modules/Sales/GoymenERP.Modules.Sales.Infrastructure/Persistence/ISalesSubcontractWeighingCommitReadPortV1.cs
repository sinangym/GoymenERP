using System.Data.Common;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>Host adapter to the Logistics owner, borrowed from the Sales commit transaction. The caller has
/// already locked the Sales invoice-claim range. This port may read but never complete either SQL handle.</summary>
public interface ISalesSubcontractWeighingCommitReadPortV1
{
    Task<SalesSubcontractDeliveryWeighingReadV1> ReadAsync(DbConnection connection, DbTransaction transaction,
        Guid companyId, Guid instructionId, Guid weighingId, CancellationToken cancellationToken = default);
}
