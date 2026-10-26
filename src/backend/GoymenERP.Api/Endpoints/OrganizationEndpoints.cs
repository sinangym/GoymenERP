using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.OrganizationRuntime;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class OrganizationEndpoints
{
    private const string IdempotencyHeaderName = "Idempotency-Key";

    private const int MaximumIdempotencyKeyLength = 128;

    public static IEndpointRouteBuilder MapOrganizationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/organization")
            .RequireAuthorization()
            .WithTags("Organization");

        group.MapGet("/companies", async (
            HttpContext httpContext,
            IOrganizationService service,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "private, no-store";
            var companies = await service.GetAuthorizedCompaniesAsync(cancellationToken);
            return Results.Ok(companies);
        })
            .WithName("GetAuthorizedCompaniesV1")
            .Produces<IReadOnlyList<CompanySummary>>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/context", (ICompanyContext context) =>
                Results.Ok(new CompanyContextResponse(context.CompanyId)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationContextRead)
            .WithName("GetOrganizationContextV1")
            .Produces<CompanyContextResponse>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/sites", async (
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSitesAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationSitesRead)
            .WithName("GetOrganizationSitesV1")
            .Produces<IReadOnlyList<SiteSummary>>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/locations", async (
                int? page,
                int? pageSize,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetLocationsAsync(
                page ?? 1,
                pageSize ?? 25,
                cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationLocationsRead)
            .WithName("GetOrganizationLocationsV1")
            .Produces<PageResult<LocationSummary>>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/locations/{id:guid}", async (
                Guid id,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
        {
            var location = await service.GetLocationAsync(id, cancellationToken);
            return location is null
                ? NotFound()
                : Results.Ok(location);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationLocationsRead)
            .WithName("GetOrganizationLocationV1")
            .Produces<LocationSummary>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/locations/{id:guid}/operator", async (
                Guid id,
                HttpContext httpContext,
                IOrganizationLocationOperatorReadHostV1 host,
                CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response.Headers);
            var result = await host.ReadDisplayAsync(id, cancellationToken);
            return result.Outcome switch
            {
                OrganizationLocationOperatorReadOutcomeV1.Success when result.Resource is not null =>
                    Results.Ok(result.Resource),
                OrganizationLocationOperatorReadOutcomeV1.NotFound => NotFound(),
                OrganizationLocationOperatorReadOutcomeV1.Unavailable => LocationOperatorUnavailable(),
                _ => LocationOperatorUnavailable(),
            };
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationLocationsRead)
            .WithName("GetOrganizationLocationOperatorV1")
            .Produces<OrganizationLocationOperatorContextV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(OrganizationProblemCodesV1.LocationOperatorDependencyUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/locations/{id:guid}/operator-candidates", async (
                Guid id,
                string? q,
                string? pageSize,
                string? cursor,
                HttpContext httpContext,
                IOrganizationLocationOperatorReadHostV1 host,
                IActorSessionBindingResolver sessionBindingResolver,
                CancellationToken cancellationToken) =>
        {
            _ = (q, pageSize, cursor);
            SetPrivateNoStore(httpContext.Response.Headers);
            var parse = OrganizationLocationOperatorCandidateQueryParserV1.Parse(
                httpContext.Request.Query,
                out var query,
                out var error);
            if (parse == OrganizationLocationOperatorCandidateQueryParseOutcomeV1.ValidationFailed)
            {
                return LocationOperatorBadRequest(
                    OrganizationLocationOperatorReadCodesV1.ValidationFailed,
                    error);
            }

            if (parse == OrganizationLocationOperatorCandidateQueryParseOutcomeV1.InvalidCursor)
            {
                return LocationOperatorBadRequest(
                    OrganizationLocationOperatorReadCodesV1.InvalidCursor,
                    error);
            }

            var sessionBinding = await sessionBindingResolver.ResolveAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(sessionBinding))
            {
                return LocationOperatorUnavailable();
            }

            var result = await host.SearchCandidatesAsync(
                id,
                query,
                sessionBinding,
                cancellationToken);
            return result.Outcome switch
            {
                OrganizationLocationOperatorReadOutcomeV1.Success when result.Resource is not null =>
                    Results.Ok(result.Resource),
                OrganizationLocationOperatorReadOutcomeV1.NotFound => NotFound(),
                OrganizationLocationOperatorReadOutcomeV1.InvalidCursor => LocationOperatorBadRequest(
                    OrganizationLocationOperatorReadCodesV1.InvalidCursor,
                    "Cursor bu lokasyon, arama ve oturum bağlamında geçerli değildir."),
                OrganizationLocationOperatorReadOutcomeV1.Unavailable => LocationOperatorUnavailable(),
                _ => LocationOperatorUnavailable(),
            };
        }).RequireCompanyContext()
            .RequireAuthorization(OrganizationPermissionManifestV1.LocationsWrite)
            .WithName("GetOrganizationLocationOperatorCandidatesV1")
            .Produces<OrganizationLocationOperatorCandidatePageV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(
                OrganizationLocationOperatorReadCodesV1.ValidationFailed,
                OrganizationLocationOperatorReadCodesV1.InvalidCursor)
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable(OrganizationProblemCodesV1.LocationOperatorDependencyUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/locations/{id:guid}/active-state", async (
                Guid id,
                SetLocationActiveState command,
                IOrganizationService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (command.ExpectedVersion <= 0)
            {
                return Results.Problem(
                    title: "Beklenen sürüm gerekli.",
                    detail: "Durum değişikliği için sıfırdan büyük beklenen sürüm zorunludur.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (string.IsNullOrWhiteSpace(command.Reason)
                || command.Reason.Trim().Length > 1000)
            {
                return Results.Problem(
                    title: "Gerekçe gerekli.",
                    detail: "Kritik değişiklik için 1-1000 karakter arasında bir gerekçe girin.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return Results.Problem(
                    title: "Command bilgisi eksik.",
                    detail: "İşlem için geçerli Idempotency-Key ve correlation bilgisi gerekir.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var execution = await service.SetLocationActiveStateAsync(
                id,
                command,
                metadata,
                cancellationToken);

            if (execution.IsReplay)
            {
                httpContext.Response.Headers["Idempotency-Replayed"] = "true";
                httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] =
                    execution.OriginalCorrelationId;
            }

            return ToHttpResult(execution.Response);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationLocationsStatusUpdate)
            .WithIdempotencyContract(
                StatusCodes.Status204NoContent,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .WithName("SetOrganizationLocationActiveStateV1")
            .Accepts<SetLocationActiveState>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpCodeLessConflict()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/work-centers", async (
                IOrganizationService service,
                CancellationToken cancellationToken) =>
            Results.Ok(await service.GetWorkCentersAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationWorkCentersRead)
            .WithName("GetOrganizationWorkCentersV1")
            .Produces<IReadOnlyList<WorkCenterSummary>>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        // ORG-04 (W1-ORG / AV-01, Business #66 5702892195): one work center's read-only card, the location card's
        // pattern. The work-centre read's own grant and scope; another company's, an out-of-scope and an unknown id
        // are the same code-less 404, never a 403 that would confirm the row exists. A read: nothing is written.
        group.MapGet("/work-centers/{id:guid}", async (
                Guid id,
                IOrganizationService service,
                CancellationToken cancellationToken) =>
        {
            var workCenter = await service.GetWorkCenterAsync(id, cancellationToken);
            return workCenter is null
                ? NotFound()
                : Results.Ok(workCenter);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationWorkCentersRead)
            .WithName("GetOrganizationWorkCenterV1")
            .Produces<WorkCenterDetail>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpCodeLessNotFound()
            .ProducesErpAuthenticatedProblems();

        MapMasterDataCommands(group);

        return endpoints;
    }

    /// <summary>
    /// The site / location / work-centre master edit (first-release outcome "create and edit sites, locations and work
    /// centres"). The active-state command's protocol: Idempotency-Key required and replayed, <c>expectedVersion</c> on
    /// every edit (a stale one is a code-less 409 carrying <c>conflict</c>), another company's / out-of-scope / unknown
    /// records one code-less 404. A create answers 201 with the new record's id and first version; an edit answers 204
    /// and the fresh state is re-read. A code already used in the company is 422 <c>organization_code_in_use</c>.
    /// </summary>
    private static void MapMasterDataCommands(RouteGroupBuilder group)
    {
        group.MapPost("/sites", async (
                CreateSite command,
                IOrganizationMasterDataService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (!OrganizationMasterDataRules.IsSiteType(command.Type))
            {
                return InvalidMasterData("Tesis türü tanımlı tesis türlerinden biri olmalıdır.");
            }

            return await ExecuteMasterDataCommandAsync(
                command.Code,
                command.Name,
                command.Reason,
                expectedVersion: null,
                httpContext,
                currentUser,
                companyContext,
                correlationContext,
                metadata => service.CreateSiteAsync(command, metadata, cancellationToken),
                _ => null);
        }).RequireCompanyContext()
            .RequireAuthorization(OrganizationPermissionManifestV1.SitesWrite)
            .WithMasterDataCreateContract<CreateSite>("CreateOrganizationSiteV1");

        group.MapPut("/sites/{id:guid}", async (
                Guid id,
                UpdateSite command,
                IOrganizationMasterDataService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await ExecuteMasterDataCommandAsync(
                command.Code,
                command.Name,
                command.Reason,
                command.ExpectedVersion,
                httpContext,
                currentUser,
                companyContext,
                correlationContext,
                metadata => service.UpdateSiteAsync(id, command, metadata, cancellationToken),
                _ => null))
            .RequireCompanyContext()
            .RequireAuthorization(OrganizationPermissionManifestV1.SitesWrite)
            .WithMasterDataUpdateContract<UpdateSite>("UpdateOrganizationSiteV1");

        group.MapPost("/locations", async (
                CreateLocation command,
                IOrganizationMasterDataService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (command.SiteId == Guid.Empty)
            {
                return InvalidMasterData("Lokasyonun bağlanacağı tesis seçilmelidir.");
            }

            return await ExecuteMasterDataCommandAsync(
                command.Code,
                command.Name,
                command.Reason,
                expectedVersion: null,
                httpContext,
                currentUser,
                companyContext,
                correlationContext,
                metadata => service.CreateLocationAsync(command, metadata, cancellationToken),
                created => $"/api/v1/organization/locations/{created.Id}");
        }).RequireCompanyContext()
            .RequireAuthorization(OrganizationPermissionManifestV1.LocationsWrite)
            .WithMasterDataCreateContract<CreateLocation>("CreateOrganizationLocationV1");

        group.MapPut("/locations/{id:guid}", async (
                Guid id,
                UpdateLocation command,
                IOrganizationMasterDataService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await ExecuteMasterDataCommandAsync(
                command.Code,
                command.Name,
                command.Reason,
                command.ExpectedVersion,
                httpContext,
                currentUser,
                companyContext,
                correlationContext,
                metadata => service.UpdateLocationAsync(id, command, metadata, cancellationToken),
                _ => null))
            .RequireCompanyContext()
            .RequireAuthorization(OrganizationPermissionManifestV1.LocationsWrite)
            .WithMasterDataUpdateContract<UpdateLocation>("UpdateOrganizationLocationV1");

        group.MapPut("/locations/{id:guid}/operator", async (
                Guid id,
                SetLocationOperator command,
                IOrganizationMasterDataService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (command.OperatorPartyId == Guid.Empty)
            {
                return InvalidMasterData("Lokasyon operatörü olarak kullanılacak cari seçilmelidir.");
            }

            if (command.ExpectedVersion <= 0)
            {
                return Results.Problem(
                    title: "Beklenen sürüm gerekli.",
                    detail: "Operatör değişikliği için sıfırdan büyük beklenen sürüm zorunludur.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!OrganizationMasterDataRules.IsValidReason(command.Reason))
            {
                return Results.Problem(
                    title: "Gerekçe gerekli.",
                    detail: "Kritik değişiklik için 1-1000 karakter arasında bir gerekçe girin.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return Results.Problem(
                    title: "Command bilgisi eksik.",
                    detail: "İşlem için geçerli Idempotency-Key ve correlation bilgisi gerekir.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var execution = await service.SetLocationOperatorAsync(
                id,
                command,
                metadata,
                cancellationToken);
            if (execution.IsReplay)
            {
                httpContext.Response.Headers["Idempotency-Replayed"] = "true";
                httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] =
                    execution.OriginalCorrelationId;
            }

            return execution.Response.Result switch
            {
                CommandResult.OperatorIneligible => Results.Problem(
                    title: execution.Response.Title,
                    detail: execution.Response.Detail,
                    statusCode: execution.Response.StatusCode,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = OrganizationProblemCodesV1.LocationOperatorIneligible,
                    }),
                CommandResult.DependencyUnavailable => Results.Problem(
                    title: execution.Response.Title,
                    detail: execution.Response.Detail,
                    statusCode: execution.Response.StatusCode,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = OrganizationProblemCodesV1.LocationOperatorDependencyUnavailable,
                    }),
                _ => ToHttpResult(execution.Response),
            };
        }).RequireCompanyContext()
            .RequireAuthorization(OrganizationPermissionManifestV1.LocationsWrite)
            .WithIdempotencyContract(
                StatusCodes.Status204NoContent,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .WithName("SetOrganizationLocationOperatorV1")
            .Accepts<SetLocationOperator>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpCodeLessConflict()
            .ProducesErpUnprocessableEntity(OrganizationProblemCodesV1.LocationOperatorIneligible)
            .ProducesErpServiceUnavailable(OrganizationProblemCodesV1.LocationOperatorDependencyUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/work-centers", async (
                CreateWorkCenter command,
                IOrganizationMasterDataService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (command.LocationId == Guid.Empty)
            {
                return InvalidMasterData("İş merkezinin bağlanacağı lokasyon seçilmelidir.");
            }

            if (!OrganizationMasterDataRules.IsWorkCenterKind(command.Kind))
            {
                return InvalidMasterData("İş merkezi türü tanımlı türlerden biri olmalıdır.");
            }

            return await ExecuteMasterDataCommandAsync(
                command.Code,
                command.Name,
                command.Reason,
                expectedVersion: null,
                httpContext,
                currentUser,
                companyContext,
                correlationContext,
                metadata => service.CreateWorkCenterAsync(command, metadata, cancellationToken),
                created => $"/api/v1/organization/work-centers/{created.Id}");
        }).RequireCompanyContext()
            .RequireAuthorization(OrganizationPermissionManifestV1.WorkCentersWrite)
            .WithMasterDataCreateContract<CreateWorkCenter>("CreateOrganizationWorkCenterV1");

        group.MapPut("/work-centers/{id:guid}", async (
                Guid id,
                UpdateWorkCenter command,
                IOrganizationMasterDataService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await ExecuteMasterDataCommandAsync(
                command.Code,
                command.Name,
                command.Reason,
                command.ExpectedVersion,
                httpContext,
                currentUser,
                companyContext,
                correlationContext,
                metadata => service.UpdateWorkCenterAsync(id, command, metadata, cancellationToken),
                _ => null))
            .RequireCompanyContext()
            .RequireAuthorization(OrganizationPermissionManifestV1.WorkCentersWrite)
            .WithMasterDataUpdateContract<UpdateWorkCenter>("UpdateOrganizationWorkCenterV1");
    }

    private static RouteHandlerBuilder WithMasterDataCreateContract<TCommand>(
        this RouteHandlerBuilder builder,
        string operationName)
        where TCommand : notnull
    {
        return builder
            .WithIdempotencyContract(
                StatusCodes.Status201Created,
                StatusCodes.Status404NotFound,
                StatusCodes.Status422UnprocessableEntity)
            .WithName(operationName)
            .Accepts<TCommand>("application/json")
            .Produces<OrganizationCreatedRecord>(StatusCodes.Status201Created)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpCodeLessConflict()
            .ProducesErpUnprocessableEntity(OrganizationProblemCodesV1.CodeInUse)
            .ProducesErpAuthenticatedProblems();
    }

    private static RouteHandlerBuilder WithMasterDataUpdateContract<TCommand>(
        this RouteHandlerBuilder builder,
        string operationName)
        where TCommand : notnull
    {
        return builder
            .WithIdempotencyContract(
                StatusCodes.Status204NoContent,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .WithName(operationName)
            .Accepts<TCommand>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpCodeLessConflict()
            .ProducesErpUnprocessableEntity(OrganizationProblemCodesV1.CodeInUse)
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> ExecuteMasterDataCommandAsync(
        string? code,
        string? name,
        string? reason,
        long? expectedVersion,
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        Func<CommandMetadata, Task<CommandExecutionResult>> execute,
        Func<OrganizationCreatedRecord, string?> createdLocation)
    {
        if (expectedVersion is <= 0)
        {
            return Results.Problem(
                title: "Beklenen sürüm gerekli.",
                detail: "Düzenleme için sıfırdan büyük beklenen sürüm zorunludur.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!OrganizationMasterDataRules.IsValidCode(code))
        {
            return InvalidMasterData(
                $"Kod 1-{OrganizationMasterDataRules.CodeMaximumLength} karakter olmalı ve kontrol karakteri içermemelidir.");
        }

        if (!OrganizationMasterDataRules.IsValidName(name))
        {
            return InvalidMasterData(
                $"Ad 1-{OrganizationMasterDataRules.NameMaximumLength} karakter olmalı ve kontrol karakteri içermemelidir.");
        }

        if (!OrganizationMasterDataRules.IsValidReason(reason))
        {
            return Results.Problem(
                title: "Gerekçe gerekli.",
                detail: "Kritik değişiklik için 1-1000 karakter arasında bir gerekçe girin.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryReadCommandMetadata(
                httpContext,
                currentUser,
                companyContext,
                correlationContext,
                out var metadata))
        {
            return Results.Problem(
                title: "Command bilgisi eksik.",
                detail: "İşlem için geçerli Idempotency-Key ve correlation bilgisi gerekir.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var execution = await execute(metadata);
        if (execution.IsReplay)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] =
                execution.OriginalCorrelationId;
        }

        var response = execution.Response;
        if (response.Result == CommandResult.Created)
        {
            if (response.StatusCode != StatusCodes.Status201Created || response.Created is null)
            {
                throw new InvalidOperationException(
                    "Organizasyon oluşturma sonucu HTTP durumuyla eşleşmiyor.");
            }

            return Results.Created(createdLocation(response.Created), response.Created);
        }

        if (response.Result == CommandResult.CodeInUse)
        {
            return Results.Problem(
                title: response.Title,
                detail: response.Detail,
                statusCode: response.StatusCode,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = OrganizationProblemCodesV1.CodeInUse,
                });
        }

        return ToHttpResult(response);
    }

    private static IResult InvalidMasterData(string detail)
    {
        return Results.Problem(
            title: "Kayıt bilgisi geçersiz.",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult LocationOperatorBadRequest(string code, string detail) =>
        Results.Problem(
            title: "Lokasyon operatörü isteği geçersiz.",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static IResult LocationOperatorUnavailable() =>
        Results.Problem(
            title: "Lokasyon operatörü bilgisi kullanılamıyor.",
            detail: "Cari operatör kaynağı şu anda güvenli bir yanıt üretemiyor.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = OrganizationProblemCodesV1.LocationOperatorDependencyUnavailable,
            });

    private static void SetPrivateNoStore(IHeaderDictionary headers)
    {
        headers.CacheControl = "private, no-store";
        headers.Pragma = "no-cache";
    }

    private static IResult NotFound()
    {
        return Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen kayıt bulunamadı veya bu bağlamda kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
    }

    private static IResult ToHttpResult(CommandResponseSnapshot response)
    {
        return response.StatusCode == StatusCodes.Status204NoContent
            ? Results.NoContent()
            : Results.Problem(
                title: response.Title,
                detail: response.Detail,
                statusCode: response.StatusCode,
                extensions: response.Conflict is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["conflict"] = response.Conflict,
                    });
    }

    private static bool TryReadCommandMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out CommandMetadata metadata)
    {
        if (!TryReadIdempotencyKey(httpContext.Request.Headers, out var idempotencyKey)
            || !correlationContext.IsResolved
            || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            metadata = null!;
            return false;
        }

        metadata = new CommandMetadata(
            companyContext.CompanyId,
            currentUser.UserId,
            correlationContext.CorrelationId,
            correlationContext.CausationId,
            correlationContext.SourceChannel,
            correlationContext.ClientIpAddress,
            correlationContext.UserAgent,
            idempotencyKey);
        return true;
    }

    private static bool TryReadIdempotencyKey(
        IHeaderDictionary headers,
        out string idempotencyKey)
    {
        if (!headers.TryGetValue(IdempotencyHeaderName, out var values)
            || values.Count != 1
            || values[0] is not { } rawValue
            || !TryCanonicalizeIdempotencyKey(rawValue, out idempotencyKey))
        {
            idempotencyKey = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryCanonicalizeIdempotencyKey(
        string rawValue,
        out string canonicalValue)
    {
        canonicalValue = rawValue.Trim();
        return canonicalValue.Length is > 0 and <= MaximumIdempotencyKeyLength
            && !canonicalValue.Contains(',', StringComparison.Ordinal);
    }
}

public sealed record CompanyContextResponse(Guid CompanyId);
