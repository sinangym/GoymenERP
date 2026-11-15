using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Companies;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-S23 (KIM-D28, DEC-ORG-005): the company VKN change commands against the module's own DbContext (EF InMemory;
/// the filtered unique indexes and the migration guards are SQL Server facts). Every company, VKN, grant and actor here
/// is synthetic.
/// </summary>
public sealed class CompanyTaxIdServiceTests
{
    private static readonly Guid CompanyA = Guid.Parse("0f000000-0000-4000-8000-0000000023a1");
    private static readonly Guid CompanyB = Guid.Parse("0f000000-0000-4000-8000-0000000023b1");
    private const string VknA = "9000000001";
    private const string VknB = "9000000002";
    private const string NewVkn = "9000000003";
    private const string Maker = "SENTETIK-tax-maker";
    private const string Checker = "SENTETIK-tax-checker";

    [Fact]
    public async Task A_request_opens_one_pending_change_and_leaves_the_company_vkn_untouched()
    {
        var database = await SeedAsync();
        var maker = new Run(database, CompanyA, Maker, canRequest: true);

        var result = await maker.Service.RequestCompanyTaxIdChangeAsync(
            new RequestCompanyTaxIdChange(1, $" {NewVkn} ", " Vergi dairesi yazısı "),
            maker.Metadata("tax-request"),
            CancellationToken.None);

        Assert.Equal(CommandResult.Created, result.Result);
        Assert.Equal(201, result.Response.StatusCode);
        var created = Assert.IsType<OrganizationCreatedRecord>(result.Response.Created);
        Assert.Equal(1, created.Version);

        var check = new Run(database, CompanyA, Maker, canRequest: true);
        var company = await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA);
        Assert.Equal((VknA, 1L), (company.Vkn, company.Version));
        var stored = Assert.Single(await check.Db.CompanyTaxIdChangeRequests.AsNoTracking().ToListAsync());
        Assert.Equal(created.Id, stored.Id);
        Assert.Equal((VknA, NewVkn, Maker), (stored.CurrentVkn, stored.ProposedVkn, stored.RequestedBy));
        Assert.Equal("Vergi dairesi yazısı", stored.Reason);
        Assert.Equal(CompanyTaxIdChangeStatus.Pending, stored.Status);

        var audit = Assert.Single(maker.Audit.Drafts);
        Assert.Equal(
            ("organization.company.tax-id.change-requested.v1", "Organization.CompanyTaxIdChangeRequest", "Create"),
            (audit.EventType, audit.EntityType, audit.Operation));
        Assert.Equal(created.Id.ToString("D"), audit.EntityId);
        Assert.Equal(OrganizationCompanyTaxIdPermissionsV1.Request, audit.PermissionCode);
        Assert.Equal(CompanyA, audit.CompanyId);

        var view = await check.Service.GetCompanyTaxIdAsync(CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal((VknA, 1L), (view.Vkn, view.Version));
        Assert.Equal("pending", view.Pending!.Status);
        Assert.Null(view.LastDecided);
        Assert.Equal(new CompanyTaxIdViewer(true, false, true), view.Viewer);
    }

