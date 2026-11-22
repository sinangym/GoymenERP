using System.Text.Json;
using GoymenERP.Api.FinanceRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CarryMaker = Guid.Parse("ae170000-0000-4000-8000-000000000001");
    private static readonly Guid CarryChecker = Guid.Parse("ae170000-0000-4000-8000-000000000002");

    [SqlServerFact]
    public async Task Period_carry_Down_guards_pass_empty_and_refuse_while_both_owner_histories_exist()
    {
        await using var run = await Run.StartAsync();
        var (period, _) = await CarryPeriods(run, includeTarget: true);
        var accountingGuard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations
            .AddPeriodClosureCarryAndReopening().DownOperations[0]).Sql;
        var financeGuard = Assert.IsType<SqlOperation>(new GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations
            .AddPeriodCarryVouchers().DownOperations[0]).Sql;
        await using var scope = OpenCarry(run, CarryChecker);
        await scope.Db.Database.ExecuteSqlRawAsync(accountingGuard);
        await scope.Db.Database.ExecuteSqlRawAsync(financeGuard);
        await scope.Store.ClosePeriodAsync(period, 2, CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose));
        Assert.Equal(51920, (await Assert.ThrowsAsync<SqlException>(() => scope.Db.Database.ExecuteSqlRawAsync(accountingGuard))).Number);
        Assert.Equal(51921, (await Assert.ThrowsAsync<SqlException>(() => scope.Db.Database.ExecuteSqlRawAsync(financeGuard))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.PeriodClosures"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryVouchers"));
        Assert.Equal(AccountingPeriodStatus.Closed, (await scope.Store.FindPeriodAsync(period))!.Period.Status);
    }

    [SqlServerFact]
    public async Task Period_close_carries_real_bank_balance_once_and_reopening_preserves_original_with_counter_record()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK carry bank");
        await run.FundBankAsync(bank, "1000.01");
        var (period, target) = await CarryPeriods(run, includeTarget: true);
        var beforeMovements = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceMovements");
        Guid closeId; Guid voucher; string hash;
        await using (var scope = OpenCarry(run, CarryChecker))
        {
            var closed = await scope.Store.ClosePeriodAsync(period, 2, CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose));
            Assert.Equal((AccountingPeriodStatus.Closed, 3L), (closed.Period.Status, closed.Version));
            var close = Assert.Single((await scope.Store.ReadLifecycleAsync(period, default))!.Closures);
            Assert.Equal(target, close.TargetPeriodId); Assert.Equal(1, close.LineCount);
            closeId = close.Id; voucher = close.CarryVoucherId; hash = close.SourceHash;
        }
        var lines = JsonSerializer.Deserialize<FinancePeriodCarryLineV1[]>(await run.ScalarAsync<string>(
            "SELECT LinesJson FROM finance.PeriodCarryVouchers WHERE Id=@id", ("@id", voucher)), FinanceJson.CreateOptions())!;
        var line = Assert.Single(lines); Assert.Equal(("Bank", bank, Currency, "1000.01", "0"),
            (line.AccountKind, line.AccountId, line.CurrencyId, line.Debit, line.Credit));
        Assert.Equal(beforeMovements, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceMovements"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Guid proposal;
        await using (var scope = OpenCarry(run, CarryMaker))
        {
            var proposed = await scope.Store.ProposeReopeningAsync(period, new(3, Guid.NewGuid(), "SENTETIK corrected source"), CarryMaker,
                CarryMetadata(CarryMaker, AccountingPermissionManifestV1.PeriodsPropose), default);
            proposal = proposed.Id;
            await Assert.ThrowsAsync<AccountingDomainException>(() => scope.Store.ApproveReopeningAsync(period, proposal, new(1, 3), CarryMaker,
                CarryMetadata(CarryMaker, AccountingPermissionManifestV1.PeriodsReopen), default));
        }
        await using (var scope = OpenCarry(run, CarryChecker))
        {
            await scope.Store.ApproveReopeningAsync(period, proposal, new(1, 3), CarryChecker,
                CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsReopen), default);
            Assert.Equal(AccountingPeriodStatus.Open, (await scope.Store.FindPeriodAsync(period))!.Period.Status);
            var close = Assert.Single((await scope.Store.ReadLifecycleAsync(period, default))!.Closures);
            Assert.Equal((closeId, voucher, hash, proposal), (close.Id, close.CarryVoucherId, close.SourceHash, close.ReopeningId));
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryVouchers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryReversals"));
        Assert.Equal(TreasuryBalanceOutcome.Sufficient, (await run.ProtectedBalanceAsync(bank, "1000.01")).Outcome);
        Assert.Equal(TreasuryBalanceOutcome.Insufficient, (await run.ProtectedBalanceAsync(bank, "1000.02")).Outcome);
        await using var foreign = OpenCarry(run, CarryChecker, CompanyB);
        Assert.Null(await foreign.Store.ReadLifecycleAsync(period, default));
    }

    [SqlServerFact]
    public async Task Missing_next_open_period_rejects_close_and_carry_together()
    {
        await using var run = await Run.StartAsync(); var (period, _) = await CarryPeriods(run, includeTarget: false);
        await using var scope = OpenCarry(run, CarryChecker);
        var error = await Assert.ThrowsAsync<AccountingStoreException>(() => scope.Store.ClosePeriodAsync(period, 2, CarryChecker,
            CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose)));
        Assert.Equal("accounting_period_carry_target_not_open", error.Code);
        Assert.Equal(AccountingPeriodStatus.Open, (await scope.Store.FindPeriodAsync(period))!.Period.Status);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryVouchers"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.PeriodClosures"));
    }

    [SqlServerFact]
    public async Task Later_closed_periods_must_be_reopened_by_their_own_two_person_flow_before_an_earlier_carry_is_reversed()
    {
        await using var run = await Run.StartAsync();
        var (earlier, later) = await CarryPeriods(run, includeTarget: true);
        Guid next;
        await using (var scope = OpenCarry(run, CarryChecker))
        {
            next = Guid.NewGuid();
            var (proposed, _) = await scope.Store.ProposePeriodAsync(next, new(2027, 1, 1), new(2027, 1, 31), CarryMaker,
                CarryMetadata(CarryMaker, AccountingPermissionManifestV1.PeriodsPropose));
            await scope.Store.ApprovePeriodAsync(next, proposed.Version, CarryChecker,
                CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsApprove));
            await scope.Store.ClosePeriodAsync(earlier, 2, CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose));
            await scope.Store.ClosePeriodAsync(later, 2, CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose));
        }
        Guid earlierProposal; Guid laterProposal;
        await using (var maker = OpenCarry(run, CarryMaker))
        {
            earlierProposal = (await maker.Store.ProposeReopeningAsync(earlier, new(3, Guid.NewGuid(), "SENTETIK earlier correction"), CarryMaker,
                CarryMetadata(CarryMaker, AccountingPermissionManifestV1.PeriodsPropose), default)).Id;
            laterProposal = (await maker.Store.ProposeReopeningAsync(later, new(3, Guid.NewGuid(), "SENTETIK later correction"), CarryMaker,
                CarryMetadata(CarryMaker, AccountingPermissionManifestV1.PeriodsPropose), default)).Id;
            await Assert.ThrowsAsync<AccountingDomainException>(() => maker.Store.ApproveReopeningAsync(later, laterProposal, new(1, 3), CarryMaker,
                CarryMetadata(CarryMaker, AccountingPermissionManifestV1.PeriodsReopen), default));
        }
        await using (var checker = OpenCarry(run, CarryChecker))
        {
            var refused = await Assert.ThrowsAsync<AccountingStoreException>(() => checker.Store.ApproveReopeningAsync(earlier,
                earlierProposal, new(1, 3), CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsReopen), default));
            Assert.Equal("accounting_period_carry_source_unavailable", refused.Code);
            Assert.Equal(AccountingPeriodStatus.Closed, (await checker.Store.FindPeriodAsync(earlier))!.Period.Status);
            Assert.Equal(AccountingPeriodStatus.Closed, (await checker.Store.FindPeriodAsync(later))!.Period.Status);
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryReversals"));
            await checker.Store.ApproveReopeningAsync(later, laterProposal, new(1, 3), CarryChecker,
                CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsReopen), default);
            Assert.Equal(AccountingPeriodStatus.Open, (await checker.Store.FindPeriodAsync(later))!.Period.Status);
            Assert.Equal(AccountingPeriodStatus.Closed, (await checker.Store.FindPeriodAsync(earlier))!.Period.Status);
            await checker.Store.ApproveReopeningAsync(earlier, earlierProposal, new(1, 3), CarryChecker,
                CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsReopen), default);
            Assert.Equal(AccountingPeriodStatus.Open, (await checker.Store.FindPeriodAsync(earlier))!.Period.Status);
            Assert.Equal(AccountingPeriodStatus.Open, (await checker.Store.FindPeriodAsync(next))!.Period.Status);
        }
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.PeriodClosures"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryVouchers"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryReversals"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceMovements"));
    }

    [SqlServerFact]
    public async Task Final_authority_failure_rolls_back_both_owner_records_and_both_audits()
    {
        await using var run = await Run.StartAsync(); var (period, _) = await CarryPeriods(run, includeTarget: true);
        await using var scope = OpenCarry(run, CarryChecker);
        var metadata = CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose) with { FreshAuthorityFence = _ =>
        {
            if (scope.Db.ChangeTracker.Entries<AccountingPeriodClosureRow>().Any(x => x.State == EntityState.Unchanged))
                throw new InvalidOperationException("SENTETIK authority lost after save before host commit");
            return Task.CompletedTask;
        } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Store.ClosePeriodAsync(period, 2, CarryChecker, metadata));
        Assert.Equal(AccountingPeriodStatus.Open, (await scope.Store.FindPeriodAsync(period))!.Period.Status);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryVouchers"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.PeriodClosures"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType IN (N'finance.period-carry.recorded.v1',N'accounting.period.closed.v1')"));
    }

    private static AccountingCommandMetadata CarryMetadata(Guid actor, string permission) => new(actor.ToString("D"), permission,
        [], "SENTETIK period carry", null, Guid.NewGuid().ToString("D"), "Web");
    private static async Task<(Guid Period, Guid Target)> CarryPeriods(Run run, bool includeTarget)
    {
        await using var scope = OpenCarry(run, CarryChecker);
        await scope.Db.Database.MigrateAsync();
        var period = await Open(new(2026, 11, 1), new(2026, 11, 30));
        var target = includeTarget ? await Open(new(2026, 12, 1), new(2026, 12, 31)) : Guid.Empty;
        return (period, target);
        async Task<Guid> Open(DateOnly start, DateOnly end)
        {
            var id = Guid.NewGuid();
            var (proposed, _) = await scope.Store.ProposePeriodAsync(id, start, end, CarryMaker,
                CarryMetadata(CarryMaker, AccountingPermissionManifestV1.PeriodsPropose));
            await scope.Store.ApprovePeriodAsync(id, proposed.Version, CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsApprove));
            return id;
        }
    }
    private static CarryScope OpenCarry(Run run, Guid actor, Guid? companyId = null)
    {
        var scope = run.Factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var company = services.GetRequiredService<ICompanyContext>(); company.Set(companyId ?? CompanyA);
        var db = services.GetRequiredService<AccountingDbContext>(); var audit = services.GetRequiredService<IAuditEventCollector>();
        var port = new FinancePeriodCarryForwardPortV1(db, company, new FixedCurrentUser(actor.ToString("D"), company.CompanyId),
            new CarryAuthority(company.CompanyId), new SyntheticPermissions(), audit, services.GetRequiredService<IAuditTransactionWriterV1>());
        return new(scope, db, new(db, company, audit, TimeProvider.System,
            new AccountingAmountScaleVerifier(new SyntheticSettings(company.CompanyId)), port));
    }
    private sealed record CarryScope(AsyncServiceScope Scope, AccountingDbContext Db, AccountingStore Store) : IAsyncDisposable
    { public ValueTask DisposeAsync() => Scope.DisposeAsync(); }
    /// <summary>SENTETIK authorization only. Both actual schema owners and the Audit owner use the real borrowed SQL transaction.</summary>
    private sealed class CarryAuthority(Guid company) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == company && permission is AccountingPermissionManifestV1.PeriodsClose or AccountingPermissionManifestV1.PeriodsApprove
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
}
