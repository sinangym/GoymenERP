using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Leasing_VAT_survives_approval_and_replay_and_its_SQL_check_and_Down_guard_preserve_the_source()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddLeasingInstallmentVat().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var input = Request();
        input = input with { Group = CreditAccountGroup.Leasing,
            Profile = input.Profile! with { Cash = null, Leasing = new("SENTETIK lease asset", input.StartDate, input.StartDate.AddYears(2)) },
            Installments = [new(1, input.StartDate.AddMonths(1), "1150.000", "1000.00", "50.000", "0", "0", Vat: "100.000")] };
        CreditAccountView proposed;
        await using (var maker = run.Open(CompanyA, Maker))
        {
            var result = await maker.Credits.ProposeAsync(maker.Context, Key("vat-propose"), input, default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            proposed = result.Resource!;
            Assert.Equal("100.000", proposed.PlanTotals!.Vat);
        }
        await using (var checker = run.Open(CompanyA, Checker))
        {
            var result = await checker.Credits.ApproveAsync(checker.Context, Key("vat-approve"), new(proposed.Id, "1"), default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            Assert.Equal("100.000", Assert.Single(result.Resource!.Installments).Vat);
        }
        await using (var restarted = run.Open(CompanyA, Maker))
        {
            var replay = await restarted.Credits.ProposeAsync(restarted.Context, Key("vat-propose"), input, default);
            Assert.True(replay.IsReplay);
            Assert.Equal(proposed.Id, replay.Resource!.Id);
        }
        Assert.Equal("100.000", await run.ScalarAsync<string>(
            "SELECT VatText FROM finance.CreditAccountInstallments WHERE CompanyId = @company AND CreditAccountId = @id",
            ("@company", CompanyA), ("@id", proposed.Id)));
        var invalid = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditAccountInstallments SET VatText = '-1' WHERE CreditAccountId = @id", ("@id", proposed.Id)));
        Assert.Equal(547, invalid.Number);
        Assert.Equal(52360, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CreditAccountInstallments WHERE CreditAccountId = @id AND VatText = '100.000'",
            ("@id", proposed.Id)));
    }
}
