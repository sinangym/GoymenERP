using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// STK-S27: the customer risk gate of a definite shipment (L-1..L-7, FNS-D30, SAT-D310, SAT-D285). The refusals are
/// terminal 409s (recorded, the same key replays them); a gate that cannot be read is a 503 that records nothing.
/// No amount, limit or risk figure is carried (SAT-D162): only the Sales owner's result, hold reason and exception.
/// </summary>
public static class LogisticsShipmentRiskGateCodesV1
{
    /// <summary>The customer's risk is over its limit and no valid single-shipment exception exists.</summary>
    public const string OverLimit = "logistics_shipment_risk_over_limit";

    /// <summary>The customer has no valid credit limit (FNS-D30 (1)) and no valid single-shipment exception exists.</summary>
    public const string CreditLimitNotSet = "logistics_shipment_risk_credit_limit_not_set";

    /// <summary>The customer's risk could not be read (SAT-D310) and no valid single-shipment exception exists.</summary>
    public const string RiskUnavailable = "logistics_shipment_risk_unavailable";

    /// <summary>The valid exception was already consumed by another definite shipment of this order (SAT-D285 (1)).</summary>
    public const string ExceptionUsed = "logistics_shipment_risk_exception_used";

    /// <summary>
    /// 503 (never recorded, L-7): the Sales gate port is not registered or failed. The endpoint answers every 503
    /// code-less; the code is kept for the service contract and its tests.
    /// </summary>
    public const string GateUnavailable = "logistics_shipment_risk_gate_unavailable";

    /// <summary>The closed 409 refusals of the gate; only these become durable terminal attempts.</summary>
    public static IReadOnlySet<string> TerminalCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        OverLimit,
        CreditLimitNotSet,
        RiskUnavailable,
        ExceptionUsed,
    };

    /// <summary>Field of the refusal error that names the Sales exception state (no amount).</summary>
    public const string ExceptionErrorField = "riskException";
}

/// <summary>
/// L-6 server-side texts of the three gate stops (plus the consumed exception). The browser shows its own localized copy
/// from the Logistics text resource keyed by the stable code; changing a text changes no rule (N-10).
/// </summary>
public static class LogisticsShipmentRiskGateTextsV1
{
    public const string OverLimit = "Müşteri risk limiti aşıldı; geçerli tek sevk istisnası olmadan kesin sevk kaydedilmez.";
    public const string CreditLimitNotSet = "Müşterinin geçerli kredi limiti tanımlı değil; geçerli tek sevk istisnası olmadan kesin sevk kaydedilmez.";
    public const string RiskUnavailable = "Müşteri riski okunamadı; geçerli tek sevk istisnası olmadan kesin sevk kaydedilmez.";
    public const string ExceptionUsed = "Bu siparişin tek sevk istisnası başka bir kesin sevkte kullanıldı; yeni istisna talebi ve onayı gerekir.";
}

/// <summary>
/// What the gate let through, stored with the shipment: the Logistics audit snapshot, the single-use record of a consumed
/// exception and the definite-shipment outbox event. <see cref="RiskExceptionNo"/> is set only when a valid exception
/// carried a stop (Over / Unavailable); a shipment within the limit consumes nothing even if an exception exists.
/// </summary>
public sealed record LogisticsShipmentRiskGateFactsV1(
    Guid ShipmentId,
    Guid SalesOrderId,
    Guid LoadingInstructionId,
    string Result,
    string? HoldReason,
    string ExceptionStatus,
    int? RiskExceptionNo,
    string ContractVersion);

/// <summary>The gate's verdict on one Sales answer: either pass with facts, or a terminal refusal code.</summary>
public sealed record LogisticsShipmentRiskGateDecisionV1(
    LogisticsShipmentRiskGateFactsV1? Facts,
    string? RefusalCode,
    string ExceptionStatus)
{
    public bool Passes => Facts is not null;
}

