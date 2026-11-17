using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.FinanceRuntime;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK HTTP adapter tests; final central endpoint mount and runtime acceptance are separate gates.</summary>
public sealed class FinanceCashExchangeBookValueHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/finance/cash-exchanges/book-values";
    private static readonly ProposeCashExchangeBookValueV1 Input = new(Guid.Parse("f1401000-0000-4000-8000-000000000001"), "1",
        "3500.00", new(Guid.Parse("f1401000-0000-4000-8000-000000000002"), 2, new string('b', 64)), "SENTETIK kaynak defter belgesi", "3300.00");

    [Fact]
    public void Actual_book_value_vault_owner_and_evidence_composition_has_no_constructor_cycle()
    {
        using var host = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddFinanceCashExchangeBookValuesV1()));
        using var scope = host.Services.CreateScope();
        Assert.IsType<CashExchangeBookValueServiceV1>(scope.ServiceProvider.GetRequiredService<ICashExchangeBookValueServiceV1>());
        Assert.IsType<DocumentsCashExchangeBookValueEvidencePortV1>(scope.ServiceProvider.GetRequiredService<ICashExchangeBookValueEvidencePortV1>());
        Assert.Contains(scope.ServiceProvider.GetServices<IDocumentOwnerLinkPortV1>(), x => x is FinanceCashExchangeDocumentOwnerLinkPortV1);
    }

    [Fact]
    public async Task Book_value_HTTP_authentication_company_and_strict_unknown_field_fences_precede_the_owner()
    {
        var owner = new Capture(); using var host = Host(owner); using var anon = host.CreateClient();
        using var unauthorized = Client(host, TestCompanyIds.CompanyB, TestUsers.CompanyA);
        using var valid = Client(host, TestCompanyIds.CompanyA);
        var body = JsonSerializer.Serialize(Input, FinanceJson.CreateOptions());
        using var anonymous = await Post(anon, body, "SENTETIK-key"); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var foreign = await Post(unauthorized, body, "SENTETIK-key"); Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var noKey = await Post(valid, body, null); Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        using var extra = await Post(valid, body[..^1] + ",\"companyId\":\"spoofed\"}", "SENTETIK-key"); Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Book_value_HTTP_preserves_exact_doc_version_amount_context_and_private_no_store()
    {
        var owner = new Capture(); using var host = Host(owner); using var valid = Client(host, TestCompanyIds.CompanyA);
        using var result = await Post(valid, JsonSerializer.Serialize(Input, FinanceJson.CreateOptions()), "SENTETIK-fixed-key");
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal(Input, owner.Input); Assert.Equal(TestCompanyIds.CompanyA, owner.Context!.CompanyId);
        Assert.Equal("SENTETIK-fixed-key", owner.Key); Assert.False(result.Headers.Contains("Idempotency-Replayed"));
        Assert.True(result.Headers.CacheControl?.Private); Assert.True(result.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Book_value_HTTP_unavailable_does_not_emit_the_owners_private_details_or_resource()
    {
        var owner = new Capture { Outcome = CommandOutcome.Unavailable }; using var host = Host(owner); using var valid = Client(host, TestCompanyIds.CompanyA);
        using var result = await Post(valid, JsonSerializer.Serialize(Input, FinanceJson.CreateOptions()), "SENTETIK-key");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.DoesNotContain("SENTETIK-private-source", await result.Content.ReadAsStringAsync());
        Assert.False(result.Headers.Contains("Idempotency-Replayed"));
    }

    private WebApplicationFactory<Program> Host(Capture source) => factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    { s.RemoveAll<ICashExchangeBookValueServiceV1>(); s.AddSingleton<ICashExchangeBookValueServiceV1>(source); }));
    private static HttpClient Client(WebApplicationFactory<Program> host, Guid company, string actor = TestUsers.GroupAdmin)
    { var client = host.CreateClient(); client.DefaultRequestHeaders.Add("X-Development-User", actor); client.DefaultRequestHeaders.Add("X-Company-Id", company.ToString("D")); return client; }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string json, string? key)
    { using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (key is not null) request.Headers.Add("Idempotency-Key", key); return await client.SendAsync(request); }
    private sealed class Capture : ICashExchangeBookValueServiceV1
    {
        public int Calls { get; private set; }
        public ProposeCashExchangeBookValueV1? Input { get; private set; }
        public FinanceContext? Context { get; private set; }
        public string? Key { get; private set; }
        public CommandOutcome Outcome { get; init; } = CommandOutcome.VersionConflict;
        public Task<ReadResult<CashExchangeBookValueCandidatePageV1>> ListAsync(FinanceContext context, Guid? afterId, CancellationToken ct) => Task.FromResult(new ReadResult<CashExchangeBookValueCandidatePageV1>(ReadOutcome.NotFound, null));
        public Task<ReadResult<CashExchangeBookValueWorkspaceV1>> GetAsync(FinanceContext context, Guid transferId, CancellationToken ct) => Task.FromResult(new ReadResult<CashExchangeBookValueWorkspaceV1>(ReadOutcome.NotFound, null));
        public Task<CashExchangeBookValueCommandResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata, ProposeCashExchangeBookValueV1 request, CancellationToken ct)
        { Calls++; Context = context; Input = request; Key = metadata.IdempotencyKey; return Task.FromResult(new CashExchangeBookValueCommandResultV1(Outcome, null, false, context.CorrelationId, "SENTETIK-private-source")); }
        public Task<CashExchangeBookValueCommandResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata, DecideCashExchangeBookValueV1 request, CancellationToken ct) => Task.FromResult(new CashExchangeBookValueCommandResultV1(CommandOutcome.Unavailable, null, false, context.CorrelationId, null));
    }
}
