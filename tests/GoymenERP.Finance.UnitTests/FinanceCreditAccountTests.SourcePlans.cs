using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Fact]
    public void Source_profile_snapshot_is_complete_and_rollback_refuses_before_removing_lender_proof_or_bank_charges()
    {
        using var context = Context();
        Assert.False(context.Database.HasPendingModelChanges());
        var model = context.GetService<IDesignTimeModel>().Model;
        var account = model.FindEntityType(typeof(CreditAccountRow))!;
        Assert.True(account.FindProperty(nameof(CreditAccountRow.SourceProfileJson))!.IsNullable);
        Assert.Equal(FinanceDbContext.CreditAccountSourceProfileCheck,
            Assert.Single(account.GetCheckConstraints(), x => x.Name == "CK_CreditAccounts_SourceProfile").Sql);
        var line = model.FindEntityType(typeof(CreditAccountInstallmentRow))!;
        Assert.Equal(FinanceDbContext.CreditAccountInstallmentChargesCheck,
            Assert.Single(line.GetCheckConstraints(), x => x.Name == "CK_CreditAccountInstallments_Charges").Sql);
        var migration = new AddCreditSourceProfiles();
        Assert.DoesNotContain(migration.UpOperations, x => x is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.Contains("THROW 51961", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[ChargesJson] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("100.01", "100.010001")]
    [InlineData("99.99", "99.989999")]
    public void Turkish_lira_installments_hold_within_one_hundredth_of_a_lira_per_line(string within, string outside)
    {
        CreditPlanInstallmentInput Line(string installment) => new(1, Start, installment, "100", "0", "0", "0");
        var accepted = FinanceCreditPlanCalculator.Validate("100", [Line(within)], SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.True(accepted.IsValid);
        Assert.Equal(within, accepted.Totals!.Installment);
        Assert.Contains(FinanceCreditPlanCalculator.Validate("100", [Line(outside)], SettingsCurrencyIdentitiesV1.TurkishLira).Issues,
            x => x.Code == FinanceCreditPlanCalculator.InstallmentSumMismatch);
        Assert.Contains(FinanceCreditPlanCalculator.Validate("100.01", [Line(within)], SettingsCurrencyIdentitiesV1.TurkishLira).Issues,
            x => x.Code == FinanceCreditPlanCalculator.PrincipalTotalMismatch);
    }

    [Fact]
    public void A_currency_without_a_decided_tolerance_is_refused_and_never_gets_its_smallest_unit()
    {
        var line = new CreditPlanInstallmentInput(1, Start, "100", "100", "0", "0", "0");
        foreach (var currency in new[] { SettingsCurrencyIdentitiesV1.UnitedStatesDollar, SettingsCurrencyIdentitiesV1.Euro })
        {
            var verdict = FinanceCreditPlanCalculator.Validate("100", [line], currency);
            Assert.False(verdict.IsValid);
            var issue = Assert.Single(verdict.Issues);
            Assert.Equal(("currencyId", FinanceCreditPlanCalculator.ToleranceUndecided), (issue.Field, issue.Code));
        }
    }

    [Fact]
    public void A_bank_plan_charge_is_refused_fail_closed_and_never_enters_the_installment()
    {
        var included = new CreditPlanInstallmentInput(1, Start, "117", "100", "10", "1", "1",
            [new("SENTETIK komisyon", "5", CreditChargeTreatmentV1.IncludedInInstallment, "BANK-1")]);
        var verdict = FinanceCreditPlanCalculator.Validate("100", [included], SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.False(verdict.IsValid);
        // 117 = 100 + 10 + 1 + 1 + 5 holds only if the charge is added; FNS-D99's equality has no charge term.
        Assert.Contains(verdict.Issues, x => (x.Field, x.Code) == ("installments[0].charges", FinanceCreditPlanCalculator.ChargesUndecided));
        Assert.Contains(verdict.Issues, x => (x.Field, x.Code) == ("installments[0].installment", FinanceCreditPlanCalculator.InstallmentSumMismatch));

        var separate = new CreditPlanInstallmentInput(1, Start, "112", "100", "10", "1", "1",
            [new("SENTETIK ayrı masraf", "9", CreditChargeTreatmentV1.CollectedSeparately, "BANK-2")]);
        var separateVerdict = FinanceCreditPlanCalculator.Validate("100", [separate], SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.False(separateVerdict.IsValid);
        var issue = Assert.Single(separateVerdict.Issues);
        Assert.Equal(("installments[0].charges", FinanceCreditPlanCalculator.ChargesUndecided), (issue.Field, issue.Code));

        var missing = FinanceCreditPlanCalculator.Validate("100", [included with
            { Charges = [included.Charges![0] with { Amount = null! }] }], SettingsCurrencyIdentitiesV1.TurkishLira);
        Assert.False(missing.IsValid);
        Assert.Null(missing.Totals);
    }

    [Fact]
    public async Task Noncash_source_movement_profile_keeps_no_synthetic_installments_and_still_requires_another_approver()
    {
        await using var run = new Run();
        var request = Request() with { Group = CreditAccountGroup.NonCash, Profile = Profile(CreditAccountGroup.NonCash), Installments = [] };
        var proposed = await run.ProposeAsync(Maker, request);
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        Assert.Null(proposed.Resource!.PlanTotals);
        Assert.Empty(proposed.Resource.Installments);
        Assert.Equal("0.01", proposed.Resource.Tolerance!.SmallestCurrencyUnit);
        Assert.Equal(CommandOutcome.Denied, (await run.ApproveAsync(Maker, new(proposed.Resource.Id, "1"))).Outcome);
        Assert.Equal(CommandOutcome.Succeeded, (await run.ApproveAsync(Checker, new(proposed.Resource.Id, "1"))).Outcome);
        Assert.Empty(await run.InstallmentsAsync());
    }

    [Fact]
    public async Task New_proposal_without_source_profile_cannot_fabricate_lender_identity_or_bank_document()
    {
        await using var run = new Run();
        var result = await run.ProposeAsync(Maker, Request() with { Profile = null });
        AssertIssue(result, CommandOutcome.ValidationFailed, "profile", "credit_source_profile_invalid");
        Assert.Empty(await run.AllRowsAsync());
    }

    [Fact]
    public async Task Missing_real_currency_profile_or_lender_owner_cannot_fall_back_to_a_display_name_or_fixed_tolerance()
    {
        await using var run = new Run { Settings = _ => null };
        AssertIssue(await run.ProposeAsync(Maker, Request()), CommandOutcome.Unavailable, "profile", "credit_plan_profile_unavailable");
        run.Settings = company => new SyntheticCreditSources(company);
        run.Lenders = _ => null;
        AssertIssue(await run.ProposeAsync(Maker, Request()), CommandOutcome.Unavailable, "profile.lender", "credit_lender_source_unavailable");
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task Approval_refuses_a_changed_money_profile_or_now_inactive_lender_without_rewriting_the_original_plan()
    {
        var sources = new SyntheticCreditSources(CompanyA);
        await using var run = new Run { Settings = _ => sources, Lenders = _ => sources };
        var proposed = (await run.ProposeAsync(Maker, Request())).Resource!;
        sources.ProfileVersion = 2;
        AssertIssue(await run.ApproveAsync(Checker, new(proposed.Id, "1")), CommandOutcome.VersionConflict, "profile", "credit_plan_profile_changed");
        sources.ProfileVersion = 1;
        sources.LenderStatus = FinanceCreditLenderStatusV1.Inactive;
        AssertIssue(await run.ApproveAsync(Checker, new(proposed.Id, "1")), CommandOutcome.ValidationFailed, "profile.lender", "credit_lender_not_active");
        Assert.Equal(1, (await run.RowAsync(proposed.Id)).Version);
    }

    [Fact]
    public async Task Unknown_source_enum_is_a_validation_result_before_strict_json_hashing()
    {
        await using var run = new Run();
        AssertIssue(await run.ProposeAsync(Maker, Request() with { Profile = Profile(CreditAccountGroup.Cash) with
            { PlanNature = (CreditPlanNatureV1)99 } }), CommandOutcome.ValidationFailed, "profile", "credit_source_profile_invalid");
        Assert.Empty(await run.AllRowsAsync());
    }

    [Fact]
    public async Task Source_profile_snapshot_survives_persistence_and_replay_and_a_charged_plan_stores_nothing()
    {
        await using var run = new Run();
        var plan = Plan();
        plan[0] = plan[0] with { Charges = [new("SENTETIK masraf", "5",
            CreditChargeTreatmentV1.CollectedSeparately, "BANK-1")] };
        AssertIssue(await run.ProposeAsync(Maker, Request() with { Installments = plan }), CommandOutcome.ValidationFailed,
            "installments[0].charges", FinanceCreditPlanCalculator.ChargesUndecided);
        Assert.Empty(await run.AllRowsAsync());

        var proposed = await run.ProposeAsync(Maker, Request());
        var read = await run.GetAsync(Checker, proposed.Resource!.Id);
        Assert.Equal(proposed.Resource.Profile, read.Resource!.Profile);
        Assert.Null(read.Resource.PlanTotals!.Charges);
        Assert.Equal(Plan()[0].Installment, read.Resource.Installments[0].Installment);
        Assert.Equal(1, read.Resource.Tolerance!.ProfileVersion);
    }
}
