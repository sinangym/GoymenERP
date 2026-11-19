using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Actual_credit_approval_keeps_source_once_with_outbox_audit_and_receipt_and_Down_refuses_loss()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedMovementAccount(run);
        var input = ActualInput(account);
        CreditMovementViewV1 pending;
        await using (var maker = run.OpenMovement(Maker))
        {
            pending = (await maker.Service.ProposeAsync(maker.Context, Key("actual-p"), input, default)).Resource!;
            Assert.NotNull(pending);
            Assert.Equal(CommandOutcome.ValidationFailed, (await maker.Service.DecideAsync(maker.Context, Key("own"), new(pending.Id, "1", true, null), default)).Outcome);
        }
        await using (var checker = run.OpenMovement(Checker))
        {
            var request = new CreditMovementDecisionV1(pending.Id, "1", true, null);
            var result = await checker.Service.DecideAsync(checker.Context, Key("actual-a"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            Assert.True((await checker.Service.DecideAsync(checker.Context, Key("actual-a"), request, default)).IsReplay);
            var events = await new FinanceCreditOutboxEventSourceV1(checker.Database, checker.Company).ReadAfterAsync(null, 10, default);
            var item = Assert.Single(events);
            var payload = JsonSerializer.Deserialize<FinanceCreditMovementRecordedV1>(item.PayloadJson, FinanceJson.CreateOptions())!;
            Assert.Equal(item.EventId, payload.EventId); Assert.Equal(pending.Id, payload.MovementId);
            Assert.Null(payload.Components[0].FunctionalAmount);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id", ("@id", pending.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement' AND EntityId=@id", ("@id", pending.Id.ToString("D"))));
        var guard = Assert.IsType<SqlOperation>(new AddCreditActualMovements().DownOperations[0]).Sql;
        Assert.Equal(51969, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovements WHERE Id=@id", ("@id", pending.Id)));
    }

    [SqlServerFact]
    public async Task Credit_actual_database_rejects_self_approval_and_source_duplicates_while_foreign_company_reads_nothing()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedMovementAccount(run);
        CreditMovementViewV1 pending;
        await using (var maker = run.OpenMovement(Maker))
        {
            pending = (await maker.Service.ProposeAsync(maker.Context, Key("source-p"), ActualInput(account), default)).Resource!;
            Assert.Equal(CommandOutcome.ValidationFailed, (await maker.Service.ProposeAsync(maker.Context, Key("duplicate-p"), ActualInput(account), default)).Outcome);
        }
        Assert.Equal(CheckViolation, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditMovements SET [Status]=2, DecidedBy=ProposedBy, DecidedAtUtc=SYSDATETIMEOFFSET(), [Version]=2 WHERE Id=@id", ("@id", pending.Id)))).Number);
        await using (var foreign = run.OpenMovement(Checker, companyId: CompanyB))
            Assert.Equal(ReadOutcome.NotFound, (await foreign.Service.ListAsync(foreign.Context, account.Id, default)).Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id", ("@id", pending.Id)));
    }

    [SqlServerFact]
    public async Task Closed_credit_opening_correction_commits_reversal_and_replacement_together_without_reopening_original_period()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedMovementAccount(run);
        var period = new ActualPeriods();
        var input = ActualInput(account) with { Kind = CreditMovementKindV1.OpeningBalance };
        Guid original;
        await using (var maker = run.OpenMovement(Maker, period)) original = (await maker.Service.ProposeAsync(maker.Context, Key("o-p"), input, default)).Resource!.Id;
        await using (var checker = run.OpenMovement(Checker, period)) Assert.Equal(CommandOutcome.Succeeded,
            (await checker.Service.DecideAsync(checker.Context, Key("o-a"), new(original, "1", true, null), default)).Outcome);
        period.State = OpeningPeriodState.Closed; period.Version = "2";
        Guid replacement;
        await using (var maker = run.OpenMovement(Maker, period)) replacement = (await maker.Service.ProposeAsync(maker.Context, Key("r-p"), input with
        {
            ReplacesOpeningMovementId = original, ExpectedOpeningMovementVersion = "2", AccountingDate = new(2026, 12, 1),
            Components = [new("principal", "105.00", null)], SourceDocument = input.SourceDocument with { Revision = "2" }
        }, default)).Resource!.Id;
        await using (var checker = run.OpenMovement(Checker, period)) Assert.Equal(CommandOutcome.Succeeded,
            (await checker.Service.DecideAsync(checker.Context, Key("r-a"), new(replacement, "1", true, null), default)).Outcome);
        Assert.Equal(replacement, await run.ScalarAsync<Guid>("SELECT ReversedByMovementId FROM finance.CreditMovements WHERE Id=@id", ("@id", original)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId IN (@a,@b)", ("@a", original), ("@b", replacement)));
        Assert.Equal(OpeningPeriodState.Closed, period.State);
    }

    private static CreditMovementProposeV1 ActualInput(CreditAccountView account) => new(account.Id, account.Version, CreditMovementKindV1.Drawdown,
        new("SENTETIK actual document", new(2026, 11, 16), "1"), new(2026, 11, 16), new(2026, 11, 16), [new("principal", "100.00", null)], null, null, null, "SENTETIK source");

    [SqlServerFact]
    public async Task Only_approved_actual_credit_bank_legs_change_the_internal_balance_and_repayment_cannot_overdraw_it()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedMovementAccount(run);
        var bank = Guid.NewGuid();
        await using (var seed = run.OpenMovement(Maker))
        {
            seed.Database.BankAccounts.Add(new BankAccountRow { Id = bank, CompanyId = CompanyA, CurrencyId = Currency,
                DisplayName = "SENTETIK bank", Version = 2, Status = 2, ProposedBy = Maker, ProposedAtUtc = DateTimeOffset.UtcNow,
                ApprovedBy = Checker, ApprovedAtUtc = DateTimeOffset.UtcNow, MoneyMovementEligible = true,
                Iban = "TR330006100519786457841326" });
            await seed.Database.SaveChangesAsync();
        }
        // The bank document explicitly says 95 net. It is not derived from the 100 principal.
        var input = ActualInput(account) with { BankEffect = new(bank, "95.00", "SENTETIK actual bank leg") };
        Guid drawdown;
        await using (var maker = run.OpenMovement(Maker))
        {
            drawdown = (await maker.Service.ProposeAsync(maker.Context, Key("bank-draw-p"), input, default)).Resource!.Id;
            Assert.True((await new FinanceTreasuryLedger(maker.Database, maker.Company)
                .ReadAsync(CompanyA, ExpensePaymentSourceKind.Bank, bank, Currency, default))!.IsZero);
        }
        await using (var checker = run.OpenMovement(Checker))
            Assert.Equal(CommandOutcome.Succeeded, (await checker.Service.DecideAsync(checker.Context, Key("bank-draw-a"), new(drawdown, "1", true, null), default)).Outcome);
        Guid repayment;
        await using (var maker = run.OpenMovement(Maker))
            repayment = (await maker.Service.ProposeAsync(maker.Context, Key("bank-pay-p"), input with { Kind = CreditMovementKindV1.Repayment,
                SourceDocument = input.SourceDocument with { Reference = "SENTETIK repayment" },
                BankEffect = new(bank, "95.01", "SENTETIK insufficient balance") }, default)).Resource!.Id;
        await using (var checker = run.OpenMovement(Checker))
        {
            Assert.Equal(CommandOutcome.ValidationFailed, (await checker.Service.DecideAsync(checker.Context, Key("bank-pay-a"), new(repayment, "1", true, null), default)).Outcome);
            Assert.True(FinanceExactDecimalText.TryParseNonNegative("95.00", out var net));
            Assert.True(FinanceExactDecimalText.TryParseNonNegative("95.01", out var tooMuch));
            var balance = (await new FinanceTreasuryLedger(checker.Database, checker.Company)
                .ReadAsync(CompanyA, ExpensePaymentSourceKind.Bank, bank, Currency, default))!;
            Assert.True(balance.Covers(net)); Assert.False(balance.Covers(tooMuch));
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
    }

    [SqlServerFact]
    public async Task Committed_outbox_cursor_does_not_advance_past_an_earlier_uncommitted_credit_event()
    {
        await using var run = await Run.StartAsync();
        await using var earlier = run.OpenMovement(Maker);
        await using var transaction = await earlier.Database.Database.BeginTransactionAsync();
        var first = SyntheticOutbox(); earlier.Database.OutboxMessages.Add(first);
        await earlier.Database.SaveChangesAsync();
        var second = SyntheticOutbox();
        await using (var later = run.OpenMovement(Checker))
        {
            later.Database.OutboxMessages.Add(second); await later.Database.SaveChangesAsync();
        }
        await using var reader = run.OpenMovement(Checker);
        var source = new FinanceCreditOutboxEventSourceV1(reader.Database, reader.Company);
        Assert.Empty(await source.ReadAfterAsync(null, 10, default).WaitAsync(TimeSpan.FromSeconds(10)));
        await transaction.CommitAsync();
        var page = await source.ReadAfterAsync(null, 10, default);
        Assert.Equal(new[] { first.Id, second.Id }, page.Select(x => x.EventId));
        Assert.Empty(await source.ReadAfterAsync(page[^1].Position, 10, default));
        await OutboxReadIsolationAssertions.VerifyAsync(reader.Database, source, "Finance");
        await using var foreign = run.OpenMovement(Checker, companyId: CompanyB);
        Assert.Empty(await new FinanceCreditOutboxEventSourceV1(foreign.Database, foreign.Company).ReadAfterAsync(null, 10, default));

        FinanceOutboxRow SyntheticOutbox() => new() { Id = Guid.NewGuid(), CompanyId = CompanyA, AggregateId = Guid.NewGuid(), AggregateVersion = 1,
            EventType = FinanceCreditMovementEventsV1.Recorded, SchemaVersion = 1, PayloadJson = "{\"synthetic\":true}",
            OccurredAtUtc = DateTimeOffset.UtcNow, CorrelationId = "SENTETIK cursor" };
    }
    private static async Task<CreditAccountView> ApprovedMovementAccount(Run run)
    {
        Guid account;
        await using (var maker = run.Open(CompanyA, Maker)) account = (await maker.Credits.ProposeAsync(maker.Context, Key("credit-p"), Request(), default)).Resource!.Id;
        await using var checker = run.Open(CompanyA, Checker);
        return (await checker.Credits.ApproveAsync(checker.Context, Key("credit-a"), new(account, "1"), default)).Resource!;
    }
    private sealed partial class Run
    {
        public ActualScope OpenMovement(string actor, ActualPeriods? periods = null, Guid? companyId = null,
            Func<FinanceDbContext, CompanyContext, ICurrentUser, IFinanceCreditAccountingPeriodPortV1>? accountingOwner = null)
        {
            var company = new CompanyContext(); company.Set(companyId ?? CompanyA);
            var audit = new AuditEventCollector();
            var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
            periods ??= new();
            var user = new FixedCurrentUser(actor, company.CompanyId);
            var service = new FinanceCreditMovementServiceV1(db, company, user, new SyntheticPermissions(),
                new SyntheticCreditSources(company.CompanyId), periods, accountingOwner?.Invoke(db, company, user) ?? periods,
                new InternalLedgerTreasuryBalanceGuard(db, company), audit,
                new(new(ErpPermissions.FinanceCreditsPropose, ["SENTETIK"], "Web"), new(ErpPermissions.FinanceCreditsApprove, ["SENTETIK"], "Web")), TimeProvider.System);
            return new(db, service, company, new(company.CompanyId, actor, Correlation, null));
        }
    }
    private sealed class ActualScope(FinanceDbContext db, FinanceCreditMovementServiceV1 service, CompanyContext company, FinanceContext context) : IAsyncDisposable
    {
        public FinanceDbContext Database => db;
        public FinanceCreditMovementServiceV1 Service => service;
        public CompanyContext Company => company;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
    private sealed class ActualPeriods : IFinanceOpeningPeriodReader, IFinanceCreditAccountingPeriodPortV1
    {
        public OpeningPeriodState State { get; set; } = OpeningPeriodState.Open;
        public string Version { get; set; } = "1";
        public Task<FinanceOpeningPeriodAnswer> ReadAsync(Guid companyId, CancellationToken cancellationToken) => Task.FromResult(new FinanceOpeningPeriodAnswer(State, Version));
        public Task<RecordPeriodAnswer> ReadAsync(FinanceContext context, DateOnly accountingDate, CancellationToken cancellationToken) => Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }
}
