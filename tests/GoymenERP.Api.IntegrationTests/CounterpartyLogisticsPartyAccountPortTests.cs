using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Logistics.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// LOG-4a, central (the Cari owner's mapping): the instruction's customer answer over the authorized
/// Counterparty party read. The party and the company account stay two owner facts (the account must be the
/// party's own in the server company); an account with no role is still an account (roles = 0 is valid); a
/// caller the real read does not authorize gets no party back (the read answers null, so NotFound: nothing
/// disclosed, as the Sales and Finance party ports); a refusal the owner throws is Denied; and nothing is
/// released for a context the caller lost while the owner answered. Nothing here reaches a database: the owner is a double that returns
/// exactly what the real read can return.
/// </summary>
public sealed class CounterpartyLogisticsPartyAccountPortTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Party = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    private static readonly Guid Account = Guid.Parse("c1000000-0000-4000-8000-000000000001");

    [Theory]
    [InlineData(PartyRole.Customer)]
    // roles = 0 is a valid company account; the port never filters by role.
    [InlineData(PartyRole.None)]
    public async Task The_partys_own_account_in_the_server_company_is_found_with_the_owners_facts(PartyRole roles)
    {
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company, roles, false, "7")));

        var answer = await Port(owner).GetAuthorizedAsync(Party, Account);

        Assert.Equal(LogisticsPartyAccountOutcomeV1.Found, answer.Outcome);
        Assert.Equal(new LogisticsPartyAccountV1(Party, Account, Company, true, false, "3", "7"), answer.Account);
        Assert.Equal([Party], owner.Reads);
    }

    [Fact]
    public async Task An_account_of_the_party_in_another_company_or_another_account_is_not_found()
    {
        var elsewhere = new Owner(Detail(new CounterpartyDetailAccountView(Account, OtherCompany, PartyRole.Customer, true, "1")));
        var another = new Owner(Detail(new CounterpartyDetailAccountView(Guid.NewGuid(), Company, PartyRole.Customer, true, "1")));

        Assert.Equal(new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.NotFound, null),
            await Port(elsewhere).GetAuthorizedAsync(Party, Account));
        Assert.Equal(new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.NotFound, null),
            await Port(another).GetAuthorizedAsync(Party, Account));
    }

    [Fact]
    // The real read returns null both for an unknown party and for a caller without counterparty.parties.read
    // in the context company: either way the answer is NotFound and discloses nothing.
    public async Task A_party_the_owner_does_not_return_is_not_found()
    {
        Assert.Equal(new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.NotFound, null),
            await Port(new Owner(null)).GetAuthorizedAsync(Party, Account));
    }

    [Fact]
    public async Task A_refusal_the_owner_throws_is_denied_not_missing()
    {
        var owner = new Owner(null) { Refuse = true };

        Assert.Equal(new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Denied, null),
            await Port(owner).GetAuthorizedAsync(Party, Account));
    }

    [Fact]
    public async Task Without_a_usable_context_or_ids_nothing_is_read()
    {
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company, PartyRole.Customer, true, "1")));
        var unresolved = new CounterpartyLogisticsPartyAccountPortV1(new CompanyContext(), new User(Company), owner);
        var foreign = new CounterpartyLogisticsPartyAccountPortV1(Context(Company), new User(OtherCompany), owner);

        foreach (var answer in new[]
                 {
                     await unresolved.GetAuthorizedAsync(Party, Account),
                     await foreign.GetAuthorizedAsync(Party, Account),
                     await Port(owner).GetAuthorizedAsync(Guid.Empty, Account),
                     await Port(owner).GetAuthorizedAsync(Party, Guid.Empty),
                 })
        {
            Assert.Equal(new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Denied, null), answer);
        }

        Assert.Empty(owner.Reads);
    }

    [Fact]
    public async Task A_company_the_caller_lost_while_the_owner_answered_releases_nothing()
    {
        var user = new User(Company);
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company, PartyRole.Customer, true, "1")))
        {
            DuringRead = () => user.Revoke(),
        };

        var answer = await new CounterpartyLogisticsPartyAccountPortV1(Context(Company), user, owner)
            .GetAuthorizedAsync(Party, Account);

        Assert.Equal(new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Denied, null), answer);
    }

    [Fact]
    // 8699d007: the adapter answers for the caller it STARTED with. A second actor with access to the SAME company
    // takes the context during the owner's awaited read: IsCurrent() passes for that actor and the company is
    // unchanged, so before the fence this answered Found with the owner's facts to an actor who never asked.
    public async Task An_actor_swapped_while_the_owner_answered_releases_nothing()
    {
        var user = new User(Company);
        var owner = new Owner(Detail(new CounterpartyDetailAccountView(Account, Company, PartyRole.Customer, true, "1")))
        {
            DuringRead = () => user.SwapTo("synthetic-logistics-other-planner"),
        };

        var answer = await new CounterpartyLogisticsPartyAccountPortV1(Context(Company), user, owner)
            .GetAuthorizedAsync(Party, Account);

        Assert.Equal(new LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1.Denied, null), answer);
        // The owner WAS asked (the swap came during its answer) and nothing of that answer was released; and the
        // second actor CAN access the company, so IsCurrent() and the company comparison alone would have let
        // this through — only the actor comparison denies it.
        Assert.Equal([Party], owner.Reads);
        Assert.True(user.CanAccessCompany(Company));
    }

    private static CounterpartyLogisticsPartyAccountPortV1 Port(Owner owner) =>
        new CounterpartyLogisticsPartyAccountPortV1(Context(Company), new User(Company), owner);

    private static CompanyContext Context(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return context;
    }

    private static CounterpartyDetailView Detail(params CounterpartyDetailAccountView[] accounts) =>
        new CounterpartyDetailView(Party, "C-0001", PartyKind.LegalEntity, "SENTETIK Müşteri A.Ş.", null, true, "3", accounts);

    private sealed class Owner(CounterpartyDetailView? party) : ICounterpartyReadService
    {
        public List<Guid> Reads { get; } = [];

        public bool Refuse { get; init; }

        public Action? DuringRead { get; init; }

        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The instruction's customer is read by id only.");

        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
        {
            Reads.Add(partyId);
            DuringRead?.Invoke();
            if (Refuse)
            {
                // A thrown refusal (the port's defensive branch; the current read answers null instead).
                throw new UnauthorizedAccessException();
            }

            return Task.FromResult(party);
        }
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        private readonly HashSet<Guid> _allowed = [company];

        public bool IsAuthenticated => true;

        public string? UserId { get; private set; } = "synthetic-logistics-planner";

        public IReadOnlySet<Guid> AllowedCompanyIds => _allowed;

        public bool CanAccessCompany(Guid companyId) => _allowed.Contains(companyId);

        public void Revoke() => _allowed.Clear();

        /// <summary>Another actor took the context: same company access, a different identity.</summary>
        public void SwapTo(string userId) => UserId = userId;
    }
}
