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
/// FNS-D25 / FNS-D47 (FIN-S15): the seven expense-payment routes over HTTP — the draft save, the approval and the
/// record, the list, the detail, the expense items and the draft context. The owner
/// (<see cref="IExpensePaymentService"/>) is a capturing stand-in, so these facts hold the route's own duties:
/// authentication and company context, the Idempotency-Key and strict-body gates, route → request mapping with a
/// server-resolved context, the draft-context query gate, and outcome → status / problem code / replay headers.
/// The owner's rules (the preparer is never the approver, the fail-closed balance guard, idempotency, the company
/// boundary and the race) are proven on real SQL in <c>FinanceExpensePaymentSqlServerRelationalAcceptanceTests</c>.
/// Every identity, amount and account below is SENTETIK / NOT_ENV test data.
/// </summary>
public sealed class FinanceExpensePaymentHttpAcceptanceTests(GoymenErpApiFactory factory)
    : IClassFixture<GoymenErpApiFactory>
{
    private const string Path = FinanceEndpoints.ExpensePaymentsPath;
    private const string ItemsPath = FinanceEndpoints.ExpenseItemsPath;
    private const string DraftContextPath = FinanceEndpoints.ExpensePaymentDraftContextPath;
    private const string PaymentText = "5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a01";
    private const string TreasuryText = "5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02";
    private const string ItemText = "5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03";
    private const string CurrencyText = "5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a04";
    private const string ProfileText = "5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a05";
    private static readonly Guid PaymentId = Guid.Parse(PaymentText);
    private static readonly Guid TreasuryId = Guid.Parse(TreasuryText);
    private static readonly Guid ItemId = Guid.Parse(ItemText);
    private static readonly Guid CurrencyId = Guid.Parse(CurrencyText);
    private static readonly DateOnly OccurredOn = new(2026, 11, 10);
    private static string DetailPath => $"{Path}/{PaymentId:D}";
    private static string ApprovalPath => $"{DetailPath}/approval";
    private static string RecordPath => $"{DetailPath}/record";
    private static string ValidDraftContextQuery =>
        $"{DraftContextPath}?sourceKind=Cash&treasuryAccountId={TreasuryText}&occurredOn=2026-11-10";

    private static readonly ExpensePaymentPreparation Preparation = new(ExpensePaymentSourceKind.Bank, TreasuryId, ItemId,
        new MoneyText("1250.50", CurrencyId), new ProfileReference(ProfileText, "1"), OccurredOn, null, null,
        "SENTETIK kirtasiye");

    private static readonly object PreparationBody = new
    {
        sourceKind = "Bank",
        treasuryAccountId = TreasuryId,
        expenseItemId = ItemId,
        money = new { amount = "1250.50", currencyId = CurrencyId },
        profile = new { profileId = ProfileText, version = "1" },
        occurredOn = "2026-11-10",
        valueOn = (string?)null,
        reference = (string?)null,
        description = "SENTETIK kirtasiye",
    };

    // Every command route with a well-formed body, in a fixed order: the draft save, the approval, the record.
    private static readonly (string Route, object Body)[] Commands =
    [
        ("draft", new { paymentId = (Guid?)null, expectedVersion = (string?)null, preparation = PreparationBody }),
        ("approval", new { paymentId = PaymentId, expectedVersion = "1" }),
        ("record", new { paymentId = PaymentId, expectedVersion = "2", paymentConfirmed = true }),
    ];

    [Fact]
    public async Task Registration_grants_nothing_and_the_real_owner_refuses_every_route()
    {
        // The unmodified host: the real owner behind the real permission port. No role holds an expense-payment
        // permission, so even the group administrator is refused before anything is read or written.
        using var client = Client(factory);
        using var list = await client.GetAsync(Path);
        using var detail = await client.GetAsync(DetailPath);
        using var items = await client.GetAsync(ItemsPath);
        using var draftContext = await client.GetAsync(ValidDraftContextQuery);
        using var commands = await PostEveryCommandAsync(client);

        Assert.Equal(Commands.Length, commands.Count);
        foreach (var response in commands.Concat([list, detail, items, draftContext]))
        {
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "forbidden");
            PrivateNoStore(response);
        }
    }

    [Fact]
    public async Task An_anonymous_caller_and_a_request_without_a_company_never_reach_the_owner()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var anonymous = host.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        using var withoutCompany = Client(host, includeCompany: false);

        foreach (var (client, expected) in new[]
                 {
                     (anonymous, HttpStatusCode.Unauthorized),
                     (withoutCompany, HttpStatusCode.BadRequest),
                 })
        {
            using var reads = await GetEveryReadAsync(client);
            using var commands = await PostEveryCommandAsync(client);
            foreach (var response in commands.Concat(reads))
            {
                Assert.Equal(expected, response.StatusCode);
            }
        }

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task A_company_the_caller_does_not_belong_to_never_reaches_the_owner()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host, TestUsers.CompanyA, TestCompanyIds.CompanyB);

        using var reads = await GetEveryReadAsync(client);
        using var commands = await PostEveryCommandAsync(client);

        foreach (var response in commands.Concat(reads))
        {
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "forbidden");
        }

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task The_company_and_actor_are_the_servers_and_each_route_hands_its_own_request_to_the_owner()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);
        var correlation = client.DefaultRequestHeaders.GetValues("X-Correlation-Id").Single();

        using var draft = await PostAsync(client, Path,
            new { paymentId = (Guid?)null, expectedVersion = (string?)null, preparation = PreparationBody }, "key-draft");
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        Assert.Equal(new ExpensePaymentDraftSaveRequest(null, null, Preparation), service.Drafted);
        Assert.Equal("key-draft", service.Metadata?.IdempotencyKey);

        // An update of an existing draft carries its id, version and the optional fields as sent.
        using var update = await PostAsync(client, Path, new
        {
            paymentId = PaymentId,
            expectedVersion = "3",
            preparation = new
            {
                sourceKind = "Cash",
                treasuryAccountId = TreasuryId,
                expenseItemId = ItemId,
                money = new { amount = "75.25", currencyId = CurrencyId },
                profile = new { profileId = ProfileText, version = "2" },
                occurredOn = "2026-11-10",
                valueOn = "2026-11-11",
                reference = "SENTETIK-REF",
                description = "SENTETIK kargo",
            },
        }, "key-update");
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(new ExpensePaymentDraftSaveRequest(PaymentId, "3", new ExpensePaymentPreparation(
                ExpensePaymentSourceKind.Cash, TreasuryId, ItemId, new MoneyText("75.25", CurrencyId),
                new ProfileReference(ProfileText, "2"), OccurredOn, new DateOnly(2026, 11, 11), "SENTETIK-REF",
                "SENTETIK kargo")),
            service.Drafted);
        Assert.Equal("key-update", service.Metadata?.IdempotencyKey);

        using var approve = await PostAsync(client, ApprovalPath,
            new { paymentId = PaymentId, expectedVersion = "7" }, "key-approve");
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(new ApproveExpensePaymentRequest(PaymentId, "7"), service.Approved);
        Assert.Equal("key-approve", service.Metadata?.IdempotencyKey);

        using var record = await PostAsync(client, RecordPath,
            new { paymentId = PaymentId, expectedVersion = "8", paymentConfirmed = true }, "key-record");
        Assert.Equal(HttpStatusCode.OK, record.StatusCode);
        Assert.Equal(new RecordExpensePaymentRequest(PaymentId, "8", true), service.Recorded);
        Assert.Equal("key-record", service.Metadata?.IdempotencyKey);

        using var detail = await client.GetAsync(DetailPath);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(PaymentId, service.ReadPaymentId);

        using var list = await client.GetAsync(Path);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Null(service.Cursor);
        using var nextPage = await client.GetAsync($"{Path}?cursor=sentetik-cursor");
        Assert.Equal(HttpStatusCode.OK, nextPage.StatusCode);
        Assert.Equal("sentetik-cursor", service.Cursor);

        using var items = await client.GetAsync(ItemsPath);
        Assert.Equal(HttpStatusCode.OK, items.StatusCode);

        using var draftContext = await client.GetAsync(ValidDraftContextQuery);
        Assert.Equal(HttpStatusCode.OK, draftContext.StatusCode);
        Assert.Equal<(ExpensePaymentSourceKind, Guid, DateOnly)?>((ExpensePaymentSourceKind.Cash, TreasuryId, OccurredOn),
            service.DraftContextQuery);

        // Four commands (including the draft update) and five reads (including the next page), each forwarded once.
        Assert.Equal(9, service.Calls);
        // Bootstrap subjects are resolved to the host's user id before the owner is called: never empty, never
        // request data, and the same for every route.
        Assert.Single(service.Contexts.Select(context => context.ActorId).Distinct(StringComparer.Ordinal));
        Assert.All(service.Contexts, context =>
        {
            Assert.False(string.IsNullOrWhiteSpace(context.ActorId));
            Assert.Equal(TestCompanyIds.CompanyA, context.CompanyId);
            Assert.Equal(correlation, context.CorrelationId);
        });
        foreach (var response in new[] { draft, update, approve, record, detail, list, nextPage, items, draftContext })
        {
            PrivateNoStore(response);
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }
    }

    [Fact]
    public async Task The_answers_carry_the_owners_values_unchanged()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);

        using var approve = await PostAsync(client, ApprovalPath, new { paymentId = PaymentId, expectedVersion = "1" });
        var result = await approve.Content.ReadFromJsonAsync<ExpensePaymentCommandResult>(FinanceJson.CreateOptions());
        Assert.Equal(CommandOutcome.Succeeded, result?.Outcome);
        Assert.Equal(CapturingService.View, result?.Resource);
        Assert.False(result?.IsReplay);
        Assert.Empty(result?.Issues ?? [new ValidationIssue("missing", "missing")]);

        using var detail = await client.GetAsync(DetailPath);
        Assert.Equal(CapturingService.View,
            await detail.Content.ReadFromJsonAsync<ExpensePaymentView>(FinanceJson.CreateOptions()));

        using var list = await client.GetAsync(Path);
        var page = await list.Content.ReadFromJsonAsync<ExpensePaymentPage>(FinanceJson.CreateOptions());
        Assert.Equal(TestCompanyIds.CompanyA, page?.CompanyId);
        Assert.Equal(CapturingService.View, Assert.Single(page?.Items ?? []));
        Assert.Equal("sentetik-next", page?.NextCursor);

        using var items = await client.GetAsync(ItemsPath);
        var catalog = await items.Content.ReadFromJsonAsync<ExpenseItemCatalogView>(FinanceJson.CreateOptions());
        Assert.Equal(TestCompanyIds.CompanyA, catalog?.CompanyId);
        Assert.Equal(new ExpenseItemView(ItemId, "general-expense"), Assert.Single(catalog?.Items ?? []));

        using var draftContext = await client.GetAsync(ValidDraftContextQuery);
        var text = await draftContext.Content.ReadAsStringAsync();
        Assert.Equal(new ExpensePaymentDraftContextView(TestCompanyIds.CompanyA, ExpensePaymentSourceKind.Cash, TreasuryId,
                CurrencyId, new ProfileReference(ProfileText, "1"), OccurredOn),
            JsonSerializer.Deserialize<ExpensePaymentDraftContextView>(text, FinanceJson.CreateOptions()));
        using var json = JsonDocument.Parse(text);
        Assert.Equal("Cash", json.RootElement.GetProperty("sourceKind").GetString());
        Assert.Equal("2026-11-10", json.RootElement.GetProperty("occurredOn").GetString());
    }

    [Fact]
    public async Task A_route_payment_id_that_differs_from_the_body_is_refused_before_the_owner()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);
        var other = Guid.Parse("5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a09");

        using var approve = await PostAsync(client, ApprovalPath, new { paymentId = other, expectedVersion = "1" });
        using var record = await PostAsync(client, RecordPath,
            new { paymentId = other, expectedVersion = "2", paymentConfirmed = true });

        foreach (var response in new[] { approve, record })
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, code: null);
            PrivateNoStore(response);
        }

        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a,b")]
    [InlineData("129")]
    public async Task A_command_without_exactly_one_canonical_idempotency_key_is_refused_before_the_owner(string? key)
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);
        var value = key == "129" ? new string('k', 129) : key;

        using var commands = await PostEveryCommandAsync(client, value);

        Assert.Equal(Commands.Length, commands.Count);
        foreach (var response in commands)
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, code: null);
            PrivateNoStore(response);
        }

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task The_longest_allowed_idempotency_key_reaches_the_owner_as_sent()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);
        var key = new string('k', 128);

        using var commands = await PostEveryCommandAsync(client, key);

        Assert.Equal(Commands.Length, commands.Count);
        foreach (var response in commands)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(key, service.Metadata?.IdempotencyKey);
        Assert.Equal(Commands.Length, service.Calls);
    }

    [Theory]
    [InlineData("draft", "{")]
    [InlineData("draft", "null")]
    [InlineData("draft", """{"paymentId":null,"expectedVersion":null}""")]
    [InlineData("draft", """{"expectedVersion":null,"preparation":{"sourceKind":"Bank","treasuryAccountId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02","expenseItemId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03","money":{"amount":"1250.50","currencyId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a04"},"profile":{"profileId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a05","version":"1"},"occurredOn":"2026-11-10","valueOn":null,"reference":null,"description":"SENTETIK"}}""")]
    [InlineData("draft", """{"paymentId":null,"expectedVersion":null,"preparation":{"sourceKind":0,"treasuryAccountId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02","expenseItemId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03","money":{"amount":"1250.50","currencyId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a04"},"profile":{"profileId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a05","version":"1"},"occurredOn":"2026-11-10","valueOn":null,"reference":null,"description":"SENTETIK"}}""")]
    [InlineData("draft", """{"paymentId":null,"expectedVersion":null,"preparation":{"sourceKind":"Bank","treasuryAccountId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02","expenseItemId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03","money":{"amount":1250.50,"currencyId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a04"},"profile":{"profileId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a05","version":"1"},"occurredOn":"2026-11-10","valueOn":null,"reference":null,"description":"SENTETIK"}}""")]
    [InlineData("draft", """{"paymentId":null,"expectedVersion":null,"preparation":{"sourceKind":"Bank","treasuryAccountId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02","expenseItemId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03","money":{"amount":"1250.50","currencyId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a04"},"profile":{"profileId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a05","version":"1"},"occurredOn":"10.11.2026","valueOn":null,"reference":null,"description":"SENTETIK"}}""")]
    [InlineData("draft", """{"paymentId":null,"expectedVersion":null,"preparation":{"sourceKind":"Bank","treasuryAccountId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02","expenseItemId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03","money":{"amount":"1250.50","currencyId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a04"},"profile":{"profileId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a05","version":"1"},"occurredOn":"2026-11-10","reference":null,"description":"SENTETIK"}}""")]
    [InlineData("draft", """{"paymentId":null,"expectedVersion":null,"companyId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a09","preparation":{"sourceKind":"Bank","treasuryAccountId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02","expenseItemId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03","money":{"amount":"1250.50","currencyId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a04"},"profile":{"profileId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a05","version":"1"},"occurredOn":"2026-11-10","valueOn":null,"reference":null,"description":"SENTETIK"}}""")]
    [InlineData("draft", """{"paymentId":null,"expectedVersion":null,"preparation":{"sourceKind":"Bank","treasuryAccountId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02","expenseItemId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03","money":{"amount":"1250.50","currencyId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a04"},"profile":{"profileId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a05","version":"1"},"occurredOn":"2026-11-10","valueOn":null,"reference":null,"description":"SENTETIK","preparedBy":"x"}}""")]
    [InlineData("approval", "null")]
    [InlineData("approval", """{}""")]
    [InlineData("approval", """{"paymentId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a01","expectedVersion":null}""")]
    [InlineData("approval", """{"paymentId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a01","expectedVersion":1}""")]
    [InlineData("approval", """{"paymentId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a01","expectedVersion":"1","approvedBy":"x"}""")]
    [InlineData("record", "{")]
    [InlineData("record", """{"paymentId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a01","expectedVersion":"2"}""")]
    [InlineData("record", """{"paymentId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a01","expectedVersion":"2","paymentConfirmed":"true"}""")]
    [InlineData("record", """{"paymentId":"5e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a01","expectedVersion":"2","paymentConfirmed":true,"recordedBy":"x"}""")]
    public async Task A_malformed_incomplete_or_over_posted_body_is_refused_before_the_owner(string route, string json)
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);

        using var response = await PostRawAsync(client, RoutePath(route), json, "application/json");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code: null);
        PrivateNoStore(response);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("approval")]
    [InlineData("record")]
    public async Task A_body_that_is_not_JSON_is_an_unsupported_media_type(string route)
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);

        using var response = await PostRawAsync(client, RoutePath(route), "expectedVersion=1", "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(CommandOutcome.ValidationFailed, 422, "payment_approval_invalid")]
    [InlineData(CommandOutcome.ValidationFailed, 422, "payment_not_found")]
    [InlineData(CommandOutcome.ValidationFailed, 422, "payment_not_draft")]
    [InlineData(CommandOutcome.ValidationFailed, 422, "finance_treasury_balance_insufficient")]
    [InlineData(CommandOutcome.ValidationFailed, 422, "finance_period_not_open")]
    [InlineData(CommandOutcome.VersionConflict, 409, "version_conflict")]
    [InlineData(CommandOutcome.VersionConflict, 409, "concurrent_change")]
    [InlineData(CommandOutcome.IdempotencyConflict, 409, null)]
    public async Task A_refusal_a_client_acts_on_carries_the_owners_result_and_issue_code(CommandOutcome outcome,
        int status, string? issue)
    {
        var service = new CapturingService { Outcome = outcome, IssueCode = issue };
        using var host = Host(service);
        using var client = Client(host);

        using var commands = await PostEveryCommandAsync(client);

        Assert.Equal(Commands.Length, commands.Count);
        foreach (var response in commands)
        {
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            PrivateNoStore(response);
            var result = await response.Content.ReadFromJsonAsync<ExpensePaymentCommandResult>(FinanceJson.CreateOptions());
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
        // The owner's own reason (a self-approval, or a balance that cannot be read) stays in the service.
        var service = new CapturingService
        {
            Outcome = outcome,
            IssueCode = outcome == CommandOutcome.Denied
                ? "payment_approval_invalid"
                : TreasuryBalanceAnswer.UnavailableCode,
        };
        using var host = Host(service);
        using var client = Client(host);

        using var commands = await PostEveryCommandAsync(client);

        Assert.Equal(Commands.Length, commands.Count);
        foreach (var response in commands)
        {
            var body = await AssertProblemAsync(response, (HttpStatusCode)status, code);
            PrivateNoStore(response);
            Assert.DoesNotContain("payment_approval_invalid", body, StringComparison.Ordinal);
            Assert.DoesNotContain(TreasuryBalanceAnswer.UnavailableCode, body, StringComparison.Ordinal);
            Assert.DoesNotContain(PaymentText, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(CommandOutcome.Succeeded, 200)]
    [InlineData(CommandOutcome.ValidationFailed, 422)]
    [InlineData(CommandOutcome.VersionConflict, 409)]
    public async Task A_replayed_answer_says_so_and_names_the_first_correlation(CommandOutcome outcome, int status)
    {
        var service = new CapturingService
        {
            Outcome = outcome,
            IsReplay = true,
            OriginalCorrelationId = "sentetik-first-correlation",
            IssueCode = outcome == CommandOutcome.Succeeded ? null : "sentetik_recorded_refusal",
        };
        using var host = Host(service);
        using var client = Client(host);

        using var commands = await PostEveryCommandAsync(client);

        Assert.Equal(Commands.Length, commands.Count);
        foreach (var response in commands)
        {
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single());
            Assert.Equal("sentetik-first-correlation",
                response.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
            var result = await response.Content.ReadFromJsonAsync<ExpensePaymentCommandResult>(FinanceJson.CreateOptions());
            Assert.True(result?.IsReplay);
            Assert.Equal("sentetik-first-correlation", result?.OriginalCorrelationId);
        }
    }

    [Theory]
    [InlineData(ReadOutcome.Denied, 403, "forbidden")]
    [InlineData(ReadOutcome.Unavailable, 503, "service_unavailable")]
    public async Task A_read_failure_exposes_only_the_public_error_boundary(ReadOutcome outcome, int status, string code)
    {
        var service = new CapturingService { Read = outcome };
        using var host = Host(service);
        using var client = Client(host);

        using var reads = await GetEveryReadAsync(client);

        foreach (var response in reads)
        {
            var body = await AssertProblemAsync(response, (HttpStatusCode)status, code);
            PrivateNoStore(response);
            Assert.DoesNotContain(PaymentText, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TreasuryText, body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(reads.Count, service.Calls);
    }

    [Fact]
    public async Task A_payment_or_account_the_owner_does_not_find_is_a_code_less_not_found_and_a_stale_cursor_a_bad_request()
    {
        var service = new CapturingService { Read = ReadOutcome.NotFound };
        using var host = Host(service);
        using var client = Client(host);

        using var detail = await client.GetAsync(DetailPath);
        using var items = await client.GetAsync(ItemsPath);
        using var draftContext = await client.GetAsync(ValidDraftContextQuery);
        using var list = await client.GetAsync($"{Path}?cursor=sentetik-stale-cursor");

        foreach (var response in new[] { detail, items, draftContext })
        {
            var body = await AssertProblemAsync(response, HttpStatusCode.NotFound, code: null);
            PrivateNoStore(response);
            Assert.DoesNotContain(PaymentText, body, StringComparison.OrdinalIgnoreCase);
        }

        // The list answers NotFound only for a cursor it cannot read: that is the client's request, not a resource.
        await AssertProblemAsync(list, HttpStatusCode.BadRequest, code: null);
        PrivateNoStore(list);
        Assert.Equal("sentetik-stale-cursor", service.Cursor);
        Assert.Equal(4, service.Calls);
    }

    [Theory]
    [InlineData("sourceKind=bank&treasuryAccountId=" + TreasuryText + "&occurredOn=2026-11-10")]
    [InlineData("sourceKind=0&treasuryAccountId=" + TreasuryText + "&occurredOn=2026-11-10")]
    [InlineData("sourceKind=Transfer&treasuryAccountId=" + TreasuryText + "&occurredOn=2026-11-10")]
    [InlineData("treasuryAccountId=" + TreasuryText + "&occurredOn=2026-11-10")]
    [InlineData("sourceKind=Bank&treasuryAccountId=not-a-guid&occurredOn=2026-11-10")]
    [InlineData("sourceKind=Bank&treasuryAccountId=00000000-0000-0000-0000-000000000000&occurredOn=2026-11-10")]
    [InlineData("sourceKind=Bank&treasuryAccountId=5e1d2c3b4a5946879a0b1c2d3e4f5a02&occurredOn=2026-11-10")]
    [InlineData("sourceKind=Bank&occurredOn=2026-11-10")]
    [InlineData("sourceKind=Bank&treasuryAccountId=" + TreasuryText + "&occurredOn=10.11.2026")]
    [InlineData("sourceKind=Bank&treasuryAccountId=" + TreasuryText + "&occurredOn=2026-11-31")]
    [InlineData("sourceKind=Bank&treasuryAccountId=" + TreasuryText)]
    public async Task A_draft_context_query_that_is_not_exact_is_refused_before_the_owner(string query)
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);

        using var response = await client.GetAsync($"{DraftContextPath}?{query}");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code: null);
        PrivateNoStore(response);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task A_payment_id_that_is_not_a_guid_matches_no_route_and_the_payment_has_no_other_verb()
    {
        var service = new CapturingService();
        using var host = Host(service);
        using var client = Client(host);

        using var detail = await client.GetAsync($"{Path}/not-a-guid");
        using var approve = await PostAsync(client, $"{Path}/not-a-guid/approval",
            new { paymentId = PaymentId, expectedVersion = "1" });
        using var record = await PostAsync(client, $"{Path}/not-a-guid/record",
            new { paymentId = PaymentId, expectedVersion = "2", paymentConfirmed = true });
        using var unknownStep = await PostAsync(client, $"{DetailPath}/cancellation",
            new { paymentId = PaymentId, expectedVersion = "1" });
        using var put = await client.PutAsJsonAsync(DetailPath, new { expectedVersion = "1" });
        using var delete = await client.DeleteAsync(DetailPath);
        using var readApproval = await client.GetAsync(ApprovalPath);
        using var readRecord = await client.GetAsync(RecordPath);

        foreach (var response in new[] { detail, approve, record, unknownStep })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        foreach (var response in new[] { put, delete, readApproval, readRecord })
        {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        Assert.Equal(0, service.Calls);
    }

    private static string RoutePath(string route) => route switch
    {
        "draft" => Path,
        "approval" => ApprovalPath,
        "record" => RecordPath,
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static async Task<Responses> PostEveryCommandAsync(HttpClient client, string? key = "sentetik-key")
    {
        var responses = new Responses();
        foreach (var (route, body) in Commands)
        {
            responses.Add(await PostAsync(client, RoutePath(route), body, key));
        }

        return responses;
    }

    private static async Task<Responses> GetEveryReadAsync(HttpClient client)
    {
        var responses = new Responses();
        foreach (var path in new[] { DetailPath, Path, ItemsPath, ValidDraftContextQuery })
        {
            responses.Add(await client.GetAsync(path));
        }

        return responses;
    }

    private WebApplicationFactory<Program> Host(CapturingService service) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IExpensePaymentService>();
            services.AddSingleton<IExpensePaymentService>(service);
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

    private sealed class Responses : List<HttpResponseMessage>, IDisposable
    {
        public void Dispose()
        {
            foreach (var response in this)
            {
                response.Dispose();
            }
        }
    }

    private sealed class CapturingService : IExpensePaymentService
    {
        public static readonly ExpensePaymentView View = new(PaymentId, TestCompanyIds.CompanyA,
            ExpensePaymentStatus.Approved, Preparation, "SENTETIK-preparer",
            new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), "SENTETIK-approver",
            new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero), null, null, false, "2");

        public CommandOutcome Outcome { get; init; } = CommandOutcome.Succeeded;
        public ReadOutcome Read { get; init; } = ReadOutcome.Found;
        public string? IssueCode { get; init; }
        public bool IsReplay { get; init; }
        public string OriginalCorrelationId { get; init; } = "sentetik-correlation";
        public int Calls => Contexts.Count;
        public List<FinanceContext> Contexts { get; } = [];
        public CommandMetadata? Metadata { get; private set; }
        public ExpensePaymentDraftSaveRequest? Drafted { get; private set; }
        public ApproveExpensePaymentRequest? Approved { get; private set; }
        public RecordExpensePaymentRequest? Recorded { get; private set; }
        public Guid? ReadPaymentId { get; private set; }
        public string? Cursor { get; private set; }
        public (ExpensePaymentSourceKind, Guid, DateOnly)? DraftContextQuery { get; private set; }

        public Task<ExpensePaymentCommandResult> DraftSaveAsync(FinanceContext context, CommandMetadata metadata,
            ExpensePaymentDraftSaveRequest request, CancellationToken cancellationToken)
        {
            Drafted = request;
            return Task.FromResult(Result(context, metadata));
        }

        public Task<ExpensePaymentCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
            ApproveExpensePaymentRequest request, CancellationToken cancellationToken)
        {
            Approved = request;
            return Task.FromResult(Result(context, metadata));
        }

        public Task<ExpensePaymentCommandResult> RecordAsync(FinanceContext context, CommandMetadata metadata,
            RecordExpensePaymentRequest request, CancellationToken cancellationToken)
        {
            Recorded = request;
            return Task.FromResult(Result(context, metadata));
        }

        public Task<ReadResult<ExpensePaymentPage>> ListAuthorizedAsync(FinanceContext context, string? cursor,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            Cursor = cursor;
            return Task.FromResult(new ReadResult<ExpensePaymentPage>(Read,
                Read == ReadOutcome.Found ? new(context.CompanyId, [View], "sentetik-next") : null));
        }

        public Task<ReadResult<ExpensePaymentView>> DetailAuthorizedAsync(FinanceContext context, Guid paymentId,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            ReadPaymentId = paymentId;
            return Task.FromResult(new ReadResult<ExpensePaymentView>(Read, Read == ReadOutcome.Found ? View : null));
        }

        public Task<ReadResult<ExpenseItemCatalogView>> ExpenseItemsAuthorizedAsync(FinanceContext context,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return Task.FromResult(new ReadResult<ExpenseItemCatalogView>(Read,
                Read == ReadOutcome.Found ? new(context.CompanyId, [new(ItemId, "general-expense")]) : null));
        }

        public Task<ReadResult<ExpensePaymentDraftContextView>> DraftContextAuthorizedAsync(FinanceContext context,
            ExpensePaymentSourceKind sourceKind, Guid treasuryAccountId, DateOnly occurredOn,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            DraftContextQuery = (sourceKind, treasuryAccountId, occurredOn);
            return Task.FromResult(new ReadResult<ExpensePaymentDraftContextView>(Read,
                Read == ReadOutcome.Found
                    ? new(context.CompanyId, sourceKind, treasuryAccountId, CurrencyId,
                        new ProfileReference(ProfileText, "1"), occurredOn)
                    : null));
        }

        private ExpensePaymentCommandResult Result(FinanceContext context, CommandMetadata metadata)
        {
            Contexts.Add(context);
            Metadata = metadata;
            return new(Outcome, Outcome == CommandOutcome.Succeeded ? View : null, IsReplay, OriginalCorrelationId,
                IssueCode is null ? [] : [new ValidationIssue("SENTETIK", IssueCode)]);
        }
    }
}
