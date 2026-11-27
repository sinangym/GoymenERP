using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Theory]
    [InlineData(CreditCashKindV1.Spot, false)]
    [InlineData(CreditCashKindV1.Installment, false)]
    [InlineData(CreditCashKindV1.ForeignCurrencyIndexed, false)]
    [InlineData(CreditCashKindV1.Export, false)]
    [InlineData(CreditCashKindV1.Revolving, true)]
    [InlineData(CreditCashKindV1.Overdraft, true)]
    [InlineData(CreditCashKindV1.CreditCard, true)]
    public async Task Fns_d124_cash_kind_keeps_the_approved_source_plan_and_replays_without_synthetic_installments(
        CreditCashKindV1 kind, bool movements)
    {
        await using var run = new Run();
        var request = CashSourceRequest(kind, movements);
        var proposed = await run.ProposeAsync(Maker, request, key: "SENTETIK-cash-source", correlation: "SENTETIK-original");
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        Assert.Equal(request.Profile, proposed.Resource!.Profile);
        if (movements)
        {
            Assert.Null(proposed.Resource.PlanTotals);
            Assert.Empty(await run.InstallmentsAsync());
        }
        else
        {
            Assert.NotNull(proposed.Resource.PlanTotals);
            Assert.Equal(request.Installments.Select(x => x.Installment), proposed.Resource.Installments.Select(x => x.Installment));
        }
        var replay = await run.ProposeAsync(Maker, request, key: "SENTETIK-cash-source", correlation: "SENTETIK-second");
        Assert.True(replay.IsReplay);
        Assert.Equal("SENTETIK-original", replay.OriginalCorrelationId);
        Assert.Single(await run.AllRowsAsync());
        Assert.Equal(CommandOutcome.Succeeded, (await run.ApproveAsync(Checker, new(proposed.Resource.Id, "1"))).Outcome);
    }

    [Theory]
    [InlineData(CreditCashKindV1.Spot, false)]
    [InlineData(CreditCashKindV1.Installment, false)]
    [InlineData(CreditCashKindV1.ForeignCurrencyIndexed, false)]
    [InlineData(CreditCashKindV1.Export, false)]
    [InlineData(CreditCashKindV1.Revolving, true)]
    [InlineData(CreditCashKindV1.Overdraft, true)]
    [InlineData(CreditCashKindV1.CreditCard, true)]
    public async Task Fns_d124_cash_kind_cannot_choose_the_other_plan_nature(CreditCashKindV1 kind, bool movements)
    {
        await using var run = new Run();
        var request = CashSourceRequest(kind, !movements);
        AssertIssue(await run.ProposeAsync(Maker, request), CommandOutcome.ValidationFailed,
            "profile.planNature", "credit_source_profile_invalid");
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.InstallmentsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Theory]
    [InlineData(CreditCashKindV1.Spot)]
    [InlineData(CreditCashKindV1.Installment)]
    public async Task Fns_d124_spot_and_installment_require_the_documented_maturity(CreditCashKindV1 kind)
    {
        await using var run = new Run();
        var request = CashSourceRequest(kind, false);
        request = request with { Profile = request.Profile! with { Cash = new(kind, null) } };
        AssertIssue(await run.ProposeAsync(Maker, request), CommandOutcome.ValidationFailed,
            "profile.cash.maturityDate", "credit_source_profile_invalid");
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task Approval_revalidates_a_stored_cash_kind_instead_of_accepting_a_legacy_incompatible_plan()
    {
        await using var run = new Run();
        var proposed = (await run.ProposeAsync(Maker, Request())).Resource!;
        await using (var db = run.Memory(CompanyA))
        {
            var row = await db.CreditAccounts.SingleAsync(x => x.Id == proposed.Id);
            row.SourceProfileJson = JsonSerializer.Serialize(proposed.Profile! with
                { Cash = new(CreditCashKindV1.Revolving, null) }, FinanceJson.CreateOptions());
            await db.SaveChangesAsync();
        }
        AssertIssue(await run.ApproveAsync(Checker, new(proposed.Id, "1")), CommandOutcome.ValidationFailed,
            "profile.planNature", "credit_source_profile_invalid");
        Assert.Equal((FinanceCreditAccountService.Proposed, 1L),
            ((await run.RowAsync(proposed.Id)).Status, (await run.RowAsync(proposed.Id)).Version));
        Assert.Single(await run.CommandsAsync());
        Assert.Single(await run.AuditsAsync(proposed.Id));
        Assert.Equal(CommandOutcome.Succeeded, (await run.RejectAsync(Checker, new(proposed.Id, "1", Reason))).Outcome);
    }

    private static CreditAccountProposeRequest CashSourceRequest(CreditCashKindV1 kind, bool movements) => Request() with
    {
        Profile = Profile(CreditAccountGroup.Cash) with
        {
            Cash = new(kind, kind is CreditCashKindV1.Spot or CreditCashKindV1.Installment ? Start.AddYears(1) : null),
            PlanNature = movements ? CreditPlanNatureV1.SourceMovements : CreditPlanNatureV1.Installments
        },
        Installments = movements ? [] : Plan()
    };
}
