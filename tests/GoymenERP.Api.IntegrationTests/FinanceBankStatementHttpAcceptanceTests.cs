using System.Net;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP12 SENTETIK / NOT_ENV HTTP boundary of the bank statement import (the owner service is a capture).</summary>
public sealed class FinanceBankStatementHttpAcceptanceTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = "/api/v1/finance/bank-statement-imports";
    private static readonly Guid Import = Guid.Parse("f3851000-0000-4000-8000-000000000001");
    private static readonly ReserveFinanceBankStatementImportV1 Reserve = new(Import,
        Guid.Parse("f3851000-0000-4000-8000-000000000002"), FinanceBankStatementFormatV1.Csv, "SENTETIK-BANKA");
    private static readonly ParseFinanceBankStatementImportV1 Parse = new("1", Guid.Parse("f3851000-0000-4000-8000-000000000003"), 1);

    [Fact]
    public async Task Authentication_and_company_boundaries_run_before_the_statement_owner()
    {
        var owner = new Capture();
        using var host = Host(owner);
        using var anonymous = host.CreateClient();
        using var noCompany = Client(host, null);
        using var foreign = Client(host, TestCompanyIds.CompanyB, TestUsers.CompanyA);
        foreach (var (client, expected) in new[]
                 {
                     (anonymous, HttpStatusCode.Unauthorized), (noCompany, HttpStatusCode.BadRequest),
                     (foreign, HttpStatusCode.Forbidden),
                 })
        {
            using var list = await client.GetAsync(Path);
            using var read = await client.GetAsync($"{Path}/{Import:D}");
            using var write = await Post(client, Path, Json(Reserve), "SENTETIK-key");
            Assert.Equal((expected, expected, expected), (list.StatusCode, read.StatusCode, write.StatusCode));
        }

        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Missing_key_unknown_field_and_wrong_media_type_fail_before_the_owner()
    {
        var owner = new Capture();
        using var host = Host(owner);
        using var client = Client(host, TestCompanyIds.CompanyA);
        var json = Json(Reserve);
        using var noKey = await Post(client, Path, json, null);
        using var amount = await Post(client, Path, json[..^1] + ",\"amount\":\"1.00\"}", "SENTETIK-key");
        using var company = await Post(client, Path, json[..^1] + ",\"companyId\":\"spoofed\"}", "SENTETIK-key");
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(json, Encoding.UTF8, "text/plain") };
        request.Headers.Add("Idempotency-Key", "SENTETIK-key");
        using var media = await client.SendAsync(request);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType),
            (noKey.StatusCode, amount.StatusCode, company.StatusCode, media.StatusCode));
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task Outcomes_map_to_status_codes_with_replay_header_and_private_no_store()
    {
        var owner = new Capture();
        using var host = Host(owner);
        using var client = Client(host, TestCompanyIds.CompanyA);
        using var ok = await Post(client, Path, Json(Reserve), "SENTETIK-key");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(Reserve, owner.Reserve);
        Assert.Equal((TestCompanyIds.CompanyA, "SENTETIK-key"), (owner.Context!.CompanyId, owner.Key));
        Assert.Equal("true", Assert.Single(ok.Headers.GetValues("Idempotency-Replayed")));
        Assert.True(ok.Headers.CacheControl?.Private);
        Assert.True(ok.Headers.CacheControl?.NoStore);

        foreach (var (outcome, code, expected) in new (CommandOutcome, string?, HttpStatusCode)[]
                 {
                     (CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.ImportNotFound, HttpStatusCode.NotFound),
                     (CommandOutcome.ValidationFailed, FinanceBankStatementCodesV1.FileInvalid, HttpStatusCode.UnprocessableEntity),
                     (CommandOutcome.VersionConflict, null, HttpStatusCode.Conflict),
                     (CommandOutcome.IdempotencyConflict, null, HttpStatusCode.Conflict),
                     (CommandOutcome.Denied, null, HttpStatusCode.Forbidden),
                     (CommandOutcome.Unavailable, FinanceBankStatementCodesV1.Unavailable, HttpStatusCode.ServiceUnavailable),
                 })
        {
            owner.Result = new FinanceBankStatementCommandResultV1(outcome, null, code,
                code == FinanceBankStatementCodesV1.FileInvalid ? [new FinanceBankStatementIssueV1(3, "amount", "amount_invalid")] : []);
            using var reply = await Post(client, $"{Path}/{Import:D}/parse", Json(Parse), "SENTETIK-parse");
            Assert.Equal(expected, reply.StatusCode);
            if (expected == HttpStatusCode.UnprocessableEntity)
                Assert.Contains("amount_invalid", await reply.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(Parse, owner.Parse);
        Assert.Equal(Import, owner.ParsedImport);
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, FinanceJson.CreateOptions());

    private WebApplicationFactory<Program> Host(Capture owner) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IFinanceBankStatementImportServiceV1>();
        services.AddSingleton<IFinanceBankStatementImportServiceV1>(owner);
    }));

    private static HttpClient Client(WebApplicationFactory<Program> host, Guid? company, string actor = TestUsers.GroupAdmin)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", actor);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "SENTETIK-statement-http");
        if (company is { } id) client.DefaultRequestHeaders.Add("X-Company-Id", id.ToString("D"));
        return client;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, string body, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed class Capture : IFinanceBankStatementImportServiceV1
    {
        public int Calls { get; private set; }
        public FinanceContext? Context { get; private set; }
        public string? Key { get; private set; }
        public ReserveFinanceBankStatementImportV1? Reserve { get; private set; }
        public ParseFinanceBankStatementImportV1? Parse { get; private set; }
        public Guid? ParsedImport { get; private set; }
        public FinanceBankStatementCommandResultV1 Result { get; set; } = new(CommandOutcome.Succeeded, null, null, [], true);

        public Task<FinanceBankStatementCommandResultV1> ReserveAsync(FinanceContext context, CommandMetadata metadata,
            ReserveFinanceBankStatementImportV1 request, CancellationToken cancellationToken)
        {
            Calls++;
            (Context, Key, Reserve) = (context, metadata.IdempotencyKey, request);
            return Task.FromResult(Result);
        }

        public Task<FinanceBankStatementCommandResultV1> ParseAsync(FinanceContext context, CommandMetadata metadata, Guid importId,
            ParseFinanceBankStatementImportV1 request, CancellationToken cancellationToken)
        {
            Calls++;
            (Context, Key, Parse, ParsedImport) = (context, metadata.IdempotencyKey, request, importId);
            return Task.FromResult(Result);
        }

        public Task<FinanceBankStatementDetailResultV1> GetAuthorizedAsync(FinanceContext context, Guid importId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new FinanceBankStatementDetailResultV1(ReadOutcome.NotFound, null));
        }

        public Task<FinanceBankStatementListResultV1> ListAuthorizedAsync(FinanceContext context, Guid? bankAccountId,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new FinanceBankStatementListResultV1(ReadOutcome.Found,
                new FinanceBankStatementImportListV1(context.CompanyId, [])));
        }

        public Task<ReadOutcome> CheckDocumentOwnerAsync(FinanceContext context, Guid importId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ReadOutcome.NotFound);
        }
    }
}
