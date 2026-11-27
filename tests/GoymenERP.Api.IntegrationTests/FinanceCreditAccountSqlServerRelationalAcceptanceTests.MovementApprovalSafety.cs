using System.Data.Common;
using System.Text.Json;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    // Actual SQL transaction and SAME scoped Audit collector/writer. Lender and permission answers remain controlled.
    [SqlServerFact]
    public async Task Credit_movement_owned_decisions_roll_back_late_authority_and_delegation_loss_then_same_key_decides_once()
    {
        foreach (var approve in new[] { true, false })
        foreach (var phase in new[] { "before-save", "after-audit", "after-save", "precommit", "actor", "company", "membership", "delegation" })
        {
            await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run);
            var save = new MovementDecisionSave(id); await using var scope = new MovementDecisionScope(run, save: save);
            var deny = true; var observed = false; var savedFences = 0; var sawPending = false;
            scope.Permission.Check = () =>
            {
                var staged = scope.Db.ChangeTracker.Entries<FinanceCreditMovementRow>().Any(x => x.Entity.Id == id && x.Entity.Version == 2);
                if (staged && scope.Writer.HasPendingDrafts) sawPending = true;
                if (save.Saved) savedFences++;
                var reached = phase == "before-save" ? staged && scope.Writer.HasPendingDrafts
                    : phase == "after-audit" ? staged && sawPending && !scope.Writer.HasPendingDrafts
                    : phase == "precommit" ? savedFences >= 3 : save.Saved;
                if (!deny || !reached) return PermissionDecision.Allowed;
                observed = true;
                if (phase == "actor") { scope.User.Actor = Maker; return PermissionDecision.Allowed; }
                if (phase == "company") { scope.Company.Set(CompanyB); return PermissionDecision.Allowed; }
                if (phase == "membership") { scope.User.Member = false; return PermissionDecision.Allowed; }
                // Controlled delegation refusal is the same owner permission-port boundary used by Work's decorator.
                return PermissionDecision.Denied;
            };
            var key = "movement-late-" + phase; var result = await scope.Decide(id, key, approve);
            Assert.Equal(CommandOutcome.Denied, result.Outcome); Assert.True(observed); Assert.Equal(phase is not ("before-save" or "after-audit"), save.Saved);
            if (phase == "precommit") Assert.Equal(3, savedFences); // post-Save (1), post-native (2), final precommit (3); no extra receipt query.
            scope.AssertClean(); await MovementSafetyDurable(run, id, null);
            deny = false; scope.User.Actor = Checker; scope.User.Member = true; scope.Company.Set(CompanyA); save.Saved = false;
            var success = await scope.Decide(id, key, approve); Assert.Equal(CommandOutcome.Succeeded, success.Outcome);
            Assert.Equal(approve ? CreditMovementStatusV1.Approved : CreditMovementStatusV1.Rejected, success.Resource!.Status);
            Assert.Equal(approve ? null : "SENTETIK literal ret", success.Resource.RejectionReason);
            Assert.Equal("2", success.Resource.Version); Assert.Null(success.Resource.Source.Components[0].FunctionalAmount);
            var originalJson = await run.ScalarAsync<string>("SELECT ResultJson FROM finance.CreditMovementCommands WHERE CompanyId=@c AND Command=N'finance.credit-movement.decide' AND KeyHash=@key",
                ("@c", CompanyA), ("@key", FinanceCommandKeys.Hash(key)));
            Assert.Equal(originalJson, JsonSerializer.Serialize(success, FinanceJson.CreateOptions()));
            using (var wire = JsonDocument.Parse(originalJson)) Assert.Equal(5, wire.RootElement.EnumerateObject().Count());
            var replay = await scope.Decide(id, key, approve); Assert.True(replay.IsReplay); Assert.Equal(success.OriginalCorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(JsonSerializer.Serialize(success.Resource, FinanceJson.CreateOptions()), JsonSerializer.Serialize(replay.Resource, FinanceJson.CreateOptions()));
            scope.AssertClean(); await MovementSafetyDurable(run, id, approve);
        }
    }

    [SqlServerFact]
    public async Task Credit_movement_decision_entry_preserves_borrowed_ambient_dirty_and_pending_caller_work()
    {
        foreach (var kind in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-writer" })
        {
            await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run);
            await using var scope = new MovementDecisionScope(run, missingWriter: kind == "missing-writer");
            using var ambient = kind == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
            await using var borrowed = kind == "borrowed" ? await scope.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
            var caller = new FinanceCreditMovementRow { CompanyId = CompanyA, Id = Guid.NewGuid(), Version = 1, SourceJson = "{}", SourceIdentityHash = new string('a', 64), MoneyProfileJson = "{}", ProposedBy = "SENTETIK caller" };
            if (kind == "dirty") scope.Db.CreditMovements.Add(caller);
            if (kind == "pending") scope.Collector.Enqueue(MovementSafetyAudit(id));
            scope.Permission.Check = () =>
            {
                if (kind == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.CreditMovements.Add(caller);
                if (kind == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(MovementSafetyAudit(id));
                return PermissionDecision.Allowed;
            };
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Decide(id, "movement-entry", true)).Outcome);
            Assert.Equal(kind is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges());
            if (kind is "dirty" or "await-dirty") Assert.Contains(scope.Db.ChangeTracker.Entries<FinanceCreditMovementRow>(), x => ReferenceEquals(x.Entity, caller) && x.State == EntityState.Added);
            Assert.Equal(kind is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
            Assert.Same(borrowed, scope.Db.Database.CurrentTransaction);
            if (borrowed is not null) await borrowed.RollbackAsync();
            // The fixture owns this caller work; the refused command must NOT clear/discard it.
            scope.Db.ChangeTracker.Clear(); scope.Writer.DiscardPending(); await MovementSafetyDurable(run, id, null);
        }
    }

    [SqlServerFact]
    public async Task Credit_movement_failed_save_or_controlled_1205_cleans_owned_state_before_one_original_key_readonly_pass()
    {
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "audit-before-save", "completed-provider" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run);
            var save = new MovementDecisionSave(id) { Fault = fault }; await using var scope = new MovementDecisionScope(run, save: save);
            var key = "movement-race"; var winnerDone = false; CreditMovementResultV1? original = null;
            scope.Permission.AfterRead = async () =>
            {
                if (fault == "audit-before-save" && !save.Injected && scope.Writer.HasPendingDrafts)
                { save.Injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                if (!save.Injected || winnerDone || scope.Db.Database.CurrentTransaction is not null) return;
                winnerDone = true; scope.AssertClean();
                if (winner)
                {
                    await using var other = new MovementDecisionScope(run);
                    original = await other.Decide(id, key, true); Assert.Equal(CommandOutcome.Succeeded, original.Outcome); other.AssertClean();
                }
            };
            var answer = await scope.Decide(id, key, true); Assert.True(save.Injected); Assert.True(winnerDone);
            Assert.Equal(winner ? CommandOutcome.Succeeded : CommandOutcome.VersionConflict, answer.Outcome);
            Assert.Equal(winner, answer.IsReplay); Assert.Equal(fault == "audit-before-save" ? 0 : 1, save.Attempts);
            if (winner) { Assert.Equal(JsonSerializer.Serialize(original!.Resource, FinanceJson.CreateOptions()), JsonSerializer.Serialize(answer.Resource, FinanceJson.CreateOptions())); Assert.Equal(original.OriginalCorrelationId, answer.OriginalCorrelationId); }
            scope.AssertClean(); await MovementSafetyDurable(run, id, winner ? true : null);
            // Controlled SQL failures + an ordered native winner, NOT evidence of a simultaneous two-session deadlock.
        }
    }

    [SqlServerFact]
    public async Task Credit_movement_rollback_and_dispose_failures_still_clean_owned_state_and_never_read_a_winner()
    {
        foreach (var dispose in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run);
            var fault = new MovementDecisionCleanup(dispose); var save = new MovementDecisionSave(id) { Fault = "concurrency" };
            await using var scope = new MovementDecisionScope(run, save: save, cleanup: fault); var postAbortReads = 0;
            scope.Permission.Check = () => { if (save.Injected && scope.Db.Database.CurrentTransaction is null) postAbortReads++; return PermissionDecision.Allowed; };
            fault.Armed = true;
            await Assert.ThrowsAsync<AggregateException>(() => scope.Decide(id, "movement-cleanup", true));
            Assert.True(fault.Failed); Assert.Equal(0, postAbortReads); Assert.Equal(1, save.Attempts); scope.AssertClean();
            await MovementSafetyDurable(run, id, null);
        }
    }

    [SqlServerFact]
    public async Task Credit_movement_postcommit_refusal_keeps_original_native_decision_and_replay_requires_fresh_authority()
    {
        await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run); var committed = new MovementDecisionCommitted();
        await using var scope = new MovementDecisionScope(run, extra: committed); var deny = true;
        scope.Permission.Check = () => deny && committed.Commits > 0 ? PermissionDecision.Denied : PermissionDecision.Allowed;
        Assert.Equal(CommandOutcome.Denied, (await scope.Decide(id, "movement-release", false)).Outcome);
        scope.AssertClean(); await MovementSafetyDurable(run, id, false);
        Assert.Equal(CommandOutcome.Denied, (await scope.Decide(id, "movement-release", false)).Outcome);
        deny = false; var replay = await scope.Decide(id, "movement-release", false);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
        Assert.Equal(scope.OriginalCorrelation, replay.OriginalCorrelationId); Assert.Equal("SENTETIK literal ret", replay.Resource!.RejectionReason);
        scope.AssertClean(); await MovementSafetyDurable(run, id, false);
    }

    [SqlServerFact]
    public async Task Credit_movement_second_readonly_fault_and_postcommit_1205_never_start_a_second_mutation()
    {
        foreach (var postcommit in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run);
            var save = new MovementDecisionSave(id) { Fault = postcommit ? null : "concurrency" }; var committed = new MovementDecisionCommitted();
            await using var scope = new MovementDecisionScope(run, save: save, extra: committed); var transactions = new HashSet<Guid>(); var injected = false;
            scope.Permission.AfterRead = () =>
            {
                if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId);
                if (postcommit ? committed.Commits > 0 : transactions.Count == 2)
                { injected = true; throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205); }
                return Task.CompletedTask;
            };
            Assert.Equal(CommandOutcome.Unavailable, (await scope.Decide(id, "movement-no-loop", true)).Outcome);
            Assert.True(injected); Assert.Equal(postcommit ? 1 : 2, transactions.Count); Assert.Equal(1, save.Attempts);
            scope.AssertClean(); await MovementSafetyDurable(run, id, postcommit ? true : null);
        }
    }

    [SqlServerFact]
    public async Task Credit_movement_owned_cancellation_after_save_rolls_back_outbox_receipt_and_audit_then_retries_once()
    {
        await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run); var save = new MovementDecisionSave(id);
        await using var scope = new MovementDecisionScope(run, save: save); using var cancellation = new CancellationTokenSource();
        scope.Permission.Check = () => { if (save.Saved) cancellation.Cancel(); return PermissionDecision.Allowed; };
        var context = new FinanceContext(CompanyA, Checker, scope.OriginalCorrelation, null); var request = new CreditMovementDecisionV1(id, "1", true, null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Service.DecideAsync(context, new("movement-cancel-owned"), request, cancellation.Token));
        Assert.True(save.Saved); scope.AssertClean(); await MovementSafetyDurable(run, id, null);
        scope.Permission.Check = null; save.Saved = false;
        var result = await scope.Service.DecideAsync(context, new("movement-cancel-owned"), request, default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome); Assert.False(result.IsReplay);
        Assert.True((await scope.Service.DecideAsync(context, new("movement-cancel-owned"), request, default)).IsReplay);
        scope.AssertClean(); await MovementSafetyDurable(run, id, true);
    }

    [SqlServerFact]
    public async Task Credit_movement_owned_terminal_refusals_keep_maker_version_hash_and_native_original_receipt_protocol()
    {
        await using var run = await Run.StartAsync(); var id = await MovementSafetyPrepare(run); await using var scope = new MovementDecisionScope(run);
        var checker = new FinanceContext(CompanyA, Checker, scope.OriginalCorrelation, null); var stale = new CreditMovementDecisionV1(id, "9", true, null);
        var refused = await scope.Service.DecideAsync(checker, new("movement-stale-native"), stale, default);
        Assert.Equal(CommandOutcome.VersionConflict, refused.Outcome); Assert.Null(refused.Resource);
        var replay = await scope.Service.DecideAsync(checker with { CorrelationId = "SENTETIK-new-request" }, new("movement-stale-native"), stale, default);
        Assert.True(replay.IsReplay); Assert.Equal(refused.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal(refused.Outcome, replay.Outcome);
        Assert.Equal(CommandOutcome.IdempotencyConflict, (await scope.Service.DecideAsync(checker, new("movement-stale-native"), stale with { ExpectedVersion = "1" }, default)).Outcome);
        scope.User.Actor = Maker;
        var own = await scope.Service.DecideAsync(checker with { ActorId = Maker }, new("movement-maker-native"), new(id, "1", false, "SENTETIK ret"), default);
        Assert.Equal(CommandOutcome.ValidationFailed, own.Outcome); Assert.Equal("credit_movement_maker_cannot_approve", Assert.Single(own.Issues).Code);
        scope.User.Actor = Checker; scope.Company.Set(CompanyB);
        Assert.Equal(CommandOutcome.Denied, (await scope.Service.DecideAsync(checker, new("movement-foreign-native"), new(id, "1", true, null), default)).Outcome);
        scope.Company.Set(CompanyA); scope.AssertClean();
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.CreditMovements WHERE CompanyId=@c AND Id=@id", ("@c", CompanyA), ("@id", id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands WHERE CompanyId=@c AND Command=N'finance.credit-movement.decide'", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id", ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement' AND EntityId=@id AND EventType<>N'finance.credit-movement.proposed.v1'", ("@id", id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Credit_opening_correction_owned_rollback_keeps_original_and_replacement_atomic_then_records_each_existing_event_once()
    {
        await using var run = await Run.StartAsync(); var account = await ApprovedMovementAccount(run); var periods = new ActualPeriods();
        var input = ActualInput(account) with { Kind = CreditMovementKindV1.OpeningBalance }; Guid original;
        await using (var maker = run.OpenMovement(Maker, periods)) original = (await maker.Service.ProposeAsync(maker.Context, Key("owned-opening-propose"), input, default)).Resource!.Id;
        await using (var checker = new MovementDecisionScope(run)) Assert.Equal(CommandOutcome.Succeeded, (await checker.Decide(original, "owned-opening-approve", true)).Outcome);
        Guid replacement;
        await using (var maker = run.OpenMovement(Maker, periods)) replacement = (await maker.Service.ProposeAsync(maker.Context, Key("owned-correction-propose"), input with
        { ReplacesOpeningMovementId = original, ExpectedOpeningMovementVersion = "2", Components = [new("principal", "105.00", null)], SourceDocument = input.SourceDocument with { Revision = "2" } }, default)).Resource!.Id;
        var save = new MovementDecisionSave(replacement); await using var scope = new MovementDecisionScope(run, save: save); var deny = true;
        scope.Permission.Check = () => deny && save.Saved ? PermissionDecision.Denied : PermissionDecision.Allowed;
        Assert.Equal(CommandOutcome.Denied, (await scope.Decide(replacement, "owned-correction-approve", true)).Outcome); scope.AssertClean();
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.CreditMovements WHERE Id=@id", ("@id", original)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovements WHERE Id=@id AND ReversedByMovementId IS NOT NULL", ("@id", original)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.CreditMovements WHERE Id=@id", ("@id", replacement)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands WHERE Command=N'finance.credit-movement.decide' AND KeyHash=@key", ("@key", FinanceCommandKeys.Hash("owned-correction-approve"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement' AND EntityId=@id AND EventType=N'finance.credit-movement.approved.v1'", ("@id", replacement.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement' AND EntityId=@id AND EventType=N'finance.credit-opening.reversed.v1'", ("@id", original.ToString("D"))));
        deny = false; save.Saved = false; var first = await scope.Decide(replacement, "owned-correction-approve", true); var replay = await scope.Decide(replacement, "owned-correction-approve", true);
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome); Assert.True(replay.IsReplay); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(replacement, await run.ScalarAsync<Guid>("SELECT ReversedByMovementId FROM finance.CreditMovements WHERE Id=@id", ("@id", original)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement' AND EntityId=@id AND EventType=N'finance.credit-opening.reversed.v1'", ("@id", original.ToString("D"))));
        scope.AssertClean();
    }

    private static async Task<Guid> MovementSafetyPrepare(Run run)
    {
        var account = await ApprovedMovementAccount(run); await using var maker = run.OpenMovement(Maker);
        var answer = await maker.Service.ProposeAsync(maker.Context, Key("movement-safety-propose"), ActualInput(account), default);
        Assert.Equal(CommandOutcome.Succeeded, answer.Outcome); return answer.Resource!.Id;
    }
    private static async Task MovementSafetyDurable(Run run, Guid id, bool? approved)
    {
        Assert.Equal(approved is null ? 1L : 2L, await run.ScalarAsync<long>("SELECT Version FROM finance.CreditMovements WHERE CompanyId=@c AND Id=@id", ("@c", CompanyA), ("@id", id)));
        Assert.Equal(approved is null ? 1 : approved.Value ? 2 : 3, await run.ScalarAsync<int>("SELECT Status FROM finance.CreditMovements WHERE CompanyId=@c AND Id=@id", ("@c", CompanyA), ("@id", id)));
        Assert.Equal(approved is null ? 0 : 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands WHERE CompanyId=@c AND Command=N'finance.credit-movement.decide'", ("@c", CompanyA)));
        Assert.Equal(approved is null ? 0 : 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE CompanyId=@c AND EntityType=N'Finance.CreditMovement' AND EntityId=@id AND EventType IN (N'finance.credit-movement.approved.v1',N'finance.credit-movement.rejected.v1')", ("@c", CompanyA), ("@id", id.ToString("D"))));
        Assert.Equal(approved == true ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE CompanyId=@c AND AggregateId=@id", ("@c", CompanyA), ("@id", id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
    }
    private static AuditEventDraft MovementSafetyAudit(Guid id) => new(CompanyA, "finance.credit-movement.decide.v1", "Finance.CreditMovement", id.ToString("D"), "Decide", 1, 2,
        "User", Checker, ErpPermissions.FinanceCreditsApprove, [], "{}", "{}", "SENTETIK caller", null, AuditAiInvolvement.None, null, "Web", null, null, "SENTETIK-movement-caller", null, "SENTETIK");
    private sealed class MovementDecisionScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public MovementDecisionCompany Company { get; } = new(); public MovementDecisionUser User { get; } = new(); public MovementDecisionPermission Permission { get; } = new();
        public FinanceDbContext Db { get; } public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public IFinanceCreditMovementServiceV1 Service { get; } public string OriginalCorrelation { get; } = "movement-safety-" + Guid.NewGuid().ToString("N");
        public MovementDecisionScope(Run run, MovementDecisionSave? save = null, IInterceptor? extra = null, MovementDecisionCleanup? cleanup = null, bool missingWriter = false)
        {
            Company.Set(CompanyA); auditScope = run.Factory.Services.CreateScope(); auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            options.AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) options.AddInterceptors(save); if (extra is not null) options.AddInterceptors(extra);
            if (cleanup is not null) { options.AddInterceptors(cleanup); cleanup.Configure(options); }
            Db = new(options.Options, Company);
            var periods = new ActualPeriods();
            Service = new FinanceCreditMovementServiceV1(Db, Company, User, Permission, new SyntheticCreditSources(CompanyA), periods, periods,
                new InternalLedgerTreasuryBalanceGuard(Db, Company), Collector,
                new(new(ErpPermissions.FinanceCreditsPropose, ["SENTETIK"], "Web"), new(ErpPermissions.FinanceCreditsApprove, ["SENTETIK"], "Web")),
                TimeProvider.System, missingWriter ? null : Writer, FinanceCreditGroupGateV1.ApprovedFieldListsV12).WithApprovalSafety();
        }
        public Task<CreditMovementResultV1> Decide(Guid id, string key, bool approve) => Service.DecideAsync(new(CompanyA, Checker, OriginalCorrelation, null), new(key), new(id, "1", approve, approve ? null : "SENTETIK literal ret"), default);
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class MovementDecisionCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid value) => CompanyId = value; }
    private sealed class MovementDecisionUser : ICurrentUser
    { public string Actor = Checker; public bool Member = true; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class MovementDecisionPermission : IFinancePermissionPort
    { public Func<PermissionDecision>? Check; public Func<Task>? AfterRead;
      public async Task<PermissionDecision> CheckAsync(FinanceContext c, FinanceAction a, Guid? id, CancellationToken t)
      { Assert.Equal(FinanceAction.ApproveCredit, a); var answer = Check?.Invoke() ?? PermissionDecision.Allowed; if (AfterRead is not null) await AfterRead(); return answer; } }
    private sealed class MovementDecisionSave(Guid id) : SaveChangesInterceptor
    {
        public string? Fault; public bool Saved, Injected; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is FinanceDbContext db && db.ChangeTracker.Entries<FinanceCreditMovementRow>().Any(x => x.Entity.Id == id && x.State == EntityState.Modified))
            {
                Attempts++;
                if (Fault is not null)
                {
                    Injected = true;
                    if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                    if (Fault is "direct" or "completed-provider") throw SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (Fault == "wrapped") throw new DbUpdateException("SENTETIK 1205", SqlConcurrencyWrapperPolicyTests.SqlFailure(1205));
                    if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK movement save race");
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
    private sealed class MovementDecisionCleanup(bool dispose) : DbTransactionInterceptor
    {
        public bool Armed, Failed;
        public void Configure(DbContextOptionsBuilder<FinanceDbContext> options) => options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed,
            _ => { if (Armed && dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK dispose fault"); } });
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        { if (Armed && !dispose && !Failed) { Failed = true; throw new InvalidOperationException("SENTETIK rollback fault"); } return ValueTask.FromResult(result); }
    }
    private sealed class MovementDecisionCommitted : DbTransactionInterceptor
    { public int Commits; public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken token = default) { Commits++; return Task.CompletedTask; } }
}
