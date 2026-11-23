using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-17 (FIN-S12): the host adapter of <see cref="IFinancePartyAccountPortV2"/> over the authorized Counterparty
/// party read. The account's roles are the owner's stored flags mapped one by one (dual role, DEC-CAR-001; an
/// unknown owner flag is Unavailable, never dropped); roles = 0 is still an account; the supplier IBAN slot says
/// SourceUnavailable for a supplier because the owner provides no approved IBAN source yet (FNS-D15), and
/// NotApplicable otherwise. The V1 fences stay: nothing is read without a usable context, and nothing is released
/// for a company or actor that changed while the owner answered. Nothing here reaches a database: the owner is a
/// double that returns exactly what the real read can return. All data is SENTETIK.
/// </summary>
public sealed class CounterpartyFinancePartyAccountPortV2Tests
{
    private const string Actor = "synthetic-finance-cashier";
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Party = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    private static readonly Guid Account = Guid.Parse("c1000000-0000-4000-8000-000000000001");

    [Theory]
    [InlineData(PartyRole.Customer | PartyRole.Supplier, FinancePartyAccountRoles.Customer | FinancePartyAccountRoles.Supplier,
        FinanceSupplierIbanState.SourceUnavailable)]
    [InlineData(PartyRole.Supplier, FinancePartyAccountRoles.Supplier, FinanceSupplierIbanState.SourceUnavailable)]
    [InlineData(PartyRole.Customer, FinancePartyAccountRoles.Customer, FinanceSupplierIbanState.NotApplicable)]
    [InlineData(PartyRole.Tolling | PartyRole.Service, FinancePartyAccountRoles.Tolling | FinancePartyAccountRoles.Service,
        FinanceSupplierIbanState.NotApplicable)]
    // roles = 0 is a valid company account: found, with no role. The port never filters by role.
    [InlineData(PartyRole.None, FinancePartyAccountRoles.None, FinanceSupplierIbanState.NotApplicable)]
    public async Task The_partys_own_account_is_found_with_the_owners_roles_and_supplier_iban_view(PartyRole ownerRoles,
        FinancePartyAccountRoles roles, FinanceSupplierIbanState iban)
    {
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company, ownerRoles, false, "7")));

        var answer = await Port(owner).GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None);

        Assert.Equal(ReadOutcome.Found, answer.Outcome);
        var found = Assert.IsType<PartyAccountEligibilityV2>(answer.Resource);
        Assert.Equal(Party, found.PartyId);
        Assert.Equal(Account, found.CompanyAccountId);
        Assert.Equal(Company, found.CompanyId);
        Assert.False(found.IsActive);
        Assert.True(found.IsPartyActive);
        Assert.Equal(roles, found.Roles);
        Assert.Equal(new FinanceSupplierIbanView(iban, null), found.SupplierIban);
        Assert.Equal(new DraftPartyLabel(Party, Account, "C-0001", "SENTETIK Cari A.Ş.", "SENTETIK", "3", "7"), found.Label);
        Assert.Equal([Party], owner.Reads);
    }

    [Fact]
    public async Task A_customer_and_supplier_account_is_one_answer_with_both_roles()
    {
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company,
            PartyRole.Customer | PartyRole.Supplier, true, "1")));

        var found = (await Port(owner).GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None))
            .Resource!;

        Assert.True(found.IsCustomer);
        Assert.True(found.IsSupplier);
        Assert.True(found.IsCustomerAndSupplier);
    }

    [Fact]
    public async Task An_owner_role_finance_does_not_know_is_unavailable_not_dropped()
    {
        var unknown = (PartyRole)16;
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company,
            PartyRole.Supplier | unknown, true, "1")));

        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.Unavailable, null),
            await Port(owner).GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None));
    }

    [Fact]
    public void Each_known_owner_role_maps_to_its_own_finance_flag()
    {
        Assert.True(CounterpartyFinancePartyAccountPortV2.TryMapRoles(
            PartyRole.Customer | PartyRole.Supplier | PartyRole.Tolling | PartyRole.Service, out var all));
        Assert.Equal(FinancePartyAccountRoles.Customer | FinancePartyAccountRoles.Supplier
            | FinancePartyAccountRoles.Tolling | FinancePartyAccountRoles.Service, all);

        Assert.True(CounterpartyFinancePartyAccountPortV2.TryMapRoles(PartyRole.None, out var none));
        Assert.Equal(FinancePartyAccountRoles.None, none);

        Assert.False(CounterpartyFinancePartyAccountPortV2.TryMapRoles(PartyRole.Customer | (PartyRole)32, out var refused));
        Assert.Equal(FinancePartyAccountRoles.None, refused);
    }

    [Fact]
    public async Task An_account_of_the_party_in_another_company_or_another_account_is_not_found()
    {
        var elsewhere = new Owner(Detail(new CounterpartyDetailAccountView(Account, OtherCompany, PartyRole.Customer, true, "1")));
        var another = new Owner(Detail(new CounterpartyDetailAccountView(Guid.NewGuid(), Company, PartyRole.Customer, true, "1")));

        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.NotFound, null),
            await Port(elsewhere).GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None));
        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.NotFound, null),
            await Port(another).GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None));
    }

    [Fact]
    // The real read returns null both for an unknown party and for a caller without counterparty.parties.read in
    // the context company: either way the answer is NotFound and discloses nothing.
    public async Task A_party_the_owner_does_not_return_is_not_found()
    {
        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.NotFound, null),
            await Port(new Owner(null)).GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None));
    }

    [Fact]
    public async Task A_refusal_the_owner_throws_is_denied_not_missing()
    {
        var owner = new Owner(null) { Refuse = true };

        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.Denied, null),
            await Port(owner).GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None));
    }

    [Fact]
    public async Task Without_a_usable_context_or_ids_nothing_is_read()
    {
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company, PartyRole.Customer, true, "1")));
        var unresolved = new CounterpartyFinancePartyAccountPortV2(new CompanyContext(), new User(Company), owner);
        var foreignUser = new CounterpartyFinancePartyAccountPortV2(Context(Company), new User(OtherCompany), owner);

        foreach (var answer in new[]
                 {
                     await unresolved.GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None),
                     await foreignUser.GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None),
                     // A request for a company other than the server-resolved one.
                     await Port(owner).GetAuthorizedAsync(Request(OtherCompany), Party, Account, CancellationToken.None),
                     // A request carrying another actor.
                     await Port(owner).GetAuthorizedAsync(Request(Company) with { ActorId = "synthetic-finance-other" },
                         Party, Account, CancellationToken.None),
                     await Port(owner).GetAuthorizedAsync(Request(Company), Guid.Empty, Account, CancellationToken.None),
                     await Port(owner).GetAuthorizedAsync(Request(Company), Party, Guid.Empty, CancellationToken.None),
                 })
        {
            Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.Denied, null), answer);
        }

        Assert.Empty(owner.Reads);
    }

    [Fact]
    public async Task A_company_the_caller_lost_while_the_owner_answered_releases_nothing()
    {
        var user = new User(Company);
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company, PartyRole.Supplier, true, "1")))
        {
            DuringRead = () => user.Revoke(),
        };

        var answer = await new CounterpartyFinancePartyAccountPortV2(Context(Company), user, owner)
            .GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None);

        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.Denied, null), answer);
        Assert.Equal([Party], owner.Reads);
    }

    [Fact]
    public async Task A_not_found_for_a_company_the_caller_lost_is_denied()
    {
        var user = new User(Company);
        var owner = new Owner(null) { DuringRead = () => user.Revoke() };

        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.Denied, null),
            await new CounterpartyFinancePartyAccountPortV2(Context(Company), user, owner)
                .GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None));
    }

    [Fact]
    public async Task An_owner_failure_for_a_company_the_caller_lost_is_denied_and_otherwise_surfaces()
    {
        var user = new User(Company);
        var lost = new Owner(null) { Fail = true, DuringRead = () => user.Revoke() };
        var failing = new Owner(null) { Fail = true };

        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.Denied, null),
            await new CounterpartyFinancePartyAccountPortV2(Context(Company), user, lost)
                .GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Port(failing).GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None));
    }

    [Fact]
    // The adapter answers for the caller it STARTED with: a second actor with access to the same company takes the
    // context during the owner's awaited read; only the actor comparison denies it.
    public async Task An_actor_swapped_while_the_owner_answered_releases_nothing()
    {
        var user = new User(Company);
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company, PartyRole.Customer, true, "1")))
        {
            DuringRead = () => user.SwapTo("synthetic-finance-other-cashier"),
        };

        var answer = await new CounterpartyFinancePartyAccountPortV2(Context(Company), user, owner)
            .GetAuthorizedAsync(Request(Company), Party, Account, CancellationToken.None);

        Assert.Equal(new ReadResult<PartyAccountEligibilityV2>(ReadOutcome.Denied, null), answer);
        Assert.Equal([Party], owner.Reads);
        Assert.True(user.CanAccessCompany(Company));
    }

    private static CounterpartyFinancePartyAccountPortV2 Port(Owner owner) =>
        new(Context(Company), new User(Company), owner);

    private static FinanceContext Request(Guid company) =>
        new(company, Actor, "synthetic-correlation", null);

    private static CompanyContext Context(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return context;
    }

    private static CounterpartyDetailView Detail(params CounterpartyDetailAccountView[] accounts) =>
        new(Party, "C-0001", PartyKind.LegalEntity, "SENTETIK Cari A.Ş.", "SENTETIK", true, "3", accounts);

    private sealed class Owner(CounterpartyDetailView? party) : ICounterpartyReadService
    {
        public List<Guid> Reads { get; } = [];

        public bool Refuse { get; init; }

        public bool Fail { get; init; }

        public Action? DuringRead { get; init; }

        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The Finance party account is read by id only.");

        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
        {
            Reads.Add(partyId);
            DuringRead?.Invoke();
            if (Refuse)
            {
                throw new UnauthorizedAccessException();
            }

            if (Fail)
            {
                throw new InvalidOperationException("SENTETIK owner failure.");
            }

            return Task.FromResult(party);
        }
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        private readonly HashSet<Guid> _allowed = [company];

        public bool IsAuthenticated => true;

        public string? UserId { get; private set; } = Actor;

        public IReadOnlySet<Guid> AllowedCompanyIds => _allowed;

        public bool CanAccessCompany(Guid companyId) => _allowed.Contains(companyId);

        public void Revoke() => _allowed.Clear();

        public void SwapTo(string userId) => UserId = userId;
    }
}
