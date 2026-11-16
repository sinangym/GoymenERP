using System.Text.Json;
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
/// KIM-D41 + KIM-D48: the staged VKN correction read and apply against the module's own DbContext (EF InMemory; the
/// filtered unique index and the migration guard are SQL Server facts). Every company, VKN, grant and actor here is
/// synthetic; the corrected VKN satisfies the official last-digit check (KIM-D40) and is not a real VKN.
/// </summary>
public sealed class CompanyTaxIdCorrectionServiceTests
{
    private static readonly Guid CompanyA = Guid.Parse("0f000000-0000-4000-8000-0000000041a1");
    private static readonly Guid Placeholder = Guid.Parse("0f000000-0000-4000-8000-0000000041c1");
    private const string VknA = "9000000017";
    private const string NewVkn = "9000000033";
    private const string OtherVkn = "9000000025";
    private const string StandIn = "1111111111";
    private const string Source = "organization.migration.stage-company-tax-id-corrections";
    private const string Approver = "SENTETIK-tax-approver";

    [Fact]
    public async Task An_approver_applies_the_pending_clear_with_one_system_actor_audit_event()
    {
        var database = await SeedAsync();
        var correctionId = await StageAsync(database, Placeholder, StandIn, correctedVkn: null);
        var approver = new Run(database, Placeholder, Approver, canApprove: true);

        var result = await approver.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(1),
            approver.Metadata("apply"),
            CancellationToken.None);

