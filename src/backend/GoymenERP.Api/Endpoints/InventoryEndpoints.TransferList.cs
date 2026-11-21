using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    private static void MapInventoryTransferListEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InventoryTransferApiV1.Path, async (HttpContext http, IInventoryTransferQueryV1 transfers,
            IOrganizationLocationDisplayReaderV1 locations, ICompanyContext company, ICurrentUser user,
            IErpAuthorityRevalidationService authority, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Count != 0) return Results.Problem(statusCode: 400);
            var companyId = company.CompanyId;
            var actor = user.UserId;
            try
            {
                var result = await transfers.ListAsync(25, cancellationToken);
                var ids = result.Items.SelectMany(item => new[] { item.SourceLocationId, item.DestinationLocationId }).Distinct().ToArray();
                var names = ids.Length == 0 ? [] : await locations.ReadAsync(companyId, ids, cancellationToken);
                var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.InventoryBalancesRead, companyId, cancellationToken);
                if (!scope.IsGranted || !scope.IsCompanyWide || !company.IsResolved || company.CompanyId != companyId
                    || !user.IsAuthenticated || !string.Equals(user.UserId, actor, StringComparison.Ordinal)
                    || !user.CanAccessCompany(companyId)) return Results.Problem(statusCode: 403);
                if (names.Count != ids.Length || names.Select(item => item.LocationId).Distinct().Count() != ids.Length
                    || names.Any(item => !ids.Contains(item.LocationId) || string.IsNullOrWhiteSpace(item.LocationCode)
                        || string.IsNullOrWhiteSpace(item.LocationName))) return Results.Problem(statusCode: 503);
                var byId = names.ToDictionary(item => item.LocationId);
                return Results.Ok(new InventoryTransferDisplayListV1(companyId, [.. result.Items.Select(item => new InventoryTransferDisplayItemV1(
                    item, byId[item.SourceLocationId].LocationCode, byId[item.SourceLocationId].LocationName,
                    byId[item.DestinationLocationId].LocationCode, byId[item.DestinationLocationId].LocationName))], result.WindowFull));
            }
            catch (InventoryTransferException exception)
            { return Results.Problem(statusCode: exception.Status is 401 or 403 ? exception.Status : 503); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                var scope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.InventoryBalancesRead, companyId, cancellationToken);
                if (!scope.IsGranted || !scope.IsCompanyWide || !company.IsResolved || company.CompanyId != companyId
                    || !user.IsAuthenticated || !string.Equals(user.UserId, actor, StringComparison.Ordinal)
                    || !user.CanAccessCompany(companyId)) return Results.Problem(statusCode: 403);
                return Results.Problem(statusCode: 503);
            }
        })
        .RequireCompanyContext().RequireAuthorization(ErpPermissions.InventoryBalancesRead)
        .WithName("ListInventoryTransfersV1").WithTags("Inventory")
        .Produces<InventoryTransferDisplayListV1>().ProducesErpBadRequest().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }
}
