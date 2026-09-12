using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace GoymenERP.Modules.Product.Api;

public static class ProductEndpoints
{
    /// <summary>Central host maps this only after the global security/persistence adapter is accepted.</summary>
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ProductApiV1.BasePath).WithTags("Product").RequireAuthorization();

        group.MapGet("/", async ([AsParameters] ProductListQuery query, IProductService service,
            HttpContext context, CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
            {
                if (query.PageSize is < 1 or > 100 || query.Q?.Length > 200
                    || query.Q?.Any(char.IsControl) == true
                    || (query.Lifecycle is not null && query.Lifecycle is not ("Draft" or "Active" or "Inactive"))
                    || (query.Role is not null && query.Role is not ("purchasable" or "sellable" or "productionInput" or "productionOutput"))
                    || query.GroupId == Guid.Empty || query.ClassId == Guid.Empty || query.FamilyId == Guid.Empty)
                {
                    return Problem(ProductApiV1.ValidationFailed, 400);
                }

                return Results.Ok(await service.GetPageAsync(query with { Q = query.Q?.Trim() }, cancellationToken));
            })).WithName("GetProductsV1").Produces<ProductPage>();

        group.MapGet("/code-groups", async (IProductService service, HttpContext context,
            CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
                Results.Ok(await service.GetCodeGroupsAsync(cancellationToken))))
            .WithName("GetProductCodeGroupsV1").Produces<IReadOnlyList<ProductCodeGroup>>();

        group.MapGet("/{id:guid}", async (Guid id, IProductService service, HttpContext context,
            CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
            {
                var card = await service.GetAsync(id, cancellationToken);
                return card is null ? Problem(ProductApiV1.NotFound, 404) : Results.Ok(card);
            })).WithName("GetProductV1").Produces<ProductCardView>().ProducesProblem(404);

        group.MapPost("/", async (CreateProductDraftRequest request, IProductService service,
            HttpContext context, CancellationToken cancellationToken) => await ExecuteAsync(context, async () =>
            {
                var result = await service.CreateDraftAsync(request, ReadIdempotencyKey(context), cancellationToken);
                return Results.Created($"{ProductApiV1.BasePath}/{result.Card.Product.Id:D}", result);
            })).WithName("CreateProductDraftV1").Produces<ProductMutationResponse>(201);

        group.MapPatch("/{id:guid}/draft", async (Guid id, EditProductDraftRequest request,
            IProductService service, HttpContext context, CancellationToken cancellationToken) =>
            await ExecuteAsync(context, async () => Results.Ok(await service.EditDraftAsync(id, request,
                ReadIdempotencyKey(context), cancellationToken))))
            .WithName("EditProductDraftV1").Produces<ProductMutationResponse>();
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

    private static async Task<IResult> ExecuteAsync(HttpContext context, Func<Task<IResult>> execute)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        try { return await execute(); }
        catch (ProductValidationException exception)
        {
            return Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?>
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
        // Unexpected storage/projection faults go to the host's canonical exception
        // handler before IResult is executed. No partial Product body has been sent.
    }

    private static IResult Problem(string code, int statusCode) => Results.Problem(statusCode: statusCode,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
