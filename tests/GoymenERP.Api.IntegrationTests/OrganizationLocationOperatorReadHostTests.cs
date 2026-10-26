using GoymenERP.Api.OrganizationRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OrganizationLocationOperatorReadHostTests
{
    private static readonly Guid Company = Guid.Parse("71000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("71000000-0000-4000-8000-000000000002");
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

    [Theory]
    [InlineData("actor")]
    [InlineData("authentication")]
    [InlineData("company")]
    [InlineData("resolution")]
    [InlineData("access")]
    public async Task Display_without_assignment_refuses_identity_drift_on_the_final_read(string drift)
    {
        var company = new CompanyContext(Company);
        var user = new UserContext("actor-1", Company);
        var owner = new StubOwner();
        var organization = new StubOrganization(LocationRow(null))
        {
            OnSecondRead = () => ApplyIdentityDrift(drift, company, user),
        };

        var result = await Host(organization, new StubLocationStatus(), owner, company, user)
            .ReadDisplayAsync(Location);

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Empty(owner.DisplayRequests);
        Assert.Equal(2, organization.LocationReads);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("authentication")]
    [InlineData("company")]
    [InlineData("resolution")]
    [InlineData("access")]
    public async Task Assigned_display_refuses_identity_drift_on_the_final_read(string drift)
    {
        var company = new CompanyContext(Company);
        var user = new UserContext("actor-1", Company);
        var organization = new StubOrganization(LocationRow(Party))
        {
            OnSecondRead = () => ApplyIdentityDrift(drift, company, user),
        };
        var owner = new StubOwner
        {
            Display = new(
                CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success,
                [new(Location, Party, "CAR-1", "Tedarikçi A.Ş.", "Tedarikçi", true)]),
        };

        var result = await Host(organization, new StubLocationStatus(), owner, company, user)
            .ReadDisplayAsync(Location);

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
        var request = Assert.Single(owner.DisplayRequests);
        Assert.Equal((Company, "actor-1"), (request.CompanyId, request.ActorId));
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
        (int)OrganizationLocationOperatorReadOutcomeV1.NotFound)]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable,
        (int)OrganizationLocationOperatorReadOutcomeV1.Unavailable)]
    public async Task Display_maps_owner_refusals_without_disclosing_owner_payload(
        CounterpartyOrganizationLocationOperatorReadOutcomeV1 ownerOutcome,
        int expected)
    {
        var owner = new StubOwner { Display = new(ownerOutcome, []) };

        var result = await Host(new StubOrganization(LocationRow(Party)), new StubLocationStatus(), owner)
            .ReadDisplayAsync(Location);

        Assert.Equal((OrganizationLocationOperatorReadOutcomeV1)expected, result.Outcome);
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
    [InlineData("actor")]
    [InlineData("authentication")]
    [InlineData("company")]
    [InlineData("resolution")]
    [InlineData("access")]
    public async Task Candidate_search_refuses_identity_drift_on_the_final_read(string drift)
    {
        var company = new CompanyContext(Company);
        var user = new UserContext("actor-1", Company);
        var status = new StubLocationStatus
        {
            OnSecondRead = () => ApplyIdentityDrift(drift, company, user),
        };
        var owner = new StubOwner
        {
            Candidates = new(
                CounterpartyOrganizationLocationOperatorReadOutcomeV1.Success,
                [new(Party, "CAR-1", "Tedarikçi A.Ş.", null)],
                null,
                false),
        };

        var result = await Host(new StubOrganization(LocationRow(null)), status, owner, company, user)
            .SearchCandidatesAsync(Location, new(null, 25, null), "server-session");

        Assert.Equal(OrganizationLocationOperatorReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
        var request = Assert.Single(owner.CandidateRequests);
        Assert.Equal((Company, "actor-1"), (request.CompanyId, request.ActorId));
        Assert.Equal(2, status.Reads);
    }

    [Theory]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Denied,
        (int)OrganizationLocationOperatorReadOutcomeV1.NotFound)]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.InvalidCursor,
        (int)OrganizationLocationOperatorReadOutcomeV1.InvalidCursor)]
    [InlineData(CounterpartyOrganizationLocationOperatorReadOutcomeV1.Unavailable,
        (int)OrganizationLocationOperatorReadOutcomeV1.Unavailable)]
    public async Task Candidate_search_maps_closed_owner_outcomes(
        CounterpartyOrganizationLocationOperatorReadOutcomeV1 ownerOutcome,
        int expected)
    {
        var owner = new StubOwner { Candidates = new(ownerOutcome, [], null, false) };

        var result = await Host(new StubOrganization(LocationRow(null)), new StubLocationStatus(), owner)
            .SearchCandidatesAsync(Location, new(null, 25, null), "server-session");

        Assert.Equal((OrganizationLocationOperatorReadOutcomeV1)expected, result.Outcome);
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
        StubOwner owner,
        CompanyContext? company = null,
        UserContext? user = null) =>
        new(organization, status, owner, company ?? new CompanyContext(Company),
            user ?? new UserContext("actor-1", Company));

    private static void ApplyIdentityDrift(string drift, CompanyContext company, UserContext user)
    {
        switch (drift)
        {
            case "actor":
                user.SetActor("actor-2");
                break;
            case "authentication":
                user.SetAuthenticated(false);
                break;
            case "company":
                company.Set(OtherCompany);
                break;
            case "resolution":
                company.Set(Guid.Empty);
                break;
            case "access":
                user.RemoveCompany(Company);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(drift), drift, null);
        }
    }

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
        public Action? OnSecondRead { get; init; }

        public Task<LocationSummary?> GetLocationAsync(Guid id, CancellationToken cancellationToken)
        {
            LocationReads++;
            if (LocationReads == 2) OnSecondRead?.Invoke();
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
        public Action? OnSecondRead { get; init; }

        public Task<OrganizationLocationStatusV1?> ReadAsync(Guid companyId, Guid locationId,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Reads == 2) OnSecondRead?.Invoke();
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
        private readonly HashSet<Guid> allowedCompanyIds = [companyId];

        public bool IsAuthenticated { get; private set; } = true;
        public string? UserId { get; private set; } = actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => allowedCompanyIds;
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
        public void SetActor(string value) => UserId = value;
        public void SetAuthenticated(bool value) => IsAuthenticated = value;
        public void RemoveCompany(Guid value) => allowedCompanyIds.Remove(value);
    }
}
