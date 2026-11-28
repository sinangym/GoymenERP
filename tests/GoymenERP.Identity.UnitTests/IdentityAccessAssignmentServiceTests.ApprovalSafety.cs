using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Identity.UnitTests;

public sealed partial class IdentityAccessAssignmentServiceTests
{
    [Fact]
    public async Task Actual_module_alias_is_guarded_and_uses_the_registered_scoped_collector_and_writer()
    {
        var h = new Harness();
        var context = new CompanyContext();
        context.Set(CompanyA);
        var writer = new ApprovalWriter(h.Audits);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DB_CONNECTION_STRING"] = "Server=source-only-not-connected;Database=SENTETIK_ModuleEntry;Integrated Security=true" }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityModule(configuration, enableAuthorizationSnapshotDatabaseReadiness: false);
        services.AddSingleton<ICompanyContext>(context);
        services.AddSingleton<ICurrentUser>(new TestUser(h.Metadata().ActorId, true, [CompanyA]));
        services.AddSingleton<IErpAuthorizationService>(h.Authorization);
        services.AddSingleton<IErpAuthorityRevalidationService>(h.Revalidation);
        services.AddSingleton<IAuditEventCollector>(h.Audits);
        services.AddSingleton<IAuditTransactionWriterV1>(writer);
        services.AddSingleton<IRefusedAttemptRecorderV1>(h.RefusedAttempts);
        services.AddSingleton<ISecurityEventWriterV1>(h.SecurityEvents);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var core = scope.ServiceProvider.GetRequiredService<IdentityAccessAssignmentService>();
        var service = scope.ServiceProvider.GetRequiredService<IIdentityAccessAssignmentService>();
        Assert.NotSame(core, service);
        Assert.Same(service, scope.ServiceProvider.GetRequiredService<IIdentityAccessAssignmentService>());
        Assert.Same(service, scope.ServiceProvider.GetRequiredService<IIdentityAccessGrantDecisionV1>());
        Assert.Same(core, scope.ServiceProvider.GetRequiredService<IIdentityUserManagementServiceV1>());
        Assert.Same(h.Audits, scope.ServiceProvider.GetRequiredService<IAuditEventCollector>());
        Assert.Same(writer, scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>());
        h.Audits.Enqueue(ApprovalAudit());
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var caller = new UserAccount(Guid.NewGuid(), "SENTETIK module caller", null);
        db.Users.Add(caller);
        Assert.Equal(503, (await service.ApproveAsync(Guid.NewGuid(), new("1"), h.Metadata())).Answer.StatusCode);
        // Even a concrete DI resolution cannot bypass the runtime guard. Directly constructed old protocol fixtures remain explicit.
        Assert.Equal(503, (await core.ApproveAsync(Guid.NewGuid(), new("1"), h.Metadata())).Answer.StatusCode);
        Assert.Equal(EntityState.Added, db.Entry(caller).State);
        Assert.Single(h.Audits.Enqueued);
        Assert.Equal(0, writer.Discards);
        Assert.Equal(0, writer.Writes);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    // Unopened SQL-provider entry tests, plus explicit InMemory refusal; neither is real transaction acceptance.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Guarded_decisions_fail_closed_on_InMemory_without_a_write_or_discard(bool approve)
    {
        var h = new Harness();
        var writer = new ApprovalWriter(h.Audits);
        var guarded = h.Service(writer: writer).WithAccessGrantApprovalSafety();
        var answer = await Decide(guarded, approve, h.Metadata());
        Assert.Equal(503, answer.Answer.StatusCode);
        Assert.False(answer.IsReplay);
        Assert.Equal(0, writer.Writes);
        Assert.Equal(0, writer.Discards);
        h.AssertNothingRecordedOrStored();
    }

    [Theory]
    [InlineData(true, "dirty")]
    [InlineData(false, "dirty")]
    [InlineData(true, "audit")]
    [InlineData(false, "audit")]
    [InlineData(true, "ambient")]
    [InlineData(false, "ambient")]
    public async Task Guarded_entry_preserves_caller_work_without_opening_SQL(bool approve, string boundary)
    {
        var h = new Harness();
        await using var db = UnopenedApprovalDatabase();
        var writer = new ApprovalWriter(h.Audits);
        var core = h.Service(db, writer);
        var caller = new UserAccount(Guid.NewGuid(), "SENTETIK caller", null);
        if (boundary == "dirty") db.Users.Add(caller);
        if (boundary == "audit") h.Audits.Enqueue(ApprovalAudit());
        using var ambient = boundary == "ambient" ? new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        var answer = await Decide(core.WithAccessGrantApprovalSafety(), approve, h.Metadata());
        Assert.Equal(503, answer.Answer.StatusCode);
        Assert.Equal(0, writer.Discards);
        Assert.Equal(0, writer.Writes);
        Assert.Empty(h.Revalidation.Asked);
        if (boundary == "dirty") Assert.Equal(EntityState.Added, db.Entry(caller).State);
        if (boundary == "audit") Assert.Single(h.Audits.Enqueued);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("audit")]
    public async Task Caller_work_introduced_during_first_fresh_await_is_not_adopted_or_cleared(string boundary)
    {
        var h = new Harness();
        await using var db = UnopenedApprovalDatabase();
        var writer = new ApprovalWriter(h.Audits);
        var caller = new UserAccount(Guid.NewGuid(), "SENTETIK callback caller", null);
        h.Revalidation.BeforeFresh = () =>
        {
            if (boundary == "dirty") db.Users.Add(caller);
            else h.Audits.Enqueue(ApprovalAudit());
        };
        var answer = await Decide(h.Service(db, writer).WithAccessGrantApprovalSafety(), true, h.Metadata());
        Assert.Equal(503, answer.Answer.StatusCode);
        Assert.Single(h.Revalidation.Asked);
        Assert.Equal(0, writer.Discards);
        Assert.Equal(0, writer.Writes);
        if (boundary == "dirty") Assert.Equal(EntityState.Added, db.Entry(caller).State);
        else Assert.Single(h.Audits.Enqueued);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_denial_or_completed_cancellation_never_opens_an_approval_transaction(bool cancel)
    {
        var h = new Harness();
        await using var db = UnopenedApprovalDatabase();
        using var cancellation = new CancellationTokenSource();
        var writer = new ApprovalWriter(h.Audits);
        h.Revalidation.BeforeFresh = () =>
        {
            if (cancel) cancellation.Cancel();
            else h.Revalidation.Scope = GoymenERP.BuildingBlocks.Security.PermissionScope.Denied;
        };
        var guarded = h.Service(db, writer).WithAccessGrantApprovalSafety();
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guarded.ApproveAsync(Guid.NewGuid(), new("1"), h.Metadata(), cancellation.Token));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guarded.ApproveAsync(Guid.NewGuid(), new("1"), h.Metadata()));
        Assert.Single(h.Revalidation.Asked);
        Assert.Equal(0, writer.Discards);
        Assert.Equal(0, writer.Writes);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("membership")]
    public async Task Completed_fresh_callback_cannot_release_a_changed_actor_company_or_membership(string change)
    {
        var h = new Harness();
        await using var db = UnopenedApprovalDatabase();
        var actor = new ApprovalActor(h.Metadata().ActorId);
        var company = new ApprovalCompany();
        var writer = new ApprovalWriter(h.Audits);
        h.Revalidation.BeforeFresh = () =>
        {
            if (change == "actor") actor.UserId = Guid.NewGuid().ToString("D");
            if (change == "company") company.Set(CompanyB);
            if (change == "membership") actor.Companies.Clear();
        };
        var guarded = h.Service(db, writer, company, actor).WithAccessGrantApprovalSafety();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guarded.ApproveAsync(Guid.NewGuid(), new("1"), h.Metadata()));
        Assert.Single(h.Revalidation.Asked);
        Assert.Equal(0, writer.Discards);
        Assert.Equal(0, writer.Writes);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task Guarded_facade_forwards_nonapproval_native_validation_unchanged()
    {
        var h = new Harness();
        var result = await h.Service().WithAccessGrantApprovalSafety().AssignAsync(
            new(Guid.Empty, Guid.Empty, null, null, null), h.Metadata());
        Assert.Equal(400, result.Answer.StatusCode);
        Assert.Equal(IdentityAccessProblemCodesV1.RequestInvalid, result.Answer.Code);
        Assert.Single(h.RefusedAttempts.Standalone);
        Assert.Empty(h.Revalidation.Asked);
    }

