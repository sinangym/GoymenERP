using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Errors;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Organization.Application.Contracts;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

public static class ErpOpenApiContract
{
    public const string DocumentName = "v1";

    public const string BearerSecuritySchemeName = "Bearer";

    public const string IdempotencyHeaderName = "Idempotency-Key";

    public const string CorrelationHeaderName = "X-Correlation-Id";

    public const string CausationHeaderName = "X-Causation-Id";

    public const string IdempotencyReplayedHeaderName = "Idempotency-Replayed";

    public const string OriginalCorrelationHeaderName =
        "Idempotency-Original-Correlation-Id";

    public const string ProblemCodesExtensionName = "x-erp-problem-codes";

    public const string EmptyBodyPossibleExtensionName =
        "x-erp-empty-body-possible";

    public const string CanonicalizationExtensionName =
        "x-erp-canonicalization";

    public const string CanonicalMinimumLengthExtensionName =
        "x-erp-canonical-min-length";

    public const string CanonicalMaximumLengthExtensionName =
        "x-erp-canonical-max-length";

    public const string AllowedFlagsExtensionName =
        "x-erp-allowed-flags";

    public const string AllowedValuesExtensionName =
        "x-erp-allowed-values";

    public const string NonEmptyUuidExtensionName =
        "x-erp-non-empty-uuid";

    public const string NormalizedMinimumExtensionName =
        "x-erp-normalized-minimum";

    public const string NormalizedMaximumExtensionName =
        "x-erp-normalized-maximum";

    public const string PositiveIntegerStringPattern = "^[1-9][0-9]*$";

    public const string KnownProblemCodePattern =
        "^(authentication_required|company_context_invalid|correlation_metadata_invalid|"
        + "forbidden|internal_error|service_unavailable|validation_failed|"
        + "command_metadata_invalid|invalid_cursor|not_found|already_linked|"
        + "version_conflict|idempotency_conflict)$";

