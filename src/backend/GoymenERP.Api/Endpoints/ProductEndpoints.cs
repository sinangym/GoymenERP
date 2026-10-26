using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace GoymenERP.Api.Endpoints;

public static class ProductEndpoints
{
    /// <summary>Central host maps this only after the global security/persistence adapter is accepted.</summary>
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ProductApiV1.BasePath).WithTags("Product").RequireAuthorization()
            .RequireCompanyContext();

        group.MapGet("/", async ([AsParameters] ProductListQuery query, IProductService service,
            HttpContext context, CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
            {
                return Results.Ok(await service.GetPageAsync(ProductQueryRules.Normalize(query), cancellationToken));
            })).WithName("GetProductsV1").Produces<ProductPage>().WithProductRuntimeContract(primitiveBinding: true);

        group.MapGet("/code-groups", async (IProductService service, HttpContext context,
            CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
                Results.Ok(await service.GetCodeGroupsAsync(cancellationToken))))
            .WithName("GetProductCodeGroupsV1").Produces<IReadOnlyList<ProductCodeGroup>>().WithProductRuntimeContract();

        group.MapGet("/reference-options", async ([AsParameters] ProductReferenceQuery query,
            IProductService service, HttpContext context, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () =>
            {
                if (query.GroupId == Guid.Empty || query.ClassId == Guid.Empty)
                {
                    return Problem(ProductApiV1.ValidationFailed, 400);
                }
                return Results.Ok(await service.GetReferenceOptionsAsync(query, cancellationToken));
            })).WithName("GetProductReferenceOptionsV1").Produces<ProductReferenceOptions>()
            .WithProductRuntimeContract(notFound: true, primitiveBinding: true);

        // §10.2 / §15: the read-only exact-name duplicate candidates. A warning to compare against, never a block.
        group.MapGet("/duplicate-candidates", async ([AsParameters] ProductDuplicateQuery query,
            IProductService service, HttpContext context, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () =>
                Results.Ok(await service.GetDuplicateCandidatesAsync(query, cancellationToken))))
            .WithName("GetProductDuplicateCandidatesV1").Produces<ProductDuplicateCandidates>()
            .WithProductRuntimeContract(primitiveBinding: true);

        group.MapGet("/{id:guid}/history", async (Guid id, ProductHistoryReader reader, HttpContext context,
            CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
                Results.Ok(await reader.GetAsync(id, cancellationToken))))
            .WithName("GetProductHistoryV1").Produces<ProductHistoryView>()
            .WithProductRuntimeContract(notFound: true);

        group.MapPost("/imports/preview", async (HttpContext context, IProductImportServiceV1 service,
            IGroupSharedMasterAuthorizationService authority, CancellationToken cancellationToken) =>
        {
            if (!IsMultipart(context.Request)) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            return await ExecuteAsync(context, async () =>
            {
                var form = await ProductImportRequestBoundary.ReadAsync(context, authority, apply: false, cancellationToken);
                return Results.Ok(await service.PreviewAsync(form.Workbook, form.Mapping, cancellationToken));
            });
        }).WithName(ProductImportApiV1.PreviewOperationId).Produces<ProductImportPreviewV1>()
            .Accepts<ProductImportPreviewFormV1>("multipart/form-data")
            .WithProductRuntimeContract(primitiveBinding: true).Produces(413).Produces(415);

        group.MapPost("/imports", async (HttpContext context, IProductImportServiceV1 service,
            IGroupSharedMasterAuthorizationService authority, CancellationToken cancellationToken) =>
        {
            if (!IsMultipart(context.Request)) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            return await ExecuteAsync(context, async () =>
            {
                var form = await ProductImportRequestBoundary.ReadAsync(context, authority, apply: true, cancellationToken);
                var binding = new ProductImportApplyBindingV1(form.FileSha256!, form.PreviewToken!, form.Mapping);
                var result = await service.ApplyAsync(form.Workbook, binding, ReadIdempotencyKey(context), cancellationToken);
                return Results.Created(ProductImportApiV1.ApplyPath, result);
            });
        }).WithName(ProductImportApiV1.ApplyOperationId).Produces<ProductImportResultV1>(201)
            .Accepts<ProductImportApplyFormV1>("multipart/form-data")
            .WithProductRuntimeContract(mutation: true, primitiveBinding: true).WithIdempotencyContract(201)
            .Produces(413);

        group.MapGet("/{id:guid}", async (Guid id, IProductService service, HttpContext context,
            CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
            {
                var card = await service.GetAsync(id, cancellationToken);
                return card is null ? Problem(ProductApiV1.NotFound, 404) : Results.Ok(card);
            })).WithName("GetProductV1").Produces<ProductCardView>().WithProductRuntimeContract(notFound: true);

        group.MapGet(ProductManufacturingTechnicalHttpApiV2.Route, async (Guid id,
            [AsParameters] ProductManufacturingTechnicalHttpQueryV2 query,
            IProductManufacturingTechnicalSnapshotV2 snapshots,
            HttpContext context,
            CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
            {
                try
                {
                    var result = await snapshots.ReadAsync(new(id, query.ExpectedProductVersion, query.EffectiveOn),
                        cancellationToken);
                    return Results.Ok(ProductManufacturingTechnicalHttpProjectionV2.From(result));
                }
                catch (SettingsPrerequisiteException exception)
                {
                    throw new ProductOperationException(exception.Code,
                        string.Equals(exception.Code, "settings_access_denied", StringComparison.Ordinal) ? 403 : 503);
                }
            }))
            .WithName(ProductManufacturingTechnicalHttpApiV2.OperationId)
            .Produces<ProductManufacturingTechnicalHttpResultV2>()
            .WithProductRuntimeContract(primitiveBinding: true, operationalSelection: true, unavailable: true);

        group.MapPost("/", async (CreateProductDraftRequest request, IProductService service,
            HttpContext context, CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
            {
                var result = await service.CreateDraftAsync(request, ReadIdempotencyKey(context), cancellationToken);
                return Results.Created($"{ProductApiV1.BasePath}/{result.Card.Product.Id:D}", result);
            })).WithName("CreateProductDraftV1").Produces<ProductMutationResponse>(201)
            .WithProductRuntimeContract(mutation: true, primitiveBinding: true).WithIdempotencyContract(201);

        group.MapPatch("/{id:guid}/draft", async (Guid id, EditProductDraftRequest request,
            IProductService service, HttpContext context, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () => Results.Ok(await service.EditDraftAsync(id, request,
                ReadIdempotencyKey(context), cancellationToken))))
            .WithName("EditProductDraftV1").Produces<ProductMutationResponse>()
            .WithProductRuntimeContract(mutation: true, notFound: true, primitiveBinding: true).WithIdempotencyContract(200);

        // DEC-PRD-010: approval of a Draft card (Draft -> Active) under the global Activate authority.
        group.MapPost("/{id:guid}/activation", async (Guid id, ActivateProductRequest request,
            IProductService service, HttpContext context, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () => Results.Ok(await service.ActivateAsync(id, request,
                ReadIdempotencyKey(context), cancellationToken))))
            .WithName("ActivateProductV1").Produces<ProductMutationResponse>()
            .WithProductRuntimeContract(mutation: true, notFound: true, primitiveBinding: true,
                activationValidation: true).WithIdempotencyContract(200);

        // DEC-CAR-003 / #98 §7.9: an Active card is taken out of new use with a reason (Active -> Inactive); nothing
        // is deleted. PRD-PERM-001 (#99): each move needs its own explicit global grant (Deactivate / Reactivate).
        group.MapPost("/{id:guid}/deactivation", async (Guid id, ChangeProductLifecycleRequest request,
            IProductService service, HttpContext context, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () => Results.Ok(await service.DeactivateAsync(id, request,
                ReadIdempotencyKey(context), cancellationToken))))
            .WithName("DeactivateProductV1").Produces<ProductMutationResponse>()
            .WithProductRuntimeContract(mutation: true, notFound: true, primitiveBinding: true).WithIdempotencyContract(200);

        group.MapPost("/{id:guid}/reactivation", async (Guid id, ChangeProductLifecycleRequest request,
            IProductService service, HttpContext context, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () => Results.Ok(await service.ReactivateAsync(id, request,
                ReadIdempotencyKey(context), cancellationToken))))
            .WithName("ReactivateProductV1").Produces<ProductMutationResponse>()
            .WithProductRuntimeContract(mutation: true, notFound: true, primitiveBinding: true).WithIdempotencyContract(200);
        return endpoints;
    }

    private static string ReadIdempotencyKey(HttpContext context)
    {
        var values = context.Request.Headers[ProductApiV1.IdempotencyHeader];
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 128
            || values[0]!.Any(character => character < 33 || character > 126 || character == ','))
        {
            throw new ProductValidationException([new("idempotencyKey", "invalid_metadata")]);
        }

        return values[0]!;
    }

    private static bool IsMultipart(HttpRequest request) => request.HasFormContentType
        && (request.ContentType ?? string.Empty).StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase);

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<Task<IResult>> execute)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try { return await execute(); }
        catch (ProductImportPayloadTooLargeException)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (ProductValidationException exception)
        {
            return Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?>
            {
                ["code"] = ProductApiV1.ValidationFailed,
                ["errors"] = exception.Errors,
            });
        }
        catch (ProductActivationValidationException exception)
        {
            return Results.Problem(statusCode: 422, extensions: new Dictionary<string, object?>
            {
                ["code"] = ProductApiV1.ValidationFailed,
                ["errors"] = exception.Errors,
            });
        }
        catch (ProductVersionConflictException exception)
        {
            return Results.Problem(statusCode: 409, extensions: new Dictionary<string, object?>
            {
                ["code"] = ProductApiV1.VersionConflict,
                ["expectedVersion"] = exception.ExpectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["currentVersion"] = exception.CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }
        catch (ProductOperationException exception) { return Problem(exception.Code, exception.StatusCode); }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: StatusCodes.Status403Forbidden); }
        // Unexpected storage/projection faults go to the host's canonical exception
        // handler before IResult is executed. No partial Product body has been sent.
    }

    private static IResult Problem(string code, int statusCode) => Results.Problem(statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record ProductImportForm(byte[] Workbook, ProductImportColumnMappingV1 Mapping,
    string? FileSha256, string? PreviewToken);

internal sealed class ProductImportPayloadTooLargeException : Exception;

internal static class ProductImportRequestBoundary
{
    private const long MultipartOverheadBytes = 64L * 1024;
    internal const long MultipartBodyLengthLimit = FormOptions.DefaultMultipartBodyLengthLimit;
    internal const long MaximumWorkbookBytes = MultipartBodyLengthLimit - MultipartOverheadBytes;

    internal static async Task<ProductImportForm> ReadAsync(HttpContext context,
        IGroupSharedMasterAuthorizationService authority, bool apply, CancellationToken cancellationToken)
    {
        var snapshot = await authority.GetSnapshotAsync(GroupSharedMasterKind.Product,
            GroupSharedMasterOperation.DraftCreate, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _ = GroupSharedMasterCommandContext.FromServerDecision(snapshot, GroupSharedMasterOperation.DraftCreate);

        var request = context.Request;
        if (request.ContentLength is { } declaredLength && declaredLength > MultipartBodyLengthLimit)
            throw new ProductImportPayloadTooLargeException();

        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = MultipartBodyLengthLimit;

        string[] fields = apply
            ? [ProductImportApiV1.LegacyCodeColumnField, ProductImportApiV1.NameColumnField,
                ProductImportApiV1.GroupIdColumnField, ProductImportApiV1.FileSha256Field, ProductImportApiV1.PreviewTokenField]
            : [ProductImportApiV1.LegacyCodeColumnField, ProductImportApiV1.NameColumnField,
                ProductImportApiV1.GroupIdColumnField];
        context.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
        {
            MultipartBodyLengthLimit = MultipartBodyLengthLimit,
            ValueCountLimit = fields.Length + 1,
            MultipartHeadersCountLimit = 8,
        }));

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            throw new ProductImportPayloadTooLargeException();
        }
        catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException or IOException)
        {
            throw new ProductValidationException([new("file", ProductImportCodesV1.InvalidWorkbook)]);
        }

        if (form.Keys.Count != fields.Length || !form.Keys.All(fields.Contains)
            || fields.Any(field => form[field].Count != 1)
            || form.Files.Count != 1
            || !string.Equals(form.Files[0].Name, ProductImportApiV1.FileField, StringComparison.Ordinal))
            throw new ProductValidationException([new("form", ProductImportCodesV1.InvalidMetadata)]);

        var file = form.Files[0];
        if (file.Length > MaximumWorkbookBytes) throw new ProductImportPayloadTooLargeException();
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream(file.Length > 0 ? checked((int)file.Length) : 0);
        await stream.CopyToAsync(buffer, cancellationToken);
        var mapping = new ProductImportColumnMappingV1(form[ProductImportApiV1.LegacyCodeColumnField].ToString(),
            form[ProductImportApiV1.NameColumnField].ToString(), form[ProductImportApiV1.GroupIdColumnField].ToString());
        return new ProductImportForm(buffer.ToArray(), mapping,
            apply ? form[ProductImportApiV1.FileSha256Field].ToString() : null,
            apply ? form[ProductImportApiV1.PreviewTokenField].ToString() : null);
    }
}

/// <summary>OpenAPI-only shape; the endpoint enforces exact multipart fields itself.</summary>
public class ProductImportPreviewFormV1
{
    public required IFormFile File { get; init; }
    public required string LegacyCodeColumn { get; init; }
    public required string NameColumn { get; init; }
    public required string GroupIdColumn { get; init; }
}

/// <summary>OpenAPI-only apply shape; the endpoint binds the exact preview and immutable workbook bytes.</summary>
public sealed class ProductImportApplyFormV1
{
    public required IFormFile File { get; init; }
    public required string LegacyCodeColumn { get; init; }
    public required string NameColumn { get; init; }
    public required string GroupIdColumn { get; init; }
    public required string FileSha256 { get; init; }
    public required string PreviewToken { get; init; }
}
