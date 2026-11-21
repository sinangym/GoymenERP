using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// F-018 / plan S2 (FNS-D30, SAT-D310): the risk result of one order at the moment of shipment, as the Logistics shipment gate
// reads it. Sales owns the answer, Logistics owns the gate rule: the plan's L-1..L-8 (Within passes; Over, CreditLimitNotSet and
// SourceUnavailable pass only with a today-valid single-shipment exception; a port error stops the shipment). No amount, limit or
// balance is ever answered (SAT-D162).

/// <summary>
/// The risk result at shipment time (plan S2). The default value is <see cref="Unavailable"/>: an answer nobody filled in is never
/// read as "within" (fail-closed).
/// </summary>
public enum SalesShipmentRiskResultV1
{
    /// <summary>The risk could not be decided: no credit limit is set, or an owner did not answer. Never read as "within".</summary>
    Unavailable = 0,

    /// <summary>The customer's gross risk with this order is within its credit limit: the shipment passes (L-1).</summary>
    Within = 1,

    /// <summary>The customer's gross risk with this order is above its credit limit: held without a valid exception (L-2).</summary>
    Over = 2,
}

/// <summary>Why the shipment is held when the result is not <see cref="SalesShipmentRiskResultV1.Within"/> (plan S2 mapping).</summary>
public enum SalesShipmentRiskHoldReasonV1
{
    /// <summary>Over the credit limit (FNS-D30).</summary>
    OverLimit = 1,

    /// <summary>The customer-finance owner has no credit limit for the customer (Finance: <c>finance_credit_limit_not_set</c>; SAT-D310).</summary>
    CreditLimitNotSet = 2,

    /// <summary>An owner of a risk input or the limit could not be read, or the evaluation itself failed (FNS-D30).</summary>
    SourceUnavailable = 3,
}

/// <summary>
/// The gate read: the order's risk result, the hold reason (null only when <see cref="SalesShipmentRiskResultV1.Within"/>), the
/// shipment's single-shipment risk exception as <see cref="ISalesShipmentRiskExceptionReadPortV1"/> answers it at the same moment
/// (embedded so the gate does not read twice), and the contract version. No amount, limit, balance, actor or reason is part of it
/// (SAT-D162).
/// </summary>
public sealed record SalesShipmentRiskGateReadV1(
    Guid OrderId,
    Guid LoadingInstructionId,
    SalesShipmentRiskResultV1 Result,
    SalesShipmentRiskHoldReasonV1? HoldReason,
    SalesShipmentRiskExceptionReadV1 Exception,
    string ContractVersion);

/// <summary>
/// The versioned read port the Logistics shipment gate consumes (plan S2; owner Sales, consumer Logistics STK-S27). It reads under
/// the caller's server-resolved company and creates or changes nothing. The consumer measures its own authority before asking;
/// the Sales evaluation behind it is a company-scoped system read that measures no Sales permission. The evaluation is the same
/// deterministic risk calculator the order's risk check and limit warning use (one calculator, never a second formula).
/// <list type="bullet">
/// <item>Another company's order, an unknown order or an empty id answers <see cref="SalesShipmentRiskResultV1.Unavailable"/> /
/// <see cref="SalesShipmentRiskHoldReasonV1.SourceUnavailable"/> with the exception <see cref="SalesShipmentRiskExceptionStatusV1.None"/>,
/// never a 403.</item>
/// <item>Without a resolved company it throws, and a lost authority stays an exception: the gate treats any error as a stop
/// (fail-closed). Any other failure of the evaluation is <c>SourceUnavailable</c>.</item>
/// <item>The gate rule is the consumer's: <c>Within</c> passes; every other result passes only when <see cref="SalesShipmentRiskGateReadV1.Exception"/>
/// is <see cref="SalesShipmentRiskExceptionStatusV1.Valid"/> (SAT-D310 — CreditLimitNotSet included), with the exception consumed by that
/// one shipment (SAT-D285).</item>
/// </list>
/// </summary>
public interface ISalesShipmentRiskGateReadPortV1
{
    Task<SalesShipmentRiskGateReadV1> GetAsync(Guid orderId, Guid loadingInstructionId, CancellationToken cancellationToken = default);
}

/// <summary>The stable identifiers of the gate read contract.</summary>
public static class SalesShipmentRiskGateContractV1
{
    /// <summary>The contract version every <see cref="SalesShipmentRiskGateReadV1"/> carries.</summary>
    public const string Version = "sales.shipment-risk-gate.v1";
}

/// <summary>The risk half of the gate answer: the result and its hold reason (never an amount).</summary>
public sealed record SalesShipmentRiskGateDecisionV1(SalesShipmentRiskResultV1 Result, SalesShipmentRiskHoldReasonV1? HoldReason);

/// <summary>
/// The Sales-internal source of the gate's risk half: the order's evaluation under the server-resolved company as a system read
/// (no Sales permission is measured; the consumer measured its own). Implemented by the draft service over the one risk calculator.
/// </summary>
public interface ISalesShipmentRiskGateSource
{
    Task<SalesShipmentRiskGateDecisionV1> EvaluateShipmentRiskGateAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The one mapping from the order's risk classification (the S1 warning's <see cref="SalesOrderRiskWarnings.Classify"/>) to the gate
/// decision, so the warning and the gate never disagree on a category.
/// </summary>
public static class SalesShipmentRiskGateDecisions
{
    /// <summary>Within: no hold reason.</summary>
    public static SalesShipmentRiskGateDecisionV1 Within { get; } = new(SalesShipmentRiskResultV1.Within, null);

    /// <summary>The decision of an evaluation that could not be made, or of an order this company cannot read.</summary>
    public static SalesShipmentRiskGateDecisionV1 SourceUnavailable { get; } =
        new(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.SourceUnavailable);

    /// <summary>Null (the calculator found the risk within the limit) is <see cref="Within"/>; otherwise the assessment's category.</summary>
    public static SalesShipmentRiskGateDecisionV1 From(SalesOrderRiskAssessment? assessment) => assessment switch
    {
        null => Within,
        { Outcome: SalesOrderRiskWarningOutcome.Over, Category: SalesOrderRiskWarningCategory.OverLimit } =>
            new(SalesShipmentRiskResultV1.Over, SalesShipmentRiskHoldReasonV1.OverLimit),
        { Outcome: SalesOrderRiskWarningOutcome.Unavailable, Category: SalesOrderRiskWarningCategory.CreditLimitNotSet } =>
            new(SalesShipmentRiskResultV1.Unavailable, SalesShipmentRiskHoldReasonV1.CreditLimitNotSet),
        // Everything else — SourceUnavailable, or a combination the mapping does not know — is never guessed: fail-closed.
        _ => SourceUnavailable,
    };
}
