using GoymenERP.Api.Multitenancy;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static partial class SettingsFinancePolicyEndpoints
{
    private static void MapCollateralPolicies(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(Path).WithTags("Settings").RequireAuthorization().RequireCompanyContext();
        ReadContract(group.MapGet("/bank-institutions", (HttpContext context, [FromServices] ISettingsBankInstitutionAdministrationV1? service, CancellationToken token) =>
            ReadAsync(context, service is null ? null : async () => (object)await service.GetBankInstitutionsAsync(token))).WithName("SettingsBankInstitutionsGetV1").Produces<SettingsBankInstitutionsWorkspaceV1>());
        CommandContract(group.MapPost("/bank-institutions", (PrepareSettingsBankInstitutionsV1 request, HttpContext context,
            [FromServices] ISettingsBankInstitutionAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.PrepareBankInstitutionsAsync(request, metadata, token))), "SettingsBankInstitutionsPrepareV1");
        CommandContract(group.MapPost("/bank-institutions/{id:guid}/publish", (Guid id, PublishSettingsFinancePolicyV1 request, HttpContext context,
            [FromServices] ISettingsBankInstitutionAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.PublishBankInstitutionsAsync(id, request, metadata, token))), "SettingsBankInstitutionsPublishV1");
        CommandContract(group.MapPost("/bank-institutions/{id:guid}/retire", (Guid id, RetireSettingsFinancePolicyV1 request, HttpContext context,
            [FromServices] ISettingsBankInstitutionAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.RetireBankInstitutionsAsync(id, request, metadata, token))), "SettingsBankInstitutionsRetireV1");
        ReadContract(group.MapGet("/collateral-rates", (HttpContext context, [FromServices] ISettingsCollateralRateAdministrationV1? service, CancellationToken token) =>
            ReadAsync(context, service is null ? null : async () => (object)await service.GetCollateralRatesAsync(token))).WithName("SettingsCollateralRatesGetV1").Produces<SettingsCollateralRatesWorkspaceV1>());
        CommandContract(group.MapPost("/collateral-rates", (PrepareSettingsCollateralRateV1 request, HttpContext context,
            [FromServices] ISettingsCollateralRateAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.PrepareCollateralRateAsync(request, metadata, token))), "SettingsCollateralRatesPrepareV1");
        CommandContract(group.MapPost("/collateral-rates/{id:guid}/publish", (Guid id, PublishSettingsFinancePolicyV1 request, HttpContext context,
            [FromServices] ISettingsCollateralRateAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.PublishCollateralRateAsync(id, request, metadata, token))), "SettingsCollateralRatesPublishV1");
        CommandContract(group.MapPost("/collateral-rates/{id:guid}/retire", (Guid id, RetireSettingsFinancePolicyV1 request, HttpContext context,
            [FromServices] ISettingsCollateralRateAdministrationV1? service, ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, CancellationToken token) =>
            WriteAsync(context, user, company, correlation, service is null ? null : metadata => service.RetireCollateralRateAsync(id, request, metadata, token))), "SettingsCollateralRatesRetireV1");
        ReadContract(group.MapPost("/collateral-rates/preview", (PreviewSettingsCollateralRateV1 request, HttpContext context,
            [FromServices] ISettingsCollateralRateAdministrationV1? service, CancellationToken token) =>
            ReadAsync(context, service is null ? null : async () => (object)await service.PreviewCollateralRateAsync(request, token))).WithName("SettingsCollateralRatePreviewV1").Produces<SettingsCollateralRateReadV1>());
    }
}
