using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Api.Errors;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

public static partial class ErpOpenApiContract
{
    public static RouteHandlerBuilder WithProductRuntimeContract(this RouteHandlerBuilder builder,
        bool mutation = false, bool notFound = false, bool primitiveBinding = false,
        bool activationValidation = false)
    {
        builder.Produces<ProductRuntimeProblemContract>(400, "application/problem+json")
            .Produces<ProductRuntimeProblemContract>(401, "application/problem+json")
            .Produces<ProductRuntimeProblemContract>(403, "application/problem+json")
            .ProducesErpInternalError();
        if (notFound) builder.Produces<ProductRuntimeProblemContract>(404, "application/problem+json");
        if (mutation)
            builder.Produces<ProductRuntimeProblemContract>(409, "application/problem+json")
                .Produces<ProductRuntimeProblemContract>(503, "application/problem+json")
                .Produces(415);
        if (activationValidation)
            builder.Produces<ProductRuntimeProblemContract>(422, "application/problem+json");
        if (primitiveBinding) builder.AllowsEmptyErpBadRequestBody();
        builder.Add(endpoint => endpoint.Metadata.Add(new ProductRuntimeContractMetadata()));
        return builder;
    }

    private static Task TransformProductOperationAsync(OpenApiOperation operation,
        OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Description.ActionDescriptor.EndpointMetadata.OfType<ProductRuntimeContractMetadata>().Any())
            return Task.CompletedTask;
        operation.Description = "Explicit server-resolved global Product authority is required for this operation. "
            + "X-Company-Id selects the authorized origin, not a Product owner. Company roles alone grant no global access. "
            + "Reads never create data. Responses are private, no-store; a successful read is not a durable write grant.";
        // Every versioned card write (draft edit, DEC-PRD-010 activation, deactivation, reactivation) answers
        // lifecycle and version conflicts.
        var versioned = operation.OperationId is "EditProductDraftV1" or "ActivateProductV1"
            or "DeactivateProductV1" or "ReactivateProductV1";
        if (operation.Parameters is not null)
            foreach (var parameter in operation.Parameters)
            {
                if (parameter is OpenApiParameter bound && bound.In == ParameterLocation.Query && bound.Name is { Length: > 0 })
                    bound.Name = char.ToLowerInvariant(bound.Name[0]) + bound.Name[1..];
                if (parameter.Name == "X-Company-Id" && parameter is OpenApiParameter origin)
                    origin.Description = "Explicit authorized origin company, not an owner or a global Product grant.";
                if (parameter.Name == IdempotencyHeaderName && parameter is OpenApiParameter writable)
                {
                    writable.Description = "Exact opaque ASCII key, 1-128 visible characters excluding comma; no trimming. "
                        + "Reuse the same key and payload after an ambiguous transport result. Actor/origin/payload mismatch is 409. "
                        + "Replay requires fresh authority and returns the original successful status, card and correlation header.";
                    writable.Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 128,
                        Pattern = "^[!-+\\--~]{1,128}$" };
                }
            }
        foreach (var (status, response) in operation.Responses!)
        {
            if (response is not OpenApiResponse writable) continue;
            string[] codes = status switch
            {
                "400" => operation.OperationId == "GetProductsV1"
                    ? [ErpProblemCodes.CompanyContextInvalid, ErpProblemCodes.CorrelationMetadataInvalid, "invalid_cursor", "validation_failed"]
                    : [ErpProblemCodes.CompanyContextInvalid, ErpProblemCodes.CorrelationMetadataInvalid, "validation_failed"],
                "401" => ["authentication_required"],
                "404" => ["not_found"],
                "409" => versioned ? ["idempotency_conflict", "lifecycle_conflict", "version_conflict"] : ["idempotency_conflict"],
                "500" => ["internal_error"],
                "503" => ["product_write_contention"],
                "422" => ["validation_failed"],
                _ => [],
            };
            if (codes.Length > 0)
            {
                writable.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                writable.Extensions[ProblemCodesExtensionName] = new JsonNodeExtension(
                    new JsonArray(codes.Select(code => (JsonNode?)JsonValue.Create(code)).ToArray()));
            }
            if (status == "403") writable.Description = "Denied without a Product body; handler or company-boundary errors may omit code. "
                + "A company membership is not a global Product assignment.";
            if (status == "409") writable.Description = "No partial write. Version conflicts expose lossless string expectedVersion/currentVersion "
                + "at the problem root, not the company V1 numeric conflict object. Replay conflicts disclose no stored Product body.";
            if (status == "503") writable.Description = "Bounded database contention exhausted; no successful terminal was established. "
                + "Recover using the same idempotency key, never manufacture a new logical command.";
        }
        return Task.CompletedTask;
    }

    private static async Task TransformProductSchemaAsync(OpenApiSchema schema,
        OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(Optional<string>))
        {
            schema.Type = context.JsonPropertyInfo?.Name is "name" or "Name"
                ? JsonSchemaType.String : JsonSchemaType.String | JsonSchemaType.Null;
            schema.Description = "Omission leaves the value unchanged; explicit null is distinct and is validated per field.";
        }
        else if (type == typeof(Optional<Guid?>))
        {
            schema.Type = JsonSchemaType.String | JsonSchemaType.Null;
            schema.Format = "uuid";
            schema.Description = "Omitted: unchanged. Null: clear the optional reference. A supplied UUID must be nonempty.";
        }
        else if (type == typeof(Optional<ProductRoleInputs>) || type == typeof(Optional<ProductReferenceChanges>)
            || type == typeof(Optional<ProductUnitChanges>) || type == typeof(Optional<IReadOnlyDictionary<string, JsonElement>>))
        {
            var valueType = type == typeof(Optional<ProductRoleInputs>) ? typeof(ProductRoleInputs)
                : type == typeof(Optional<ProductReferenceChanges>) ? typeof(ProductReferenceChanges)
                : type == typeof(Optional<ProductUnitChanges>) ? typeof(ProductUnitChanges)
                : typeof(IReadOnlyDictionary<string, JsonElement>);
            schema.AllOf = [await context.GetOrCreateSchemaAsync(valueType, cancellationToken: cancellationToken)];
            schema.Description = "Optional JSON value, not an Optional wrapper. Omission leaves state unchanged; explicit null is rejected.";
        }
        if (type == typeof(ProductDraftChanges))
        {
            schema.Properties!["name"] = new OpenApiSchema { Type = JsonSchemaType.String,
                Description = "Omitted: unchanged. Explicit null/empty normalized name is invalid. The server trims and validates the name." };
            schema.Required?.Clear();
            schema.MinProperties = 1;
        }
        if (type == typeof(ProductDraftView)) SetPositiveIntegerStringProperty(schema, "version");
        if (type == typeof(ProductHistoryEntryView))
        {
            SetPositiveIntegerStringProperty(schema, "versionAfter");
            schema.Properties!["versionBefore"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                MinLength = 1,
                MaxLength = 19,
                Pattern = PositiveIntegerStringPattern,
                Description = "Lossless positive Int64 decimal string; null only for DraftCreate.",
            };
        }
        if (type == typeof(EditProductDraftRequest) || type == typeof(ActivateProductRequest)
            || type == typeof(ChangeProductLifecycleRequest))
        {
            schema.Properties!["expectedVersion"] = new OpenApiSchema { Type = JsonSchemaType.String,
                MinLength = 1, MaxLength = 19, Pattern = PositiveIntegerStringPattern,
                Description = "Required lossless positive Int64 decimal string, at most 9223372036854775807. JSON numbers are rejected." };
        }
        if (type == typeof(ProductRoleInput))
        {
            schema.OneOf = [RoleCombination("not-evaluated", JsonSchemaType.Null), RoleCombination("manual", JsonSchemaType.Boolean)];
        }
        if (type == typeof(ProductRuntimeProblemContract))
        {
            SetIntegerProperty(schema, "status", nullable: false);
            foreach (var name in new[] { "expectedVersion", "currentVersion" })
                schema.Properties![name] = new OpenApiSchema { Type = JsonSchemaType.String,
                    MinLength = 1, MaxLength = 19, Pattern = PositiveIntegerStringPattern };
            schema.Description = "Actual Product problem body. Type/title/status are present; code/detail/correlationId/errors "
                + "and lossless string version fields are conditional. A handler 403 can omit code. "
                + "No problem response establishes success or current authority.";
        }
    }

    private static OpenApiSchema RoleCombination(string provenance, JsonSchemaType valueType) => new()
    {
        Type = JsonSchemaType.Object,
        Required = new HashSet<string>(StringComparer.Ordinal) { "value", "provenance" },
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["value"] = new OpenApiSchema { Type = valueType },
            ["provenance"] = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create(provenance)] },
        },
    };

    private sealed record ProductRuntimeContractMetadata;
}

// Documentation only. Runtime still uses the existing module Results.Problem
// and host metadata/error middleware; this is not a second error pipeline.
public sealed class ProductRuntimeProblemContract
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required int Status { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }
    public string? Code { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<ProductRuntimeFieldError>? Errors { get; init; }
    public string? ExpectedVersion { get; init; }
    public string? CurrentVersion { get; init; }
}

public sealed record ProductRuntimeFieldError(string Field, string Code);