/// <summary>
/// The pure gate rule. <see cref="Evaluate"/> answers <see langword="null"/> when the owner's answer is not a valid
/// answer to THIS question (another order/instruction, another contract, an inconsistent result/reason pair, a valid
/// exception without its number or date); the caller turns that into a 503 and records nothing.
/// </summary>
public static class LogisticsShipmentRiskGate
{
    public static LogisticsShipmentRiskGateDecisionV1? Evaluate(SalesShipmentRiskGateReadV1? read, Guid shipmentId,
        Guid salesOrderId, Guid loadingInstructionId)
    {
        if (read is null || read.Exception is not { } exception || salesOrderId == Guid.Empty || loadingInstructionId == Guid.Empty
            || read.OrderId != salesOrderId || read.LoadingInstructionId != loadingInstructionId
            || exception.OrderId != salesOrderId || exception.LoadingInstructionId != loadingInstructionId
            || !string.Equals(read.ContractVersion, SalesShipmentRiskGateContractV1.Version, StringComparison.Ordinal)
            || !Enum.IsDefined(read.Result) || !Enum.IsDefined(exception.Status)
            || read.HoldReason is { } definedReason && !Enum.IsDefined(definedReason))
        {
            return null;
        }

        var consistent = read.Result switch
        {
            SalesShipmentRiskResultV1.Within => read.HoldReason is null,
            SalesShipmentRiskResultV1.Over => read.HoldReason == SalesShipmentRiskHoldReasonV1.OverLimit,
            SalesShipmentRiskResultV1.Unavailable => read.HoldReason is SalesShipmentRiskHoldReasonV1.CreditLimitNotSet
                or SalesShipmentRiskHoldReasonV1.SourceUnavailable,
            _ => false,
        };
        var valid = exception.Status == SalesShipmentRiskExceptionStatusV1.Valid;
        if (!consistent || valid && (exception.ExceptionNo is not >= 1 || exception.ValidOn is null))
        {
            return null;
        }

        var status = StatusText(exception.Status);
        if (read.Result == SalesShipmentRiskResultV1.Within)
        {
            return new(Facts(read, shipmentId, status, null), null, status);
        }

        if (valid)
        {
            return new(Facts(read, shipmentId, status, exception.ExceptionNo), null, status);
        }

        var code = read.HoldReason switch
        {
            SalesShipmentRiskHoldReasonV1.OverLimit => LogisticsShipmentRiskGateCodesV1.OverLimit,
            SalesShipmentRiskHoldReasonV1.CreditLimitNotSet => LogisticsShipmentRiskGateCodesV1.CreditLimitNotSet,
            _ => LogisticsShipmentRiskGateCodesV1.RiskUnavailable,
        };
        return new(null, code, status);
    }

    /// <summary>Lower-case stable names stored in the audit snapshot and the refusal error (never a localized text).</summary>
    public static string StatusText(SalesShipmentRiskExceptionStatusV1 status) => status switch
    {
        SalesShipmentRiskExceptionStatusV1.None => "none",
        SalesShipmentRiskExceptionStatusV1.Pending => "pending",
        SalesShipmentRiskExceptionStatusV1.Valid => "valid",
        SalesShipmentRiskExceptionStatusV1.Lapsed => "lapsed",
        SalesShipmentRiskExceptionStatusV1.Rejected => "rejected",
        SalesShipmentRiskExceptionStatusV1.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static LogisticsShipmentRiskGateFactsV1 Facts(SalesShipmentRiskGateReadV1 read, Guid shipmentId, string status,
        int? riskExceptionNo) =>
        new(shipmentId, read.OrderId, read.LoadingInstructionId, ResultText(read.Result), ReasonText(read.HoldReason), status,
            riskExceptionNo, read.ContractVersion);

    private static string ResultText(SalesShipmentRiskResultV1 result) => result switch
    {
        SalesShipmentRiskResultV1.Within => "within",
        SalesShipmentRiskResultV1.Over => "over",
        _ => "unavailable",
    };

    private static string? ReasonText(SalesShipmentRiskHoldReasonV1? reason) => reason switch
    {
        null => null,
        SalesShipmentRiskHoldReasonV1.OverLimit => "over_limit",
        SalesShipmentRiskHoldReasonV1.CreditLimitNotSet => "credit_limit_not_set",
        _ => "source_unavailable",
    };
}

/// <summary>
/// Optional store extension: the definite shipment write plus, in the SAME save, the gate facts in the audit snapshot,
/// the single-use record of a consumed exception (unique per company, order and exception number; a second use is
/// <see cref="LogisticsShipmentRiskGateCodesV1.ExceptionUsed"/>) and the
/// <c>logistics.definite-shipment.recorded.v1</c> outbox row. A replay of the same key returns the stored result and
/// writes nothing.
/// </summary>
public interface ILogisticsShipmentRiskGateStoreV1
{
    Task<LogisticsStoreResult?> MutateInstructionWithShipmentRiskGateAsync(LogisticsWriteEnvelope envelope, string requestHash,
        string commandName, Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit,
        LogisticsShipmentRiskGateFactsV1 gate, CancellationToken cancellationToken = default);
}
