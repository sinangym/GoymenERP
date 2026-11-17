using System.Globalization;
using GoymenERP.Api.Errors;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class InventoryEndpoints
{
    private static void MapInventoryTransferReceiptWorkCandidatesEndpoint(IEndpointRouteBuilder endpoints) => endpoints.MapGet(
        "/api/v1/inventory/transfer-receipt-work/candidates", ReceiptWorkCandidatesAsync)
        .WithName("ListInventoryTransferReceiptWorkCandidatesV1").WithTags("Inventory").RequireAuthorization().RequireCompanyContext()
        .WithMetadata(new PrivateNoStoreOnAuthorizationFailureMetadata())
        .Produces<InventoryTransferReceiptWorkDisplayCandidatesV1>().ProducesErpBadRequest(InventoryTransferCodesV1.ValidationFailed)
        .AllowsCodeLessErpBadRequestBody().ProducesErpServiceUnavailable().ProducesErpAuthenticatedProblems();

    internal static async Task<IResult> ReceiptWorkCandidatesAsync(HttpContext http, IInventoryTransferReceiptWorkV1 owner,
        IOrganizationLocationDisplayReaderV1 locations, ICompanyContext company, ICurrentUser user,
        IErpAuthorityRevalidationService authority, CancellationToken token)
    {
        SetPrivateNoStore(http.Response);
        if (http.Request.Query.Any(x => x.Key is not ("purpose" or "q" or "limit" or "cursor") || x.Value.Count != 1)
            || !http.Request.Query.TryGetValue("purpose", out var rawPurpose)
            || InventoryTransferReceiptWorkPurposesV1.Permission(rawPurpose[0]) is not { } permission) return Results.Problem(statusCode: 400);
        var purpose = rawPurpose[0]!;
        var q = http.Request.Query.TryGetValue("q", out var rawQuery) ? rawQuery[0] : null;
        var cursor = http.Request.Query.TryGetValue("cursor", out var rawCursor) ? rawCursor[0] : null;
        var limit = 25;
        if (http.Request.Query.TryGetValue("limit", out var rawLimit)
            && (!int.TryParse(rawLimit[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || rawLimit[0] != limit.ToString(CultureInfo.InvariantCulture))
            || cursor is not null && string.IsNullOrWhiteSpace(cursor)) return Results.Problem(statusCode: 400);
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty; var actor = user.UserId;
        try
        {
            await Fence();
            var result = await owner.SearchAsync(purpose, q, cursor, limit, token);
            await Fence();
            if (result.CompanyId != companyId || result.Purpose != purpose || result.Query != q || result.Limit != limit
                || result.Items is null || result.Items.Count > limit || result.SearchWindowFull != (result.NextCursor is not null)
                || result.SearchWindowFull && result.Items.Count != limit
                || result.Items.Any(x => x.Transfer.CompanyId != companyId)) return Results.Problem(statusCode: 503);
            var ids = result.Items.SelectMany(x => new[] { x.Transfer.SourceLocationId, x.Transfer.DestinationLocationId }).Distinct().ToArray();
            var display = new List<OrganizationLocationDisplayV1>();
            foreach (var batch in ids.Chunk(100))
            {
                display.AddRange(await locations.ReadAsync(companyId, batch, token));
                await Fence();
            }
            if (display.Count != ids.Length || display.Select(x => x.LocationId).Distinct().Count() != ids.Length
                || display.Any(x => !ids.Contains(x.LocationId) || string.IsNullOrWhiteSpace(x.LocationCode) || string.IsNullOrWhiteSpace(x.LocationName)))
                return Results.Problem(statusCode: 503);
            var names = display.ToDictionary(x => x.LocationId);
            return Results.Ok(new InventoryTransferReceiptWorkDisplayCandidatesV1(companyId, purpose, q, limit,
                result.Items.Select(x => new InventoryTransferReceiptWorkDisplayCandidateV1(x.Transfer, x.LabelCodes,
                    names[x.Transfer.SourceLocationId].LocationCode, names[x.Transfer.SourceLocationId].LocationName,
                    names[x.Transfer.DestinationLocationId].LocationCode, names[x.Transfer.DestinationLocationId].LocationName)).ToArray(),
                result.SearchWindowFull, result.NextCursor));
        }
        catch (OperationCanceledException) { throw; }
        catch (InventoryTransferException error) { return await Refuse(error); }
        catch { return await Refuse(null); }

        async Task<IResult> Refuse(InventoryTransferException? original)
        {
            try { await Fence(); return original is null ? Results.Problem(statusCode: 503) : ReceiptDocumentProblem(original); }
            catch (OperationCanceledException) { throw; }
            catch (InventoryTransferException refreshed) { return ReceiptDocumentProblem(refreshed); }
            catch { return Results.Problem(statusCode: 503); }
        }

        async Task Fence()
        {
            Same(); var grant = await authority.RevalidatePermissionScopeAsync(permission, companyId, token); Same();
            if (!grant.IsGranted || !grant.IsCompanyWide) throw new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden);
        }
        void Same()
        {
            token.ThrowIfCancellationRequested();
            if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
                || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(companyId))
                throw new InventoryTransferException(403, InventoryTransferCodesV1.Forbidden);
        }
    }
}
