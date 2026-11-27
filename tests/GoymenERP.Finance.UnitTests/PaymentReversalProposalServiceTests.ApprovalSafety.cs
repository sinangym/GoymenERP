using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class PaymentReversalProposalServiceTests
{
    [Theory]
    [InlineData("missing-writer")] [InlineData("nonrelational")] [InlineData("dirty")] [InlineData("pending-audit")] [InlineData("ambient")]
    public async Task Guarded_decision_entry_preserves_unowned_tracker_and_audit_without_saving(string boundary)
    {
        await using var run = await Run.StartAsync(); await using var database = run.Context();
        var company = new CompanyContext(); company.Set(Company); var writer = new DecisionEntryWriter { HasPendingDrafts = boundary == "pending-audit" };
        if (boundary == "dirty") database.Commands.Add(new() { CompanyId = Company, CommandName = "SENTETIK-caller" });
        var before = database.ChangeTracker.Entries().Select(e => (e.Entity, e.State)).ToArray();
        var service = DecisionCore(database, company, new User("checker"), new FixedPermission(PermissionDecision.Allowed), boundary == "missing-writer" ? null : writer).WithApprovalSafety();
        using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
        foreach (var approve in new[] { true, false })
        {
            var result = approve ? await service.ApproveAsync(new(Company, "checker", "SENTETIK", null), Key("original"), new(Guid.NewGuid(), "1"), default)
                : await service.RejectAsync(new(Company, "checker", "SENTETIK", null), Key("original"), new(Guid.NewGuid(), "1", " reason "), default);
            Assert.Equal(CommandOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource);
            Assert.Equal(before, database.ChangeTracker.Entries().Select(e => (e.Entity, e.State)).ToArray());
            Assert.Equal(boundary == "pending-audit", writer.HasPendingDrafts); Assert.Equal((0, 0), (writer.Writes, writer.Discards));
        }
    }

    [Theory]
    [InlineData("dirty")] [InlineData("audit")]
    public async Task Caller_work_created_during_first_authority_await_is_preserved_outside_owned_cleanup(string work)
    {
        await using var run = await Run.StartAsync(); await using var database = run.Context(); var company = new CompanyContext(); company.Set(Company);
        var writer = new DecisionEntryWriter(); var permissions = new DecisionEntryPermissions();
        permissions.BeforeReturn = () => { if (work == "dirty") database.Commands.Add(new() { CompanyId = Company, CommandName = "SENTETIK-caller" }); else writer.HasPendingDrafts = true; };
        var service = DecisionCore(database, company, new User("checker"), permissions, writer).WithApprovalSafety();
        Assert.Equal(CommandOutcome.Unavailable, (await service.ApproveAsync(new(Company, "checker", "SENTETIK", null), Key("entry"), new(Guid.NewGuid(), "1"), default)).Outcome);
        Assert.Equal(work == "dirty", database.ChangeTracker.HasChanges()); Assert.Equal(work == "audit", writer.HasPendingDrafts);
        Assert.Equal((0, 0), (writer.Writes, writer.Discards));
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)] [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Native_authority_refusal_precedes_unavailable_persistence_without_cleaning_the_caller(PermissionDecision permission, CommandOutcome outcome)
    {
        await using var run = await Run.StartAsync(); await using var database = run.Context(); var company = new CompanyContext(); company.Set(Company);
        var writer = new DecisionEntryWriter { HasPendingDrafts = true };
        var service = DecisionCore(database, company, new User("checker"), new FixedPermission(permission), writer).WithApprovalSafety();
        Assert.Equal(outcome, (await service.RejectAsync(new(Company, "checker", "SENTETIK", null), Key("entry"), new(Guid.NewGuid(), "1", "reason"), default)).Outcome);
        Assert.True(writer.HasPendingDrafts); Assert.Equal((0, 0), (writer.Writes, writer.Discards));
    }

    [Fact]
    public async Task Actual_module_factory_exposes_only_guarded_payment_reversal_runtime_and_preserves_read_alias()
    {
        var company = new CompanyContext(); company.Set(Company);
        await using var database = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("reversal-di-" + Guid.NewGuid()).Options, company);
        var services = new ServiceCollection(); services.AddFinanceServices();
        services.AddSingleton<ICompanyContext>(company); services.AddSingleton<ICurrentUser>(new User("checker"));
        services.AddSingleton<IErpAuthorityRevalidationService>(new DecisionRegistrationAuthority()); services.AddSingleton(database);
        services.AddSingleton<ICorrelationContext>(new CorrelationContext()); services.AddSingleton<IAuditEventCollector>(new AuditEventCollector());
        services.AddSingleton<IAuditTransactionWriterV1>(new DecisionEntryWriter()); services.AddSingleton<IFinanceRecordPeriodRule>(new OpenPeriod()); services.AddSingleton(TimeProvider.System);
        Assert.Single(services, item => item.ServiceType == typeof(IPaymentReversalProposalService));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var service = Assert.IsType<GuardedPaymentReversalProposalService>(scope.ServiceProvider.GetRequiredService<IPaymentReversalProposalService>());
        Assert.Null(scope.ServiceProvider.GetService<PaymentReversalProposalService>());
        Assert.IsAssignableFrom<IPaymentReversalDecisionReadV1>(service);
    }

    [Fact]
    public async Task Decision_read_requires_Approve_and_refuses_the_proposer_without_widening_the_native_general_read()
    {
        await using var run = await Run.StartAsync(); var id = await run.ProposeAsync("maker"); await using var database = run.Context();
        var company = new DecisionCompany(Company); var user = new DecisionUser(); var permissions = new DecisionEntryPermissions();
        permissions.ForAction = a => a == FinanceAction.ProposePaymentReversal ? PermissionDecision.Allowed : PermissionDecision.Denied;
        var core = DecisionCore(database, company, user, permissions, null);
        var context = new FinanceContext(Company, "checker", "read", null);
        Assert.Equal(ReadOutcome.Found, (await core.DetailAuthorizedAsync(context, id, default)).Outcome);
        Assert.Equal(ReadOutcome.Denied, (await core.ReadDecisionAsync(context, id, _ => Task.CompletedTask, default)).Outcome);
        permissions.ForAction = _ => PermissionDecision.Allowed;
        var calls = 0; var found = await core.ReadDecisionAsync(context, id, _ => { calls++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, found.Outcome); Assert.Equal(4, calls); Assert.Equal(run.PaymentId, found.Resource!.PaymentId);
        user.Actor = "maker";
        Assert.Equal(ReadOutcome.Denied, (await core.ReadDecisionAsync(context with { ActorId = "maker" }, id, _ => Task.CompletedTask, default)).Outcome);
        user.Actor = "checker"; calls = 0;
        var revoked = await core.ReadDecisionAsync(context, id, _ => { if (++calls == 3) company.Set(Guid.NewGuid()); return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Denied, revoked.Outcome); Assert.Null(revoked.Resource); Assert.False(database.ChangeTracker.HasChanges());
    }

    private static PaymentReversalProposalService DecisionCore(FinanceDbContext db, ICompanyContext company, ICurrentUser user, IFinancePermissionPort permissions, IAuditTransactionWriterV1? writer) =>
        new(db, company, user, permissions, new OpenPeriod(), new AuditEventCollector(),
            new(new(FinancePermissionNames.PaymentReversePropose, [], "Web"), new(FinancePermissionNames.PaymentReverseApprove, [], "Web")), TimeProvider.System, writer);
    // Counter-only entry fixture; it never represents a SQL transaction or a real collector flush.
    private sealed class DecisionEntryWriter : IAuditTransactionWriterV1
    { public bool HasPendingDrafts { get; set; } public int Writes, Discards;
      public Task WritePendingAsync(IDbTransaction tx, CancellationToken t = default) { Writes++; throw new InvalidOperationException("Entry refusal cannot write."); }
      public void DiscardPending() { Discards++; HasPendingDrafts = false; } }
    private sealed class DecisionEntryPermissions : IFinancePermissionPort
    { public Action? BeforeReturn; public Func<FinanceAction, PermissionDecision>? ForAction;
      public Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
      { var result = ForAction?.Invoke(a) ?? PermissionDecision.Allowed; BeforeReturn?.Invoke(); return Task.FromResult(result); } }
    private sealed class DecisionRegistrationAuthority : IErpAuthorityRevalidationService
    { public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid c, CancellationToken t = default) => Task.FromResult(PermissionScope.Denied); }
    private sealed class DecisionCompany(Guid id) : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } = id; public void Set(Guid value) => CompanyId = value; }
    private sealed class DecisionUser : ICurrentUser
    { public string Actor = "checker"; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id); }
}
