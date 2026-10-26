using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.LogisticsRuntime;

internal sealed class LogisticsTransferOutgoingDocumentScope(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority) : ILogisticsTransferOutgoingDocumentScopeV1
{
    public async Task<LogisticsTransferOutgoingDocumentScopeV1> RequireAsync(string permission,
        CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
            throw new LogisticsTransferOutgoingDocumentServiceException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new LogisticsTransferOutgoingDocumentServiceException(403, "forbidden");
        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
            throw new LogisticsTransferOutgoingDocumentServiceException(503, "permission_unregistered");

        var companyId = company.CompanyId;
        var actorId = user.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
            throw new LogisticsTransferOutgoingDocumentServiceException(403, "forbidden");
        return new(companyId, actorId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    public async Task RequireStillAsync(LogisticsTransferOutgoingDocumentScopeV1 scope, string permission,
        CancellationToken cancellationToken = default)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId
            || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
            throw new LogisticsTransferOutgoingDocumentServiceException(403, "forbidden");
    }

    private bool Matches(Guid companyId, string actorId) => user.IsAuthenticated
        && string.Equals(user.UserId, actorId, StringComparison.Ordinal)
        && company.IsResolved && company.CompanyId == companyId && user.CanAccessCompany(companyId);
}

/// <summary>
/// Host composition seam: Logistics owns the stored document and Inventory owns the typed proof
/// contract. The adapter never writes either module and never exposes owner data on a refused proof.
/// </summary>
internal sealed class InventoryTransferOutgoingDocumentProofAdapter(
    ILogisticsTransferOutgoingDocumentProofSourceV1 source)
    : IInventoryTransferOutgoingDocumentProofPortV1
{
    public async Task<InventoryTransferOutgoingDocumentProofV1> ReadAsync(
        InventoryTransferOutgoingDocumentProofQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        LogisticsTransferOutgoingDocumentOwnerSnapshotV1? owner;
        try
        {
            owner = await source.FindAsync(query.DocumentId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return InventoryTransferOutgoingDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.Unavailable);
        }

        if (owner is null)
            return InventoryTransferOutgoingDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.Missing);

        var sameBindingExceptVersion = owner.CompanyId == query.CompanyId
            && owner.TransferId == query.TransferId
            && owner.DocumentId == query.DocumentId
            && owner.OrderedTransferLineIds.SequenceEqual(query.OrderedTransferLineIds);
        if (!sameBindingExceptVersion)
            return InventoryTransferOutgoingDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.BindingMismatch);

        if (owner.State == LogisticsTransferOutgoingDocumentProofStateV1.Draft)
            return InventoryTransferOutgoingDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.Draft);
        if (owner.State == LogisticsTransferOutgoingDocumentProofStateV1.Cancelled)
            return InventoryTransferOutgoingDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.Cancelled);
        if (owner.State != LogisticsTransferOutgoingDocumentProofStateV1.CurrentEffective)
            return InventoryTransferOutgoingDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.Unavailable);

        if (owner.DocumentVersion != query.ExpectedDocumentVersion)
            return InventoryTransferOutgoingDocumentProofV1.Refused(
                InventoryTransferDocumentProofOutcomeV1.Stale);

        return InventoryTransferOutgoingDocumentProofV1.Match(query,
            new InventoryTransferOutgoingDocumentOwnerSnapshotV1(owner.CompanyId, owner.TransferId,
                owner.DocumentId, owner.DocumentVersion, owner.OrderedTransferLineIds));
    }
}
