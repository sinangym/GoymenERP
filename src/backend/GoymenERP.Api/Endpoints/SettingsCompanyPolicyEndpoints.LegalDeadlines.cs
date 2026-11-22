using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): the legal deadline calendar. <c>GET …/legal-deadline-calendar</c> reads the version in force
/// and the history; <c>POST …/legal-deadline-calendar/versions</c> records a new version (K3 path: Idempotency-Key, expected
/// version, reason, audit). Both on <see cref="SettingsLegalDeadlinePermissionsV1.CalendarManage"/>. Nothing is seeded.
/// </summary>
public static partial class SettingsCompanyPolicyEndpoints
{
    private static void MapLegalDeadlineCalendar(RouteGroupBuilder group)
    {
        group.MapGet("/legal-deadline-calendar", async (HttpContext context, ISettingsLegalDeadlineCalendarAdministrationV1 service,
            CancellationToken cancellationToken) => await ReadAsync(context, () => service.GetLegalDeadlineCalendarAsync(cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(SettingsLegalDeadlinePermissionsV1.CalendarManage)
            .WithName("GetSettingsLegalDeadlineCalendarV1").Produces<LegalDeadlineCalendarViewV1>()
            .ProducesErpBadRequest(CompanyPolicyProblemCodesV1.RequestInvalid)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
        group.MapPost("/legal-deadline-calendar/versions", async (RecordLegalDeadlineCalendarRequestV1 request,
            ISettingsLegalDeadlineCalendarAdministrationV1 service, ICurrentUser currentUser, ICompanyContext companyContext,
            ICorrelationContext correlation, HttpContext context, CancellationToken cancellationToken) =>
                await CommandAsync(context, currentUser, companyContext, correlation,
                    metadata => service.RecordLegalDeadlineCalendarAsync(request, metadata, cancellationToken)))
            .RequireCompanyContext().RequireAuthorization(SettingsLegalDeadlinePermissionsV1.CalendarManage)
            .WithName("RecordSettingsLegalDeadlineCalendarV1").WithIdempotencyContract(204, 409)
            .Accepts<RecordLegalDeadlineCalendarRequestV1>("application/json").Produces(204)
            .ProducesErpBadRequest(CommandBadRequestCodes).AllowsCodeLessErpBadRequestBody().ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(CompanyPolicyProblemCodesV1.IdempotencyConflict, CompanyPolicyProblemCodesV1.VersionConflict)
            .ProducesErpServiceUnavailable(CompanyPolicyProblemCodesV1.ProviderUnavailable).ProducesErpAuthenticatedProblems();
    }
}
