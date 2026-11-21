using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    private static void MapInventoryFutureSupplyOptionsEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/inventory/sales-orders/{salesOrderId:guid}/lines/{salesOrderLineId:guid}/future-supply-options",
            async (Guid salesOrderId, Guid salesOrderLineId, string? cursor, HttpContext http,
                ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority,
                IInventoryFutureDemandSourceV1 demand, IPurchaseFutureSupplyReadPortV1 purchasing,
                IInventoryFutureAllocationReadPortV1 allocations, ICounterpartyPurchasingRecordPartyDisplaySourceV1 suppliers,
                ISettingsUnitIdentityCatalogV1 units, ICorrelationContext correlation, CancellationToken cancellationToken) =>
            {
                SetPrivateNoStore(http.Response);
                if (http.Request.Query.Any(item => item.Key != "cursor" || item.Value.Count != 1))
                    return OpeningProblem(400, InventoryFutureAllocationCodesV1.ValidationFailed, correlation);
                try { return Results.Ok(await new InventoryFutureSupplyOptionsHost(company, user, authority, demand,
                    purchasing, allocations, suppliers, units).ReadAsync(salesOrderId, salesOrderLineId, cursor, cancellationToken)); }
                catch (InventoryAllocationException exception) { return FutureAllocationProblem(http, exception, correlation); }
            })
            .RequireCompanyContext().RequireAuthorization(ErpPermissions.InventoryFutureAllocationWrite)
            .WithName("ListInventoryFutureSupplyOptionsV1").WithTags("Inventory")
            .Produces<InventoryFutureSupplyOptionsV1>().ProducesErpBadRequest(InventoryFutureAllocationCodesV1.ValidationFailed)
            .ProducesErpCodeLessNotFound().ProducesErpUnprocessableEntity(InventoryFutureAllocationCodesV1.InvalidReference)
            .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();
    }
}
