using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyManufacturingPartyAccountSourceV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid Party = Guid.Parse("30000000-0000-4000-8000-000000000003");
    private static readonly Guid Account = Guid.Parse("40000000-0000-4000-8000-000000000004");

    [Fact]
    public async Task Search_UsesOwnerReadAndPreservesBothRoleFactsAndPresentation()
    {
        var read = new ReadService
        {
            Page = CounterpartyPageExecution.Succeeded(new CounterpartyPage(
                [new CounterpartyListItem(Party, "CR-001", PartyKind.LegalEntity, "Cari A.Ş.", "Cari",
                    true, 7, [new CounterpartyListAccount(Account, Company, PartyRole.Customer | PartyRole.Tolling, false, 9)])],
                "next", true, null, null)),
        };
        var source = Create(read);

        var answer = await source.SearchAuthorizedAsync(new("cari", 25));

        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.Found, answer.Outcome);
        var item = Assert.Single(answer.Items);
        Assert.Equal((Party, Account, Company), (item.PartyId, item.CompanyAccountId, item.CompanyId));
        Assert.Equal(("CR-001", "Cari A.Ş.", "Cari"), (item.Code, item.LegalName, item.TradeName));
        Assert.True(item.HasCustomerRole);
        Assert.True(item.HasTollingRole);
        Assert.True(item.PartyIsActive);
        Assert.False(item.AccountIsActive);
        Assert.Equal(("7", "9"), (item.PartyVersion, item.AccountVersion));
        Assert.Equal("next", answer.NextCursor);
        Assert.True(answer.HasMore);
        Assert.Equal(PartyRole.Customer | PartyRole.Tolling, read.LastRequest!.Query.Roles);
        Assert.Equal(CounterpartyRoleMatch.Any, read.LastRequest.Query.RoleMatch);
        Assert.Equal(CounterpartyRecordState.All, read.LastRequest.Query.PartyState);
        Assert.Equal(CounterpartyRecordState.All, read.LastRequest.Query.AccountState);
    }

    [Fact]
    public async Task Search_ContextChangeAfterAwait_IsDenied()
    {
        var context = new CompanyContext(Company);
        var read = new ReadService
        {
            Page = CounterpartyPageExecution.Succeeded(new CounterpartyPage([], null, false, null, null)),
            AfterPage = () => context.Set(OtherCompany),
        };
        var source = Create(read, context: context);

        var answer = await source.SearchAuthorizedAsync(new(null));

        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.Denied, answer.Outcome);
        Assert.Empty(answer.Items);
    }

    [Theory]
    [InlineData(" query", 25, null)]
    [InlineData("query ", 25, null)]
    [InlineData("query", 0, null)]
    [InlineData("query", 101, null)]
    [InlineData("query", 25, " cursor")]
    public async Task Search_NonCanonicalOrOutOfRangeInput_IsDenied(string? query, int limit, string? cursor)
    {
        var read = new ReadService();
        var answer = await Create(read).SearchAuthorizedAsync(new(query, limit, cursor));

        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.Denied, answer.Outcome);
        Assert.Equal(0, read.PageCalls);
    }

    [Fact]
    public async Task Search_DuplicatePartyRows_AreUnavailable()
    {
        var row = new CounterpartyListItem(Party, "CR-001", PartyKind.LegalEntity, "Cari", null,
            true, 1, [new CounterpartyListAccount(Account, Company, PartyRole.Customer, true, 1)]);
        var read = new ReadService
        {
            Page = CounterpartyPageExecution.Succeeded(new CounterpartyPage([row, row], null, false, null, null)),
        };

        var answer = await Create(read).SearchAuthorizedAsync(new(null));

        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable, answer.Outcome);
        Assert.Empty(answer.Items);
    }

    [Fact]
    public async Task ExactValidation_PreservesBothRoleFactsAndOpaqueVersions()
    {
        var read = new ReadService
        {
            Detail = new CounterpartyDetailView(Party, "CR-001", PartyKind.LegalEntity, "Cari", null,
                true, "17", [new CounterpartyDetailAccountView(Account, Company, PartyRole.Tolling, false, "23")]),
        };

        var answer = await Create(read).GetAuthorizedAsync(Party, Account);

        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.Found, answer.Outcome);
        Assert.NotNull(answer.Account);
        Assert.False(answer.Account.HasCustomerRole);
        Assert.True(answer.Account.HasTollingRole);
        Assert.Equal(("17", "23"), (answer.Account.PartyVersion, answer.Account.AccountVersion));
    }

    [Fact]
    public async Task ExactValidation_ForeignOrMissingAccount_IsNotFound()
    {
        var read = new ReadService
        {
            Detail = new CounterpartyDetailView(Party, "CR-001", PartyKind.LegalEntity, "Cari", null,
                true, "1", [new CounterpartyDetailAccountView(Account, OtherCompany, PartyRole.Customer, true, "1")]),
        };

        var answer = await Create(read).GetAuthorizedAsync(Party, Account);

        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.NotFound, answer.Outcome);
        Assert.Null(answer.Account);
    }

    [Fact]
    public async Task ExactValidation_NonCanonicalVersion_IsUnavailable()
    {
        var read = new ReadService
        {
            Detail = new CounterpartyDetailView(Party, "CR-001", PartyKind.LegalEntity, "Cari", null,
                true, "01", [new CounterpartyDetailAccountView(Account, Company, PartyRole.Customer, true, "1")]),
        };

        var answer = await Create(read).GetAuthorizedAsync(Party, Account);

        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable, answer.Outcome);
    }

    [Fact]
    public async Task OwnerReadRefusalAndFailure_MapToClosedOutcomes()
    {
        var denied = new ReadService { Exception = new UnauthorizedAccessException() };
        var unavailable = new ReadService { Exception = new InvalidOperationException("synthetic") };

        var deniedAnswer = await Create(denied).GetAuthorizedAsync(Party, Account);
        var unavailableAnswer = await Create(unavailable).GetAuthorizedAsync(Party, Account);

        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.Denied, deniedAnswer.Outcome);
        Assert.Equal(CounterpartyManufacturingPartyAccountOutcomeV1.Unavailable, unavailableAnswer.Outcome);
    }

    [Fact]
    public void CounterpartyModule_OwnsTheScopedRegistration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "SqlServer",
                ["DB_CONNECTION_STRING"] = "Server=(local);Database=contract-only;Integrated Security=true;TrustServerCertificate=true",
            })
            .Build();

        services.AddCounterpartyModule(configuration);

        var descriptor = Assert.Single(services.Where(item =>
            item.ServiceType == typeof(ICounterpartyManufacturingPartyAccountSourceV1)));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(CounterpartyManufacturingPartyAccountSourceV1), descriptor.ImplementationType);
    }

    private static CounterpartyManufacturingPartyAccountSourceV1 Create(
        ReadService read,
        CompanyContext? context = null,
        CurrentUser? user = null) => new(
            context ?? new CompanyContext(Company),
            user ?? new CurrentUser("actor", [Company]),
            read);

    private sealed class ReadService : ICounterpartyReadService
    {
        public CounterpartyPageExecution Page { get; init; } = CounterpartyPageExecution.Succeeded(
            new CounterpartyPage([], null, false, null, null));
        public CounterpartyDetailView? Detail { get; init; }
        public Exception? Exception { get; init; }
        public Action? AfterPage { get; init; }
        public CounterpartyReadRequest? LastRequest { get; private set; }
        public int PageCalls { get; private set; }

        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(
            CounterpartyReadRequest request,
            CancellationToken cancellationToken = default)
        {
            PageCalls++;
            LastRequest = request;
            if (Exception is not null)
            {
                return Task.FromException<CounterpartyPageExecution>(Exception);
            }

            AfterPage?.Invoke();
            return Task.FromResult(Page);
        }

        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(
            Guid partyId,
            CancellationToken cancellationToken = default) => Exception is null
                ? Task.FromResult(Detail)
                : Task.FromException<CounterpartyDetailView?>(Exception);
    }

    private sealed class CompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved { get; private set; } = true;
        public Guid CompanyId { get; private set; } = companyId;

        public void Set(Guid value)
        {
            CompanyId = value;
            IsResolved = value != Guid.Empty;
        }
    }

    private sealed class CurrentUser(string userId, IReadOnlySet<Guid> allowedCompanies) : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = allowedCompanies;
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
