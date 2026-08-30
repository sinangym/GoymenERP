using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.Extensions.DependencyInjection;

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
            Guid? companyId,
            ICounterpartyReadService service,
            CancellationToken cancellationToken) =>
        {
            var parties = await service.GetAuthorizedPartiesAsync(
                companyId,
                cancellationToken);
            return Results.Ok(parties);
        });

        group.MapGet("/{partyId:guid}", async (
            Guid partyId,
            Guid? companyId,
            ICounterpartyReadService service,
            CancellationToken cancellationToken) =>
        {
            var party = await service.GetAuthorizedPartyAsync(
                partyId,
                companyId,
                cancellationToken);
            return party is null
                ? NotFound()
                : Results.Ok(party);
        });

        group.MapPost("/", async (
                CreateCounterparty command,
                ICounterpartyCommandService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (!IsValidCreate(command, out var validationDetail))
            {
                return Results.Problem(
                    title: "Cari bilgisi geçersiz.",
                    detail: validationDetail,
                    statusCode: StatusCodes.Status400BadRequest);
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
            ApplyReplayHeaders(httpContext, execution);
            return ToHttpResult(execution.Response);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesCreate);

        group.MapPost("/{partyId:guid}/company-account", async (
                Guid partyId,
                CreateCompanyPartyAccount command,
                ICounterpartyCompanyAccountLinkService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (partyId == Guid.Empty || !IsValidRoles(command.Roles))
            {
                return Results.Problem(
                    title: "Cari şirket ilişkisi geçersiz.",
                    detail: "Geçerli cari kimliği ve en az bir bilinen ticari rol zorunludur.",
                    statusCode: StatusCodes.Status400BadRequest);
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
            ApplyReplayHeaders(httpContext, execution);
            return ToHttpResult(execution.Response);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyAccountsCreate)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead);

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
                return Results.Problem(
                    title: "Cari durum değişikliği geçersiz.",
                    detail: "Geçerli cari kimliği, sıfırdan büyük beklenen sürüm ve 1-1000 karakter gerekçe zorunludur.",
                    statusCode: StatusCodes.Status400BadRequest);
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
            ApplyReplayHeaders(httpContext, execution);
            return ToHttpResult(execution.Response);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyAccountsStatusUpdate);

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
            return Results.Problem(
                title: "Geçersiz şirket seçimi.",
                detail: "X-Company-Id geçerli ve boş olmayan bir şirket kimliği olmalıdır.",
                statusCode: StatusCodes.Status400BadRequest);
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
            validationDetail = "Cari en az bir bilinen ticari rol taşımalıdır.";
            return false;
        }

        validationDetail = string.Empty;
        return true;
    }

    private static bool IsValidRoles(PartyRole roles)
    {
        const PartyRole knownRoles =
            PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service;
        return roles != PartyRole.None && (roles & ~knownRoles) == 0;
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
        if (response.StatusCode == StatusCodes.Status201Created
            && response.Resource is not null)
        {
            return Results.Created(
                $"/api/v1/counterparties/{response.Resource.Id}",
                response.Resource);
        }

        if (response.StatusCode == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        return Results.Problem(
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

    private static IResult InvalidCommandMetadata()
    {
        return Results.Problem(
            title: "Command bilgisi eksik.",
            detail: "İşlem için tekil, geçerli Idempotency-Key ve correlation bilgisi gerekir.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult NotFound()
    {
        return Results.Problem(
            title: "Kayıt bulunamadı.",
            detail: "İstenen cari kayıt bulunamadı veya yetkili şirket kapsamınızda kullanılamıyor.",
            statusCode: StatusCodes.Status404NotFound);
    }
}
