using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// MOD-03 tax master (SAT-D83, EK12; SAT-D93, EK13; SAT-D133…D139, EK19): the Settings owner's default tax per item type (the
/// reviewed SAT-D139 catalog as the base, Settings records over it), the UBL-TR exemption / withholding code lists and the VUK
/// 232 withholding threshold. Every route is
/// in the server-resolved company (X-Company-Id) under ONE name, <see cref="SettingsTaxMasterPermissionManifestV1.TaxMasterManage"/>
/// (company-wide, not Critical, granted to no role here), re-authorized by the service. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/tax-master</c> — the default per item type in force today and scheduled, both code lists and
/// the threshold. Nothing is seeded: a code list or threshold not entered is "kaynak yok".</item>
/// <item><c>POST …/item-type-defaults/{itemType}</c> · <c>POST …/item-type-defaults/records/{recordId}/withdrawal</c> ·
/// <c>POST …/code-lists/{listKind}/versions</c> ·
/// <c>POST …/code-lists/{listKind}/versions/{listVersion}/withdrawal</c> · SAT-D93 <c>POST …/withholding-threshold</c> ·
/// <c>POST …/withholding-threshold/records/{recordId}/withdrawal</c> (204).</item>
/// <item>SAT-D158 (EK21): <c>GET …/buyer-situations</c> · <c>POST …/buyer-situations/versions</c> ·
/// <c>POST …/buyer-situations/versions/{listVersion}/withdrawal</c> (204) — the buyer situation list over the reviewed catalog;
/// the main read carries it too.</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the version as decimal
/// text, a mandatory reason (≤ 4000). Another company's record is the same code-less 404 as an unknown one.
/// </summary>
public static class SettingsTaxMasterEndpoints
{
    public const string TaxMasterPath = "/api/v1/settings/tax-master";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    private static readonly string[] CommandBadRequestCodes =
    [
        TaxMasterProblemCodesV1.RequestInvalid,
        TaxMasterProblemCodesV1.CommandMetadataInvalid,
        TaxMasterProblemCodesV1.ReasonRequired,
        TaxMasterProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsTaxMasterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(TaxMasterPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("", async (
                HttpContext httpContext,
                ISettingsTaxMasterAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("GetSettingsTaxMasterV1")
            .Produces<TaxMasterViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(TaxMasterProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // SAT-D139: the default tax per item type (the reviewed catalog is the base; a record replaces it from its date).
        group.MapPost("/item-type-defaults/{itemType}", async (
                string itemType,
                RecordItemTypeDefaultRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordItemTypeDefaultAsync(itemType, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("RecordSettingsItemTypeDefaultV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordItemTypeDefaultRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(
                TaxMasterProblemCodesV1.ExemptionCodeUnknown,
                TaxMasterProblemCodesV1.WithholdingCodeUnknown,
                TaxMasterProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/item-type-defaults/records/{recordId:guid}/withdrawal", async (
                Guid recordId,
                WithdrawTaxRecordRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.WithdrawItemTypeDefaultAsync(recordId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .RequiresNonEmptyGuidPathParameter("recordId")
            .WithName("WithdrawSettingsItemTypeDefaultV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<WithdrawTaxRecordRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict,
                TaxMasterProblemCodesV1.RecordInEffect,
                TaxMasterProblemCodesV1.RecordWithdrawn)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/code-lists/{listKind}/versions", async (
                string listKind,
                PublishTaxCodeListRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.PublishCodeListAsync(listKind, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("PublishSettingsTaxCodeListV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<PublishTaxCodeListRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(TaxMasterProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/code-lists/{listKind}/versions/{listVersion:long}/withdrawal", async (
                string listKind,
                long listVersion,
                WithdrawTaxCodeListRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.WithdrawCodeListAsync(listKind, listVersion, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("WithdrawSettingsTaxCodeListV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<WithdrawTaxCodeListRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict,
                TaxMasterProblemCodesV1.RecordInEffect,
                TaxMasterProblemCodesV1.RecordWithdrawn)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // SAT-D93 (Sinan, 2026-10-31): the dated VUK 232 withholding amount threshold, starting empty.
        group.MapPost("/withholding-threshold", async (
                RecordWithholdingThresholdRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordWithholdingThresholdAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("RecordSettingsWithholdingThresholdV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordWithholdingThresholdRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(TaxMasterProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/withholding-threshold/records/{recordId:guid}/withdrawal", async (
                Guid recordId,
                WithdrawTaxRecordRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.WithdrawWithholdingThresholdAsync(recordId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .RequiresNonEmptyGuidPathParameter("recordId")
            .WithName("WithdrawSettingsWithholdingThresholdV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<WithdrawTaxRecordRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict,
                TaxMasterProblemCodesV1.RecordInEffect,
                TaxMasterProblemCodesV1.RecordWithdrawn)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // SAT-D158 (EK21): the effective-dated buyer situation list, each situation with its tax effect as data.
        group.MapGet("/buyer-situations", async (
                HttpContext httpContext,
                ISettingsTaxMasterAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetBuyerSituationsAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("GetSettingsTaxBuyerSituationsV1")
            .Produces<TaxBuyerSituationListViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(TaxMasterProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/buyer-situations/versions", async (
                PublishTaxBuyerSituationListRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.PublishBuyerSituationListAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("PublishSettingsTaxBuyerSituationListV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<PublishTaxBuyerSituationListRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(TaxMasterProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/buyer-situations/versions/{listVersion:long}/withdrawal", async (
                long listVersion,
                WithdrawTaxCodeListRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.WithdrawBuyerSituationListAsync(listVersion, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("WithdrawSettingsTaxBuyerSituationListV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<WithdrawTaxCodeListRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict,
                TaxMasterProblemCodesV1.RecordInEffect,
                TaxMasterProblemCodesV1.RecordWithdrawn)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, TaxMasterProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Şirket yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
        }

        try
        {
            return Results.Ok(await read());
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return FromPrerequisite(exception);
        }
    }

    private static async Task<IResult> CommandAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        Func<SettingsCommandMetadataV1, Task<TaxMasterCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, TaxMasterProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, TaxMasterProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        TaxMasterCommandResultV1 result;
        try
        {
            result = await execute(metadata);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }
        catch (SettingsPrerequisiteException exception)
        {
            return FromPrerequisite(exception);
        }

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
            _ => throw new InvalidOperationException("Vergi masterı komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        TaxMasterProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        TaxMasterProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            TaxMasterProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, TaxMasterProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.", "Vergi masterı şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
    };

    private static IResult Problem(int status, string code, string title, string detail) =>
        Results.Problem(title: title, detail: detail, statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static bool TryReadMetadata(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        out SettingsCommandMetadataV1 metadata)
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

        metadata = new SettingsCommandMetadataV1(
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
