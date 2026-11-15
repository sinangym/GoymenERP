using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-029 S4b (URUN_F029 spec §5.4(1); URN-D2…D5): the Sales delivery tolerance (SAT-D5) and order-approval amount threshold
/// (SAT-D6) as persistent, versioned Settings records. Every route is in the server-resolved company (X-Company-Id) under the
/// family's own name (<see cref="SettingsSalesPolicyPermissionManifestV1"/>; company-wide, not Critical, granted to no role
/// here), re-authorized by the service. Responses are private, no-store.
/// <list type="bullet">
/// <item><c>GET /api/v1/settings/sales-policies/delivery-tolerance</c> · <c>GET …/approval-thresholds</c> — the timeline
/// (the reviewed catalog until the family's baseline is transferred, the stored versions afterwards).</item>
/// <item><c>POST …/delivery-tolerance/baseline-transfer</c> · <c>POST …/approval-thresholds/baseline-transfer</c> (204) — the
/// one-time, explicit copy of the reviewed catalog entries into the table.</item>
/// <item><c>POST …/delivery-tolerance/versions</c> · <c>POST …/approval-thresholds/{currencyId}/versions</c> (204) — a new
/// version from its business date (today or later).</item>
/// </list>
/// Commands: one Idempotency-Key (stored terminal answers replay with <c>Idempotency-Replayed</c>), the expected version as
/// decimal text, a mandatory reason (≤ 4000).
/// </summary>
public static class SettingsSalesPolicyEndpoints
{
    public const string SalesPoliciesPath = "/api/v1/settings/sales-policies";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        SalesPolicyProblemCodesV1.RequestInvalid,
        SalesPolicyProblemCodesV1.CommandMetadataInvalid,
        SalesPolicyProblemCodesV1.ReasonRequired,
        SalesPolicyProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsSalesPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(SalesPoliciesPath)
            .RequireAuthorization()
            .WithTags("Settings");

        // SAT-D5: the company's delivery tolerance.
        group.MapGet("/delivery-tolerance", async (
                HttpContext httpContext,
                ISettingsSalesPolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetDeliveryToleranceAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage)
            .WithName("GetSettingsSalesDeliveryToleranceV1")
            .Produces<SalesDeliveryToleranceViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(SalesPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/delivery-tolerance/baseline-transfer", async (
                TransferSalesPolicyBaselineRequestV1 request,
                ISettingsSalesPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.TransferDeliveryToleranceBaselineAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage)
            .WithName("TransferSettingsSalesDeliveryToleranceBaselineV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<TransferSalesPolicyBaselineRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.AlreadyTransferred)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/delivery-tolerance/versions", async (
                RecordSalesDeliveryToleranceRequestV1 request,
                ISettingsSalesPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordDeliveryToleranceAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesPolicyPermissionManifestV1.DeliveryToleranceManage)
            .WithName("RecordSettingsSalesDeliveryToleranceV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordSalesDeliveryToleranceRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.VersionConflict,
                SalesPolicyProblemCodesV1.BaselineRequired)
            .ProducesErpUnprocessableEntity(SalesPolicyProblemCodesV1.EffectiveFromPast)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        // SAT-D6: the order-approval amount threshold, one timeline per currency.
        group.MapGet("/approval-thresholds", async (
                HttpContext httpContext,
                ISettingsSalesPolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetApprovalThresholdsAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage)
            .WithName("GetSettingsSalesApprovalThresholdsV1")
            .Produces<SalesApprovalThresholdsViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(SalesPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/approval-thresholds/baseline-transfer", async (
                TransferSalesPolicyBaselineRequestV1 request,
                ISettingsSalesPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.TransferApprovalThresholdBaselineAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage)
            .WithName("TransferSettingsSalesApprovalThresholdBaselineV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<TransferSalesPolicyBaselineRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.AlreadyTransferred)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/approval-thresholds/{currencyId:guid}/versions", async (
                Guid currencyId,
                RecordSalesApprovalThresholdRequestV1 request,
                ISettingsSalesPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordApprovalThresholdAsync(currencyId, request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesPolicyPermissionManifestV1.ApprovalThresholdManage)
            .RequiresNonEmptyGuidPathParameter("currencyId")
            .WithName("RecordSettingsSalesApprovalThresholdV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordSalesApprovalThresholdRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                SalesPolicyProblemCodesV1.IdempotencyConflict,
                SalesPolicyProblemCodesV1.VersionConflict,
                SalesPolicyProblemCodesV1.BaselineRequired)
            .ProducesErpUnprocessableEntity(
                SalesPolicyProblemCodesV1.EffectiveFromPast,
                SalesPolicyProblemCodesV1.CurrencyNotServed,
                SalesPolicyProblemCodesV1.AmountScaleExceeded)
            .ProducesErpServiceUnavailable(SalesPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        return endpoints;
    }

    internal static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SalesPolicyProblemCodesV1.RequestInvalid,
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

    internal static async Task<IResult> CommandAsync(
        HttpContext httpContext,
        ICurrentUser currentUser,
        ICompanyContext companyContext,
        ICorrelationContext correlationContext,
        Func<SettingsCommandMetadataV1, Task<SalesPolicyCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, SalesPolicyProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, SalesPolicyProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        SalesPolicyCommandResultV1 result;
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
            _ when answer.Code is { } code => Problem(answer.StatusCode, code, answer.Title, answer.Detail),
            _ => throw new InvalidOperationException("Satış politikası komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        SalesPolicyProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        SalesPolicyProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            SalesPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, SalesPolicyProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.", "Satış politikaları şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