        Assert.Equal(CommandResult.Succeeded, result.Result);
        Assert.False(result.IsReplay);
        var check = new Run(database, Placeholder, Approver, canApprove: true);
        var company = await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == Placeholder);
        Assert.Equal(((string?)null, 2L), (company.Vkn, company.Version));
        var stored = await check.Db.CompanyTaxIdCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(CompanyTaxIdCorrectionStatus.Applied, stored.Status);
        Assert.Equal(Approver, stored.AppliedBy);
        Assert.NotNull(stored.AppliedAtUtc);

        var audit = Assert.Single(approver.Audit.Drafts);
        Assert.Equal(
            ("organization.company.tax-id.correction-applied.v1", "Organization.Company", Placeholder.ToString("D")),
            (audit.EventType, audit.EntityType, audit.EntityId));
        // KIM-D41: the actor is the system source that staged the row; KIM-D48: the approver is in the approval context.
        Assert.Equal((AuditActorTypes.System, Source), (audit.ActorType, audit.ActorId));
        Assert.Equal(OrganizationCompanyTaxIdPermissionsV1.Approve, audit.PermissionCode);
        Assert.Equal(((long?)1, (long?)2), (audit.EntityVersionBefore, audit.EntityVersionAfter));
        Assert.Equal(Placeholder, audit.CompanyId);
        using (var before = JsonDocument.Parse(audit.BeforeJson))
        {
            Assert.Equal(StandIn, before.RootElement.GetProperty("OldVkn").GetString());
            Assert.Equal(JsonValueKind.Null, before.RootElement.GetProperty("NewVkn").ValueKind);
            Assert.Equal(Source, before.RootElement.GetProperty("Source").GetString());
        }

        using (var approval = JsonDocument.Parse(audit.ApprovalContextJson!))
        {
            Assert.Equal(Approver, approval.RootElement.GetProperty("ApprovedBy").GetString());
            Assert.Equal(correctionId, approval.RootElement.GetProperty("CorrectionId").GetGuid());
        }

        var view = await check.Service.GetCompanyTaxIdCorrectionsAsync(CancellationToken.None);
        var item = Assert.Single(view!.Items);
        Assert.Equal(("applied", false), (item.Status, item.IsCurrent));
        Assert.Equal(2L, view.CompanyVersion);
    }

    [Fact]
    public async Task An_approver_applies_a_staged_replacement_vkn()
    {
        var database = await SeedAsync();
        var correctionId = await StageAsync(database, CompanyA, VknA, NewVkn);
        var approver = new Run(database, CompanyA, Approver, canApprove: true);

        var result = await approver.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(1),
            approver.Metadata("apply-replacement"),
            CancellationToken.None);

        Assert.Equal(CommandResult.Succeeded, result.Result);
        var check = new Run(database, CompanyA, Approver, canApprove: true);
        Assert.Equal(NewVkn, (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA)).Vkn);
        Assert.Single(approver.Audit.Drafts);
    }

    [Fact]
    public async Task Without_the_approve_name_the_apply_is_not_found_and_the_read_hides_the_button()
    {
        var database = await SeedAsync();
        var correctionId = await StageAsync(database, Placeholder, StandIn, correctedVkn: null);
        var requester = new Run(database, Placeholder, Approver, canRequest: true);

        var refused = await requester.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(1),
            requester.Metadata("no-grant"),
            CancellationToken.None);

        Assert.Equal(CommandResult.NotFound, refused.Result);
        Assert.Empty(requester.Audit.Drafts);
        var view = await requester.Service.GetCompanyTaxIdCorrectionsAsync(CancellationToken.None);
        Assert.False(view!.CanApply);
        var item = Assert.Single(view.Items);
        Assert.Equal(("pending", true), (item.Status, item.IsCurrent));

        var nobody = new Run(database, Placeholder, Approver);
        Assert.Null(await nobody.Service.GetCompanyTaxIdCorrectionsAsync(CancellationToken.None));
        var check = new Run(database, Placeholder, Approver, canApprove: true);
        Assert.Equal(StandIn, (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == Placeholder)).Vkn);
    }

    [Fact]
    public async Task Another_companys_correction_is_not_found()
    {
        var database = await SeedAsync();
        var correctionId = await StageAsync(database, Placeholder, StandIn, correctedVkn: null);
        var other = new Run(database, CompanyA, Approver, canApprove: true);

        var refused = await other.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(1),
            other.Metadata("cross-company"),
            CancellationToken.None);

        Assert.Equal(CommandResult.NotFound, refused.Result);
        Assert.Empty(other.Audit.Drafts);
        Assert.Empty((await other.Service.GetCompanyTaxIdCorrectionsAsync(CancellationToken.None))!.Items);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_and_changes_nothing()
    {
        var database = await SeedAsync();
        var correctionId = await StageAsync(database, Placeholder, StandIn, correctedVkn: null);
        var approver = new Run(database, Placeholder, Approver, canApprove: true);

        var refused = await approver.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(7),
            approver.Metadata("old-version"),
            CancellationToken.None);

        Assert.Equal(CommandResult.VersionConflict, refused.Result);
        Assert.Equal(409, refused.Response.StatusCode);
        Assert.Empty(approver.Audit.Drafts);
        var check = new Run(database, Placeholder, Approver, canApprove: true);
        Assert.True((await check.Db.CompanyTaxIdCorrections.AsNoTracking().SingleAsync()).IsPending);
    }

    [Fact]
    public async Task A_correction_whose_staged_value_is_no_longer_on_the_company_is_refused_as_stale()
    {
        var database = await SeedAsync();
        // Staged against a value company A does not carry: the VKN moved through another path after staging.
        var correctionId = await StageAsync(database, CompanyA, OtherVkn, NewVkn);
        var approver = new Run(database, CompanyA, Approver, canApprove: true);

        var view = await approver.Service.GetCompanyTaxIdCorrectionsAsync(CancellationToken.None);
        Assert.False(Assert.Single(view!.Items).IsCurrent);

        var refused = await approver.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(1),
            approver.Metadata("stale"),
            CancellationToken.None);

        Assert.Equal(CommandResult.TaxIdCorrectionStale, refused.Result);
        Assert.Equal(409, refused.Response.StatusCode);
        Assert.Empty(approver.Audit.Drafts);
        var check = new Run(database, CompanyA, Approver, canApprove: true);
        Assert.Equal(VknA, (await check.Db.Companies.AsNoTracking().SingleAsync(x => x.Id == CompanyA)).Vkn);
        Assert.True((await check.Db.CompanyTaxIdCorrections.AsNoTracking().SingleAsync()).IsPending);
    }

    [Fact]
    public async Task A_second_apply_with_a_new_key_is_not_pending_and_the_same_key_replays()
    {
        var database = await SeedAsync();
        var correctionId = await StageAsync(database, Placeholder, StandIn, correctedVkn: null);
        var approver = new Run(database, Placeholder, Approver, canApprove: true);
        var first = await approver.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(1),
            approver.Metadata("once"),
            CancellationToken.None);
        Assert.Equal(CommandResult.Succeeded, first.Result);

        var again = new Run(database, Placeholder, Approver, canApprove: true);
        var replay = await again.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(1),
            again.Metadata("once", "second-correlation"),
            CancellationToken.None);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Response, replay.Response);
        Assert.Empty(again.Audit.Drafts);

        var twice = new Run(database, Placeholder, Approver, canApprove: true);
        var refused = await twice.Service.ApplyCompanyTaxIdCorrectionAsync(
            correctionId,
            new ApplyCompanyTaxIdCorrection(2),
            twice.Metadata("twice"),
            CancellationToken.None);
        Assert.Equal(CommandResult.TaxIdCorrectionNotPending, refused.Result);
        Assert.Equal(409, refused.Response.StatusCode);
        Assert.Empty(twice.Audit.Drafts);
    }

    [Fact]
    public async Task The_context_refuses_to_delete_a_correction_or_stage_one_for_another_company()
    {
        var database = await SeedAsync();
        var correctionId = await StageAsync(database, Placeholder, StandIn, correctedVkn: null);

        await using (var placeholder = Context(database, Resolved(Placeholder)))
        {
            var correction = await placeholder.CompanyTaxIdCorrections.SingleAsync(x => x.Id == correctionId);
            placeholder.CompanyTaxIdCorrections.Remove(correction);
            await Assert.ThrowsAsync<InvalidOperationException>(() => placeholder.SaveChangesAsync());
        }

        await using (var a = Context(database, Resolved(CompanyA)))
        {
            a.CompanyTaxIdCorrections.Add(new CompanyTaxIdCorrection(
                Guid.NewGuid(), Placeholder, Source, StandIn, null, 1, "Sentetik gerekçe", DateTimeOffset.UtcNow));
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
        }

        var check = new Run(database, Placeholder, Approver, canApprove: true);
        Assert.Single(await check.Db.CompanyTaxIdCorrections.AsNoTracking().ToListAsync());
    }

    private static async Task<string> SeedAsync()
    {
        var database = $"organization-tax-id-correction-{Guid.NewGuid():N}";
        var bootstrap = new OrganizationBootstrapWriteScope();
        await using var context = new OrganizationDbContext(
            new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase(database).Options,
            new CompanyContext(),
            bootstrap);
        await new OrganizationDataSeeder(context, bootstrap, Options.Create(new OrganizationSeedOptions
        {
            Companies =
            [
                new SeedCompany { Id = CompanyA, Code = "SYN-A", LegalName = "Sentetik A", Vkn = VknA, ApplyTopologyTemplates = true },
                new SeedCompany
                {
                    Id = Placeholder,
                    Code = "SYN-P",
                    LegalName = "Sentetik Yer Tutucu",
                    Vkn = StandIn,
                    IsPlaceholder = true,
                    InvoiceCapable = false,
                },
            ],
            SiteTemplates = [new SeedSiteTemplate { Code = "SYN-TAX-SITE", Name = "Sentetik VKN tesisi", Type = "Factory" }],
        })).SeedAsync(CancellationToken.None);
        return database;
    }

    /// <summary>Stages one pending correction the way the data-fix migration does, inside the company's own context.</summary>
    private static async Task<Guid> StageAsync(string database, Guid companyId, string stagedVkn, string? correctedVkn)
    {
        var id = Guid.NewGuid();
        await using var context = Context(database, Resolved(companyId));
        context.CompanyTaxIdCorrections.Add(new CompanyTaxIdCorrection(
            id, companyId, Source, stagedVkn, correctedVkn, 1, "Yer tutucu VKN işaretlendi", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return id;
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

    private sealed class Run
    {
        public Run(string database, Guid companyId, string actor, bool canRequest = false, bool canApprove = false)
        {
            Company = Resolved(companyId);
            Db = Context(database, Company);
            var scopes = new Dictionary<string, PermissionScope>(StringComparer.Ordinal)
            {
                [OrganizationCompanyTaxIdPermissionsV1.Request] = canRequest ? CompanyWide() : Denied(),
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
            throw new NotSupportedException("The VKN correction flow never asks for location operator eligibility.");
    }

    private sealed class RecordingAudit : IAuditEventCollector
    {
        public List<AuditEventDraft> Drafts { get; } = [];

        public void Enqueue(AuditEventDraft auditEvent) => Drafts.Add(auditEvent);
    }
}
