using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// B-08 / BS-10 (STK-S12): the company's package standard table (pieces per package of a product), read, imported and edited by
/// an actor of the server-resolved company (X-Company-Id) under <see cref="ProductPackageStandardPermissionsV2.Manage"/> in that
/// company (company-wide, granted to no role here).
/// <list type="bullet">
/// <item><c>GET /api/v1/product-package-standards/current</c> answers the current table (version "0" and no entries while none
/// exists).</item>
/// <item><c>POST /api/v1/product-package-standards/imports</c> imports a new version from an Excel file (204) with Idempotency-Key,
/// the expected version, a mandatory source label and reason; any row error refuses the whole file.</item>
/// <item><c>PUT /api/v1/product-package-standards/products/{productId}</c> sets or removes one product's entry (204) with
/// Idempotency-Key, the expected version and a mandatory reason; an unknown product answers a code-less 404.</item>
/// <item>SAT-D382 / SAT-D383 (STK-SC36-c): <c>GET /approval-state</c> (preparer) and <c>GET /pending</c> (approver, under
/// <see cref="ProductPackageStandardPermissionsV3.Approve"/>) answer the effective version, its customer overrides and the
/// pending version with its changes; <c>PUT /customers/{customerId}/products/{productId}</c> sets or removes a customer's
/// override; <c>POST /versions/{version}/approve</c> and <c>/reject</c> decide the pending version; <c>GET /imports</c> and
/// <c>GET /imports/{importId}/report</c> list the kept files and read a kept report (the file itself is not served).</item>
/// </list>
/// Every new version waits for approval. The consumers (Sales, Production) read the value in effect through
/// <see cref="IProductPackageStandardReaderV3"/> (and V2 for today), not over HTTP.
/// Private, no-store.
/// </summary>
public static class ProductPackageStandardEndpoints
{
    public const string BasePath = "/api/v1/product-package-standards";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    public static IEndpointRouteBuilder MapProductPackageStandardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath)
            .RequireAuthorization()
            .WithTags("Product");

        group.MapGet("/current", async (
                IProductPackageStandardServiceV2 service,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return QueryRefused();
                }

                try
                {
                    return Results.Ok(await service.GetCurrentAsync(cancellationToken));
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductPackageStandardException exception)
                {
                    return FromException(exception);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV2.Manage)
            .WithName("GetCurrentProductPackageStandardsV2")
            .Produces<ProductPackageStandardTableViewV2>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductPackageStandardProblemCodesV2.RequestInvalid)
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/imports", async (
                ImportProductPackageStandardsRequestV2 request,
                IProductPackageStandardServiceV2 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return QueryRefused();
                }

                if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
                {
                    return MetadataRefused();
                }

                ProductPackageStandardResultV2 result;
                try
                {
                    result = await service.ImportAsync(request, metadata, cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductPackageStandardException exception)
                {
                    return FromException(exception);
                }

                return Answer(httpContext, result);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV2.Manage)
            .WithName("ImportProductPackageStandardsV2")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status400BadRequest,
                StatusCodes.Status409Conflict)
            .Accepts<ImportProductPackageStandardsRequestV2>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                ProductPackageStandardProblemCodesV2.RequestInvalid,
                ProductPackageStandardProblemCodesV2.CommandMetadataInvalid,
                ProductPackageStandardProblemCodesV2.FileInvalid,
                ProductPackageStandardProblemCodesV2.SourceLabelRequired,
                ProductPackageStandardProblemCodesV2.SourceLabelTooLong,
                ProductPackageStandardProblemCodesV2.ReasonRequired,
                ProductPackageStandardProblemCodesV2.ReasonTooLong)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ProductPackageStandardProblemCodesV2.IdempotencyConflict,
                ProductPackageStandardProblemCodesV2.VersionConflict,
                ProductPackageStandardProblemCodesV3.ApprovalPending)
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/products/{productId:guid}", async (
                Guid productId,
                SetProductPackageStandardRequestV2 request,
                IProductPackageStandardServiceV2 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return QueryRefused();
                }

                if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
                {
                    return MetadataRefused();
                }

                ProductPackageStandardResultV2 result;
                try
                {
                    result = await service.SetAsync(productId, request, metadata, cancellationToken);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductPackageStandardException exception)
                {
                    return FromException(exception);
                }

                return Answer(httpContext, result);
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV2.Manage)
            .RequiresNonEmptyGuidPathParameter("productId")
            .WithName("SetProductPackageStandardV2")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<SetProductPackageStandardRequestV2>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                ProductPackageStandardProblemCodesV2.RequestInvalid,
                ProductPackageStandardProblemCodesV2.CommandMetadataInvalid,
                ProductPackageStandardProblemCodesV2.PiecesInvalid,
                ProductPackageStandardProblemCodesV2.ReasonRequired,
                ProductPackageStandardProblemCodesV2.ReasonTooLong)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                ProductPackageStandardProblemCodesV2.IdempotencyConflict,
                ProductPackageStandardProblemCodesV2.VersionConflict,
                ProductPackageStandardProblemCodesV3.ApprovalPending,
                ProductPackageStandardProblemCodesV3.ProductInactive)
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        MapApprovalEndpoints(group);
        return endpoints;
    }

    /// <summary>SAT-D382 / SAT-D383 (STK-SC36-c): the approval, the customer override and the kept import files.</summary>
    private static void MapApprovalEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/approval-state", (IProductPackageStandardServiceV3 service, HttpContext httpContext,
                CancellationToken cancellationToken) =>
                ReadAsync(httpContext, () => service.GetApprovalStateAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV2.Manage)
            .WithName("GetProductPackageStandardApprovalStateV3")
            .Produces<ProductPackageStandardApprovalViewV3>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductPackageStandardProblemCodesV2.RequestInvalid)
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/pending", (IProductPackageStandardServiceV3 service, HttpContext httpContext,
                CancellationToken cancellationToken) =>
                ReadAsync(httpContext, () => service.GetApprovalStateForApproverAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV3.Approve)
            .WithName("GetPendingProductPackageStandardVersionV3")
            .Produces<ProductPackageStandardApprovalViewV3>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductPackageStandardProblemCodesV2.RequestInvalid)
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/imports", (IProductPackageStandardServiceV3 service, HttpContext httpContext,
                CancellationToken cancellationToken) =>
                ReadAsync(httpContext, () => service.ListImportsAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV2.Manage)
            .WithName("ListProductPackageStandardImportsV3")
            .Produces<ProductPackageStandardImportFileListV3>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductPackageStandardProblemCodesV2.RequestInvalid)
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/imports/{importId:guid}/report", async (Guid importId, IProductPackageStandardServiceV3 service,
                HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "private, no-store";
                if (httpContext.Request.Query.Count != 0)
                {
                    return QueryRefused();
                }

                try
                {
                    return await service.GetImportReportAsync(importId, cancellationToken) is { } report
                        ? Results.Ok(report)
                        : Results.Problem(title: "Dosya bulunamadı.", detail: "İstenen içe aktarma dosyası yok.",
                            statusCode: StatusCodes.Status404NotFound);
                }
                catch (UnauthorizedAccessException)
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ProductPackageStandardException exception)
                {
                    return FromException(exception);
                }
            })
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV2.Manage)
            .RequiresNonEmptyGuidPathParameter("importId")
            .WithName("GetProductPackageStandardImportReportV3")
            .Produces<ProductPackageStandardImportReportV3>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(ProductPackageStandardProblemCodesV2.RequestInvalid)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/customers/{customerId:guid}/products/{productId:guid}", (Guid customerId, Guid productId,
                SetProductPackageStandardRequestV2 request, IProductPackageStandardServiceV3 service, ICurrentUser currentUser,
                ICompanyContext companyContext, ICorrelationContext correlationContext, HttpContext httpContext,
                CancellationToken cancellationToken) =>
                CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.SetCustomerAsync(customerId, productId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV2.Manage)
            .RequiresNonEmptyGuidPathParameter("customerId")
            .RequiresNonEmptyGuidPathParameter("productId")
            .WithName("SetProductPackageStandardCustomerEntryV3")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<SetProductPackageStandardRequestV2>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                ProductPackageStandardProblemCodesV2.RequestInvalid,
                ProductPackageStandardProblemCodesV2.CommandMetadataInvalid,
                ProductPackageStandardProblemCodesV2.PiecesInvalid,
                ProductPackageStandardProblemCodesV2.ReasonRequired,
                ProductPackageStandardProblemCodesV2.ReasonTooLong)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                ProductPackageStandardProblemCodesV2.IdempotencyConflict,
                ProductPackageStandardProblemCodesV2.VersionConflict,
                ProductPackageStandardProblemCodesV3.ApprovalPending,
                ProductPackageStandardProblemCodesV3.ProductInactive,
                ProductPackageStandardProblemCodesV3.CustomerNotFound)
            .ProducesErpServiceUnavailable(
                ProductPackageStandardProblemCodesV2.ProviderUnavailable,
                ProductPackageStandardProblemCodesV3.CustomerProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/versions/{version:long}/approve", (long version, ApproveProductPackageStandardVersionRequestV3 request,
                IProductPackageStandardServiceV3 service, ICurrentUser currentUser, ICompanyContext companyContext,
                ICorrelationContext correlationContext, HttpContext httpContext, CancellationToken cancellationToken) =>
                CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.ApproveAsync(version, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV3.Approve)
            .WithName("ApproveProductPackageStandardVersionV3")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<ApproveProductPackageStandardVersionRequestV3>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                ProductPackageStandardProblemCodesV2.RequestInvalid,
                ProductPackageStandardProblemCodesV2.CommandMetadataInvalid,
                ProductPackageStandardProblemCodesV3.EffectiveDateInvalid,
                ProductPackageStandardProblemCodesV2.ReasonRequired,
                ProductPackageStandardProblemCodesV2.ReasonTooLong)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ProductPackageStandardProblemCodesV2.IdempotencyConflict,
                ProductPackageStandardProblemCodesV2.VersionConflict,
                ProductPackageStandardProblemCodesV3.VersionNotPending,
                ProductPackageStandardProblemCodesV3.PreparerCannotDecide,
                ProductPackageStandardProblemCodesV3.EffectiveDateInvalid)
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/versions/{version:long}/reject", (long version, RejectProductPackageStandardVersionRequestV3 request,
                IProductPackageStandardServiceV3 service, ICurrentUser currentUser, ICompanyContext companyContext,
                ICorrelationContext correlationContext, HttpContext httpContext, CancellationToken cancellationToken) =>
                CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                    metadata => service.RejectAsync(version, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ProductPackageStandardPermissionsV3.Approve)
            .WithName("RejectProductPackageStandardVersionV3")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<RejectProductPackageStandardVersionRequestV3>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                ProductPackageStandardProblemCodesV2.RequestInvalid,
                ProductPackageStandardProblemCodesV2.CommandMetadataInvalid,
                ProductPackageStandardProblemCodesV2.ReasonRequired,
                ProductPackageStandardProblemCodesV2.ReasonTooLong)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                ProductPackageStandardProblemCodesV2.IdempotencyConflict,
                ProductPackageStandardProblemCodesV2.VersionConflict,
                ProductPackageStandardProblemCodesV3.VersionNotPending,
                ProductPackageStandardProblemCodesV3.PreparerCannotDecide)
            .ProducesErpServiceUnavailable(ProductPackageStandardProblemCodesV2.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return QueryRefused();
        }

        try
        {
            return Results.Ok(await read());
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ProductPackageStandardException exception)
        {
            return FromException(exception);
        }
    }

    private static async Task<IResult> CommandAsync(HttpContext httpContext, ICurrentUser currentUser,
        ICompanyContext companyContext, ICorrelationContext correlationContext,
        Func<ProductPackageStandardCommandMetadataV2, Task<ProductPackageStandardResultV2>> command)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return QueryRefused();
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return MetadataRefused();
        }

        ProductPackageStandardResultV2 result;
        try
        {
            result = await command(metadata);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ProductPackageStandardException exception)
        {
            return FromException(exception);
        }

        return Answer(httpContext, result);
    }

    private static IResult Answer(HttpContext httpContext, ProductPackageStandardResultV2 result)
    {
        if (result.IsReplay)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
        }

        var answer = result.Answer;
        return answer.StatusCode switch
        {
            StatusCodes.Status204NoContent => Results.NoContent(),
            StatusCodes.Status404NotFound when answer.Code is null => Results.Problem(title: answer.Title,
                detail: answer.Detail, statusCode: StatusCodes.Status404NotFound),
            _ when answer.Code is { } code => Problem(answer.StatusCode, code, answer.Title, answer.Detail),
            _ => throw new InvalidOperationException("Paket standardı komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult QueryRefused() =>
        Problem(StatusCodes.Status400BadRequest, ProductPackageStandardProblemCodesV2.RequestInvalid,
            "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");

    private static IResult MetadataRefused() =>
        Problem(StatusCodes.Status400BadRequest, ProductPackageStandardProblemCodesV2.CommandMetadataInvalid,
            "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");

    private static IResult FromException(ProductPackageStandardException exception) => exception.StatusCode switch
    {
        StatusCodes.Status403Forbidden => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        StatusCodes.Status404NotFound => Results.Problem(title: "Ürün bulunamadı.", detail: "İstenen ürün kartı yok.",
            statusCode: StatusCodes.Status404NotFound),
        StatusCodes.Status400BadRequest => Problem(StatusCodes.Status400BadRequest,
            ProductPackageStandardProblemCodesV2.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, ProductPackageStandardProblemCodesV2.ProviderUnavailable,
            "Paket standardı tablosu kullanılamıyor.",
            "Şirketin paket standardı tablosu şu anda okunamıyor veya değiştirilemiyor; daha sonra yeniden deneyin."),
    };

    private static IResult Problem(int status, string code, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryReadMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out ProductPackageStandardCommandMetadataV2 metadata)
    {
        metadata = null!;
        if (!httpContext.Request.Headers.TryGetValue(IdempotencyHeaderName, out var values)
            || values.Count != 1
            || values[0] is not { } raw
            || raw.Trim() is not { Length: > 0 and <= MaximumIdempotencyKeyLength } key
            || key.Contains(',', StringComparison.Ordinal)
            || !correlationContext.IsResolved
            || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved)
        {
            return false;
        }

        metadata = new ProductPackageStandardCommandMetadataV2(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId,
            correlationContext.SourceChannel,
            correlationContext.ClientIpAddress,
            correlationContext.UserAgent,
            key);
        return true;
    }
}
