using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FinanceDraftContextHttpAcceptanceTests(
    GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid TreasuryAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CurrencyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PartyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CompanyAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateOnly OccurredOn = new(2026, 9, 13);
    private static string Endpoint => FinanceEndpoints.DraftContextPath
        + $"?treasuryAccountId={TreasuryAccountId:D}&partyId={PartyId:D}"
        + $"&companyAccountId={CompanyAccountId:D}&occurredOn={OccurredOn:yyyy-MM-dd}";

    [Fact]
    public void Central_binding_uses_the_owner_pinned_manual_receipt_operation()
    {
        var binding = new UnavailableFinanceDraftReadBinding();
        Assert.Equal(FinanceMoneyOperations.ManualBankReceiptV1, binding.MoneyOperation);
        Assert.Equal("finance.manual-bank-receipt.v1", binding.MoneyOperation);
    }

    [Fact]
    public async Task Default_runtime_is_unavailable_without_required_permission_bank_and_profile_sources()
    {
        using var client = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
        using var response = await client.GetAsync(Endpoint);
        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "service_unavailable");
        AssertPrivateNoStore(response);
    }

    [Fact]
    public async Task Selected_company_is_required_before_the_finance_reader()
    {
        var service = new CapturingService(Found());
        using var host = CreateHost(service);
        using var client = CreateClient(host, false);
        using var response = await client.GetAsync(Endpoint);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "company_context_invalid");
        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task Found_context_is_lossless_private_and_contains_no_amount_or_balance()
    {
        var service = new CapturingService(Found());
        using var host = CreateHost(service);
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(Endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("amount", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("balance", body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(TestCompanyIds.CompanyA, document.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal("9007199254740993", document.RootElement.GetProperty("currency")
            .GetProperty("profile").GetProperty("reference").GetProperty("version").GetString());
        Assert.Equal(TreasuryAccountId, Assert.Single(service.Requests).TreasuryAccountId);
    }

    [Theory]
    [InlineData(DraftReadOutcome.InvalidRequest, HttpStatusCode.BadRequest, null)]
    [InlineData(DraftReadOutcome.Denied, HttpStatusCode.Forbidden, "forbidden")]
    [InlineData(DraftReadOutcome.NotFound, HttpStatusCode.NotFound, null)]
    [InlineData(DraftReadOutcome.Unavailable, HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    public async Task Consumer_outcomes_have_the_exact_public_status_boundary(
        DraftReadOutcome outcome, HttpStatusCode expectedStatus, string? expectedCode)
    {
        var service = new CapturingService(new(outcome, null, "internal_diagnostic_not_public"));
        using var host = CreateHost(service);
        using var client = CreateClient(host, true);
        using var response = await client.GetAsync(Endpoint);
        Assert.Equal(expectedStatus, response.StatusCode);
        AssertPrivateNoStore(response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (expectedCode is null) Assert.False(document.RootElement.TryGetProperty("code", out _));
        else Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("internal_diagnostic_not_public", document.RootElement.GetRawText(),
            StringComparison.Ordinal);
    }

    // Lenient forms the DateOnly binder alone accepted (ORG #261 P2-A), plus the forms it already
    // refused. None may reach the reader; the refusal is the existing code-less 400.
    [Theory]
    [InlineData("01%2F02%2F2026")]
    [InlineData("09%2F13%2F2026")]
    [InlineData("2026-9-3")]
    [InlineData("13%20Sep%202026")]
    [InlineData("2026-09-13%20")]
    [InlineData("%202026-09-13")]
    [InlineData("2026-09-13T10%3A00%3A00")]
    [InlineData("2026-09-13T00%3A00%3A00Z")]
    [InlineData("2026-09-13T01%3A00%3A00%2B03%3A00")]
    [InlineData("20260913")]
    [InlineData("2026-02-30")]
    [InlineData("")]
    public async Task Occurred_on_binds_only_the_exact_iso_date(string occurredOn)
    {
        var service = new CapturingService(Found());
        using var host = CreateHost(service);
        using var client = CreateClient(host, true);

        using var response = await client.GetAsync(EndpointWith(occurredOn));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(service.Requests);
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 0)
        {
            using var document = JsonDocument.Parse(body);
            Assert.False(document.RootElement.TryGetProperty("code", out _));
        }
    }

    [Fact]
    public async Task A_missing_or_repeated_occurred_on_never_reaches_the_reader()
    {
        var service = new CapturingService(Found());
        using var host = CreateHost(service);
        using var client = CreateClient(host, true);
        var withoutDate = FinanceEndpoints.DraftContextPath
            + $"?treasuryAccountId={TreasuryAccountId:D}&partyId={PartyId:D}&companyAccountId={CompanyAccountId:D}";

        using var missing = await client.GetAsync(withoutDate);
        using var repeated = await client.GetAsync(withoutDate + "&occurredOn=2026-09-13&occurredOn=2026-09-14");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Empty(service.Requests);
    }

    [Theory]
    [InlineData("2026-09-13", 2026, 9, 13)]
    [InlineData("2026-01-02", 2026, 1, 2)]
    [InlineData("2024-02-29", 2024, 2, 29)]
    public async Task The_exact_iso_date_reaches_the_reader_unchanged(string occurredOn, int year, int month, int day)
    {
        var service = new CapturingService(new(DraftReadOutcome.Unavailable, null, null));
        using var host = CreateHost(service);
        using var client = CreateClient(host, true);

        using var response = await client.GetAsync(EndpointWith(occurredOn));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(new DateOnly(year, month, day), Assert.Single(service.Requests).OccurredOn);
    }

    [Fact]
    public async Task Counterparty_adapter_preserves_an_active_roles0_account_and_owner_versions()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(TestCompanyIds.CompanyA);
        var owner = new StubCounterpartyReadService(new(PartyId, "CARI-001",
            PartyKind.LegalEntity, "Rol Gerektirmeyen Cari", null, true, "party-v7",
            [new(CompanyAccountId, TestCompanyIds.CompanyA, PartyRole.None, true, "account-v11")]));
        var adapter = new CounterpartyFinancePartyAccountPort(companyContext,
            new StubCurrentUser("finance-reader", TestCompanyIds.CompanyA), owner);

        var result = await adapter.GetAuthorizedAsync(
            new(TestCompanyIds.CompanyA, "finance-reader", "correlation-1", null),
            PartyId, CompanyAccountId, CancellationToken.None);

        Assert.Equal(ReadOutcome.Found, result.Outcome);
        var account = Assert.IsType<PartyAccountEligibility>(result.Resource);
        Assert.True(account.IsActive);
        Assert.True(account.IsPartyActive);
        Assert.Equal("party-v7", account.Label?.PartyVersion);
        Assert.Equal("account-v11", account.Label?.CompanyAccountVersion);
        Assert.Equal(1, owner.DetailReadCount);
    }

    [Fact]
    public async Task Counterparty_adapter_denies_a_foreign_company_before_owner_read()
    {
        var companyContext = new CompanyContext();
        companyContext.Set(TestCompanyIds.CompanyA);
        var owner = new StubCounterpartyReadService(null);
        var adapter = new CounterpartyFinancePartyAccountPort(companyContext,
            new StubCurrentUser("finance-reader", TestCompanyIds.CompanyA), owner);

        var result = await adapter.GetAuthorizedAsync(
            new(TestCompanyIds.CompanyB, "finance-reader", "correlation-2", null),
            PartyId, CompanyAccountId, CancellationToken.None);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(0, owner.DetailReadCount);
    }

    [Theory]
    [InlineData("found")]
    [InlineData("not-found")]
    [InlineData("throws")]
    public async Task Counterparty_adapter_denies_when_company_access_is_revoked_while_the_owner_read_is_pending(
        string completion)
    {
        // CompanyContext is fixed for a request; what can change under a pending owner read is the
        // actor's authority for that company (membership revoked / re-authenticated elsewhere).
        var companyContext = new CompanyContext();
        companyContext.Set(TestCompanyIds.CompanyA);
        var currentUser = new StubCurrentUser("finance-reader", TestCompanyIds.CompanyA);
        var owner = new StubCounterpartyReadService(completion == "found"
            ? new(PartyId, "CARI-001", PartyKind.LegalEntity, "Rol Gerektirmeyen Cari", null, true, "party-v7",
                [new(CompanyAccountId, TestCompanyIds.CompanyA, PartyRole.None, true, "account-v11")])
            : null)
        {
            During = () => currentUser.Revoke(TestCompanyIds.CompanyA),
            Failure = completion == "throws" ? new InvalidOperationException("SENTETIK owner failure") : null,
        };
        var adapter = new CounterpartyFinancePartyAccountPort(companyContext, currentUser, owner);

        var result = await adapter.GetAuthorizedAsync(
            new(TestCompanyIds.CompanyA, "finance-reader", "correlation-3", null),
            PartyId, CompanyAccountId, CancellationToken.None);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(1, owner.DetailReadCount);
    }

    private static string EndpointWith(string occurredOn) => FinanceEndpoints.DraftContextPath
        + $"?treasuryAccountId={TreasuryAccountId:D}&partyId={PartyId:D}"
        + $"&companyAccountId={CompanyAccountId:D}&occurredOn={occurredOn}";

    private WebApplicationFactory<Program> CreateHost(IFinanceDraftContextEndpointService service) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFinanceDraftContextEndpointService>();
            services.AddSingleton(service);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host, bool includeCompany)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        if (includeCompany) client.DefaultRequestHeaders.Add("X-Company-Id", TestCompanyIds.CompanyA.ToString("D"));
        return client;
    }

    private static DraftReadContextResult Found() => new(DraftReadOutcome.Found,
        new(TestCompanyIds.CompanyA, OccurredOn,
            new(TreasuryAccountId, TestCompanyIds.CompanyA, CurrencyId, "Sentetik banka", true, true,
                "9007199254740993"),
            new(CurrencyId, "SENTETIK", new(
                new("77777777-7777-7777-7777-777777777777", "9007199254740993"), 0, OccurredOn, null)),
            new(PartyId, CompanyAccountId, "SENTETIK-CARI", "Sentetik Cari", null,
                "9007199254740993", "9007199254740993")), null);

    private static async Task AssertProblemAsync(HttpResponseMessage response,
        HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
    }

    private sealed class CapturingService(DraftReadContextResult result)
        : IFinanceDraftContextEndpointService
    {
        public List<DraftReadContextRequest> Requests { get; } = [];
        public Task<DraftReadContextResult> GetAsync(DraftReadContextRequest request,
            string correlationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Assert.False(string.IsNullOrWhiteSpace(correlationId));
            return Task.FromResult(result);
        }
    }

    private sealed class StubCurrentUser(string userId, params Guid[] allowedCompanyIds)
        : ICurrentUser
    {
        private readonly HashSet<Guid> _allowedCompanyIds = [.. allowedCompanyIds];
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds => _allowedCompanyIds;
        public bool CanAccessCompany(Guid companyId) => _allowedCompanyIds.Contains(companyId);
        public void Revoke(Guid companyId) => _allowedCompanyIds.Remove(companyId);
    }

    private sealed class StubCounterpartyReadService(CounterpartyDetailView? party)
        : ICounterpartyReadService
    {
        public int DetailReadCount { get; private set; }
        public Action? During { get; init; }
        public Exception? Failure { get; init; }

        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(
            CounterpartyReadRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(
            Guid partyId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DetailReadCount++;
            During?.Invoke();
            return Failure is not null ? Task.FromException<CounterpartyDetailView?>(Failure) : Task.FromResult(party);
        }
    }
}
