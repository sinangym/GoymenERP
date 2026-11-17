using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>Bounded owner observation under the consuming transfer permission. No extra report permission or stock write.</summary>
internal sealed class InventoryTransferPlanCandidates(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IInventoryStockPositionReader? source) : IInventoryTransferPlanCandidatesV1
{
    public async Task<InventoryTransferPlanCandidatesV1> SearchAsync(string? query, int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        try
        {
            await FenceAsync();
            var q = query?.Trim();
            if (q?.Length > MaterialLabel.MaximumCodeLength || q?.Any(char.IsControl) == true || limit is < 1 or > 100)
                throw new InventoryTransferException(400, InventoryTransferCodesV1.ValidationFailed);
            q = string.IsNullOrWhiteSpace(q) ? null : q;
            if (source is null) throw Unavailable();
            var window = await source.SearchAsync(companyId,
                new InventoryStockPositionReadQuery(q ?? "", limit, null, null, null, null, null, null,
                    null, null, null, null, null, null, null), cancellationToken);
            await FenceAsync();
            if (window.Records is null || window.Records.Count > limit) throw Unavailable();
            var projected = window.Records.Select(item => InventoryStockPositionService.View(item, companyId)).ToArray();
            if (projected.Select(x => x.LabelId).Distinct().Count() != projected.Length) throw Unavailable();
            var items = projected.Select(x => new InventoryTransferPlanCandidateV1(x.LabelId, x.LabelCode, x.LabelVersion,
                x.ProductId, x.ProductVersion, x.LocationId, x.CustodianKind, x.CustodianId, x.PhysicalKg,
                x.BlockedPhysicalKg, x.ActiveAllocationKg, x.DocumentStatus)).ToArray();
            await FenceAsync();
            return new(companyId, q, limit, items, window.HasMore);
        }
        catch (OperationCanceledException) { throw; }
        catch (InventoryTransferException) { throw; }
        catch (InventoryStockPositionException error)
        {
            await FenceAsync();
            throw error.StatusCode == 409 ? new InventoryTransferException(409, InventoryTransferCodesV1.LabelVersionConflict) : Unavailable();
        }
        catch
        {
            await FenceAsync();
            throw Unavailable();
        }

        async Task FenceAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            Same();
            PermissionScope scope;
            try { scope = await authority.RevalidatePermissionScopeAsync(InventoryPermissionManifestV1.StockTransferWrite, companyId, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { Same(); throw Unavailable(); }
            Same();
            if (!scope.IsGranted || !scope.IsCompanyWide) throw new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden);
        }
        void Same()
        {
            if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
                || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId))
                throw new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden);
        }
    }
    private static InventoryTransferException Unavailable() => new(503, InventoryTransferCodesV1.Unavailable);
}
