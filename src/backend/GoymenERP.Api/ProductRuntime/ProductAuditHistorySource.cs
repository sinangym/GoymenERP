using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.ProductRuntime;

/// <summary>
/// Host adapter over Audit's authorized global Product reader. Product receives only the
/// bounded projection needed for its public history contract; actor and request metadata
/// remain inside Audit.
/// </summary>
public sealed class ProductAuditHistorySource(GroupSharedProductAuditReader audit)
    : IProductAuditHistorySourceV1
{
    public async Task<IReadOnlyList<ProductAuditRecordV1>> ReadNewestAsync(Guid productId, int limit,
        CancellationToken cancellationToken)
    {
        var rows = await audit.ReadAsync(productId, limit, cancellationToken);
        return rows.Select(row =>
        {
            row.ValidateIntegrity();
            row.Envelope.Validate();
            var envelope = row.Envelope;
            return new ProductAuditRecordV1(envelope.Id, envelope.Subject.ProductId, envelope.Operation,
                envelope.OccurredAtUtc, envelope.EntityVersionBefore, envelope.EntityVersionAfter,
                envelope.Reason, envelope.BeforeJson, envelope.AfterJson);
        }).ToArray();
    }
}
