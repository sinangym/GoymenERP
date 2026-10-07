using GoymenERP.Api.Search;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Search;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-002 / ERP-000I: the platform composer and the host's owner adapters without a database. The composer merges only the
/// kinds whose owner answered, in the fixed kind order; a NotAuthorized kind leaves no group, hit or "more"; a hit of another
/// company, an over-limit or duplicate answer, a missing or duplicated source, or an owner failure is 503 for the whole
/// search (never a partial page). The term rule admits one trimmed <c>q</c> of 2–100 characters and nothing else.
/// </summary>
public sealed class GlobalSearchComposerTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;

    [Fact]
    public async Task Only_answered_kinds_become_groups_in_the_fixed_order_and_a_denied_kind_leaves_nothing()
    {
        var composer = Composer(
            Source.Answering(GlobalSearchKindsV1.SalesOrder, true, Hit("SO-2026-000001", CompanyA)),
            Source.Denied(GlobalSearchKindsV1.ProductItem),
            Source.Answering(GlobalSearchKindsV1.CounterpartyParty, false, Hit("CAR-000001", CompanyA), Hit("CAR-000002", CompanyA)));

        var response = await composer.SearchAsync("ab");

        Assert.Equal("ab", response.Query);
        Assert.Equal([GlobalSearchKindsV1.CounterpartyParty, GlobalSearchKindsV1.SalesOrder], response.Groups.Select(group => group.Kind));
        Assert.Equal(["CAR-000001", "CAR-000002"], response.Groups[0].Items.Select(item => item.Code));
        Assert.False(response.Groups[0].HasMore);
        Assert.True(response.Groups[1].HasMore);
    }

    [Fact]
    public async Task No_readable_kind_is_an_empty_answer_indistinguishable_from_no_match()
    {
        var denied = await Composer(
            Source.Denied(GlobalSearchKindsV1.CounterpartyParty),
            Source.Denied(GlobalSearchKindsV1.ProductItem),
            Source.Denied(GlobalSearchKindsV1.SalesOrder)).SearchAsync("zz");

        Assert.Empty(denied.Groups);
    }

    [Fact]
    public async Task Every_source_is_asked_the_same_term_with_the_per_kind_bound()
    {
        var party = Source.Answering(GlobalSearchKindsV1.CounterpartyParty, false);
        var product = Source.Answering(GlobalSearchKindsV1.ProductItem, false);
        var sales = Source.Answering(GlobalSearchKindsV1.SalesOrder, false);

        await Composer(party, product, sales).SearchAsync("çelik");

        Assert.All(new[] { party, product, sales }, source =>
            Assert.Equal(new GlobalSearchRequestV1("çelik", GlobalSearchApiV1.PerKindLimit), Assert.Single(source.Requests)));
    }

    public static TheoryData<string> BrokenAnswers => new()
    {
        "foreign-company",
        "over-limit",
        "duplicate-id",
        "empty-code",
        "denied-with-hits",
        "null-answer",
        "throws",
    };

    [Theory]
    [MemberData(nameof(BrokenAnswers))]
    public async Task An_answer_against_the_contract_or_an_owner_failure_is_unavailable_for_the_whole_search(string broken)
    {
        var sales = broken switch
        {
            "foreign-company" => Source.Answering(GlobalSearchKindsV1.SalesOrder, false, Hit("SO-B", CompanyB)),
            "over-limit" => Source.Answering(GlobalSearchKindsV1.SalesOrder, false,
                [.. Enumerable.Range(0, GlobalSearchApiV1.PerKindLimit + 1).Select(index => Hit($"SO-{index}", CompanyA))]),
            "duplicate-id" => DuplicateIds(),
            "empty-code" => Source.Answering(GlobalSearchKindsV1.SalesOrder, false, Hit(" ", CompanyA)),
            "denied-with-hits" => new Source(GlobalSearchKindsV1.SalesOrder,
                new GlobalSearchSourceAnswerV1(GlobalSearchSourceOutcomeV1.NotAuthorized, [Hit("SO-X", CompanyA)], false)),
            "null-answer" => new Source(GlobalSearchKindsV1.SalesOrder, null),
            _ => new Source(GlobalSearchKindsV1.SalesOrder, null, new InvalidOperationException("owner down")),
        };
        var composer = Composer(Source.Answering(GlobalSearchKindsV1.CounterpartyParty, false, Hit("CAR-1", CompanyA)),
            Source.Answering(GlobalSearchKindsV1.ProductItem, false, Hit("URN-1", null)), sales);

        await Assert.ThrowsAsync<GlobalSearchUnavailableException>(() => composer.SearchAsync("so"));
    }

    [Fact]
    public async Task A_missing_duplicated_or_unknown_source_is_a_setup_gap()
    {
        var party = Source.Answering(GlobalSearchKindsV1.CounterpartyParty, false);
        var product = Source.Answering(GlobalSearchKindsV1.ProductItem, false);
        var sales = Source.Answering(GlobalSearchKindsV1.SalesOrder, false);

        await Assert.ThrowsAsync<GlobalSearchUnavailableException>(() => Composer(party, product).SearchAsync("ab"));
        await Assert.ThrowsAsync<GlobalSearchUnavailableException>(() => Composer(party, product, sales, sales).SearchAsync("ab"));
        await Assert.ThrowsAsync<GlobalSearchUnavailableException>(() =>
            Composer(party, product, sales, Source.Answering("purchasing.order", false)).SearchAsync("ab"));
        Assert.Empty(party.Requests);
    }

    [Fact]
    public async Task Without_a_resolved_company_nothing_is_asked()
    {
        var party = Source.Answering(GlobalSearchKindsV1.CounterpartyParty, false);
        var composer = new GlobalSearchComposer([party, Source.Answering(GlobalSearchKindsV1.ProductItem, false),
            Source.Answering(GlobalSearchKindsV1.SalesOrder, false),
            Source.Answering(GlobalSearchKindsV1.InventoryMaterialLabel, false)], new CompanyContext());

        await Assert.ThrowsAsync<GlobalSearchUnavailableException>(() => composer.SearchAsync("ab"));
        Assert.Empty(party.Requests);
    }

    [Theory]
    [InlineData("q=ab", true, "ab")]
    [InlineData("q=%20%20%C3%87elik%20", true, "Çelik")]
    [InlineData("q=a", false, "")]
    [InlineData("q=%20a%20", false, "")]
    [InlineData("q=", false, "")]
    [InlineData("", false, "")]
    [InlineData("q=ab&q=cd", false, "")]
    [InlineData("q=ab&kind=sales.order", false, "")]
    [InlineData("q=ab%01cd", false, "")]
    public void The_term_rule_admits_one_trimmed_q_of_bounded_length_and_nothing_else(string raw, bool valid, string expected)
    {
        var query = new QueryCollection(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(raw));

        Assert.Equal(valid, GlobalSearchEndpoints.TryReadTerm(query, out var term));
        Assert.Equal(expected, term);
    }

    [Fact]
    public void The_term_rule_bounds_the_length_in_characters()
    {
        var longest = new string('ş', GlobalSearchApiV1.MaximumTermLength);
        Assert.True(GlobalSearchEndpoints.TryReadTerm(Query(longest), out _));
        Assert.False(GlobalSearchEndpoints.TryReadTerm(Query(longest + "ş"), out _));
    }

    [Fact]
    public async Task The_cari_adapter_reads_the_owners_list_for_the_context_company_and_maps_forbidden_to_not_authorized()
    {
        var partyId = Guid.NewGuid();
        var reads = new CounterpartyReads(CounterpartyPageExecution.Succeeded(new CounterpartyPage(
            [new CounterpartyListItem(partyId, "CAR-000001", PartyKind.LegalEntity, "Zeta Çelik A.Ş.", "Zeta", true, 3,
                [new CounterpartyListAccount(Guid.NewGuid(), CompanyA, PartyRole.Customer, true, 1)])],
            null, true, null, null)));
        var source = new CounterpartyGlobalSearchSource(reads, new User(CompanyA), Company(CompanyA), new Sessions("binding-1"));

        var answer = await source.SearchAsync(new GlobalSearchRequestV1("zeta", 5));

        var hit = Assert.Single(answer.Hits);
        Assert.Equal((partyId, "CAR-000001", "Zeta Çelik A.Ş.", "Zeta", "Active", (Guid?)CompanyA),
            (hit.Id, hit.Code, hit.Name, hit.SecondaryName, hit.State, hit.CompanyId));
        Assert.True(answer.HasMore);
        var request = Assert.Single(reads.Requests);
        Assert.Equal(("zeta", 5, (string?)null, CounterpartyRecordState.Active, false, "SENTETIK-search-user", "binding-1"),
            (request.Query.Query, request.Query.PageSize, request.Query.Cursor, request.Query.PartyState, request.Query.IncludeCount,
                request.ActorId, request.SessionBinding));

        var forbidden = await new CounterpartyGlobalSearchSource(new CounterpartyReads(CounterpartyPageExecution.Forbidden()),
            new User(CompanyA), Company(CompanyA), new Sessions("binding-1")).SearchAsync(new GlobalSearchRequestV1("zeta", 5));
        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, forbidden.Outcome);
    }

    [Fact]
    public async Task The_cari_adapter_refuses_a_party_without_an_account_in_the_context_company_and_an_owner_outage()
    {
        var foreign = new CounterpartyReads(CounterpartyPageExecution.Succeeded(new CounterpartyPage(
            [new CounterpartyListItem(Guid.NewGuid(), "CAR-B", PartyKind.LegalEntity, "B", null, true, 1,
                [new CounterpartyListAccount(Guid.NewGuid(), CompanyB, PartyRole.Supplier, true, 1)])],
            null, false, null, null)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CounterpartyGlobalSearchSource(foreign, new User(CompanyA), Company(CompanyA), new Sessions("b"))
                .SearchAsync(new GlobalSearchRequestV1("ca", 5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CounterpartyGlobalSearchSource(new CounterpartyReads(CounterpartyPageExecution.ServiceUnavailable()), new User(CompanyA),
                Company(CompanyA), new Sessions("b")).SearchAsync(new GlobalSearchRequestV1("ca", 5)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CounterpartyGlobalSearchSource(foreign, new User(CompanyA), Company(CompanyA), new Sessions(null))
                .SearchAsync(new GlobalSearchRequestV1("ca", 5)));
    }

    [Fact]
    public async Task The_product_adapter_maps_the_owners_refusal_to_not_authorized_and_a_next_cursor_to_more()
    {
        var denied = await new ProductGlobalSearchSource(new ProductReads(null)).SearchAsync(new GlobalSearchRequestV1("sac", 5));
        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, denied.Outcome);

        var reads = new ProductReads(new ProductPage([], "next"));
        var answered = await new ProductGlobalSearchSource(reads).SearchAsync(new GlobalSearchRequestV1("sac", 5));
        Assert.Equal(GlobalSearchSourceOutcomeV1.Answered, answered.Outcome);
        Assert.True(answered.HasMore);
        Assert.Equal(("sac", 5, (string?)null), (reads.Queries[0].Q, reads.Queries[0].PageSize, reads.Queries[0].Cursor));
    }

    private static IQueryCollection Query(string term) =>
        new QueryCollection(new Dictionary<string, StringValues> { [GlobalSearchApiV1.QueryParameter] = term });

    private static Source DuplicateIds()
    {
        var id = Guid.NewGuid();
        return Source.Answering(GlobalSearchKindsV1.SalesOrder, false,
            new GlobalSearchHitV1(id, "SO-1", null, null, "Approved", null, CompanyA),
            new GlobalSearchHitV1(id, "SO-2", null, null, "Approved", null, CompanyA));
    }

    private static GlobalSearchComposer Composer(params IGlobalSearchSourceV1[] sources) => new GlobalSearchComposer(
        sources.Any(source => source.Kind == GlobalSearchKindsV1.InventoryMaterialLabel)
            ? sources
            : [.. sources, Source.Denied(GlobalSearchKindsV1.InventoryMaterialLabel)],
        Company(CompanyA));

    private static CompanyContext Company(Guid companyId)
    {
        var context = new CompanyContext();
        context.Set(companyId);
        return context;
    }

    private static GlobalSearchHitV1 Hit(string code, Guid? company) =>
        new GlobalSearchHitV1(Guid.NewGuid(), code, "Ad", null, "Active", null, company);

    private sealed class Source(string kind, GlobalSearchSourceAnswerV1? answer, Exception? failure = null) : IGlobalSearchSourceV1
    {
        public List<GlobalSearchRequestV1> Requests { get; } = [];

        public string Kind => kind;

        public static Source Denied(string kind) => new Source(kind, GlobalSearchSourceAnswerV1.NotAuthorized);

        public static Source Answering(string kind, bool hasMore, params GlobalSearchHitV1[] hits) =>
            new Source(kind, GlobalSearchSourceAnswerV1.Answered(hits, hasMore));

        public Task<GlobalSearchSourceAnswerV1> SearchAsync(GlobalSearchRequestV1 request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return failure is null ? Task.FromResult(answer!) : Task.FromException<GlobalSearchSourceAnswerV1>(failure);
        }
    }

    private sealed class CounterpartyReads(CounterpartyPageExecution execution) : ICounterpartyReadService
    {
        public List<CounterpartyReadRequest> Requests { get; } = [];

        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(execution);
        }

        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The search reads the list only.");
    }

    private sealed class Sessions(string? binding) : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(binding);
    }

    private sealed class ProductReads(ProductPage? page) : IProductService
    {
        public List<ProductListQuery> Queries { get; } = [];

        public Task<ProductPage> GetPageAsync(ProductListQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return page is null
                ? Task.FromException<ProductPage>(new UnauthorizedAccessException("denied"))
                : Task.FromResult(page);
        }

        public Task<ProductCardView?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCodeGroup>> GetCodeGroupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductReferenceOptions> GetReferenceOptionsAsync(ProductReferenceQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ProductDuplicateCandidates> GetDuplicateCandidatesAsync(ProductDuplicateQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ProductMutationResponse> CreateDraftAsync(CreateProductDraftRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ProductMutationResponse> EditDraftAsync(Guid id, EditProductDraftRequest request, string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductMutationResponse> ActivateAsync(Guid id, ActivateProductRequest request, string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductMutationResponse> DeactivateAsync(Guid id, ChangeProductLifecycleRequest request, string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProductMutationResponse> ReactivateAsync(Guid id, ChangeProductLifecycleRequest request, string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class User(params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => "SENTETIK-search-user";

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
