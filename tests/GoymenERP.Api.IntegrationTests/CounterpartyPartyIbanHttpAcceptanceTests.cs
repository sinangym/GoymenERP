using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>FNS-D121 SENTETIK / NOT_ENV HTTP boundary of the cari IBAN list (the owner service is a capture).</summary>
public sealed class CounterpartyPartyIbanHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid Party = Guid.Parse("f3853000-0000-4000-8000-000000000001");
    private static readonly Guid IbanId = Guid.Parse("f3853000-0000-4000-8000-000000000002");
    private static string Path => $"/api/v1/counterparties/{Party:D}/ibans";

    [Fact]
    public async Task Anonymous_and_foreign_company_calls_never_reach_the_owner()
    {
        var owner = new Capture();
        using var host = Host(owner);
        using var anonymous = host.CreateClient();
        using var foreign = Client(host, TestCompanyIds.CompanyB, TestUsers.CompanyA);
        using (var read = await anonymous.GetAsync(Path)) Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        using (var read = await foreign.GetAsync(Path)) Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Commands_need_a_key_reject_unknown_fields_and_map_outcomes()
    {
        var owner = new Capture();
        using var host = Host(owner);
        using var client = Client(host, TestCompanyIds.CompanyA);
        var add = JsonSerializer.Serialize(new AddCounterpartyPartyIbanV1(IbanId, "TR130006200000000000000002", "SENTETIK", null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var noKey = await Post(client, Path, add, null)) Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        using (var extra = await Post(client, Path, add[..^1] + ",\"partyName\":\"x\"}", "SENTETIK-k"))
            Assert.Equal(HttpStatusCode.BadRequest, extra.StatusCode);
        Assert.Equal(0, owner.Calls);

        foreach (var (outcome, expected) in new[]
                 {
                     (CounterpartyPartyIbanOutcomeV1.Succeeded, HttpStatusCode.OK),
                     (CounterpartyPartyIbanOutcomeV1.ValidationFailed, HttpStatusCode.UnprocessableEntity),
                     (CounterpartyPartyIbanOutcomeV1.VersionConflict, HttpStatusCode.Conflict),
                     (CounterpartyPartyIbanOutcomeV1.NotFound, HttpStatusCode.NotFound),
                     (CounterpartyPartyIbanOutcomeV1.Denied, HttpStatusCode.Forbidden),
                     (CounterpartyPartyIbanOutcomeV1.Unavailable, HttpStatusCode.ServiceUnavailable),
                 })
        {
            owner.Result = new(outcome, null, null);
            using var reply = await Post(client, $"{Path}/{IbanId:D}/deactivate", "{\"expectedVersion\":\"1\",\"reason\":\"SENTETIK\"}",
                "SENTETIK-d");
            Assert.Equal(expected, reply.StatusCode);
        }

        Assert.Equal((Party, IbanId, "SENTETIK-d"), (owner.PartyId, owner.IbanId, owner.Key));
    }

    private WebApplicationFactory<Program> Host(Capture owner) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<ICounterpartyPartyIbanService>();
        services.AddSingleton<ICounterpartyPartyIbanService>(owner);
    }));

    private static HttpClient Client(WebApplicationFactory<Program> host, Guid company, string actor = TestUsers.GroupAdmin)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", actor);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "SENTETIK-iban-http");
        client.DefaultRequestHeaders.Add("X-Company-Id", company.ToString("D"));
        // The factory's clients already send the Counterparty client contract header.
        return client;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, string body, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed class Capture : ICounterpartyPartyIbanService
    {
        public int Calls { get; private set; }
        public Guid? PartyId { get; private set; }
        public Guid? IbanId { get; private set; }
        public string? Key { get; private set; }
        public CounterpartyPartyIbanCommandResultV1 Result { get; set; } = new(CounterpartyPartyIbanOutcomeV1.Succeeded, null, null);

        public Task<CounterpartyPartyIbanListResultV1> ListAsync(Guid partyId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new CounterpartyPartyIbanListResultV1(CounterpartyPartyIbanOutcomeV1.Succeeded,
                new CounterpartyPartyIbanListV1(TestCompanyIds.CompanyA, partyId, [])));
        }

        public Task<CounterpartyPartyIbanCommandResultV1> AddAsync(Guid partyId, AddCounterpartyPartyIbanV1 request, string idempotencyKey,
            string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent,
            CancellationToken cancellationToken)
        {
            Calls++;
            (PartyId, Key) = (partyId, idempotencyKey);
            return Task.FromResult(Result);
        }

        public Task<CounterpartyPartyIbanCommandResultV1> DeactivateAsync(Guid partyId, Guid ibanId, DeactivateCounterpartyPartyIbanV1 request,
            string idempotencyKey, string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
            string? userAgent, CancellationToken cancellationToken)
        {
            Calls++;
            (PartyId, IbanId, Key) = (partyId, ibanId, idempotencyKey);
            return Task.FromResult(Result);
        }
    }
}
