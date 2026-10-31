using System.Globalization;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// API-41 / API-42 (Satış hazırlık planı v1 §3.4, UI-80; KPI No 124, No 25, SAT-D25): the three read-only Sales reports — the open
/// order quantities, the lost quotes and the invoice-to-order price variance — under <c>sales.reports.read</c>.
/// <see cref="ISalesReportService"/> measures the names; the route binds only the server context and the strict query (every name
/// single, dates exact <c>yyyy-MM-dd</c>). Nothing is written in any module.
/// </summary>
public static partial class SalesEndpoints
{
    public const string ReportOpenOrdersPath = SalesReportApiV1.OpenOrdersPath;
    public const string ReportLostOpportunitiesPath = SalesReportApiV1.LostOpportunitiesPath;
    public const string ReportPriceVariancePath = SalesReportApiV1.PriceVariancePath;

    private static readonly HashSet<string> OpenOrderReportQueryNames =
        new(["from", "to", "companyAccountId", "pageSize", "cursor"], StringComparer.Ordinal);
    private static readonly HashSet<string> LostOpportunityReportQueryNames =
        new(["from", "to", "companyAccountId", "reasonId", "pageSize", "cursor"], StringComparer.Ordinal);
    private static readonly HashSet<string> PriceVarianceReportQueryNames =
        new(["from", "to", "companyAccountId", "pageSize", "cursor"], StringComparer.Ordinal);

    private static void MapReportEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ReportOpenOrdersPath, async (
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] Guid? companyAccountId,
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            HttpContext httpContext,
            [FromServices] ISalesReportService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, OpenOrderReportQueryNames, Array.Empty<string>())
                || !TryReportDate(from, out var fromDate) || !TryReportDate(to, out var toDate)
                || !IsReportPage(pageSize, cursor))
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetOpenOrderReportAsync(new SalesOpenOrderReportQuery(fromDate, toDate, companyAccountId,
                    pageSize ?? SalesReportApiV1.DefaultPageSize, cursor), cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("GetSalesOpenOrderReportV1")
            .Produces<SalesOpenOrderReportResponse>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpUnprocessableEntity(SalesReportProblemCodesV1.ReportScopeTooLarge)
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(ReportLostOpportunitiesPath, async (
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] Guid? companyAccountId,
            [FromQuery] Guid? reasonId,
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            HttpContext httpContext,
            [FromServices] ISalesReportService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, LostOpportunityReportQueryNames, Array.Empty<string>())
                || !TryReportDate(from, out var fromDate) || !TryReportDate(to, out var toDate)
                || !IsReportPage(pageSize, cursor))
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetLostOpportunityReportAsync(new SalesLostOpportunityReportQuery(fromDate, toDate,
                    companyAccountId, reasonId, pageSize ?? SalesReportApiV1.DefaultPageSize, cursor), cancellationToken),
                    SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("GetSalesLostOpportunityReportV1")
            .Produces<SalesLostOpportunityReportResponse>()
            .ProducesErpBadRequest("validation_failed", SalesDraftProblemCodesV1.InvalidCursor)
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();

        endpoints.MapGet(ReportPriceVariancePath, async (
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] Guid? companyAccountId,
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            HttpContext httpContext,
            [FromServices] ISalesReportService service,
            ICorrelationContext correlation,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(httpContext.Response);
            if (!HasOnlySingleQuery(httpContext.Request.Query, PriceVarianceReportQueryNames, Array.Empty<string>())
                || !TryReportDate(from, out var fromDate) || !TryReportDate(to, out var toDate)
                || !IsReportPage(pageSize, cursor))
            {
                return DraftProblem(StatusCodes.Status400BadRequest, "validation_failed", correlation);
            }

            try
            {
                return Results.Json(await service.GetPriceVarianceReportAsync(new SalesPriceVarianceReportQuery(fromDate, toDate,
                    companyAccountId, pageSize ?? SalesReportApiV1.DefaultPageSize, cursor), cancellationToken), SalesDraftJson.Options);
            }
            catch (SalesDraftServiceException exception)
            {
                return DraftProblem(exception, correlation);
            }
        })
            .WithTags("Sales")
            .RequireAuthorization()
            .RequireCompanyContext()
            .WithName("GetSalesPriceVarianceReportV1")
            .Produces<SalesPriceVarianceReportResponse>()
            .ProducesErpBadRequest("validation_failed")
            .AllowsEmptyErpBadRequestBody()
            .ProducesErpServiceUnavailable()
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>An absent date is no bound; a present one must be exactly <c>yyyy-MM-dd</c>.</summary>
    private static bool TryReportDate(string? value, out DateOnly? date)
    {
        date = null;
        if (value is null)
        {
            return true;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        date = parsed;
        return true;
    }

    private static bool IsReportPage(int? pageSize, string? cursor) =>
        pageSize is null or >= 1 and <= SalesReportApiV1.MaximumPageSize && cursor is not { Length: 0 or > MaximumCursorLength };
}
