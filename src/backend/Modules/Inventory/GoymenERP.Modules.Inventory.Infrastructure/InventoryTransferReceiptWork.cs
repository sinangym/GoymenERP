using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>Actual command context, no report permission alias, no write, no customer or money data.</summary>
public sealed partial class InventoryTransferReceiptWork(ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority,
    IInventoryTransferReaderV1 transfers, IInventoryTransferReceiptReaderV1 facts, InventoryDbContext database,
    IInventoryTransferReceiptWorkCursorV1? cursors = null) : IInventoryTransferReceiptWorkV1
{
    public async Task<InventoryTransferReceiptWorkV1> ReadAsync(Guid transferId, string purpose, CancellationToken cancellationToken = default)
    {
        var id = company.IsResolved ? company.CompanyId : Guid.Empty; var actor = user.UserId ?? string.Empty;
        var permission = InventoryTransferReceiptWorkPurposesV1.Permission(purpose);
        if (transferId == Guid.Empty || permission is null) throw new InventoryTransferException(400, InventoryTransferCodesV1.ValidationFailed);
        try
        {
            await Fence();
            var transfer = await transfers.ReadAsync(transferId, cancellationToken);
            await Fence();
            if (transfer is null) throw new InventoryTransferException(404, InventoryTransferCodesV1.TransferNotFound);
            var recorded = await facts.ReadReceiptFactsAsync(transferId, cancellationToken);
            await Fence();
            if (recorded is null || transfer.CompanyId != id || recorded.CompanyId != id || transfer.TransferId != transferId
                || recorded.TransferId != transferId || transfer.Version != recorded.Version || transfer.Status != recorded.Status)
                throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
            return new InventoryTransferReceiptWorkV1(purpose, transfer, recorded);
        }
        catch (OperationCanceledException) { throw; }
        catch (InventoryTransferException) { await Fence(); throw; }
        catch { await Fence(); throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable); }

        async Task Fence()
        {
            Same();
            PermissionScope grant;
            try { grant = await authority.RevalidatePermissionScopeAsync(permission, id, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { Same(); throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable); }
            Same();
            if (!grant.IsGranted || !grant.IsCompanyWide) throw new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden);
        }
        void Same()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (id == Guid.Empty || !company.IsResolved || company.CompanyId != id || !user.IsAuthenticated
                || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(id))
                throw new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden);
        }
    }
}
