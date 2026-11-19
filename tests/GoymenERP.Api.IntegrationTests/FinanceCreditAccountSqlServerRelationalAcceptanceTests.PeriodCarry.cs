using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    /// <summary>Actual credit approvals, treasury reader and both carry owners. The existing
    /// fixture's identity/profile ports remain synthetic; this is not grant or HTTP evidence.</summary>
    [SqlServerFact]
    public async Task Bank_period_carry_uses_approved_actual_credit_transaction_dates_without_reposting_the_accounting_event()
    {
        await using var run = await Run.StartAsync();
        var period = await OpenCreditAccountingPeriod(run);
        var account = await ApprovedMovementAccount(run);
        var bank = Guid.NewGuid();
        await using (var seed = run.OpenMovement(Maker))
        {
            seed.Database.BankAccounts.Add(new BankAccountRow { Id = bank, CompanyId = CompanyA, CurrencyId = Currency,
                DisplayName = "SENTETIK carry bank", Version = 2, Status = 2, ProposedBy = Maker, ProposedAtUtc = DateTimeOffset.UtcNow,
                ApprovedBy = Checker, ApprovedAtUtc = DateTimeOffset.UtcNow, MoneyMovementEligible = true,
                Iban = "TR330006100519786457841326" });
            await seed.Database.SaveChangesAsync();
        }
        var boundary = new DateOnly(2026, 11, 30);
        var originalSources = new Dictionary<Guid, string>();
        await Approve(CreditMovementKindV1.Drawdown, "100.01", boundary, "boundary-draw");
        await Approve(CreditMovementKindV1.Repayment, "25.01", boundary, "boundary-pay");
        await Approve(CreditMovementKindV1.Drawdown, "200", boundary.AddDays(1), "future-draw");

        await using (var closing = run.Factory.Services.CreateAsyncScope())
        {
            closing.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var store = AccountingPeriodCarrySqlFixture.Store(closing.ServiceProvider, run.Factory.Services, CreditPeriodChecker);
            var result = await store.ClosePeriodAsync(period, 2, CreditPeriodChecker, CreditPeriodMetadata());
            Assert.Equal(AccountingPeriodStatus.Closed, result.Period.Status);
        }
        var lines = JsonSerializer.Deserialize<FinancePeriodCarryLineV1[]>(await run.ScalarAsync<string>(
            "SELECT LinesJson FROM finance.PeriodCarryVouchers WHERE SourcePeriodId=@id", ("@id", period)), FinanceJson.CreateOptions())!;
        var carried = Assert.Single(lines);
        Assert.Equal(("Bank", bank, Currency, "75.00", "0"),
            (carried.AccountKind, carried.AccountId, carried.CurrencyId, carried.Debit, carried.Credit));
        await using (var current = run.OpenMovement(Checker))
        {
            var balance = Assert.IsType<FinanceTreasuryBalance>(await new FinanceTreasuryLedger(current.Database, current.Company)
                .ReadAsync(CompanyA, ExpensePaymentSourceKind.Bank, bank, Currency, default));
            Assert.Equal(("300.01", "25.01"), (balance.Incoming.Text, balance.Outgoing.Text));
        }
        foreach (var (movement, sourceJson) in originalSources)
        {
            Assert.Equal(sourceJson, await run.ScalarAsync<string>("SELECT SourceJson FROM finance.CreditMovements WHERE Id=@id", ("@id", movement)));
            var source = JsonSerializer.Deserialize<CreditMovementProposeV1>(sourceJson, FinanceJson.CreateOptions())!;
            var accountingSource = JsonSerializer.Deserialize<FinanceCreditMovementRecordedV1>(await run.ScalarAsync<string>(
                "SELECT PayloadJson FROM finance.OutboxMessages WHERE AggregateId=@id", ("@id", movement)), FinanceJson.CreateOptions())!;
            Assert.Equal(source.TransactionDate, accountingSource.TransactionDate);
            Assert.Equal(source.AccountingDate, accountingSource.AccountingDate);
        }
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutboxMessages"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpeningBalanceMovements"));

        async Task Approve(CreditMovementKindV1 kind, string amount, DateOnly date, string key)
        {
            var input = ActualInput(account) with { Kind = kind, TransactionDate = date, AccountingDate = date,
                SourceDocument = new("SENTETIK " + key, date, "1"), Components = [new("principal", amount, null)],
                BankEffect = new(bank, amount, "SENTETIK documented " + key) };
            Guid movement;
            await using (var maker = run.OpenMovement(Maker))
            {
                var proposed = await maker.Service.ProposeAsync(maker.Context, Key(key + "-propose"), input, default);
                Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome); movement = proposed.Resource!.Id;
            }
            await using (var checker = run.OpenMovement(Checker))
            {
                var request = new CreditMovementDecisionV1(movement, "1", true, null);
                Assert.Equal(CommandOutcome.Succeeded, (await checker.Service.DecideAsync(checker.Context, Key(key + "-approve"), request, default)).Outcome);
                Assert.True((await checker.Service.DecideAsync(checker.Context, Key(key + "-approve"), request, default)).IsReplay);
            }
            originalSources.Add(movement, await run.ScalarAsync<string>("SELECT SourceJson FROM finance.CreditMovements WHERE Id=@id", ("@id", movement)));
        }
    }
}
