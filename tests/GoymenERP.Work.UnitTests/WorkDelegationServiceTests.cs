using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.ApprovalFollowUp;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Work.UnitTests;

/// <summary>Synthetic authority/audit owner and InMemory: behavioral evidence only; SQL acceptance is separate.</summary>
public sealed class WorkDelegationServiceTests
{
    [Fact]
    public void The_delegation_model_keeps_company_filters_CAS_and_a_forward_fix_guard()
    {
        using var db = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseSqlServer("Server=unused;Database=unused").Options,
            new CompanyContext());
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var row = model.FindEntityType(typeof(WorkDelegationRow))!;
        Assert.Equal("work", row.GetSchema());
        Assert.Equal(new[] { "CompanyId", "Id" }, row.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.NotEmpty(row.GetDeclaredQueryFilters());
        Assert.True(row.FindProperty(nameof(WorkDelegationRow.Version))!.IsConcurrencyToken);
        Assert.True(row.FindProperty(nameof(WorkDelegationRow.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(WorkDbContext.DelegationCheck, Assert.Single(row.GetCheckConstraints()).Sql);
        var migration = new AddWorkApprovalDelegations();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 52120", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static readonly Guid Company = Guid.Parse("d3100000-0000-4000-8000-000000000001");
    private static readonly Guid Sender = Guid.Parse("d3100000-0000-4000-8000-000000000002");
    private static readonly Guid Receiver = Guid.Parse("d3100000-0000-4000-8000-000000000003");
    private static readonly Guid Manager = Guid.Parse("d3100000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);
    private const string Kind = "sales.order.v1";
    private static CreateWorkDelegationV1 Input() => new(Kind, Receiver, Now.AddDays(1), "SENTETIK absence");
    private static WorkCommandMetadataV1 Metadata(string key, string correlation = "SENTETIK-correlation") => new(key, correlation, null);

    [Fact]
    public async Task A_general_manager_cannot_delegate_to_a_system_only_holder_or_borrow_its_broader_scope()
    {
        await using var run = new Run();
        run.People.Items[0] = Holder(Sender) with { IsGeneralManager = true };
        Assert.Equal(WorkDelegationCodesV1.MissingPermission, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
            run.Service(Sender).CreateAsync(Input(), Metadata("system-only")))).Code);
        run.People.Items[1] = Holder(Receiver) with { ModuleManagerScope = new(false,
            new HashSet<Guid>([Guid.NewGuid()]), new HashSet<Guid>(), new HashSet<Guid>()) };
        Assert.Equal(422, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
            run.Service(Sender).CreateAsync(Input(), Metadata("narrow-manager")))).Status);
        run.People.Items[1] = Holder(Receiver) with { ModuleManagerScope = new(true,
            new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) };
        Assert.Equal("1", (await run.Service(Sender).CreateAsync(Input(), Metadata("manager"))).Delegation.Version);
        Assert.Single(await run.Db.Delegations.ToArrayAsync());
    }

    [Fact]
    public async Task An_approver_creates_and_revokes_their_own_delegation_once_without_a_management_grant_or_an_extra_approval()
    {
        await using var run = new Run();
        var service = run.Service(Sender);
        var created = await service.CreateAsync(Input(), Metadata("create"));
        var replay = await service.CreateAsync(Input(), Metadata("create", "SENTETIK-retry"));
        Assert.Equal(Sender, created.Delegation.DelegatorUserId);
        Assert.Equal("1", created.Delegation.Version);
        Assert.Equal(Now, created.Delegation.StartsAtUtc);
        Assert.True(replay.Replayed);
        Assert.Equal(created.Delegation.Id, replay.Delegation.Id);
        Assert.Equal("SENTETIK-correlation", replay.OriginalCorrelationId);
        Assert.DoesNotContain(ErpPermissions.WorkDelegationsManage, run.Authority.Requested);
        var revoked = await service.RevokeAsync(created.Delegation.Id, new("1", "SENTETIK return"), Metadata("revoke"));
        Assert.Equal("2", revoked.Delegation.Version);
        Assert.Equal(Sender, revoked.Delegation.RevokedByUserId);
        Assert.True((await service.RevokeAsync(created.Delegation.Id, new("1", "SENTETIK return"), Metadata("revoke"))).Replayed);
        Assert.Single(await run.Db.Delegations.ToArrayAsync());
        Assert.Equal(2, run.Audit.Requested.Count);
        Assert.Equal(2, await run.Db.CollaborationAttempts.CountAsync());
        Assert.False(run.Audit.HasPendingDrafts);
    }

    [Fact]
    public async Task Managing_someone_elses_delegation_requires_the_separate_company_wide_permission()
    {
        await using var run = new Run();
        var input = Input() with { DelegatorUserId = Sender };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
            run.Service(Manager).CreateAsync(input, Metadata("denied")))).Status);
        run.Authority.Manage = true;
        var created = await run.Service(Manager).CreateAsync(input, Metadata("managed"));
        Assert.Equal(Manager, created.Delegation.RecordedByUserId);
        Assert.Single((await run.Service(Manager).ListAsync(all: true)).Items);
        Assert.Empty((await run.Service(Manager).ListAsync()).Items);
        Assert.Equal(ErpPermissions.WorkDelegationsManage, run.Audit.Requested.Single().PermissionCode);
    }

    [Fact]
    public async Task The_management_permission_can_revoke_its_holders_own_delegation_after_the_approval_grant_is_removed()
    {
        await using var run = new Run();
        var service = run.Service(Sender);
        var created = await service.CreateAsync(Input(), Metadata("own"));
        run.Authority.Approve = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
            service.RevokeAsync(created.Delegation.Id, new("1", "SENTETIK return"), Metadata("denied")))).Status);
        run.Authority.Manage = true;
        var revoked = await service.RevokeAsync(created.Delegation.Id, new("1", "SENTETIK return"), Metadata("managed-return"));
        Assert.Equal("2", revoked.Delegation.Version);
        Assert.Equal(ErpPermissions.WorkDelegationsManage, run.Audit.Requested.Last().PermissionCode);
    }

    [Fact]
    public async Task Future_nonoverlapping_periods_are_allowed_but_do_not_apply_early_or_survive_revocation()
    {
        await using var run = new Run();
        var service = run.Service(Sender);
        var current = await service.CreateAsync(Input(), Metadata("current"));
        var future = await service.CreateAsync(Input() with { StartsAtUtc = Now.AddDays(1), EndsAtUtc = Now.AddDays(2) }, Metadata("future"));
        var item = new WorkInboxItemV1("sales", Kind, Guid.NewGuid(), "SENTETIK order", Now, "/satis/siparisler", null, [], []);
        Assert.Null(await run.Service(Receiver).FindForActorAsync(future.Delegation.Id, item));
        run.Clock.At = Now.AddDays(1);
        Assert.Null(await run.Service(Receiver).FindForActorAsync(current.Delegation.Id, item));
        Assert.NotNull(await run.Service(Receiver).FindForActorAsync(future.Delegation.Id, item));
        await service.RevokeAsync(future.Delegation.Id, new("1", "SENTETIK return"), Metadata("return"));
        Assert.Null(await run.Service(Receiver).FindForActorAsync(future.Delegation.Id, item));
    }

    [Fact]
    public async Task A_missing_existing_approval_grant_or_late_revocation_never_creates_a_delegation()
    {
        await using var run = new Run();
        run.People.Items.RemoveAll(person => person.UserId == Receiver);
        var missing = await Assert.ThrowsAsync<WorkDelegationException>(() => run.Service(Sender).CreateAsync(Input(), Metadata("missing")));
        Assert.Equal(WorkDelegationCodesV1.MissingPermission, missing.Code);
        run.People.Items.Add(Holder(Receiver));
        run.Audit.OnEnqueue = () => run.Authority.Approve = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
            run.Service(Sender).CreateAsync(Input(), Metadata("late")))).Status);
        Assert.Empty(await run.Db.Delegations.ToArrayAsync());
        Assert.Empty(await run.Db.CollaborationAttempts.ToArrayAsync());
        Assert.False(run.Audit.HasPendingDrafts);
    }

    [Fact]
    public async Task Overlapping_redelegation_and_cycles_are_refused_and_the_conflict_replays_its_original_correlation()
    {
        await using var run = new Run();
        await run.Service(Sender).CreateAsync(Input(), Metadata("first"));
        var chain = Input() with { DelegateUserId = Manager };
        var conflict = await Assert.ThrowsAsync<WorkDelegationException>(() => run.Service(Receiver).CreateAsync(chain, Metadata("chain")));
        Assert.Equal(WorkDelegationCodesV1.Chain, conflict.Code);
        var replay = await Assert.ThrowsAsync<WorkDelegationException>(() => run.Service(Receiver).CreateAsync(chain, Metadata("chain", "new")));
        Assert.True(replay.IsReplay);
        Assert.Equal("SENTETIK-correlation", replay.OriginalCorrelationId);
        Assert.Equal(WorkDelegationCodesV1.Chain, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
            run.Service(Manager).CreateAsync(Input() with { DelegateUserId = Sender }, Metadata("cycle")))).Code);
        Assert.Single(await run.Db.Delegations.ToArrayAsync());
    }

    [Fact]
    public async Task The_reader_never_bypasses_the_owners_maker_exclusion_or_a_revoked_permission_and_stops_at_expiry()
    {
        await using var run = new Run();
        var created = await run.Service(Sender).CreateAsync(Input(), Metadata("one"));
        var item = new WorkInboxItemV1("sales", Kind, Guid.NewGuid(), "SENTETIK order", Now, "/satis/siparisler", null, [], []);
        Assert.NotNull(await run.Service(Receiver).FindForActorAsync(created.Delegation.Id, item));
        Assert.Null(await run.Service(Receiver).FindForActorAsync(created.Delegation.Id, item with { PreparedBy = Receiver.ToString("D") }));
        Assert.Null(await run.Service(Receiver).FindForActorAsync(created.Delegation.Id, item with { ExcludedDeciders = [Receiver.ToString("D")] }));
        run.People.Items.RemoveAll(person => person.UserId == Sender);
        Assert.Null(await run.Service(Receiver).FindForActorAsync(created.Delegation.Id, item));
        run.People.Items.Add(Holder(Sender));
        run.Clock.At = created.Delegation.EndsAtUtc;
        Assert.Null(await run.Service(Receiver).FindForActorAsync(created.Delegation.Id, item));
    }

    [Fact]
    public async Task Default_closed_self_foreign_company_bad_period_and_changed_key_cannot_mutate_a_record()
    {
        await using var run = new Run();
        Assert.False(new WorkDelegationOptionsV1().Enabled);
        run.Options.Enabled = false;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDelegationException>(() => run.Service(Sender).CreateAsync(Input(), Metadata("off")))).Status);
        run.Options.Enabled = true;
        foreach (var bad in new[] { Input() with { DelegateUserId = Sender }, Input() with { EndsAtUtc = Now },
                     Input() with { StartsAtUtc = Now.AddMinutes(-1) } })
            Assert.Equal(422, (await Assert.ThrowsAsync<WorkDelegationException>(() => run.Service(Sender).CreateAsync(bad, Metadata(Guid.NewGuid().ToString())))).Status);
        var created = await run.Service(Sender).CreateAsync(Input(), Metadata("valid"));
        Assert.Equal(WorkDelegationCodesV1.KeyMismatch, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
            run.Service(Sender).CreateAsync(Input() with { EndsAtUtc = Now.AddDays(2) }, Metadata("valid")))).Code);
        Assert.Equal(WorkDelegationCodesV1.Version, (await Assert.ThrowsAsync<WorkDelegationException>(() =>
            run.Service(Sender).RevokeAsync(created.Delegation.Id, new("2", "SENTETIK stale"), Metadata("stale")))).Code);
        var other = new CompanyContext(); other.Set(Guid.NewGuid());
        await using var foreign = new WorkDbContext(run.DbOptions, other);
        Assert.Empty(await foreign.Delegations.ToArrayAsync());
        Assert.Single(await run.Db.Delegations.ToArrayAsync());
    }

    private static WorkPermissionHolderV1 Holder(Guid id) => new(id, true, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
    private sealed class Run : IAsyncDisposable
    {
        public DbContextOptions<WorkDbContext> DbOptions { get; } = new DbContextOptionsBuilder<WorkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        public WorkDbContext Db { get; }
        private readonly CompanyContext company = new();
        public Authority Authority { get; } = new();
        public People People { get; } = new();
        public Audit Audit { get; } = new();
        public Clock Clock { get; } = new();
        public WorkDelegationOptionsV1 Options { get; } = new() { Enabled = true };
        public Run() { company.Set(Company); Db = new(DbOptions, company); }
        public WorkDelegationServiceV1 Service(Guid actor) => new(Db, company, new User(actor), Authority, People, [new Source()],
            Audit, Audit, new CorrelationContextForTest(), Microsoft.Extensions.Options.Options.Create(Options), Clock);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Approve { get; set; } = true;
        public bool Manage { get; set; }
        public List<string> Requested { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Requested.Add(permission);
            return Task.FromResult(companyId == Company && (permission == ErpPermissions.WorkDelegationsManage ? Manage : Approve)
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }
    private sealed class People : IWorkPermissionHolderDirectoryV1
    {
        public List<WorkPermissionHolderV1> Items { get; } = [Holder(Sender), Holder(Receiver), Holder(Manager)];
        public Task<WorkPermissionHoldersV1> ListHoldersAsync(Guid companyId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPermissionHoldersV1(companyId == Company && permission == ErpPermissions.SalesOrdersApprove, Items.ToArray()));
    }
    private sealed class Source : IWorkInboxSourceV1
    {
        public string Module => "sales"; public string Kind => WorkDelegationServiceTests.Kind;
        public string ApproverPermission => ErpPermissions.SalesOrdersApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No owner record is read by delegation configuration.");
    }
    private sealed class User(Guid actor) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId => actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset At { get; set; } = Now; public override DateTimeOffset GetUtcNow() => At; }
    private sealed class Audit : IAuditEventCollector, IAuditTransactionWriterV1
    {
        public List<AuditEventDraft> Requested { get; } = [];
        public Action? OnEnqueue { get; set; }
        public bool HasPendingDrafts { get; private set; }
        public void Enqueue(AuditEventDraft draft) { Requested.Add(draft); HasPendingDrafts = true; OnEnqueue?.Invoke(); }
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) => throw new InvalidOperationException("SQL owner is not simulated.");
        public void DiscardPending() => HasPendingDrafts = false;
    }
    private sealed class CorrelationContextForTest : ICorrelationContext
    {
        public bool IsResolved => true; public string CorrelationId => "SENTETIK"; public string? CausationId => null;
        public string SourceChannel => "test"; public string? ClientIpAddress => null; public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }
}
