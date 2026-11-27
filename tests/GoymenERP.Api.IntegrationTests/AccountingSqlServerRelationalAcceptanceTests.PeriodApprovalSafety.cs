using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Both_period_approvals_roll_back_business_real_finance_participant_receipt_and_audit_on_late_refusal_then_same_key_once()
    {
        foreach (var reopen in new[] { false, true })
        foreach (var fault in new[] { "after-audit", "after-save", "precommit", "actor", "company", "member", "delegation", "cancel" })
        {
            await using var run = await Run.StartAsync(); var prepared = await PreparePeriodApproval(run, reopen);
            var save = new PeriodApprovalSave(prepared.Period); await using var scope = new PeriodApprovalScope(run, save);
            var deny = true; var savedFences = 0; var observed = false; var sawPending = false;
            using var cancellation = new CancellationTokenSource();
            scope.Authority.Read = () =>
            {
                var staged = scope.Db.ChangeTracker.Entries<PeriodRow>().Any(x => x.Entity.Id == prepared.Period && x.State == EntityState.Modified);
                if (staged && scope.Writer.HasPendingDrafts) sawPending = true;
                if (save.Saved) savedFences++;
                var reached = fault == "after-audit" ? staged && sawPending && !scope.Writer.HasPendingDrafts
                    : fault == "precommit" ? savedFences == 3 : save.Saved;
                if (!deny || !reached) return true;
                observed = true;
                if (fault == "actor") { scope.User.Actor = run.Preparer; return true; }
                if (fault == "company") { scope.Company.Set(CompanyB); return true; }
                if (fault == "member") { scope.User.Member = false; return true; }
                if (fault == "cancel") { cancellation.Cancel(); return true; }
                return false; // A controlled native authority/delegation refusal, not a full WP3 E2E.
            };
            if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Approve(prepared, "period-late", cancellation.Token));
            else Assert.Equal(AccountingApiStatus.Denied, (await scope.Approve(prepared, "period-late")).Status);
            Assert.True(observed); if (fault == "precommit") Assert.Equal(3, savedFences); // Save completion, native result, precommit.
            scope.AssertClean(); await AssertPeriodApprovalDurable(run, prepared, false);
            deny = false; scope.User.Actor = run.Actor; scope.User.Member = true; scope.Company.Set(CompanyA); save.Saved = false;
            var success = await scope.Approve(prepared, "period-late"); var replay = await scope.Approve(prepared, "period-late");
            Assert.Equal(AccountingApiStatus.Ok, success.Status); Assert.True(replay.Replay);
            Assert.Equal(success.Json, replay.Json); Assert.Equal(run.Correlation, replay.Correlation);
            scope.AssertClean(); await AssertPeriodApprovalDurable(run, prepared, true);
        }
    }

    [SqlServerFact]
    public async Task Both_period_approval_entries_preserve_borrowed_ambient_dirty_and_pending_caller_state()
    {
        foreach (var reopen in new[] { false, true })
        foreach (var fault in new[] { "borrowed", "ambient", "dirty", "pending", "await-dirty", "await-pending", "missing-writer" })
        {
            await using var run = await Run.StartAsync(); var prepared = await PreparePeriodApproval(run, reopen);
            await using var scope = new PeriodApprovalScope(run, missingWriter: fault == "missing-writer");
            await using var borrowed = fault == "borrowed" ? await scope.Db.Database.BeginTransactionAsync() : null;
            using var ambient = fault == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            var caller = NormalSafetyCaller(); if (fault == "dirty") scope.Db.Commands.Add(caller);
            if (fault == "pending") scope.Collector.Enqueue(NormalSafetyAudit(run));
            scope.Authority.Read = () =>
            {
                if (fault == "await-dirty" && !scope.Db.ChangeTracker.HasChanges()) scope.Db.Commands.Add(caller);
                if (fault == "await-pending" && !scope.Writer.HasPendingDrafts) scope.Collector.Enqueue(NormalSafetyAudit(run));
                return true;
            };
            Assert.Equal(AccountingApiStatus.Unavailable, (await scope.Approve(prepared, "period-entry")).Status);
            Assert.Equal(fault is "dirty" or "await-dirty", scope.Db.ChangeTracker.HasChanges());
            Assert.Equal(fault is "pending" or "await-pending", scope.Writer.HasPendingDrafts);
            if (scope.Db.ChangeTracker.HasChanges()) Assert.Same(caller, Assert.Single(scope.Db.ChangeTracker.Entries()).Entity);
            if (borrowed is not null) { Assert.Same(borrowed, scope.Db.Database.CurrentTransaction); await borrowed.RollbackAsync(); }
            else Assert.Null(scope.Db.Database.CurrentTransaction);
            // Test teardown only: production entry refusal did not clear/discard caller state.
            scope.Db.ChangeTracker.Clear(); scope.Writer.DiscardPending(); await AssertPeriodApprovalDurable(run, prepared, false);
        }
    }

    [SqlServerFact]
    public async Task Both_period_approvals_abort_before_one_readonly_original_key_winner_pass_without_mutation_retry()
    {
        foreach (var reopen in new[] { false, true })
        foreach (var fault in new[] { "concurrency", "direct", "wrapped", "completed-provider" })
        foreach (var winner in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var prepared = await PreparePeriodApproval(run, reopen);
            var save = new PeriodApprovalSave(prepared.Period) { Fault = fault }; await using var scope = new PeriodApprovalScope(run, save);
            var transactions = new HashSet<Guid>(); var won = false;
            scope.Authority.Read = () => { if (scope.Db.Database.CurrentTransaction is { } tx) transactions.Add(tx.TransactionId); return true; };
            scope.Authority.BeforeRead = async () =>
            {
                if (!save.Injected || scope.Db.Database.CurrentTransaction is not null || won || !winner) return;
                scope.AssertClean(); won = true;
                await using var other = new PeriodApprovalScope(run);
                Assert.Equal(AccountingApiStatus.Ok, (await other.Approve(prepared, "period-race")).Status);
            };
            var result = await scope.Approve(prepared, "period-race"); Assert.True(save.Injected);
            Assert.Equal(1, save.Attempts); Assert.Equal(2, transactions.Count); Assert.Equal(winner, result.Replay);
            Assert.Equal(winner ? AccountingApiStatus.Ok : AccountingApiStatus.Conflict, result.Status);
            if (!winner) Assert.Equal(AccountingStoreCodes.ConcurrentChange, result.Code);
            scope.AssertClean(); await AssertPeriodApprovalDurable(run, prepared, winner);
        }
    }

    [SqlServerFact]
    public async Task Both_guarded_period_approvals_preserve_the_native_maker_refusal_code_and_field_without_receipt_or_audit()
    {
        foreach (var reopen in new[] { false, true })
        {
            await using var run = await Run.StartAsync(); var prepared = await PreparePeriodApproval(run, reopen);
            await using var scope = new PeriodApprovalScope(run); scope.User.Actor = run.Preparer;
            if (prepared.Proposal is { } proposal)
            {
                var answer = await scope.Service.ApprovePeriodReopeningAsync(prepared.Period, proposal,
                    new(1, prepared.ExpectedPeriodVersion), "period-maker", default);
                Assert.Equal(AccountingApiStatus.Denied, answer.Status);
                Assert.Equal(GoymenERP.Modules.Accounting.Domain.AccountingErrorCodes.PeriodSelfApproval, answer.Code);
                Assert.Equal("approval", answer.Field); Assert.Null(answer.Resource);
            }
            else
            {
                var answer = await scope.Service.ApprovePeriodAsync(prepared.Period, new(prepared.ExpectedPeriodVersion), "period-maker", default);
                Assert.Equal(AccountingApiStatus.Denied, answer.Status);
                Assert.Equal(GoymenERP.Modules.Accounting.Domain.AccountingErrorCodes.PeriodSelfApproval, answer.Code);
                Assert.Equal("approval", answer.Field); Assert.Null(answer.Resource);
            }
            scope.AssertClean(); await AssertPeriodApprovalDurable(run, prepared, false);
        }
    }

    private sealed record PreparedPeriodApproval(Guid Period, Guid? Proposal, long ExpectedPeriodVersion)
    { public string Command => Proposal is null ? AccountingCommandNames.ApprovePeriod : "accounting.period.reopening.approve.v1"; }
    private static async Task<PreparedPeriodApproval> PreparePeriodApproval(Run run, bool reopen)
    {
        var id = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
            await store.Periods.ProposePeriodAsync(id, new(2026, 9, 1), new(2026, 9, 30), run.Preparer,
                Metadata(run) with { ActorId = run.Preparer.ToString("D"), PermissionCode = AccountingPermissionManifestV1.PeriodsPropose });
        if (!reopen) return new(id, null, 1);
        await using (var store = run.Store(CompanyA))
            await store.Periods.ApprovePeriodAsync(id, 1, run.Actor, Metadata(run));
        await AccountingPeriodCarrySqlFixture.PrepareAsync(run.Factory, CompanyA, id, run.Actor);
        await AccountingPeriodCarrySqlFixture.CloseAsync(run.Factory, CompanyA, id, 2, run.Actor, Metadata(run));
        var proposal = Guid.NewGuid();
        await using (var store = run.Store(CompanyA))
            await ((IAccountingPeriodReopeningStoreV1)store.Periods).ProposeReopeningAsync(id, new(3, proposal, "SENTETIK reversal reason"),
                run.Preparer, Metadata(run) with { ActorId = run.Preparer.ToString("D"), PermissionCode = AccountingPermissionManifestV1.PeriodsPropose }, default);
        return new(id, proposal, 3);
    }
    private static async Task AssertPeriodApprovalDurable(Run run, PreparedPeriodApproval value, bool approved)
    {
        Assert.Equal(value.ExpectedPeriodVersion + (approved ? 1 : 0), await run.ScalarAsync<long>("SELECT Version FROM accounting.Periods WHERE Id=@id", ("@id", value.Period)));
        Assert.Equal(approved ? (int)AccountingPeriodStatus.Open : value.Proposal is null ? (int)AccountingPeriodStatus.Proposed : (int)AccountingPeriodStatus.Closed,
            await run.ScalarAsync<int>("SELECT Status FROM accounting.Periods WHERE Id=@id", ("@id", value.Period)));
        Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Commands WHERE CommandName=@n", ("@n", value.Command)));
        var eventType = value.Proposal is null ? "accounting.period.approved.v1" : "accounting.period.reopened.v1";
        var entity = value.Proposal ?? value.Period;
        Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=@n", ("@id", entity.ToString("D")), ("@n", eventType)));
        if (value.Proposal is { } proposal)
        {
            Assert.Equal(approved ? 2L : 1L, await run.ScalarAsync<long>("SELECT Version FROM accounting.PeriodReopenings WHERE Id=@id", ("@id", proposal)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryVouchers"));
            Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryReversals"));
            Assert.Equal(approved ? 1 : 0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'finance.period-carry.reversed.v1'", ("@id", proposal.ToString("D"))));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        }
    }
    private sealed record PeriodApprovalAnswer(AccountingApiStatus Status, bool Replay, string? Code, string? Correlation, string Json);
    private sealed class PeriodApprovalScope : IAsyncDisposable
    {
        private readonly IServiceScope auditScope;
        public NormalApprovalCompany Company { get; } = new(); public NormalApprovalUser User { get; }
        public PeriodApprovalAuthority Authority { get; } = new(); public AccountingDbContext Db { get; }
        public AuditEventCollector Collector { get; } public IAuditTransactionWriterV1 Writer { get; }
        public AccountingApiService Service { get; }
        public PeriodApprovalScope(Run run, PeriodApprovalSave? save = null, bool missingWriter = false)
        {
            Company.Set(CompanyA); User = new(run.Actor); auditScope = run.Factory.Services.CreateScope();
            auditScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            Collector = auditScope.ServiceProvider.GetRequiredService<AuditEventCollector>(); Writer = auditScope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var builder = new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(Collector, Company));
            if (save is not null) builder.AddInterceptors(save); Db = new(builder.Options, Company);
            var core = new AccountingStore(Db, Company, Collector, TimeProvider.System, new AccountingAmountScaleVerifier(new SyntheticMoneyProfiles(CompanyA)),
                AccountingPeriodCarrySqlFixture.Port(Db, Company, Collector, Writer, run.Actor), missingWriter ? null : Writer);
            var correlation = new CorrelationContext(); correlation.Set(run.Correlation, null, "Web", null, null);
            Service = new(core, core, core, Authority, Company, User, correlation);
        }
        public async Task<PeriodApprovalAnswer> Approve(PreparedPeriodApproval value, string key, CancellationToken token = default)
        {
            if (value.Proposal is { } id)
            {
                var result = await Service.ApprovePeriodReopeningAsync(value.Period, id, new(1, value.ExpectedPeriodVersion), key, token);
                return new(result.Status, result.IsReplay, result.Code, result.OriginalCorrelationId, JsonSerializer.Serialize(result.Resource));
            }
            var opening = await Service.ApprovePeriodAsync(value.Period, new(value.ExpectedPeriodVersion), key, token);
            return new(opening.Status, opening.IsReplay, opening.Code, opening.OriginalCorrelationId, JsonSerializer.Serialize(opening.Resource));
        }
        public void AssertClean() { Assert.False(Db.ChangeTracker.HasChanges()); Assert.False(Writer.HasPendingDrafts); Assert.Null(Db.Database.CurrentTransaction); }
        public async ValueTask DisposeAsync() { try { await Db.DisposeAsync(); } finally { auditScope.Dispose(); } }
    }
    private sealed class PeriodApprovalAuthority : IErpAuthorityRevalidationService
    {
        public Func<bool>? Read; public Func<Task>? BeforeRead;
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        {
            Assert.Contains(permission, new[] { AccountingPermissionManifestV1.PeriodsApprove, AccountingPermissionManifestV1.PeriodsReopen });
            if (BeforeRead is not null) await BeforeRead();
            return new(Read?.Invoke() ?? true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        }
    }
    private sealed class PeriodApprovalSave(Guid id) : SaveChangesInterceptor
    {
        public bool Saved, Injected; public string? Fault; public int Attempts;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (data.Context is AccountingDbContext db && db.ChangeTracker.Entries<PeriodRow>().Any(x => x.Entity.Id == id && x.State == EntityState.Modified))
            {
                Attempts++;
                if (!Injected && Fault is not null)
                {
                    Injected = true; if (Fault == "completed-provider") db.Database.CurrentTransaction!.GetDbTransaction().Rollback();
                    if (Fault == "concurrency") throw new DbUpdateConcurrencyException("SENTETIK period approval race");
                    var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(1205);
                    if (Fault == "wrapped") throw new DbUpdateException("SENTETIK controlled 1205", error); throw error;
                }
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Attempts > 0) Saved = true; return ValueTask.FromResult(result); }
    }
}
