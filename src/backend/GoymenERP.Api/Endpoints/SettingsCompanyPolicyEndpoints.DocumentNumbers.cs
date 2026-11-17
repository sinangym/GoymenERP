using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

public static partial class SettingsCompanyPolicyEndpoints
{
    private static void MapDocumentNumberPolicies(RouteGroupBuilder group)
    {
        group.MapGet("/document-number-series/{series:int}", async (int series, HttpContext context,
            ISettingsDocumentNumberSeriesAdministrationV1 service, CancellationToken ct) =>
                Enum.IsDefined(typeof(SettingsDocumentSeriesV1), series) ? await ReadAsync(context, () => service.GetFormatHistoryAsync((SettingsDocumentSeriesV1)series, ct))
                    : Problem(400, CompanyPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", "Belge serisi geçersiz."))
            .RequireCompanyContext().RequireAuthorization(SettingsDocumentNumberSeriesPermissionsV1.Manage)
            .WithName("GetSettingsDocumentNumberFormatV1").Produces<SettingsDocumentNumberFormatViewV1>()
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid).ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapPost("/document-number-series/versions", async (RecordSettingsDocumentNumberFormatV1 body,
            ISettingsDocumentNumberSeriesAdministrationV1 service, HttpContext context, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, CancellationToken ct) =>
                Enum.IsDefined(body.Series) ? await CommandAsync(context, user, company, correlation, metadata => service.RecordFormatAsync(body, metadata, ct))
                    : Problem(400, CompanyPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", "Belge serisi geçersiz."))
            .RequireCompanyContext().RequireAuthorization(SettingsDocumentNumberSeriesPermissionsV1.Manage)
            .WithName("RecordSettingsDocumentNumberFormatV1").WithIdempotencyContract(204, 409)
            .Accepts<RecordSettingsDocumentNumberFormatV1>("application/json").Produces(204)
            .ProducesErpBadRequest(CommandBadRequestCodes).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(SettingsDocumentNumberSeriesProblemCodesV1.VersionConflict, SettingsDocumentNumberSeriesProblemCodesV1.IdempotencyConflict)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapGet("/document-number-series/{series:int}/gaps/{year:int}", async (int series, int year,
            HttpContext context, ISettingsDocumentNumberSeriesAdministrationV1 service, CancellationToken ct) =>
                Enum.IsDefined(typeof(SettingsDocumentSeriesV1), series) && year is >= 1 and <= 9999
                    ? await ReadAsync(context, () => service.GetGapsAsync((SettingsDocumentSeriesV1)series, year, ct))
                    : Problem(400, CompanyPolicyProblemCodesV1.RequestInvalid, "İstek geçersiz.", "Belge serisi veya yıl geçersiz."))
            .RequireCompanyContext().RequireAuthorization(SettingsDocumentNumberSeriesPermissionsV1.Manage)
            .WithName("GetSettingsDocumentNumberGapsV1").Produces<IReadOnlyList<SettingsDocumentNumberGapV1>>()
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid).ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
    }
}
