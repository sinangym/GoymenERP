using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// T07(a): Inventory's read surface, company-scoped. Each read needs the server-resolved company
/// (<c>X-Company-Id</c>) and its own permission, checked by the host's policy against the actor's
/// current grants. A label of another company, or one that does not exist, is the same 404: the
/// tenant filter never shows it. No write is exposed here; the receipt is a later slice.
/// </summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/inventory")
            .RequireAuthorization()
            .WithTags("Inventory");

        group.MapGet("/labels/{labelId:guid}", async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryLabelStore labels,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                var label = await labels.FindLabelAsync(labelId, cancellationToken);
                return label is null
                    ? NotFound()
                    : Results.Ok(InventoryReadModelsV1.LabelView(label));
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryLabelsRead)
            .WithName("GetInventoryLabelV1")
            .Produces<InventoryLabelViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/labels/{labelId:guid}/balances", async (
                Guid labelId,
                HttpContext httpContext,
                IInventoryLabelStore labels,
                IInventoryMovementStore movements,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                // The label must be this company's; its facts are not returned here, only its stock.
                if (await labels.FindLabelAsync(labelId, cancellationToken) is null)
                {
                    return NotFound();
                }

                var balances = await movements.ReadBalancesAsync(labelId, cancellationToken);
                return Results.Ok(InventoryReadModelsV1.Balances(labelId, balances));
            })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.InventoryBalancesRead)
            .WithName("GetInventoryLabelBalancesV1")
            .Produces<InventoryLabelBalancesV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static IResult NotFound() =>
        Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
}
