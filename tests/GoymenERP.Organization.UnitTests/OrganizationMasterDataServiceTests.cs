using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Locations;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Domain.WorkCenters;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// The site / location / work-centre master edit against the module's own DbContext (EF InMemory; the SQL Server
/// relational facts — unique code index, rowversion race, audit rows — are the host's SqlServerFact suite). Every row,
/// grant and actor here is synthetic.
/// </summary>
public sealed class OrganizationMasterDataServiceTests
{
    private static readonly Guid CompanyA = Guid.Parse("0f000000-0000-4000-8000-0000000000a1");
    private static readonly Guid CompanyB = Guid.Parse("0f000000-0000-4000-8000-0000000000b1");
    private static readonly Guid FactorySite = Guid.Parse("0f200000-0000-4000-8000-0000000000a1");
    private static readonly Guid ExternalSite = Guid.Parse("0f200000-0000-4000-8000-0000000000a2");
    private static readonly Guid Hall = Guid.Parse("0f100000-0000-4000-8000-0000000000a1");
    private static readonly Guid Line = Guid.Parse("0f300000-0000-4000-8000-0000000000a1");
    private static readonly Guid SiteOfB = Guid.Parse("0f200000-0000-4000-8000-0000000000b1");
    private static readonly Guid LocationOfB = Guid.Parse("0f100000-0000-4000-8000-0000000000b1");
    private const string Actor = "SENTETIK-org-editor";

