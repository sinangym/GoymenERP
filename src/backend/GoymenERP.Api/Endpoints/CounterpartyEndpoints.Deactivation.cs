using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// ALS-D99 / ALS-D100 (Sinan, 2026-11-15) Cari deactivation warning and group-wide deactivation. Under the Counterparty
/// group, so the client-contract filter, company context and private no-store apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/{partyId}/company-account/deactivation-warning</c> — the selected company's warning list (Finance open
/// balance, Sales and Purchasing open documents, read from the owners' ports; counts only). A line the owner could not
/// answer reads <c>unavailable</c>. Same permission as the per-company status change.</item>
/// <item><c>GET …/{partyId}/group-deactivation-preview</c> — every reachable company's account with its warning and
/// whether the separate group permission is held there.</item>
/// <item><c>POST …/{partyId}/group-deactivation</c> — deactivate in each named company as its own command (200 with a
/// per-company outcome list). One Idempotency-Key; each company stores its answer under a key derived from it.</item>
/// </list>
/// The warning never blocks: when it is not clear, the command needs <c>exposureWarningAcknowledged</c>.
/// </summary>
public static partial class CounterpartyEndpoints
{
    private static void MapDeactivationEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{partyId:guid}/company-account/deactivation-warning", async (
            Guid partyId,
            ICounterpartyDeactivationExposureReader reader,
            IErpAuthorizationService authorizationService,
            ICurrentUser currentUser,
            ICompanyContext companyContext,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari pasife alma uyarısı isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            var scope = await authorizationService.GetPermissionScopeAsync(
                ErpPermissions.CounterpartyAccountsStatusUpdate,
                companyContext.CompanyId,
                cancellationToken);
            var actorId = currentUser.UserId;
            if (!scope.IsGranted || !scope.IsCompanyWide || string.IsNullOrWhiteSpace(actorId))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }

            var view = await reader.ReadAsync(
                companyContext.CompanyId,
                actorId,
                partyId,
                cancellationToken);
            return view is null ? NotFound() : Results.Ok(view);
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyAccountsStatusUpdate)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("GetCounterpartyDeactivationWarningV1")
            .Produces<CounterpartyDeactivationExposureView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{partyId:guid}/group-deactivation-preview", async (
            Guid partyId,
            ICounterpartyGroupDeactivationService service,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari grup pasife alma önizlemesi geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.PreviewAsync(partyId, cancellationToken);
                return view is null ? NotFound() : Results.Ok(view);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesGroupDeactivate)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("GetCounterpartyGroupDeactivationPreviewV1")
            .Produces<CounterpartyGroupDeactivationPreviewView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{partyId:guid}/group-deactivation", async (
            Guid partyId,
            GroupDeactivateCounterparty command,
            ICounterpartyGroupDeactivationService service,
            ICurrentUser currentUser,
            ICompanyContext companyContext,
            ICorrelationContext correlationContext,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari grup pasife alma isteği geçersiz.",
                    "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
            }

            if (!TryReadCommandMetadata(context, currentUser, companyContext, correlationContext, out var metadata))
            {
                return InvalidCommandMetadata();
            }

            try
            {
                var result = await service.DeactivateAsync(partyId, command, metadata, cancellationToken);
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return ValidationProblem("Cari grup pasife alma isteği geçersiz.", exception.Message);
            }
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesGroupDeactivate)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("GroupDeactivateCounterpartyV1")
            // Each company stores and replays its own answer under the derived key; the list is rebuilt from them.
            .WithIdempotencyContract(StatusCodes.Status200OK)
            .Accepts<GroupDeactivateCounterparty>("application/json")
            .Produces<CounterpartyGroupDeactivationResultView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(
                CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();
    }
}
