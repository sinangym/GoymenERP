namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// F-018 / plan S1 (FNS-D27(3)): the risk outcome that leaves a limit warning on an order. A warning never blocks the order and
/// asks for no separate approval; <see cref="SalesRiskOutcome.Within"/> leaves no warning.
/// </summary>
public enum SalesOrderRiskWarningOutcome
{
    /// <summary>The gross risk with this order is above the customer's credit limit.</summary>
    Over = 1,

    /// <summary>The risk could not be decided (fail-closed: never read as "within").</summary>
    Unavailable = 2,
}

/// <summary>
/// ASSUMPTION-SAT-RISK-4 (plan S1, reversible): why a warning was left. "Unavailable" keeps its own categories so a missing limit
/// is not confused with an owner that did not answer.
/// </summary>
public enum SalesOrderRiskWarningCategory
{
    /// <summary>Over the credit limit.</summary>
    OverLimit = 1,

    /// <summary>The customer-finance owner has no credit limit for the customer (Finance: <c>finance_credit_limit_not_set</c>).</summary>
    CreditLimitNotSet = 2,

    /// <summary>An owner of a risk input or the limit could not be read, or the evaluation itself failed.</summary>
    SourceUnavailable = 3,
}

/// <summary>
/// ASSUMPTION-SAT-RISK-1 (plan S1, reversible): what one warning records — the outcome, its category, the business day of the
/// check and the risk read contract version. No amount is recorded or disclosed (amounts stay with their owners).
/// </summary>
public sealed record SalesOrderRiskAssessment(
    SalesOrderRiskWarningOutcome Outcome,
    SalesOrderRiskWarningCategory Category,
    DateOnly CheckBusinessDay,
    string RiskSourceContract);

/// <summary>The one mapping from the calculator's evaluation to an order's limit warning (plan S1; reused by every caller).</summary>
public static class SalesOrderRiskWarnings
{
    /// <summary>The risk read contract the warning was decided on (FNS-D45: the Finance party-risk port shape is unchanged).</summary>
    public const string PartyRiskContractV1 = "sales.party-risk-read.v1";

    /// <summary>The audit event of a recorded warning (append-only; the order's own audit trail).</summary>
    public const string RecordedEventType = "sales.order.risk-warning.recorded.v1";

    /// <summary>The audit operation of a recorded warning.</summary>
    public const string RecordedOperation = "RecordRiskWarning";

    /// <summary>
    /// Null when the outcome is <see cref="SalesRiskOutcome.Within"/>; otherwise the warning. <c>Over</c> is <c>OverLimit</c>; an
    /// <c>Unavailable</c> outcome is <c>CreditLimitNotSet</c> when the limit owner answered "no source" or the calculator found no
    /// single limit amount, and <c>SourceUnavailable</c> for everything else.
    /// </summary>
    public static SalesOrderRiskAssessment? Classify(SalesRiskEvaluation evaluation, SalesRiskInput creditLimit, DateOnly checkBusinessDay)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(creditLimit);
        return evaluation.Outcome switch
        {
            SalesRiskOutcome.Within => null,
            SalesRiskOutcome.Over => new SalesOrderRiskAssessment(SalesOrderRiskWarningOutcome.Over,
                SalesOrderRiskWarningCategory.OverLimit, checkBusinessDay, PartyRiskContractV1),
            SalesRiskOutcome.Unavailable when creditLimit.State == SalesRiskInputState.NoSource
                || evaluation.Reasons.Any(reason => reason.Component == SalesRiskComponents.CreditLimit
                    && reason.Code == SalesRiskReasonCodes.CreditLimitNotDefined) =>
                new SalesOrderRiskAssessment(SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.CreditLimitNotSet,
                    checkBusinessDay, PartyRiskContractV1),
            _ => SourceUnavailable(checkBusinessDay),
        };
    }

    /// <summary>The warning of an evaluation that could not be made at all (an owner or the calculator failed).</summary>
    public static SalesOrderRiskAssessment SourceUnavailable(DateOnly checkBusinessDay) =>
        new(SalesOrderRiskWarningOutcome.Unavailable, SalesOrderRiskWarningCategory.SourceUnavailable, checkBusinessDay,
            PartyRiskContractV1);
}
