using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK / NOT_ENV HTTP boundary only. Capturing owner is not an authorization grant or SQL acceptance.</summary>
public sealed class FinanceCreditMovementHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/finance/credit-movements";
    private static readonly Guid Movement = Guid.Parse("c0693000-0000-4000-8000-000000000001");
    private static readonly Guid Account = Guid.Parse("c0693000-0000-4000-8000-000000000002");
    private static readonly CreditMovementProposeV1 Input = new(Account, "2", CreditMovementKindV1.Drawdown,
        new("SENTETIK bank document", new(2026, 11, 17), "1"), new(2026, 11, 17), new(2026, 11, 17),
        [new("principal", "100.0001", null)], null, null, null, "SENTETIK source");

    [Fact]
    public async Task Anonymous_missing_company_and_foreign_company_never_reach_the_credit_movement_owner()
    {
        var owner = new Capturing(); using var host = Host(owner);
        using var anonymous = host.CreateClient();
        using var missing = Client(host, company: null);
        using var foreign = Client(host, TestCompanyIds.CompanyB, TestUsers.CompanyA);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (missing, HttpStatusCode.BadRequest), (foreign, HttpStatusCode.Forbidden) })
        {
            using var read = await client.GetAsync($"{Path}?creditAccountId={Account:D}");
            using var write = await Post(client, Path, JsonSerializer.Serialize(Input, FinanceJson.CreateOptions()), "SENTETIK-key");
            Assert.Equal(expected, read.StatusCode); Assert.Equal(expected, write.StatusCode);
        }
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Missing_idempotency_key_numeric_money_and_unknown_request_members_fail_before_owner()
    {
        var owner = new Capturing(); using var host = Host(owner); using var client = Client(host, TestCompanyIds.CompanyA);
        var valid = JsonSerializer.Serialize(Input, FinanceJson.CreateOptions());
        using var missing = await Post(client, Path, valid, null);
        using var numeric = await Post(client, Path, valid.Replace("\"100.0001\"", "100.0001", StringComparison.Ordinal), "SENTETIK-key");
        using var unknown = await Post(client, $"{Path}/{Movement:D}/decision", "{\"expectedVersion\":\"1\",\"approve\":true,\"reason\":null,\"companyId\":\"spoofed\"}", "SENTETIK-key");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode); Assert.Equal(HttpStatusCode.BadRequest, numeric.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode); Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Source_decimal_text_and_null_TL_reach_owner_unchanged_and_decision_identity_comes_from_route()
    {
        var owner = new Capturing(); using var host = Host(owner); using var client = Client(host, TestCompanyIds.CompanyA);
        using var proposed = await Post(client, Path, JsonSerializer.Serialize(Input, FinanceJson.CreateOptions()), "SENTETIK-propose");
        Assert.Equal(HttpStatusCode.OK, proposed.StatusCode);
        Assert.Equal("100.0001", Assert.Single(owner.Proposed!.Components).Amount);
        Assert.Null(Assert.Single(owner.Proposed.Components).FunctionalAmount);
        Assert.Equal(TestCompanyIds.CompanyA, owner.Context!.CompanyId);
        Assert.Equal("SENTETIK-propose", owner.Metadata!.IdempotencyKey);
        using var decided = await Post(client, $"{Path}/{Movement:D}/decision", "{\"expectedVersion\":\"1\",\"approve\":true,\"reason\":null}", "SENTETIK-decide");
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode); Assert.Equal(Movement, owner.Decided!.MovementId);
        Assert.True(decided.Headers.CacheControl?.Private); Assert.True(decided.Headers.CacheControl?.NoStore);
    }

    private WebApplicationFactory<Program> Host(Capturing owner) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    { services.RemoveAll<IFinanceCreditMovementServiceV1>(); services.AddSingleton<IFinanceCreditMovementServiceV1>(owner); }));
    private static HttpClient Client(WebApplicationFactory<Program> host, Guid? company, string actor = TestUsers.GroupAdmin)
    {
        var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", actor);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "SENTETIK-credit-http");
        if (company is { } id) client.DefaultRequestHeaders.Add("X-Company-Id", id.ToString("D"));
        return client;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string route, string json, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
    private sealed class Capturing : IFinanceCreditMovementServiceV1
    {
        public int Calls { get; private set; }
        public FinanceContext? Context { get; private set; }
        public CommandMetadata? Metadata { get; private set; }
        public CreditMovementProposeV1? Proposed { get; private set; }
        public CreditMovementDecisionV1? Decided { get; private set; }
        public Task<CreditMovementResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata, CreditMovementProposeV1 request, CancellationToken cancellationToken)
        { Proposed = request; return Task.FromResult(Result(context, metadata)); }
        public Task<CreditMovementResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata, CreditMovementDecisionV1 request, CancellationToken cancellationToken)
        { Decided = request; return Task.FromResult(Result(context, metadata)); }
        public Task<ReadResult<CreditMovementPageV1>> ListAsync(FinanceContext context, Guid creditAccountId, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new ReadResult<CreditMovementPageV1>(ReadOutcome.Found, new(context.CompanyId, []))); }
        private CreditMovementResultV1 Result(FinanceContext context, CommandMetadata metadata)
        { Calls++; Context = context; Metadata = metadata; return new(CommandOutcome.Succeeded, null, false, context.CorrelationId, []); }
    }
}
