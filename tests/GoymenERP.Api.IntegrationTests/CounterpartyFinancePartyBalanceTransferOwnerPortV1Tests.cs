using GoymenERP.Api.FinanceRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests
{
    private static readonly Guid Company = Guid.Parse("f5320000-0000-4000-8000-000000000001");
    private static readonly Guid Previous = Guid.Parse("f5320000-0000-4000-8000-000000000002");
    private static readonly Guid Next = Guid.Parse("f5320000-0000-4000-8000-000000000003");
    private static readonly Guid PreviousAccount = Guid.Parse("f5320000-0000-4000-8000-000000000004");
    private static readonly Guid NextAccount = Guid.Parse("f5320000-0000-4000-8000-000000000005");
    private static readonly FinanceContext Context = new(Company, "SENTETIK-maker", "SENTETIK-transfer", null);

    [Fact]
    public async Task Actual_succession_carries_both_exact_company_accounts_owner_versions_and_legal_evidence()
    {
        var owner = new Owner();
        var result = await Read(owner);
        Assert.Equal(FinancePartyBalanceTransferOwnerOutcomeV1.Available, result.Outcome);
        Assert.Equal((PreviousAccount, NextAccount, "7", "4", "5", "2"),
            (result.Proof!.PredecessorAccountId, result.Proof.SuccessorAccountId, result.Proof.PredecessorPartyVersion,
                result.Proof.PredecessorAccountVersion, result.Proof.SuccessorPartyVersion, result.Proof.SuccessorAccountVersion));
        Assert.Equal(FinancePartyBalanceTransferLinkKindV1.Succession, result.Proof.LinkKind);
        Assert.Equal(owner.History.Successor!.LegalBasis, result.Proof.LegalBasis);
        Assert.Equal(owner.History.Successor.EffectiveOn, result.Proof.EffectiveOn);
        Assert.Equal(0, owner.MergeReads);
    }

    [Fact]
    public async Task Real_duplicate_merge_uses_its_owner_version_without_inventing_a_date_or_legal_basis()
    {
        var owner = new Owner();
        owner.Previous = owner.Previous with { IsActive = false };
        owner.History = owner.History with { Successor = null };
        var result = await Read(owner);
        Assert.Equal(FinancePartyBalanceTransferOwnerOutcomeV1.Available, result.Outcome);
        Assert.Equal(FinancePartyBalanceTransferLinkKindV1.DuplicateMerge, result.Proof!.LinkKind);
        Assert.Null(result.Proof.EffectiveOn); Assert.Null(result.Proof.LegalBasis); Assert.Null(result.Proof.LinkRecordedAtUtc);
        Assert.Equal(1, owner.MergeReads);
    }

    [Theory]
    [InlineData("different-company")]
    [InlineData("different-account")]
    [InlineData("no-relation")]
    [InlineData("inactive-target")]
    [InlineData("stale-owner-version")]
    public async Task Missing_or_conflicting_actual_owner_evidence_never_returns_a_transfer_proof(string fault)
    {
        var owner = new Owner();
        switch (fault)
        {
            case "different-company": owner.History = owner.History with { CompanyId = Guid.NewGuid() }; break;
            case "different-account": owner.Next = owner.Next with { CompanyAccounts = [owner.Next.CompanyAccounts[0] with { Id = Guid.NewGuid() }] }; break;
            case "no-relation": owner.History = owner.History with { Successor = null }; owner.Merge = owner.Merge with { MergedInto = null }; break;
            case "inactive-target": owner.Next = owner.Next with { IsActive = false }; break;
            case "stale-owner-version": owner.History = owner.History with { AccountVersion = "3" }; break;
        }
        var result = await Read(owner);
        Assert.NotEqual(FinancePartyBalanceTransferOwnerOutcomeV1.Available, result.Outcome);
        Assert.Null(result.Proof);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Changed_company_during_any_owner_await_withholds_all_transfer_data(int read)
    {
        var company = new MutableCompany(Company); var foreignCompany = Guid.NewGuid();
        var owner = new Owner();
        owner.Previous = owner.Previous with { IsActive = false };
        owner.History = owner.History with { Successor = null };
        owner.DuringRead = call => { if (call == read) company.Set(foreignCompany); };
        var result = await Read(owner, company);
        Assert.Equal(FinancePartyBalanceTransferOwnerOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Proof);
        Assert.Equal(foreignCompany, company.CompanyId);
    }

    private static Task<FinancePartyBalanceTransferOwnerAnswerV1> Read(Owner owner, ICompanyContext? company = null)
    {
        if (company is null) { company = new CompanyContext(); company.Set(Company); }
        return new CounterpartyFinancePartyBalanceTransferOwnerPortV1(company, new User(), owner, owner, owner)
            .ReadAsync(Context, Previous, PreviousAccount, Next, NextAccount, default);
    }
    private sealed class MutableCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Context.ActorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class Owner : ICounterpartyReadService, ICounterpartyIdentityHistoryService, ICounterpartyPartyMergeService
    {
        public CounterpartyDetailView Previous { get; set; } = new(CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests.Previous,
            "SENTETIK-P", PartyKind.LegalEntity, "SENTETIK önceki", null, true, "7", [new(PreviousAccount, Company, PartyRole.None, false, "4")]);
        public CounterpartyDetailView Next { get; set; } = new(CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests.Next,
            "SENTETIK-N", PartyKind.LegalEntity, "SENTETIK sonraki", null, true, "5", [new(NextAccount, Company, PartyRole.None, true, "2")]);
        public CounterpartyIdentityHistoryView History { get; set; } = new(CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests.Previous,
            Company, "SENTETIK-P", PartyKind.LegalEntity, "4", false, null, "SENTETIK önceki", [], [],
            new(CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests.Previous, CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests.Next,
                CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests.Next, "SENTETIK-N", "SENTETIK sonraki", CounterpartyPartySuccessionKindV1.Merger,
                "SENTETIK karar belgesi", new(2026, 11, 17), DateTimeOffset.UnixEpoch), [], new(false));
        public CounterpartyPartyMergeView Merge { get; set; } = new(CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests.Previous, Company,
            "SENTETIK-P", "7", "SENTETIK önceki", new(CounterpartyFinancePartyBalanceTransferOwnerPortV1Tests.Next, "SENTETIK-N"), [], new(false));
        public Action<int>? DuringRead { get; set; }
        public int MergeReads { get; private set; }
        private int reads;
        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default)
        { DuringRead?.Invoke(++reads); return Task.FromResult<CounterpartyDetailView?>(partyId == Previous.Id ? Previous : Next); }
        public Task<CounterpartyIdentityHistoryView?> GetIdentityHistoryAsync(Guid partyId, CancellationToken cancellationToken)
        { DuringRead?.Invoke(++reads); return Task.FromResult<CounterpartyIdentityHistoryView?>(History); }
        public Task<CounterpartyPartyMergeView?> GetMergeAsync(Guid partyId, CancellationToken cancellationToken)
        { MergeReads++; DuringRead?.Invoke(++reads); return Task.FromResult<CounterpartyPartyMergeView?>(Merge); }
        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CounterpartyCommandExecution> RecordPartySuccessionAsync(Guid predecessorPartyId, RecordCounterpartyPartySuccession command,
            CounterpartyCommandMetadata metadata, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CounterpartyCommandExecution> ApproveMergeAsync(Guid survivorPartyId, ApproveCounterpartyPartyMerge command,
            CounterpartyCommandMetadata metadata, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
