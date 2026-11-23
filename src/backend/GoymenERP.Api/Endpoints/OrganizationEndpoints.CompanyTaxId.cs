using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// KIM-S23 (KIM-D28, DEC-ORG-005): the active company's VKN and its maker-checker change flow. The VKN is read only by
/// holders of one of the two tax-id permissions; a change is prepared by one person and decided by another, and the
/// company's VKN moves only on approval.
/// </summary>
public static partial class OrganizationEndpoints
{
    private const string CompanyTaxIdLocation = "/api/v1/organization/company/tax-id";

    private static void MapCompanyTaxId(RouteGroupBuilder group)
    {
        group.MapGet("/company/tax-id", async (
                HttpContext httpContext,
                IOrganizationCompanyTaxIdService service,
                CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "private, no-store";
            var view = await service.GetCompanyTaxIdAsync(cancellationToken);
            return view is null
                ? Results.Problem(statusCode: StatusCodes.Status403Forbidden)
                : Results.Ok(view);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationContextRead)
            .WithName("GetOrganizationCompanyTaxIdV1")
            .Produces<CompanyTaxIdView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/company/tax-id/change-requests", async (
                RequestCompanyTaxIdChange command,
                IOrganizationCompanyTaxIdService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (command.ExpectedVersion < 1)
            {
                return Results.Problem(
                    title: "Beklenen sürüm gerekli.",
                    detail: "VKN değişikliği için şirket kaydının sıfırdan büyük beklenen sürümü zorunludur.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!OrganizationCompanyTaxIdRules.IsValidVkn(command.ProposedVkn))
            {
                return InvalidMasterData("Önerilen VKN tam olarak 10 rakamdan oluşmalıdır.");
            }

            if (!OrganizationCompanyTaxIdRules.IsValidReason(command.Reason))
            {
                return Results.Problem(
                    title: "Gerekçe gerekli.",
                    detail: "VKN değişikliği için 1-1000 karakter arasında bir gerekçe girin.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return MissingCommandMetadata();
            }

            var execution = await service.RequestCompanyTaxIdChangeAsync(command, metadata, cancellationToken);
            WriteReplayHeaders(httpContext, execution);
            return ToCompanyTaxIdHttpResult(execution.Response);
        }).RequireCompanyContext()
            .RequireAuthorization(OrganizationCompanyTaxIdPermissionsV1.Request)
            .WithIdempotencyContract(
                StatusCodes.Status201Created,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .WithName("RequestOrganizationCompanyTaxIdChangeV1")
            .Accepts<RequestCompanyTaxIdChange>("application/json")
            .Produces<OrganizationCreatedRecord>(StatusCodes.Status201Created)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                OrganizationProblemCodesV1.CompanyTaxIdChangePending,
                OrganizationProblemCodesV1.CompanyTaxIdUnchanged,
                OrganizationProblemCodesV1.CompanyTaxIdVersionConflict,
                OrganizationProblemCodesV1.CompanyTaxIdIdempotencyConflict)
            .ProducesErpUnprocessableEntity(
                OrganizationProblemCodesV1.CompanyTaxIdInUse,
                OrganizationProblemCodesV1.CompanyTaxIdCheckDigitInvalid)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/company/tax-id/change-requests/{requestId:guid}/decision", async (
                Guid requestId,
                DecideCompanyTaxIdChange command,
                IOrganizationCompanyTaxIdService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (requestId == Guid.Empty)
            {
                return InvalidMasterData("Talep kimliği boş olamaz.");
            }

            if (command.ExpectedVersion < 1)
            {
                return Results.Problem(
                    title: "Beklenen sürüm gerekli.",
                    detail: "VKN kararı için şirket kaydının sıfırdan büyük beklenen sürümü zorunludur.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!OrganizationCompanyTaxIdRules.IsDecision(command.Decision))
            {
                return InvalidMasterData("Karar yalnız onay (approve) veya ret (reject) olabilir.");
            }

            if (!OrganizationCompanyTaxIdRules.IsValidNote(command.Note))
            {
                return InvalidMasterData("Karar notu en fazla 1000 karakter olabilir ve kontrol karakteri içeremez.");
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return MissingCommandMetadata();
            }

            var execution = await service.DecideCompanyTaxIdChangeAsync(
                requestId,
                command,
                metadata,
                cancellationToken);
            WriteReplayHeaders(httpContext, execution);
            return ToCompanyTaxIdHttpResult(execution.Response);
        }).RequireCompanyContext()
            .RequireAuthorization(OrganizationCompanyTaxIdPermissionsV1.Approve)
            .RequiresNonEmptyGuidPathParameter("requestId")
            .WithIdempotencyContract(
                StatusCodes.Status204NoContent,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .WithName("DecideOrganizationCompanyTaxIdChangeV1")
            .Accepts<DecideCompanyTaxIdChange>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                OrganizationProblemCodesV1.CompanyTaxIdChangeNotPending,
                OrganizationProblemCodesV1.CompanyTaxIdChangeStale,
                OrganizationProblemCodesV1.CompanyTaxIdSelfDecision,
                OrganizationProblemCodesV1.CompanyTaxIdVersionConflict,
                OrganizationProblemCodesV1.CompanyTaxIdIdempotencyConflict)
            .ProducesErpUnprocessableEntity(OrganizationProblemCodesV1.CompanyTaxIdInUse)
            .ProducesErpAuthenticatedProblems();

        MapCompanyTaxIdCorrections(group);
    }

    /// <summary>
    /// KIM-D41/KIM-D48: staged VKN corrections. A data fix stages the old and new value; the company's VKN moves only
    /// when a holder of the existing approve permission applies the row (no new permission name).
    /// </summary>
    private static void MapCompanyTaxIdCorrections(RouteGroupBuilder group)
    {
        group.MapGet("/company/tax-id/corrections", async (
                HttpContext httpContext,
                IOrganizationCompanyTaxIdService service,
                CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "private, no-store";
            var view = await service.GetCompanyTaxIdCorrectionsAsync(cancellationToken);
            return view is null
                ? Results.Problem(statusCode: StatusCodes.Status403Forbidden)
                : Results.Ok(view);
        }).RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.OrganizationContextRead)
            .WithName("GetOrganizationCompanyTaxIdCorrectionsV1")
            .Produces<CompanyTaxIdCorrectionsView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest()
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/company/tax-id/corrections/{correctionId:guid}/apply", async (
                Guid correctionId,
                ApplyCompanyTaxIdCorrection command,
                IOrganizationCompanyTaxIdService service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
        {
            if (correctionId == Guid.Empty)
            {
                return InvalidMasterData("Düzeltme kimliği boş olamaz.");
            }

            if (command.ExpectedVersion < 1)
            {
                return Results.Problem(
                    title: "Beklenen sürüm gerekli.",
                    detail: "VKN düzeltmesi için şirket kaydının sıfırdan büyük beklenen sürümü zorunludur.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryReadCommandMetadata(
                    httpContext,
                    currentUser,
                    companyContext,
                    correlationContext,
                    out var metadata))
            {
                return MissingCommandMetadata();
            }

            var execution = await service.ApplyCompanyTaxIdCorrectionAsync(
                correctionId,
                command,
                metadata,
                cancellationToken);
            WriteReplayHeaders(httpContext, execution);
            return ToCompanyTaxIdHttpResult(execution.Response);
        }).RequireCompanyContext()
            .RequireAuthorization(OrganizationCompanyTaxIdPermissionsV1.Approve)
            .RequiresNonEmptyGuidPathParameter("correctionId")
            .WithIdempotencyContract(
                StatusCodes.Status204NoContent,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .WithName("ApplyOrganizationCompanyTaxIdCorrectionV1")
            .Accepts<ApplyCompanyTaxIdCorrection>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                OrganizationProblemCodesV1.CompanyTaxIdCorrectionNotPending,
                OrganizationProblemCodesV1.CompanyTaxIdCorrectionStale,
                OrganizationProblemCodesV1.CompanyTaxIdVersionConflict,
                OrganizationProblemCodesV1.CompanyTaxIdIdempotencyConflict)
            .ProducesErpUnprocessableEntity(OrganizationProblemCodesV1.CompanyTaxIdInUse)
            .ProducesErpAuthenticatedProblems();
    }

