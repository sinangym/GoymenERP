using System.Net;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D27 (EK3) through the real host composition (Program's <see cref="SalesReferenceReadBindings"/>, the real handler and
/// the real product-candidate route) over SENTETIK owners: (2) a holder of <c>sales.requests.write</c> or
/// <c>sales.quotes.write</c> reads the selection lists beside the order create name, a read or decision name does not, and a
/// grant of another company opens nothing; (1) the sales-owner picker route's query boundary, its answer shape and that it
/// hands the Identity adapter the order create name as the eligibility. The Identity measurement itself is real-SQL tested in
/// <see cref="IdentityUserDirectorySqlServerRelationalAcceptanceTests"/>. No grant is real; the authority is a test double.
/// </summary>
public sealed class SalesReferencePoolPermissionHttpTests(GoymenErpApiFactory factory) : IClassFixture<GoymenErpApiFactory>
{
    private static readonly Guid PartyId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ProductId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid UnitId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid ProfileId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private const string ProductsQuery = "?q=BANT&effectiveOn=2026-10-27&limit=5";

    public static TheoryData<string> WriteNames => new()
    {
        SalesPermissionManifestV1.RequestsWrite,
        SalesPermissionManifestV1.QuotesWrite,
        SalesPermissionManifestV1.DraftsCreate,
    };

    public static TheoryData<string> NonOpeningNames => new()
    {
        SalesPermissionManifestV1.RequestsRead,
        SalesPermissionManifestV1.RequestsApprove,
        SalesPermissionManifestV1.QuotesRead,
        SalesPermissionManifestV1.DraftsRead,
        SalesPermissionManifestV1.OrdersApprove,
    };

    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task One_write_name_alone_opens_the_account_product_and_sales_owner_lists(string held)
    {
        var world = new World([(held, TestCompanyIds.CompanyA)]);
        using var host = CreateHost(world);
        using var client = CreateClient(host, TestCompanyIds.CompanyA);

        using (var accounts = await client.GetAsync(SalesEndpoints.AccountsPath + "?pageSize=10"))
        {
            Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
            using var body = JsonDocument.Parse(await accounts.Content.ReadAsStringAsync());
            Assert.Equal(AccountId, Assert.Single(body.RootElement.GetProperty("accounts").EnumerateArray()).GetProperty("companyAccountId").GetGuid());
        }

        using (var products = await client.GetAsync(SalesProductCandidateEndpoints.Path + ProductsQuery))
        {
            Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        }

        using (var owners = await client.GetAsync(SalesEndpoints.SalesOwnersPath))
        {
            Assert.Equal(HttpStatusCode.OK, owners.StatusCode);
        }

        Assert.Single(world.Accounts.Requests);
        Assert.Single(world.Products.Queries);
        Assert.Single(world.Owners.Requests);
    }

    [Theory]
    [MemberData(nameof(NonOpeningNames))]
    public async Task A_read_or_decision_name_alone_opens_no_selection_list_and_asks_no_owner(string held)
    {
        var world = new World([(held, TestCompanyIds.CompanyA)]);
        using var host = CreateHost(world);
        using var client = CreateClient(host, TestCompanyIds.CompanyA);

        foreach (var path in new[]
                 {
                     SalesEndpoints.AccountsPath + "?pageSize=10",
                     SalesProductCandidateEndpoints.Path + ProductsQuery,
                     SalesEndpoints.SalesOwnersPath,
                 })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Empty(world.Accounts.Requests);
        Assert.Empty(world.Products.Queries);
        Assert.Empty(world.Owners.Requests);
    }

    [Fact]
    public async Task The_write_names_of_company_A_open_nothing_in_company_B_and_the_same_names_in_B_do()
    {
        var onlyA = new World([
            (SalesPermissionManifestV1.RequestsWrite, TestCompanyIds.CompanyA),
            (SalesPermissionManifestV1.QuotesWrite, TestCompanyIds.CompanyA),
        ]);
        using (var host = CreateHost(onlyA))
        using (var client = CreateClient(host, TestCompanyIds.CompanyB))
        {
            foreach (var path in new[] { SalesEndpoints.AccountsPath + "?pageSize=10", SalesProductCandidateEndpoints.Path + ProductsQuery, SalesEndpoints.SalesOwnersPath })
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            Assert.Empty(onlyA.Accounts.Requests);
            Assert.Empty(onlyA.Products.Queries);
            Assert.Empty(onlyA.Owners.Requests);
        }

        // Control: the same actor with the quote write name in B is answered in B, so the refusal above is the company's.
        var inB = new World([(SalesPermissionManifestV1.QuotesWrite, TestCompanyIds.CompanyB)]);
        using (var host = CreateHost(inB))
        using (var client = CreateClient(host, TestCompanyIds.CompanyB))
        using (var owners = await client.GetAsync(SalesEndpoints.SalesOwnersPath))
        {
            Assert.Equal(HttpStatusCode.OK, owners.StatusCode);
            Assert.Equal(TestCompanyIds.CompanyB, Assert.Single(inB.Owners.Requests).CompanyId);
        }
    }

    [Fact]
    public async Task The_sales_owner_picker_asks_identity_for_the_order_create_holders_and_answers_only_id_and_name()
    {
        var alice = Guid.Parse("a11ce000-0000-4000-8000-000000000001");
        var world = new World([(SalesPermissionManifestV1.RequestsWrite, TestCompanyIds.CompanyA)])
        {
            Owners = { Answer = new(SalesOwnerCandidateOutcomeV1.Success, [new(alice, "SENTETIK Alice")], true) },
        };
        using var host = CreateHost(world);
        using var client = CreateClient(host, TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(SalesEndpoints.SalesOwnersPath + "?q=%20ali%20&limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(["companyId", "owners", "hasMore"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(TestCompanyIds.CompanyA, root.GetProperty("companyId").GetGuid());
        Assert.True(root.GetProperty("hasMore").GetBoolean());
        var owner = Assert.Single(root.GetProperty("owners").EnumerateArray());
        Assert.Equal(["userId", "displayName"], owner.EnumerateObject().Select(property => property.Name));
        Assert.Equal((alice, "SENTETIK Alice"), (owner.GetProperty("userId").GetGuid(), owner.GetProperty("displayName").GetString()));

        var request = Assert.Single(world.Owners.Requests);
        // SAT-D27 (1): the eligibility is the server's binding — the order create name — never a caller value.
        Assert.Equal((TestCompanyIds.CompanyA, "ali", 1, SalesPermissionManifestV1.DraftsCreate),
            (request.CompanyId, request.Query, request.Limit, request.EligibilityPermission));
        Assert.True(Guid.TryParse(request.ActorId, out _));
    }

    [Fact]
    public async Task The_sales_owner_picker_defaults_its_page_and_treats_a_blank_fragment_as_none()
    {
        var world = new World([(SalesPermissionManifestV1.DraftsCreate, TestCompanyIds.CompanyA)]);
        using var host = CreateHost(world);
        using var client = CreateClient(host, TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(SalesEndpoints.SalesOwnersPath + "?q=%20%20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(world.Owners.Requests);
        Assert.Equal((null, SalesOwnerPickerLimitsV1.DefaultLimit), (request.Query, request.Limit));
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=51")]
    [InlineData("?limit=many")]
    [InlineData("?q=a&q=b")]
    [InlineData("?eligibility=sales.drafts.read")]
    [InlineData("?companyId=10000000-0000-0000-0000-000000000002")]
    public async Task The_sales_owner_picker_refuses_a_malformed_query_before_identity_is_asked(string query)
    {
        var world = new World([(SalesPermissionManifestV1.DraftsCreate, TestCompanyIds.CompanyA)]);
        using var host = CreateHost(world);
        using var client = CreateClient(host, TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(SalesEndpoints.SalesOwnersPath + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(world.Owners.Requests);
    }

    [Fact]
    public async Task The_sales_owner_picker_refuses_a_fragment_over_the_bound()
    {
        var world = new World([(SalesPermissionManifestV1.DraftsCreate, TestCompanyIds.CompanyA)]);
        using var host = CreateHost(world);
        using var client = CreateClient(host, TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(SalesEndpoints.SalesOwnersPath + "?q="
            + new string('a', SalesOwnerPickerLimitsV1.MaximumQueryLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(world.Owners.Requests);
    }

    [Theory]
    [InlineData(SalesOwnerCandidateOutcomeV1.Denied, HttpStatusCode.Forbidden)]
    [InlineData(SalesOwnerCandidateOutcomeV1.Unavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task The_owners_refusal_reaches_the_wire_as_a_code_less_problem_never_an_empty_list(
        SalesOwnerCandidateOutcomeV1 outcome, HttpStatusCode expected)
    {
        var world = new World([(SalesPermissionManifestV1.DraftsCreate, TestCompanyIds.CompanyA)])
        {
            Owners = { Answer = new(outcome, [], false) },
        };
        using var host = CreateHost(world);
        using var client = CreateClient(host, TestCompanyIds.CompanyA);

        using var response = await client.GetAsync(SalesEndpoints.SalesOwnersPath);

        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("owners", body, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateHost(World world) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IErpAuthorizationService>();
            services.AddSingleton<IErpAuthorizationService>(world.Authority);
            services.RemoveAll<IErpAuthorityRevalidationService>();
            services.AddSingleton<IErpAuthorityRevalidationService>(world.Authority);
            services.RemoveAll<ICounterpartyReadService>();
            services.AddSingleton<ICounterpartyReadService>(world.Accounts);
            services.RemoveAll<IProductSalesCandidatesV1>();
            services.AddSingleton<IProductSalesCandidatesV1>(world.Products);
            services.RemoveAll<ISalesOwnerCandidateSourceV1>();
            services.AddSingleton<ISalesOwnerCandidateSourceV1>(world.Owners);
            services.RemoveAll<IActorSessionBindingResolver>();
            services.AddSingleton<IActorSessionBindingResolver>(new FixedSession());
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host, Guid company)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", TestUsers.GroupAdmin);
        client.DefaultRequestHeaders.Add("X-Company-Id", company.ToString("D"));
        return client;
    }

    private sealed class World(IReadOnlyCollection<(string Permission, Guid Company)> grants)
    {
        public GrantSetAuthority Authority { get; } = new(grants);
        public AccountOwner Accounts { get; } = new();
        public ProductOwner Products { get; } = new();
        public OwnerSource Owners { get; } = new();
    }

    /// <summary>Holds exactly the given (name, company) pairs, cached and fresh alike.</summary>
    private sealed class GrantSetAuthority(IReadOnlyCollection<(string Permission, Guid Company)> grants)
        : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        private PermissionScope Scope(string permission, Guid companyId) => grants.Contains((permission, companyId))
            ? new(true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
            : PermissionScope.Denied;

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId).IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission, companyId));

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("sat-d27-controlled", new HashSet<Guid> { companyId },
                permissions.ToDictionary(permission => permission, permission => Scope(permission, companyId))));
    }

    private sealed class AccountOwner : ICounterpartyReadService
    {
        public List<CounterpartyReadRequest> Requests { get; } = [];

        public Task<CounterpartyPageExecution> GetAuthorizedPageAsync(CounterpartyReadRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(CounterpartyPageExecution.Succeeded(new(
                [new(PartyId, "CARI", PartyKind.LegalEntity, "SENTETIK Cari", null, true, 7,
                    [new(AccountId, TestCompanyIds.CompanyA, PartyRole.None, true, 3), new(Guid.NewGuid(), TestCompanyIds.CompanyB, PartyRole.None, true, 3)])],
                null, false, null, null)));
        }

        public Task<CounterpartyDetailView?> GetAuthorizedPartyAsync(Guid partyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ProductOwner : IProductSalesCandidatesV1
    {
        public List<ProductSalesCandidateQueryV1> Queries { get; } = [];

        public Task<ProductSalesCandidatesV1> SearchAsync(ProductSalesCandidateQueryV1 query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(new ProductSalesCandidatesV1([new(ProductId, "7", "PRD-7", "SENTETIK Bant", [new(UnitId, "kg", ProfileId, "3")])], false));
        }
    }

    private sealed class OwnerSource : ISalesOwnerCandidateSourceV1
    {
        public List<SalesOwnerCandidateRequestV1> Requests { get; } = [];
        public SalesOwnerCandidateAnswerV1 Answer { get; set; } = new(SalesOwnerCandidateOutcomeV1.Success, [], false);

        public Task<SalesOwnerCandidateAnswerV1> SearchAsync(SalesOwnerCandidateRequestV1 request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Answer);
        }
    }

    private sealed class FixedSession : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("sat-d27-session");
    }
}