    public static IServiceCollection AddErpOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Göymen ERP API";
                document.Info.Version = "v1";
                document.Servers = [];
                var components = document.Components ??= new OpenApiComponents();
                components.SecuritySchemes ??=
                    new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
                components.SecuritySchemes[BearerSecuritySchemeName] =
                    new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        Description = "OIDC/JWT bearer access token.",
                    };
                return Task.CompletedTask;
            });
            options.AddOperationTransformer(TransformOperationAsync);
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type == typeof(CreateCounterparty))
                {
                    schema.Required?.Remove("tradeName");
                    AddCanonicalLengthContract(
                        schema,
                        "legalName",
                        minimumLength: 1,
                        maximumLength: 250);
                    AddCanonicalLengthContract(
                        schema,
                        "tradeName",
                        minimumLength: null,
                        maximumLength: 200);
                }

                if (context.JsonTypeInfo.Type == typeof(SetCompanyPartyAccountActiveState))
                {
                    SetPositiveInteger(schema, "expectedVersion");
                    AddCanonicalLengthContract(
                        schema,
                        "reason",
                        minimumLength: 1,
                        maximumLength: 1000);
                }

                if (context.JsonTypeInfo.Type == typeof(SetLocationActiveState))
                {
                    SetPositiveInteger(schema, "expectedVersion");
                    AddCanonicalLengthContract(
                        schema,
                        "reason",
                        minimumLength: 1,
                        maximumLength: 1000);
                }

                if (context.JsonTypeInfo.Type == typeof(PartyKind))
                {
                    schema.Minimum = "1";
                    schema.Maximum = "2";
                    var allowedValues = new JsonArray(
                        (int)PartyKind.LegalEntity,
                        (int)PartyKind.NaturalPerson);
                    SetSchemaExtension(
                        schema,
                        AllowedValuesExtensionName,
                        allowedValues);
                }

                if (context.JsonTypeInfo.Type == typeof(PartyRole))
                {
                    schema.Minimum = "1";
                    schema.Maximum = "15";
                    var allowedFlags = new JsonArray(
                        (int)PartyRole.Customer,
                        (int)PartyRole.Supplier,
                        (int)PartyRole.Tolling,
                        (int)PartyRole.Service);
                    SetSchemaExtension(
                        schema,
                        AllowedFlagsExtensionName,
                        allowedFlags);
                }

                if (context.JsonTypeInfo.Type == typeof(AuditEventItem))
                {
                    SetIntegerProperty(schema, "entityVersionBefore", nullable: true);
                    SetIntegerProperty(schema, "entityVersionAfter", nullable: true);
                    SetIntegerProperty(schema, "schemaVersion", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(AuditEventPage))
                {
                    SetIntegerProperty(schema, "pageSize", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(CompanyPartyAccountView)
                    || context.JsonTypeInfo.Type == typeof(PartyCoreView)
                    || context.JsonTypeInfo.Type == typeof(LocationSummary))
                {
                    SetIntegerProperty(schema, "version", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(PageResult<LocationSummary>))
                {
                    SetIntegerProperty(schema, "page", nullable: false);
                    SetIntegerProperty(schema, "pageSize", nullable: false);
                    SetIntegerProperty(schema, "totalCount", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(ErpProblemDetailsContract))
                {
                    SetIntegerProperty(schema, "status", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(ErpVersionConflictContract))
                {
                    SetIntegerProperty(schema, "expectedVersion", nullable: false);
                    SetIntegerProperty(schema, "currentVersion", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(ErpProblemDetailsContract)
                    && schema.Properties is not null
                    && schema.Properties.TryGetValue("code", out var codeProperty)
                    && codeProperty is OpenApiSchema codeSchema)
                {
                    codeSchema.Pattern = KnownProblemCodePattern;
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static TBuilder UsesOptionalCompanyContext<TBuilder>(
        this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
            endpointBuilder.Metadata.Add(OptionalCompanyContextMetadata.Instance));
        return builder;
    }

    public static TBuilder WithIdempotencyContract<TBuilder>(
        this TBuilder builder,
        params int[] replayableStatusCodes)
        where TBuilder : IEndpointConventionBuilder
    {
        var statuses = replayableStatusCodes
            .Distinct()
            .Order()
            .ToArray();
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(
            new IdempotentCommandMetadata(statuses)));
        return builder;
    }

    public static TBuilder RequiresNonEmptyGuidPathParameter<TBuilder>(
        this TBuilder builder,
        string parameterName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(
            new NonEmptyGuidPathParameterMetadata(parameterName)));
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpAuthenticatedProblems(
        this RouteHandlerBuilder builder)
    {
        builder
            .Produces<ErpProblemDetailsContract>(
                StatusCodes.Status401Unauthorized,
                "application/problem+json")
            .Produces<ErpProblemDetailsContract>(
                StatusCodes.Status403Forbidden,
                "application/problem+json");
        AddProblemCodeMetadata(
            builder,
            StatusCodes.Status401Unauthorized,
            [ErpProblemCodes.AuthenticationRequired]);
        AddProblemCodeMetadata(
            builder,
            StatusCodes.Status403Forbidden,
            [ErpProblemCodes.Forbidden]);
        return builder.ProducesErpInternalError();
    }

    public static RouteHandlerBuilder ProducesErpInternalError(
        this RouteHandlerBuilder builder)
    {
        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status500InternalServerError,
            "application/problem+json");
        AddProblemCodeMetadata(
            builder,
            StatusCodes.Status500InternalServerError,
            [ErpProblemCodes.InternalError]);
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpBadRequest(
        this RouteHandlerBuilder builder,
        params string[] stableCodes)
    {
        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status400BadRequest,
            "application/problem+json");
        AddProblemCodeMetadata(
            builder,
            StatusCodes.Status400BadRequest,
            [ErpProblemCodes.CorrelationMetadataInvalid, .. stableCodes]);
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpNotFound(
        this RouteHandlerBuilder builder,
        params string[] stableCodes)
    {
        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status404NotFound,
            "application/problem+json");
        AddProblemCodeMetadata(builder, StatusCodes.Status404NotFound, stableCodes);
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpConflict(
        this RouteHandlerBuilder builder,
        params string[] stableCodes)
    {
        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status409Conflict,
            "application/problem+json");
        AddProblemCodeMetadata(builder, StatusCodes.Status409Conflict, stableCodes);
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpUnsupportedMediaType(
        this RouteHandlerBuilder builder)
    {
        builder.Produces(StatusCodes.Status415UnsupportedMediaType);
        AddProblemCodeMetadata(builder, StatusCodes.Status415UnsupportedMediaType, []);
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(
            new EmptyBodyPossibleResponseMetadata(StatusCodes.Status400BadRequest)));
        return builder;
    }

    public static RouteHandlerBuilder AllowsEmptyErpBadRequestBody(
        this RouteHandlerBuilder builder)
    {
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(
            new EmptyBodyPossibleResponseMetadata(StatusCodes.Status400BadRequest)));
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpServiceUnavailable(
        this RouteHandlerBuilder builder)
    {
        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status503ServiceUnavailable,
            "application/problem+json");
        AddProblemCodeMetadata(
            builder,
            StatusCodes.Status503ServiceUnavailable,
            [ErpProblemCodes.ServiceUnavailable]);
        return builder;
    }

    private static Task TransformOperationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var problemCodesByStatus = endpointMetadata
            .OfType<ErpProblemCodeMetadata>()
            .GroupBy(metadata => metadata.StatusCode)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(metadata => metadata.Codes)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

        AddHeaderParameter(
            operation,
            CorrelationHeaderName,
            required: false,
            "Optional client correlation identifier; the server creates one when absent.",
            minimumLength: 1,
            maximumLength: 128,
            pattern: "^[A-Za-z0-9._:-]+$");
        AddHeaderParameter(
            operation,
            CausationHeaderName,
            required: false,
            "Optional identifier of the command or event that caused this request.",
            minimumLength: 1,
            maximumLength: 128,
            pattern: "^[A-Za-z0-9._:-]+$");

        if (endpointMetadata.OfType<RequiresCompanyContextAttribute>().Any())
        {
            problemCodesByStatus[StatusCodes.Status400BadRequest] =
                problemCodesByStatus
                    .GetValueOrDefault(StatusCodes.Status400BadRequest, [])
                    .Append(ErpProblemCodes.CompanyContextInvalid)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            AddHeaderParameter(
                operation,
                CompanyContextMiddleware.CompanyHeaderName,
                required: true,
                "Selected company identifier for this company-scoped request.",
                "uuid",
                nonEmptyUuid: true);
        }
        else if (endpointMetadata.Contains(OptionalCompanyContextMetadata.Instance))
        {
            AddHeaderParameter(
                operation,
                CompanyContextMiddleware.CompanyHeaderName,
                required: false,
                "Optional selected company identifier for an authorized company view.",
                "uuid",
                nonEmptyUuid: true);
        }

        var idempotentCommand = endpointMetadata
            .OfType<IdempotentCommandMetadata>()
            .SingleOrDefault();
        var isIdempotentCommand = idempotentCommand is not null;
        if (isIdempotentCommand)
        {
            AddHeaderParameter(
                operation,
                IdempotencyHeaderName,
                required: true,
                "Unique command key; one non-empty value with at most 128 canonical characters.",
                minimumLength: 1,
                pattern: "^(?!.*[,])(?=.*\\S).+$",
                canonicalMaximumLength: 128);
        }

        if (string.Equals(operation.OperationId, "GetAuditEventsV1", StringComparison.Ordinal))
        {
            SetNormalizedIntegerParameter(
                operation,
                ParameterLocation.Query,
                "pageSize",
                minimum: 1,
                maximum: 100);
            SetCanonicalStringParameter(
                operation,
                ParameterLocation.Query,
                "cursor",
                maximumLength: 128);
            SetCanonicalStringParameter(
                operation,
                ParameterLocation.Query,
                "correlationId",
                maximumLength: 128);
            SetCanonicalStringParameter(
                operation,
                ParameterLocation.Query,
                "entityType",
                maximumLength: 160);
            SetCanonicalStringParameter(
                operation,
                ParameterLocation.Query,
                "entityId",
                maximumLength: 128);
        }


        foreach (var metadata in endpointMetadata
                     .OfType<NonEmptyGuidPathParameterMetadata>())
        {
            var parameter = operation.Parameters?.SingleOrDefault(candidate =>
                candidate.In == ParameterLocation.Path
                && string.Equals(
                    candidate.Name,
                    metadata.ParameterName,
                    StringComparison.Ordinal));
            if (parameter?.Schema is not OpenApiSchema parameterSchema)
            {
                throw new InvalidOperationException(
                    $"OpenAPI path parameter '{metadata.ParameterName}' is missing or not concrete.");
            }

            SetSchemaExtension(
                parameterSchema,
                NonEmptyUuidExtensionName,
                JsonValue.Create(true)!);
        }

        var requiresAuthorization = endpointMetadata.OfType<IAuthorizeData>().Any()
            && !endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuthorization)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    BearerSecuritySchemeName,
                    context.Document,
                    externalResource: null)] = [],
            });
        }

        operation.Responses ??= new OpenApiResponses();
        foreach (var (statusCode, response) in operation.Responses)
        {
            if (response is not OpenApiResponse concreteResponse)
            {
                continue;
            }

            var hasNumericStatusCode = int.TryParse(
                statusCode,
                out var numericStatusCode);
            concreteResponse.Headers ??=
                new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
            concreteResponse.Headers[CorrelationHeaderName] = CorrelationHeader(
                "Server-resolved correlation identifier.");
            if (hasNumericStatusCode
                && idempotentCommand?.ReplayableStatusCodes.Contains(numericStatusCode) == true)
            {
                concreteResponse.Headers[IdempotencyReplayedHeaderName] = BooleanHeader(
                    "True only when the response safely replays an earlier command result.");
                concreteResponse.Headers[OriginalCorrelationHeaderName] = CorrelationHeader(
                    "Original command correlation identifier when the response is a replay.");
            }

            if (hasNumericStatusCode
                && numericStatusCode >= StatusCodes.Status400BadRequest
                && problemCodesByStatus.TryGetValue(numericStatusCode, out var problemCodes))
            {
                var codeArray = new JsonArray();
                foreach (var problemCode in problemCodes)
                {
                    codeArray.Add(problemCode);
                }

                concreteResponse.Extensions ??=
                    new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                concreteResponse.Extensions[ProblemCodesExtensionName] =
                    new JsonNodeExtension(codeArray);
            }

            if (hasNumericStatusCode
                && endpointMetadata.OfType<EmptyBodyPossibleResponseMetadata>()
                    .Any(metadata => metadata.StatusCode == numericStatusCode))
            {
                concreteResponse.Extensions ??=
                    new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                concreteResponse.Extensions[EmptyBodyPossibleExtensionName] =
                    new JsonNodeExtension(JsonValue.Create(true)!);
            }
        }

        return Task.CompletedTask;
    }

    private static void AddHeaderParameter(
        OpenApiOperation operation,
        string name,
        bool required,
        string description,
        string? format = null,
        int? minimumLength = null,
        int? maximumLength = null,
        string? pattern = null,
        int? canonicalMaximumLength = null,
        bool nonEmptyUuid = false)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter =>
                string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)
                && parameter.In == ParameterLocation.Header))
        {
            return;
        }

        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Format = format,
            MinLength = minimumLength,
            MaxLength = maximumLength,
            Pattern = pattern,
        };
        if (canonicalMaximumLength is not null)
        {
            SetSchemaExtension(
                schema,
                CanonicalizationExtensionName,
                JsonValue.Create("trim")!);
            SetSchemaExtension(
                schema,
                CanonicalMaximumLengthExtensionName,
                JsonValue.Create(canonicalMaximumLength.Value)!);
        }

        if (nonEmptyUuid)
        {
            SetSchemaExtension(
                schema,
                NonEmptyUuidExtensionName,
                JsonValue.Create(true)!);
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Header,
            Required = required,
            Description = description,
            Schema = schema,
        });
    }

    private static void AddCanonicalLengthContract(
        OpenApiSchema schema,
        string propertyName,
        int? minimumLength,
        int? maximumLength)
    {
        if (schema.Properties?.TryGetValue(propertyName, out var property) != true
            || property is not OpenApiSchema concreteProperty)
        {
            throw new InvalidOperationException(
                $"OpenAPI property '{propertyName}' is not a concrete schema.");
        }

        concreteProperty.MinLength = null;
        concreteProperty.MaxLength = null;
        SetSchemaExtension(
            concreteProperty,
            CanonicalizationExtensionName,
            JsonValue.Create("trim")!);
        if (minimumLength is not null)
        {
            SetSchemaExtension(
                concreteProperty,
                CanonicalMinimumLengthExtensionName,
                JsonValue.Create(minimumLength.Value)!);
        }

        if (maximumLength is not null)
        {
            SetSchemaExtension(
                concreteProperty,
                CanonicalMaximumLengthExtensionName,
                JsonValue.Create(maximumLength.Value)!);
        }
    }

    private static void SetPositiveInteger(
        OpenApiSchema schema,
        string propertyName)
    {
        if (schema.Properties?.TryGetValue(propertyName, out var property) != true
            || property is not OpenApiSchema concreteProperty)
        {
            throw new InvalidOperationException(
                $"OpenAPI property '{propertyName}' is not a concrete schema.");
        }

        concreteProperty.Minimum = "1";
        concreteProperty.Pattern = PositiveIntegerStringPattern;
    }

    private static void SetCanonicalStringParameter(
        OpenApiOperation operation,
        ParameterLocation location,
        string parameterName,
        int maximumLength)
    {
        var schema = GetConcreteParameterSchema(operation, location, parameterName);
        schema.MinLength = null;
        schema.MaxLength = null;
        SetSchemaExtension(
            schema,
            CanonicalizationExtensionName,
            JsonValue.Create("trim")!);
        SetSchemaExtension(
            schema,
            CanonicalMaximumLengthExtensionName,
            JsonValue.Create(maximumLength)!);
    }

    private static void SetNormalizedIntegerParameter(
        OpenApiOperation operation,
        ParameterLocation location,
        string parameterName,
        int minimum,
        int maximum)
    {
        var schema = GetConcreteParameterSchema(operation, location, parameterName);
        SetSchemaExtension(
            schema,
            NormalizedMinimumExtensionName,
            JsonValue.Create(minimum)!);
        SetSchemaExtension(
            schema,
            NormalizedMaximumExtensionName,
            JsonValue.Create(maximum)!);
    }

    private static OpenApiSchema GetConcreteParameterSchema(
        OpenApiOperation operation,
        ParameterLocation location,
        string parameterName)
    {
        var parameter = operation.Parameters?.SingleOrDefault(candidate =>
            candidate.In == location
            && string.Equals(candidate.Name, parameterName, StringComparison.Ordinal));
        if (parameter?.Schema is not OpenApiSchema schema)
        {
            throw new InvalidOperationException(
                $"OpenAPI parameter '{location}:{parameterName}' is missing or not concrete.");
        }

        return schema;
    }

    private static void SetIntegerProperty(
        OpenApiSchema schema,
        string propertyName,
        bool nullable)
    {
        if (schema.Properties?.TryGetValue(propertyName, out var property) != true
            || property is not OpenApiSchema concreteProperty)
        {
            throw new InvalidOperationException(
                $"OpenAPI property '{propertyName}' is not a concrete schema.");
        }

        concreteProperty.Type = nullable
            ? JsonSchemaType.Integer | JsonSchemaType.Null
            : JsonSchemaType.Integer;
        concreteProperty.Pattern = null;
    }

    private static void SetSchemaExtension(
        OpenApiSchema schema,
        string name,
        JsonNode value)
    {
        schema.Extensions ??=
            new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        schema.Extensions[name] = new JsonNodeExtension(value);
    }

    private static OpenApiHeader CorrelationHeader(string description)
    {
        return new OpenApiHeader
        {
            Description = description,
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 1,
                MaxLength = 128,
                Pattern = "^[A-Za-z0-9._:-]+$",
            },
        };
    }

    private static OpenApiHeader BooleanHeader(string description)
    {
        return new OpenApiHeader
        {
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean },
        };
    }

    private static void AddProblemCodeMetadata(
        IEndpointConventionBuilder builder,
        int statusCode,
        IReadOnlyList<string> codes)
    {
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(
            new ErpProblemCodeMetadata(statusCode, codes)));
    }

    private sealed class OptionalCompanyContextMetadata
    {
        public static readonly OptionalCompanyContextMetadata Instance = new();
    }

    private sealed record IdempotentCommandMetadata(
        IReadOnlySet<int> ReplayableStatusCodes)
    {
        public IdempotentCommandMetadata(IEnumerable<int> replayableStatusCodes)
            : this(replayableStatusCodes.ToHashSet())
        {
        }
    }

    private sealed record ErpProblemCodeMetadata(
        int StatusCode,
        IReadOnlyList<string> Codes);

    private sealed record EmptyBodyPossibleResponseMetadata(int StatusCode);

    private sealed record NonEmptyGuidPathParameterMetadata(string ParameterName);
}

public sealed class ErpProblemDetailsContract
{
    public required string Type { get; init; }

    public required string Title { get; init; }

    public required int Status { get; init; }

    public required string Detail { get; init; }

    public string? Code { get; init; }

    public string? CorrelationId { get; init; }

    public ErpVersionConflictContract? Conflict { get; init; }
}

public sealed class ErpVersionConflictContract
{
    public required long ExpectedVersion { get; init; }

    public required long CurrentVersion { get; init; }
}
