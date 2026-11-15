using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// F-018 / plan S1 (FNS-D27(3)): the one mapping from the risk calculator's evaluation to an order's limit warning. "Within"
/// leaves no warning; "Over" is <c>OverLimit</c>; an undecided outcome keeps its own category (ASSUMPTION-SAT-RISK-4).
/// </summary>
public sealed class SalesOrderRiskWarningTests
{
    private static readonly DateOnly CheckDay = new(2026, 9, 18);

    [Fact]
    public void Within_leaves_no_warning_and_over_is_the_over_limit_warning_on_the_party_risk_contract()
    {
        var limit = SalesRiskInput.Available([new SalesRiskAmount(Guid.NewGuid(), 100m)], null);

        Assert.Null(SalesOrderRiskWarnings.Classify(Evaluation(SalesRiskOutcome.Within), limit, CheckDay));
        Assert.Equal(
            new SalesOrderRiskAssessment(SalesOrderRiskWarningOutcome.Over, SalesOrderRiskWarningCategory.OverLimit, CheckDay,
                "sales.party-risk-read.v1"),
            SalesOrderRiskWarnings.Classify(Evaluation(SalesRiskOutcome.Over), limit, CheckDay));
    }

    [Fact]
    public void An_undecided_outcome_is_credit_limit_not_set_only_when_the_limit_has_no_source_or_no_single_amount()
    {
        // Finance on main: a customer without a limit is "no source" (finance_credit_limit_not_set).
        var notSet = SalesOrderRiskWarnings.Classify(Evaluation(SalesRiskOutcome.Unavailable),
            SalesRiskInput.NoSource("finance_credit_limit_not_set"), CheckDay);
        Assert.Equal((SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.CreditLimitNotSet),
            (notSet!.Outcome, notSet.Category));

        // The calculator found no single limit amount.
        var notDefined = SalesOrderRiskWarnings.Classify(
            Evaluation(SalesRiskOutcome.Unavailable, new SalesRiskReason(SalesRiskComponents.CreditLimit, SalesRiskReasonCodes.CreditLimitNotDefined)),
            SalesRiskInput.Available([], null), CheckDay);
        Assert.Equal(SalesOrderRiskWarningCategory.CreditLimitNotSet, notDefined!.Category);

        // An unreadable limit or an unreadable exposure part beside a readable limit is the source's own category.
        foreach (var (evaluation, limit) in new[]
                 {
                     (Evaluation(SalesRiskOutcome.Unavailable), SalesRiskInput.Unavailable("finance_credit_limit_unreadable")),
                     (Evaluation(SalesRiskOutcome.Unavailable), SalesRiskInput.Unavailable("finance_customer_unidentified")),
                     (Evaluation(SalesRiskOutcome.Unavailable, new SalesRiskReason(SalesRiskComponents.OpenReceivables, "synthetic_unreadable")),
                         SalesRiskInput.Available([new SalesRiskAmount(Guid.NewGuid(), 100m)], null)),
                 })
        {
            Assert.Equal(SalesOrderRiskWarnings.SourceUnavailable(CheckDay), SalesOrderRiskWarnings.Classify(evaluation, limit, CheckDay));
        }

        Assert.Equal(
            new SalesOrderRiskAssessment(SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.SourceUnavailable, CheckDay,
                SalesOrderRiskWarnings.PartyRiskContractV1),
            SalesOrderRiskWarnings.SourceUnavailable(CheckDay));
    }

    [Fact]
    public void The_recorded_event_and_operation_names_are_stable()
    {
        Assert.Equal(("sales.order.risk-warning.recorded.v1", "RecordRiskWarning"),
            (SalesOrderRiskWarnings.RecordedEventType, SalesOrderRiskWarnings.RecordedOperation));
        Assert.Throws<ArgumentNullException>(() => SalesOrderRiskWarnings.Classify(null!, SalesRiskInput.NoSource("x"), CheckDay));
    }

    private static SalesRiskEvaluation Evaluation(SalesRiskOutcome outcome, params SalesRiskReason[] reasons) =>
        new(outcome, reasons, null, null, null);
}
