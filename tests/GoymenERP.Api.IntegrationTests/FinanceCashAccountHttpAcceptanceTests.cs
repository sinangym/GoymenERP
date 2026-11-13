using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-30: the six cash-account routes over HTTP. The owner (<see cref="IFinanceCashAccountMaster"/>) is a
/// capturing stand-in, so these facts hold the route's own duties: authentication and company context,
/// the Idempotency-Key and strict-body gates, route → request mapping with a server-resolved context, and
/// outcome → status / problem code / replay headers. The owner's rules are proven on real SQL in
/// <c>FinanceCashAccountSqlServerRelationalAcceptanceTests</c>.
/// </summary>
public sealed class FinanceCashAccountHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = FinanceEndpoints.CashAccountsPath;
    private const string CandidatesPath = FinanceEndpoints.CashAccountCurrencyCandidatesPath;
    private const string GroupAdminUserId = "20000000-0000-0000-0000-000000000003";
    private static readonly Guid AccountId = Guid.Parse("30a00000-0000-4000-8000-000000000001");
    private static readonly Guid CurrencyId = Guid.Parse("30a00000-0000-4000-8000-000000000002");
    private static string DetailPath => $"{Path}/{AccountId:D}";
    private static string ApprovalPath => $"{DetailPath}/approval";
    private static string RejectionPath => $"{DetailPath}/rejection";

    [Fact]
    public async Task Registration_grants_nothing_and_the_real_owner_refuses_every_route()
    {
        // The unmodified host: the real owner behind the real permission port. No role holds a
        // cash-account permission, so even the group administrator is refused before anything is read.
        using var client = Client(factory);
        using var list = await client.GetAsync(Path);
        using var detail = await client.GetAsync(DetailPath);
        using var candidates = await client.GetAsync(CandidatesPath);
        using var propose = await PostAsync(client, Path, new { displayName = "SENTETIK Kasa", currencyId = CurrencyId });
        using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "1" });
        using var reject = await PostAsync(client, RejectionPath, new { expectedVersion = "1", reason = "SENTETIK" });

        foreach (var response in new[] { list, detail, candidates, propose, approve, reject })
        {
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "forbidden");
            PrivateNoStore(response);
        }
    }

    [Fact]
    public async Task An_anonymous_caller_and_a_request_without_a_company_never_reach_the_owner()
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var anonymous = host.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        using var withoutCompany = Client(host, includeCompany: false);

        foreach (var (client, expected) in new[]
                 {
                     (anonymous, HttpStatusCode.Unauthorized),
                     (withoutCompany, HttpStatusCode.BadRequest),
                 })
        {
            using var list = await client.GetAsync(Path);
            using var detail = await client.GetAsync(DetailPath);
            using var candidates = await client.GetAsync(CandidatesPath);
            using var propose = await PostAsync(client, Path, new { displayName = "SENTETIK Kasa", currencyId = CurrencyId });
            using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "1" });
            using var reject = await PostAsync(client, RejectionPath, new { expectedVersion = "1", reason = "SENTETIK" });
            foreach (var response in new[] { list, detail, candidates, propose, approve, reject })
            {
                Assert.Equal(expected, response.StatusCode);
            }
        }

        Assert.Equal(0, master.Calls);
    }

    [Fact]
    public async Task A_company_the_caller_does_not_belong_to_never_reaches_the_owner()
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var client = Client(host, TestUsers.CompanyA, TestCompanyIds.CompanyB);

        using var list = await client.GetAsync(Path);
        using var detail = await client.GetAsync(DetailPath);
        using var propose = await PostAsync(client, Path, new { displayName = "SENTETIK Kasa", currencyId = CurrencyId });
        using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "1" });

        foreach (var response in new[] { list, detail, propose, approve })
        {
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "forbidden");
        }

        Assert.Equal(0, master.Calls);
    }

    [Fact]
    public async Task The_company_and_actor_are_the_servers_and_each_route_hands_its_own_request_to_the_owner()
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var client = Client(host);
        var correlation = client.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single();

        using var propose = await PostAsync(client, Path,
            new { displayName = "SENTETIK Kasa", currencyId = CurrencyId }, "key-propose");
        Assert.Equal(HttpStatusCode.OK, propose.StatusCode);
        Assert.Equal(new CashAccountProposeRequest("SENTETIK Kasa", CurrencyId), master.Proposed);
        Assert.Equal("key-propose", master.Metadata?.IdempotencyKey);

        using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "7" }, "key-approve");
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(new CashAccountApproveRequest(AccountId, "7"), master.Approved);
        Assert.Equal("key-approve", master.Metadata?.IdempotencyKey);

        using var reject = await PostAsync(client, RejectionPath,
            new { expectedVersion = "8", reason = "SENTETIK gerekce" }, "key-reject");
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(new CashAccountRejectRequest(AccountId, "8", "SENTETIK gerekce"), master.Rejected);
        Assert.Equal("key-reject", master.Metadata?.IdempotencyKey);

        using var detail = await client.GetAsync(DetailPath);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(AccountId, master.ReadAccountId);

        using var list = await client.GetAsync(Path);
        using var candidates = await client.GetAsync(CandidatesPath);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);

        Assert.Equal(6, master.Calls);
        Assert.All(master.Contexts, context =>
        {
            // Bootstrap subjects are resolved to the Identity user id before the Finance owner is called.
            Assert.Equal(GroupAdminUserId, context.ActorId);
            Assert.Equal(TestCompanyIds.CompanyA, context.CompanyId);
            Assert.Equal(correlation, context.CorrelationId);
        });
        foreach (var response in new[] { propose, approve, reject, detail, list, candidates })
        {
            PrivateNoStore(response);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }
    }

    [Fact]
    public async Task The_answers_carry_the_owners_values_unchanged()
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var client = Client(host);

        using var propose = await PostAsync(client, Path, new { displayName = "SENTETIK Kasa", currencyId = CurrencyId });
        var result = await propose.Content.ReadFromJsonAsync<CashAccountCommandResult>(FinanceJson.CreateOptions());
        Assert.Equal(CommandOutcome.Succeeded, result?.Outcome);
        Assert.Equal(CapturingMaster.View, result?.Resource);
        Assert.False(result?.IsReplay);
        Assert.Empty(result?.Issues ?? [new ValidationIssue("missing", "missing")]);

        using var detail = await client.GetAsync(DetailPath);
        Assert.Equal(CapturingMaster.View,
            await detail.Content.ReadFromJsonAsync<CashAccountView>(FinanceJson.CreateOptions()));

        using var list = await client.GetAsync(Path);
        var page = await list.Content.ReadFromJsonAsync<CashAccountMasterPage>(FinanceJson.CreateOptions());
        Assert.Equal(TestCompanyIds.CompanyA, page?.CompanyId);
        Assert.Equal(new CashAccountMasterItem(CapturingMaster.View, true), Assert.Single(page?.Items ?? []));

        using var candidates = await client.GetAsync(CandidatesPath);
        var currencies = await candidates.Content.ReadFromJsonAsync<CashAccountCurrencyCandidates>(FinanceJson.CreateOptions());
        Assert.Equal(TestCompanyIds.CompanyA, currencies?.CompanyId);
        Assert.Equal(new CashAccountCurrencyCandidate(CurrencyId, "SENTETIK", "SENTETIK para birimi"),
            Assert.Single(currencies?.Items ?? []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a,b")]
    [InlineData("129")]
    public async Task A_command_without_exactly_one_canonical_idempotency_key_is_refused_before_the_owner(string? key)
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var client = Client(host);
        var value = key == "129" ? new string('k', 129) : key;

        using var propose = await PostAsync(client, Path, new { displayName = "SENTETIK Kasa", currencyId = CurrencyId }, value);
        using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "1" }, value);
        using var reject = await PostAsync(client, RejectionPath, new { expectedVersion = "1", reason = "SENTETIK" }, value);

        foreach (var response in new[] { propose, approve, reject })
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, code: null);
            PrivateNoStore(response);
        }

        Assert.Equal(0, master.Calls);
    }

    [Fact]
    public async Task The_longest_allowed_idempotency_key_reaches_the_owner_as_sent()
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var client = Client(host);
        var key = new string('k', 128);

        using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "1" }, key);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(key, master.Metadata?.IdempotencyKey);
    }

    [Theory]
    [InlineData("propose", "{")]
    [InlineData("propose", "null")]
    [InlineData("propose", """{"displayName":"SENTETIK Kasa"}""")]
    [InlineData("propose", """{"displayName":"SENTETIK Kasa","currencyId":"not-a-guid"}""")]
    [InlineData("propose", """{"displayName":"SENTETIK Kasa","currencyId":"30a00000-0000-4000-8000-000000000002","companyId":"10000000-0000-0000-0000-000000000002"}""")]
    [InlineData("propose", """{"displayName":"SENTETIK Kasa","currencyId":"30a00000-0000-4000-8000-000000000002","openingBalance":"1"}""")]
    [InlineData("approval", """{}""")]
    [InlineData("approval", """{"expectedVersion":"1","accountId":"30a00000-0000-4000-8000-000000000009"}""")]
    [InlineData("rejection", """{"expectedVersion":"1"}""")]
    [InlineData("rejection", """{"expectedVersion":"1","reason":"SENTETIK","approvedBy":"x"}""")]
    public async Task A_malformed_incomplete_or_over_posted_body_is_refused_before_the_owner(string route, string json)
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var client = Client(host);

        using var response = await PostRawAsync(client, RoutePath(route), json, "application/json");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code: null);
        PrivateNoStore(response);
        Assert.Equal(0, master.Calls);
    }

    [Theory]
    [InlineData("propose")]
    [InlineData("approval")]
    [InlineData("rejection")]
    public async Task A_body_that_is_not_JSON_is_an_unsupported_media_type(string route)
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var client = Client(host);

        using var response = await PostRawAsync(client, RoutePath(route), "expectedVersion=1", "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, master.Calls);
    }

    [Theory]
    [InlineData(CommandOutcome.ValidationFailed, 422, "currency_not_a_candidate")]
    [InlineData(CommandOutcome.ValidationFailed, 422, "cash_account_name_taken")]
    [InlineData(CommandOutcome.ValidationFailed, 422, "cash_account_not_found")]
    [InlineData(CommandOutcome.ValidationFailed, 422, "cash_account_not_proposed")]
    [InlineData(CommandOutcome.VersionConflict, 409, "version_conflict")]
    [InlineData(CommandOutcome.VersionConflict, 409, "concurrent_change")]
    [InlineData(CommandOutcome.IdempotencyConflict, 409, null)]
    public async Task A_refusal_a_client_acts_on_carries_the_owners_result_and_issue_code(CommandOutcome outcome,
        int status, string? issue)
    {
        var master = new CapturingMaster { Outcome = outcome, IssueCode = issue };
        using var host = Host(master);
        using var client = Client(host);

        using var propose = await PostAsync(client, Path, new { displayName = "SENTETIK Kasa", currencyId = CurrencyId });
        using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "1" });
        using var reject = await PostAsync(client, RejectionPath, new { expectedVersion = "1", reason = "SENTETIK" });

        foreach (var response in new[] { propose, approve, reject })
        {
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            PrivateNoStore(response);
            var result = await response.Content.ReadFromJsonAsync<CashAccountCommandResult>(FinanceJson.CreateOptions());
            Assert.Equal(outcome, result?.Outcome);
            Assert.Null(result?.Resource);
            Assert.Equal(issue is null ? [] : new[] { issue }, result?.Issues.Select(value => value.Code).ToArray());
        }
    }

    [Theory]
    [InlineData(CommandOutcome.Denied, 403, "forbidden")]
    [InlineData(CommandOutcome.Unavailable, 503, "service_unavailable")]
    public async Task A_denied_or_unavailable_command_is_the_platforms_problem_and_tells_nothing_else(
        CommandOutcome outcome, int status, string code)
    {
        var master = new CapturingMaster { Outcome = outcome, IssueCode = "finance_approver_is_proposer" };
        using var host = Host(master);
        using var client = Client(host);

        using var propose = await PostAsync(client, Path, new { displayName = "SENTETIK Kasa", currencyId = CurrencyId });
        using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "1" });
        using var reject = await PostAsync(client, RejectionPath, new { expectedVersion = "1", reason = "SENTETIK" });

        foreach (var response in new[] { propose, approve, reject })
        {
            var body = await AssertProblemAsync(response, (HttpStatusCode)status, code);
            PrivateNoStore(response);
            Assert.DoesNotContain("finance_approver_is_proposer", body, StringComparison.Ordinal);
            Assert.DoesNotContain(AccountId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(CommandOutcome.Succeeded, 200)]
    [InlineData(CommandOutcome.ValidationFailed, 422)]
    [InlineData(CommandOutcome.VersionConflict, 409)]
    public async Task A_replayed_answer_says_so_and_names_the_first_correlation(CommandOutcome outcome, int status)
    {
        var master = new CapturingMaster
        {
            Outcome = outcome,
            IsReplay = true,
            OriginalCorrelationId = "sentetik-first-correlation",
            IssueCode = outcome == CommandOutcome.Succeeded ? null : "sentetik_recorded_refusal",
        };
        using var host = Host(master);
        using var client = Client(host);

        using var propose = await PostAsync(client, Path, new { displayName = "SENTETIK Kasa", currencyId = CurrencyId });
        using var approve = await PostAsync(client, ApprovalPath, new { expectedVersion = "1" });
        using var reject = await PostAsync(client, RejectionPath, new { expectedVersion = "1", reason = "SENTETIK" });

        foreach (var response in new[] { propose, approve, reject })
        {
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal("sentetik-first-correlation",
                response.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
            var result = await response.Content.ReadFromJsonAsync<CashAccountCommandResult>(FinanceJson.CreateOptions());
            Assert.True(result?.IsReplay);
            Assert.Equal("sentetik-first-correlation", result?.OriginalCorrelationId);
        }
    }

    [Theory]
    [InlineData(ReadOutcome.Denied, 403, "forbidden")]
    [InlineData(ReadOutcome.Unavailable, 503, "service_unavailable")]
    [InlineData(ReadOutcome.NotFound, 404, null)]
    public async Task A_read_failure_exposes_only_the_public_error_boundary(ReadOutcome outcome, int status, string? code)
    {
        var master = new CapturingMaster { Read = outcome };
        using var host = Host(master);
        using var client = Client(host);

        using var detail = await client.GetAsync(DetailPath);
        using var list = await client.GetAsync(Path);
        using var candidates = await client.GetAsync(CandidatesPath);

        foreach (var response in new[] { detail, list, candidates })
        {
            var body = await AssertProblemAsync(response, (HttpStatusCode)status, code);
            PrivateNoStore(response);
            Assert.DoesNotContain(AccountId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task An_account_id_that_is_not_a_guid_matches_no_route_and_the_master_has_no_other_verb()
    {
        var master = new CapturingMaster();
        using var host = Host(master);
        using var client = Client(host);

        using var detail = await client.GetAsync($"{Path}/not-a-guid");
        using var approve = await PostAsync(client, $"{Path}/not-a-guid/approval", new { expectedVersion = "1" });
        using var put = await client.PutAsJsonAsync(DetailPath, new { displayName = "SENTETIK Kasa" });
        using var delete = await client.DeleteAsync(DetailPath);

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        Assert.Equal(0, master.Calls);
    }

    private static string RoutePath(string route) => route switch
    {
        "propose" => Path,
        "approval" => ApprovalPath,
        "rejection" => RejectionPath,
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private WebApplicationFactory<Program> Host(CapturingMaster master) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFinanceCashAccountMaster>();
            services.AddSingleton<IFinanceCashAccountMaster>(master);
        }));

    private static HttpClient Client(WebApplicationFactory<Program> host, string user = TestUsers.GroupAdmin,
        Guid? company = null, bool includeCompany = true)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", user);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        if (includeCompany)
        {
            client.DefaultRequestHeaders.Add("X-Company-Id", (company ?? TestCompanyIds.CompanyA).ToString("D"));
        }

        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body,
        string? key = "sentetik-key") =>
        PostRawAsync(client, path, JsonSerializer.Serialize(body), "application/json", key);

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string path, string body,
        string mediaType, string? key = "sentetik-key")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? code)
    {
        Assert.Equal(status, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        if (code is null)
        {
            Assert.False(body.RootElement.TryGetProperty("code", out _));
        }
        else
        {
            Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        }

        return text;
    }

    private static void PrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class CapturingMaster : IFinanceCashAccountMaster
    {
        public static readonly CashAccountView View = new(AccountId, TestCompanyIds.CompanyA, CurrencyId,
            "SENTETIK Kasa", CashAccountStatus.Proposed, false, null, "1");

        public CommandOutcome Outcome { get; init; } = CommandOutcome.Succeeded;
        public ReadOutcome Read { get; init; } = ReadOutcome.Found;
        public string? IssueCode { get; init; }
        public bool IsReplay { get; init; }
        public string OriginalCorrelationId { get; init; } = "sentetik-correlation";
        public int Calls => Contexts.Count;
        public List<FinanceContext> Contexts { get; } = [];
        public CommandMetadata? Metadata { get; private set; }
        public CashAccountProposeRequest? Proposed { get; private set; }
        public CashAccountApproveRequest? Approved { get; private set; }
        public CashAccountRejectRequest? Rejected { get; private set; }
        public Guid? ReadAccountId { get; private set; }

        public Task<CashAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountProposeRequest request, CancellationToken cancellationToken)
        {
            Proposed = request;
            return Task.FromResult(Result(context, metadata));
        }

        public Task<CashAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountApproveRequest request, CancellationToken cancellationToken)
        {
            Approved = request;
            return Task.FromResult(Result(context, metadata));
        }

        public Task<CashAccountCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
            CashAccountRejectRequest request, CancellationToken cancellationToken)
        {
            Rejected = request;
            return Task.FromResult(Result(context, metadata));
        }

        public Task<ReadResult<CashAccountMasterPage>> ListAsync(FinanceContext context,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return Task.FromResult(new ReadResult<CashAccountMasterPage>(Read,
                Read == ReadOutcome.Found ? new(context.CompanyId, [new(View, true)]) : null));
        }

        public Task<ReadResult<CashAccountView>> GetAsync(FinanceContext context, Guid accountId,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            ReadAccountId = accountId;
            return Task.FromResult(new ReadResult<CashAccountView>(Read, Read == ReadOutcome.Found ? View : null));
        }

        public Task<ReadResult<CashAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return Task.FromResult(new ReadResult<CashAccountCurrencyCandidates>(Read,
                Read == ReadOutcome.Found
                    ? new(context.CompanyId, [new(CurrencyId, "SENTETIK", "SENTETIK para birimi")])
                    : null));
        }

        private CashAccountCommandResult Result(FinanceContext context, CommandMetadata metadata)
        {
            Contexts.Add(context);
            Metadata = metadata;
            return new(Outcome, Outcome == CommandOutcome.Succeeded ? View : null, IsReplay, OriginalCorrelationId,
                IssueCode is null ? [] : [new ValidationIssue("SENTETIK", IssueCode)]);
        }
    }
}
