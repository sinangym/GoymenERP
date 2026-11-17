using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Refusing_a_weaker_borrowed_transaction_preserves_the_callers_tracking_and_pending_audit() =>
        AssertBorrowedRefusalPreservesCallerAsync(IsolationLevel.ReadCommitted, pendingAudit: false);

    [SqlServerFact]
    public Task Refusing_a_protected_attempt_with_existing_audit_preserves_the_callers_pending_batch() =>
        AssertBorrowedRefusalPreservesCallerAsync(IsolationLevel.Serializable, pendingAudit: true);

    private static async Task AssertBorrowedRefusalPreservesCallerAsync(IsolationLevel isolation, bool pendingAudit)
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK borrowed bank");
        await using var scope = run.OpenLedger(CompanyA, Recorder);
        var db = scope.Db;
        var tracked = await db.BankAccounts.SingleAsync(row => row.Id == bank);
        await using var caller = await db.Database.BeginTransactionAsync(isolation);
        var audit = new UntouchedCallerAudit(pendingAudit);
        var operationCalled = false;

        var answer = await FinanceTreasuryTransaction.ExecuteAsync(db, () =>
        {
            operationCalled = true;
            return Task.FromResult("unexpected");
        }, () => Task.FromResult("unavailable"), audit, CancellationToken.None);

        Assert.Equal("unavailable", answer);
        Assert.False(operationCalled);
        Assert.Same(caller, db.Database.CurrentTransaction);
        Assert.Contains(db.ChangeTracker.Entries<BankAccountRow>(), entry => ReferenceEquals(entry.Entity, tracked));
        Assert.Equal(pendingAudit, audit.HasPendingDrafts);
        Assert.Equal(0, audit.DiscardCalls);
        Assert.Equal(42, await db.Database.SqlQuery<int>($"SELECT CAST(42 AS int) AS [Value]").SingleAsync());
        await caller.CommitAsync();
    }

    private sealed class UntouchedCallerAudit(bool pending) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; private set; } = pending;
        public int DiscardCalls { get; private set; }
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The refused Finance attempt must not write the caller's audit.");
        public void DiscardPending() { DiscardCalls++; HasPendingDrafts = false; }
    }

    /// <summary>Real internal ledger and real SQL transaction; permission, profile and period remain explicit synthetic seams.</summary>
    [SqlServerFact]
    public async Task Internal_ledger_serializes_two_outgoing_records_and_same_key_retry_never_overdraws_the_approved_opening()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK ledger bank");
        await run.FundBankAsync(bank, "2000");
        var first = await run.ApprovedExpenseAsync(bank, "ledger-1");
        var second = await run.ApprovedExpenseAsync(bank, "ledger-2");
        var barrier = new TwoSufficientReads();
        var requests = new[] { first, second };
        var results = await Task.WhenAll(requests.Select(async (item, index) =>
        {
            await using var scope = run.OpenLedger(CompanyA, Recorder, guard => new BarrierLedgerGuard(guard, barrier));
            return await scope.Payments.RecordAsync(scope.Context, Key("ledger-r" + index),
                new RecordExpensePaymentRequest(item.Id, item.Version, true), CancellationToken.None);
        }));

        Assert.Equal(1, results.Count(result => result.Outcome == CommandOutcome.Succeeded));
        var loser = Array.FindIndex(results, result => result.Outcome != CommandOutcome.Succeeded);
        Assert.Contains(results[loser].Outcome, new[] { CommandOutcome.Unavailable, CommandOutcome.ValidationFailed });
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePayments WHERE TreasuryAccountId=@bank AND Status=2", ("@bank", bank)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CommandName=@command AND IdempotencyKeyHash=@hash",
            ("@command", ExpensePaymentService.RecordCommand), ("@hash", FinanceCommandKeys.Hash("ledger-r" + loser))));

        await using (var retry = run.OpenLedger(CompanyA, Recorder))
        {
            var refused = await retry.Payments.RecordAsync(retry.Context, Key("ledger-r" + loser),
                new RecordExpensePaymentRequest(requests[loser].Id, requests[loser].Version, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Contains(refused.Issues, issue => issue.Code == TreasuryBalanceAnswer.InsufficientCode);
            Assert.False(refused.IsReplay);
        }
        var winner = 1 - loser;
        await using (var replay = run.OpenLedger(CompanyA, Recorder))
        {
            var again = await replay.Payments.RecordAsync(replay.Context, Key("ledger-r" + winner),
                new RecordExpensePaymentRequest(requests[winner].Id, requests[winner].Version, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, again.Outcome);
            Assert.True(again.IsReplay);
        }
        Assert.Equal(TreasuryBalanceOutcome.Sufficient, (await run.ProtectedBalanceAsync(bank, "749.50")).Outcome);
        Assert.Equal(TreasuryBalanceOutcome.Insufficient, (await run.ProtectedBalanceAsync(bank, "749.51")).Outcome);
    }

    [SqlServerFact]
    public async Task Reversing_a_spent_bank_opening_is_refused_without_changing_the_opening_or_consuming_the_approval_key()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK reversal bank");
        var opening = await run.FundBankAsync(bank, "2000");
        var expense = await run.ApprovedExpenseAsync(bank, "spent");
        await using (var recorder = run.OpenLedger(CompanyA, Recorder))
        {
            var recorded = await recorder.Payments.RecordAsync(recorder.Context, Key("spent-record"),
                new RecordExpensePaymentRequest(expense.Id, expense.Version, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
        }
        OpeningBalanceView reversal;
        await using (var proposer = run.OpenLedgerOpenings(Preparer))
        {
            var proposed = await proposer.Service.ProposeReversalAsync(proposer.Context, Key("spent-reversal-propose"),
                new OpeningBalanceReversalProposeRequest(opening.Id, opening.Version, "SENTETIK correction"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
            reversal = proposed.Resource!;
        }
        await using (var approver = run.OpenLedgerOpenings(Approver))
        {
            var refused = await approver.Service.ApproveReversalAsync(approver.Context, Key("spent-reversal-approve"),
                new OpeningBalanceReversalApproveRequest(opening.Id, reversal.Reversal!.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Contains(refused.Issues, issue => issue.Code == TreasuryBalanceAnswer.InsufficientCode);
        }
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM finance.OpeningBalances WHERE Id=@id", ("@id", opening.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.OpeningBalanceReversals WHERE OpeningBalanceId=@id", ("@id", opening.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceMovements WHERE OpeningBalanceId=@id AND Kind=2", ("@id", opening.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceCommands WHERE CommandName=@command AND IdempotencyKeyHash=@hash",
            ("@command", FinanceOpeningBalanceService.ApproveReversalCommand), ("@hash", FinanceCommandKeys.Hash("spent-reversal-approve"))));
    }

    [SqlServerFact]
    public Task Partial_SQL_save_failure_rolls_back_the_ledger_audit_and_key_before_retry() =>
        AssertPartialSaveRollbackAsync(mars: false);

    [SqlServerFact]
    public Task Partial_SQL_save_failure_rolls_back_the_ledger_audit_and_key_even_with_MARS() =>
        AssertPartialSaveRollbackAsync(mars: true);

    private static async Task AssertPartialSaveRollbackAsync(bool mars)
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK partial-save bank");
        await run.FundBankAsync(bank, "2000");
        var expense = await run.ApprovedExpenseAsync(bank, "partial-save");
        var fault = new FailAfterRealExpenseUpdate();
        await using (var failing = run.OpenLedger(CompanyA, Recorder, multipleActiveResultSets: mars, extra: [fault]))
        {
            var failed = await failing.Payments.RecordAsync(failing.Context, Key("partial-save-record"),
                new RecordExpensePaymentRequest(expense.Id, expense.Version, true), CancellationToken.None);
            Assert.True(fault.Fired);
            Assert.Equal(IsolationLevel.Serializable, fault.Isolation);
            Assert.Equal(mars, fault.Mars);
            Assert.Equal(CommandOutcome.Unavailable, failed.Outcome);
        }
        Assert.Equal(ExpensePaymentStatuses.Approved, await run.ScalarAsync<int>("SELECT Status FROM finance.ExpensePayments WHERE Id=@id", ("@id", expense.Id)));
        Assert.Equal(expense.Version, (await run.ScalarAsync<long>("SELECT Version FROM finance.ExpensePayments WHERE Id=@id", ("@id", expense.Id)))
            .ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ExpensePaymentCommands WHERE CommandName=@command AND IdempotencyKeyHash=@hash",
            ("@command", ExpensePaymentService.RecordCommand), ("@hash", FinanceCommandKeys.Hash("partial-save-record"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.expense-payment.recorded.v1' AND EntityId=@id",
            ("@id", expense.Id.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id",
            ("@id", expense.Id)));
        Assert.Equal(TreasuryBalanceOutcome.Sufficient, (await run.ProtectedBalanceAsync(bank, "2000")).Outcome);
        await using (var retry = run.OpenLedger(CompanyA, Recorder))
        {
            var saved = await retry.Payments.RecordAsync(retry.Context, Key("partial-save-record"),
                new RecordExpensePaymentRequest(expense.Id, expense.Version, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
            Assert.False(saved.IsReplay);
        }
        await using (var replay = run.OpenLedger(CompanyA, Recorder))
        {
            var saved = await replay.Payments.RecordAsync(replay.Context, Key("partial-save-record"),
                new RecordExpensePaymentRequest(expense.Id, expense.Version, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, saved.Outcome);
            Assert.True(saved.IsReplay);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.expense-payment.recorded.v1' AND EntityId=@id",
            ("@id", expense.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id",
            ("@id", expense.Id)));
    }

    /// <summary>MaxBatchSize(1) ensures the real canonical debit UPDATE executes before this injected EF failure.</summary>
    private sealed class FailAfterRealExpenseUpdate : DbCommandInterceptor
    {
        public bool Fired { get; private set; }
        public IsolationLevel? Isolation { get; private set; }
        public bool Mars { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("UPDATE [finance].[ExpensePayments]", StringComparison.Ordinal))
            {
                Fired = true;
                Isolation = command.Transaction?.IsolationLevel;
                Mars = new SqlConnectionStringBuilder(command.Connection!.ConnectionString).MultipleActiveResultSets;
                await result.DisposeAsync();
                throw new DbUpdateConcurrencyException("SENTETIK fault after the real expense debit UPDATE");
            }
            return result;
        }
    }

    private sealed class TwoSufficientReads
    {
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;
        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref count) == 2) both.TrySetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private sealed class BarrierLedgerGuard(IFinanceTreasuryBalanceGuard real, TwoSufficientReads barrier)
        : IFinanceTreasuryBalanceGuard
    {
        public async Task<TreasuryBalanceAnswer> CheckOutgoingAsync(FinanceContext context, ExpensePaymentSourceKind kind,
            Guid accountId, MoneyText money, CancellationToken cancellationToken)
        {
            var answer = await real.CheckOutgoingAsync(context, kind, accountId, money, cancellationToken);
            if (answer.Outcome == TreasuryBalanceOutcome.Sufficient) await barrier.ArriveAsync(cancellationToken);
            return answer;
        }
    }

    private sealed partial class Run
    {
        public Scope OpenLedger(Guid company, string actor,
            Func<IFinanceTreasuryBalanceGuard, IFinanceTreasuryBalanceGuard>? decorate = null,
            bool multipleActiveResultSets = false, params IInterceptor[] extra)
        {
            var companyContext = new CompanyContext(); companyContext.Set(company);
            var collector = new AuditEventCollector();
            var connectionString = new SqlConnectionStringBuilder(Factory.DatabaseConnectionString)
                { MultipleActiveResultSets = multipleActiveResultSets }.ConnectionString;
            var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(connectionString, sql => { sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema); sql.MaxBatchSize(1); })
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).AddInterceptors(extra).Options, companyContext);
            IFinanceTreasuryBalanceGuard guard = new InternalLedgerTreasuryBalanceGuard(db, companyContext);
            var service = new ExpensePaymentService(db, companyContext, new FixedCurrentUser(actor, company), new SyntheticPermissions(),
                new SingleExpenseItemCatalog(), decorate?.Invoke(guard) ?? guard, new SyntheticSettings(company),
                new SyntheticOpenPeriod(), collector, new ExpensePaymentBinding(FinanceMoneyOperations.ManualBankPaymentV1),
                new ExpensePaymentAuditEnvelopes(Envelope(FinancePermissionNames.ExpensePaymentDraftSave),
                    Envelope(FinancePermissionNames.ExpensePaymentApprove), Envelope(FinancePermissionNames.ExpensePaymentRecord)), TimeProvider.System);
            return new Scope(db, service, new FinanceContext(company, actor, Correlation, null));
        }

        public LedgerOpeningScope OpenLedgerOpenings(string actor)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var user = new FixedCurrentUser(actor, CompanyA);
            var collector = new AuditEventCollector();
            var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new FinanceOpeningBalanceService(db, company, user, new SyntheticPermissions(),
                new UnavailableSettingsPrerequisiteProvider(company, user), new UnusedOpeningPartyPort(), new SyntheticOpeningPeriod(),
                collector, new FinanceOpeningBalanceEnvelopes(Envelope(ErpPermissions.FinanceOpeningBalancesPropose),
                    Envelope(ErpPermissions.FinanceOpeningBalancesApprove)), TimeProvider.System,
                new InternalLedgerTreasuryBalanceGuard(db, company));
            return new LedgerOpeningScope(db, service, new FinanceContext(CompanyA, actor, Correlation, null));
        }

        public async Task<OpeningBalanceView> FundBankAsync(Guid bank, string amount)
        {
            OpeningBalanceView proposed;
            await using (var maker = OpenLedgerOpenings(Preparer))
            {
                var result = await maker.Service.ProposeAsync(maker.Context, Key("ledger-opening-p"),
                    new OpeningBalanceProposeRequest(OpeningBalanceTargetKind.BankAccount, bank, null, Currency, null, amount, OccurredOn), CancellationToken.None);
                Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
                proposed = result.Resource!;
            }
            await using var checker = OpenLedgerOpenings(Approver);
            var approved = await checker.Service.ApproveAsync(checker.Context, Key("ledger-opening-a"),
                new OpeningBalanceApproveRequest(proposed.Id, proposed.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            return approved.Resource!;
        }

        public async Task<ExpensePaymentView> ApprovedExpenseAsync(Guid bank, string key)
        {
            var draft = await DraftAsync(CompanyA, bank, key + "-d");
            await using var scope = OpenLedger(CompanyA, Approver);
            var result = await scope.Payments.ApproveAsync(scope.Context, Key(key + "-a"),
                new ApproveExpensePaymentRequest(draft.Id, draft.Version), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            return result.Resource!;
        }

        public async Task<TreasuryBalanceAnswer> ProtectedBalanceAsync(Guid bank, string amount)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            await using var db = new FinanceDbContext(Options(Factory), company);
            return await FinanceTreasuryTransaction.ExecuteAsync(db,
                () => new InternalLedgerTreasuryBalanceGuard(db, company).CheckOutgoingAsync(
                    new FinanceContext(CompanyA, Recorder, Correlation, null), ExpensePaymentSourceKind.Bank, bank,
                    new MoneyText(amount, Currency), CancellationToken.None),
                () => Task.FromResult(new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Unavailable, TreasuryBalanceAnswer.UnavailableCode)),
                auditOwner: null, CancellationToken.None);
        }
    }

    private sealed record LedgerOpeningScope(FinanceDbContext Db, FinanceOpeningBalanceService Service, FinanceContext Context) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class SyntheticOpeningPeriod : IFinanceOpeningPeriodReader
    {
        public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceOpeningPeriodAnswer(OpeningPeriodState.Open, "SENTETIK-opening-v1"));
    }
    private sealed class UnusedOpeningPartyPort : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId, Guid companyAccountId,
            CancellationToken cancellationToken) => Task.FromResult(new ReadResult<PartyAccountEligibility>(ReadOutcome.Unavailable, null));
    }
}
