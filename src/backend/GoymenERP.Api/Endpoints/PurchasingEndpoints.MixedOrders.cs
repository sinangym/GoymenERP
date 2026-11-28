using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class PurchasingEndpoints
{
    private static void MapMixedOrders(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(OrdersPath).WithTags("Purchasing").RequireAuthorization().RequireCompanyContext();
        group.MapGet("/{id:guid}/mixed-approval-preview", async (Guid id, HttpContext http,
            [FromServices] IPurchaseMixedOrderApprovalPreviewV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            // The required version is only a read precondition; neither company nor children come from query parameters.
            if (http.Request.Query.Count != 1 || !http.Request.Query.TryGetValue("expectedVersion", out var versions)
                || versions.Count != 1 || versions[0] is not { } version)
                return Problem(400, "validation_failed", correlation);
            try
            {
                _ = PurchaseMixedOrderMembershipV1.SelectedChildVersion(version);
                return Results.Json(await service.ReadMixedOrderApprovalPreviewAsync(id, version, token), PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception) { return Problem(http, exception, correlation); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return Problem(503, "service_unavailable", correlation); }
        }).WithName("GetPurchaseMixedOrderApprovalPreviewV1").Produces<PurchaseMixedOrderApprovalPreviewV1>()
            .ProducesErpBadRequest("validation_failed").ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        group.MapGet("/{id:guid}/service-price-revision-preview", async (Guid id, HttpContext http,
            [FromServices] IPurchaseMixedServicePriceRevisionPreviewV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Count != 1 || !http.Request.Query.TryGetValue("expectedVersion", out var versions)
                || versions.Count != 1 || versions[0] is not { } version) return Problem(400, "validation_failed", correlation);
            try
            {
                _ = PurchaseMixedOrderMembershipV1.SelectedChildVersion(version);
                return Results.Json(await service.ReadMixedServicePriceRevisionPreviewAsync(id, version, token), PurchasingJson.Options);
            }
            catch (PurchasingServiceException exception) { return Problem(http, exception, correlation); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return Problem(503, "service_unavailable", correlation); }
        }).WithName("GetPurchaseMixedServicePriceRevisionPreviewV1").Produces<PurchaseMixedServicePriceRevisionPreviewV1>()
            .ProducesErpBadRequest("validation_failed").ProducesErpCodeLessNotFound()
            .ProducesErpConflict(PurchasingProblemCodesV1.VersionConflict).ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

        MapRevisionMutation(group.MapPost("/{id:guid}/service-price-revisions", (Guid id, HttpContext http,
            [FromServices] IPurchaseMixedServicePriceRevisionServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Count != 0) return Task.FromResult(Problem(400, "validation_failed", correlation));
            // The same parent owner authorizes OrdersRevise and freezes all selected native child versions/lines.
            return MutateAsync(http, correlation, PurchaseMixedOrderJsonV1.ReadServicePriceRevision,
                (body, metadata) => service.ReviseMixedServicePricesAsync(id, body, metadata, token), token);
        }).WithName("RevisePurchaseMixedServicePricesV1").Accepts<RevisePurchaseMixedServicePricesRequest>("application/json"),
            PurchasingRuleCodes.OrderNotApproved, PurchaseServiceOrder.StateConflict, PurchaseServiceOrder.VersionConflict);

        MapRevisionMutation(group.MapPost("/{id:guid}/service-memberships", (Guid id, HttpContext http,
            [FromServices] IPurchaseMixedOrderMembershipServiceV1 service, ICorrelationContext correlation, CancellationToken token) =>
        {
            SetPrivateNoStore(http.Response);
            if (http.Request.Query.Count != 0) return Task.FromResult(Problem(400, "validation_failed", correlation));
            return MutateAsync(http, correlation, PurchaseMixedOrderJsonV1.ReadMembership,
                (body, metadata) => service.BindServicesAsync(id, body, metadata, token), token);
        }).WithName("BindPurchaseOrderServicesV1").Accepts<BindPurchaseOrderServicesRequest>("application/json"));
    }
}

/// <summary>Closed mixed-intent transport. Absent/null mixed intent still uses the unchanged legacy native reader.</summary>
public static class PurchaseMixedOrderJsonV1
{
    public static BindPurchaseOrderServicesRequest ReadMembership(string json) =>
        PurchaseServiceOrderJsonV1.Read<BindPurchaseOrderServicesRequest>(json);

    public static RevisePurchaseMixedServicePricesRequest ReadServicePriceRevision(string json) =>
        PurchaseServiceOrderJsonV1.Read<RevisePurchaseMixedServicePricesRequest>(json);

    public static ApprovePurchaseOrderRequest ReadApprove(string json)
    {
        var legacy = PurchasingJson.ReadApprove(json);
        return legacy.MixedServices is null ? legacy : PurchaseServiceOrderJsonV1.Read<ApprovePurchaseOrderRequest>(json);
    }

    public static RevisePurchaseOrderSupplierRequest ReadSupplierRevision(string json)
    {
        var legacy = PurchaseSupplierRevisionJsonV1.ReadRevision(json);
        return legacy.MixedServices is null ? legacy : PurchaseServiceOrderJsonV1.Read<RevisePurchaseOrderSupplierRequest>(json);
    }
}