    [Fact]
    public async Task A_created_location_is_readable_after_reopen_and_offered_to_the_next_modules_picker()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());

        var created = await run.Service.CreateLocationAsync(
            new CreateLocation(FactorySite, "  SYN-HOL-9 ", " Sentetik Hol 9 ", " Yeni hol açıldı "),
            run.Metadata("create-hol-9"),
            CancellationToken.None);

        Assert.Equal(CommandResult.Created, created.Result);
        Assert.Equal(201, created.Response.StatusCode);
        Assert.False(created.IsReplay);
        var record = Assert.IsType<OrganizationCreatedRecord>(created.Response.Created);
        Assert.Equal(1, record.Version);

        // Close and reopen: a fresh context and service read the saved record as the location card does.
        var reopened = new Run(database, CompanyA, CompanyWide());
        var card = await reopened.Reads.GetLocationAsync(record.Id, CancellationToken.None);
        Assert.Equal(new LocationSummary(record.Id, FactorySite, "SYN-HOL-9", "Sentetik Hol 9", false, true, 1), card);

        // The next module's selector (Inventory's location candidates) offers it without any Organization grant.
        var candidates = await new OrganizationLocationCandidateReader(reopened.Db, reopened.Company)
            .SearchAsync(CompanyA, new OrganizationLocationCandidateQueryV1("SYN-HOL-9"));
        var candidate = Assert.Single(candidates.Items);
        Assert.Equal((record.Id, "SYN-HOL-9", FactorySite), (candidate.LocationId, candidate.LocationCode, candidate.SiteId));

        var audit = Assert.Single(run.Audit.Drafts);
        Assert.Equal(
            ("organization.location.created.v1", "Organization.Location", record.Id.ToString("D"), "Create"),
            (audit.EventType, audit.EntityType, audit.EntityId, audit.Operation));
        Assert.Equal(((long?)null, (long?)1), (audit.EntityVersionBefore, audit.EntityVersionAfter));
        Assert.Equal(OrganizationPermissionManifestV1.LocationsWrite, audit.PermissionCode);
        Assert.Equal(SourceChannels.Web, audit.SourceChannel);
        Assert.Equal("Yeni hol açıldı", audit.Reason);
        Assert.Equal(CompanyA, audit.CompanyId);
    }

    [Fact]
    public async Task A_location_under_an_external_warehouse_site_is_external_and_is_not_a_picker_candidate()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());

        var created = await run.Service.CreateLocationAsync(
            new CreateLocation(ExternalSite, "SYN-EXT-2", "Sentetik harici depo 2", "Tedarikçi deposu eklendi"),
            run.Metadata("create-ext"),
            CancellationToken.None);

        var id = created.Response.Created!.Id;
        var stored = await new Run(database, CompanyA, CompanyWide()).Db.Locations.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.True(stored.IsExternal);
        var candidates = await new OrganizationLocationCandidateReader(run.Db, run.Company)
            .SearchAsync(CompanyA, new OrganizationLocationCandidateQueryV1("SYN-EXT-2"));
        Assert.Empty(candidates.Items);
    }

    [Fact]
    public async Task A_replayed_key_returns_the_stored_answer_and_applies_nothing_again()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());
        var command = new CreateSite("SYN-SITE-NEW", "Sentetik yeni tesis", "Warehouse", "Yeni depo");

        var first = await run.Service.CreateSiteAsync(command, run.Metadata("site-key"), CancellationToken.None);
        var replay = await new Run(database, CompanyA, CompanyWide()).Service
            .CreateSiteAsync(command, run.Metadata("site-key", correlation: "second-correlation"), CancellationToken.None);

        Assert.Equal(CommandResult.Created, replay.Result);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Response, replay.Response);
        Assert.Equal("first-correlation", replay.OriginalCorrelationId);
        var check = new Run(database, CompanyA, CompanyWide());
        Assert.Single(await check.Db.Sites.Where(x => x.Code == "SYN-SITE-NEW").ToListAsync());
        Assert.Single(await check.Db.CommandExecutions.ToListAsync());
        Assert.Single(run.Audit.Drafts);

        // The same key with a different payload (or actor) is an idempotency conflict, never a second site.
        var reused = await check.Service.CreateSiteAsync(
            command with { Name = "Başka ad" }, check.Metadata("site-key"), CancellationToken.None);
        Assert.Equal(CommandResult.IdempotencyConflict, reused.Result);
        Assert.Equal(409, reused.Response.StatusCode);
        Assert.Null(reused.Response.Conflict);
        var otherActor = new Run(database, CompanyA, CompanyWide(), actor: "SENTETIK-other");
        Assert.Equal(
            CommandResult.IdempotencyConflict,
            (await otherActor.Service.CreateSiteAsync(command, otherActor.Metadata("site-key"), CancellationToken.None)).Result);
        Assert.Single(await check.Db.Sites.Where(x => x.Code == "SYN-SITE-NEW").ToListAsync());
    }

    [Fact]
    public async Task An_edit_renames_moves_the_version_once_and_a_stale_version_is_a_stored_409()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());

        var updated = await run.Service.UpdateLocationAsync(
            Hall,
            new UpdateLocation("SYN-HOL-0B", "Sentetik Hol 0 Boya", 1, "Ad netleştirildi"),
            run.Metadata("rename"),
            CancellationToken.None);
        Assert.Equal(CommandResult.Succeeded, updated.Result);
        Assert.Equal(204, updated.Response.StatusCode);

        var reopened = new Run(database, CompanyA, CompanyWide());
        var card = await reopened.Reads.GetLocationAsync(Hall, CancellationToken.None);
        Assert.Equal(new LocationSummary(Hall, FactorySite, "SYN-HOL-0B", "Sentetik Hol 0 Boya", false, true, 2), card);
        var audit = Assert.Single(run.Audit.Drafts);
        Assert.Equal(("Update", (long?)1, (long?)2), (audit.Operation, audit.EntityVersionBefore, audit.EntityVersionAfter));
        Assert.Contains("\"Code\":\"SYN-HOL-0\"", audit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("\"Code\":\"SYN-HOL-0B\"", audit.AfterJson, StringComparison.Ordinal);

        var stale = await reopened.Service.UpdateLocationAsync(
            Hall,
            new UpdateLocation("SYN-HOL-0C", "Eski sürümle", 1, "Eski ekran"),
            reopened.Metadata("stale"),
            CancellationToken.None);
        Assert.Equal(CommandResult.VersionConflict, stale.Result);
        Assert.Equal(409, stale.Response.StatusCode);
        Assert.Equal(new VersionConflictSnapshot(1, 2), stale.Response.Conflict);
        Assert.Empty(reopened.Audit.Drafts);

        // The stored 409 is replayed for its key even after the record moves on.
        var replay = await new Run(database, CompanyA, CompanyWide()).Service.UpdateLocationAsync(
            Hall,
            new UpdateLocation("SYN-HOL-0C", "Eski sürümle", 1, "Eski ekran"),
            reopened.Metadata("stale"),
            CancellationToken.None);
        Assert.True(replay.IsReplay);
        Assert.Equal(stale.Response, replay.Response);
        Assert.Equal("SYN-HOL-0B", (await new Run(database, CompanyA, CompanyWide()).Reads
            .GetLocationAsync(Hall, CancellationToken.None))!.Code);
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_is_audited_as_no_change_and_keeps_the_version()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());

        var result = await run.Service.UpdateWorkCenterAsync(
            Line,
            new UpdateWorkCenter(" SYN-WC-1 ", "Sentetik Boya Hattı", 1, "Kontrol"),
            run.Metadata("same"),
            CancellationToken.None);

        Assert.Equal(CommandResult.Succeeded, result.Result);
        var detail = await new Run(database, CompanyA, CompanyWide()).Reads.GetWorkCenterAsync(Line, CancellationToken.None);
        Assert.Equal(1, detail!.Version);
        var audit = Assert.Single(run.Audit.Drafts);
        Assert.Equal(("NoChange", (long?)1, (long?)1), (audit.Operation, audit.EntityVersionBefore, audit.EntityVersionAfter));
    }

    [Fact]
    public async Task A_code_already_used_in_the_company_is_a_stored_422_and_changes_nothing()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());

        var create = await run.Service.CreateLocationAsync(
            new CreateLocation(FactorySite, "SYN-HOL-0", "Aynı kod", "Deneme"),
            run.Metadata("dup-create"),
            CancellationToken.None);
        Assert.Equal(CommandResult.CodeInUse, create.Result);
        Assert.Equal(422, create.Response.StatusCode);

        var otherLocation = await run.Service.CreateLocationAsync(
            new CreateLocation(FactorySite, "SYN-HOL-1", "Sentetik Hol 1", "Deneme"),
            run.Metadata("second"),
            CancellationToken.None);
        var rename = await run.Service.UpdateLocationAsync(
            otherLocation.Response.Created!.Id,
            new UpdateLocation("SYN-HOL-0", "Sentetik Hol 1", 1, "Deneme"),
            run.Metadata("dup-rename"),
            CancellationToken.None);
        Assert.Equal(CommandResult.CodeInUse, rename.Result);

        // Another company's code does not collide: the unique index is per company.
        var runB = new Run(database, CompanyB, CompanyWide());
        var inB = await runB.Service.CreateWorkCenterAsync(
            new CreateWorkCenter(LocationOfB, "SYN-WC-1", "B hattı", "Slitting", "Deneme"),
            runB.Metadata("b-wc"),
            CancellationToken.None);
        Assert.Equal(CommandResult.Created, inB.Result);

        var check = new Run(database, CompanyA, CompanyWide());
        Assert.Single(await check.Db.Locations.Where(x => x.Code == "SYN-HOL-0").ToListAsync());
        Assert.Equal("SYN-HOL-1", (await check.Reads.GetLocationAsync(otherLocation.Response.Created.Id, CancellationToken.None))!.Code);
        Assert.Single(run.Audit.Drafts); // only the one location that was really created
    }

    [Fact]
    public async Task Another_companys_record_or_parent_is_not_found_and_a_foreign_context_is_refused()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());

        Assert.Equal(CommandResult.NotFound, (await run.Service.UpdateLocationAsync(
            LocationOfB, new UpdateLocation("X", "X", 1, "Deneme"), run.Metadata("b-loc"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.NotFound, (await run.Service.CreateLocationAsync(
            new CreateLocation(SiteOfB, "SYN-NEW", "X", "Deneme"), run.Metadata("b-site"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.NotFound, (await run.Service.CreateWorkCenterAsync(
            new CreateWorkCenter(LocationOfB, "SYN-NEW", "X", "Paint", "Deneme"), run.Metadata("b-wc"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.NotFound, (await run.Service.UpdateSiteAsync(
            SiteOfB, new UpdateSite("X", "X", 1, "Deneme"), run.Metadata("b-site-edit"), CancellationToken.None)).Result);
        Assert.Empty(run.Audit.Drafts);

        // Metadata naming another company than the resolved context never runs.
        await Assert.ThrowsAsync<InvalidOperationException>(() => run.Service.CreateSiteAsync(
            new CreateSite("SYN-X", "X", "Factory", "Deneme"),
            run.Metadata("foreign") with { CompanyId = CompanyB },
            CancellationToken.None));
        var b = new Run(database, CompanyB, CompanyWide());
        Assert.Equal("SYN-SITE-B", (await b.Db.Sites.SingleAsync(x => x.Id == SiteOfB)).Code);
    }

    [Fact]
    public async Task Each_write_is_bounded_by_its_own_grants_row_scope()
    {
        var database = await SeedAsync();

        // A site-scoped location writer adds a location to its site and edits the site's locations only.
        var siteScoped = new Run(database, CompanyA, Scoped(sites: [FactorySite]));
        Assert.Equal(CommandResult.Created, (await siteScoped.Service.CreateLocationAsync(
            new CreateLocation(FactorySite, "SYN-S-1", "X", "Deneme"), siteScoped.Metadata("s1"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.NotFound, (await siteScoped.Service.CreateLocationAsync(
            new CreateLocation(ExternalSite, "SYN-S-2", "X", "Deneme"), siteScoped.Metadata("s2"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.Succeeded, (await siteScoped.Service.UpdateLocationAsync(
            Hall, new UpdateLocation("SYN-HOL-0", "Yeni ad", 1, "Deneme"), siteScoped.Metadata("s3"), CancellationToken.None)).Result);

        // A location-scoped writer edits that location but cannot add a location beside it.
        var locationScoped = new Run(database, CompanyA, Scoped(locations: [Hall]));
        Assert.Equal(CommandResult.NotFound, (await locationScoped.Service.CreateLocationAsync(
            new CreateLocation(FactorySite, "SYN-L-1", "X", "Deneme"), locationScoped.Metadata("l1"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.Created, (await locationScoped.Service.CreateWorkCenterAsync(
            new CreateWorkCenter(Hall, "SYN-WC-9", "X", "CutToLength", "Deneme"), locationScoped.Metadata("l2"), CancellationToken.None)).Result);

        // A work-centre-scoped writer edits that work centre and adds none.
        var workCenterScoped = new Run(database, CompanyA, Scoped(workCenters: [Line]));
        Assert.Equal(CommandResult.NotFound, (await workCenterScoped.Service.CreateWorkCenterAsync(
            new CreateWorkCenter(Hall, "SYN-WC-8", "X", "Paint", "Deneme"), workCenterScoped.Metadata("w1"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.Succeeded, (await workCenterScoped.Service.UpdateWorkCenterAsync(
            Line, new UpdateWorkCenter("SYN-WC-1", "Yeni hat adı", 1, "Deneme"), workCenterScoped.Metadata("w2"), CancellationToken.None)).Result);

        // The site master is company-wide only: a site-scoped holder reaches no site record.
        Assert.Equal(CommandResult.NotFound, (await siteScoped.Service.CreateSiteAsync(
            new CreateSite("SYN-SITE-X", "X", "Factory", "Deneme"), siteScoped.Metadata("site-create"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.NotFound, (await siteScoped.Service.UpdateSiteAsync(
            FactorySite, new UpdateSite("SYN-SITE-A", "X", 1, "Deneme"), siteScoped.Metadata("site-edit"), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task A_created_work_center_is_the_planning_reference_and_card_the_next_module_reads()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());

        var created = await run.Service.CreateWorkCenterAsync(
            new CreateWorkCenter(Hall, "SYN-WC-NEW", "Sentetik Dilme Hattı", "Slitting", "Yeni hat kuruldu"),
            run.Metadata("wc"),
            CancellationToken.None);
        var id = created.Response.Created!.Id;

        var reopened = new Run(database, CompanyA, CompanyWide());
        Assert.Equal(
            new OrganizationWorkCenterPlanningReferenceV1(id, "Slitting"),
            await new OrganizationWorkCenterPlanningReferenceReader(reopened.Db, reopened.Company).ReadAsync(CompanyA, id));
        var listed = Assert.Single(await reopened.Reads.GetWorkCentersAsync(CancellationToken.None), x => x.Id == id);
        Assert.Equal((Hall, "SYN-WC-NEW", "Slitting", true), (listed.LocationId, listed.Code, listed.Kind, listed.IsActive));
    }

    [Fact]
    public async Task The_seed_recognises_a_renamed_seeded_record_by_its_id_and_does_not_re_add_it()
    {
        var database = $"organization-master-seed-{Guid.NewGuid():N}";
        var companyId = Guid.Parse("0f000000-0000-4000-8000-0000000000c1");
        var options = Options.Create(new OrganizationSeedOptions
        {
            Companies = [new SeedCompany { Id = companyId, Code = "SYNC", LegalName = "Sentetik C", ApplyTopologyTemplates = true, IsPlaceholder = true }],
            SiteTemplates =
            [
                new SeedSiteTemplate
                {
                    Code = "SYN-SEED-SITE", Name = "Sentetik tesis", Type = "Factory",
                    Locations = [new SeedLocation { Code = "SYN-SEED-LOC", Name = "Sentetik lokasyon" }],
                },
            ],
            WorkCenters = [new SeedWorkCenter { Code = "SYN-SEED-WC", Name = "Sentetik hat", LocationCode = "SYN-SEED-LOC", Kind = "Paint" }],
        });
        await SeedWithAsync(database, options);

        var run = new Run(database, companyId, CompanyWide());
        var site = await run.Db.Sites.SingleAsync();
        var location = await run.Db.Locations.SingleAsync();
        var workCenter = await run.Db.WorkCenters.SingleAsync();
        Assert.Equal(CommandResult.Succeeded, (await run.Service.UpdateSiteAsync(
            site.Id, new UpdateSite("SYN-SEED-SITE-R", "Yeni", 1, "Deneme"), run.Metadata("r1"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.Succeeded, (await run.Service.UpdateLocationAsync(
            location.Id, new UpdateLocation("SYN-SEED-LOC-R", "Yeni", 1, "Deneme"), run.Metadata("r2"), CancellationToken.None)).Result);
        Assert.Equal(CommandResult.Succeeded, (await run.Service.UpdateWorkCenterAsync(
            workCenter.Id, new UpdateWorkCenter("SYN-SEED-WC-R", "Yeni", 1, "Deneme"), run.Metadata("r3"), CancellationToken.None)).Result);

        // The next host start runs the seed again: nothing is re-added under the old codes and nothing collides.
        await SeedWithAsync(database, options);

        var check = new Run(database, companyId, CompanyWide());
        Assert.Equal("SYN-SEED-SITE-R", Assert.Single(await check.Db.Sites.ToListAsync()).Code);
        Assert.Equal("SYN-SEED-LOC-R", Assert.Single(await check.Db.Locations.ToListAsync()).Code);
        Assert.Equal("SYN-SEED-WC-R", Assert.Single(await check.Db.WorkCenters.ToListAsync()).Code);
    }

    [Fact]
    public async Task Request_shapes_outside_the_rules_are_refused_before_anything_is_read()
    {
        var database = await SeedAsync();
        var run = new Run(database, CompanyA, CompanyWide());

        await Assert.ThrowsAsync<ArgumentException>(() => run.Service.CreateSiteAsync(
            new CreateSite("SYN", "X", "factory", "Deneme"), run.Metadata("a"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => run.Service.CreateSiteAsync(
            new CreateSite("SYN", "X", "1", "Deneme"), run.Metadata("b"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => run.Service.CreateWorkCenterAsync(
            new CreateWorkCenter(Hall, "SYN", "X", "Welding", "Deneme"), run.Metadata("c"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => run.Service.CreateLocationAsync(
            new CreateLocation(FactorySite, new string('K', 65), "X", "Deneme"), run.Metadata("d"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => run.Service.CreateLocationAsync(
            new CreateLocation(FactorySite, "SYN", "X", "   "), run.Metadata("e"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => run.Service.UpdateLocationAsync(
            Hall, new UpdateLocation("SYN", "X", 0, "Deneme"), run.Metadata("f"), CancellationToken.None));
        Assert.Empty(await new Run(database, CompanyA, CompanyWide()).Db.CommandExecutions.ToListAsync());
    }

    private static PermissionScope CompanyWide() =>
        new(true, true, new HashSet<string>(["SENTETIK-ORG-EDITOR"], StringComparer.Ordinal),
            new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private static PermissionScope Scoped(Guid[]? sites = null, Guid[]? locations = null, Guid[]? workCenters = null) =>
        new(true, false, new HashSet<string>(["SENTETIK-ORG-SCOPED"], StringComparer.Ordinal),
            new HashSet<Guid>(sites ?? []), new HashSet<Guid>(locations ?? []), new HashSet<Guid>(workCenters ?? []));

    private static async Task<string> SeedAsync()
    {
        var database = $"organization-master-{Guid.NewGuid():N}";
        await using (var a = Context(database, Resolved(CompanyA)))
        {
            a.Sites.Add(new Site(FactorySite, CompanyA, "SYN-SITE-A", "Sentetik fabrika", SiteType.Factory));
            a.Sites.Add(new Site(ExternalSite, CompanyA, "SYN-SITE-EXT", "Sentetik harici", SiteType.ExternalWarehouse));
            a.Locations.Add(new Location(Hall, CompanyA, FactorySite, "SYN-HOL-0", "Sentetik Hol 0", false));
            a.WorkCenters.Add(new WorkCenter(Line, CompanyA, Hall, "SYN-WC-1", "Sentetik Boya Hattı", WorkCenterKind.Paint));
            await a.SaveChangesAsync();
        }

        await using (var b = Context(database, Resolved(CompanyB)))
        {
            b.Sites.Add(new Site(SiteOfB, CompanyB, "SYN-SITE-B", "B tesisi", SiteType.Factory));
            b.Locations.Add(new Location(LocationOfB, CompanyB, SiteOfB, "SYN-HOL-0", "B hol", false));
            await b.SaveChangesAsync();
        }

        return database;
    }

    private static async Task SeedWithAsync(string database, IOptions<OrganizationSeedOptions> options)
    {
        var bootstrap = new OrganizationBootstrapWriteScope();
        await using var context = new OrganizationDbContext(
            new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
            new CompanyContext(),
            bootstrap);
        await new OrganizationDataSeeder(context, bootstrap, options).SeedAsync(CancellationToken.None);
    }

    private static CompanyContext Resolved(Guid companyId)
    {
        var context = new CompanyContext();
        context.Set(companyId);
        return context;
    }

    private static OrganizationDbContext Context(string database, ICompanyContext company) =>
        new(
            new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
            company,
            new OrganizationBootstrapWriteScope());

    private sealed class Run
    {
        public Run(string database, Guid companyId, PermissionScope scope, string actor = Actor)
        {
            Company = Resolved(companyId);
            Db = Context(database, Company);
            var user = new FixedUser(actor, companyId);
            var authority = new FixedAuthority(scope);
            Service = new OrganizationMasterDataService(
                Db, user, Company, authority, Audit, NullLogger<OrganizationMasterDataService>.Instance);
            Reads = new OrganizationService(
                Db, user, Company, authority, Audit, NullLogger<OrganizationService>.Instance);
            ActorId = actor;
        }

        public CompanyContext Company { get; }

        public OrganizationDbContext Db { get; }

        public RecordingAudit Audit { get; } = new();

        public OrganizationMasterDataService Service { get; }

        public OrganizationService Reads { get; }

        public string ActorId { get; }

        public CommandMetadata Metadata(string key, string correlation = "first-correlation") =>
            new(Company.CompanyId, ActorId, correlation, null, SourceChannels.Web, "127.0.0.1", "SENTETIK-agent", key);
    }

    private sealed class FixedUser(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => userId;

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([companyId]);

        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    /// <summary>Every permission the service asks for answers the same synthetic scope.</summary>
    private sealed class FixedAuthority(PermissionScope scope) : IErpAuthorizationService
    {
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(scope.IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);
    }

    private sealed class RecordingAudit : IAuditEventCollector
    {
        public List<AuditEventDraft> Drafts { get; } = [];

        public void Enqueue(AuditEventDraft auditEvent) => Drafts.Add(auditEvent);
    }
}
