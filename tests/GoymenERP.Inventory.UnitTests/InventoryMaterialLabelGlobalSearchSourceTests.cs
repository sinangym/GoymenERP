using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Search;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Infrastructure.Search;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryMaterialLabelGlobalSearchSourceTests
{
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Label = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly GlobalSearchRequestV1 Request = new("SYN-LABEL-001", 5);

    [Fact]
    public async Task Both_existing_reads_are_required_company_wide_before_any_owner_read()
    {
        var world = new World();
        world.Authority.Denied = ErpPermissions.InventoryBalancesRead;

        var answer = await world.Source().SearchAsync(Request);

        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, answer.Outcome);
        Assert.Empty(answer.Hits);
        Assert.False(answer.HasMore);
        Assert.Equal(0, world.Reader.Reads);
        Assert.Equal([ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead], world.Authority.Calls);

        world = new World { Authority = { CompanyWide = false } };
        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, (await world.Source().SearchAsync(Request)).Outcome);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Fact]
    public async Task Exact_owner_record_is_price_free_single_and_has_no_more()
    {
        var world = new World();

        var answer = await world.Source().SearchAsync(Request);

        Assert.Equal(GlobalSearchSourceOutcomeV1.Answered, answer.Outcome);
        var hit = Assert.Single(answer.Hits);
        Assert.Equal((Label, "SYN-LABEL-001", "PendingDocuments", (Guid?)CompanyA),
            (hit.Id, hit.Code, hit.State, hit.CompanyId));
        Assert.Null(hit.Name);
        Assert.Null(hit.SecondaryName);
        Assert.Null(hit.Date);
        Assert.False(answer.HasMore);
        Assert.Equal((CompanyA, "SYN-LABEL-001"), Assert.Single(world.Reader.Requests));
        Assert.Equal([
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
            ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead,
        ], world.Authority.Calls);
    }

    [Fact]
    public async Task Missing_exact_code_is_an_authorized_empty_group_without_fuzzy_fallback()
    {
        var world = new World { Reader = { Record = null } };

        var answer = await world.Source().SearchAsync(new GlobalSearchRequestV1("syn-label", 5));

        Assert.Equal(GlobalSearchSourceOutcomeV1.Answered, answer.Outcome);
        Assert.Empty(answer.Hits);
        Assert.False(answer.HasMore);
        Assert.Equal((CompanyA, "syn-label"), Assert.Single(world.Reader.Requests));
    }

    [Fact]
    public async Task Revoked_read_or_changed_actor_or_company_after_the_read_releases_nothing()
    {
        var revoked = new World();
        revoked.Reader.During = () => revoked.Authority.Denied = ErpPermissions.InventoryLabelsRead;
        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, (await revoked.Source().SearchAsync(Request)).Outcome);

        var actor = new World();
        actor.Reader.During = () => actor.User.Actor = "SENTETIK-other";
        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, (await actor.Source().SearchAsync(Request)).Outcome);

        var company = new World();
        company.Reader.During = () => company.Company.Set(CompanyB);
        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, (await company.Source().SearchAsync(Request)).Outcome);

        Assert.All(new[] { revoked, actor, company }, item => Assert.Equal(1, item.Reader.Reads));
    }

    [Fact]
    public async Task A_failing_owner_read_is_released_only_under_still_fresh_authority()
    {
        var available = new World { Reader = { Fail = true } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => available.Source().SearchAsync(Request));
        Assert.Equal(4, available.Authority.Calls.Count);

        var revoked = new World { Reader = { Fail = true } };
        revoked.Reader.During = () => revoked.Authority.Denied = ErpPermissions.InventoryBalancesRead;
        var answer = await revoked.Source().SearchAsync(Request);
        Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, answer.Outcome);
        Assert.Empty(answer.Hits);
    }

    [Fact]
    public async Task Anonymous_unresolved_or_foreign_actor_measures_nothing_and_reads_nothing()
    {
        var anonymous = new World { User = { Authenticated = false } };
        var unresolved = new World(resolveCompany: false);
        var foreign = new World { User = { Companies = new HashSet<Guid> { CompanyB } } };

        foreach (var world in new[] { anonymous, unresolved, foreign })
        {
            Assert.Equal(GlobalSearchSourceOutcomeV1.NotAuthorized, (await world.Source().SearchAsync(Request)).Outcome);
            Assert.Empty(world.Authority.Calls);
            Assert.Equal(0, world.Reader.Reads);
        }
    }

    [Theory]
    [InlineData("empty-id")]
    [InlineData("foreign-company")]
    [InlineData("empty-code")]
    [InlineData("wrong-code")]
    [InlineData("unknown-state")]
    public async Task A_malformed_owner_record_fails_closed(string malformed)
    {
        var world = new World();
        world.Reader.Record = malformed switch
        {
            "empty-id" => world.Reader.Record! with { Id = Guid.Empty },
            "foreign-company" => world.Reader.Record! with { CompanyId = CompanyB },
            "empty-code" => world.Reader.Record! with { Code = " " },
            "wrong-code" => world.Reader.Record! with { Code = "syn-label-001" },
            _ => world.Reader.Record! with { DocumentStatus = "Unknown" },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => world.Source().SearchAsync(Request));
    }

    [Fact]
    public void Registration_adds_one_owner_source_and_its_sql_reader()
    {
        var services = new ServiceCollection();

        services.AddInventoryGlobalSearchSource();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IGlobalSearchSourceV1)
            && descriptor.ImplementationType == typeof(InventoryMaterialLabelGlobalSearchSource));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IInventoryMaterialLabelSearchReaderV1)
            && descriptor.ImplementationType == typeof(SqlInventoryMaterialLabelSearchReaderV1));
    }

    private sealed class World(bool resolveCompany = true)
    {
        public MutableCompanyContext Company { get; } = new(resolveCompany);
        public User User { get; set; } = new();
        public Authority Authority { get; set; } = new();
        public Reader Reader { get; set; } = new();

        public InventoryMaterialLabelGlobalSearchSource Source() => new(Reader, Company, User, Authority);

    }

    private sealed class MutableCompanyContext(bool resolved) : ICompanyContext
    {
        private Guid? _companyId = resolved ? CompanyA : null;

        public bool IsResolved => _companyId.HasValue;

        public Guid CompanyId => _companyId
            ?? throw new InvalidOperationException("SENTETIK unresolved company");

        public void Set(Guid companyId) => _companyId = companyId;
    }

    private sealed class Reader : IInventoryMaterialLabelSearchReaderV1
    {
        public InventoryMaterialLabelSearchRecordV1? Record { get; set; } =
            new(Label, CompanyA, "SYN-LABEL-001", "PendingDocuments");
        public Action? During { get; set; }
        public bool Fail { get; set; }
        public int Reads { get; private set; }
        public List<(Guid CompanyId, string Code)> Requests { get; } = [];

        public Task<InventoryMaterialLabelSearchRecordV1?> ReadExactAsync(
            Guid companyId,
            string code,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            Requests.Add((companyId, code));
            During?.Invoke();
            if (Fail)
            {
                throw new InvalidOperationException("SENTETIK owner failure");
            }

            return Task.FromResult(Record);
        }
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public string? Denied { get; set; }
        public bool CompanyWide { get; set; } = true;
        public List<string> Calls { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(permission);
            return Task.FromResult(permission == Denied
                ? PermissionScope.Denied
                : PermissionScope.Denied with { IsGranted = true, IsCompanyWide = CompanyWide });
        }
    }

    private sealed class User : ICurrentUser
    {
        public bool Authenticated { get; set; } = true;
        public string Actor { get; set; } = "SENTETIK-search-user";
        public IReadOnlySet<Guid> Companies { get; set; } = new HashSet<Guid> { CompanyA };
        public bool IsAuthenticated => Authenticated;
        public string? UserId => Authenticated ? Actor : null;
        public IReadOnlySet<Guid> AllowedCompanyIds => Companies;
        public bool CanAccessCompany(Guid companyId) => Companies.Contains(companyId);
    }
}
