using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class InventoryTransferPlanCandidateEndpoints
{
    public const string BasePath = "/api/v1/inventory/transfer-plan-candidates";
    private const string Invalid = "inventory_transfer_plan_candidates_invalid_query";
    public static IEndpointRouteBuilder MapInventoryTransferPlanCandidateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        Common(endpoints.MapGet(BasePath, LabelsAsync).WithName("ListInventoryTransferPlanCandidatesV1"))
            .Produces<InventoryTransferPlanCandidatesV1>();
        Common(endpoints.MapGet(BasePath + "/destinations", DestinationsAsync).WithName("ListInventoryTransferPlanDestinationsV1"))
            .Produces<InventoryTransferPlanDestinationsV1>();
        return endpoints;
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder route) => route.WithTags("Inventory")
        .RequireCompanyContext().RequireAuthorization(InventoryPermissionManifestV1.StockTransferWrite)
        .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
        .ProducesErpBadRequest(Invalid).ProducesErpConflict(InventoryTransferCodesV1.LabelVersionConflict)
        .ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

    internal static async Task<IResult> LabelsAsync(HttpContext context, IInventoryTransferPlanCandidatesV1 source,
        CancellationToken token)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (!TryQuery(context.Request.Query, 64, out var q, out var limit)) return Results.Problem(statusCode: 400,
            extensions: new Dictionary<string, object?> { ["code"] = Invalid });
        try { return Results.Ok(await source.SearchAsync(q, limit, token)); }
        catch (OperationCanceledException) { throw; }
        catch (InventoryTransferException error)
        {
            return error.Status switch
            {
                400 => Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = Invalid }),
                409 => Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?> { ["code"] = InventoryTransferCodesV1.LabelVersionConflict }),
                401 or 403 => Results.Problem(statusCode: error.Status),
                _ => Results.Problem(statusCode: 503),
            };
        }
    }
    internal static async Task<IResult> DestinationsAsync(HttpContext context, ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, IOrganizationTransferDestinationCandidatesV1 source, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = user.UserId;
        try
        {
            await Fence();
            if (!TryQuery(context.Request.Query, 200, out var q, out var limit)) return Results.Problem(statusCode: 400,
                extensions: new Dictionary<string, object?> { ["code"] = Invalid });
            var result = await source.SearchAsync(companyId, new(q, limit), token);
            await Fence();
            if (result.Items is null || result.Items.Count > limit || result.SearchWindowFull && result.Items.Count != limit
                || result.Items.Select(x => x.LocationId).Distinct().Count() != result.Items.Count
                || result.Items.Any(x => x.LocationId == Guid.Empty || x.SiteId == Guid.Empty || x.LocationVersion < 1
                    || x.IsExternal && (!x.OperatorPartyId.HasValue || x.OperatorPartyId == Guid.Empty)
                    || !Text(x.LocationCode) || !Text(x.LocationName) || !Text(x.SiteCode) || !Text(x.SiteName)))
                return Results.Problem(statusCode: 503);
            return Results.Ok(new InventoryTransferPlanDestinationsV1(companyId, q, limit,
                result.Items.Select(x => new InventoryTransferPlanDestinationV1(x.LocationId, x.LocationCode, x.LocationName,
                    x.SiteId, x.SiteCode, x.SiteName, x.LocationVersion, x.IsExternal ? "Counterparty" : "Company",
                    x.IsExternal ? x.OperatorPartyId!.Value : companyId)).ToArray(), result.SearchWindowFull));
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch
        {
            try { await Fence(); return Results.Problem(statusCode: 503); }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch { return Results.Problem(statusCode: 503); }
        }

        async Task Fence()
        {
            Same();
            var permission = await authority.RevalidatePermissionScopeAsync(InventoryPermissionManifestV1.StockTransferWrite, companyId, token);
            Same();
            if (!permission.IsGranted || !permission.IsCompanyWide) throw new UnauthorizedAccessException();
        }
        void Same()
        {
            token.ThrowIfCancellationRequested();
            if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
                || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException();
        }
    }
    internal static bool TryQuery(IQueryCollection values, int maximumText, out string? query, out int limit)
    {
        query = null; limit = 25;
        if (values.Any(x => x.Key is not ("q" or "limit") || x.Value.Count != 1)) return false;
        if (values.TryGetValue("limit", out var raw) && (!int.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
            || raw[0] != limit.ToString(CultureInfo.InvariantCulture))) return false;
        query = values.TryGetValue("q", out var q) ? q[0]?.Trim() : null;
        if (limit is < 1 or > 100 || query?.Length > maximumText || query?.Any(char.IsControl) == true) return false;
        if (string.IsNullOrWhiteSpace(query)) query = null;
        return true;
    }
    private static bool Text(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2000 && !value.Any(char.IsControl);
}
public sealed record InventoryTransferPlanDestinationsV1(Guid CompanyId, string? Query, int Limit,
    IReadOnlyList<InventoryTransferPlanDestinationV1> Items, bool SearchWindowFull);
public sealed record InventoryTransferPlanDestinationV1(Guid LocationId, string LocationCode, string LocationName,
    Guid SiteId, string SiteCode, string SiteName, long LocationVersion, string CustodianKind, Guid CustodianId);
