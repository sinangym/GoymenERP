using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// ORG-03: <c>WorkCenterSummary.CanOpenLocationDetail</c> must agree with the location card itself.
/// <para>
/// The failure mode this guards is DRIFT, and it is the classic shape: two places computing one
/// authorisation. The flag says a row opens and the detail read refuses, or the flag says it does not
/// and the user loses access they actually hold. Today both derive from the same pair —
/// <c>GetPermissionScopeAsync(OrganizationLocationsRead)</c> then <c>ApplyLocationScope</c> — and this
/// case exists so that a second, parallel computation cannot be introduced quietly: it compares the flag
/// against what <c>GetLocationAsync</c> ACTUALLY answers, not against a re-implementation of the rule.
/// </para>
/// <para>
/// The scope predicate has three independent branches (a location by id, its site, or a work centre on
/// it), which is exactly why a second implementation would drift — forgetting one branch is easy and
/// silent. So the granted scope below exercises ALL THREE, plus an ungranted location, and the case
/// pins that the answers are not uniform: an equivalence that held because everything was false would
/// prove nothing.
/// </para>
/// <para>
/// The two reads deliberately use DIFFERENT permissions — rows come from
/// <c>organization.work-centers.read</c> and the flag from <c>organization.locations.read</c> — because
/// ORG-03 exists for the person who holds the first and not the second. The fake below answers each
/// permission separately for that reason.
/// </para>
/// SENTETIK: in-memory store, synthetic ids, no database and no host.
/// </summary>
public sealed class WorkCenterLocationOpenabilityTests
{
    private static readonly Guid CompanyA = Guid.Parse("0c000000-0000-4000-8000-00000000000a");
    private static readonly Guid SiteDirect = Guid.Parse("0c100000-0000-4000-8000-000000000001");
    private static readonly Guid SiteGranted = Guid.Parse("0c100000-0000-4000-8000-000000000002");
    private static readonly Guid SiteViaWorkCenter = Guid.Parse("0c100000-0000-4000-8000-000000000003");
    private static readonly Guid SiteUngranted = Guid.Parse("0c100000-0000-4000-8000-000000000004");
    private static readonly Guid LocationById = Guid.Parse("0c200000-0000-4000-8000-000000000001");
    private static readonly Guid LocationBySite = Guid.Parse("0c200000-0000-4000-8000-000000000002");
    private static readonly Guid LocationByWorkCenter = Guid.Parse("0c200000-0000-4000-8000-000000000003");
    private static readonly Guid LocationUngranted = Guid.Parse("0c200000-0000-4000-8000-000000000004");
    private static readonly Guid WorkCenterById = Guid.Parse("0c300000-0000-4000-8000-000000000001");
    private static readonly Guid WorkCenterBySite = Guid.Parse("0c300000-0000-4000-8000-000000000002");
    private static readonly Guid WorkCenterGranting = Guid.Parse("0c300000-0000-4000-8000-000000000003");
    private static readonly Guid WorkCenterUngranted = Guid.Parse("0c300000-0000-4000-8000-000000000004");

    [Fact]
    public async Task The_flag_agrees_with_the_location_card_on_every_row_and_the_answers_are_not_uniform()
    {
        var database = await SeedAsync(nameof(The_flag_agrees_with_the_location_card_on_every_row_and_the_answers_are_not_uniform));
        var locations = new PermissionScope(
            IsGranted: true,
            IsCompanyWide: false,
            RoleCodes: new HashSet<string>(StringComparer.Ordinal),
            SiteIds: new HashSet<Guid> { SiteGranted },
            LocationIds: new HashSet<Guid> { LocationById },
            WorkCenterIds: new HashSet<Guid> { WorkCenterGranting });
        var service = Service(database, locations);

        var rows = await service.GetWorkCentersAsync(CancellationToken.None);
        Assert.Equal(4, rows.Count);

        // The guarantee, stated as the test: the flag and the card cannot disagree. Comparing against
        // what GetLocationAsync ANSWERS — rather than against the scope rule re-applied here — is what
        // makes a second, drifting implementation fail this case.
        foreach (var row in rows)
        {
            var card = await service.GetLocationAsync(row.LocationId, CancellationToken.None);
            Assert.Equal(card is not null, row.CanOpenLocationDetail);
        }

        // Not vacuous: all three granting branches answer true and the ungranted one answers false, so
        // the equivalence above is exercised in both directions rather than holding over a constant.
        Assert.True(Flag(rows, WorkCenterById), "a location granted by its own id must open");
        Assert.True(Flag(rows, WorkCenterBySite), "a location granted through its site must open");
        Assert.True(Flag(rows, WorkCenterGranting), "a location granted through a work centre on it must open");
        Assert.False(Flag(rows, WorkCenterUngranted), "an ungranted location must not open");
    }