    [Theory]
    [InlineData(" 1", "reason_code", "expectedVersion")]
    [InlineData("0", "reason_code", "expectedVersion")]
    [InlineData("1", null, "reasonCode")]
    [InlineData("1", "Reason", "reasonCode")]
    public async Task Authorized_malformed_decisions_preserve_native_validation_and_foreign_pending_audit(string version, string? reason, string field)
    {
        var h = new Harness();
        var writer = new ApprovalWriter(h.Audits);
        h.Audits.Enqueue(ApprovalAudit());
        var caller = new UserAccount(Guid.NewGuid(), "SENTETIK invalid caller", null);
        h.Database.Users.Add(caller);
        var result = await h.Service(writer: writer).WithAccessGrantApprovalSafety().RejectAsync(Guid.NewGuid(), new(version, reason!), h.Metadata());
        Assert.Equal(400, result.Answer.StatusCode);
        Assert.Equal(IdentityAccessProblemCodesV1.RequestInvalid, result.Answer.Code);
        Assert.Equal(field, Assert.Single(Assert.Single(h.RefusedAttempts.Standalone).FieldErrors).Field);
        Assert.Single(h.Audits.Enqueued);
        Assert.Equal(EntityState.Added, h.Database.Entry(caller).State);
        Assert.Equal(0, writer.Discards);
        Assert.Equal(0, writer.Writes);
        Assert.Empty(h.Revalidation.Asked);
    }