    private static IResult MissingCommandMetadata()
    {
        return Results.Problem(
            title: "Command bilgisi eksik.",
            detail: "İşlem için geçerli Idempotency-Key ve correlation bilgisi gerekir.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static void WriteReplayHeaders(HttpContext httpContext, CommandExecutionResult execution)
    {
        if (!execution.IsReplay)
        {
            return;
        }

        httpContext.Response.Headers["Idempotency-Replayed"] = "true";
        httpContext.Response.Headers["Idempotency-Original-Correlation-Id"] = execution.OriginalCorrelationId;
    }

    /// <summary>
    /// Maps a tax-id command outcome to HTTP. Every 409/422 carries its stable code; a version conflict also carries the
    /// expected/current versions. A missing or foreign record stays the module's code-less 404.
    /// </summary>
    private static IResult ToCompanyTaxIdHttpResult(CommandResponseSnapshot response)
    {
        if (response.StatusCode == StatusCodes.Status201Created)
        {
            if (response.Created is null)
            {
                throw new InvalidOperationException("VKN değişiklik talebi sonucu HTTP durumuyla eşleşmiyor.");
            }

            return Results.Created(CompanyTaxIdLocation, response.Created);
        }

        var code = CompanyTaxIdProblemCode(response.Result);
        if (code is null)
        {
            return ToHttpResult(response);
        }

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
        };
        if (response.Conflict is not null)
        {
            extensions["conflict"] = response.Conflict;
        }

        return Results.Problem(
            title: response.Title,
            detail: response.Detail,
            statusCode: response.StatusCode,
            extensions: extensions);
    }

    private static string? CompanyTaxIdProblemCode(CommandResult result) => result switch
    {
        CommandResult.TaxIdInUse => OrganizationProblemCodesV1.CompanyTaxIdInUse,
        CommandResult.TaxIdChangePending => OrganizationProblemCodesV1.CompanyTaxIdChangePending,
        CommandResult.TaxIdChangeNotPending => OrganizationProblemCodesV1.CompanyTaxIdChangeNotPending,
        CommandResult.TaxIdChangeStale => OrganizationProblemCodesV1.CompanyTaxIdChangeStale,
        CommandResult.TaxIdSelfDecision => OrganizationProblemCodesV1.CompanyTaxIdSelfDecision,
        CommandResult.TaxIdUnchanged => OrganizationProblemCodesV1.CompanyTaxIdUnchanged,
        CommandResult.TaxIdCheckDigitInvalid => OrganizationProblemCodesV1.CompanyTaxIdCheckDigitInvalid,
        CommandResult.TaxIdCorrectionNotPending => OrganizationProblemCodesV1.CompanyTaxIdCorrectionNotPending,
        CommandResult.TaxIdCorrectionStale => OrganizationProblemCodesV1.CompanyTaxIdCorrectionStale,
        CommandResult.VersionConflict => OrganizationProblemCodesV1.CompanyTaxIdVersionConflict,
        CommandResult.IdempotencyConflict => OrganizationProblemCodesV1.CompanyTaxIdIdempotencyConflict,
        _ => null,
    };
}
