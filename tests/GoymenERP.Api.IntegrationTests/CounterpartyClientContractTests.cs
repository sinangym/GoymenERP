using System.Net;
using GoymenERP.Api.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyClientContractTests
{
    [Theory]
    [InlineData("POST", null)]
    [InlineData("PUT", null)]
    [InlineData("POST", "")]
    [InlineData("POST", "optional-trade-roles-v2")]
    [InlineData("POST", "OPTIONAL-TRADE-ROLES-V1")]
    [InlineData("POST", "optional-trade-roles-v1,optional-trade-roles-v1")]
    public async Task MissingOrUnsupportedContract_FailsBeforeCommandExecution(
        string method, string? version)
    {
        var context = Context(method, version);
        // A previous middleware's reflection is not the server's acknowledgment.
        context.Response.Headers[CounterpartyClientContract.HeaderName] =
            CounterpartyClientContract.Version;
        var called = false;
        var result = await Filter().InvokeAsync(Invocation(context), _ =>
        {
            called = true;
            return ValueTask.FromResult<object?>(Results.NoContent());
        });

        Assert.False(called);
        AssertProblem(result, version is null
            ? CounterpartyClientContract.RequiredCode
            : CounterpartyClientContract.UnsupportedCode);
        Assert.False(context.Response.Headers.ContainsKey(CounterpartyClientContract.HeaderName));
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task DuplicateDeclarations_AreNotNegotiation()
    {
        var context = Context("POST", null);
        context.Request.Headers[CounterpartyClientContract.HeaderName] = new StringValues(
            [CounterpartyClientContract.Version, CounterpartyClientContract.Version]);
        var result = await Filter().InvokeAsync(Invocation(context), _ =>
            throw new InvalidOperationException("Duplicate headers reached the command."));
        AssertProblem(result, CounterpartyClientContract.UnsupportedCode);
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public async Task UnsupportedRead_PreservesAuthorizationAndFailureResponses(int status)
    {
        var context = Context("GET", "unknown");
        var response = Results.Problem(statusCode: status);
        var result = await Filter().InvokeAsync(Invocation(context), _ =>
            ValueTask.FromResult<object?>(response));
        Assert.Same(response, result);
        Assert.False(context.Response.Headers.ContainsKey(CounterpartyClientContract.HeaderName));
        Assert.Equal("private, no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData("list")]
    [InlineData("detail")]
    [InlineData("candidates")]
    public async Task UnsupportedRead_WithholdsSuccessfulProjection(string shape)
    {
        var context = Context("GET", "unknown");
        var response = ReadResult(shape, PartyRole.None);
        var called = false;
        var result = await Filter().InvokeAsync(Invocation(context), _ =>
        {
            called = true;
            return ValueTask.FromResult<object?>(response);
        });
        Assert.True(called);
        AssertProblem(result, CounterpartyClientContract.UnsupportedCode);
        Assert.False(context.Response.Headers.ContainsKey(CounterpartyClientContract.HeaderName));
    }

    [Theory]
    [InlineData("list", 0)]
    [InlineData("list", 1)]
    [InlineData("list", 15)]
    [InlineData("detail", 0)]
    [InlineData("detail", 1)]
    [InlineData("detail", 15)]
    [InlineData("candidates", 0)]
    [InlineData("candidates", 1)]
    [InlineData("candidates", 15)]
    public async Task LegacyReads_WithholdOnlyIncompatibleAuthorizedProjections(
        string shape, int mask)
    {
        var context = Context("GET", null);
        var response = ReadResult(shape, (PartyRole)mask);
        var result = await Filter().InvokeAsync(Invocation(context), _ =>
            ValueTask.FromResult<object?>(response));
        if (mask == 0)
        {
            AssertProblem(result, CounterpartyClientContract.RequiredCode);
        }
        else
        {
            Assert.Same(response, result);
        }

        Assert.False(context.Response.Headers.ContainsKey(CounterpartyClientContract.HeaderName));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task ExactDeclaration_IsAcknowledgedPerRequest_NotCachedAsAuthority(string method)
    {
        var filter = Filter();
        var accepted = Context(method, CounterpartyClientContract.Version);
        var response = ReadResult("detail", PartyRole.None);
        Assert.Same(response, await filter.InvokeAsync(Invocation(accepted), _ =>
            ValueTask.FromResult<object?>(response)));
        Assert.Equal(CounterpartyClientContract.Version,
            accepted.Response.Headers[CounterpartyClientContract.HeaderName].ToString());

        // A later request gets no authority from a previous session/company's ack.
        var nextRequest = Context("POST", null);
        AssertProblem(await filter.InvokeAsync(Invocation(nextRequest), _ =>
                throw new InvalidOperationException("Negotiation leaked between requests.")),
            CounterpartyClientContract.RequiredCode);
    }

    [Fact]
    public async Task Cors_ExposesOwnedAcknowledgmentAndAllowsDeclarationPreflight()
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = options.GetPolicy(options.DefaultPolicyName);
        Assert.NotNull(policy);
        Assert.False(policy.AllowAnyOrigin);
        Assert.NotEmpty(policy.Origins);
        var origin = policy.Origins[0];
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/counterparties");
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", CounterpartyClientContract.HeaderName);
        using var preflightResponse = await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.Equal(origin,
            Assert.Single(preflightResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains(CounterpartyClientContract.HeaderName,
            string.Join(",", preflightResponse.Headers.GetValues("Access-Control-Allow-Headers")),
            StringComparison.OrdinalIgnoreCase);

        const string deniedOrigin = "https://unlisted-counterparty-client.invalid";
        Assert.DoesNotContain(deniedOrigin, policy.Origins);
        using var deniedPreflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/counterparties");
        deniedPreflight.Headers.Add("Origin", deniedOrigin);
        deniedPreflight.Headers.Add("Access-Control-Request-Method", "POST");
        deniedPreflight.Headers.Add("Access-Control-Request-Headers", CounterpartyClientContract.HeaderName);
        using var deniedResponse = await client.SendAsync(deniedPreflight);
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));

        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString());
        client.DefaultRequestHeaders.Add("Origin", origin);
        using var response = await client.GetAsync("/api/v1/counterparties?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CounterpartyClientContract.Version,
            Assert.Single(response.Headers.GetValues(CounterpartyClientContract.HeaderName)));
        Assert.Contains(CounterpartyClientContract.HeaderName,
            string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompatibilityMessages_AreResolvedFromRegisteredResources()
    {
        await using var factory = new GoymenErpApiFactory();
        var messages = factory.Services
            .GetRequiredService<IStringLocalizer<CounterpartyClientContractFilter>>();
        foreach (var name in new[] { "Title", "Detail" })
        {
            Assert.False(messages[name].ResourceNotFound);
            Assert.NotEmpty(messages[name].Value);
            Assert.NotEqual(name, messages[name].Value);
        }
    }

    private static CounterpartyClientContractFilter Filter() =>
        new(NullLogger<CounterpartyClientContractFilter>.Instance, new TestLocalizer());

    private sealed class TestLocalizer : IStringLocalizer<CounterpartyClientContractFilter>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static DefaultHttpContext Context(string method, string? version)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        if (version is not null)
        {
            context.Request.Headers[CounterpartyClientContract.HeaderName] = version;
        }
        return context;
    }

    private static DefaultEndpointFilterInvocationContext Invocation(HttpContext context) =>
        new(context, Array.Empty<object>());

    private static void AssertProblem(object? result, string code)
    {
        Assert.Equal(StatusCodes.Status409Conflict,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var problem = Assert.IsType<ProblemDetails>(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal(code, problem.Extensions["code"]);
    }

    private static IResult ReadResult(string shape, PartyRole roles)
    {
        var partyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        return shape switch
        {
            "list" => Results.Ok(new CounterpartyPage(
                [new CounterpartyListItem(partyId, "TEST", PartyKind.LegalEntity,
                    "Test", null, true, 1,
                    [new CounterpartyListAccount(accountId, companyId, roles, true, 1)])],
                null, false, null, null)),
            "detail" => Results.Ok(new CounterpartyDetailView(partyId, "TEST",
                PartyKind.LegalEntity, "Test", null, true, "1",
                [new CounterpartyDetailAccountView(accountId, companyId, roles, true, "1")])),
            "candidates" => Results.Ok(new CounterpartyLinkCandidatePage(
                [new CounterpartyLinkCandidateItem(partyId, "TEST", PartyKind.LegalEntity,
                    "Test", null, true,
                    [new CounterpartyLinkCandidateSourceAccount(accountId, companyId, roles, true, "1")])],
                null, false)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }
}
