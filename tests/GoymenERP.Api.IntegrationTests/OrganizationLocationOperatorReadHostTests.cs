using GoymenERP.Api.OrganizationRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OrganizationLocationOperatorReadHostTests
{
    private static readonly Guid Company = Guid.Parse("71000000-0000-4000-8000-000000000001");
    private static readonly Guid Location = Guid.Parse("75000000-0000-4000-8000-000000000001");
    private static readonly Guid Party = Guid.Parse("74000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Display_uses_the_authorized_location_binding_and_rechecks_the_location()
    {
        var organization = new StubOrganization(LocationRow(Party));
        var owner = new StubOwner
        {
            Display = new(
                CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success,
                [new(Location, Party, "CAR-1", "Tedarikçi A.Ş.", "Tedarikçi", true)]),
        };
        var host = Host(organization, new StubLocationStatus(), owner);

        var result = await host.ReadDisplayAsync(Location);

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Success, result.Outcome);
        Assert.Equal(2, organization.LocationReads);
        var request = Assert.Single(owner.DisplayRequests);
        Assert.Equal((Company, "actor-1"), (request.CompanyId, request.ActorId));
        Assert.Equal((Location, Party),
            (Assert.Single(request.Bindings).LocationId, Assert.Single(request.Bindings).PartyId));
        Assert.Equal((Party, "CAR-1", "Tedarikçi A.Ş.", "Tedarikçi", true),
            (result.Resource!.Operator!.PartyId,
                result.Resource.Operator.Code,
                result.Resource.Operator.LegalName,
                result.Resource.Operator.TradeName,
                result.Resource.Operator.IsCurrentlyEligible));
    }

    [Fact]
    public async Task Display_without_assignment_is_successful_and_never_calls_Counterparty()
    {
        var organization = new StubOrganization(LocationRow(null));
        var owner = new StubOwner();

        var result = await Host(organization, new StubLocationStatus(), owner).ReadDisplayAsync(Location);

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Success, result.Outcome);
        Assert.Null(result.Resource!.Operator);
        Assert.Empty(owner.DisplayRequests);
        Assert.Equal(2, organization.LocationReads);
    }

    [Fact]
    public async Task Display_without_assignment_refuses_a_new_assignment_between_reads()
    {
        var organization = new StubOrganization(LocationRow(null), LocationRow(Party));
        var owner = new StubOwner();

        var result = await Host(organization, new StubLocationStatus(), owner).ReadDisplayAsync(Location);

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Empty(owner.DisplayRequests);
        Assert.Equal(2, organization.LocationReads);
    }

    [Fact]
    public async Task Display_without_assignment_refuses_version_drift_between_reads()
    {
        var organization = new StubOrganization(LocationRow(null), LocationRow(null, 5));
        var owner = new StubOwner();

        var result = await Host(organization, new StubLocationStatus(), owner).ReadDisplayAsync(Location);

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Empty(owner.DisplayRequests);
        Assert.Equal(2, organization.LocationReads);
    }

    [Fact]
    public async Task Display_without_assignment_maps_disappearance_between_reads_to_not_found()
    {
        var organization = new StubOrganization(LocationRow(null), null);
        var owner = new StubOwner();

        var result = await Host(organization, new StubLocationStatus(), owner).ReadDisplayAsync(Location);

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.NotFound, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Empty(owner.DisplayRequests);
        Assert.Equal(2, organization.LocationReads);
    }

    [Theory]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied,
        OrganizationLocationOperatorReadOutcomeV1.NotFound)]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable,
        OrganizationLocationOperatorReadOutcomeV1.Unavailable)]
    public async Task Display_maps_owner_refusals_without_disclosing_owner_payload(
        CounterpartyOrganizationLocationOperatorReadOutcomeV1 ownerOutcome,
        OrganizationLocationOperatorReadOutcomeV1 expected)
    {
        var owner = new StubOwner { Display = new(ownerOutcome, []) };

        var result = await Host(new StubOrganization(LocationRow(Party)), new StubLocationStatus(), owner)
            .ReadDisplayAsync(Location);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Candidate_search_binds_company_actor_session_and_exact_location()
    {
        var status = new StubLocationStatus();
        var owner = new StubOwner
        {
            Candidates = new(
                CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success,
                [new(Party, "CAR-1", "Tedarikçi A.Ş.", null)],
                "opaque-next",
                true),
        };
        var host = Host(new StubOrganization(LocationRow(null)), status, owner);

        var result = await host.SearchCandidatesAsync(
            Location,
            new("tedarikçi", 10, "opaque-current"),
            "server-session");

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Success, result.Outcome);
        Assert.Equal(2, status.Reads);
        var request = Assert.Single(owner.CandidateRequests);
        Assert.Equal(
            (Company, "actor-1", "server-session", Location, "tedarikçi", 10, "opaque-current"),
            (request.CompanyId, request.ActorId, request.SessionBinding, request.LocationId,
                request.Query, request.PageSize, request.Cursor));
        Assert.Equal("opaque-next", result.Resource!.NextCursor);
        Assert.True(result.Resource.HasMore);
        Assert.Equal(Party, Assert.Single(result.Resource.Items).PartyId);
    }

    [Theory]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied,
        OrganizationLocationOperatorReadOutcomeV1.NotFound)]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.InvalidCursor,
        OrganizationLocationOperatorReadOutcomeV1.InvalidCursor)]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable,
        OrganizationLocationOperatorReadOutcomeV1.Unavailable)]
    public async Task Candidate_search_maps_closed_owner_outcomes(
        CounterpartyOrganizationLocationOperatorReadOutcomeV1 ownerOutcome,
        OrganizationLocationOperatorReadOutcomeV1 expected)
    {
        var owner = new StubOwner { Candidates = new(ownerOutcome, [], null, false) };

        var result = await Host(new StubOrganization(LocationRow(null)), new StubLocationStatus(), owner)
            .SearchCandidatesAsync(Location, new(null, 25, null), "server-session");

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Unknown_location_and_invalid_server_binding_stop_before_Counterparty()
    {
        var owner = new StubOwner();
        var unknown = await Host(
                new StubOrganization((LocationSummary?)null),
                new StubLocationStatus { Resource = null },
                owner)
            .SearchCandidatesAsync(Location, new(null, 25, null), "server-session");
        var invalidSession = await Host(
                new StubOrganization((LocationSummary?)null),
                new StubLocationStatus(),
                owner)
            .SearchCandidatesAsync(Location, new(null, 25, null), " ");

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.NotFound, unknown.Outcome);
        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Unavailable, invalidSession.Outcome);
        Assert.Empty(owner.CandidateRequests);
    }

    private static OrganizationLocationOperatorReadHostV1 Host(
        StubOrganization organization,
        StubLocationStatus status,
        StubOwner owner) =>
        new(organization, status, owner, new CompanyContext(Company), new UserContext("actor-1", Company));

    private static LocationSummary LocationRow(Guid? partyId, long version = 4) =>
        new(Location, Guid.NewGuid(), "LOC-1", "Harici Depo", true, true, version, partyId);

    private sealed class StubOrganization : IOrganizationService
    {
        private readonly Queue<LocationSummary?> locations;

        public StubOrganization(params LocationSummary?[] locations)
        {
            this.locations = new Queue<LocationSummary?>(locations);
        }

        public int LocationReads { get; private set; }

        public Task<LocationSummary?> GetLocationAsync(Guid id, CancellationToken cancellationToken)
        {
            LocationReads++;
            var location = locations.Count > 1 ? locations.Dequeue() : locations.Peek();
            return Task.FromResult(id == Location ? location : null);
        }

        public Task<IReadOnlyList<CompanySummary>> GetAuthorizedCompaniesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SiteSummary>> GetSitesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PageResult<LocationSummary>> GetLocationsAsync(int page, int pageSize,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkCenterSummary>> GetWorkCentersAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<WorkCenterDetail?> GetWorkCenterAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CommandExecutionResult> SetLocationActiveStateAsync(Guid id, SetLocationActiveState command,
            CommandMetadata metadata, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubLocationStatus : IOrganizationLocationStatusReaderV1
    {
        public OrganizationLocationStatusV1? Resource { get; init; } = new(Location, true, true);
        public int Reads { get; private set; }

        public Task<OrganizationLocationStatusV1?> ReadAsync(Guid companyId, Guid locationId,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(companyId == Company && locationId == Location ? Resource : null);
        }
    }

    private sealed class StubOwner : ICounterpartyOrganizationLocationOperatorReadSourceV1
    {
        public CounterpartyOrganizationLocationOperatorDisplayAnswerV1 Display { get; init; } =
            new(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable, []);
        public CounterpartyOrganizationLocationOperatorCandidateAnswerV1 Candidates { get; init; } =
            new(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable, [], null, false);
        public List<CounterpartyOrganizationLocationOperatorDisplayRequestV1> DisplayRequests { get; } = [];
        public List<CounterpartyOrganizationLocationOperatorCandidateRequestV1> CandidateRequests { get; } = [];

        public Task<CounterpartyOrganizationLocationOperatorDisplayAnswerV1> ReadAuthorizedDisplaysAsync(
            CounterpartyOrganizationLocationOperatorDisplayRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            DisplayRequests.Add(request);
            return Task.FromResult(Display);
        }

        public Task<CounterpartyOrganizationLocationOperatorCandidateAnswerV1> SearchAuthorizedCandidatesAsync(
            CounterpartyOrganizationLocationOperatorCandidateRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            CandidateRequests.Add(request);
            return Task.FromResult(Candidates);
        }
    }

    private sealed class CompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class UserContext(string actorId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
    }
}
