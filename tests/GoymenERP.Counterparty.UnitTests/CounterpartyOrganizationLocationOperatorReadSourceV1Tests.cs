using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Infrastructure;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyOrganizationLocationOperatorReadSourceV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Location = Guid.Parse("72000000-0000-4000-8000-000000000001");
    private static readonly Guid Party = Guid.Parse("73000000-0000-4000-8000-000000000001");

    [Fact]
    public void Module_exposes_only_the_bounded_source_and_keeps_runtime_internal()
    {
        var services = new ServiceCollection();
        services.AddCounterpartyModule(new TestConfiguration());

        var source = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ICounterpartyOrganizationLocationOperatorReadSourceV1));
        Assert.Equal(typeof(CounterpartyOrganizationLocationOperatorReadSourceV1), source.ImplementationType);
        Assert.False(source.ImplementationType!.IsPublic);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Name.Contains("OrganizationLocationOperatorReader", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_display_or_candidate_context_is_denied_before_database_access()
    {
        var context = new TestCompanyContext(Company);
        var user = new TestCurrentUser("organization-location-writer", new HashSet<Guid> { Company });
        var source = Source(context, user);

        var displayRequests = new CounterpartyOrganizationLocationOperatorDisplayRequestV1[]
        {
            new(Guid.Empty, "organization-location-writer", [new(Location, Party)]),
            new(Company, " other", [new(Location, Party)]),
            new(Guid.Empty, "organization-location-writer", []),
            new(Company, "other", [null!]),
        };
        foreach (var request in displayRequests)
        {
            var answer = await source.ReadAuthorizedDisplaysAsync(request);
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, answer.Outcome);
            Assert.Empty(answer.Items);
        }

        var candidateRequests = new CounterpartyOrganizationLocationOperatorCandidateRequestV1[]
        {
            Candidate() with { CompanyId = Guid.Empty },
            Candidate() with { ActorId = "other" },
            Candidate() with { SessionBinding = " " },
            Candidate() with { LocationId = Guid.Empty },
            Candidate() with { PageSize = 0 },
            Candidate() with { PageSize = 26 },
            Candidate() with { Query = new string('x', 201) },
        };
        foreach (var request in candidateRequests)
        {
            var answer = await source.SearchAuthorizedCandidatesAsync(request);
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, answer.Outcome);
            Assert.Empty(answer.Items);
        }
    }

    [Fact]
    public async Task Malformed_or_duplicate_display_bindings_are_unavailable_before_database_access()
    {
        var context = new TestCompanyContext(Company);
        var user = new TestCurrentUser("organization-location-writer", new HashSet<Guid> { Company });
        var authority = new TestAuthority(PermissionScope.Denied);
        var source = Source(context, user, authority);
        var requests = new CounterpartyOrganizationLocationOperatorDisplayRequestV1[]
        {
            new(Company, "organization-location-writer", []),
            new(Company, "organization-location-writer", [new(Guid.Empty, Party)]),
            new(Company, "organization-location-writer", [new(Location, Party), new(Location, Guid.NewGuid())]),
            new(Company, "organization-location-writer", [null!]),
        };

        foreach (var request in requests)
        {
            var answer = await source.ReadAuthorizedDisplaysAsync(request);
            Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable, answer.Outcome);
            Assert.Empty(answer.Items);
        }

        Assert.Empty(authority.Permissions);
    }

    [Fact]
    public async Task Malformed_cursor_is_distinct_and_cancellation_is_not_collapsed()
    {
        var context = new TestCompanyContext(Company);
        var user = new TestCurrentUser("organization-location-writer", new HashSet<Guid> { Company });
        var source = Source(context, user);

        var invalidCursor = await source.SearchAuthorizedCandidatesAsync(Candidate() with { Cursor = "not-signed" });
        Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.InvalidCursor, invalidCursor.Outcome);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.ReadAuthorizedDisplaysAsync(
                new(Company, "organization-location-writer", [new(Location, Party)]), cancellation.Token));
    }

    [Fact]
    public async Task Exact_location_authority_is_required_before_storage_access()
    {
        var context = new TestCompanyContext(Company);
        var user = new TestCurrentUser("organization-location-writer", new HashSet<Guid> { Company });
        var authority = new TestAuthority(new PermissionScope(
            true,
            false,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<Guid>(),
            new HashSet<Guid> { Guid.Parse("72000000-0000-4000-8000-000000000099") },
            new HashSet<Guid>()));
        var source = Source(context, user, authority);

        var display = await source.ReadAuthorizedDisplaysAsync(
            new(Company, "organization-location-writer", [new(Location, Party)]));
        var candidates = await source.SearchAuthorizedCandidatesAsync(Candidate());

        Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, display.Outcome);
        Assert.Equal(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied, candidates.Outcome);
        Assert.Equal(
            [ErpPermissions.OrganizationLocationsRead, ErpPermissions.OrganizationLocationsWrite],
            authority.Permissions);
    }

    private static CounterpartyOrganizationLocationOperatorReadSourceV1 Source(
        TestCompanyContext context,
        TestCurrentUser user,
        IErpAuthorityRevalidationService? authority = null) =>
        new(
            null!,
            context,
            user,
            authority ?? new TestAuthority(new PermissionScope(
                true,
                false,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(),
                new HashSet<Guid> { Location },
                new HashSet<Guid>())),
            new RejectingCursorCodec(),
            TimeProvider.System,
            null!);

    private static CounterpartyOrganizationLocationOperatorCandidateRequestV1 Candidate() =>
        new(Company, "organization-location-writer", "organization.location.operator", Location, null, 25, null);

    private sealed class RejectingCursorCodec : ICounterpartyCursorCodec
    {
        public bool IsAvailable => true;
        public string Protect(CounterpartyCursorState state) => throw new NotSupportedException();
        public bool TryUnprotect(string cursor, out CounterpartyCursorState state)
        {
            state = null!;
            return false;
        }
    }

    private sealed class TestAuthority(PermissionScope scope) : IErpAuthorityRevalidationService
    {
        public List<string> Permissions { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Permissions.Add(permission);
            return Task.FromResult(companyId == Company ? scope : PermissionScope.Denied);
        }
    }

    private sealed class TestCompanyContext(Guid companyId) : GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class TestCurrentUser(string actorId, IReadOnlySet<Guid> companies)
        : GoymenERP.BuildingBlocks.Security.ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => companies;
        public bool CanAccessCompany(Guid companyId) => companies.Contains(companyId);
    }

    private sealed class TestConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => key switch
            {
                "DB_PROVIDER" => "SqlServer",
                "DB_CONNECTION_STRING" => "Server=127.0.0.1;Database=not-opened;User Id=x;Password=x;",
                _ => null,
            };
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
