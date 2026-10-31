using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// MOD-03 tax master (SAT-D73/D74/D82/D83, EK12; SAT-D93/D110, EK13): the Settings owner's VAT classes, withholding classes
/// and buyer categories, their effective-dated rate records and the UBL-TR exemption / withholding code lists. Every route is
/// in the server-resolved company (X-Company-Id) under ONE name, <see cref="SettingsTaxMasterPermissionManifestV1.TaxMasterManage"/>
/// (company-wide, not Critical, granted to no role here), re-authorized by the service. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/tax-master</c> — categories, the rate records in force today and scheduled, and both code
/// lists. Nothing is seeded: an empty master is "kaynak yok" everywhere.</item>
/// <item><c>POST …/categories</c> (201 + id) · <c>PUT …/categories/{categoryId}/name</c> ·
/// <c>POST …/vat-classes/{classId}/rates</c> · <c>POST …/withholding-classes/{classId}/rates</c> ·
/// <c>POST …/rate-records/{recordId}/withdrawal</c> · <c>POST …/code-lists/{listKind}/versions</c> ·
/// <c>POST …/code-lists/{listKind}/versions/{listVersion}/withdrawal</c> · SAT-D93 <c>POST …/withholding-threshold</c> ·
/// <c>POST …/withholding-threshold/records/{recordId}/withdrawal</c> (204).</item>
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

        group.MapPost("/categories", async (
                CreateTaxCategoryRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.CreateCategoryAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .WithName("CreateSettingsTaxCategoryV1")
            .WithIdempotencyContract(StatusCodes.Status201Created, StatusCodes.Status409Conflict)
            .Accepts<CreateTaxCategoryRequestV1>("application/json")
            .Produces<TaxCategoryCreatedV1>(StatusCodes.Status201Created)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.CategoryCodeTaken,
                TaxMasterProblemCodesV1.CategoryNameTaken)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/categories/{categoryId:guid}/name", async (
                Guid categoryId,
                RenameTaxCategoryRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RenameCategoryAsync(categoryId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .RequiresNonEmptyGuidPathParameter("categoryId")
            .WithName("RenameSettingsTaxCategoryV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<RenameTaxCategoryRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict,
                TaxMasterProblemCodesV1.CategoryNameTaken)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/vat-classes/{classId:guid}/rates", async (
                Guid classId,
                RecordVatRateRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordVatRateAsync(classId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .RequiresNonEmptyGuidPathParameter("classId")
            .WithName("RecordSettingsVatRateV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordVatRateRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(
                TaxMasterProblemCodesV1.CategoryKindMismatch,
                TaxMasterProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/withholding-classes/{classId:guid}/rates", async (
                Guid classId,
                RecordWithholdingRateRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordWithholdingRateAsync(classId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .RequiresNonEmptyGuidPathParameter("classId")
            .WithName("RecordSettingsWithholdingRateV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordWithholdingRateRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                TaxMasterProblemCodesV1.IdempotencyConflict,
                TaxMasterProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(
                TaxMasterProblemCodesV1.CategoryKindMismatch,
                TaxMasterProblemCodesV1.BuyerCategoryNotFound,
                TaxMasterProblemCodesV1.WithholdingCodeUnknown,
                TaxMasterProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(TaxMasterProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/rate-records/{recordId:guid}/withdrawal", async (
                Guid recordId,
                WithdrawTaxRateRecordRequestV1 request,
                ISettingsTaxMasterAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.WithdrawRateRecordAsync(recordId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsTaxMasterPermissionManifestV1.TaxMasterManage)
            .RequiresNonEmptyGuidPathParameter("recordId")
            .WithName("WithdrawSettingsTaxRateRecordV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<WithdrawTaxRateRecordRequestV1>("application/json")
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
                WithdrawTaxRateRecordRequestV1 request,
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
            .Accepts<WithdrawTaxRateRecordRequestV1>("application/json")
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
            StatusCodes.Status201Created when answer.Created is { } created => Results.Json(created,
                statusCode: StatusCodes.Status201Created),
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
