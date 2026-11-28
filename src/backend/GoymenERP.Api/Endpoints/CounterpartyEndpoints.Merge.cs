using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// F-027 / EK-18 #76-#77 duplicate party merge. Under the Counterparty group, so the client-contract filter, company
/// context and private no-store apply as for every Cari route.
/// <list type="bullet">
/// <item><c>GET …/{partyId}/merge</c> — the surviving card's code, name and PARTY version as decimal text, the same-VKN
/// duplicate candidates held by the active company (each with its version as text), the target when this party was
/// merged away, and whether this reader may approve a merge.</item>
/// <item><c>PUT …/{partyId}/merge</c> — the group manager's approval: the duplicate named in the body is merged into the
/// path's party (204). Both party versions are fenced; the approver needs the merge permission in every company holding
/// a card of either party. One Idempotency-Key; the stored terminal answers (204, 404, 409) replay for the same key and
/// body. There is no unmerge (#77).</item>
/// </list>
/// </summary>
public static partial class CounterpartyEndpoints
{
    private static void MapPartyMergeEndpoints(RouteGroupBuilder group)
    {
        MapPartyMergePurposePreview(group);
        group.MapGet("/{partyId:guid}/merge", async (
            Guid partyId, ICounterpartyPartyMergeService service,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari birleştirme isteği geçersiz.",
                    "Şirket kapsamı yalnız X-Company-Id başlığından belirlenir; sorgu parametresi kabul edilmez.");
            }

            try
            {
                var view = await service.GetMergeAsync(partyId, cancellationToken);
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
            .WithName("GetCounterpartyPartyMergeV1")
            .Produces<CounterpartyPartyMergeView>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesErpAuthenticatedProblems();

        group.MapPost("/{partyId:guid}/merge/preview", async (
            Guid partyId, PreviewCounterpartyPartyMergeV1 command, ICounterpartyPartyMergeService service,
            HttpContext context, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
                return ValidationProblem("Birleştirme önizlemesi geçersiz.", "Sorgu parametresi kabul edilmez.");
            try
            {
                var answer = await service.PreviewMergeAsync(partyId, command, token);
                return answer switch
                {
                    { Status: CounterpartyPartyMergePreviewStatusV1.Found, Preview: { } preview } => Results.Ok(preview),
                    { Status: CounterpartyPartyMergePreviewStatusV1.NotFound, Preview: null } => NotFound(),
                    { Status: CounterpartyPartyMergePreviewStatusV1.Changed, Preview: null } => Results.Problem(statusCode: 409, title: "Birleştirme önizlemesi değişti."),
                    _ => Results.Problem(statusCode: 503, title: "Birleştirme önizlemesi kullanılamıyor."),
                };
            }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch (ArgumentException error) { return ValidationProblem("Birleştirme önizlemesi geçersiz.", error.Message); }
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesMergeApprove)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("PreviewCounterpartyPartyMergeV1")
            .Accepts<PreviewCounterpartyPartyMergeV1>("application/json")
            .Produces<CounterpartyPartyMergeImpactPreviewV1>(StatusCodes.Status200OK)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesErpAuthenticatedProblems();

        group.MapPut("/{partyId:guid}/merge", async (
            Guid partyId, ApproveCounterpartyPartyMerge command,
            ICounterpartyPartyMergeService service, ICurrentUser user,
            ICompanyContext company, ICorrelationContext correlation, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (context.Request.Query.Count != 0)
            {
                return ValidationProblem("Cari birleştirme isteği geçersiz.",
                    "Sorgu parametresi kabul edilmez; şirket kapsamı sunucu tarafından belirlenir.");
            }

            if (!TryReadCommandMetadata(context, user, company, correlation, out var metadata))
            {
                return InvalidCommandMetadata();
            }

            CounterpartyCommandExecution execution;
            try
            {
                if (context.Request.Headers.TryGetValue(MergeSeenImpactHeader, out var seen))
                {
                    if (seen.Count != 1 || !IsMergeSeenImpactDigest(seen[0]))
                        return ValidationProblem(CounterpartyMergePurposeTexts.InvalidRequestTitle, CounterpartyMergePurposeTexts.InvalidImpactDetail);
                    var purpose = context.RequestServices.GetService<ICounterpartyPartyMergePurposeV1>();
                    if (purpose is null) return Results.Problem(statusCode: 503);
                    var answer = await purpose.ApprovePurposeAsync(partyId, command, seen[0]!, metadata,
                        token => RequireMergeHttpContext(user, company, correlation, metadata.CompanyId, metadata.ActorId, metadata.CorrelationId, token), cancellationToken);
                    if (answer is { Status: CounterpartyPartyMergePurposeStatusV1.Changed, Execution: null })
                        return CounterpartyProblem(409, CounterpartyProblemCodesV1.PartyMergeImpactChanged,
                            CounterpartyMergePurposeTexts.ChangedTitle, CounterpartyMergePurposeTexts.ChangedDetail);
                    if (answer is not { Status: CounterpartyPartyMergePurposeStatusV1.Executed, Execution: { } original })
                        return Results.Problem(statusCode: 503);
                    execution = original;
                }
                else execution = await service.ApproveMergeAsync(partyId, command, metadata, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException exception)
            {
                return ValidationProblem("Cari birleştirme isteği geçersiz.", exception.Message);
            }

            ApplyReplayHeaders(context, execution);
            return ToPartyMergeHttpResult(execution.Response);
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesMergeApprove)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("ApproveCounterpartyPartyMergeV1")
            .WithIdempotencyContract(StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict)
            .Accepts<ApproveCounterpartyPartyMerge>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed,
                CounterpartyProblemCodesV1.CommandMetadataInvalid)
            .ProducesErpUnsupportedMediaType()
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesErpConflict(CounterpartyProblemCodesV1.VersionConflict,
                CounterpartyProblemCodesV1.IdempotencyConflict,
                CounterpartyProblemCodesV1.PartyAlreadyMerged,
                CounterpartyProblemCodesV1.PartyMergeTargetMerged,
                CounterpartyProblemCodesV1.PartyMergeTaxIdentityMismatch,
                CounterpartyProblemCodesV1.PartyMergeImpactChanged,
                CounterpartyClientContract.RequiredCode, CounterpartyClientContract.UnsupportedCode)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesErpAuthenticatedProblems();
    }

    /// <summary>
    /// The stored merge answer as HTTP. No conflict carries an Int64 <c>conflict</c> member: two party versions are
    /// fenced and the fresh GET supplies both as text.
    /// </summary>
    private static IResult ToPartyMergeHttpResult(CounterpartyCommandResponse response)
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
            CounterpartyCommandResult.VersionConflict
                or CounterpartyCommandResult.IdempotencyConflict
                or CounterpartyCommandResult.PartyAlreadyMerged
                or CounterpartyCommandResult.PartyMergeTargetMerged
                or CounterpartyCommandResult.PartyMergeTaxIdentityMismatch => StatusCodes.Status409Conflict,
            _ => throw new InvalidOperationException($"Cari birleştirme komutu bu sonucu üretmez: {response.Result}."),
        };
        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException("Cari birleştirme terminal sonucu HTTP durumuyla eşleşmiyor.");
        }

        return CounterpartyProblem(response.StatusCode, code, response.Title, response.Detail);
    }
}
