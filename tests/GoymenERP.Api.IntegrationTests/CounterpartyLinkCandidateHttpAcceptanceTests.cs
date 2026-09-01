using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyLinkCandidateHttpAcceptanceTests(
    GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Endpoint =
        "/api/v1/counterparties/company-account-link-candidates";

    [Fact]
    public async Task Authorized_request_is_actor_session_bound_lossless_redacted_and_private()
    {
        var partyId = Guid.Parse("58000000-0000-0000-0000-000000000001");
        var accountId = Guid.Parse("59000000-0000-0000-0000-000000000001");
        var service = CapturingService.Succeeded(
            new CounterpartyLinkCandidatePage(
                [
                    new CounterpartyLinkCandidateItem(
                        partyId,
                        "CAR-000001",
                        PartyKind.LegalEntity,
                        "İzmir Demir A.Ş.",
                        "İzmir Demir",
                        true,
                        [
                            new CounterpartyLinkCandidateSourceAccount(
                                accountId,
                                TestCompanyIds.CompanyB,
                                PartyRole.Customer | PartyRole.Supplier,
                                true,
                                "9007199254740993"),
                        ]),
                ],
                "opaque-next",
                true));
        using var host = CreateHost(service);
        using var client = CreateClient(
            host,
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        var rawQuery = Uri.EscapeDataString("  I\u0307zmir  ");

        using var response = await client.GetAsync(
            $"{Endpoint}?q={rawQuery}&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            "externalIntegrationCode",
            body,
            StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        AssertExactProperties(document.RootElement, "hasMore", "items", "nextCursor");
        Assert.True(document.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal("opaque-next", document.RootElement.GetProperty("nextCursor").GetString());
        var item = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        AssertExactProperties(
            item,
            "code",
            "id",
            "isActive",
            "kind",
            "legalName",
            "tradeName",
            "visibleSourceAccounts");
        Assert.Equal(partyId, item.GetProperty("id").GetGuid());
        var account = Assert.Single(
            item.GetProperty("visibleSourceAccounts").EnumerateArray());
        AssertExactProperties(
            account,
            "companyId",
            "id",
            "isActive",
            "roles",
            "version");
        Assert.Equal(accountId, account.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.String, account.GetProperty("version").ValueKind);
        Assert.Equal("9007199254740993", account.GetProperty("version").GetString());

        var request = Assert.Single(service.Requests);
        Assert.Equal("İzmir", request.Query.Query);
        Assert.True(request.Query.Query!.IsNormalized(NormalizationForm.FormC));
        Assert.Equal(100, request.Query.PageSize);
        Assert.Null(request.Query.Cursor);
        Assert.True(Guid.TryParse(request.ActorId, out _));
        Assert.False(string.IsNullOrWhiteSpace(request.SessionBinding));
    }

    [Theory]
    [InlineData("?unknown=value")]
    [InlineData("?q=first&q=second")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=not-a-number")]
    public async Task Invalid_raw_query_fails_closed_before_service(string query)
    {
        var service = CapturingService.Succeeded(EmptyPage());
        using var host = CreateHost(service);
        using var client = CreateClient(
            host,
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(Endpoint + query);

        await AssertProblemCodeAsync(
            response,
            HttpStatusCode.BadRequest,
            "validation_failed");
        AssertPrivateNoStore(response);
        Assert.Empty(service.Requests);
    }

    [Theory]
    [InlineData("?cursor=")]
    [InlineData("?cursor=%20")]
    [InlineData("?cursor=first&cursor=second")]
    public async Task Invalid_cursor_fails_closed_before_service(string query)
    {
        var service = CapturingService.Succeeded(EmptyPage());
        using var host = CreateHost(service);
        using var client = CreateClient(
            host,
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(Endpoint + query);

        await AssertProblemCodeAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_cursor");
        AssertPrivateNoStore(response);
        Assert.Empty(service.Requests);
    }

    [Theory]
    [InlineData(CounterpartyLinkCandidateResult.Forbidden, HttpStatusCode.Forbidden, "forbidden")]
    [InlineData(CounterpartyLinkCandidateResult.InvalidCursor, HttpStatusCode.BadRequest, "invalid_cursor")]
    [InlineData(
        CounterpartyLinkCandidateResult.ServiceUnavailable,
        HttpStatusCode.ServiceUnavailable,
        "service_unavailable")]
    public async Task Service_outcome_has_stable_problem_contract(
        CounterpartyLinkCandidateResult result,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var service = new CapturingService(result);
        using var host = CreateHost(service);
        using var client = CreateClient(
            host,
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(Endpoint);

        var body = await AssertProblemCodeAsync(response, expectedStatus, expectedCode);
        AssertPrivateNoStore(response);
        Assert.DoesNotContain("items", body, StringComparison.OrdinalIgnoreCase);
        Assert.Single(service.Requests);
    }

    [Fact]
    public async Task Missing_session_binding_returns_stable_service_unavailable_before_service()
    {
        var service = CapturingService.Succeeded(EmptyPage());
        using var host = CreateHost(service, new EmptySessionBindingResolver());
        using var client = CreateClient(
            host,
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(Endpoint);

        await AssertProblemCodeAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "service_unavailable");
        AssertPrivateNoStore(response);
        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task Both_permissions_are_required_before_service()
    {
        var service = CapturingService.Succeeded(EmptyPage());
        using var host = CreateHost(service);
        using var client = CreateClient(
            host,
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(Endpoint);

        await AssertProblemCodeAsync(response, HttpStatusCode.Forbidden, "forbidden");
        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task Selected_company_is_required_before_service()
    {
        var service = CapturingService.Succeeded(EmptyPage());
        using var host = CreateHost(service);
        using var client = CreateClient(host, TestUsers.GroupAdmin, null);

        using var response = await client.GetAsync(Endpoint);

        await AssertProblemCodeAsync(
            response,
            HttpStatusCode.BadRequest,
            "company_context_invalid");
        Assert.Empty(service.Requests);
    }

    private WebApplicationFactory<Program> CreateHost(
        ICounterpartyLinkCandidateReadService service,
        IActorSessionBindingResolver? sessionBindingResolver = null)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICounterpartyLinkCandidateReadService>();
                services.AddSingleton(service);
                if (sessionBindingResolver is not null)
                {
                    services.RemoveAll<IActorSessionBindingResolver>();
                    services.AddSingleton(sessionBindingResolver);
                }
            }));
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> host,
        string userId,
        Guid? companyId)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", userId);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        if (companyId.HasValue)
        {
            client.DefaultRequestHeaders.Add("X-Company-Id", companyId.Value.ToString("D"));
        }

        return client;
    }

    private static CounterpartyLinkCandidatePage EmptyPage() =>
        new([], null, false);

    private static async Task<string> AssertProblemCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            (int)expectedStatus,
            document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("detail").GetString()));
        return body;
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private static void AssertExactProperties(JsonElement value, params string[] names)
    {
        Assert.Equal(
            names.Order(StringComparer.Ordinal),
            value.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    private sealed class CapturingService(CounterpartyLinkCandidateResult result)
        : ICounterpartyLinkCandidateReadService
    {
        private readonly CounterpartyLinkCandidateResult _result = result;
        private readonly List<CounterpartyLinkCandidateRequest> _requests = [];
        private CounterpartyLinkCandidatePage? _page;

        public IReadOnlyList<CounterpartyLinkCandidateRequest> Requests => _requests;

        public static CapturingService Succeeded(CounterpartyLinkCandidatePage page)
        {
            return new CapturingService(CounterpartyLinkCandidateResult.Succeeded)
            {
                _page = page,
            };
        }

        public Task<CounterpartyLinkCandidateExecution> GetAuthorizedPageAsync(
            CounterpartyLinkCandidateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requests.Add(request);
            var execution = _result switch
            {
                CounterpartyLinkCandidateResult.Succeeded =>
                    CounterpartyLinkCandidateExecution.Succeeded(_page!),
                CounterpartyLinkCandidateResult.Forbidden =>
                    CounterpartyLinkCandidateExecution.Forbidden(),
                CounterpartyLinkCandidateResult.InvalidCursor =>
                    CounterpartyLinkCandidateExecution.InvalidCursor(),
                CounterpartyLinkCandidateResult.ServiceUnavailable =>
                    CounterpartyLinkCandidateExecution.ServiceUnavailable(),
                _ => throw new InvalidOperationException("Unsupported test execution result."),
            };
            return Task.FromResult(execution);
        }
    }

    private sealed class EmptySessionBindingResolver : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }
    }
}
