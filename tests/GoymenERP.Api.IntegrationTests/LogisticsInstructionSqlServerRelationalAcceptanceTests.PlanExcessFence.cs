using System.Transactions;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    // Actual SQL/native plan proposal+approval/receipt/audit, controlled authority. No hosted Work/Identity acceptance.
    [SqlServerFact]
    public async Task Plan_excess_native_approval_post_save_authority_loss_rolls_back_capacity_receipt_and_real_audit_then_replays_exactly()
    {
        await using var run = await Run.StartAsync(); var probe = new PlanApprovalProbe();
        await using var frame = OwnedPlanExcessFrame(run, probe);
        var (instruction, exception) = await SeedOwnedPlanException(run, frame);
        var before = await run.CountsAsync(instruction); var refuse = true;
        var service = PlanApprovalService(frame, () => !refuse || !probe.Saved);
        var request = new ApproveLogisticsPlanExcessV1("1", "3"); var metadata = new LogisticsCommandMetadata("px-owned-approve", run.Correlation, null, DateTimeOffset.UtcNow);
        Assert.Equal(403, (await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => service.ApproveAsync(exception, request, metadata))).Status);
        Assert.True(probe.Saved); Assert.False(frame.Writer.HasPendingDrafts); Assert.Empty(frame.Db.ChangeTracker.Entries());
        Assert.Null(frame.Db.Database.CurrentTransaction); Assert.Equal(before, await run.CountsAsync(instruction));
        Assert.Equal(0m, await frame.Db.Lines.Select(row => row.ApprovedPlanExcess).SingleAsync());
        refuse = false;
        var approved = await service.ApproveAsync(exception, request, metadata);
        Assert.Equal(("Approved", "2", "3"), (approved.Exception.Status, approved.Exception.Version, approved.Exception.InstructionVersion));
        Assert.Equal(20m, await frame.Db.Lines.Select(row => row.ApprovedPlanExcess).SingleAsync());
        var replay = await service.ApproveAsync(exception, request, metadata);
        Assert.Equal(approved with { IsReplay = true }, replay); Assert.False(frame.Writer.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Plan_excess_save_concurrency_aborts_real_pending_audit_and_performs_only_one_bounded_winner_lookup_without_new_conflict_receipt()
    {
        await using var run = await Run.StartAsync(); var probe = new PlanApprovalProbe { Concurrency = true };
        await using var frame = OwnedPlanExcessFrame(run, probe);
        var (instruction, exception) = await SeedOwnedPlanException(run, frame); var before = await run.CountsAsync(instruction);
        var service = PlanApprovalService(frame, () => true);
        var error = await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => service.ApproveAsync(exception,
            new("1", "3"), new LogisticsCommandMetadata("px-owned-race", run.Correlation, null, DateTimeOffset.UtcNow)));
        Assert.Equal(409, error.Status); Assert.Equal(1, probe.SaveAttempts);
        Assert.False(frame.Writer.HasPendingDrafts); Assert.Empty(frame.Db.ChangeTracker.Entries());
        Assert.Null(frame.Db.Database.CurrentTransaction); Assert.Equal(before, await run.CountsAsync(instruction));
        Assert.Empty(await frame.Db.Attempts.Where(row => row.CommandName == LogisticsPlanExcessV1.ApproveCommand).ToListAsync());
        probe.Concurrency = false;
        var retry = await service.ApproveAsync(exception, new("1", "3"), new LogisticsCommandMetadata("px-owned-race", run.Correlation, null, DateTimeOffset.UtcNow));
        Assert.False(retry.IsReplay); Assert.False(frame.Writer.HasPendingDrafts);
    }

    [SqlServerFact]
    public async Task Plan_excess_owned_boundary_refuses_ambient_borrowed_dirty_pending_caller_audit_and_missing_writer_without_cleanup_or_callbacks()
    {
        await using var run = await Run.StartAsync();
        foreach (var fault in new[] { "ambient", "borrowed", "dirty", "pending-audit", "missing-writer" })
        {
            await using var frame = OwnedPlanExcessFrame(run);
            var (instruction, exception) = await SeedOwnedPlanException(run, frame);
            var service = PlanApprovalService(frame, () => true);
            var body = new ApproveLogisticsPlanExcessV1("1", "3"); var metadata = new LogisticsCommandMetadata("px-entry-" + exception.ToString("N"), run.Correlation, null, DateTimeOffset.UtcNow);
            await service.ApproveAsync(exception, body, metadata); // A real stored replay exists before caller state is introduced.
            var row = await frame.Db.Instructions.SingleAsync(row => row.Id == instruction);
            if (fault == "dirty") row.Version++;
            if (fault == "pending-audit") frame.Collector.Enqueue(new(CompanyA, "logistics.synthetic.caller.v1", "Logistics.LoadingInstruction", instruction.ToString("D"),
                "Caller", 1, 1, AuditActorTypes.User, run.Actor, "synthetic.permission", [], "{}", "{}", "SYNTHETIC caller-owned draft", null,
                AuditAiInvolvement.None, null, "Web", null, null, run.Correlation, null, "caller-owned"));
            await using var borrowed = fault == "borrowed" ? await frame.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled) : null;
            using var missing = new ServiceCollection().AddSingleton<ICompanyContext>(frame.Company).BuildServiceProvider();
            var runner = fault == "missing-writer" ? new LogisticsRealizationTransactionRunner(frame.Db, missing, TimeProvider.System) : frame.Runner;
            var callbacks = 0; var reads = 0;
            Assert.Equal(503, (await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => runner.ExecutePlanExcessAsync(CompanyA,
                async token => { reads++; return await frame.Store.FindPlanExcessReplayAsync(run.Envelope(metadata.IdempotencyKey, actor: PlanExcessApprover),
                    Hash("unused"), LogisticsPlanExcessV1.ApproveCommand, token); }, _ => { callbacks++; return Task.CompletedTask; }))).Status);
            Assert.Equal((0, 0), (callbacks, reads));
            if (fault == "dirty") Assert.True(frame.Db.ChangeTracker.HasChanges());
            if (fault == "pending-audit") Assert.True(frame.Writer.HasPendingDrafts);
            if (fault == "borrowed") Assert.Same(borrowed, frame.Db.Database.CurrentTransaction);
        }
    }

    [SqlServerFact]
    public async Task Shared_plan_excess_proposal_boundary_accepts_native_proposal_with_real_audit_and_preserves_rejected_caller_state()
    {
        await using var run = await Run.StartAsync();
        foreach (var fault in new[] { "dirty", "pending-audit", "borrowed" })
        {
            await using var frame = OwnedPlanExcessFrame(run);
            // Existing helper executes the real native ProposePlanExcessAsync through the shared owned runner,
            // using exactly the same actual Audit collector/writer as LogisticsStore and its interceptor.
            var (instruction, exception) = await SeedOwnedPlanException(run, frame);
            var original = await frame.Store.FindPlanExcessReplayAsync(run.Envelope("px-owned-propose-" + exception)
                with { PermissionCode = LogisticsPermissionManifestV1.LoadsRecord }, Hash("propose"), LogisticsPlanExcessV1.ProposeCommand, default);
            Assert.NotNull(original); Assert.True(original.IsReplay);
            Assert.Equal((exception, "Pending", "1", "3", run.Actor), (original.Exception.ExceptionId,
                original.Exception.Status, original.Exception.Version, original.Exception.InstructionVersion, original.Exception.ProposedBy));
            Assert.False(frame.Writer.HasPendingDrafts);
            var before = await run.CountsAsync(instruction);
            var row = await frame.Db.Instructions.SingleAsync(row => row.Id == instruction);
            if (fault == "dirty") row.Version++;
            if (fault == "pending-audit") frame.Collector.Enqueue(new(CompanyA, "logistics.synthetic.caller.v1", "Logistics.LoadingInstruction", instruction.ToString("D"),
                "Caller", 1, 1, AuditActorTypes.User, run.Actor, "synthetic.permission", [], "{}", "{}", "SYNTHETIC caller-owned draft", null,
                AuditAiInvolvement.None, null, "Web", null, null, run.Correlation, null, "caller-proposal"));
            await using var borrowed = fault == "borrowed" ? await frame.Db.Database.BeginTransactionAsync() : null;
            var callbacks = 0; var writes = 0;
            Assert.Equal(503, (await Assert.ThrowsAsync<LogisticsInstructionServiceException>(() => frame.Runner.ExecutePlanExcessAsync(CompanyA,
                token => { writes++; return frame.Store.ProposePlanExcessAsync(run.Envelope("px-refused-proposal-" + exception)
                    with { PermissionCode = LogisticsPermissionManifestV1.LoadsRecord }, Hash("refused-proposal"), instruction, original.Exception.LineId,
                    new(Guid.NewGuid(), "3", "10", "SYNTHETIC refused proposal"), token); },
                _ => { callbacks++; return Task.CompletedTask; }))).Status);
            Assert.Equal((0, 0), (callbacks, writes)); Assert.Equal(before, await run.CountsAsync(instruction));
            if (fault == "dirty") Assert.True(frame.Db.ChangeTracker.HasChanges());
            if (fault == "pending-audit") Assert.True(frame.Writer.HasPendingDrafts);
            if (fault == "borrowed") Assert.Same(borrowed, frame.Db.Database.CurrentTransaction);
        }
    }

    private static LogisticsInstructionService PlanApprovalService(PlanApprovalFrame frame, Func<bool> allowed)
    {
        var correlation = new CorrelationContext(); correlation.Set("px-owned-correlation", null, "Web", null, null);
        return new(frame.Company, new PlanApprovalUser(), new PlanApprovalAuthority(allowed), correlation,
            null!, null!, null!, frame.Store, new(), TimeProvider.System, frame.Runner);
    }
    private sealed class PlanApprovalUser : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => PlanExcessApprover;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([CompanyA]);
        public bool CanAccessCompany(Guid companyId) => companyId == CompanyA;
    }
    private sealed class PlanApprovalAuthority(Func<bool> allowed) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(allowed() ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
    private static async Task<(Guid Instruction, Guid Exception)> SeedOwnedPlanException(Run run, PlanApprovalFrame frame)
    {
        var instruction = InstructionForOrderLine(Owners.For(CompanyA), Guid.NewGuid(), 100m); var exception = Guid.NewGuid();
        await frame.Store.StoreInstructionAsync(run.Envelope("px-owned-create-" + exception), Hash("create"), Day, () => instruction);
        await frame.Store.MutateInstructionAsync(run.Envelope("px-owned-issue-" + exception), Hash("issue"), LogisticsCommands.IssueV1,
            instruction.Id, (value, now) => value.Issue(2, now), Issued);
        await frame.Runner.ExecutePlanExcessAsync(CompanyA, token => frame.Store.ProposePlanExcessAsync(run.Envelope("px-owned-propose-" + exception)
            with { PermissionCode = LogisticsPermissionManifestV1.LoadsRecord }, Hash("propose"), instruction.Id, instruction.Lines[0].Id,
            new(exception, "3", "20", "SYNTHETIC native proposal"), token), _ => Task.CompletedTask);
        return (instruction.Id, exception);
    }
    private sealed record PlanApprovalFrame(LogisticsDbContext Db, LogisticsStore Store, ICompanyContext Company,
        AuditEventCollector Collector, IAuditTransactionWriterV1 Writer, LogisticsRealizationTransactionRunner Runner, IServiceScope Scope) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); Scope.Dispose(); }
    }
    private static PlanApprovalFrame OwnedPlanExcessFrame(Run run, IInterceptor? extra = null)
    {
        // Reuse the existing SQL factory and its actual Audit registration, collector and writer scope.
        var scope = run.Factory.Services.CreateScope(); var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>(); var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
        var options = new DbContextOptionsBuilder<LogisticsDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
        if (extra is not null) options.AddInterceptors(extra);
        var db = new LogisticsDbContext(options.Options, company);
        return new(db, new LogisticsStore(db, company, collector, TimeProvider.System), company, collector, writer,
            new LogisticsRealizationTransactionRunner(db, scope.ServiceProvider, TimeProvider.System), scope);
    }
    private sealed class PlanApprovalProbe : SaveChangesInterceptor
    {
        public bool Saved, Concurrency; public int SaveAttempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is LogisticsDbContext db && db.ChangeTracker.Entries<LogisticsPlanExcessRow>().Any(row => row.Entity.Status == 2))
            { SaveAttempts++; if (Concurrency) throw new DbUpdateConcurrencyException("SYNTHETIC first approval save concurrency"); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        {
            if (data.Context is LogisticsDbContext db && db.ChangeTracker.Entries<LogisticsPlanExcessRow>().Any(row => row.Entity.Status == 2)) Saved = true;
            return ValueTask.FromResult(result);
        }
    }
}
