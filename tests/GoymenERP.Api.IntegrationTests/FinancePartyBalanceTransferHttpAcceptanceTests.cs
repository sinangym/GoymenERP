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

/// <summary>SENTETIK / NOT_ENV HTTP boundary; source compile does not assert central route registration or runtime acceptance.</summary>
public sealed class FinancePartyBalanceTransferHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/finance/party-balance-transfers";
    private static readonly Guid Transfer = Guid.Parse("f3841000-0000-4000-8000-000000000001");
    private static readonly FinancePartyBalanceTransferSelectionV1 Selection = new(Guid.Parse("f3841000-0000-4000-8000-000000000002"),
        Guid.Parse("f3841000-0000-4000-8000-000000000003"), Guid.Parse("f3841000-0000-4000-8000-000000000004"), Guid.Parse("f3841000-0000-4000-8000-000000000005"));
    private static readonly RecordFinancePartyBalanceTransferV1 Input = new(Transfer, Selection, new string('a', 64),
        new(2026, 11, 17), "SENTETIK legal transfer", new(Guid.Parse("f3841000-0000-4000-8000-000000000006"), 1, new string('b', 64)));

    [Fact]
    public async Task Authentication_and_company_boundaries_run_before_balance_transfer_owner()
    {
        var owner = new Capture(); using var host = Host(owner);
        using var anonymous = host.CreateClient(); using var noCompany = Client(host, null);
        using var foreign = Client(host, TestCompanyIds.CompanyB, TestUsers.CompanyA);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (noCompany, HttpStatusCode.BadRequest), (foreign, HttpStatusCode.Forbidden) })
        {
            using var read = await client.GetAsync($"{Path}/{Transfer:D}");
            using var write = await Post(client, JsonSerializer.Serialize(Input, FinanceJson.CreateOptions()), "SENTETIK-key");
            Assert.Equal(expected, read.StatusCode); Assert.Equal(expected, write.StatusCode);
        }
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Missing_key_and_client_selected_amount_or_company_fail_before_the_owner()
    {
        var owner = new Capture(); using var host = Host(owner); using var client = Client(host, TestCompanyIds.CompanyA);
        var json = JsonSerializer.Serialize(Input, FinanceJson.CreateOptions());
        using var noKey = await Post(client, json, null);
        using var money = await Post(client, json[..^1] + ",\"amount\":\"10.00\"}", "SENTETIK-key");
        using var company = await Post(client, json[..^1] + ",\"companyId\":\"spoofed\"}", "SENTETIK-key");
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode); Assert.Equal(HttpStatusCode.BadRequest, money.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, company.StatusCode); Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Selection_document_and_snapshot_proof_are_preserved_with_company_context_and_no_store_replay_header()
    {
        var owner = new Capture(); using var host = Host(owner); using var client = Client(host, TestCompanyIds.CompanyA);
        using var reply = await Post(client, JsonSerializer.Serialize(Input, FinanceJson.CreateOptions()), "SENTETIK-key");
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Equal(Input, owner.Input); Assert.Equal(TestCompanyIds.CompanyA, owner.Context!.CompanyId);
        Assert.Equal("SENTETIK-key", owner.Key); Assert.Equal("true", Assert.Single(reply.Headers.GetValues("Idempotency-Replayed")));
        Assert.True(reply.Headers.CacheControl?.Private); Assert.True(reply.Headers.CacheControl?.NoStore);
    }
    private WebApplicationFactory<Program> Host(Capture owner) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    { services.RemoveAll<IFinancePartyBalanceTransferServiceV1>(); services.AddSingleton<IFinancePartyBalanceTransferServiceV1>(owner); }));
    private static HttpClient Client(WebApplicationFactory<Program> host, Guid? company, string actor = TestUsers.GroupAdmin)
    { var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", actor);
      client.DefaultRequestHeaders.Add("X-Correlation-Id", "SENTETIK-transfer-http");
      if (company is { } id) client.DefaultRequestHeaders.Add("X-Company-Id", id.ToString("D")); return client; }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string body, string? key)
    { using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
      if (key is not null) request.Headers.Add("Idempotency-Key", key); return await client.SendAsync(request); }
    private sealed class Capture : IFinancePartyBalanceTransferServiceV1
    {
        public int Calls { get; private set; }
        public RecordFinancePartyBalanceTransferV1? Input { get; private set; }
        public FinanceContext? Context { get; private set; }
        public string? Key { get; private set; }
        public Task<FinancePartyBalanceTransferReadResultV1> GetAuthorizedAsync(FinanceContext context, Guid transferId, CancellationToken token)
        { Calls++; return Task.FromResult(new FinancePartyBalanceTransferReadResultV1(ReadOutcome.NotFound, null, null)); }
        public Task<FinancePartyBalanceTransferReadResultV1> PreviewAuthorizedAsync(FinanceContext context, FinancePartyBalanceTransferSelectionV1 selection, CancellationToken token)
        { Calls++; return Task.FromResult(new FinancePartyBalanceTransferReadResultV1(ReadOutcome.NotFound, null, null)); }
        public Task<FinancePartyBalanceTransferCommandResultV1> RecordAsync(FinanceContext context, CommandMetadata metadata, RecordFinancePartyBalanceTransferV1 input, CancellationToken token)
        { Calls++; Context = context; Input = input; Key = metadata.IdempotencyKey;
          return Task.FromResult(new FinancePartyBalanceTransferCommandResultV1(CommandOutcome.Succeeded, null, null, true)); }
    }
}
