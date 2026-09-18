using GoymenERP.Api.Multitenancy;
using GoymenERP.Api.OpenApi;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Endpoints;

public static class FinanceEndpoints
{
    public const string DraftContextPath = "/api/v1/finance/manual-receipts/draft-context";

    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handler = endpoints.MapGet(DraftContextPath, async (
            [FromQuery] Guid treasuryAccountId,
            [FromQuery] Guid partyId,
            [FromQuery] Guid companyAccountId,
            [FromQuery] DateOnly occurredOn,
            IFinanceDraftContextEndpointService service,
            ICorrelationContext correlationContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "private, no-store";
            httpContext.Response.Headers.Pragma = "no-cache";
            // The binder above already refused a missing, repeated or impossible date. It also
            // accepts lenient forms (month-first, unpadded, a dropped time part); only the exact
            // ISO text may reach the reader, as the contract's format: date says.
            var rawOccurredOn = httpContext.Request.Query["occurredOn"];
            if (rawOccurredOn.Count != 1 || !FinanceIsoDate.TryParse(rawOccurredOn[0], out var isoOccurredOn)
                || isoOccurredOn != occurredOn)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.GetAsync(
                new(treasuryAccountId, partyId, companyAccountId, occurredOn),
                correlationContext.CorrelationId,
                cancellationToken);
            return result.Outcome switch
            {
                DraftReadOutcome.Found when result.Resource is not null => Results.Ok(result.Resource),
                DraftReadOutcome.InvalidRequest => Results.Problem(statusCode: StatusCodes.Status400BadRequest),
                DraftReadOutcome.Denied => Results.Problem(statusCode: StatusCodes.Status403Forbidden),
                DraftReadOutcome.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound),
                DraftReadOutcome.Unavailable => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException(
                    "Finance draft context returned an invalid outcome/resource combination."),
            };
        });

        handler.WithName("GetFinanceManualReceiptDraftContextV1")
            .WithTags("Finance")
            .RequireAuthorization()
            .RequireCompanyContext()
            .Produces<DraftReadContextView>()
            .ProducesErpAuthenticatedProblems()
            .ProducesErpBadRequest()
            .AllowsEmptyErpBadRequestBody()
            .AllowsCodeLessErpBadRequestBody()
            .ProducesErpCodeLessNotFound()
            .ProducesErpServiceUnavailable();
        return endpoints;
    }
}

public interface IFinanceDraftReadBinding
{
    string? MoneyOperation { get; }
}

public sealed class ManualReceiptDraftReadBinding : IFinanceDraftReadBinding
{
    public string? MoneyOperation => FinanceMoneyOperations.ManualBankReceiptV1;
}

public interface IFinanceDraftContextEndpointService
{
    Task<DraftReadContextResult> GetAsync(DraftReadContextRequest request, string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed class FinanceDraftContextEndpointService(DraftReadContextReader reader,
    IFinanceDraftReadBinding binding) : IFinanceDraftContextEndpointService
{
    public Task<DraftReadContextResult> GetAsync(DraftReadContextRequest request, string correlationId,
        CancellationToken cancellationToken = default) =>
        reader.GetAsync(request, binding.MoneyOperation, correlationId, cancellationToken);
}

public sealed class CounterpartyFinancePartyAccountPort(ICompanyContext companyContext,
    ICurrentUser currentUser, ICounterpartyReadService counterparties) : IFinancePartyAccountPort
{
    public async Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context,
        Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        CounterpartyDetailView? party;
        try
        {
            party = await counterparties.GetAuthorizedPartyAsync(partyId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return new(ReadOutcome.Denied, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!IsCurrent(context)) return new(ReadOutcome.Denied, null);
            throw;
        }

        // The owner answered for the context captured before the await. If company or actor
        // changed meanwhile, neither its data nor its NotFound may be released.
        if (!IsCurrent(context))
        {
            return new(ReadOutcome.Denied, null);
        }

        if (party is null || party.Id != partyId)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var accounts = party.CompanyAccounts.Where(account => account.Id == companyAccountId
            && account.CompanyId == context.CompanyId).ToArray();
        if (accounts.Length != 1)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var account = accounts[0];
        return new(ReadOutcome.Found, new(
            party.Id,
            account.Id,
            account.CompanyId,
            account.IsActive,
            new(party.Id, account.Id, party.Code, party.LegalName, party.TradeName,
                party.Version, account.Version),
            party.IsActive));
    }

    private bool IsCurrent(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && currentUser.UserId == context.ActorId && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);
}
