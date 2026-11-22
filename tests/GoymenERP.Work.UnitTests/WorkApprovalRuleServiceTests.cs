using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalRules;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Work.UnitTests;

/// <summary>WP1 versioned approval rule table. SENTETIK data only; in-memory Work store, authority and sources are doubles.</summary>
public sealed class WorkApprovalRuleServiceTests
{
    private static readonly Guid Company = Guid.Parse("c1000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("c1000000-0000-4000-8000-000000000002");
    private static readonly Guid Manager = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private const string Manage = ErpPermissions.SettingsWorkApprovalDurationManage;
    private const string PlanKind = "manufacturing.plan.v1";
    private const string PaymentKind = "finance.manual-bank-payment.v1";

    [Fact]
    public void The_YOL_D18_default_puts_exactly_the_four_decided_kinds_in_the_hour_class()
    {
        Assert.Equal(
            ["logistics.dispatch-note.v1", "logistics.plan-excess.v1", "logistics.weighing-difference.v1", PlanKind],
            WorkApprovalRuleDefaultsV1.HourScaleKinds.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(WorkApprovalDurationClassV1.Hours, WorkApprovalRuleDefaultsV1.DurationClassOf(PlanKind));
        foreach (var kind in new[] { PaymentKind, "sales.order-draft.v1", "inventory.stock-count.v1", "purchasing.order.v1" })
        {
            Assert.Equal(WorkApprovalDurationClassV1.BusinessDays, WorkApprovalRuleDefaultsV1.DurationClassOf(kind));
        }
    }

    [Fact]
    public async Task The_table_lists_one_row_per_registered_kind_with_its_owner_module_approver_permission_and_the_default()
    {
        var harness = new Harness();

        var table = await harness.Service().ListAsync();

        Assert.Equal(Company, table.CompanyId);
        Assert.Equal([PaymentKind, PlanKind], table.Rules.Select(rule => rule.Kind).ToArray());
        var plan = table.Rules[1];
        Assert.Equal(("manufacturing", "manufacturing.plans.approve", "Hours", (string?)null, "0", true, "YOL-D18",
                WorkApprovalRuleStatesV1.Current, "work.approval-rule:manufacturing.plan.v1:0"),
            (plan.OwnerModule, plan.ApproverPermission, plan.DurationClass, plan.EscalationPermission, plan.Version, plan.IsDefault,
                plan.DefaultBasis, plan.State, plan.VersionText));
        Assert.Empty(plan.Versions);
        Assert.Equal("BusinessDays", table.Rules[0].DurationClass);
    }

    [Fact]
    public async Task A_change_with_the_expected_version_appends_the_next_version_audits_it_and_the_same_key_replays_it()
    {
        var harness = new Harness();
        var request = new RecordWorkApprovalRuleRequestV1("0", "Hours", ErpPermissions.SettingsWorkApprovalDurationManage,
            "SENTETIK: banka ödemesi saat ölçeğinde");

        var first = await harness.Service().RecordAsync(PaymentKind, request, Meta("k1"));
        var replay = await harness.Service().RecordAsync(PaymentKind, request, Meta("k1", "corr-2"));

        Assert.Equal((false, "corr-k1", "1", "Hours", false), (first.Replayed, first.CorrelationId, first.Rule.Version,
            first.Rule.DurationClass, first.Rule.IsDefault));
        Assert.Equal((Manager.ToString("D"), Now, ErpPermissions.SettingsWorkApprovalDurationManage),
            (first.Rule.RecordedBy, first.Rule.RecordedAtUtc!.Value, first.Rule.EscalationPermission));
        Assert.Equal((true, "corr-k1", "1"), (replay.Replayed, replay.CorrelationId, replay.Rule.Version));
        await using var database = harness.Database();
        var row = Assert.Single(await database.ApprovalRuleVersions.ToListAsync());
        Assert.Equal((Company, PaymentKind, 1L, 1, "finance.manual-bank-payment.approve", "corr-k1"),
            (row.CompanyId, row.Kind, row.Version, row.DurationClass, row.ApproverPermission, row.CorrelationId));
        var audit = Assert.Single(harness.Audit.Events);
        Assert.Equal(("work.approval-rule.recorded.v1", 0L, 1L, Manage, "k1"),
            (audit.EventType, audit.EntityVersionBefore!.Value, audit.EntityVersionAfter!.Value, audit.PermissionCode, audit.IdempotencyKey));

        // The inbox reader applies the recorded version from now on.
        var resolved = await new WorkApprovalRuleReaderV1(harness.Database()).ResolveAsync(Company, harness.Sources, default);
        Assert.Equal((WorkApprovalDurationClassV1.Hours, 1L, false, true),
            (resolved![PaymentKind].DurationClass, resolved[PaymentKind].Version, resolved[PaymentKind].IsDefault,
                resolved[PaymentKind].ApproverPermissionMatches));
        Assert.Equal((WorkApprovalDurationClassV1.Hours, 0L, true), (resolved[PlanKind].DurationClass, resolved[PlanKind].Version,
            resolved[PlanKind].IsDefault));
    }

    [Fact]
    public async Task A_stale_expected_version_is_a_recorded_refusal_its_key_replays_it_and_an_unchanged_rule_is_refused()
    {
        var harness = new Harness();
        await harness.Service().RecordAsync(PaymentKind, new("0", "Hours", null, "SENTETIK ilk"), Meta("k1"));

        var stale = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service()
            .RecordAsync(PaymentKind, new("0", "BusinessDays", null, "SENTETIK eski sürüm"), Meta("k2")));
        var staleReplay = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service()
            .RecordAsync(PaymentKind, new("0", "BusinessDays", null, "SENTETIK eski sürüm"), Meta("k2", "corr-3")));
        var unchanged = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service()
            .RecordAsync(PaymentKind, new("1", "Hours", null, "SENTETIK aynı"), Meta("k3")));
        var mismatch = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service()
            .RecordAsync(PaymentKind, new("1", "BusinessDays", null, "SENTETIK başka gövde"), Meta("k1")));

        Assert.Equal((409, WorkApprovalRuleProblemCodesV1.VersionConflict, false), (stale.Status, stale.Code, stale.IsReplay));
        Assert.Equal((409, WorkApprovalRuleProblemCodesV1.VersionConflict, true, "corr-k2"),
            (staleReplay.Status, staleReplay.Code, staleReplay.IsReplay, staleReplay.OriginalCorrelationId));
        Assert.Equal((409, WorkApprovalRuleProblemCodesV1.Unchanged), (unchanged.Status, unchanged.Code));
        Assert.Equal((409, WorkApprovalRuleProblemCodesV1.SameKeyPayloadMismatch), (mismatch.Status, mismatch.Code));
        await using var database = harness.Database();
        Assert.Single(await database.ApprovalRuleVersions.ToListAsync());
    }

    [Fact]
    public async Task The_table_needs_the_company_wide_manage_permission_of_the_resolved_company()
    {
        var harness = new Harness();

        var none = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service(granted: false).ListAsync());
        var scoped = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service(companyWide: false)
            .RecordAsync(PaymentKind, new("0", "Hours", null, "SENTETIK"), Meta("k1")));
        var foreign = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service(allowedCompany: OtherCompany).ListAsync());
        var anonymous = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service(null, true, true, null, anonymous: true).ListAsync());

        Assert.Equal([403, 403, 403, 401], new[] { none.Status, scoped.Status, foreign.Status, anonymous.Status });
        await using var database = harness.Database();
        Assert.Empty(await database.ApprovalRuleVersions.ToListAsync());
        Assert.Empty(await database.CollaborationAttempts.ToListAsync());
    }

    [Theory]
    [InlineData(PaymentKind, "-1", "Hours", null, "SENTETIK", 400)]
    [InlineData(PaymentKind, "0", "Weeks", null, "SENTETIK", 422)]
    [InlineData(PaymentKind, "0", "Hours", "unknown.permission.name", "SENTETIK", 422)]
    [InlineData(PaymentKind, "0", "Hours", null, " ", 422)]
    [InlineData("sales.unknown.v1", "0", "Hours", null, "SENTETIK", 404)]
    public async Task An_invalid_change_is_refused_before_anything_is_written(string kind, string version, string durationClass,
        string? escalation, string reason, int status)
    {
        var harness = new Harness();
        var request = new RecordWorkApprovalRuleRequestV1(version, durationClass, escalation, reason);

        var error = await Assert.ThrowsAsync<WorkApprovalRuleException>(() => harness.Service().RecordAsync(kind, request, Meta("k1")));

        Assert.Equal(status, error.Status);
        await using var database = harness.Database();
        Assert.Empty(await database.ApprovalRuleVersions.ToListAsync());
        Assert.Empty(harness.Audit.Events);
    }

    [Fact]
    public async Task Another_companys_versions_are_never_read_and_an_owner_permission_change_marks_the_rule_until_re_recorded()
    {
        var harness = new Harness();
        await using (var foreign = harness.Database(OtherCompany))
        {
            foreign.ApprovalRuleVersions.Add(Row(OtherCompany, PlanKind, 1, durationClass: 2, "manufacturing.plans.approve"));
            await foreign.SaveChangesAsync();
        }
        await using (var seed = harness.Database())
        {
            seed.ApprovalRuleVersions.Add(Row(Company, PaymentKind, 1, durationClass: 2, "finance.old-approve"));
            await seed.SaveChangesAsync();
        }

        var table = await harness.Service().ListAsync();
        var resolved = await new WorkApprovalRuleReaderV1(harness.Database()).ResolveAsync(Company, harness.Sources, default);

        Assert.Equal(("Hours", true), (table.Rules[1].DurationClass, table.Rules[1].IsDefault));
        Assert.Equal((WorkApprovalDurationClassV1.Hours, true), (resolved![PlanKind].DurationClass, resolved[PlanKind].IsDefault));
        Assert.Equal(WorkApprovalRuleStatesV1.ApproverPermissionChanged, table.Rules[0].State);
        Assert.False(resolved[PaymentKind].ApproverPermissionMatches);

        // Re-recording the same class against the owner's current permission is a change, not "unchanged".
        var fixedRule = await harness.Service().RecordAsync(PaymentKind, new("1", "BusinessDays", null, "SENTETIK izin adı güncel"),
            Meta("k1"));
        Assert.Equal(("2", WorkApprovalRuleStatesV1.Current, "finance.manual-bank-payment.approve"),
            (fixedRule.Rule.Version, fixedRule.Rule.State, fixedRule.Rule.Versions[0].ApproverPermission));
    }

    [Fact]
    public void The_rule_version_migration_is_DDL_only_and_Down_first_refuses_while_versions_exist()
    {
        var migration = new AddWorkApprovalRuleVersions();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal("ApprovalRuleVersions", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 52100", guard, StringComparison.Ordinal);
        Assert.Contains("[work].[ApprovalRuleVersions]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
    }

    [Fact]
    public void The_rule_version_model_is_tenant_filtered_append_only_and_one_version_number_per_kind()
    {
        var company = new CompanyContext();
        company.Set(Company);
        using var database = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>()
            .UseSqlServer("Server=invalid;Database=NOT_ENV;").Options, company);
        var entity = Assert.IsAssignableFrom<IEntityType>(
            database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(WorkApprovalRuleVersionRow)));
        Assert.Single(entity.GetDeclaredQueryFilters());
        Assert.Equal(["CompanyId", "Id"], entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetFilter() is null
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "Kind", "Version"]));
        Assert.Equal(WorkDbContext.ApprovalRuleVersionCheck, Assert.Single(entity.GetCheckConstraints()).Sql);
        Assert.True(entity.FindProperty(nameof(WorkApprovalRuleVersionRow.EscalationPermission))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(WorkApprovalRuleVersionRow.ApproverPermission))!.IsNullable);
    }

    [Fact]
    public async Task A_stored_rule_version_cannot_be_modified_or_deleted()
    {
        var harness = new Harness();
        await using var database = harness.Database();
        var row = Row(Company, PaymentKind, 1, durationClass: 1, "finance.manual-bank-payment.approve");
        database.ApprovalRuleVersions.Add(row);
        await database.SaveChangesAsync();

        row.DurationClass = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    private static WorkApprovalRuleVersionRow Row(Guid company, string kind, long version, int durationClass, string approver) => new()
    {
        CompanyId = company, Id = Guid.NewGuid(), Kind = kind, Version = version, DurationClass = durationClass,
        ApproverPermission = approver, RecordedByUserId = Manager, RecordedAtUtc = Now, Reason = "SENTETIK", CorrelationId = "seed",
    };

    private static WorkCommandMetadataV1 Meta(string key, string? correlation = null) => new(key, correlation ?? "corr-" + key, null);

    private sealed class Harness
    {
        private readonly string name = Guid.NewGuid().ToString("N");
        public WorkCollaborationServiceTests.CollectingAudit Audit { get; } = new();
        public IReadOnlyList<IWorkInboxSourceV1> Sources { get; } =
        [
            new FakeSource("manufacturing", PlanKind, "manufacturing.plans.approve"),
            new FakeSource("finance", PaymentKind, "finance.manual-bank-payment.approve"),
        ];

        public WorkDbContext Database(Guid? companyId = null)
        {
            var company = new CompanyContext();
            company.Set(companyId ?? Company);
            return new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseInMemoryDatabase(name).Options, company);
        }

        public WorkApprovalRuleServiceV1 Service(Guid? actor = null, bool granted = true, bool companyWide = true,
            Guid? allowedCompany = null) => Service(actor ?? Manager, granted, companyWide, allowedCompany, anonymous: false);

        public WorkApprovalRuleServiceV1 Service(Guid? actor, bool granted, bool companyWide, Guid? allowedCompany, bool anonymous)
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new WorkApprovalRuleServiceV1(Database(), company, new FakeUser(anonymous ? null : actor, allowedCompany ?? Company),
                new FakeAuthority(granted, companyWide), Audit, new FakeCorrelation(), new FixedTime(Now), Sources);
        }
    }

    private sealed class FakeSource(string module, string kind, string permission) : IWorkInboxSourceV1
    {
        public string Module => module;
        public string Kind => kind;
        public string ApproverPermission => permission;

        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount,
            CancellationToken cancellationToken) => throw new InvalidOperationException("The rule table never reads records.");
    }

    private sealed class FakeAuthority(bool granted, bool companyWide) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(granted && permission == Manage && companyId == Company
                ? new PermissionScope(true, companyWide, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(),
                    companyWide ? new HashSet<Guid>() : new HashSet<Guid>([Guid.Parse("d1000000-0000-4000-8000-000000000001")]),
                    new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class FakeUser(Guid? actor, Guid allowed) : ICurrentUser
    {
        public bool IsAuthenticated => actor is not null;
        public string? UserId => actor?.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([allowed]);
        public bool CanAccessCompany(Guid companyId) => companyId == allowed;
    }

    private sealed class FakeCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-request";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
