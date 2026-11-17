using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class SettingsFinancePolicyEndpoints
{
    public const string Path = "/api/v1/settings/finance-policies";
    public static IEndpointRouteBuilder MapSettingsFinancePolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).WithTags("Settings").RequireAuthorization().RequireCompanyContext();
        ReadContract(group.MapGet("", GetAsync).WithName("SettingsFinancePoliciesGetV1").Produces<SettingsFinancePoliciesWorkspaceV1>());
        CommandContract(group.MapPost("/approval-matrices", (PrepareSettingsFinanceApprovalPolicyV1 request, HttpContext context,
            [FromServices] ISettingsFinancePolicyAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.PrepareApprovalAsync(request, metadata, token))), "SettingsFinancePolicyPrepareApprovalV1");
        CommandContract(group.MapPost("/cash-count-differences", (PrepareSettingsCashCountDifferencePolicyV1 request, HttpContext context,
            [FromServices] ISettingsFinancePolicyAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.PrepareCashDifferenceAsync(request, metadata, token))), "SettingsFinancePolicyPrepareCashDifferenceV1");
        CommandContract(group.MapPost("/approval-matrices/{id:guid}/publish", (Guid id, PublishSettingsFinancePolicyV1 request, HttpContext context,
            [FromServices] ISettingsFinancePolicyAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.PublishApprovalAsync(id, request, metadata, token))), "SettingsFinancePolicyPublishApprovalV1");
        CommandContract(group.MapPost("/cash-count-differences/{id:guid}/publish", (Guid id, PublishSettingsFinancePolicyV1 request, HttpContext context,
            [FromServices] ISettingsFinancePolicyAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.PublishCashDifferenceAsync(id, request, metadata, token))), "SettingsFinancePolicyPublishCashDifferenceV1");
        CommandContract(group.MapPost("/approval-matrices/{id:guid}/retire", (Guid id, RetireSettingsFinancePolicyV1 request, HttpContext context,
            [FromServices] ISettingsFinancePolicyAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.RetireApprovalAsync(id, request, metadata, token))), "SettingsFinancePolicyRetireApprovalV1");
        CommandContract(group.MapPost("/cash-count-differences/{id:guid}/retire", (Guid id, RetireSettingsFinancePolicyV1 request, HttpContext context,
            [FromServices] ISettingsFinancePolicyAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.RetireCashDifferenceAsync(id, request, metadata, token))), "SettingsFinancePolicyRetireCashDifferenceV1");
        ReadContract(group.MapPost("/approval-matrices/preview", (PreviewSettingsFinanceApprovalPolicyV1 request, HttpContext context,
            [FromServices] ISettingsFinancePolicyPreviewV1? service, CancellationToken token) =>
            ReadAsync(context, service is null ? null : async () => (object)await service.PreviewApprovalAsync(request, token))).WithName("SettingsFinancePolicyPreviewApprovalV1").Produces<SettingsFinanceApprovalPreviewV1>());
        ReadContract(group.MapPost("/cash-count-differences/preview", (PreviewSettingsCashCountDifferencePolicyV1 request, HttpContext context,
            [FromServices] ISettingsFinancePolicyPreviewV1? service, CancellationToken token) =>
            ReadAsync(context, service is null ? null : async () => (object)await service.PreviewCashDifferenceAsync(request, token))).WithName("SettingsFinancePolicyPreviewCashDifferenceV1").Produces<SettingsCashCountDifferencePreviewV1>());
        MapCollateralPolicies(endpoints);
        return endpoints;
    }

    private static RouteHandlerBuilder ReadContract(RouteHandlerBuilder builder) => builder
        .ProducesErpBadRequest(SettingsFinancePolicyCodesV1.Invalid)
        .ProducesErpServiceUnavailable(SettingsFinancePolicyCodesV1.Unavailable).ProducesErpAuthenticatedProblems();
    private static RouteHandlerBuilder CommandContract(RouteHandlerBuilder builder, string name) => ReadContract(builder.WithName(name)
        .WithIdempotencyContract(204, 409).Produces(204).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
        .ProducesErpNotFound(SettingsFinancePolicyCodesV1.NotFound)
        .ProducesErpConflict(SettingsFinancePolicyCodesV1.VersionConflict, SettingsFinancePolicyCodesV1.IdempotencyConflict,
            SettingsFinancePolicyCodesV1.ReferenceChanged, SettingsFinancePolicyCodesV1.WindowConflict));

    private static Task<IResult> GetAsync(HttpContext context, [FromServices] ISettingsFinancePolicyAdministrationV1? service, CancellationToken token) =>
        ReadAsync(context, service is null ? null : async () => (object)await service.GetAsync(token));

    private static async Task<IResult> ReadAsync(HttpContext context, Func<Task<object>>? read)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (context.Request.Query.Count != 0) return Invalid();
        if (read is null) return Unavailable();
        try { return Results.Ok(await read()); }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (SettingsPrerequisiteException exception) { return From(exception); }
    }

    private static async Task<IResult> WriteAsync(HttpContext context, ICurrentUser actor, ICompanyContext company,
        ICorrelationContext correlation, Func<SettingsCommandMetadataV1, Task<SettingsFinancePolicyCommandResultV1>>? write)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (context.Request.Query.Count != 0 || !context.Request.Headers.TryGetValue("Idempotency-Key", out var values)
            || values.Count != 1 || values[0]?.Trim() is not { Length: > 0 and <= 128 } key
            || key.Contains(',', StringComparison.Ordinal) || !company.IsResolved || !correlation.IsResolved
            || string.IsNullOrWhiteSpace(actor.UserId)) return Invalid();
        if (write is null) return Unavailable();
        try
        {
            var metadata = new SettingsCommandMetadataV1(company.CompanyId, actor.UserId, correlation.CorrelationId,
                correlation.CausationId, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, key);
            var result = await write(metadata);
            if (result.IsReplay)
            {
                context.Response.Headers["Idempotency-Replayed"] = "true";
                context.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
            }
            return result.Answer.StatusCode == 204 ? Results.NoContent() : Results.Problem(statusCode: result.Answer.StatusCode,
                title: result.Answer.Title, detail: result.Answer.Detail, extensions: new Dictionary<string, object?> { ["code"] = result.Answer.Code });
        }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (SettingsPrerequisiteException exception) { return From(exception); }
    }
    private static IResult From(SettingsPrerequisiteException exception) => exception.Code == SettingsFinancePolicyCodesV1.AccessDenied
        ? Results.Problem(statusCode: 403) : Unavailable();
    private static IResult Unavailable() => Results.Problem(statusCode: 503, title: "Finans politikaları kullanılamıyor.");
    private static IResult Invalid() => Results.Problem(statusCode: 400, title: "İstek geçersiz.",
        extensions: new Dictionary<string, object?> { ["code"] = SettingsFinancePolicyCodesV1.Invalid });
}
