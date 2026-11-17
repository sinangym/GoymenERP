using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Accounting.UnitTests;

public sealed class CreditMovementJournalComponentsV1Tests
{
    private static readonly Guid Company = Guid.NewGuid();
    internal static CreditMovementSourceV1 Source(string kind = "Drawdown") => new(Guid.NewGuid(), Company, Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), kind, new("SENTETIK statement", new(2026, 11, 18), "1"), new(2026, 11, 18), new(2026, 11, 18),
        [new("principal", "100", null), new("fee", "1.50", null), new("interest", "0", null)],
        new(Guid.NewGuid(), "98.50", "SENTETIK bank movement"), null, Guid.NewGuid().ToString("D"), "SENTETIK evidence");
    internal static NormalJournalPostingRuleV1 Rule(string kind, params (string Key, PostingSide Side)[] mappings)
    {
        var accounts = mappings.Select(_ => new AccountRef(Company, Guid.NewGuid())).ToArray(); var actor = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        var mirror = new AccountPlanMirrorV1(Company, "SENTETIK plan", new string('a', 64), actor, at,
            accounts.Select((x, i) => new AccountPlanMirrorRowV1(x, "SENTETIK-" + i, "SENTETIK account")).ToArray());
        return new(mirror, CreditMovementJournalComponentsV1.Purpose(kind)!, "SENTETIK-rule", actor, at,
            mappings.Select((x, i) => new NormalJournalMappingV1(x.Key, x.Side, accounts[i])).ToArray());
    }
    [Fact]
    public void Only_actual_document_components_and_bank_amounts_fill_approved_accounts_without_calculating_tax_or_exchange()
    {
        var source = Source(); var rule = Rule(source.Kind, ("credit.bank", PostingSide.Debit),
            ("credit.component.fee", PostingSide.Debit), ("credit.component.principal", PostingSide.Credit));
        var lines = Assert.IsAssignableFrom<IReadOnlyList<NormalJournalExactSourceLineV1>>(CreditMovementJournalComponentsV1.TryCreate(source, rule, 2));
        Assert.Equal(new[] { "98.5", "1.5", "100" }, lines.Select(x => x.Amount));
        Assert.DoesNotContain(lines, x => x.MappingKey.Contains("interest"));
        Assert.Null(CreditMovementJournalComponentsV1.TryCreate(source with { BankEffect = source.BankEffect! with { Amount = "98" } }, rule, 2));
        Assert.Null(CreditMovementJournalComponentsV1.TryCreate(source with { Components = [new("principal", "101", null), new("fee", "1.50", null)] }, rule, 2));
        Assert.True(NormalJournalSourceKindsV1.IsSupported(NormalJournalSourceKindsV1.RecordedCreditMovement));
    }
    [Fact]
    public void A_documented_accrual_total_is_exact_and_missing_component_mapping_cannot_be_filled_with_a_generic_account()
    {
        var source = Source("InterestAccrual") with { BankEffect = null, Components = [new("interest", "12.30", null), new("bsmv", "0.61", null)] };
        var rule = Rule(source.Kind, ("credit.component.interest", PostingSide.Debit), ("credit.component.bsmv", PostingSide.Debit),
            ("credit.total", PostingSide.Credit));
        var lines = CreditMovementJournalComponentsV1.TryCreate(source, rule, 2)!;
        Assert.Equal("12.91", lines.Single(x => x.MappingKey == "credit.total").Amount);
        Assert.Null(CreditMovementJournalComponentsV1.TryCreate(source, new NormalJournalPostingRuleV1(rule.Mirror, rule.PostingPurpose, rule.Version, rule.ApprovedByUserId, rule.ApprovedAtUtc, rule.Mappings.Where(x => !x.MappingKey.EndsWith("bsmv"))), 2));
        Assert.Null(CreditMovementJournalComponentsV1.TryCreate(source with { Components = [new("interest", "12.301", null), new("bsmv", "0.61", null)] }, rule, 2));
        Assert.Null(CreditMovementJournalComponentsV1.TryCreate(source with { ReversesMovementId = source.MovementId }, rule, 2));
    }
    [Fact]
    public void Large_component_sums_do_not_hide_a_missing_minor_unit_or_overflow_into_a_different_value()
    {
        var source = Source("InterestAccrual") with { BankEffect = null, Components = [
            new("interest", "333333333333333333333333333.33", null),
            new("fee", "333333333333333333333333333.33", null),
            new("commission", "333333333333333333333333333.33", null)] };
        var rule = Rule(source.Kind, ("credit.component.interest", PostingSide.Debit), ("credit.component.fee", PostingSide.Debit),
            ("credit.component.commission", PostingSide.Debit), ("credit.total", PostingSide.Credit));
        Assert.Null(CreditMovementJournalComponentsV1.TryCreate(source, rule, 2)); // exact 10^27-.01 cannot fit decimal; do not silently round it.
        Assert.Null(CreditMovementJournalComponentsV1.TryCreate(source with { Components = [new("interest", decimal.MaxValue.ToString(), null), new("fee", "1", null)] }, rule, 0));
    }
}
