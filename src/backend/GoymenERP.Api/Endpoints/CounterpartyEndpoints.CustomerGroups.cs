using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-D57 customer (price) group on the Cari card (Sinan, 2026-10-27). Under the Counterparty group, so the
/// client-contract filter, company context and private no-store apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/customer-groups</c> — the active company's group list (passive ones included) and whether this reader
/// may manage it.</item>
/// <item><c>POST …/customer-groups</c> — create a group with a caller-chosen UUID (204).</item>
/// <item><c>PUT …/customer-groups/{id}/name</c>, <c>PUT …/customer-groups/{id}/active-state</c> — rename, deactivate or
/// reactivate (204). A group is never deleted.</item>
/// <item><c>GET …/{partyId}/company-account/customer-group</c> — the active company's account and its group.</item>
/// <item><c>PUT …/{partyId}/company-account/customer-group</c> — assign a group of the same company, or clear it with
/// an explicit null (204).</item>
/// </list>
/// Commands carry one Idempotency-Key and the record's version as decimal text; the stored terminal answers replay for
/// the same key and body.
/// </summary>
public static partial class CounterpartyEndpoints
{
    private const string CustomerGroupsPath = "/customer-groups";

    private static readonly string[] CustomerGroupCommon409Codes =
    [
        CounterpartyProblemCodesV1.IdempotencyConflict,
        CounterpartyClientContract.RequiredCode,
        CounterpartyClientContract.UnsupportedCode,
    ];

