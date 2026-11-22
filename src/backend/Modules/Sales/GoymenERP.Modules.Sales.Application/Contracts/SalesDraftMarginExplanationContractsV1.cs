namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): GET …/{id}/lines/{lineId}/margin-explanation. <see cref="Margin"/> is the
/// very line margin the line-pricing read shows (the same resolution, the same calculator run); <see cref="Steps"/> are the
/// intermediate values that run used, in order, as exact invariant text — the screen shows them and never recomputes.
/// <see cref="Sources"/> link the input records (the order line, the cost observation or snapshot with the owner's proof, the
/// frozen sale rate, the pinned kg per unit); <see cref="RuleReferences"/> name the decisions and goldens the calculator follows.
/// An unavailable margin carries the steps up to the check that refused it (possibly none) and its reason.
/// </summary>
public sealed record SalesDraftLineMarginExplanationV1(int ContractVersion, Guid DraftId, string Version, string Kind,
    string CalculatorVersion, SalesDraftLineMarginV1 Margin, IReadOnlyList<SalesMarginTraceStepV1> Steps,
    IReadOnlyList<SalesMarginTraceSourceV1> Sources, IReadOnlyList<string> RuleReferences)
{
    public const int CurrentContractVersion = 1;
}

/// <summary>
/// One step: a stable <see cref="Code"/>, the <see cref="Operation"/> over earlier step codes (<see cref="Operands"/>), the exact
/// <see cref="Value"/> the calculator used, its currency or unit when it has one, and the scale of a rounding step.
/// </summary>
public sealed record SalesMarginTraceStepV1(string Code, string Operation, IReadOnlyList<string> Operands, string Value,
    Guid? CurrencyId, Guid? UnitId, int? Scale);

/// <summary>
/// One input record: <see cref="Kind"/> (<see cref="SalesMarginTraceSourceKindsV1"/>), the record's identity and revision, a
/// reference text (a rate day, a cost unit), when it was recorded and the owner's own proof text (<see cref="Detail"/>, as stored).
/// </summary>
public sealed record SalesMarginTraceSourceV1(string Kind, Guid? RecordId, int? Revision, string? Reference,
    DateTimeOffset? RecordedAtUtc, string? Detail);

public static class SalesMarginTraceSourceKindsV1
{
    public const string OrderLine = "sales-order-line";
    public const string CostObservation = "inventory-cost-observation";
    public const string CostSnapshot = "margin-cost-snapshot";
    public const string SaleRate = "sale-rate";
    public const string KilogramsPerUnit = "kilograms-per-unit";
}

/// <summary>The decisions and goldens the margin calculator follows; the calculator's version text.</summary>
public static class SalesMarginTraceRulesV1
{
    public const string CalculatorVersion = "sales-line-margin/1 (SAT-D259, SAT-D265, SAT-D275)";
    public const string BaseGolden = "SAT-D265(5) golden (PR1200)";
    public const string ForeignSaleGolden = "SAT-D426 golden D01-D23 (PR1203)";
    public const string PieceGolden = "ADET_KG_FORMUL_GOLDEN_V1 M01-M15 (SAT-D438)";
}
