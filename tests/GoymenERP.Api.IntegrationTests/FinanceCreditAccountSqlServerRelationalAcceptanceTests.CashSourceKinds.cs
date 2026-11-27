using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Cash_kind_requires_its_documented_plan_and_maturity_without_inventing_installments_or_approving_legacy_mismatch()
    {
        await using var run = await Run.StartAsync();
        var input = Request();
        await using (var maker = run.Open(CompanyA, Maker))
        {
            var missingMaturity = input with { Profile = input.Profile! with { Cash = new(CreditCashKindV1.Spot, null) } };
            var refused = await maker.Credits.ProposeAsync(maker.Context, Key("cash-maturity"), missingMaturity, default);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal("profile.cash.maturityDate", Assert.Single(refused.Issues).Field);
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditAccounts WHERE CompanyId = @company", ("@company", CompanyA)));
        }

        input = input with
        {
            Profile = input.Profile! with { Cash = new(CreditCashKindV1.Overdraft, null), PlanNature = CreditPlanNatureV1.SourceMovements },
            Installments = []
        };
        CreditAccountView proposed;
        await using (var maker = run.Open(CompanyA, Maker))
        {
            var result = await maker.Credits.ProposeAsync(maker.Context, Key("cash-source"), input, default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            proposed = result.Resource!;
            Assert.Null(proposed.PlanTotals);
            Assert.Empty(proposed.Installments);
        }
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CreditAccountInstallments WHERE CompanyId = @company AND CreditAccountId = @id",
            ("@company", CompanyA), ("@id", proposed.Id)));
        await using (var restarted = run.Open(CompanyA, Maker))
        {
            var replay = await restarted.Credits.ProposeAsync(restarted.Context, Key("cash-source"), input, default);
            Assert.True(replay.IsReplay);
            Assert.Equal(run.Correlation, replay.OriginalCorrelationId);
            Assert.Equal(proposed.Id, replay.Resource!.Id);
        }

        // A legacy/inconsistent imported source is retained, never rewritten to manufacture a plan.
        var incompatible = input.Profile! with { Cash = new(CreditCashKindV1.Installment, input.StartDate.AddYears(1)) };
        var sourceJson = JsonSerializer.Serialize(incompatible, FinanceJson.CreateOptions());
        await run.ExecuteAsync("UPDATE finance.CreditAccounts SET SourceProfileJson = @json WHERE CompanyId = @company AND Id = @id",
            ("@json", sourceJson), ("@company", CompanyA), ("@id", proposed.Id));
        await using (var checker = run.Open(CompanyA, Checker))
        {
            var refused = await checker.Credits.ApproveAsync(checker.Context, Key("cash-invalid-approval"), new(proposed.Id, "1"), default);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal("profile.planNature", Assert.Single(refused.Issues).Field);
        }
        Assert.Equal(sourceJson, await run.ScalarAsync<string>("SELECT SourceProfileJson FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT [Status] FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditAccountCommands WHERE CreditAccountId = @id", ("@id", proposed.Id)));
    }
}
