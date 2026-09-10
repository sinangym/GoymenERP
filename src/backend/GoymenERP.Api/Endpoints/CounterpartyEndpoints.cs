using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Endpoints;

public static class CounterpartyEndpoints
{
    public static IEndpointRouteBuilder MapCounterpartyEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/counterparties")
            .RequireAuthorization()
            .AddEndpointFilter(ResolveOptionalCompanyContextAsync)
            .WithTags("Counterparty");

        group.MapGet("/", async (
            HttpContext httpContext,
            ICounterpartyReadService service,
            ICurrentUser currentUser,
            IActorSessionBindingResolver sessionBindingResolver,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response.Headers);
            var parseResult = CounterpartyPageQueryParser.Parse(
                httpContext.Request.Query,
                out var query,
                out var error);
            if (parseResult == CounterpartyPageQueryParseResult.InvalidCursor)
            {
                return InvalidCursor();
            }

            if (parseResult == CounterpartyPageQueryParseResult.ValidationFailed)
            {
                return ValidationProblem("Cari listeleme isteği geçersiz.", error);
            }

            var sessionBinding = await sessionBindingResolver.ResolveAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(currentUser.UserId)
                || string.IsNullOrWhiteSpace(sessionBinding))
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var execution = await service.GetAuthorizedPageAsync(
                new CounterpartyReadRequest(
                    query,
                    currentUser.UserId,
                    sessionBinding),
                cancellationToken);
            return execution.Result switch
            {
                CounterpartyPageResult.Succeeded => Results.Ok(execution.Page),
                CounterpartyPageResult.Forbidden => Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden),
                CounterpartyPageResult.InvalidCursor => InvalidCursor(),
                CounterpartyPageResult.ServiceUnavailable => Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException("Bilinmeyen Cari listeleme sonucu."),
            };
        })
            .UsesOptionalCompanyContext()
            .WithName("GetCounterpartiesV1")
            .Produces<CounterpartyPage>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(
                CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.InvalidCursor)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/company-account-link-candidates", async (
            HttpContext httpContext,
            ICounterpartyLinkCandidateReadService service,
            ICurrentUser currentUser,
            IActorSessionBindingResolver sessionBindingResolver,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response.Headers);
            var parseResult = CounterpartyLinkCandidateQueryParser.Parse(
                httpContext.Request.Query,
                out var query,
                out var error);
            if (parseResult == CounterpartyLinkCandidateQueryParseResult.InvalidCursor)
            {
                return InvalidCursor();
            }

            if (parseResult == CounterpartyLinkCandidateQueryParseResult.ValidationFailed)
            {
                return ValidationProblem("Cari bağlama adayı isteği geçersiz.", error);
            }

            var sessionBinding = await sessionBindingResolver.ResolveAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(currentUser.UserId)
                || string.IsNullOrWhiteSpace(sessionBinding))
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var execution = await service.GetAuthorizedPageAsync(
                new CounterpartyLinkCandidateRequest(
                    query,
                    currentUser.UserId,
                    sessionBinding),
                cancellationToken);
            return execution.Result switch
            {
                CounterpartyLinkCandidateResult.Succeeded => Results.Ok(execution.Page),
                CounterpartyLinkCandidateResult.Forbidden => Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden),
                CounterpartyLinkCandidateResult.InvalidCursor => InvalidCursor(),
                CounterpartyLinkCandidateResult.ServiceUnavailable => Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException(
                    "Bilinmeyen cari bağlama adayı listeleme sonucu."),
            };
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyAccountsCreate)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .WithName("GetCounterpartyCompanyAccountLinkCandidatesV1")
            .Produces<CounterpartyLinkCandidatePage>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(
                CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.InvalidCursor)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{partyId:guid}", async (
            Guid partyId,
            ICounterpartyReadService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response.Headers);
            if (httpContext.Request.Query.ContainsKey("companyId"))
            {
                return ValidationProblem(
                    "Cari detay isteği geçersiz.",
                    "Cari detay kapsamı sunucu tarafından belirlenir; companyId sorgu parametresi kabul edilmez.");
            }

            var party = await service.GetAuthorizedPartyAsync(
                partyId,
                cancellationToken);
            return party is null
                ? NotFound()
                : Results.Ok(party);
        })
            .UsesOptionalCompanyContext()
            .WithName("GetCounterpartyV1")
            .Produces<CounterpartyDetailView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/", async (
                CreateCounterparty command,
                ICounterpartyCommandService service,
                ICounterpartyMutationRelationshipVerifier relationshipVerifier,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (!IsValidCreate(command, out var validationDetail))
            {
                return ValidationProblem(
                    "Cari bilgisi geçersiz.",
                    validationDetail);
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return InvalidCommandMetadata();
            }

            var execution = await service.CreateAsync(
                command,
                metadata,
                cancellationToken);
            var result = await ToPartyCreateHttpResultAsync(
                execution.Response,
                companyContext.CompanyId,
                command,
                relationshipVerifier,
                cancellationToken);
            ApplyReplayHeaders(httpContext, execution);
            return result;
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesCreate)
            .WithIdempotencyContract(StatusCodes.Status201Created)
            .WithName("CreateCounterpartyV1")
            .Accepts<CreateCounterparty>("application/json")
            .Produces<CounterpartyMutationView>(StatusCodes.Status201Created)
            .ProducesErpBadRequest(
                CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(CounterpartyProblemCodesV1.IdempotencyConflict)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{partyId:guid}/company-account", async (
                Guid partyId,
                CreateCompanyPartyAccount command,
                ICounterpartyCompanyAccountLinkService service,
                ICounterpartyMutationRelationshipVerifier relationshipVerifier,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (partyId == Guid.Empty || !IsValidRoles(command.Roles))
            {
                return ValidationProblem(
                    "Cari şirket ilişkisi geçersiz.",
                    "Cari kimliği veya ticari sınıflandırma değeri geçersiz.");
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return InvalidCommandMetadata();
            }

            var execution = await service.CreateCompanyAccountAsync(
                partyId,
                command,
                metadata,
                cancellationToken);
            var result = await ToCompanyAccountCreateHttpResultAsync(
                execution.Response,
                companyContext.CompanyId,
                partyId,
                command,
                relationshipVerifier,
                cancellationToken);
            ApplyReplayHeaders(httpContext, execution);
            return result;
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyAccountsCreate)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithIdempotencyContract(
                StatusCodes.Status201Created,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .WithName("CreateCounterpartyCompanyAccountV1")
            .Accepts<CreateCompanyPartyAccount>("application/json")
            .Produces<CounterpartyMutationView>(StatusCodes.Status201Created)
            .ProducesErpBadRequest(
                CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(
                CounterpartyProblemCodesV1.AlreadyLinked,
                CounterpartyProblemCodesV1.IdempotencyConflict)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{partyId:guid}/company-account/active-state", async (
                Guid partyId,
                SetCompanyPartyAccountActiveState command,
                ICounterpartyCommandService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (partyId == Guid.Empty
                || command.ExpectedVersion <= 0
                || string.IsNullOrWhiteSpace(command.Reason)
                || command.Reason.Trim().Length > 1000)
            {
                return ValidationProblem(
                    "Cari durum değişikliği geçersiz.",
                    "Geçerli cari kimliği, sıfırdan büyük beklenen sürüm ve 1-1000 karakter gerekçe zorunludur.");
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return InvalidCommandMetadata();
            }

            var execution = await service.SetCompanyAccountActiveStateAsync(
                partyId,
                command,
                metadata,
                cancellationToken);
            var result = ToHttpResult(execution.Response);
            ApplyReplayHeaders(httpContext, execution);
            return result;
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyAccountsStatusUpdate)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithIdempotencyContract(
                StatusCodes.Status204NoContent,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .WithName("SetCounterpartyCompanyAccountActiveStateV1")
            .Accepts<SetCompanyPartyAccountActiveState>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(
                CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(
                CounterpartyProblemCodesV1.VersionConflict,
                CounterpartyProblemCodesV1.IdempotencyConflict)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async ValueTask<object?> ResolveOptionalCompanyContextAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next)
    {
        var httpContext = invocationContext.HttpContext;
        if (!httpContext.Request.Headers.TryGetValue(
                CompanyContextMiddleware.CompanyHeaderName,
                out var headerValue))
        {
            return await next(invocationContext);
        }

        if (!Guid.TryParse(headerValue.ToString(), out var companyId)
            || companyId == Guid.Empty)
        {
            return ValidationProblem(
                "Geçersiz şirket seçimi.",
                "X-Company-Id geçerli ve boş olmayan bir şirket kimliği olmalıdır.");
        }

        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.CanAccessCompany(companyId))
        {
            return Results.Problem(
                title: "Erişim reddedildi.",
                detail: "Seçili şirket için erişim yetkiniz bulunmuyor.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        httpContext.RequestServices
            .GetRequiredService<ICompanyContext>()
            .Set(companyId);

        return await next(invocationContext);
    }

    private static bool IsValidCreate(
        CreateCounterparty command,
        out string validationDetail)
    {
        if (!Enum.IsDefined(command.Kind))
        {
            validationDetail = "Geçerli bir taraf türü seçin.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(command.LegalName)
            || command.LegalName.Trim().Length > 250)
        {
            validationDetail = "Hukuki ad 1-250 karakter arasında olmalıdır.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(command.TradeName)
            && command.TradeName.Trim().Length > 200)
        {
            validationDetail = "Ticari ad 200 karakterden uzun olamaz.";
            return false;
        }

        if (!IsValidRoles(command.Roles))
        {
            validationDetail = "Ticari sınıflandırma değeri geçersiz.";
            return false;
        }

        validationDetail = string.Empty;
        return true;
    }

    private static bool IsValidRoles(PartyRole roles)
    {
        const PartyRole knownRoles =
            PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service;
        return (roles & ~knownRoles) == 0;
    }

    private static bool TryReadCommandMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out CounterpartyCommandMetadata metadata)
    {
        const int maximumMetadataLength = 128;
        var idempotencyHeaderValues = httpContext.Request.Headers["Idempotency-Key"];
        if (idempotencyHeaderValues.Count != 1)
        {
            metadata = null!;
            return false;
        }

        var idempotencyKey = idempotencyHeaderValues[0]?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > maximumMetadataLength
            || idempotencyKey.Contains(',', StringComparison.Ordinal)
            || !companyContext.IsResolved
            || !correlationContext.IsResolved
            || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            metadata = null!;
            return false;
        }

        metadata = new CounterpartyCommandMetadata(
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

    private static void ApplyReplayHeaders(
        HttpContext httpContext,
        CounterpartyCommandExecution execution)
    {
        if (!execution.IsReplay)
        {
            return;
        }

        httpContext.Response.Headers["Idempotency-Replayed"] = "true";
        httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] =
            execution.OriginalCorrelationId;
    }

    private static IResult ToHttpResult(CounterpartyCommandResponse response)
    {
        if (response.Result == CounterpartyCommandResult.Succeeded
            && response.StatusCode == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        var problemCode = CounterpartyProblemCodesV1.RequiredForProblem(response.Result);
        if (!HasValidTerminalProblemShape(response))
        {
            throw new InvalidOperationException(
                "Cari terminal sonucu HTTP durumuyla veya conflict zarfıyla eşleşmiyor.");
        }

        return CounterpartyProblem(
            response.StatusCode,
            problemCode,
            response.Title,
            response.Detail,
            response.Conflict);
    }

    private static async Task<IResult> ToPartyCreateHttpResultAsync(
        CounterpartyCommandResponse response,
        Guid activeCompanyId,
        CreateCounterparty submittedCommand,
        ICounterpartyMutationRelationshipVerifier relationshipVerifier,
        CancellationToken cancellationToken)
    {
        if (response.Result == CounterpartyCommandResult.Created
            && response.StatusCode == StatusCodes.Status201Created
            && response.Resource is not null
            && response.Title is null
            && response.Detail is null
            && response.Conflict is null)
        {
            var resource = CounterpartyMutationViewProjector.ProjectPartyCreate(
                response.Resource,
                activeCompanyId,
                submittedCommand);
            await RequireVerifiedMutationRelationshipAsync(
                resource,
                relationshipVerifier,
                cancellationToken);
            return Results.Created(
                $"/api/v1/counterparties/{resource.Id}",
                resource);
        }

        return ToHttpResult(response);
    }

    private static async Task<IResult> ToCompanyAccountCreateHttpResultAsync(
        CounterpartyCommandResponse response,
        Guid activeCompanyId,
        Guid expectedPartyId,
        CreateCompanyPartyAccount submittedCommand,
        ICounterpartyMutationRelationshipVerifier relationshipVerifier,
        CancellationToken cancellationToken)
    {
        if (response.Result == CounterpartyCommandResult.Created
            && response.StatusCode == StatusCodes.Status201Created
            && response.Resource is not null
            && response.Title is null
            && response.Detail is null
            && response.Conflict is null)
        {
            var resource = CounterpartyMutationViewProjector.ProjectCompanyAccountCreate(
                response.Resource,
                activeCompanyId,
                expectedPartyId,
                submittedCommand);
            await RequireVerifiedMutationRelationshipAsync(
                resource,
                relationshipVerifier,
                cancellationToken);
            return Results.Created(
                $"/api/v1/counterparties/{resource.Id}",
                resource);
        }

        return ToHttpResult(response);
    }

    private static async Task RequireVerifiedMutationRelationshipAsync(
        CounterpartyMutationView resource,
        ICounterpartyMutationRelationshipVerifier relationshipVerifier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relationshipVerifier);
        var account = resource.CompanyAccounts.Single();
        if (!await relationshipVerifier.IsAccountOwnedByPartyAsync(
                resource.Id,
                account.Id,
                account.CompanyId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Cari işlem sonucu kalıcı hesap ilişkisiyle eşleşmiyor.");
        }
    }

    private static bool HasValidTerminalProblemShape(
        CounterpartyCommandResponse response)
    {
        return response.Result switch
        {
            CounterpartyCommandResult.NotFound =>
                response.StatusCode == StatusCodes.Status404NotFound
                && response.Conflict is null,
            CounterpartyCommandResult.VersionConflict =>
                response.StatusCode == StatusCodes.Status409Conflict
                && response.Conflict is not null,
            CounterpartyCommandResult.IdempotencyConflict
                or CounterpartyCommandResult.AlreadyLinked =>
                response.StatusCode == StatusCodes.Status409Conflict
                && response.Conflict is null,
            _ => false,
        };
    }

    private static IResult InvalidCursor()
    {
        return CounterpartyProblem(
            StatusCodes.Status400BadRequest,
            CounterpartyProblemCodesV1.InvalidCursor,
            "Sayfalama bilgisi geçersiz.",
            "Sayfalama bilgisi süresi dolmuş, değiştirilmiş veya mevcut kapsamla eşleşmiyor.");
    }

    private static IResult ValidationProblem(string title, string detail)
    {
        return CounterpartyProblem(
            StatusCodes.Status400BadRequest,
            CounterpartyProblemCodesV1.ValidationFailed,
            title,
            detail);
    }

    private static IResult InvalidCommandMetadata()
    {
        return CounterpartyProblem(
            StatusCodes.Status400BadRequest,
            CounterpartyProblemCodesV1.CommandMetadataInvalid,
            "Command bilgisi eksik.",
            "İşlem için tekil, geçerli Idempotency-Key ve correlation bilgisi gerekir.");
    }

    private static IResult NotFound()
    {
        return CounterpartyProblem(
            StatusCodes.Status404NotFound,
            CounterpartyProblemCodesV1.NotFound,
            "Kayıt bulunamadı.",
            "İstenen cari kayıt bulunamadı veya yetkili şirket kapsamınızda kullanılamıyor.");
    }

    private static IResult CounterpartyProblem(
        int statusCode,
        string code,
        string? title,
        string? detail,
        CounterpartyVersionConflict? conflict = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
        };
        if (conflict is not null)
        {
            extensions["conflict"] = conflict;
        }

        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: extensions);
    }

    private static void SetPrivateNoStore(IHeaderDictionary headers)
    {
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.Pragma] = "no-cache";
    }
}
