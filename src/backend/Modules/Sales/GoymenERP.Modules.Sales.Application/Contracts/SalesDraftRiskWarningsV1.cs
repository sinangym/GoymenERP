namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>F-018 / plan S1: the read of an order's recorded limit warnings.</summary>
public static class SalesDraftRiskWarningsApiV1
{
    /// <summary>The most warnings one read answers, newest first (a technical page bound, not a business rule).</summary>
    public const int MaximumListed = 100;
}

/// <summary>The outcome names of a recorded warning on the wire.</summary>
public static class SalesDraftRiskWarningOutcomesV1
{
    public const string Over = "Over";
    public const string Unavailable = "Unavailable";
}

/// <summary>The category names of a recorded warning on the wire (ASSUMPTION-SAT-RISK-4).</summary>
public static class SalesDraftRiskWarningCategoriesV1
{
    public const string OverLimit = "OverLimit";
    public const string CreditLimitNotSet = "CreditLimitNotSet";
    public const string SourceUnavailable = "SourceUnavailable";
}

/// <summary>
/// F-018 / plan S1 (FNS-D27(3)): <c>GET /api/v1/sales/drafts/{id}/risk-warnings</c> under <c>sales.drafts.read</c>
/// (ASSUMPTION-SAT-RISK-3: whoever reads the order sees its warnings; no new permission). Newest first, at most
/// <see cref="SalesDraftRiskWarningsApiV1.MaximumListed"/>; <c>truncated</c> says more exist. No amount is ever carried
/// (ASSUMPTION-SAT-RISK-1). Another company's draft is 404. Read-only.
/// </summary>
public sealed record SalesOrderDraftRiskWarningsResponse(
    IReadOnlyList<SalesOrderDraftRiskWarningV1> Warnings,
    bool Truncated);

/// <summary>
/// One recorded warning: the command that recorded it (<c>sales.order-draft.submit.v1</c> / <c>….approve.v1</c>), the order's
/// revision and its version after that command, the outcome (<c>Over</c> / <c>Unavailable</c>), the category (<c>OverLimit</c> /
/// <c>CreditLimitNotSet</c> / <c>SourceUnavailable</c>), who recorded it and when (UTC and the Europe/Istanbul business day) and
/// the risk read contract it was decided on.
/// </summary>
public sealed record SalesOrderDraftRiskWarningV1(
    int Revision,
    string DraftVersion,
    string Command,
    string Outcome,
    string Category,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    DateOnly CheckBusinessDay,
    string RiskSourceContract);
