using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceExpensePaymentSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CarryParty = Guid.Parse("a5100000-0000-4000-8000-000000000001");
    private static readonly Guid CarryAccount = Guid.Parse("a5100000-0000-4000-8000-000000000002");

    [SqlServerFact]
    public async Task Approved_party_opening_is_carried_into_the_next_ledger_without_new_facts_and_reopening_removes_only_the_carry()
    {
        await using var run = await Run.StartAsync();
        var (period, _) = await CarryPeriods(run, includeTarget: true);
        OpeningBalanceView opening;
        await using (var maker = OpenPartyCarryOpening(run, CarryMaker))
        {
            var proposed = await maker.Service.ProposeAsync(maker.Context, Key("party-carry-propose"),
                new(OpeningBalanceTargetKind.PartyAccount, CarryAccount, CarryParty, Currency,
                    OpeningBalanceDirection.Debit, "100.01", OccurredOn), default);
            Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome); opening = proposed.Resource!;
        }
        await using (var checker = OpenPartyCarryOpening(run, CarryChecker))
        {
            var approved = await checker.Service.ApproveAsync(checker.Context, Key("party-carry-approve"),
                new(opening.Id, opening.Version), default);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        }
        Guid carryId;
        await using (var closer = OpenCarry(run, CarryChecker))
        {
            await closer.Store.ClosePeriodAsync(period, 2, CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose));
            carryId = Assert.Single((await closer.Store.ReadLifecycleAsync(period, default))!.Closures).CarryVoucherId;
        }
        await using (var reader = OpenPartyCarryOpening(run, CarryMaker))
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var service = new PartyLedgerService(reader.Db, company, new FixedCurrentUser(CarryMaker.ToString("D"), CompanyA), new SyntheticPermissions());
            var result = await service.ReadAuthorizedAsync(reader.Context, new(CarryParty, CarryAccount, new(2026, 12, 1), new(2026, 12, 31)), default);
            Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
            Assert.Equal(carryId, result.Ledger!.PeriodCarry!.VoucherId);
            var currency = Assert.Single(result.Ledger.Currencies);
            Assert.Empty(currency.Entries); Assert.Equal("100.01", currency.OpeningBalance!.Amount.Money!.Amount);
            Assert.Equal("100.01", currency.ClosingBalance.Amount.Money!.Amount);
            Assert.Equal("0.00", currency.DebitTotal.Money!.Amount);
            var allTime = await service.SummarizeAuthorizedAsync(reader.Context, new(CarryParty, CarryAccount), default);
            Assert.Equal("100.01", Assert.Single(allTime.Balance!.Currencies).Balance.Amount.Money!.Amount);
        }
        OpeningBalanceView reversal;
        await using (var maker = OpenPartyCarryOpening(run, CarryMaker))
        {
            var current = await maker.Db.OpeningBalances.AsNoTracking().SingleAsync(x => x.Id == opening.Id);
            var result = await maker.Service.ProposeReversalAsync(maker.Context, Key("party-carry-reversal-propose"),
                new(opening.Id, current.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), "SENTETIK correction"), default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome); reversal = result.Resource!;
        }
        await using (var checker = OpenPartyCarryOpening(run, CarryChecker))
        {
            var refused = await checker.Service.ApproveReversalAsync(checker.Context, Key("party-carry-reversal-approve"),
                new(opening.Id, reversal.Reversal!.Version), default);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Contains(refused.Issues, x => x.Code == "opening_balance_period_carried");
            Assert.Equal(1, await checker.Db.OpeningBalanceMovements.CountAsync());
        }
        Guid proposal;
        await using (var maker = OpenCarry(run, CarryMaker))
            proposal = (await maker.Store.ProposeReopeningAsync(period, new(3, Guid.NewGuid(), "SENTETIK carry ledger correction"), CarryMaker,
                CarryMetadata(CarryMaker, AccountingPermissionManifestV1.PeriodsPropose), default)).Id;
        await using (var checker = OpenCarry(run, CarryChecker))
            await checker.Store.ApproveReopeningAsync(period, proposal, new(1, 3), CarryChecker,
                CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsReopen), default);
        await using (var reader = OpenPartyCarryOpening(run, CarryMaker))
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var service = new PartyLedgerService(reader.Db, company, new FixedCurrentUser(CarryMaker.ToString("D"), CompanyA), new SyntheticPermissions());
            var result = await service.ReadAuthorizedAsync(reader.Context, new(CarryParty, CarryAccount, new(2026, 12, 1), new(2026, 12, 31)), default);
            Assert.Equal(PartyLedgerOutcome.Found, result.Outcome);
            Assert.Equal(PartyLedgerCarryForward.NoneOpen018, result.Ledger!.CarryForward);
            Assert.Null(result.Ledger.PeriodCarry); Assert.Empty(result.Ledger.Currencies);
            var allTime = await service.SummarizeAuthorizedAsync(reader.Context, new(CarryParty, CarryAccount), default);
            Assert.Equal("100.01", Assert.Single(allTime.Balance!.Currencies).Balance.Amount.Money!.Amount);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceMovements"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryVouchers"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PeriodCarryReversals"));
        await using (var checker = OpenPartyCarryOpening(run, CarryChecker))
        {
            // The refused attempt did not consume the command key. Explicit two-person reopening removes
            // the active carry barrier, without silently modifying the original carry snapshot.
            var approved = await checker.Service.ApproveReversalAsync(checker.Context, Key("party-carry-reversal-approve"),
                new(opening.Id, reversal.Reversal!.Version), default);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal(2, await checker.Db.OpeningBalanceMovements.CountAsync());
        }
    }

    [SqlServerFact]
    public async Task A_pending_initial_opening_cannot_be_approved_after_its_date_has_been_carried()
    {
        await using var run = await Run.StartAsync();
        var (period, _) = await CarryPeriods(run, includeTarget: true);
        var bank = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK pending carry bank");
        OpeningBalanceView opening;
        await using (var maker = OpenPartyCarryOpening(run, CarryMaker))
        {
            var proposed = await maker.Service.ProposeAsync(maker.Context, Key("pending-carry-propose"),
                new(OpeningBalanceTargetKind.BankAccount, bank, null, Currency, null, "100.01", OccurredOn), default);
            Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome); opening = proposed.Resource!;
        }
        await using (var closer = OpenCarry(run, CarryChecker))
            await closer.Store.ClosePeriodAsync(period, 2, CarryChecker, CarryMetadata(CarryChecker, AccountingPermissionManifestV1.PeriodsClose));
        await using (var checker = OpenPartyCarryOpening(run, CarryChecker))
        {
            var approved = await checker.Service.ApproveAsync(checker.Context, Key("pending-carry-approve"), new(opening.Id, opening.Version), default);
            Assert.Equal(CommandOutcome.ValidationFailed, approved.Outcome);
            Assert.Contains(approved.Issues, x => x.Code == "opening_balance_period_carried");
            Assert.Empty(await checker.Db.OpeningBalanceMovements.ToListAsync());
            Assert.Equal(FinanceOpeningBalanceService.Proposed,
                (await checker.Db.OpeningBalances.AsNoTracking().SingleAsync(x => x.Id == opening.Id)).Status);
        }
    }

    private static LedgerOpeningScope OpenPartyCarryOpening(Run run, Guid actor)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        var user = new FixedCurrentUser(actor.ToString("D"), CompanyA); var audit = new AuditEventCollector();
        var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        var service = new FinanceOpeningBalanceService(db, company, user, new SyntheticPermissions(), new CarryCandidates(),
            new CarryPartyPort(), new SyntheticOpeningPeriod(), audit,
            new(new(ErpPermissions.FinanceOpeningBalancesPropose, ["SENTETIK-FINANCE"], "Web"),
                new(ErpPermissions.FinanceOpeningBalancesApprove, ["SENTETIK-FINANCE"], "Web")),
            TimeProvider.System, new InternalLedgerTreasuryBalanceGuard(db, company));
        return new(db, service, new(CompanyA, actor.ToString("D"), "SENTETIK carry ledger", null));
    }
    // Synthetic owner prerequisites only; Finance, Accounting, Audit, their schema and borrowed transaction are real.
    private sealed class CarryCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new(Currency, "SENTETIK", "SENTETIK carry currency")]);
    }
    private sealed class CarryPartyPort : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId, Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new(CarryParty, CarryAccount, context.CompanyId, true, IsPartyActive: true)));
    }
}
