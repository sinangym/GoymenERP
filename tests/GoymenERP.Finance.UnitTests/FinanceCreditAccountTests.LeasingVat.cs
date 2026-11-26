using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceCreditAccountTests
{
    [Fact]
    public async Task Leasing_VAT_is_source_text_in_the_plan_store_approval_and_replay_and_does_not_replace_legacy_tax()
    {
        await using var run = new Run();
        var input = Request() with { Group = CreditAccountGroup.Leasing, Profile = Profile(CreditAccountGroup.Leasing),
            Installments = PlanFor(CreditAccountGroup.Leasing) };
        var proposed = await run.ProposeAsync(Maker, input, key: "vat-plan");
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        Assert.Equal("14.1515", proposed.Resource!.PlanTotals!.Vat);
        Assert.Equal("0", proposed.Resource.PlanTotals.Bsmv);
        Assert.Equal("0", proposed.Resource.PlanTotals.Kkdf);
        Assert.Equal(new[] { "8.2345", "5.917" }, proposed.Resource.Installments.Select(line => line.Vat));
        var approved = await run.ApproveAsync(Checker, new(proposed.Resource.Id, "1"));
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        var replay = await run.ProposeAsync(Maker, input, key: "vat-plan");
        Assert.True(replay.IsReplay);
        Assert.Equal(proposed.Resource.Id, replay.Resource!.Id);
        await using var db = run.Memory(CompanyA);
        Assert.Equal(new[] { "8.2345", "5.917" }, await db.CreditAccountInstallments.OrderBy(line => line.Sequence)
            .Select(line => line.VatText).ToArrayAsync());
        Assert.Empty(await db.OutboxMessages.ToArrayAsync());
        var changed = input with { Installments = [input.Installments[0] with { Vat = "8.2346" }, input.Installments[1]] };
        Assert.NotEqual(FinanceCreditAccountService.PayloadHash("SENTETIK", input), FinanceCreditAccountService.PayloadHash("SENTETIK", changed));
        Assert.DoesNotContain("\"vat\"", JsonSerializer.Serialize(Request(), FinanceJson.CreateOptions()));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1,20")]
    [InlineData("")]
    [InlineData("999")]
    public async Task Malformed_or_mismatched_leasing_VAT_cannot_be_proposed(string vat)
    {
        await using var run = new Run();
        var lines = PlanFor(CreditAccountGroup.Leasing); lines[0] = lines[0] with { Vat = vat };
        var result = await run.ProposeAsync(Maker, Request() with { Group = CreditAccountGroup.Leasing,
            Profile = Profile(CreditAccountGroup.Leasing), Installments = lines });
        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        await using var db = run.Memory(CompanyA);
        Assert.Empty(await db.CreditAccounts.ToArrayAsync());
    }

    [Fact]
    public async Task VAT_cannot_be_added_to_cash_credit_and_BSMV_is_not_silently_relabelled_as_leasing_VAT()
    {
        await using var run = new Run();
        var cash = Request() with { Installments = PlanFor(CreditAccountGroup.Leasing) };
        Assert.Equal(CommandOutcome.ValidationFailed, (await run.ProposeAsync(Maker, cash)).Outcome);
        var legacyTaxes = Request() with { Group = CreditAccountGroup.Leasing, Profile = Profile(CreditAccountGroup.Leasing) };
        Assert.Equal(CommandOutcome.ValidationFailed, (await run.ProposeAsync(Maker, legacyTaxes)).Outcome);
    }

    [Fact]
    public void Leasing_VAT_has_a_nullable_source_column_and_a_forward_fix_guard_without_backfill()
    {
        using var db = Context();
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var row = model.FindEntityType(typeof(CreditAccountInstallmentRow))!;
        Assert.True(row.FindProperty(nameof(CreditAccountInstallmentRow.VatText))!.IsNullable);
        Assert.Equal(FinanceDbContext.CreditAccountInstallmentVatCheck,
            row.GetCheckConstraints().Single(check => check.Name == "CK_CreditAccountInstallments_Vat").Sql);
        var migration = new AddLeasingInstallmentVat();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 52360", Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
