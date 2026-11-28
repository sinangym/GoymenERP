using GoymenERP.Api.Contracts;
using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static partial class CounterpartyEndpoints
{
    internal const string MergeSeenImpactHeader = "X-Counterparty-Merge-Seen-Impact";
    internal static bool IsMergeSeenImpactDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static Task RequireMergeHttpContext(ICurrentUser user, ICompanyContext company, ICorrelationContext correlation,
        Guid companyId, string actor, string correlationId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || user.UserId != actor || !company.IsResolved || company.CompanyId != companyId
            || !user.CanAccessCompany(companyId) || !correlation.IsResolved || correlation.CorrelationId != correlationId)
            throw new UnauthorizedAccessException();
        return Task.CompletedTask;
    }

    private static void MapPartyMergePurposePreview(RouteGroupBuilder group)
    {
        group.MapPost("/{partyId:guid}/merge/purpose-preview", async (
            Guid partyId, PreviewCounterpartyPartyMergeV1 command, ICounterpartyPartyMergePurposeV1 purpose,
            ICurrentUser user, ICompanyContext company, ICorrelationContext correlation,
            HttpContext context, CancellationToken token) =>
        {
            SetPrivateNoStore(context.Response.Headers);
            if (context.Request.Query.Count != 0)
                return ValidationProblem(CounterpartyMergePurposeTexts.InvalidPreviewTitle, CounterpartyMergePurposeTexts.QueryNotAccepted);
            var companyId = company.CompanyId; var actor = user.UserId; var correlationId = correlation.CorrelationId;
            if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(actor)) return Results.Problem(statusCode: 403);
            try
            {
                var answer = await purpose.PreviewPurposeAsync(partyId, command,
                    ct => RequireMergeHttpContext(user, company, correlation, companyId, actor, correlationId, ct), token);
                return answer switch
                {
                    { NativePreview: { Status: CounterpartyPartyMergePreviewStatusV1.Found, Preview: { } impact }, SeenImpactDigest: { } digest }
                        when IsMergeSeenImpactDigest(digest) => Results.Ok(new CounterpartyMergePurposePreviewResponseV1(impact, digest)),
                    { NativePreview: { Status: CounterpartyPartyMergePreviewStatusV1.NotFound, Preview: null }, SeenImpactDigest: null } => NotFound(),
                    { NativePreview: { Status: CounterpartyPartyMergePreviewStatusV1.Changed, Preview: null }, SeenImpactDigest: null }
                        => Results.Problem(statusCode: 409, title: CounterpartyMergePurposeTexts.PreviewChangedTitle),
                    _ => Results.Problem(statusCode: 503),
                };
            }
            catch (UnauthorizedAccessException) { return Results.Problem(statusCode: 403); }
            catch (ArgumentException error) { return ValidationProblem(CounterpartyMergePurposeTexts.InvalidPreviewTitle, error.Message); }
        })
            .RequireCompanyContext()
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesRead)
            .RequireAuthorization(ErpPermissions.CounterpartyPartiesMergeApprove)
            .RequiresNonEmptyGuidPathParameter("partyId")
            .WithName("PreviewCounterpartyPartyMergePurposeV1")
            .Accepts<PreviewCounterpartyPartyMergeV1>("application/json")
            .Produces<CounterpartyMergePurposePreviewResponseV1>(200)
            .ProducesErpBadRequest(CounterpartyProblemCodesV1.ValidationFailed)
            .ProducesErpNotFound(CounterpartyProblemCodesV1.NotFound)
            .ProducesProblem(409).ProducesProblem(503).ProducesErpAuthenticatedProblems();
    }
}
