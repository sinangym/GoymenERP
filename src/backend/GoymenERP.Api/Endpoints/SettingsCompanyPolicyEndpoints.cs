using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// KIM-S19 (B-13, ADR-SET-002): company-wide policy keys as insert-only, versioned Settings records. Every route is in the
/// server-resolved company (X-Company-Id), re-authorized by the service; responses are private, no-store.
/// <list type="bullet">
/// <item><c>/finance-opening-period</c> — the Finance opening-balance period (open/closed) under
/// <see cref="SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage"/> (company-wide, not Critical, granted to
/// no role). Nothing recorded is "not recorded", never open.</item>
/// <item><c>/manufacturing-due-date-buffer</c> — URT-D96/URT-D100 company default buffer in working days (start value 0).</item>
/// <item><c>/sales-subcontract-tax-default</c> — SAT-D381 default subcontract VAT/withholding; codes are checked against the
/// tax master lists (422 when unlisted). The separate purchasing record uses its own purchase-order approval authority.</item>
/// </list>
/// Commands (<c>POST …/versions</c>, 204): one Idempotency-Key (stored terminal answers replay with
/// <c>Idempotency-Replayed</c>), the expected version as decimal text, a mandatory reason (≤ 4000).
/// </summary>
public static partial class SettingsCompanyPolicyEndpoints
{
    public const string CompanyPolicyPath = "/api/v1/settings/company-policies";
    private const string IdempotencyHeaderName = "Idempotency-Key";
    private const int MaximumIdempotencyKeyLength = 128;

    internal static readonly string[] CommandBadRequestCodes =
    [
        CompanyPolicyProblemCodesV1.RequestInvalid,
        CompanyPolicyProblemCodesV1.CommandMetadataInvalid,
        CompanyPolicyProblemCodesV1.ReasonRequired,
        CompanyPolicyProblemCodesV1.ReasonTooLong,
    ];

    public static IEndpointRouteBuilder MapSettingsCompanyPolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(CompanyPolicyPath)
            .RequireAuthorization()
            .WithTags("Settings");

        group.MapGet("/sales-contract-discount-limit", async (
                HttpContext httpContext,
                ISettingsSalesContractDiscountLimitAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetSalesContractDiscountLimitAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesContractDiscountLimitPermissionsV1.Manage)
            .WithName("GetSettingsSalesContractDiscountLimitV1")
            .Produces<SalesContractDiscountLimitViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/sales-contract-discount-limit/versions", async (
                RecordSalesContractDiscountLimitRequestV1 request,
                ISettingsSalesContractDiscountLimitAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordSalesContractDiscountLimitAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsSalesContractDiscountLimitPermissionsV1.Manage)
            .WithName("RecordSettingsSalesContractDiscountLimitV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<RecordSalesContractDiscountLimitRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                CompanyPolicyProblemCodesV1.IdempotencyConflict,
                CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/finance-opening-period", async (
                HttpContext httpContext,
                ISettingsCompanyPolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetFinanceOpeningPeriodAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage)
            .WithName("GetSettingsFinanceOpeningPeriodV1")
            .Produces<FinanceOpeningPeriodViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/finance-opening-period/versions", async (
                RecordFinanceOpeningPeriodRequestV1 request,
                ISettingsCompanyPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordFinanceOpeningPeriodAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(SettingsCompanyPolicyPermissionManifestV1.FinanceOpeningPeriodManage)
            .WithName("RecordSettingsFinanceOpeningPeriodV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<RecordFinanceOpeningPeriodRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                CompanyPolicyProblemCodesV1.IdempotencyConflict,
                CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/manufacturing-due-date-buffer", async (
                HttpContext httpContext,
                ISettingsCompanyPolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetManufacturingDueDateBufferAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansApprove)
            .WithName("GetSettingsManufacturingDueDateBufferV1")
            .Produces<ManufacturingDueDateBufferViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/manufacturing-due-date-buffer/versions", async (
                RecordManufacturingDueDateBufferRequestV1 request,
                ISettingsCompanyPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordManufacturingDueDateBufferAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.ManufacturingPlansApprove)
            .WithName("RecordSettingsManufacturingDueDateBufferV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<RecordManufacturingDueDateBufferRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                CompanyPolicyProblemCodesV1.IdempotencyConflict,
                CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/sales-subcontract-tax-default", async (
                HttpContext httpContext,
                ISettingsCompanyPolicyAdministrationV1 service,
                CancellationToken cancellationToken) =>
            await ReadAsync(httpContext, () => service.GetSalesSubcontractTaxDefaultAsync(cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.SalesOrdersApprove)
            .WithName("GetSettingsSalesSubcontractTaxDefaultV1")
            .Produces<SubcontractTaxDefaultViewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/sales-subcontract-tax-default/versions", async (
                RecordSubcontractTaxDefaultRequestV1 request,
                ISettingsCompanyPolicyAdministrationV1 service,
                ICurrentUser currentUser,
                ICompanyContext companyContext,
                ICorrelationContext correlationContext,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            await CommandAsync(httpContext, currentUser, companyContext, correlationContext,
                metadata => service.RecordSalesSubcontractTaxDefaultAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.SalesOrdersApprove)
            .WithName("RecordSettingsSalesSubcontractTaxDefaultV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .Accepts<RecordSubcontractTaxDefaultRequestV1>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CommandBadRequestCodes)
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(
                CompanyPolicyProblemCodesV1.IdempotencyConflict,
                CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpUnprocessableEntity(
                CompanyPolicyProblemCodesV1.ExemptionCodeUnknown,
                CompanyPolicyProblemCodesV1.WithholdingCodeUnknown,
                CompanyPolicyProblemCodesV1.RateUnknown)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();

        MapPurchasingSubcontractTaxDefault(group);
        MapOperationalPolicies(group);
        MapLegalDeadlineCalendar(group);
        MapDocumentNumberPolicies(group);
        return endpoints;
    }

    private static async Task<IResult> ReadAsync<T>(HttpContext httpContext, Func<Task<T>> read)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid,
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
        Func<SettingsCommandMetadataV1, Task<CompanyPolicyCommandResultV1>> execute)
    {
        httpContext.Response.Headers.CacheControl = "private, no-store";
        if (httpContext.Request.Query.Count != 0)
        {
            return Problem(StatusCodes.Status400BadRequest, CompanyPolicyProblemCodesV1.RequestInvalid,
                "İstek geçersiz.", "Sorgu parametresi kabul edilmez; şirket sunucu tarafından belirlenir.");
        }

        if (!TryReadMetadata(httpContext, currentUser, companyContext, correlationContext, out var metadata))
        {
            return Problem(StatusCodes.Status400BadRequest, CompanyPolicyProblemCodesV1.CommandMetadataInvalid,
                "Komut bilgisi eksik.", "İşlem için tek ve geçerli bir Idempotency-Key ve correlation bilgisi gerekir.");
        }

        CompanyPolicyCommandResultV1 result;
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
            _ => throw new InvalidOperationException("Şirket politikası komut sonucu HTTP durumuyla eşleşmiyor."),
        };
    }

    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) => exception.Code switch
    {
        CompanyPolicyProblemCodesV1.AccessDenied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
        CompanyPolicyProblemCodesV1.RequestInvalid => Problem(StatusCodes.Status400BadRequest,
            CompanyPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", "İstek alanları geçersiz."),
        _ => Problem(StatusCodes.Status503ServiceUnavailable, CompanyPolicyProblemCodesV1.ProviderUnavailable,
            "Ayarlar kullanılamıyor.",
            "Şirket politikası şu anda okunamıyor veya yazılamıyor; daha sonra yeniden deneyin."),
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