    private static Task<IdentityAccessCommandResultV1> Decide(IIdentityAccessAssignmentService service, bool approve, IdentityAccessCommandMetadataV1 metadata) =>
        approve ? service.ApproveAsync(Guid.NewGuid(), new("1"), metadata)
            : service.RejectAsync(Guid.NewGuid(), new("1", "reason_code"), metadata);

    private static IdentityDbContext UnopenedApprovalDatabase() => new(new DbContextOptionsBuilder<IdentityDbContext>()
        .UseSqlServer("Server=source-only-not-connected;Database=SENTETIK_IdentityEntry;Integrated Security=true;TrustServerCertificate=true").Options);

    private static AuditEventDraft ApprovalAudit() => new(CompanyA, "identity.source-only.probe.v1", "SENTETIK", Guid.NewGuid().ToString("D"), "Probe",
        null, null, AuditActorTypes.System, "SENTETIK", IdentityPermissionManifestV1.AccessApprove, [], "{}", "{}", "SENTETIK caller audit", null,
        AuditAiInvolvement.None, null, "System", null, null, "SENTETIK-correlation", null, "SENTETIK-key");

    private sealed class ApprovalWriter(RecordingAuditCollector collector) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => collector.Enqueued.Count != 0;
        public int Writes { get; private set; }
        public int Discards { get; private set; }
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Entry-only test must never open SQL."); }
        public void DiscardPending() { Discards++; collector.Enqueued.Clear(); }
    }

    private sealed class ApprovalCompany : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = CompanyA;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class ApprovalActor(string actorId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = actorId;
        public HashSet<Guid> Companies { get; } = [CompanyA];
        public IReadOnlySet<Guid> AllowedCompanyIds => Companies;
        public bool CanAccessCompany(Guid companyId) => Companies.Contains(companyId);
    }
}
