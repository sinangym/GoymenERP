using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Exchange_book_value_SQL_guards_reject_mutation_and_lossy_Down_but_allow_an_empty_store()
    {
        await using var run = await Run.StartAsync();
        var source = await run.RecordBookValueExchangeAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCashExchangeBookValues().DownOperations[0]).Sql;
        // An unrelated real FX transfer is not book-value evidence.
        await run.ExecuteAsync(guard);
        CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker"))
            pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-guards"), BookRequest(source), default)).Resource!;
        Assert.Equal(51915, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(51912, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CashExchangeBookValueProposals SET TransferSourceHash=REPLICATE('c',64) WHERE Id=@id", ("@id", pending.Id)))).Number);
        Assert.Equal(51912, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "DELETE FROM finance.CashExchangeBookValueProposals WHERE Id=@id", ("@id", pending.Id)))).Number);
        await using (var checker = run.OpenBookValue("SENTETIK-book-checker"))
            Assert.Equal(CommandOutcome.Succeeded, (await checker.Service.DecideAsync(checker.Context, Key("book-guards-decision"), new(pending.Id, "1", true, null), default)).Outcome);
        Assert.Equal(51913, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CashExchangeBookValueDecisions SET DecidedAtUtc=DATEADD(second,1,DecidedAtUtc) WHERE ProposalId=@id", ("@id", pending.Id)))).Number);
        Assert.Equal(51913, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "DELETE FROM finance.CashExchangeBookValueDecisions WHERE ProposalId=@id", ("@id", pending.Id)))).Number);
        Assert.Equal(51914, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CashExchangeBookValueCommands SET ResultJson=N'{}'"))).Number);
        Assert.Equal(51914, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "DELETE FROM finance.CashExchangeBookValueCommands"))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueProposals"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
    }

    /// <summary>Real SQL/Audit owner, synthetic authority and document proof; no browser, grant or external ledger assertion.</summary>
    [SqlServerFact]
    public async Task Exchange_book_value_distinct_checker_and_original_replay_do_not_move_money_or_requote_TCMB()
    {
        await using var run = await Run.StartAsync();
        var source = await run.RecordBookValueExchangeAsync();
        var request = BookRequest(source);
        CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker"))
        {
            var result = await maker.Service.ProposeAsync(maker.Context, Key("book-propose"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            pending = Assert.IsType<CashExchangeBookValueViewV1>(result.Resource);
            var self = await maker.Service.DecideAsync(maker.Context, Key("book-self"), new(pending.Id, "1", true, null), default);
            Assert.Equal(CommandOutcome.ValidationFailed, self.Outcome);
        }
        await using (var foreign = run.OpenBookValue("SENTETIK-book-checker", CompanyB))
        {
            var hidden = await foreign.Service.GetAsync(foreign.Context, source.Id, default);
            Assert.Equal(ReadOutcome.NotFound, hidden.Outcome); Assert.Null(hidden.Resource);
        }
        await using (var checker = run.OpenBookValue("SENTETIK-book-checker"))
        {
            var approved = await checker.Service.DecideAsync(checker.Context, Key("book-approve"), new(pending.Id, "1", true, null), default);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal(("3500.00", CashExchangeBookValueStatusV1.Approved), (approved.Resource!.SourceBookValueTl, approved.Resource.Status));
            var replay = await checker.Service.DecideAsync(checker.Context, Key("book-approve"), new(pending.Id, "1", true, null), default);
            Assert.True(replay.IsReplay); Assert.Equal(approved.Resource, replay.Resource);
        }
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker", documentOutcome: ReadOutcome.Unavailable))
        {
            var replay = await maker.Service.ProposeAsync(maker.Context, Key("book-propose"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome); Assert.True(replay.IsReplay);
            Assert.Equal(pending, replay.Resource); // The original pending response is immutable after approval.
            Assert.Equal(CommandOutcome.IdempotencyConflict, (await maker.Service.ProposeAsync(maker.Context, Key("book-propose"),
                request with { SourceBookValueTl = "3400.00" }, default)).Outcome);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashTransfers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueProposals"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CashExchangeBookValue'"));
        var json = await run.ScalarAsync<string>("SELECT PayloadJson FROM finance.OutboxMessages WHERE EventType=N'finance.cash-exchange.book-value-approved.v1'");
        var payload = JsonSerializer.Deserialize<FinanceCashExchangeBookValueApprovedV1>(json, FinanceJson.CreateOptions())!;
        Assert.Equal("3500.00", payload.BookValue.SourceBookValueTl);
        Assert.Equal("3400", payload.Source.Exchange!.SourceReferenceTl!.Amount);
        Assert.Equal(source, payload.Source);
        Assert.Equal(TreasuryBalanceOutcome.Sufficient, (await run.ExchangeBalanceAsync(source.Preparation.SourceAccountId, ExchangeUsd, "50")).Outcome);
        Assert.Equal(TreasuryBalanceOutcome.Insufficient, (await run.ExchangeBalanceAsync(source.Preparation.SourceAccountId, ExchangeUsd, "50.01")).Outcome);
    }

    [SqlServerFact]
    public async Task Exchange_book_value_failure_after_real_Audit_append_rolls_back_decision_event_and_key()
    {
        await using var run = await Run.StartAsync();
        var source = await run.RecordBookValueExchangeAsync();
        CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker"))
            pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-prepare"), BookRequest(source), default)).Resource!;
        await using (var fail = run.OpenBookValue("SENTETIK-book-checker", failAfterAudit: true))
            Assert.Equal(CommandOutcome.Unavailable, (await fail.Service.DecideAsync(fail.Context, Key("book-check"), new(pending.Id, "1", true, null), default)).Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CashExchangeBookValue'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE EventType=N'finance.cash-exchange.book-value-approved.v1'"));
        await using var retry = run.OpenBookValue("SENTETIK-book-checker");
        Assert.Equal(CommandOutcome.Succeeded, (await retry.Service.DecideAsync(retry.Context, Key("book-check"), new(pending.Id, "1", true, null), default)).Outcome);
    }

    [SqlServerFact]
    public async Task Exchange_book_value_permission_loss_after_real_save_rolls_back_and_same_key_remains_retryable()
    {
        await using var run = await Run.StartAsync();
        var source = await run.RecordBookValueExchangeAsync();
        var authority = new BookAuthority();
        await using (var lost = run.OpenBookValue("SENTETIK-book-maker", authority: authority, interceptors: [new BookSavedLoss(() => authority.Granted = false)]))
            Assert.Equal(CommandOutcome.Denied, (await lost.Service.ProposeAsync(lost.Context, Key("book-late-loss"), BookRequest(source), default)).Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueProposals"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueCommands"));
        await using var retry = run.OpenBookValue("SENTETIK-book-maker");
        Assert.Equal(CommandOutcome.Succeeded, (await retry.Service.ProposeAsync(retry.Context, Key("book-late-loss"), BookRequest(source), default)).Outcome);
    }

    [SqlServerFact]
    public async Task Exchange_book_value_two_SQL_checkers_cannot_approve_one_economic_source_twice()
    {
        await using var run = await Run.StartAsync();
        var source = await run.RecordBookValueExchangeAsync(); CashExchangeBookValueViewV1 pending;
        await using (var maker = run.OpenBookValue("SENTETIK-book-maker"))
            pending = (await maker.Service.ProposeAsync(maker.Context, Key("book-race-p"), BookRequest(source), default)).Resource!;
        using var barrier = new Barrier(2);
        await using var first = run.OpenBookValue("SENTETIK-checker-A", interceptors: [new BarrierInterceptor(barrier)]);
        await using var second = run.OpenBookValue("SENTETIK-checker-B", interceptors: [new BarrierInterceptor(barrier)]);
        var replies = await Task.WhenAll(Task.Run(() => first.Service.DecideAsync(first.Context, Key("book-race-A"), new(pending.Id, "1", true, null), default)),
            Task.Run(() => second.Service.DecideAsync(second.Context, Key("book-race-B"), new(pending.Id, "1", true, null), default)));
        Assert.Single(replies, x => x.Outcome == CommandOutcome.Succeeded);
        Assert.All(replies.Where(x => x.Outcome != CommandOutcome.Succeeded), x => Assert.Contains(x.Outcome, new[] { CommandOutcome.Unavailable, CommandOutcome.VersionConflict }));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashExchangeBookValueDecisions WHERE Approved=1"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE EventType=N'finance.cash-exchange.book-value-approved.v1'"));
    }

    private static ProposeCashExchangeBookValueV1 BookRequest(CashTransferView source) => new(source.Id, source.Version,
        "3500.00", new(Guid.Parse("f1400000-0000-4000-8000-000000000007"), 1, new string('b', 64)), "SENTETIK kayıtlı defter belgesi");

    private sealed partial class Run
    {
        public async Task<CashTransferView> RecordBookValueExchangeAsync()
        {
            var bank = await ApprovedExchangeBankAsync(ExchangeUsd); var target = await ApprovedExchangeBankAsync(ExchangeTry);
            await FundExchangeBankAsync(bank, "150");
            await using var exchange = OpenExchange(CompanyA, new SyntheticExchangeComparison());
            var result = await exchange.Service.CommitAsync(exchange.Context, Key("book-actual-exchange"), ExchangeRequest(bank, target), default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome); return Assert.IsType<CashTransferView>(result.Resource);
        }
        public BookScope OpenBookValue(string actor, Guid? companyId = null, ReadOutcome documentOutcome = ReadOutcome.Found,
            bool failAfterAudit = false, BookAuthority? authority = null, IInterceptor[]? interceptors = null)
        {
            var scope = Factory.Services.CreateAsyncScope();
            var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(companyId ?? CompanyA);
            var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, x => x.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(interceptors ?? []).Options, company);
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            if (failAfterAudit) writer = new FailAfterActualBookAudit(writer);
            var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null);
            var context = new FinanceContext(companyId ?? CompanyA, actor, Correlation, null);
            var service = new CashExchangeBookValueServiceV1(db, company, new FixedCurrentUser(actor, context.CompanyId),
                authority ?? new BookAuthority(), new SyntheticBookEvidence(documentOutcome),
                scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(), writer, correlation, TimeProvider.System);
            return new(scope, db, context, service);
        }
    }
    private sealed record BookScope(AsyncServiceScope Scope, FinanceDbContext Db, FinanceContext Context,
        CashExchangeBookValueServiceV1 Service) : IAsyncDisposable
    { public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Scope.DisposeAsync(); } }
    private sealed class BookAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Granted ? new PermissionScope(true, true, new HashSet<string> { "SENTETIK-accountant" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
    private sealed class SyntheticBookEvidence(ReadOutcome answer) : ICashExchangeBookValueEvidencePortV1
    { public Task<ReadOutcome> CheckAsync(FinanceContext context, CashExchangeBookValueEvidenceV1 evidence, CancellationToken cancellationToken) => Task.FromResult(answer); }
    private sealed class FailAfterActualBookAudit(IAuditTransactionWriterV1 actual) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => actual.HasPendingDrafts;
        public void DiscardPending() => actual.DiscardPending();
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { await actual.WritePendingAsync(transaction, cancellationToken); throw new InvalidOperationException("SENTETIK rollback after actual owner append"); }
    }
    private sealed class BookSavedLoss(Action lose) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { lose(); return ValueTask.FromResult(result); }
    }
}
