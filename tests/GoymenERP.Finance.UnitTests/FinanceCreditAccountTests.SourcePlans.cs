using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
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
    [InlineData(0, "101", "101.0001")]
    [InlineData(2, "100.01", "100.010001")]
    [InlineData(3, "100.001", "100.001001")]
    public void Imported_installments_use_one_smallest_currency_unit_without_conversion_or_rounding(int scale, string within, string outside)
    {
        CreditPlanInstallmentInput Line(string installment) => new(1, Start, installment, "100", "0", "0", "0");
        var accepted = FinanceCreditPlanCalculator.Validate("100", [Line(within)], scale);
        Assert.True(accepted.IsValid);
        Assert.Equal(within, accepted.Totals!.Installment);
        Assert.Contains(FinanceCreditPlanCalculator.Validate("100", [Line(outside)], scale).Issues,
            x => x.Code == FinanceCreditPlanCalculator.InstallmentSumMismatch);
        Assert.Contains(FinanceCreditPlanCalculator.Validate("100.01", [Line(within)], scale).Issues,
            x => x.Code == FinanceCreditPlanCalculator.PrincipalTotalMismatch);
    }

    [Fact]
    public void Bank_document_included_charge_enters_the_installment_and_separate_charge_remains_separate()
    {
        var line = new CreditPlanInstallmentInput(1, Start, "117", "100", "10", "1", "1",
            [new("SENTETIK komisyon", "5", CreditChargeTreatmentV1.IncludedInInstallment, "BANK-1"),
             new("SENTETIK ayrı masraf", "9", CreditChargeTreatmentV1.CollectedSeparately, "BANK-2")]);
        var verdict = FinanceCreditPlanCalculator.Validate("100", [line], 2);
        Assert.True(verdict.IsValid);
        Assert.Equal(new CreditPlanChargeTotalsV1("5", "9"), verdict.Totals!.Charges);
        Assert.Equal("117", verdict.Totals.Installment);
        var missing = FinanceCreditPlanCalculator.Validate("100", [line with
            { Charges = [line.Charges![0] with { Amount = null! }] }], 2);
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
    public async Task Source_charge_and_profile_snapshot_survive_persistence_and_replay_without_recalculation()
    {
        await using var run = new Run();
        var plan = Plan();
        plan[0] = plan[0] with { Installment = "546.2345", Charges = [new("SENTETIK masraf", "5",
            CreditChargeTreatmentV1.IncludedInInstallment, "BANK-1")] };
        var proposed = await run.ProposeAsync(Maker, Request() with { Installments = plan });
        var read = await run.GetAsync(Checker, proposed.Resource!.Id);
        Assert.Equal(proposed.Resource.Profile, read.Resource!.Profile);
        Assert.Equal("5", read.Resource.PlanTotals!.Charges!.IncludedInInstallment);
        Assert.Equal("546.2345", read.Resource.Installments[0].Installment);
        Assert.Equal(1, read.Resource.Tolerance!.ProfileVersion);
    }
}