    [Fact]
    public async Task The_preparer_can_neither_approve_nor_reject_their_own_request_even_holding_both_names()
    {
        var database = await SeedAsync();
        var requestId = await OpenRequestAsync(database);
        var both = new Run(database, CompanyA, Maker, canRequest: true, canApprove: true);

        foreach (var decision in new[] { OrganizationCompanyTaxIdRules.ApproveDecision, OrganizationCompanyTaxIdRules.RejectDecision })
        {
            var refused = await both.Service.DecideCompanyTaxIdChangeAsync(
                requestId,
                new DecideCompanyTaxIdChange(1, decision, null),
                both.Metadata($"self-{decision}"),
                CancellationToken.None);

            Assert.Equal(CommandResult.TaxIdSelfDecision, refused.Result);
            Assert.Equal(409, refused.Response.StatusCode);
        }

        Assert.Empty(both.Audit.Drafts);
        var check = new Run(database, CompanyA, Checker, canApprove: true);
        Assert.Equal(VknA, (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA)).Vkn);
        Assert.True((await check.Db.CompanyTaxIdChangeRequests.AsNoTracking().SingleAsync()).IsPending);
        var view = await both.Service.GetCompanyTaxIdAsync(CancellationToken.None);
        Assert.True(view!.Viewer.IsRequesterOfPending);
    }

    [Fact]
    public async Task Another_person_approves_and_the_vkn_moves_with_one_audit_event_on_the_company()
    {
        var database = await SeedAsync();
        var requestId = await OpenRequestAsync(database);
        var checker = new Run(database, CompanyA, Checker, canApprove: true);

        var result = await checker.Service.DecideCompanyTaxIdChangeAsync(
            requestId,
            new DecideCompanyTaxIdChange(1, OrganizationCompanyTaxIdRules.ApproveDecision, " Belge görüldü "),
            checker.Metadata("approve"),
            CancellationToken.None);

        Assert.Equal(CommandResult.Succeeded, result.Result);
        var check = new Run(database, CompanyA, Checker, canApprove: true);
        var company = await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA);
        Assert.Equal((NewVkn, 2L), (company.Vkn, company.Version));
        var stored = await check.Db.CompanyTaxIdChangeRequests.AsNoTracking().SingleAsync();
        Assert.Equal(CompanyTaxIdChangeStatus.Approved, stored.Status);
        Assert.Equal((Checker, "Belge görüldü"), (stored.DecidedBy, stored.DecisionNote));

        var audit = Assert.Single(checker.Audit.Drafts);
        Assert.Equal(
            ("organization.company.tax-id.change-approved.v1", "Organization.Company", CompanyA.ToString("D"), "Update"),
            (audit.EventType, audit.EntityType, audit.EntityId, audit.Operation));
        Assert.Equal(((long?)1, (long?)2), (audit.EntityVersionBefore, audit.EntityVersionAfter));
        Assert.Equal(OrganizationCompanyTaxIdPermissionsV1.Approve, audit.PermissionCode);
        Assert.Equal(Checker, audit.ActorId);

        var view = await check.Service.GetCompanyTaxIdAsync(CancellationToken.None);
        Assert.Null(view!.Pending);
        Assert.Equal("approved", view.LastDecided!.Status);
        Assert.Equal(new CompanyTaxIdViewer(false, true, false), view.Viewer);
    }

    [Fact]
    public async Task A_rejection_closes_the_request_and_leaves_the_vkn()
    {
        var database = await SeedAsync();
        var requestId = await OpenRequestAsync(database);
        var checker = new Run(database, CompanyA, Checker, canApprove: true);

        var result = await checker.Service.DecideCompanyTaxIdChangeAsync(
            requestId,
            new DecideCompanyTaxIdChange(1, OrganizationCompanyTaxIdRules.RejectDecision, null),
            checker.Metadata("reject"),
            CancellationToken.None);

        Assert.Equal(CommandResult.Succeeded, result.Result);
        var check = new Run(database, CompanyA, Checker, canApprove: true);
        var company = await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA);
        Assert.Equal((VknA, 1L), (company.Vkn, company.Version));
        Assert.Equal(
            CompanyTaxIdChangeStatus.Rejected,
            (await check.Db.CompanyTaxIdChangeRequests.AsNoTracking().SingleAsync()).Status);
        var audit = Assert.Single(checker.Audit.Drafts);
        Assert.Equal(
            ("organization.company.tax-id.change-rejected.v1", "Organization.CompanyTaxIdChangeRequest", "Reject"),
            (audit.EventType, audit.EntityType, audit.Operation));

        // A decided request cannot be decided again.
        var again = await check.Service.DecideCompanyTaxIdChangeAsync(
            requestId,
            new DecideCompanyTaxIdChange(1, OrganizationCompanyTaxIdRules.ApproveDecision, null),
            check.Metadata("approve-after-reject"),
            CancellationToken.None);
        Assert.Equal(CommandResult.TaxIdChangeNotPending, again.Result);
        Assert.Equal(409, again.Response.StatusCode);
    }

    [Fact]
    public async Task A_vkn_of_another_company_is_refused_and_no_request_is_opened()
    {
        var database = await SeedAsync();
        var maker = new Run(database, CompanyA, Maker, canRequest: true);

        var result = await maker.Service.RequestCompanyTaxIdChangeAsync(
            new RequestCompanyTaxIdChange(1, VknB, "Başka şirketin VKN'si"),
            maker.Metadata("in-use"),
            CancellationToken.None);

        Assert.Equal(CommandResult.TaxIdInUse, result.Result);
        Assert.Equal(422, result.Response.StatusCode);
        Assert.Empty(await new Run(database, CompanyA, Maker, canRequest: true).Db.CompanyTaxIdChangeRequests.ToListAsync());
        Assert.Empty(maker.Audit.Drafts);
    }

    [Fact]
    public async Task A_second_request_while_one_is_pending_the_current_vkn_and_a_stale_version_are_refused()
    {
        var database = await SeedAsync();
        var maker = new Run(database, CompanyA, Maker, canRequest: true);

        var unchanged = await maker.Service.RequestCompanyTaxIdChangeAsync(
            new RequestCompanyTaxIdChange(1, VknA, "Aynı VKN"), maker.Metadata("unchanged"), CancellationToken.None);
        Assert.Equal(CommandResult.TaxIdUnchanged, unchanged.Result);
        Assert.Equal(409, unchanged.Response.StatusCode);

        var stale = await maker.Service.RequestCompanyTaxIdChangeAsync(
            new RequestCompanyTaxIdChange(7, NewVkn, "Eski sürüm"), maker.Metadata("stale-version"), CancellationToken.None);
        Assert.Equal(CommandResult.VersionConflict, stale.Result);
        Assert.Equal((7L, 1L), (stale.Response.Conflict!.ExpectedVersion, stale.Response.Conflict.CurrentVersion));

        await OpenRequestAsync(database);
        var second = new Run(database, CompanyA, "SENTETIK-tax-second", canRequest: true);
        var pending = await second.Service.RequestCompanyTaxIdChangeAsync(
            new RequestCompanyTaxIdChange(1, "9000000004", "İkinci talep"), second.Metadata("second"), CancellationToken.None);
        Assert.Equal(CommandResult.TaxIdChangePending, pending.Result);
        Assert.Equal(409, pending.Response.StatusCode);

        Assert.Single(await second.Db.CompanyTaxIdChangeRequests.AsNoTracking().ToListAsync());
        Assert.Empty(second.Audit.Drafts);
    }

    [Fact]
    public async Task A_replayed_request_returns_the_stored_answer_and_opens_nothing_again()
    {
        var database = await SeedAsync();
        var maker = new Run(database, CompanyA, Maker, canRequest: true);
        var command = new RequestCompanyTaxIdChange(1, NewVkn, "Vergi dairesi yazısı");

        var first = await maker.Service.RequestCompanyTaxIdChangeAsync(
            command, maker.Metadata("replay-key"), CancellationToken.None);
        var again = new Run(database, CompanyA, Maker, canRequest: true);
        var replay = await again.Service.RequestCompanyTaxIdChangeAsync(
            command, again.Metadata("replay-key", correlation: "second-correlation"), CancellationToken.None);

        Assert.True(replay.IsReplay);
        Assert.Equal(first.Response, replay.Response);
        Assert.Equal("first-correlation", replay.OriginalCorrelationId);
        Assert.Single(await again.Db.CompanyTaxIdChangeRequests.AsNoTracking().ToListAsync());
        Assert.Empty(again.Audit.Drafts);

        // The same key with another payload is an idempotency conflict, never a second request.
        var reused = await again.Service.RequestCompanyTaxIdChangeAsync(
            command with { ProposedVkn = "9000000004" }, again.Metadata("replay-key"), CancellationToken.None);
        Assert.Equal(CommandResult.IdempotencyConflict, reused.Result);
    }

    [Fact]
    public async Task An_approval_over_a_company_change_the_request_did_not_see_is_stale()
    {
        var database = await SeedAsync();
        var requestId = await OpenRequestAsync(database);
        // The company moved after the request was prepared (the version the decider sees is the new one).
        await using (var a = Context(database, Resolved(CompanyA)))
        {
            var company = await a.Companies.SingleAsync(x => x.Id == CompanyA);
            company.ApplyApprovedVkn("9000000005", DateTimeOffset.UtcNow);
            await a.SaveChangesAsync();
        }

        var checker = new Run(database, CompanyA, Checker, canApprove: true);
        var approve = await checker.Service.DecideCompanyTaxIdChangeAsync(
            requestId,
            new DecideCompanyTaxIdChange(2, OrganizationCompanyTaxIdRules.ApproveDecision, null),
            checker.Metadata("stale-approve"),
            CancellationToken.None);

        Assert.Equal(CommandResult.TaxIdChangeStale, approve.Result);
        Assert.Equal(409, approve.Response.StatusCode);
        var check = new Run(database, CompanyA, Checker, canApprove: true);
        Assert.Equal("9000000005", (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA)).Vkn);

        // It can still be rejected.
        var reject = await check.Service.DecideCompanyTaxIdChangeAsync(
            requestId,
            new DecideCompanyTaxIdChange(2, OrganizationCompanyTaxIdRules.RejectDecision, "Güncelliğini yitirdi"),
            check.Metadata("stale-reject"),
            CancellationToken.None);
        Assert.Equal(CommandResult.Succeeded, reject.Result);
    }

    [Fact]
    public async Task An_approval_is_refused_when_another_company_took_the_vkn_meanwhile()
    {
        var database = await SeedAsync();
        var requestId = await OpenRequestAsync(database);
        await using (var b = Context(database, Resolved(CompanyB)))
        {
            var companyB = await b.Companies.SingleAsync(x => x.Id == CompanyB);
            companyB.ApplyApprovedVkn(NewVkn, DateTimeOffset.UtcNow);
            await b.SaveChangesAsync();
        }

        var checker = new Run(database, CompanyA, Checker, canApprove: true);
        var result = await checker.Service.DecideCompanyTaxIdChangeAsync(
            requestId,
            new DecideCompanyTaxIdChange(1, OrganizationCompanyTaxIdRules.ApproveDecision, null),
            checker.Metadata("approve-in-use"),
            CancellationToken.None);

        Assert.Equal(CommandResult.TaxIdInUse, result.Result);
        Assert.Equal(422, result.Response.StatusCode);
        var check = new Run(database, CompanyA, Checker, canApprove: true);
        Assert.Equal(VknA, (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA)).Vkn);
        Assert.True((await check.Db.CompanyTaxIdChangeRequests.AsNoTracking().SingleAsync()).IsPending);
        Assert.Empty(checker.Audit.Drafts);
    }

    [Fact]
    public async Task Without_a_company_wide_grant_nothing_is_read_or_written()
    {
        var database = await SeedAsync();
        var none = new Run(database, CompanyA, Maker);

        Assert.Null(await none.Service.GetCompanyTaxIdAsync(CancellationToken.None));
        var request = await none.Service.RequestCompanyTaxIdChangeAsync(
            new RequestCompanyTaxIdChange(1, NewVkn, "Yetkisiz"), none.Metadata("no-grant"), CancellationToken.None);
        Assert.Equal(CommandResult.NotFound, request.Result);

        // A site-scoped grant is not company-wide: the request name stays closed.
        var scoped = new Run(database, CompanyA, Maker, scopedRequest: true);
        Assert.Null(await scoped.Service.GetCompanyTaxIdAsync(CancellationToken.None));
        var scopedResult = await scoped.Service.RequestCompanyTaxIdChangeAsync(
            new RequestCompanyTaxIdChange(1, NewVkn, "Tesis kapsamlı"), scoped.Metadata("scoped"), CancellationToken.None);
        Assert.Equal(CommandResult.NotFound, scopedResult.Result);

        var requestId = await OpenRequestAsync(database);
        // The request name alone does not decide.
        var requester = new Run(database, CompanyA, Checker, canRequest: true);
        var decide = await requester.Service.DecideCompanyTaxIdChangeAsync(
            requestId,
            new DecideCompanyTaxIdChange(1, OrganizationCompanyTaxIdRules.ApproveDecision, null),
            requester.Metadata("request-name-decides"),
            CancellationToken.None);
        Assert.Equal(CommandResult.NotFound, decide.Result);

        var check = new Run(database, CompanyA, Checker, canApprove: true);
        Assert.Equal(VknA, (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA)).Vkn);
        Assert.Single(await check.Db.CompanyTaxIdChangeRequests.AsNoTracking().ToListAsync());
        Assert.Empty(none.Audit.Drafts);
        Assert.Empty(scoped.Audit.Drafts);
        Assert.Empty(requester.Audit.Drafts);
    }

    [Fact]
    public async Task A_request_of_another_company_is_not_found_and_not_shown()
    {
        var database = await SeedAsync();
        var requestOfA = await OpenRequestAsync(database);
        var checkerOfB = new Run(database, CompanyB, Checker, canApprove: true);

        var result = await checkerOfB.Service.DecideCompanyTaxIdChangeAsync(
            requestOfA,
            new DecideCompanyTaxIdChange(1, OrganizationCompanyTaxIdRules.ApproveDecision, null),
            checkerOfB.Metadata("cross-company"),
            CancellationToken.None);

        Assert.Equal(CommandResult.NotFound, result.Result);
        var viewOfB = await checkerOfB.Service.GetCompanyTaxIdAsync(CancellationToken.None);
        Assert.Equal((CompanyB, VknB), (viewOfB!.CompanyId, viewOfB.Vkn));
        Assert.Null(viewOfB.Pending);
        var check = new Run(database, CompanyA, Checker, canApprove: true);
        Assert.True((await check.Db.CompanyTaxIdChangeRequests.AsNoTracking().SingleAsync()).IsPending);
        Assert.Equal(VknA, (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA)).Vkn);
    }

    [Fact]
    public async Task The_context_refuses_to_change_another_company_or_delete_a_request()
    {
        var database = await SeedAsync();
        var requestId = await OpenRequestAsync(database);

        await using (var a = Context(database, Resolved(CompanyA)))
        {
            var companyB = await a.Companies.SingleAsync(x => x.Id == CompanyB);
            companyB.ApplyApprovedVkn(NewVkn, DateTimeOffset.UtcNow);
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
        }

        await using (var a = Context(database, Resolved(CompanyA)))
        {
            var request = await a.CompanyTaxIdChangeRequests.SingleAsync(x => x.Id == requestId);
            a.CompanyTaxIdChangeRequests.Remove(request);
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
        }

        var check = new Run(database, CompanyA, Checker, canApprove: true);
        Assert.Equal(VknB, (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyB)).Vkn);
        Assert.Single(await check.Db.CompanyTaxIdChangeRequests.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task The_seed_refuses_two_companies_with_the_same_vkn()
    {
        var database = $"organization-tax-id-seed-{Guid.NewGuid():N}";
        var options = Options.Create(new OrganizationSeedOptions
        {
            Companies =
            [
                new SeedCompany { Id = CompanyA, Code = "SYN-A", LegalName = "Sentetik A", Vkn = VknA },
                new SeedCompany { Id = CompanyB, Code = "SYN-B", LegalName = "Sentetik B", Vkn = $" {VknA} " },
            ],
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedWithAsync(database, options));
        Assert.Contains("VKN", error.Message, StringComparison.Ordinal);
    }

    private static async Task<string> SeedAsync()
    {
        var database = $"organization-tax-id-{Guid.NewGuid():N}";
        await SeedWithAsync(database, Options.Create(new OrganizationSeedOptions
        {
            Companies =
            [
                new SeedCompany { Id = CompanyA, Code = "SYN-A", LegalName = "Sentetik A", Vkn = VknA },
                new SeedCompany { Id = CompanyB, Code = "SYN-B", LegalName = "Sentetik B", Vkn = VknB },
            ],
        }));
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

    /// <summary>The maker opens a request for <see cref="NewVkn"/> against company A version 1.</summary>
    private static async Task<Guid> OpenRequestAsync(string database)
    {
        var maker = new Run(database, CompanyA, Maker, canRequest: true);
        var result = await maker.Service.RequestCompanyTaxIdChangeAsync(
            new RequestCompanyTaxIdChange(1, NewVkn, "Vergi dairesi yazısı"),
            maker.Metadata($"open-{Guid.NewGuid():N}"),
            CancellationToken.None);
        Assert.Equal(CommandResult.Created, result.Result);
        return result.Response.Created!.Id;
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

    private static PermissionScope Denied() =>
        new(false, false, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private static PermissionScope CompanyWide() =>
        new(true, true, new HashSet<string>(["SENTETIK-TAX"], StringComparer.Ordinal),
            new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private static PermissionScope SiteScoped() =>
        new(true, false, new HashSet<string>(["SENTETIK-TAX-SITE"], StringComparer.Ordinal),
            new HashSet<Guid>([Guid.Parse("0f200000-0000-4000-8000-0000000023a1")]), new HashSet<Guid>(), new HashSet<Guid>());

    private sealed class Run
    {
        public Run(
            string database,
            Guid companyId,
            string actor,
            bool canRequest = false,
            bool canApprove = false,
            bool scopedRequest = false)
        {
            Company = Resolved(companyId);
            Db = Context(database, Company);
            var scopes = new Dictionary<string, PermissionScope>(StringComparer.Ordinal)
            {
                [OrganizationCompanyTaxIdPermissionsV1.Request] =
                    scopedRequest ? SiteScoped() : canRequest ? CompanyWide() : Denied(),
                [OrganizationCompanyTaxIdPermissionsV1.Approve] = canApprove ? CompanyWide() : Denied(),
            };
            var authority = new PerPermissionAuthority(scopes);
            Service = new OrganizationMasterDataService(
                Db,
                new FixedUser(actor, companyId),
                Company,
                authority,
                authority,
                new FixedEligibility(),
                Audit,
                NullLogger<OrganizationMasterDataService>.Instance);
            ActorId = actor;
        }

        public CompanyContext Company { get; }

        public OrganizationDbContext Db { get; }

        public RecordingAudit Audit { get; } = new();

        public OrganizationMasterDataService Service { get; }

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

    /// <summary>Each permission name answers its own synthetic scope; an unknown name is denied.</summary>
    private sealed class PerPermissionAuthority(IReadOnlyDictionary<string, PermissionScope> scopes)
        : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission).IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission));

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope(permission));

        private PermissionScope Scope(string permission) =>
            scopes.TryGetValue(permission, out var scope) ? scope : Denied();
    }

    private sealed class FixedEligibility : IOrganizationLocationOperatorEligibilityPortV1
    {
        public Task<OrganizationLocationOperatorEligibilityV1> CheckAsync(
            Guid companyId,
            string actorId,
            Guid partyId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The VKN flow never asks for location operator eligibility.");
    }

    private sealed class RecordingAudit : IAuditEventCollector
    {
        public List<AuditEventDraft> Drafts { get; } = [];

        public void Enqueue(AuditEventDraft auditEvent) => Drafts.Add(auditEvent);
    }
}
