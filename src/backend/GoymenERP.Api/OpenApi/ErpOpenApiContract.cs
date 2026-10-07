using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.Errors;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

public static partial class ErpOpenApiContract
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
        + "version_conflict|idempotency_conflict|client_contract_required|"
        + "client_contract_unsupported|product_selection_changed|unsupported_currency|"
        + "unsupported_product|unsupported_unit)$";

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
            options.AddOperationTransformer(TransformProductOperationAsync);
            options.AddSchemaTransformer(TransformProductSchemaAsync);
            options.AddSchemaTransformer((schema, context, _) =>
            {
                ApplyContactInformationSchema(schema, context.JsonTypeInfo.Type);
                if (context.JsonTypeInfo.Type == typeof(SalesDraftLineCommercialV1))
                {
                    schema.Required ??= new HashSet<string>();
                    schema.Required.Add("unitPrice");
                    schema.Required.Add("priceUnitId");
                    schema.Required.Add("discountPercent");
                }

                if (context.JsonTypeInfo.Type == typeof(CreateCounterparty))
                {
                    schema.Required?.Remove("tradeName");
                    schema.Required?.Remove("roles");
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

                if (context.JsonTypeInfo.Type == typeof(CreateCompanyPartyAccount))
                {
                    schema.Required?.Remove("roles");
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

                if (context.JsonTypeInfo.Type == typeof(SetLocationActiveState)
                    // ORG master edit: every edit carries the row version it expects and the audited reason, as the
                    // active-state command does.
                    || context.JsonTypeInfo.Type == typeof(UpdateSite)
                    || context.JsonTypeInfo.Type == typeof(UpdateLocation)
                    || context.JsonTypeInfo.Type == typeof(UpdateWorkCenter))
                {
                    SetPositiveInteger(schema, "expectedVersion");
                    AddCanonicalLengthContract(
                        schema,
                        "reason",
                        minimumLength: 1,
                        maximumLength: 1000);
                }

                if (context.JsonTypeInfo.Type == typeof(CreateSite)
                    || context.JsonTypeInfo.Type == typeof(CreateLocation)
                    || context.JsonTypeInfo.Type == typeof(CreateWorkCenter))
                {
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
                    schema.Description = "Optional legacy trade classification. Zero means no specified "
                        + "classification; values 1 through 15 retain their stored flags. "
                        + "This value does not grant user permissions or restrict buying and selling.";
                    schema.Minimum = "0";
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

                // AT-055 (#518 P2): a dispatch note names 1..500 DISTINCT shipments; the lifecycle commands' text shapes.
                if (context.JsonTypeInfo.Type == typeof(GoymenERP.Modules.Logistics.Application.Contracts.CreateDispatchNoteRequest))
                {
                    SetArrayBounds(
                        schema,
                        "shipmentIds",
                        minimumItems: 1,
                        maximumItems: GoymenERP.Modules.Logistics.Application.Contracts.LogisticsJson.MaximumDispatchNoteShipments,
                        uniqueItems: true);
                }

                if (context.JsonTypeInfo.Type == typeof(GoymenERP.Modules.Logistics.Application.Contracts.CancelDispatchNoteRequest))
                {
                    SetStringBounds(
                        schema,
                        "reason",
                        minimumLength: 1,
                        maximumLength: GoymenERP.Modules.Logistics.Application.Contracts.LogisticsJson.CancelReasonMaximumLength,
                        pattern: null);
                }

                if (context.JsonTypeInfo.Type == typeof(GoymenERP.Modules.Logistics.Application.Contracts.SetEDispatchNumberRequest))
                {
                    SetStringBounds(
                        schema,
                        "eDispatchNumber",
                        minimumLength: 16,
                        maximumLength: 16,
                        pattern: GoymenERP.Modules.Logistics.Application.Contracts.LogisticsJson.EDispatchNumberPattern);
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
                    || context.JsonTypeInfo.Type == typeof(CounterpartyListAccount)
                    || context.JsonTypeInfo.Type == typeof(CounterpartyListItem)
                    || context.JsonTypeInfo.Type == typeof(LocationSummary)
                    // ORG-04: the work-center card's row version is the location card's integer, not a string.
                    || context.JsonTypeInfo.Type == typeof(WorkCenterDetail)
                    // ORG master edit: the site row and a create's answer carry the same integer row version.
                    || context.JsonTypeInfo.Type == typeof(SiteSummary)
                    || context.JsonTypeInfo.Type == typeof(OrganizationCreatedRecord))
                {
                    SetIntegerProperty(schema, "version", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(CounterpartyDetailAccountView)
                    || context.JsonTypeInfo.Type == typeof(CounterpartyLinkCandidateSourceAccount)
                    || context.JsonTypeInfo.Type == typeof(CounterpartyDetailView)
                    || context.JsonTypeInfo.Type == typeof(CounterpartyMutationAccountView)
                    || context.JsonTypeInfo.Type == typeof(CounterpartyMutationView))
                {
                    SetPositiveIntegerStringProperty(schema, "version");
                }

                if (context.JsonTypeInfo.Type == typeof(CounterpartyMutationView))
                {
                    SetExactSingleItemArrayProperty(schema, "companyAccounts");
                }

                if (context.JsonTypeInfo.Type == typeof(PageResult<LocationSummary>))
                {
                    SetIntegerProperty(schema, "page", nullable: false);
                    SetIntegerProperty(schema, "pageSize", nullable: false);
                    SetIntegerProperty(schema, "totalCount", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(ErpProblemDetailsContract)
                    || context.JsonTypeInfo.Type == typeof(ErpCodeLessProblemDetailsContract))
                {
                    SetIntegerProperty(schema, "status", nullable: false);
                }

                if (context.JsonTypeInfo.Type == typeof(ErpCodeLessProblemDetailsContract))
                {
                    schema.Not = new OpenApiSchema
                    {
                        Required = new HashSet<string>(StringComparer.Ordinal)
                        {
                            "code",
                        },
                    };
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
        this TBuilder builder,
        bool usesPlatformInvalidCode = false)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
            endpointBuilder.Metadata.Add(
                new OptionalCompanyContextMetadata(usesPlatformInvalidCode)));
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
        if (stableCodes.Length == 0)
        {
            throw new InvalidOperationException(
                "Coded 404 OpenAPI responses require at least one stable code. "
                + "Use ProducesErpCodeLessNotFound for module-owned code-less responses.");
        }

        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status404NotFound,
            "application/problem+json");
        AddProblemCodeMetadata(builder, StatusCodes.Status404NotFound, stableCodes);
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpCodeLessNotFound(
        this RouteHandlerBuilder builder)
    {
        return builder.Produces<ErpCodeLessProblemDetailsContract>(
            StatusCodes.Status404NotFound,
            "application/problem+json");
    }

    public static RouteHandlerBuilder ProducesErpUnprocessableEntity(
        this RouteHandlerBuilder builder,
        params string[] stableCodes)
    {
        if (stableCodes.Length == 0)
        {
            throw new InvalidOperationException(
                "Coded 422 OpenAPI responses require at least one stable code.");
        }

        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status422UnprocessableEntity,
            "application/problem+json");
        AddProblemCodeMetadata(builder, StatusCodes.Status422UnprocessableEntity, stableCodes);
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpConflict(
        this RouteHandlerBuilder builder,
        params string[] stableCodes)
    {
        if (stableCodes.Length == 0)
        {
            throw new InvalidOperationException(
                "Coded 409 OpenAPI responses require at least one stable code. "
                + "Use ProducesErpCodeLessConflict for module-owned code-less responses.");
        }

        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status409Conflict,
            "application/problem+json");
        AddProblemCodeMetadata(builder, StatusCodes.Status409Conflict, stableCodes);
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpCodeLessConflict(
        this RouteHandlerBuilder builder)
    {
        return builder.Produces<ErpCodeLessProblemDetailsContract>(
            StatusCodes.Status409Conflict,
            "application/problem+json");
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

    public static RouteHandlerBuilder AllowsCodeLessErpBadRequestBody(
        this RouteHandlerBuilder builder)
    {
        builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(
            new CodeLessProblemDetailsResponseMetadata(
                StatusCodes.Status400BadRequest)));
        return builder;
    }

    public static RouteHandlerBuilder ProducesErpServiceUnavailable(
        this RouteHandlerBuilder builder,
        params string[] codes)
    {
        builder.Produces<ErpProblemDetailsContract>(
            StatusCodes.Status503ServiceUnavailable,
            "application/problem+json");
        AddProblemCodeMetadata(
            builder,
            StatusCodes.Status503ServiceUnavailable,
            [ErpProblemCodes.ServiceUnavailable, .. codes]);
        return builder;
    }

    private static async Task TransformOperationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var hasCounterpartyClientContract = endpointMetadata
            .OfType<CounterpartyClientContractMetadata>().Any();
        var problemCodesByStatus = endpointMetadata
            .OfType<ErpProblemCodeMetadata>()
            .GroupBy(metadata => metadata.StatusCode)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(metadata => metadata.Codes)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());

        var isManufacturingInventoryRealizationOperation = operation.OperationId is
            "IssueManufacturingInputV1"
            or "ReturnManufacturingInputV1"
            or "DeclareManufacturingOutputV1";
        if (!isManufacturingInventoryRealizationOperation
            && problemCodesByStatus.TryGetValue(
                StatusCodes.Status400BadRequest,
                out var badRequestCodes))
        {
            problemCodesByStatus[StatusCodes.Status400BadRequest] = badRequestCodes
                .Where(code => code != GoymenERP.Modules.Inventory.Application
                                   .InventoryManufacturingRealizationCodesV1.InvalidRequest
                               && code != GoymenERP.Modules.Inventory.Application
                                   .InventoryManufacturingRealizationCodesV1.InvalidVersion
                               && code != GoymenERP.Modules.Inventory.Application
                                   .InventoryManufacturingRealizationCodesV1.InvalidQuantity)
                .ToArray();
        }
        else if (isManufacturingInventoryRealizationOperation)
        {
            problemCodesByStatus[StatusCodes.Status409Conflict] = problemCodesByStatus
                .GetValueOrDefault(StatusCodes.Status409Conflict, [])
                .Append(GoymenERP.Api.ManufacturingRuntime
                    .ManufacturingInventoryInputIssueHostCodes.IncompleteRealization)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

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
        else if (endpointMetadata
                 .OfType<OptionalCompanyContextMetadata>()
                 .SingleOrDefault() is { } optionalCompanyContext)
        {
            if (optionalCompanyContext.UsesPlatformInvalidCode)
            {
                problemCodesByStatus[StatusCodes.Status400BadRequest] =
                    problemCodesByStatus
                        .GetValueOrDefault(StatusCodes.Status400BadRequest, [])
                        .Append(ErpProblemCodes.CompanyContextInvalid)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
            }

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
        if (hasCounterpartyClientContract)
        {
            AddHeaderParameter(
                operation,
                CounterpartyClientContract.HeaderName,
                required: !HttpMethods.IsGet(context.Description.HttpMethod ?? string.Empty),
                "Exact client compatibility declaration. Required for every command and "
                    + "for reads containing zero-role accounts. Before enabling commands, "
                    + "require the matching server acknowledgment on a fresh, successful, "
                    + "validated GET bound to the current API origin, actor/session, "
                    + "selected company and generation. An error response never establishes readiness.",
                pattern: CounterpartyClientContract.VersionPattern);
        }
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

        if (string.Equals(
                operation.OperationId,
                "GetCounterpartiesV1",
                StringComparison.Ordinal))
        {
            AddCounterpartyReadPageParameters(operation);
        }

        if (string.Equals(
                operation.OperationId,
                "GetCounterpartyCompanyAccountLinkCandidatesV1",
                StringComparison.Ordinal))
        {
            AddCounterpartyLinkCandidateParameters(operation);
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

        if (string.Equals(operation.OperationId, "GetSalesAccountReferencesV1", StringComparison.Ordinal))
        {
            SetNormalizedIntegerParameter(operation, ParameterLocation.Query, "pageSize", 1, 100);
            SetStringLengthParameter(operation, ParameterLocation.Query, "q", null, 200);
            SetStringLengthParameter(operation, ParameterLocation.Query, "cursor", 1, 8192);
        }

        if (string.Equals(operation.OperationId, "GetSalesProductReferenceV1", StringComparison.Ordinal))
        {
            SetStringLengthParameter(
                operation, ParameterLocation.Query, "expectedProductVersion", 1, 128);
        }

        if (string.Equals(operation.OperationId, "GetAccountingTrialBalanceV1", StringComparison.Ordinal))
        {
            AddQueryParameter(operation, "fromAccountingDate",
                new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
                "Inclusive first accounting date (yyyy-MM-dd).", required: true);
            AddQueryParameter(operation, "throughAccountingDate",
                new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
                "Inclusive last accounting date (yyyy-MM-dd).", required: true);
            AddQueryParameter(operation, "currencyId",
                new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                "Optional non-empty currency identifier; currencies are never combined.");
        }

        if (string.Equals(
                operation.OperationId,
                GoymenERP.Modules.Inventory.Application.InventoryParentRollApiV1.GetCandidatesOperationId,
                StringComparison.Ordinal))
        {
            AddInventoryParentRollCandidateParameters(operation);
        }

        if (string.Equals(
                operation.OperationId,
                GoymenERP.Modules.Inventory.Application.InventoryStockPositionApiV1.GetPositionsOperationId,
                StringComparison.Ordinal))
        {
            await AddInventoryStockPositionParametersAsync(operation, context, cancellationToken);
        }

        if (string.Equals(
                operation.OperationId,
                GoymenERP.Modules.Inventory.Application.InventoryMovementTimelineApiV1.GetOperationId,
                StringComparison.Ordinal))
        {
            AddInventoryMovementTimelineParameters(operation);
        }

        if (string.Equals(operation.OperationId, "GetInventoryPhysicalAllocationsV1", StringComparison.Ordinal))
        {
            AddInventoryPhysicalAllocationReadParameters(operation);
        }

        // AT-055 (#518 P2): the Sales order's dispatch notes are read for exactly one REQUIRED uuid; the route reads it by hand.
        if (string.Equals(operation.OperationId, "GetSalesOrderDispatchNotesV1", StringComparison.Ordinal))
        {
            AddQueryParameter(
                operation,
                "salesOrderId",
                new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                "Required Sales order identifier; the only query member.",
                required: true);
        }

        if (operation.OperationId is "GetInventoryProductCandidatesV1"
            or "GetManufacturingOutputProductCandidatesV1")
        {
            AddInventoryProductCandidateParameters(operation);
        }

        if (operation.OperationId is "GetInventoryLocationCandidatesV1"
            or "GetManufacturingOutputLocationCandidatesV1")
        {
            AddInventoryLocationCandidateParameters(operation);
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
            if (hasCounterpartyClientContract)
            {
                concreteResponse.Headers[CounterpartyClientContract.HeaderName] =
                    new OpenApiHeader
                    {
                        Required = false,
                        Description = "Optional server acknowledgment, present only after the "
                            + "client-contract filter accepts the exact request declaration. "
                            + "It can accompany handler-generated errors; errors before the filter "
                            + "(including authentication, company resolution and body binding) may omit it. "
                            + "Its presence on an error is not command success or client readiness. "
                            + "Readiness requires a fresh, successful, validated GET with the matching "
                            + "acknowledgment bound to the current API origin, actor/session, selected "
                            + "company and generation. Absence on an error is not proof of an old server.",
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Pattern = CounterpartyClientContract.VersionPattern,
                        },
                    };
            }
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
                && problemCodesByStatus.TryGetValue(numericStatusCode, out var problemCodes)
                && problemCodes.Length > 0)
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
                && endpointMetadata.OfType<CodeLessProblemDetailsResponseMetadata>()
                    .Any(metadata => metadata.StatusCode == numericStatusCode)
                && concreteResponse.Content is not null
                && concreteResponse.Content.TryGetValue(
                    "application/problem+json",
                    out var problemMediaType)
                && problemMediaType is OpenApiMediaType concreteProblemMediaType)
            {
                concreteProblemMediaType.Schema = new OpenApiSchema
                {
                    OneOf =
                    [
                        new OpenApiSchemaReference(
                            nameof(ErpProblemDetailsContract),
                            context.Document,
                            externalResource: null),
                        new OpenApiSchemaReference(
                            nameof(ErpCodeLessProblemDetailsContract),
                            context.Document,
                            externalResource: null),
                    ],
                };
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

    private static void AddCounterpartyReadPageParameters(OpenApiOperation operation)
    {
        AddQueryParameter(
            operation,
            "q",
            new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 0,
                MaxLength = 200,
            },
            "Code, legalName or tradeName contains search; Unicode trim + NFC, Turkish CI/AS.");
        AddQueryParameter(
            operation,
            "kind",
            StringChoiceSchema("^(LegalEntity|NaturalPerson)$"),
            "Optional Party kind.");
        AddQueryParameter(
            operation,
            "role",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = StringChoiceSchema("^(Customer|Supplier|Tolling|Service)$"),
            },
            "Repeatable Party role filter; one visible account must satisfy roleMatch.",
            explode: true);
        AddQueryParameter(
            operation,
            "roleMatch",
            StringChoiceSchema("^(any|all)$"),
            "Role matching mode; default any.");
        AddQueryParameter(
            operation,
            "partyState",
            StringChoiceSchema("^(active|inactive|all)$"),
            "Global Party state; default active.");
        AddQueryParameter(
            operation,
            "accountState",
            StringChoiceSchema("^(active|inactive|all)$"),
            "Visible company-account state; default all.");
        AddQueryParameter(
            operation,
            "sort",
            StringChoiceSchema("^(code|legalName|updatedAt)$"),
            "Keyset sort; default code.");
        AddQueryParameter(
            operation,
            "direction",
            StringChoiceSchema("^(asc|desc)$"),
            "Sort direction; default asc.");
        AddQueryParameter(
            operation,
            "pageSize",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Minimum = "1",
                Maximum = "200",
            },
            "Page size; default 50.");
        AddQueryParameter(
            operation,
            "cursor",
            new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 1,
            },
            "Opaque authenticated cursor bound to actor, session, scope and normalized query.");
        AddQueryParameter(
            operation,
            "includeCount",
            new OpenApiSchema { Type = JsonSchemaType.Boolean },
            "Return exact filtered authorized distinct Party count; default false.");

        var querySchema = GetConcreteParameterSchema(
            operation,
            ParameterLocation.Query,
            "q");
        SetSchemaExtension(
            querySchema,
            CanonicalizationExtensionName,
            JsonValue.Create("unicode-trim+nfc")!);
    }

    private static void AddCounterpartyLinkCandidateParameters(OpenApiOperation operation)
    {
        AddQueryParameter(
            operation,
            "q",
            new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 0,
                MaxLength = 200,
            },
            "Code, legalName or tradeName contains search; Unicode trim + NFC, Turkish CI/AS.");
        AddQueryParameter(
            operation,
            "pageSize",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Minimum = "1",
                Maximum = "100",
            },
            "Page size; default 25.");
        AddQueryParameter(
            operation,
            "cursor",
            new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 1,
            },
            "Opaque authenticated cursor bound to actor, session, target company, permissions and normalized query.");

        var querySchema = GetConcreteParameterSchema(
            operation,
            ParameterLocation.Query,
            "q");
        SetSchemaExtension(
            querySchema,
            CanonicalizationExtensionName,
            JsonValue.Create("unicode-trim+nfc")!);
    }

    private static void AddInventoryParentRollCandidateParameters(OpenApiOperation operation)
    {
        AddQueryParameter(
            operation,
            "q",
            new OpenApiSchema { Type = JsonSchemaType.String },
            "Optional label-code search; trimmed by the Inventory owner, at most 64 characters.");
        SetCanonicalStringParameter(operation, ParameterLocation.Query, "q", maximumLength: 64);
        AddQueryParameter(
            operation,
            "limit",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = "1",
                Maximum = "100",
            },
            "Maximum candidate rows; default 25, range 1..100.");
    }

    private static async Task AddInventoryStockPositionParametersAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        AddInventoryParentRollCandidateParameters(operation);
        AddQueryParameter(operation, "cursor",
            new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 4096 },
            "Opaque authenticated continuation bound to actor, company and normalized filters.");
        AddQueryParameter(operation, "productId",
            new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            "Optional exact Product identifier.");
        AddQueryParameter(operation, "locationId",
            new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            "Optional exact Organization location identifier.");
        AddQueryParameter(operation, "classification",
            new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = ["Standard", "SecondQuality", "Scrap"],
            },
            "Optional exact Inventory classification.");
        AddQueryParameter(operation, "ownerKind",
            new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = ["Company", "Counterparty"],
            },
            "Optional owner kind; ownerId is required with it.");
        AddQueryParameter(operation, "ownerId",
            new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            "Optional exact owner identifier; ownerKind is required with it.");
        AddHeaderParameter(operation,
            GoymenERP.Modules.Inventory.Application.InventoryStockPositionApiV1.ProductDisplayContractHeader,
            required: false,
            "Optional exact compatibility selection. Omit for the previously published stock-position response; "
            + "send 1.0 for Product identity and nominal technical display fields.",
            minimumLength: 3,
            maximumLength: 3,
            pattern: "^1\\.0$");

        if (operation.Responses is not null
            && operation.Responses.TryGetValue("200", out var response)
            && response.Content is not null
            && response.Content.TryGetValue("application/json", out var content))
        {
            var document = context.Document ?? throw new InvalidOperationException(
                "Inventory stock-position OpenAPI document was not available.");
            document.Components ??= new OpenApiComponents();
            document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            var stockDisplayTypes = new (string Name, Type Type)[]
            {
                (nameof(GoymenERP.Modules.Product.Application.Contracts.ProductManufacturingFamilyRevisionV1),
                    typeof(GoymenERP.Modules.Product.Application.Contracts.ProductManufacturingFamilyRevisionV1)),
                (nameof(GoymenERP.Modules.Product.Application.Contracts.ProductManufacturingTemplateRevisionV1),
                    typeof(GoymenERP.Modules.Product.Application.Contracts.ProductManufacturingTemplateRevisionV1)),
                (nameof(GoymenERP.Modules.Product.Application.Contracts.ProductFieldError),
                    typeof(GoymenERP.Modules.Product.Application.Contracts.ProductFieldError)),
                (nameof(GoymenERP.Modules.Product.Application.Contracts.ProductStockDisplayIdentityV1),
                    typeof(GoymenERP.Modules.Product.Application.Contracts.ProductStockDisplayIdentityV1)),
                (nameof(GoymenERP.Modules.Product.Application.Contracts.ProductStockNominalTechnicalV1),
                    typeof(GoymenERP.Modules.Product.Application.Contracts.ProductStockNominalTechnicalV1)),
                (nameof(GoymenERP.Modules.Product.Application.Contracts.ProductStockDisplayResultV1),
                    typeof(GoymenERP.Modules.Product.Application.Contracts.ProductStockDisplayResultV1)),
                (nameof(GoymenERP.Api.Endpoints.InventoryStockPositionDisplayV1),
                    typeof(GoymenERP.Api.Endpoints.InventoryStockPositionDisplayV1)),
                (nameof(GoymenERP.Api.Endpoints.InventoryStockPositionPageV1),
                    typeof(GoymenERP.Api.Endpoints.InventoryStockPositionPageV1)),
            };
            foreach (var (name, type) in stockDisplayTypes)
            {
                document.Components.Schemas[name] = await context.GetOrCreateSchemaAsync(
                    type, cancellationToken: cancellationToken);
            }

            OpenApiSchemaReference Reference(string name) => new(name, document, externalResource: null);
            var pageSchema = (OpenApiSchema)document.Components.Schemas[
                nameof(GoymenERP.Api.Endpoints.InventoryStockPositionPageV1)];
            pageSchema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            pageSchema.Properties["items"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = Reference(nameof(GoymenERP.Api.Endpoints.InventoryStockPositionDisplayV1)),
            };
            var displaySchema = (OpenApiSchema)document.Components.Schemas[
                nameof(GoymenERP.Api.Endpoints.InventoryStockPositionDisplayV1)];
            displaySchema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            displaySchema.Properties["lot"] = Reference(nameof(GoymenERP.Modules.Inventory.Application.InventoryLotV1));
            displaySchema.Properties["productDisplay"] = Reference(
                nameof(GoymenERP.Modules.Product.Application.Contracts.ProductStockDisplayResultV1));
            var productSchema = (OpenApiSchema)document.Components.Schemas[
                nameof(GoymenERP.Modules.Product.Application.Contracts.ProductStockDisplayResultV1)];
            productSchema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            productSchema.Properties["identity"] = new OpenApiSchema
            {
                OneOf =
                [
                    Reference(nameof(GoymenERP.Modules.Product.Application.Contracts.ProductStockDisplayIdentityV1)),
                    new OpenApiSchema { Type = JsonSchemaType.Null },
                ],
            };
            productSchema.Properties["technical"] = new OpenApiSchema
            {
                OneOf =
                [
                    Reference(nameof(GoymenERP.Modules.Product.Application.Contracts.ProductStockNominalTechnicalV1)),
                    new OpenApiSchema { Type = JsonSchemaType.Null },
                ],
            };
            productSchema.Properties["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = Reference(nameof(GoymenERP.Modules.Product.Application.Contracts.ProductFieldError)),
            };
            var technicalSchema = (OpenApiSchema)document.Components.Schemas[
                nameof(GoymenERP.Modules.Product.Application.Contracts.ProductStockNominalTechnicalV1)];
            technicalSchema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            technicalSchema.Properties["family"] = Reference(
                nameof(GoymenERP.Modules.Product.Application.Contracts.ProductManufacturingFamilyRevisionV1));
            technicalSchema.Properties["template"] = Reference(
                nameof(GoymenERP.Modules.Product.Application.Contracts.ProductManufacturingTemplateRevisionV1));

            var enriched = new OpenApiSchemaReference(
                nameof(GoymenERP.Api.Endpoints.InventoryStockPositionPageV1), document, externalResource: null);
            var legacy = await context.GetOrCreateSchemaAsync(
                typeof(GoymenERP.Api.Endpoints.InventoryStockPositionLegacyPageV1),
                cancellationToken: cancellationToken);
            content.Schema = new OpenApiSchema
            {
                AnyOf = [legacy, enriched],
            };
        }
    }

    private static void AddInventoryMovementTimelineParameters(OpenApiOperation operation)
    {
        AddQueryParameter(
            operation,
            "limit",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = "1",
                Maximum = "100",
            },
            "Maximum movement rows; default 50, range 1..100.");
        AddQueryParameter(
            operation,
            "cursor",
            new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 4096 },
            "Opaque authenticated continuation bound to actor, company and limit.");
    }

    private static void AddInventoryPhysicalAllocationReadParameters(OpenApiOperation operation)
    {
        AddQueryParameter(
            operation,
            "salesOrderId",
            new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            "Required Sales order identifier.",
            required: true);
        AddQueryParameter(
            operation,
            "salesOrderLineId",
            new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            "Required Sales order line identifier.",
            required: true);
        AddQueryParameter(
            operation,
            "limit",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = "1",
                Maximum = "100",
            },
            "Maximum allocation rows; default 100, range 1..100.");
    }

    private static void AddInventoryProductCandidateParameters(OpenApiOperation operation)
    {
        AddQueryParameter(operation, "q", new OpenApiSchema { Type = JsonSchemaType.String },
            "Optional Product code or name search.");
        AddQueryParameter(operation, "effectiveOn",
            new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
            "Required effective date in yyyy-MM-dd form.", required: true);
        AddBoundedCandidateLimit(operation);
    }

    private static void AddInventoryLocationCandidateParameters(OpenApiOperation operation)
    {
        AddQueryParameter(operation, "q", new OpenApiSchema { Type = JsonSchemaType.String },
            "Optional active location, site code or name search.");
        AddBoundedCandidateLimit(operation);
    }

    private static void AddBoundedCandidateLimit(OpenApiOperation operation) =>
        AddQueryParameter(operation, "limit",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = "1",
                Maximum = "100",
            },
            "Maximum candidate rows; default 25, range 1..100.");

    private static OpenApiSchema StringChoiceSchema(string pattern)
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Pattern = pattern,
        };
    }

    private static void AddQueryParameter(
        OpenApiOperation operation,
        string name,
        OpenApiSchema schema,
        string description,
        bool explode = false,
        bool required = false)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter =>
                parameter.In == ParameterLocation.Query
                && string.Equals(parameter.Name, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"OpenAPI query parameter '{name}' is already declared.");
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Query,
            Required = required,
            Description = description,
            Schema = schema,
            Style = ParameterStyle.Form,
            Explode = explode,
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

    private static void SetStringLengthParameter(OpenApiOperation operation,
        ParameterLocation location, string parameterName, int? minimum, int? maximum)
    {
        var schema = GetConcreteParameterSchema(operation, location, parameterName);
        schema.MinLength = minimum;
        schema.MaxLength = maximum;
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

    private static void SetPositiveIntegerStringProperty(
        OpenApiSchema schema,
        string propertyName)
    {
        if (schema.Properties?.TryGetValue(propertyName, out var property) != true
            || property is not OpenApiSchema concreteProperty)
        {
            throw new InvalidOperationException(
                $"OpenAPI property '{propertyName}' is not a concrete schema.");
        }

        concreteProperty.Type = JsonSchemaType.String;
        concreteProperty.Format = null;
        concreteProperty.Minimum = null;
        concreteProperty.Maximum = null;
        concreteProperty.MinLength = 1;
        concreteProperty.MaxLength = 19;
        concreteProperty.Pattern = PositiveIntegerStringPattern;
    }

    private static OpenApiSchema ConcreteProperty(OpenApiSchema schema, string propertyName) =>
        schema.Properties?.TryGetValue(propertyName, out var property) == true && property is OpenApiSchema concreteProperty
            ? concreteProperty
            : throw new InvalidOperationException($"OpenAPI property '{propertyName}' is not a concrete schema.");

    private static void SetArrayBounds(OpenApiSchema schema, string propertyName, int minimumItems, int maximumItems, bool uniqueItems)
    {
        var property = ConcreteProperty(schema, propertyName);
        property.MinItems = minimumItems;
        property.MaxItems = maximumItems;
        property.UniqueItems = uniqueItems;
    }

    private static void SetStringBounds(OpenApiSchema schema, string propertyName, int minimumLength, int maximumLength, string? pattern)
    {
        var property = ConcreteProperty(schema, propertyName);
        property.MinLength = minimumLength;
        property.MaxLength = maximumLength;
        property.Pattern = pattern;
    }

    private static void SetExactSingleItemArrayProperty(
        OpenApiSchema schema,
        string propertyName)
    {
        if (schema.Properties?.TryGetValue(propertyName, out var property) != true
            || property is not OpenApiSchema concreteProperty)
        {
            throw new InvalidOperationException(
                $"OpenAPI property '{propertyName}' is not a concrete schema.");
        }

        concreteProperty.MinItems = 1;
        concreteProperty.MaxItems = 1;
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

    private sealed record OptionalCompanyContextMetadata(bool UsesPlatformInvalidCode);

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

    private sealed record CodeLessProblemDetailsResponseMetadata(int StatusCode);

    private sealed record NonEmptyGuidPathParameterMetadata(string ParameterName);
}

public sealed class ErpProblemDetailsContract
{
    public required string Type { get; init; }

    public required string Title { get; init; }

    public required int Status { get; init; }

    public required string Detail { get; init; }

    public required string Code { get; init; }

    public string? CorrelationId { get; init; }

    public ErpVersionConflictContract? Conflict { get; init; }
}

public sealed class ErpCodeLessProblemDetailsContract
{
    public required string Type { get; init; }

    public required string Title { get; init; }

    public required int Status { get; init; }

    public required string Detail { get; init; }

    public ErpVersionConflictContract? Conflict { get; init; }
}

public sealed class ErpVersionConflictContract
{
    public required long ExpectedVersion { get; init; }

    public required long CurrentVersion { get; init; }
}
