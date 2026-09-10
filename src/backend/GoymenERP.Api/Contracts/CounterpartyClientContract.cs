using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Contracts;

/// <summary>
/// A transport compatibility declaration, never an identity or permission grant.
/// Clients must observe the server acknowledgment on a successful context-bound
/// GET before enabling commands; an old server cannot supply this acknowledgment.
/// </summary>
public static class CounterpartyClientContract
{
    public const string HeaderName = "X-ERP-Counterparty-Contract";
    public const string Version = "optional-trade-roles-v1";
    public const string VersionPattern = "^optional-trade-roles-v1$";
    public const string RequiredCode = "client_contract_required";
    public const string UnsupportedCode = "client_contract_unsupported";
}

internal sealed class CounterpartyClientContractMetadata;

public sealed class CounterpartyClientContractFilter(
    ILogger<CounterpartyClientContractFilter> logger,
    IStringLocalizer<CounterpartyClientContractFilter> messages) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next)
    {
        var context = invocationContext.HttpContext;
        var responseHeaders = context.Response.Headers;
        responseHeaders.Remove(CounterpartyClientContract.HeaderName);
        responseHeaders[HeaderNames.CacheControl] = "private, no-store";
        responseHeaders[HeaderNames.Pragma] = "no-cache";

        var supplied = context.Request.Headers.TryGetValue(
            CounterpartyClientContract.HeaderName, out var values);
        if (supplied)
        {
            if (values.Count != 1
                || !string.Equals(values[0], CounterpartyClientContract.Version,
                    StringComparison.Ordinal))
            {
                // A read must retain the handler's authorization and non-disclosure
                // failures. Only an authorized success is replaced by negotiation.
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    var readResult = await next(invocationContext);
                    if (readResult is IStatusCodeHttpResult { StatusCode: >= 400 })
                    {
                        return readResult;
                    }
                }

                return Reject(CounterpartyClientContract.UnsupportedCode);
            }

            // This is an owned acknowledgment, not a reflected arbitrary header.
            responseHeaders[CounterpartyClientContract.HeaderName] =
                CounterpartyClientContract.Version;
            return await next(invocationContext);
        }

        // Gate before command execution, including durable replay and active-state
        // changes. An old tab must not continue writing with a cached legacy view.
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return Reject(CounterpartyClientContract.RequiredCode);
        }

        var result = await next(invocationContext);
        // Inspect only the already-authorized projection. A foreign company's zero
        // rows must not change a legacy caller's result or disclose their existence.
        return ContainsUnclassifiedAccount(result)
            ? Reject(CounterpartyClientContract.RequiredCode)
            : result;
    }

    private static bool ContainsUnclassifiedAccount(object? result) => result switch
    {
        IValueHttpResult { Value: CounterpartyPage page } =>
            page.Items.Any(party => party.CompanyAccounts.Any(account =>
                account.Roles == PartyRole.None)),
        IValueHttpResult { Value: CounterpartyDetailView party } =>
            party.CompanyAccounts.Any(account => account.Roles == PartyRole.None),
        IValueHttpResult { Value: CounterpartyLinkCandidatePage page } =>
            page.Items.Any(party => party.VisibleSourceAccounts.Any(account =>
                account.Roles == PartyRole.None)),
        _ => false,
    };

    private IResult Reject(string code)
    {
        logger.LogInformation("Cari client contract rejected. Code={Code}", code);
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: Message("Title"),
            detail: Message("Detail"),
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    private string Message(string name)
    {
        var message = messages[name];
        return !message.ResourceNotFound
            ? message.Value
            : throw new InvalidOperationException("Cari contract resource is missing.");
    }
}