    [Fact]
    public async Task Without_the_location_permission_every_row_says_closed_and_the_card_refuses_too()
    {
        var database = await SeedAsync(nameof(Without_the_location_permission_every_row_says_closed_and_the_card_refuses_too));
        var service = Service(database, PermissionScope.Denied);

        var rows = await service.GetWorkCentersAsync(CancellationToken.None);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.False(row.CanOpenLocationDetail));

        // The same equivalence on the closed side: the card refuses every one of them, so "no link" is
        // the truth about this caller rather than a default the flag happens to carry.
        foreach (var row in rows)
        {
            Assert.Null(await service.GetLocationAsync(row.LocationId, CancellationToken.None));
        }
    }

    private static bool Flag(IReadOnlyList<WorkCenterSummary> rows, Guid workCenterId) =>
        rows.Single(row => row.Id == workCenterId).CanOpenLocationDetail;

    private static OrganizationService Service(string database, PermissionScope locations)
    {
        var companyContext = Resolved(CompanyA);
        return new OrganizationService(
            Context(database, companyContext),
            new FakeCurrentUser(),
            companyContext,
            new FakeAuthorization(locations),
            new FakeAuditCollector(),
            NullLogger<OrganizationService>.Instance);
    }

    private static async Task<string> SeedAsync(string database)
    {
        var companyContext = Resolved(CompanyA);
        await using var context = Context(database, companyContext);
        await context.Database.EnsureCreatedAsync();
        context.Sites.AddRange(
            new Site(SiteDirect, CompanyA, "S1", "Site 1", SiteType.Factory),
            new Site(SiteGranted, CompanyA, "S2", "Site 2", SiteType.Factory),
            new Site(SiteViaWorkCenter, CompanyA, "S3", "Site 3", SiteType.Factory),
            new Site(SiteUngranted, CompanyA, "S4", "Site 4", SiteType.Factory));
        context.Locations.AddRange(
            new Location(LocationById, CompanyA, SiteDirect, "L1", "Location 1", false),
            new Location(LocationBySite, CompanyA, SiteGranted, "L2", "Location 2", false),
            new Location(LocationByWorkCenter, CompanyA, SiteViaWorkCenter, "L3", "Location 3", false),
            new Location(LocationUngranted, CompanyA, SiteUngranted, "L4", "Location 4", false));
        context.WorkCenters.AddRange(
            new WorkCenter(WorkCenterById, CompanyA, LocationById, "W1", "Work centre 1", WorkCenterKind.Paint),
            new WorkCenter(WorkCenterBySite, CompanyA, LocationBySite, "W2", "Work centre 2", WorkCenterKind.Slitting),
            new WorkCenter(WorkCenterGranting, CompanyA, LocationByWorkCenter, "W3", "Work centre 3", WorkCenterKind.ColdRolling),
            new WorkCenter(WorkCenterUngranted, CompanyA, LocationUngranted, "W4", "Work centre 4", WorkCenterKind.CutToLength));
        await context.SaveChangesAsync();
        return database;
    }

    private static CompanyContext Resolved(Guid companyId)
    {
        var context = new CompanyContext();
        context.Set(companyId);
        return context;
    }

    private static OrganizationDbContext Context(string database, ICompanyContext companyContext) => new(
        new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
        companyContext,
        new OrganizationBootstrapWriteScope());

    /// <summary>Work centres are readable company-wide; the LOCATION scope is the one under test.</summary>
    private sealed class FakeAuthorization(PermissionScope locations) : IErpAuthorizationService
    {
        private static readonly PermissionScope CompanyWide = new(
            true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These cases read scopes, never a snapshot.");

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeFor(permission).IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeFor(permission));

        private PermissionScope ScopeFor(string permission) =>
            permission == ErpPermissions.OrganizationLocationsRead ? locations : CompanyWide;
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => "synthetic-org03-reader";

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA };

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class FakeAuditCollector : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent)
        {
        }
    }
}
