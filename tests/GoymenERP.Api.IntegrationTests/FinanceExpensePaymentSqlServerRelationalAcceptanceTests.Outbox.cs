using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Recorded_expense_outbox_keeps_exact_source_on_replay_and_committed_cursor_is_company_scoped()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK outbox bank");
        await run.FundBankAsync(bank, "2000");
        var approved = await run.ApprovedExpenseAsync(bank, "outbox-expense");
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id", ("@id", approved.Id)));
        ExpensePaymentView recorded;
        await using (var scope = run.OpenLedger(CompanyA, Recorder))
        {
            var result = await scope.Payments.RecordAsync(scope.Context, Key("outbox-record"),
                new(approved.Id, approved.Version, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            recorded = Assert.IsType<ExpensePaymentView>(result.Resource);
        }
        await using (var scope = run.OpenLedger(CompanyA, Recorder))
        {
            var replay = await scope.Payments.RecordAsync(scope.Context, Key("outbox-record"),
                new(approved.Id, approved.Version, true), CancellationToken.None);
            Assert.True(replay.IsReplay);
            var mismatch = await scope.Payments.RecordAsync(scope.Context, Key("outbox-record"),
                new(approved.Id, approved.Version, false), CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, mismatch.Outcome);
        }
        var company = new CompanyContext(); company.Set(CompanyA);
        await using var database = OutboxDatabase(run, company);
        var source = new FinanceCreditOutboxEventSourceV1(database, company);
        var delivered = Assert.Single(await source.ReadAfterAsync(null, 50, CancellationToken.None),
            item => item.EventType == FinanceFinancialMovementEventsV1.ExpensePaymentRecorded);
        var payload = JsonSerializer.Deserialize<FinanceExpensePaymentRecordedV1>(delivered.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(recorded, payload.Source);
        Assert.Equal(delivered.EventId, payload.EventId);
        Assert.Equal(CompanyA, delivered.CompanyId);
        Assert.Equal(run.Correlation, delivered.CorrelationId);
        Assert.Equal(1, await database.OutboxMessages.CountAsync(item => item.AggregateId == recorded.Id));
        Assert.Empty(await source.ReadAfterAsync(delivered.Position, 50, CancellationToken.None));
        company.Set(CompanyB);
        Assert.Empty(await source.ReadAfterAsync(null, 50, CancellationToken.None));
    }

    /// <summary>Real SQL source/command/audit/outbox transaction; permission, period and balance ports are synthetic.</summary>
    [SqlServerFact]
    public async Task Same_currency_transfer_publishes_one_source_with_two_legs_and_a_replayed_commit_adds_nothing()
    {
        await using var run = await Run.StartAsync();
        var from = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK transfer from");
        var to = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK transfer to");
        var request = new CommitCashTransferRequest(new(CashTransferKind.SameCurrency,
            CashTransferAccountKind.Bank, from, new("100.00", Currency), new(Profile.ToString("D"), "1"),
            CashTransferAccountKind.Bank, to, new("100.00", Currency), new(Profile.ToString("D"), "1"),
            null, OccurredOn, null, "SENTETIK transfer source", null), true);
        CashTransferView? original = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var audit = new AuditEventCollector();
            await using var database = OutboxDatabase(run, company, new AuditSaveChangesInterceptor(audit, company));
            var service = new CashTransferService(database, company, new FixedCurrentUser(Recorder, CompanyA),
                new SyntheticPermissions(), new SyntheticBalance(TreasuryBalanceOutcome.Sufficient),
                new UnavailableCurrencyExchangeRateRule(), new SyntheticSettings(CompanyA), new SyntheticOpenPeriod(), audit,
                new(FinanceMoneyOperations.ManualBankPaymentV1),
                new(new FinanceAuditEnvelope(FinancePermissionNames.CashTransferCommit, ["SENTETIK-FINANCE"], "Web")), TimeProvider.System);
            var answer = await service.CommitAsync(new(CompanyA, Recorder, run.Correlation, "SENTETIK cause"),
                Key("outbox-transfer"), request, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, answer.Outcome);
            Assert.Equal(attempt == 1, answer.IsReplay);
            if (original is null) original = Assert.IsType<CashTransferView>(answer.Resource);
            else Assert.Equal(original, answer.Resource);
        }
        var owner = new CompanyContext(); owner.Set(CompanyA);
        await using var verify = OutboxDatabase(run, owner);
        var row = await verify.OutboxMessages.SingleAsync(item => item.EventType == FinanceFinancialMovementEventsV1.CashTransferRecorded);
        var source = JsonSerializer.Deserialize<FinanceCashTransferRecordedV1>(row.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(row.Id, source.EventId);
        Assert.Equal(original, source.Source);
        Assert.Equal((from, to), (source.Source.Preparation.SourceAccountId, source.Source.Preparation.TargetAccountId));
        Assert.Equal(1, await verify.CashTransfers.CountAsync());
        Assert.Equal(1, await verify.CashTransferCommands.CountAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.cash-transfer.recorded.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
    }

    private static FinanceDbContext OutboxDatabase(Run run, CompanyContext company,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
            .AddInterceptors(interceptors).Options, company);
}
