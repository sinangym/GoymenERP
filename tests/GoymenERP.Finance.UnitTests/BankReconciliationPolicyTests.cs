using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class BankReconciliationPolicyTests
{
    private static readonly Guid CurrencyId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData(1, 1, BankReconciliationTopologyV1.OneToOne)]
    [InlineData(1, 2, BankReconciliationTopologyV1.OneToMany)]
    [InlineData(2, 1, BankReconciliationTopologyV1.ManyToOne)]
    [InlineData(2, 2, BankReconciliationTopologyV1.ManyToMany)]
    public void Evaluate_supports_all_locked_topologies(
        int statementCount,
        int eventCount,
        BankReconciliationTopologyV1 topology)
    {
        var statementLines = SplitStatement("100", statementCount, BankReconciliationDirectionV1.Inflow);
        var events = SplitEvents("100", eventCount, BankReconciliationDirectionV1.Inflow);

        var result = BankReconciliationPolicy.Evaluate(statementLines, events);

        Assert.Equal(topology, result.Topology);
        Assert.True(result.IsBalanced);
        Assert.Equal(new MoneyText("100", CurrencyId), result.Totals.BankInflow);
        Assert.Equal(new MoneyText("100", CurrencyId), result.Totals.FinanceInflow);
        Assert.Equal(new MoneyText("0", CurrencyId), result.Totals.BankOutflow);
        Assert.Equal(new MoneyText("0", CurrencyId), result.Totals.FinanceOutflow);
    }

    [Fact]
    public void Evaluate_balances_inflow_and_outflow_separately_without_netting()
    {
        var statementLines = new[]
        {
            Statement(1, "125.00", BankReconciliationDirectionV1.Inflow),
            Statement(2, "25", BankReconciliationDirectionV1.Outflow)
        };
        var events = new[]
        {
            Event(1, "100", BankReconciliationDirectionV1.Inflow),
            Event(2, "25", BankReconciliationDirectionV1.Inflow),
            Event(3, "25.0", BankReconciliationDirectionV1.Outflow)
        };

        var result = BankReconciliationPolicy.Evaluate(statementLines, events);

        Assert.True(result.IsBalanced);
        Assert.Equal(BankReconciliationTopologyV1.ManyToMany, result.Topology);
        Assert.Equal("125", result.Totals.BankInflow.Amount);
        Assert.Equal("25", result.Totals.BankOutflow.Amount);
        Assert.Equal("125", result.Totals.FinanceInflow.Amount);
        Assert.Equal("25", result.Totals.FinanceOutflow.Amount);
    }

    [Fact]
    public void Difference_remains_unbalanced_and_cannot_be_manually_closed_by_the_policy()
    {
        var result = BankReconciliationPolicy.Evaluate(
            [Statement(1, "100", BankReconciliationDirectionV1.Outflow)],
            [Event(1, "99.99", BankReconciliationDirectionV1.Outflow)]);

        Assert.False(result.IsBalanced);
        var error = Assert.Throws<BankReconciliationPolicyException>(
            () => BankReconciliationPolicy.RequireBalanced(result));
        Assert.Equal(BankReconciliationPolicyCodes.Unbalanced, error.Code);
        Assert.Equal("totals", error.Field);
    }

    [Fact]
    public void Duplicate_references_are_refused_on_each_side()
    {
        var statement = Statement(1, "50", BankReconciliationDirectionV1.Inflow);
        var financeEvent = Event(1, "50", BankReconciliationDirectionV1.Inflow);

        AssertCode(
            BankReconciliationPolicyCodes.DuplicateReference,
            "statementLines",
            () => BankReconciliationPolicy.Evaluate([statement, statement], [financeEvent]));
        AssertCode(
            BankReconciliationPolicyCodes.DuplicateReference,
            "financeEvents",
            () => BankReconciliationPolicy.Evaluate([statement], [financeEvent, financeEvent]));
    }

    [Fact]
    public void Currency_mismatch_is_refused_instead_of_converting_or_netting()
    {
        var otherCurrency = Guid.Parse("22222222-2222-2222-2222-222222222222");

        AssertCode(
            BankReconciliationPolicyCodes.CurrencyMismatch,
            "financeEvents",
            () => BankReconciliationPolicy.Evaluate(
                [Statement(1, "100", BankReconciliationDirectionV1.Inflow)],
                [Event(1, "100", BankReconciliationDirectionV1.Inflow, otherCurrency)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,00")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("1.2.3")]
    [InlineData("NaN")]
    public void Invalid_money_text_is_refused_without_zero_or_locale_coercion(string amount)
    {
        AssertCode(
            BankReconciliationPolicyCodes.InvalidCandidate,
            "statementLines",
            () => BankReconciliationPolicy.Evaluate(
                [Statement(1, amount, BankReconciliationDirectionV1.Inflow)],
                [Event(1, "1", BankReconciliationDirectionV1.Inflow)]));
    }

    [Fact]
    public void Missing_lists_empty_ids_and_unknown_directions_fail_closed()
    {
        AssertCode(
            BankReconciliationPolicyCodes.InvalidCandidate,
            "statementLines",
            () => BankReconciliationPolicy.Evaluate(
                [],
                [Event(1, "1", BankReconciliationDirectionV1.Inflow)]));
        AssertCode(
            BankReconciliationPolicyCodes.InvalidCandidate,
            "financeEvents",
            () => BankReconciliationPolicy.Evaluate(
                [Statement(1, "1", BankReconciliationDirectionV1.Inflow)],
                []));
        AssertCode(
            BankReconciliationPolicyCodes.InvalidCandidate,
            "statementLines",
            () => BankReconciliationPolicy.Evaluate(
                [new BankStatementReconciliationItemV1(
                    Guid.Empty,
                    BankReconciliationDirectionV1.Inflow,
                    new MoneyText("1", CurrencyId))],
                [Event(1, "1", BankReconciliationDirectionV1.Inflow)]));
        AssertCode(
            BankReconciliationPolicyCodes.InvalidCandidate,
            "financeEvents",
            () => BankReconciliationPolicy.Evaluate(
                [Statement(1, "1", BankReconciliationDirectionV1.Inflow)],
                [Event(1, "1", (BankReconciliationDirectionV1)999)]));
    }

    private static BankStatementReconciliationItemV1 Statement(
        int id,
        string amount,
        BankReconciliationDirectionV1 direction) =>
        new(GuidFrom(id), direction, new MoneyText(amount, CurrencyId));

    private static FinanceEventReconciliationItemV1 Event(
        int id,
        string amount,
        BankReconciliationDirectionV1 direction,
        Guid? currencyId = null) =>
        new(GuidFrom(id), direction, new MoneyText(amount, currencyId ?? CurrencyId));

    private static IReadOnlyList<BankStatementReconciliationItemV1> SplitStatement(
        string total,
        int count,
        BankReconciliationDirectionV1 direction) =>
        count switch
        {
            1 => [Statement(1, total, direction)],
            2 => [Statement(1, "40", direction), Statement(2, "60", direction)],
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };

    private static IReadOnlyList<FinanceEventReconciliationItemV1> SplitEvents(
        string total,
        int count,
        BankReconciliationDirectionV1 direction) =>
        count switch
        {
            1 => [Event(1, total, direction)],
            2 => [Event(1, "30", direction), Event(2, "70", direction)],
            _ => throw new ArgumentOutOfRangeException(nameof(count))
        };

    private static Guid GuidFrom(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    private static void AssertCode(string code, string field, Action action)
    {
        var error = Assert.Throws<BankReconciliationPolicyException>(action);
        Assert.Equal(code, error.Code);
        Assert.Equal(field, error.Field);
    }
}