    private static void MapCustomerGroupEndpoints(RouteGroupBuilder group)
    {
        group.MapGet(CustomerGroupsPath, async (
            ICounterpartyCustomerGroupService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Müşteri grubu isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                return Results.Ok(await service.GetCustomerGroupsAsync(cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .WithName("GetCounterpartyCustomerGroupsV1")
            .Produces<CounterpartyCustomerGroupList>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPost(CustomerGroupsPath, async (
            CreateCounterpartyCustomerGroup command, ICounterpartyCustomerGroupService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
            await ExecuteCustomerGroupCommandAsync(context, user, company, correlation, metadata =>
                service.CreateCustomerGroupAsync(command, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(CounterpartyCustomerGroupPermissionsV1.Manage)
            .WithName("CreateCounterpartyCustomerGroupV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status409Conflict)
            .Accepts<CreateCounterpartyCustomerGroup>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpConflict([
                .. CustomerGroupCommon409Codes,
                CounterpartyProblemCodesV1.CustomerGroupExists,
                CounterpartyProblemCodesV1.CustomerGroupNameTaken,
            ])
            .ProducesErpAuthenticatedProblems();

        group.MapPut(CustomerGroupsPath + "/{customerGroupId:guid}/name", async (
            Guid customerGroupId, RenameCounterpartyCustomerGroup command, ICounterpartyCustomerGroupService service,
            ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
            await ExecuteCustomerGroupCommandAsync(context, user, company, correlation, metadata =>
                service.RenameCustomerGroupAsync(customerGroupId, command, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(CounterpartyCustomerGroupPermissionsV1.Manage)
            .RequiresNonEmptyGuidPathParameter("customerGroupId")
            .WithName("RenameCounterpartyCustomerGroupV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<RenameCounterpartyCustomerGroup>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict([
                .. CustomerGroupCommon409Codes,
                CounterpartyProblemCodesV1.VersionConflict,
                CounterpartyProblemCodesV1.CustomerGroupNameTaken,
            ])
            .ProducesErpAuthenticatedProblems();

        group.MapPut(CustomerGroupsPath + "/{customerGroupId:guid}/active-state", async (
            Guid customerGroupId, SetCounterpartyCustomerGroupActiveState command,
            ICounterpartyCustomerGroupService service, ICurrentUser user, ICompanyContext company,
            ICorrelationContext correlation, HttpContext context, CancellationToken cancellationToken) =>
            await ExecuteCustomerGroupCommandAsync(context, user, company, correlation, metadata =>
                service.SetCustomerGroupActiveStateAsync(customerGroupId, command, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(CounterpartyCustomerGroupPermissionsV1.Manage)
            .RequiresNonEmptyGuidPathParameter("customerGroupId")
            .WithName("SetCounterpartyCustomerGroupActiveStateV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<SetCounterpartyCustomerGroupActiveState>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict([.. CustomerGroupCommon409Codes, CounterpartyProblemCodesV1.VersionConflict])
            .ProducesErpAuthenticatedProblems();

        group.MapGet("/{partyId:guid}/company-account/customer-group", async (
            Guid partyId, ICounterpartyCustomerGroupService service, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari müşteri grubu isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.GetAccountCustomerGroupAsync(partyId, cancellationToken);
                return view is null ? NotFound() : Results.Ok(view);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("GetCounterpartyAccountCustomerGroupV1")
            .Produces<CounterpartyAccountCustomerGroupView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{partyId:guid}/company-account/customer-group", async (
            Guid partyId, AssignCounterpartyCustomerGroup command, ICounterpartyCustomerGroupService service,
            ICurrentUser user, ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
            await ExecuteCustomerGroupCommandAsync(context, user, company, correlation, metadata =>
                service.AssignAccountCustomerGroupAsync(partyId, command, metadata, cancellationToken)))
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(CounterpartyCustomerGroupPermissionsV1.Assign)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("AssignCounterpartyAccountCustomerGroupV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity)
            .Accepts<AssignCounterpartyCustomerGroup>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict([
                .. CustomerGroupCommon409Codes,
                CounterpartyProblemCodesV1.VersionConflict,
                CounterpartyProblemCodesV1.CustomerGroupInactive,
            ])
            .ProducesErpUnprocessableEntity(CounterpartyProblemCodesV1.CustomerGroupNotFound)
            .ProducesErpAuthenticatedProblems();
    }

    private static async Task<IResult> ExecuteCustomerGroupCommandAsync(
        HttpContext context,
        ICurrentUser user,
        ICompanyContext company,
        ICorrelationContext correlation,
        Func<CounterpartyCommandMetadata, Task<CounterpartyCommandExecution>> execute)
    {
        if (context.Request.Query.Count != 0)
        {
            return ValidationProblem("Müşteri grubu isteği geçersiz.",
                "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
        }

        if (!TryReadCommandMetadata(context, user, company, correlation, out var metadata))
        {
            return InvalidCommandMetadata();
        }

        CounterpartyCommandExecution execution;
        try
        {
            execution = await execute(metadata);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem("Müşteri grubu isteği geçersiz.", exception.Message);
        }

        ApplyReplayHeaders(context, execution);
        return ToCustomerGroupHttpResult(execution.Response);
    }

    /// <summary>
    /// The stored answer as HTTP. The version conflict carries no <c>conflict</c> member: an Int64 version must not
    /// travel as a JavaScript number, and the fresh GET supplies the current version as text.
    /// </summary>
    private static IResult ToCustomerGroupHttpResult(CounterpartyCommandResponse response)
    {
        if (response.Result == CounterpartyCommandResult.Succeeded
            && response.StatusCode == StatusCodes.Status204NoContent)
        {
            return Results.NoContent();
        }

        var code = CounterpartyProblemCodesV1.RequiredForProblem(response.Result);
        var expectedStatus = response.Result switch
        {
            CounterpartyCommandResult.NotFound => StatusCodes.Status404NotFound,
            CounterpartyCommandResult.CustomerGroupNotFound => StatusCodes.Status422UnprocessableEntity,
            CounterpartyCommandResult.VersionConflict
                or CounterpartyCommandResult.IdempotencyConflict
                or CounterpartyCommandResult.CustomerGroupExists
                or CounterpartyCommandResult.CustomerGroupNameTaken
                or CounterpartyCommandResult.CustomerGroupInactive => StatusCodes.Status409Conflict,
            _ => throw new InvalidOperationException($"Müşteri grubu komutu bu sonucu üretmez: {response.Result}."),
        };
        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException("Müşteri grubu terminal sonucu HTTP durumuyla eşleşmiyor.");
        }

        return CounterpartyProblem(response.StatusCode, code, response.Title, response.Detail);
    }
}
