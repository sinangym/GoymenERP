using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>FNS-D59 HTTP contract: an explicit capturing owner, never a real Settings/TCMB or financial posting proof.</summary>
public sealed class FinanceCashTransferExchangeHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Usd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
    private static readonly Guid Try = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateOnly Day = new(2026, 11, 17);
    private static readonly CashTransferPreparation Preparation = new(CashTransferKind.CurrencyExchange,
        CashTransferAccountKind.Bank, Guid.Parse("33333333-3333-4333-8333-333333333333"), new("100", Usd), new(Profile.ToString("D"), "1"),
        CashTransferAccountKind.Bank, Guid.Parse("44444444-4444-4444-8444-444444444444"), new("3300", Try), new(Profile.ToString("D"), "1"),
        null, Day, null, "SENTETIK exchange", null);

    [Fact]
    public async Task Exchange_preview_returns_server_quote_and_commit_preserves_the_original_null_rate_and_replay_headers()
    {
        var owner = new CapturingExchangeService();
        using var host = Host(owner);
        using var client = Client(host);
        using var preview = await client.PostAsJsonAsync(FinanceEndpoints.CashTransferPreviewPath, Preparation, Json);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = (await preview.Content.ReadFromJsonAsync<CashTransferPreviewResult>(Json))!;
        Assert.Null(previewBody.Preparation!.Rate);
        Assert.Equal(("33.00", "34.00", "-1.00"),
            (previewBody.Exchange!.TransactionRate, previewBody.Exchange.ComparisonRate, previewBody.Exchange.RateDeviation));
        Assert.Equal(Preparation, owner.Previewed);
        Assert.Equal(TestCompanyIds.CompanyA, owner.Context!.CompanyId);
        Assert.False(string.IsNullOrWhiteSpace(owner.Context.ActorId));

        using var message = new HttpRequestMessage(HttpMethod.Post, FinanceEndpoints.CashTransfersPath)
        { Content = JsonContent.Create(new CommitCashTransferRequest(Preparation, true), options: Json) };
        message.Headers.Add("Idempotency-Key", "SENTETIK-exchange-key");
        using var recorded = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        Assert.Equal("SENTETIK-exchange-key", owner.Metadata!.IdempotencyKey);
        Assert.Equal(new CommitCashTransferRequest(Preparation, true), owner.Committed);
        Assert.Equal("true", recorded.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal("SENTETIK-original", recorded.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
        var body = (await recorded.Content.ReadFromJsonAsync<CashTransferCommandResult>(Json))!;
        Assert.Equal("33.00", body.Resource!.Preparation.Rate);
        Assert.Equal(previewBody.Exchange, body.Resource.Exchange);
        Assert.True(recorded.Headers.CacheControl?.Private);
        Assert.True(recorded.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Exchange_authentication_company_key_and_strict_body_fences_run_before_the_owner()
    {
        var owner = new CapturingExchangeService();
        using var host = Host(owner);
        using var anonymous = host.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        using var anonymousResponse = await anonymous.PostAsJsonAsync(FinanceEndpoints.CashTransferPreviewPath, Preparation, Json);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        using var foreign = Client(host, TestUsers.CompanyA, TestCompanyIds.CompanyB);
        using var foreignResponse = await foreign.PostAsJsonAsync(FinanceEndpoints.CashTransferPreviewPath, Preparation, Json);
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
        using var client = Client(host);
        using var missingKey = await client.PostAsJsonAsync(FinanceEndpoints.CashTransfersPath, new CommitCashTransferRequest(Preparation, true), Json);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        var json = JsonSerializer.Serialize(Preparation, Json);
        using var forged = await client.PostAsync(FinanceEndpoints.CashTransferPreviewPath,
            new StringContent(json[..^1] + ",\"companyId\":\"" + TestCompanyIds.CompanyB.ToString("D") + "\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Exchange_source_unavailable_is_503_and_does_not_return_a_successful_zero_quote()
    {
        var owner = new CapturingExchangeService { Outcome = CommandOutcome.Unavailable };
        using var host = Host(owner);
        using var client = Client(host);
        using var response = await client.PostAsJsonAsync(FinanceEndpoints.CashTransferPreviewPath, Preparation, Json);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("service_unavailable", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("exchange", out _));
    }

    [Fact]
    public async Task Registering_the_actual_exchange_source_does_not_grant_transfer_authority()
    {
        using var client = Client(factory);
        using var response = await client.PostAsJsonAsync(FinanceEndpoints.CashTransferPreviewPath, Preparation, Json);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private WebApplicationFactory<Program> Host(CapturingExchangeService service) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => { services.RemoveAll<ICashTransferService>(); services.AddSingleton<ICashTransferService>(service); }));

    private static HttpClient Client(WebApplicationFactory<Program> host, string user = TestUsers.GroupAdmin, Guid? company = null)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", user);
        client.DefaultRequestHeaders.Add("X-Company-Id", (company ?? TestCompanyIds.CompanyA).ToString("D"));
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "SENTETIK-exchange-http");
        return client;
    }

    private sealed class CapturingExchangeService : ICashTransferService
    {
        private readonly Guid rateId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        public int Calls { get; private set; }
        public CommandOutcome Outcome { get; init; } = CommandOutcome.Succeeded;
        public FinanceContext? Context { get; private set; }
        public CashTransferPreparation? Previewed { get; private set; }
        public CommitCashTransferRequest? Committed { get; private set; }
        public CommandMetadata? Metadata { get; private set; }
        private CashTransferExchangeSnapshotV1 Snapshot(FinanceContext context, CashTransferPreparation p) =>
            CashTransferExchangeCalculatorV1.Calculate(context.CompanyId, p.OccurredOn, p.SourceMoney, p.TargetMoney,
                p.SourceProfile, p.TargetProfile, new(Usd, "34", Day, ExchangeRateSourcesV1.Tcmb, rateId, "7", Day.AddDays(-1), "SENTETIK"),
                new(Try, "1", Day, "FunctionalCurrency", null, null, null, null));
        public Task<CashTransferPreviewResult> PreviewAsync(FinanceContext context, CashTransferPreparation preparation, CancellationToken ct)
        {
            Calls++; Context = context; Previewed = preparation;
            return Task.FromResult(new CashTransferPreviewResult(Outcome, Outcome == CommandOutcome.Succeeded ? preparation : null, [],
                Outcome == CommandOutcome.Succeeded ? Snapshot(context, preparation) : null));
        }
        public Task<CashTransferCommandResult> CommitAsync(FinanceContext context, CommandMetadata metadata, CommitCashTransferRequest request, CancellationToken ct)
        {
            Calls++; Context = context; Committed = request; Metadata = metadata;
            var snapshot = Snapshot(context, request.Preparation);
            return Task.FromResult(new CashTransferCommandResult(CommandOutcome.Succeeded,
                new(Guid.Parse("88888888-8888-4888-8888-888888888888"), context.CompanyId,
                    request.Preparation with { Rate = snapshot.TransactionRate }, context.ActorId, DateTimeOffset.UnixEpoch,
                    true, "1", snapshot), true, "SENTETIK-original", []));
        }
        public Task<ReadResult<CashTransferPage>> ListAuthorizedAsync(FinanceContext c, string? cursor, CancellationToken ct) => throw new NotSupportedException();
        public Task<ReadResult<CashTransferView>> DetailAuthorizedAsync(FinanceContext c, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<ReadResult<CashTransferAccountContextView>> AccountContextAuthorizedAsync(FinanceContext c, CashTransferAccountKind kind, Guid id, DateOnly on, CancellationToken ct) => throw new NotSupportedException();
    }
}
