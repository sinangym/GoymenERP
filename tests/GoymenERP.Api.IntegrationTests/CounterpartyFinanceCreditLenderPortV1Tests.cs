using GoymenERP.Api.FinanceRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyFinanceCreditLenderPortV1Tests
{
    private static readonly Guid Company = Guid.Parse("f4730000-0000-4000-8000-000000000001");
    private static readonly Guid Party = Guid.Parse("f4730000-0000-4000-8000-000000000002");
    private static readonly Guid Account = Guid.Parse("f4730000-0000-4000-8000-000000000003");

    [Fact]
    public async Task Lender_uses_exact_real_party_company_account_and_versions_without_requiring_an_invented_bank_role()
    {
        var answer = await Port(new Owner(Detail())).ReadAsync(Company, Party, Account);
        Assert.Equal(new FinanceCreditLenderAnswerV1(Company, Party, Account, FinanceCreditLenderStatusV1.Available,
            "SENTETIK kurum", 3, 4), answer);
        Assert.Equal(FinanceCreditLenderStatusV1.NotFound,
            (await Port(new Owner(Detail())).ReadAsync(Company, Party, Guid.NewGuid())).Status);
        Assert.Equal(FinanceCreditLenderStatusV1.Inactive,
            (await Port(new Owner(Detail() with { IsActive = false })).ReadAsync(Company, Party, Account)).Status);
    }

    [Fact]
    public async Task Revoked_actor_or_changed_company_during_the_authorized_owner_read_releases_no_lender_data()
    {
        var user = new User();
        var owner = new Owner(Detail()) { DuringRead = () => user.UserId = "SENTETIK-other" };
        var answer = await Port(owner, user).ReadAsync(Company, Party, Account);
        Assert.Equal(FinanceCreditLenderStatusV1.Unavailable, answer.Status);
        Assert.Null(answer.DisplayName);
        Assert.Null(answer.PartyVersion);
        var context = new MutableCompany(Company); var foreignCompany = Guid.NewGuid();
        owner = new Owner(Detail()) { DuringRead = () => context.Set(foreignCompany) };
        Assert.Equal(FinanceCreditLenderStatusV1.Unavailable,
            (await new CounterpartyFinanceCreditLenderPortV1(context, new User(), owner).ReadAsync(Company, Party, Account)).Status);
        Assert.Equal(foreignCompany, context.CompanyId);
    }

    [Fact]
    public async Task Invalid_owner_versions_are_unavailable_and_never_seeded()
    {
        Assert.Equal(FinanceCreditLenderStatusV1.Unavailable,
            (await Port(new Owner(Detail() with { Version = "03" })).ReadAsync(Company, Party, Account)).Status);
        Assert.Equal(FinanceCreditLenderStatusV1.Unavailable,
            (await Port(new Owner(Detail()) { DuringRead = () => throw new UnauthorizedAccessException() }).ReadAsync(Company, Party, Account)).Status);
    }

    private static CounterpartyFinanceCreditLenderPortV1 Port(Owner owner, User? user = null) => new(Context(), user ?? new User(), owner);
    private static CompanyContext Context() { var value = new CompanyContext(); value.Set(Company); return value; }
    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }
    private static CounterpartyDetailView Detail() => new(Party, "SENTETIK-1", PartyKind.LegalEntity,
        "SENTETIK kurum", null, true, "3", [new(Account, Company, PartyRole.None, true, "4")]);
    private sealed class Owner(CounterpartyDetailView? value) : ICounterpartyReadService
    {
        public Action? DuringRead { get; init; }
        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
        { DuringRead?.Invoke(); return Task.FromResult(value); }
        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = "SENTETIK-maker";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
