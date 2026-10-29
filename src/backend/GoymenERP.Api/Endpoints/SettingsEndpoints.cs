using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-D61…SAT-D70 (Sinan, 2026-10-27, EK10/EK11): the Settings owner's discount limit groups, their limits and the
/// person → group bindings. Every route is in the server-resolved company (X-Company-Id), under ONE name,
/// <see cref="SettingsPermissionManifestV1.SalesDiscountLimitsManage"/> (company-wide, not Critical, granted to no role here),
/// re-authorized by the service and, for the binding, in every company it reads or writes. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET …/groups</c> — the company's groups (passive ones included), each with the record and the limit in force
/// today and its scheduled records (SAT-D78).</item>
/// <item><c>POST …/groups</c> (201 + id) · <c>PUT …/groups/{groupId}/name</c> · <c>POST …/groups/{groupId}/deactivation</c>
/// · <c>POST …/groups/{groupId}/activation</c> (SAT-D118)
/// · <c>PUT …/groups/{groupId}/limit</c> (204; SAT-D78: optional later <c>effectiveFrom</c>) · SAT-D84
/// <c>POST …/groups/{groupId}/limit/withdrawal</c> and <c>…/limit/correction</c> for a record not in force yet (204).</item>
/// <item><c>GET …/people?q=&amp;limit=</c> — active people of the company (id + display name) for the binding editor.</item>
/// <item><c>GET …/bindings/{userId}</c> / <c>PUT …/bindings/{userId}</c> — the person's groups in every company the actor
/// can act in; the write goes to all of them in one transaction (SAT-D66 default).</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the version as decimal
/// text, a mandatory reason (≤ 4000). Another company's group is the same code-less 404 as an unknown one.
/// </summary>
public static class SettingsEndpoints
{
    public const string SalesDiscountLimitsPath = "/api/v1/settings/sales-discount-limits";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    private static readonly string[] CommandBadRequestCodes =
    [
        SalesDiscountLimitProblemCodesV1.RequestInvalid,
        SalesDiscountLimitProblemCodesV1.CommandMetadataInvalid,
        SalesDiscountLimitProblemCodesV1.ReasonRequired,
        SalesDiscountLimitProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(SalesDiscountLimitsPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("/groups", async (
                HttpContext httpContext,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, allowedQuery: [], () => service.GetGroupsAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .WithName("GetSettingsSalesDiscountLimitGroupsV1")
            .Produces<SalesDiscountLimitGroupListV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(SalesDiscountLimitProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/groups", async (
                CreateSalesDiscountLimitGroupRequestV1 request,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.CreateGroupAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .WithName("CreateSettingsSalesDiscountLimitGroupV1")
            .WithIdempotencyContract(StatusCodes.Status201Created, StatusCodes.Status409Conflict)
            .Accepts<CreateSalesDiscountLimitGroupRequestV1>("application/json")
            .Produces<SalesDiscountLimitGroupCreatedV1>(StatusCodes.Status201Created)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                SalesDiscountLimitProblemCodesV1.GroupCodeTaken,
                SalesDiscountLimitProblemCodesV1.GroupNameTaken)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/groups/{groupId:guid}/name", async (
                Guid groupId,
                RenameSalesDiscountLimitGroupRequestV1 request,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RenameGroupAsync(groupId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .RequiresNonEmptyGuidPathParameter("groupId")
            .WithName("RenameSettingsSalesDiscountLimitGroupV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<RenameSalesDiscountLimitGroupRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                SalesDiscountLimitProblemCodesV1.VersionConflict,
                SalesDiscountLimitProblemCodesV1.GroupInactive,
                SalesDiscountLimitProblemCodesV1.GroupNameTaken)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/groups/{groupId:guid}/deactivation", async (
                Guid groupId,
                DeactivateSalesDiscountLimitGroupRequestV1 request,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.DeactivateGroupAsync(groupId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .RequiresNonEmptyGuidPathParameter("groupId")
            .WithName("DeactivateSettingsSalesDiscountLimitGroupV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<DeactivateSalesDiscountLimitGroupRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                SalesDiscountLimitProblemCodesV1.VersionConflict,
                SalesDiscountLimitProblemCodesV1.GroupInactive)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // SAT-D118: a passive group is reactivated with a reason (one person, audited).
        group.MapPost("/groups/{groupId:guid}/activation", async (
                Guid groupId,
                ReactivateSalesDiscountLimitGroupRequestV1 request,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.ReactivateGroupAsync(groupId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .RequiresNonEmptyGuidPathParameter("groupId")
            .WithName("ReactivateSettingsSalesDiscountLimitGroupV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<ReactivateSalesDiscountLimitGroupRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                SalesDiscountLimitProblemCodesV1.VersionConflict,
                SalesDiscountLimitProblemCodesV1.GroupActive)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/groups/{groupId:guid}/limit", async (
                Guid groupId,
                SetSalesDiscountLimitRequestV1 request,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.SetLimitAsync(groupId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .RequiresNonEmptyGuidPathParameter("groupId")
            .WithName("SetSettingsSalesDiscountLimitV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<SetSalesDiscountLimitRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                SalesDiscountLimitProblemCodesV1.VersionConflict,
                SalesDiscountLimitProblemCodesV1.GroupInactive)
            .ProducesErpUnprocessableEntity(
                SalesDiscountLimitProblemCodesV1.LinkedGroupNotFound,
                SalesDiscountLimitProblemCodesV1.LinkedGroupInactive,
                SalesDiscountLimitProblemCodesV1.LinkCycle,
                SalesDiscountLimitProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // SAT-D84: a not-yet-effective record is withdrawn, or corrected (withdrawn + replaced), before its date.
        group.MapPost("/groups/{groupId:guid}/limit/withdrawal", async (
                Guid groupId,
                WithdrawSalesDiscountLimitRequestV1 request,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.WithdrawLimitAsync(groupId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .RequiresNonEmptyGuidPathParameter("groupId")
            .WithName("WithdrawSettingsSalesDiscountLimitV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<WithdrawSalesDiscountLimitRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                SalesDiscountLimitProblemCodesV1.VersionConflict,
                SalesDiscountLimitProblemCodesV1.GroupInactive,
                SalesDiscountLimitProblemCodesV1.RecordInEffect,
                SalesDiscountLimitProblemCodesV1.RecordWithdrawn)
            .ProducesErpUnprocessableEntity(
                SalesDiscountLimitProblemCodesV1.LinkCycle)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/groups/{groupId:guid}/limit/correction", async (
                Guid groupId,
                CorrectSalesDiscountLimitRequestV1 request,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.CorrectLimitAsync(groupId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .RequiresNonEmptyGuidPathParameter("groupId")
            .WithName("CorrectSettingsSalesDiscountLimitV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<CorrectSalesDiscountLimitRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpCodeLessNotFound()
            .ProducesErpConflict(
                SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                SalesDiscountLimitProblemCodesV1.VersionConflict,
                SalesDiscountLimitProblemCodesV1.GroupInactive,
                SalesDiscountLimitProblemCodesV1.RecordInEffect,
                SalesDiscountLimitProblemCodesV1.RecordWithdrawn)
            .ProducesErpUnprocessableEntity(
                SalesDiscountLimitProblemCodesV1.LinkedGroupNotFound,
                SalesDiscountLimitProblemCodesV1.LinkedGroupInactive,
                SalesDiscountLimitProblemCodesV1.LinkCycle,
                SalesDiscountLimitProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/people", async (
                string? q,
                int? limit,
                HttpContext httpContext,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, allowedQuery: ["q", "limit"],
                () => service.SearchPeopleAsync(q, limit, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .WithName("SearchSettingsSalesDiscountLimitPeopleV1")
            .Produces<SalesDiscountLimitPersonListV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(SalesDiscountLimitProblemCodesV1.RequestInvalid)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/bindings/{userId:guid}", async (
                Guid userId,
                HttpContext httpContext,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, allowedQuery: [], () => service.GetBindingAsync(userId, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .RequiresNonEmptyGuidPathParameter("userId")
            .WithName("GetSettingsSalesDiscountLimitBindingV1")
            .Produces<SalesDiscountLimitBindingViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(SalesDiscountLimitProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/bindings/{userId:guid}", async (
                Guid userId,
                SetSalesDiscountLimitBindingRequestV1 request,
                ISettingsSalesDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.SetBindingAsync(userId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsPermissionManifestV1.SalesDiscountLimitsManage)
            .RequiresNonEmptyGuidPathParameter("userId")
            .WithName("SetSettingsSalesDiscountLimitBindingV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<SetSalesDiscountLimitBindingRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesDiscountLimitProblemCodesV1.IdempotencyConflict,
                SalesDiscountLimitProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(
                SalesDiscountLimitProblemCodesV1.BindingCompaniesMismatch,
                SalesDiscountLimitProblemCodesV1.BindingPersonNotMember,
                SalesDiscountLimitProblemCodesV1.BindingGroupNotFound,
                SalesDiscountLimitProblemCodesV1.BindingGroupInactive)
            .ProducesErpServiceUnavailable(SalesDiscountLimitProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, string[] allowedQuery, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Keys.Any(key => !allowedQuery.Contains(key, StringComparer.Ordinal)))
        {
            return Problem(StatusCodes.Status400BadRequest, SalesDiscountLimitProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Şirket yalnız X-Company-Id başlığından belirlenir; bu sorgu parametresi kabul edilmez.");
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
        Func<SettingsCommandMetadataV1, Task<SettingsCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SalesDiscountLimitProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, SalesDiscountLimitProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        SettingsCommandResultV1 result;
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
            _ when answer.Code is { } code => Problem(answer.StatusCode, code, answer.Title, answer.Detail,
                answer.CompanyId, answer.GroupCode),
            _ => throw new InvalidOperationException("Ayarlar komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        "settings_access_denied" => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        SalesDiscountLimitProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            SalesDiscountLimitProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, SalesDiscountLimitProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.", "İskonto limit kayıtları şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
    };

    private static IResult Problem(int status, string code, string title, string detail, Guid? companyId = null,
        string? groupCode = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (companyId is { } company)
        {
            extensions["companyId"] = company;
        }

        if (groupCode is not null)
        {
            extensions["groupCode"] = groupCode;
        }

        return Results.Problem(title: title, detail: detail, statusCode: status, extensions: extensions);
    }

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
