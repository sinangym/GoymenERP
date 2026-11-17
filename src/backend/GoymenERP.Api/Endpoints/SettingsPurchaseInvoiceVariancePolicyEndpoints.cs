using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>ALS-D8/D15/D63 API-97. Company from the server context only; no seeds or automatic publication.</summary>
public static class SettingsPurchaseInvoiceVariancePolicyEndpoints
{
    public const string Path = "/api/v1/settings/purchase-invoice-variance-policy";

    public static IEndpointRouteBuilder MapSettingsPurchaseInvoiceVariancePolicyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).WithTags("Settings").RequireAuthorization()
            .RequireCompanyContext().RequireAuthorization(SettingsPurchaseInvoiceVariancePolicyPermissionsV1.Manage);
        group.MapGet("", ReadAsync).WithName("GetSettingsPurchaseInvoiceVariancePolicyV1")
            .Produces<PurchaseInvoiceVariancePolicyViewV1>()
            .ProducesErpBadRequest(PurchaseInvoiceVariancePolicyCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();
        group.MapPost("/publish", PublishAsync).WithName("PublishSettingsPurchaseInvoiceVariancePolicyV1")
            .Accepts<PublishPurchaseInvoiceVariancePolicyRequestV1>("application/json")
            .WithIdempotencyContract(204, 409).Produces(204)
            .ProducesErpBadRequest(PurchaseInvoiceVariancePolicyCodesV1.RequestInvalid)
            .AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(PurchaseInvoiceVariancePolicyCodesV1.VersionConflict, PurchaseInvoiceVariancePolicyCodesV1.IdempotencyConflict)
            .ProducesErpServiceUnavailable(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable)
            .ProducesErpAuthenticatedProblems();
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(HttpContext context,
        [FromServices] ISettingsPurchaseInvoiceVariancePolicyAdministrationV1? service, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (context.Request.Query.Count != 0) return Invalid();
        if (service is null) return Unavailable();
        try { return Results.Ok(await service.ReadAsync(cancellationToken)); }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (SettingsPrerequisiteException exception) { return FromPrerequisite(exception); }
    }

    private static async Task<IResult> PublishAsync(PublishPurchaseInvoiceVariancePolicyRequestV1 request,
        HttpContext context, ICurrentUser currentUser, ICompanyContext companyContext, ICorrelationContext correlation,
        [FromServices] ISettingsPurchaseInvoiceVariancePolicyAdministrationV1? service, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        if (context.Request.Query.Count != 0 || !TryMetadata(context, currentUser, companyContext, correlation, out var metadata))
            return Invalid();
        if (service is null) return Unavailable();
        try
        {
            var result = await service.PublishAsync(request, metadata, cancellationToken);
            if (result.IsReplay)
            {
                context.Response.Headers["Idempotency-Replayed"] = "true";
                context.Response.Headers["Idempotency-Original-Correlation-Id"] = result.OriginalCorrelationId;
            }
            return result.Answer.StatusCode == 204 ? Results.NoContent()
                : Results.Problem(statusCode: result.Answer.StatusCode, title: result.Answer.Title, detail: result.Answer.Detail,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Answer.Code });
        }
        catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
        catch (SettingsPrerequisiteException exception) { return FromPrerequisite(exception); }
    }

    private static bool TryMetadata(HttpContext context, ICurrentUser actor, ICompanyContext company,
        ICorrelationContext correlation, out SettingsCommandMetadataV1 metadata)
    {
        metadata = null!;
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count != 1
            || values[0]?.Trim() is not { Length: > 0 and <= 128 } key || key.Contains(',', StringComparison.Ordinal)
            || !correlation.IsResolved || string.IsNullOrWhiteSpace(actor.UserId) || !company.IsResolved) return false;
        metadata = new(company.CompanyId, actor.UserId, correlation.CorrelationId, correlation.CausationId,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, key);
        return true;
    }

    private static IResult Invalid() => Results.Problem(statusCode: 400, title: "İstek geçersiz.",
        detail: "Sorgu parametresi kabul edilmez; komut için tek geçerli Idempotency-Key gerekir.",
        extensions: new Dictionary<string, object?> { ["code"] = PurchaseInvoiceVariancePolicyCodesV1.RequestInvalid });
    private static IResult Unavailable() => Results.Problem(statusCode: 503, title: "Ayarlar kullanılamıyor.",
        detail: "Fatura fark toleransı şu anda okunamıyor veya yayımlanamıyor.",
        extensions: new Dictionary<string, object?> { ["code"] = PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable });
    private static IResult FromPrerequisite(SettingsPrerequisiteException exception) =>
        exception.Code == PurchaseInvoiceVariancePolicyCodesV1.AccessDenied ? Results.Problem(statusCode: 403) : Unavailable();
}
