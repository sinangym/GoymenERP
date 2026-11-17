using System.Data;
using GoymenERP.Api.FinanceRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Credit_approval_holds_the_real_accounting_period_open_until_its_finance_transaction_commits()
    {
        await using var run = await Run.StartAsync();
        var period = await OpenCreditAccountingPeriod(run);
        var account = await ApprovedMovementAccount(run);
        Guid movement;
        await using (var maker = run.OpenMovement(Maker, accountingOwner: (db, company, user) =>
            new FinanceCreditAccountingPeriodPortV1(db, company, user, new CreditPeriodAuthority())))
            movement = (await maker.Service.ProposeAsync(maker.Context, Key("period-propose"), ActualInput(account), default)).Resource!.Id;
        var reads = 0;
        await using (var checker = run.OpenMovement(Checker, accountingOwner: (db, company, user) =>
            new AfterCreditPeriodRead(new FinanceCreditAccountingPeriodPortV1(db, company, user, new CreditPeriodAuthority()), async () =>
            {
                reads++;
                Assert.NotNull(db.Database.CurrentTransaction);
                // The adapter disposed its borrowed Accounting reader already. The real period writer,
                // using a different connection, must still fail to close while Finance is uncommitted.
                await using var closing = run.Factory.Services.CreateAsyncScope();
                closing.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
                var closingDb = closing.ServiceProvider.GetRequiredService<AccountingDbContext>();
                await closingDb.Database.OpenConnectionAsync();
                await closingDb.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 0");
                var blocked = await Record.ExceptionAsync(() => closing.ServiceProvider.GetRequiredService<AccountingStore>()
                    .ClosePeriodAsync(period, 2, CreditPeriodChecker, CreditPeriodMetadata()));
                Assert.NotNull(blocked);
                Assert.Equal(1222, Assert.IsType<SqlException>(blocked.GetBaseException()).Number);
            })))
        {
            var accepted = await checker.Service.DecideAsync(checker.Context, Key("period-approve"), new(movement, "1", true, null), default);
            Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome);
            Assert.Equal(CreditMovementStatusV1.Approved, accepted.Resource!.Status);
        }
        Assert.Equal(1, reads);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id", ("@id", movement)));
        await using var after = run.Factory.Services.CreateAsyncScope();
        after.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var closed = await after.ServiceProvider.GetRequiredService<AccountingStore>()
            .ClosePeriodAsync(period, 2, CreditPeriodChecker, CreditPeriodMetadata());
        Assert.Equal("Closed", closed.Period.Status.ToString());
    }

    [SqlServerFact]
    public async Task Credit_approval_after_the_real_period_closed_preserves_the_proposal_and_creates_no_actual_event()
    {
        await using var run = await Run.StartAsync();
        var period = await OpenCreditAccountingPeriod(run);
        var account = await ApprovedMovementAccount(run);
        Guid movement;
        await using (var maker = run.OpenMovement(Maker, accountingOwner: (db, company, user) =>
            new FinanceCreditAccountingPeriodPortV1(db, company, user, new CreditPeriodAuthority())))
            movement = (await maker.Service.ProposeAsync(maker.Context, Key("closed-propose"), ActualInput(account), default)).Resource!.Id;
        await using (var closing = run.Factory.Services.CreateAsyncScope())
        {
            closing.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            await closing.ServiceProvider.GetRequiredService<AccountingStore>()
                .ClosePeriodAsync(period, 2, CreditPeriodChecker, CreditPeriodMetadata());
        }
        await using (var checker = run.OpenMovement(Checker, accountingOwner: (db, company, user) =>
            new FinanceCreditAccountingPeriodPortV1(db, company, user, new CreditPeriodAuthority())))
        {
            var refused = await checker.Service.DecideAsync(checker.Context, Key("closed-approve"), new(movement, "1", true, null), default);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Contains(refused.Issues, issue => issue.Code == RecordPeriodAnswer.PeriodNotOpenCode);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.CreditMovements WHERE Id=@id", ("@id", movement)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement' AND EntityId=@id", ("@id", movement.ToString("D"))));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages WHERE AggregateId=@id", ("@id", movement)));
    }

    [SqlServerFact]
    public async Task Credit_period_owner_permission_loss_does_not_record_the_movement_and_borrowed_rollback_keeps_handles_owned_by_finance()
    {
        await using var run = await Run.StartAsync();
        await OpenCreditAccountingPeriod(run);
        var account = await ApprovedMovementAccount(run);
        var authority = new CreditPeriodAuthority { DenyAfterFirstRead = true };
        await using (var maker = run.OpenMovement(Maker, accountingOwner: (db, company, user) =>
            new FinanceCreditAccountingPeriodPortV1(db, company, user, authority)))
        {
            var refused = await maker.Service.ProposeAsync(maker.Context, Key("late-denied"), ActualInput(account), default);
            Assert.Equal(CommandOutcome.Unavailable, refused.Outcome);
        }
        Assert.Equal(2, authority.Calls);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovements"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditMovementCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Finance.CreditMovement'"));

        await using var owner = run.OpenMovement(Maker);
        var adapter = new FinanceCreditAccountingPeriodPortV1(owner.Database, owner.Company,
            new FixedCurrentUser(Maker, CompanyA), new CreditPeriodAuthority());
        Assert.Equal(RecordPeriodOutcome.Unavailable, (await adapter.ReadAsync(owner.Context, new(2026, 11, 16), default)).Outcome);
        await using (var weak = await owner.Database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            Assert.Equal(RecordPeriodOutcome.Unavailable, (await adapter.ReadAsync(owner.Context, new(2026, 11, 16), default)).Outcome);
            Assert.Same(weak, owner.Database.Database.CurrentTransaction);
            await weak.RollbackAsync();
        }
        await using (var transaction = await owner.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            Assert.Equal(RecordPeriodOutcome.Open, (await adapter.ReadAsync(owner.Context, new(2026, 11, 16), default)).Outcome);
            Assert.Same(transaction, owner.Database.Database.CurrentTransaction);
            await using var command = owner.Database.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction(); command.CommandText = "SELECT 1";
            Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
            await transaction.RollbackAsync();
        }
        Assert.Null(owner.Database.Database.CurrentTransaction);
        await using var fresh = await owner.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        Assert.Equal(RecordPeriodOutcome.Open, (await adapter.ReadAsync(owner.Context, new(2026, 11, 16), default)).Outcome);
        await fresh.RollbackAsync();
    }

    private static readonly Guid CreditPeriodMaker = Guid.Parse("d4710000-0000-4000-8000-000000000001");
    private static readonly Guid CreditPeriodChecker = Guid.Parse("d4710000-0000-4000-8000-000000000002");
    private static AccountingCommandMetadata CreditPeriodMetadata() => new(CreditPeriodChecker.ToString("D"),
        "synthetic.accounting.period-write", [], "SENTETIK credit period", null, Guid.NewGuid().ToString("D"), "Web");
    private static async Task<Guid> OpenCreditAccountingPeriod(Run run)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        await scope.ServiceProvider.GetRequiredService<AccountingDbContext>().Database.MigrateAsync();
        var store = scope.ServiceProvider.GetRequiredService<AccountingStore>(); var id = Guid.NewGuid();
        var (created, _) = await store.ProposePeriodAsync(id, new(2026, 11, 1), new(2026, 11, 30), CreditPeriodMaker,
            CreditPeriodMetadata() with { ActorId = CreditPeriodMaker.ToString("D") });
        await store.ApprovePeriodAsync(id, created.Version, CreditPeriodChecker, CreditPeriodMetadata());
        return id;
    }
    private sealed class CreditPeriodAuthority : IErpAuthorityRevalidationService
    {
        public bool DenyAfterFirstRead { get; init; }
        public int Calls { get; private set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(permission == AccountingPermissionManifestV1.PeriodsRead && companyId == CompanyA
                && (!DenyAfterFirstRead || Calls == 1)
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }
    private sealed class AfterCreditPeriodRead(IFinanceCreditAccountingPeriodPortV1 actual, Func<Task> after) : IFinanceCreditAccountingPeriodPortV1
    {
        public async Task<RecordPeriodAnswer> ReadAsync(FinanceContext context, DateOnly date, CancellationToken cancellationToken)
        {
            var answer = await actual.ReadAsync(context, date, cancellationToken);
            Assert.Equal(RecordPeriodOutcome.Open, answer.Outcome);
            await after(); return answer;
        }
    }
}
