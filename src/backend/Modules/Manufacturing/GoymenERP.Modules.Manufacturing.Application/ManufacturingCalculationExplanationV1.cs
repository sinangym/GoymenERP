using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

// WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): the production cost and the mass balance explained. Every value is the
// calculator's own (ProductionReceiptCostCalculatorV1's trace captured in the run that computed the cost; the recorded figures
// of ProductionCostRoundingCalculatorV1; GDC-BAL/1 through MassBalance.Reconciliation). Nothing is computed again here or on
// the screen. The existing grants decide: the production cost read's ManufacturingReportsRead, the mass balance read's
// ManufacturingOrdersRead. No new permission. Nothing is written.

/// <summary>
/// One step: a stable <see cref="Code"/> (<see cref="ManufacturingExplanationCodesV1"/>), the <see cref="Operation"/> over
/// earlier step codes (<see cref="Operands"/>), the value as lossless invariant text, its unit (<c>kg</c>, <c>TL</c>,
/// <c>TL/kg</c>) and the scale of a rounding step.
/// </summary>
public sealed record ManufacturingTraceStepViewV1(string Code, string Operation, IReadOnlyList<string> Operands, string Value,
    string Unit, int? Scale);

/// <summary>One kg issue (an Inventory issue movement) as the calculator valued it.</summary>
public sealed record ProductionCostExplanationIssueViewV1(Guid IssueMovementId, string IssuedKg, string ReturnedKg,
    string KeptKg, string TlUnitCostPerKg, string AmountTl);

/// <summary>One scrap weighing (a scrap output) as the calculator valued it; no price at zero kg without one.</summary>
public sealed record ProductionCostExplanationScrapViewV1(Guid ScrapOutputId, string Kilograms, string? TlPerKg, string ValueTl);

/// <summary>
/// The order's production cost explained, version 1. <see cref="DisplayedCostPerKgTl"/> is the production cost read's
/// <c>costPerKgTl</c> from the same port answer; the last step's value equals it. Only <c>available</c> carries steps and
/// inputs; any other status carries none (never zero) and <see cref="ReasonCode"/> says why. <see cref="CurrencyLines"/> are
/// the rates the converted inputs entered stock at (URT-D55).
/// </summary>
public sealed record ProductionCostExplanationViewV1(
    int ContractVersion,
    Guid OrderId,
    long OrderVersion,
    string Status,
    string? ReasonCode,
    string? CostBasis,
    string CalculatorVersion,
    string? DisplayedCostPerKgTl,
    IReadOnlyList<ManufacturingTraceStepViewV1> Steps,
    IReadOnlyList<ProductionCostExplanationIssueViewV1> Issues,
    IReadOnlyList<ProductionCostExplanationScrapViewV1> ScrapWeighings,
    IReadOnlyList<ProductionCostCurrencyLineViewV1> CurrencyLines,
    IReadOnlyList<string> RuleReferences)
{
    public const int CurrentContractVersion = 1;
}

/// <summary>One parent roll's BAL-003 explained: the steps and the issue and return facts behind the actual input.</summary>
public sealed record RollMassBalanceExplanationViewV1(Guid ParentRollId, string DisplayedDifferenceKg,
    IReadOnlyList<ManufacturingTraceStepViewV1> Steps, IReadOnlyList<Guid> IssueIds, IReadOnlyList<Guid> ReturnIds,
    IReadOnlyList<Guid> OutputIds);

/// <summary>The order's mass balance explained, roll by roll; <see cref="RollMassBalanceExplanationViewV1.DisplayedDifferenceKg"/>
/// is the mass balance read's <c>differenceKg</c> for the roll and equals the last step.</summary>
public sealed record MassBalanceExplanationViewV1(int ContractVersion, Guid OrderId, long Version, string FormulaVersion,
    IReadOnlyList<RollMassBalanceExplanationViewV1> Rolls, IReadOnlyList<string> RuleReferences)
{
    public const int CurrentContractVersion = 1;
}

public static class ManufacturingExplanationCodesV1
{
    public const string IssueAmounts = "issue_amounts";
    public const string NetInputCost = "net_input_cost";
    public const string ConsumedKg = "consumed_kg";
    public const string RawMaterialCostPerKg = "raw_material_cost_per_kg";
    public const string UsableRemnantKg = "usable_remnant_kg";
    public const string UsableRemnantValue = "usable_remnant_value";
    public const string WeighedScrapValue = "weighed_scrap_value";
    public const string GoodProductKg = "good_product_kg";
    public const string SecondQualityKg = "second_quality_kg";
    public const string ProducedKg = "produced_kg";
    public const string CostNumerator = "cost_numerator";
    public const string CostPerKgExact = "cost_per_kg_exact";
    public const string CostPerKgRecorded = "cost_per_kg_recorded";

    public const string IssuedKg = "issued_kg";
    public const string ReturnedKg = "returned_kg";
    public const string ActualInputKg = "actual_input_kg";
    public const string ScrapKg = "scrap_kg";
    public const string ProcessLossKg = "process_loss_kg";
    public const string WorkInProgressKg = "work_in_progress_kg";
    public const string MeasurementCorrectionKg = "measurement_correction_kg";
    public const string AccountedKg = "accounted_kg";
    public const string DifferenceKg = "difference_kg";

    public const string Input = "input";
    public const string Sum = "sum";
    public const string Add = "add";
    public const string Subtract = "subtract";
    public const string Multiply = "multiply";
    public const string Divide = "divide";
    public const string RoundHalfAwayFromZero = "round_half_away_from_zero";

    public const string Kilograms = "kg";
    public const string Lira = "TL";
    public const string LiraPerKilogram = "TL/kg";

    public const string ProductionCostCalculatorVersion =
        "production-receipt-cost/1 + production-cost-rounding/1 (SAT-D317, URT-D44, URT-D46, URT-D75, URT-D78, URT-D79)";

    public static readonly IReadOnlyList<string> ProductionCostRules =
        ["SAT-D317", "URT-D44", "URT-D46", "URT-D55", "URT-D75 (4) golden", "URT-D78", "URT-D79"];

    public static readonly IReadOnlyList<string> MassBalanceRules = ["GDC-BAL/1 BAL-003 (master v3 §28.9)", "URT-D5", "URT-D41"];
}

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// WP11: the order's production cost explained, under the production cost read's own grant
    /// (<see cref="ErpPermissions.ManufacturingReportsRead"/>) measured again after the reads; the same company, not-found and
    /// retryable-503 answers as <see cref="GetProductionCostAsync"/>. The port is read once; the displayed figure and the steps
    /// come from that one answer.
    /// </summary>
    public async Task<ManufacturingApiResult<ProductionCostExplanationViewV1>> ExplainProductionCostAsync(Guid orderId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<ProductionCostExplanationViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<ProductionCostExplanationViewV1>();
        }

        var companyId = companyContext.CompanyId;
        ManufacturingApiResult<ProductionCostExplanationViewV1> answer;
        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            if (order is null || order.CompanyId != companyId)
            {
                answer = NotFoundResult<ProductionCostExplanationViewV1>();
            }
            else
            {
                var cost = await ReadProductionCostAsync(companyId, order.Id, cancellationToken);
                var view = ProductionCostView(order.Id, order.Status.ToString(), order.Version, companyId, cost);
                answer = new ManufacturingApiResult<ProductionCostExplanationViewV1>(ManufacturingApiStatus.Ok,
                    ProductionCostExplanation(view, cost), false, null);
            }
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            answer = new ManufacturingApiResult<ProductionCostExplanationViewV1>(ManufacturingApiStatus.Unavailable, null,
                false, null);
        }

        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingReportsRead, companyId, cancellationToken);
        return scope.IsGranted && HasCapturedContext(companyId, actorId)
            ? answer
            : Denied<ProductionCostExplanationViewV1>();
    }

    /// <summary>
    /// The explanation of <paramref name="view"/>: steps only when the view is available AND the port answer carries the
    /// calculator's trace and its recorded kg cost equals the view's (one answer, so always); otherwise no step, never zero.
    /// </summary>
    internal static ProductionCostExplanationViewV1 ProductionCostExplanation(ProductionCostViewV1 view,
        ManufacturingProductionCostResultV2? cost)
    {
        if (view.Status != ManufacturingCostTraceCodesV1.CostAvailable || cost?.Trace is not { } trace
            || cost.RecordedCostPerKgTl is not { } recorded || view.CostPerKgTl != Text(recorded))
        {
            var reason = view.Status == ManufacturingCostTraceCodesV1.CostAvailable
                ? ManufacturingCostTraceCodesV1.CostAnswerInvalid
                : view.ReasonCode;
            return new ProductionCostExplanationViewV1(ProductionCostExplanationViewV1.CurrentContractVersion, view.OrderId,
                view.OrderVersion, view.Status == ManufacturingCostTraceCodesV1.CostAvailable
                    ? ManufacturingCostTraceCodesV1.CostUnavailable
                    : view.Status, reason, null,
                ManufacturingExplanationCodesV1.ProductionCostCalculatorVersion, null, [], [], [], [],
                ManufacturingExplanationCodesV1.ProductionCostRules);
        }

        const string kg = ManufacturingExplanationCodesV1.Kilograms;
        const string tl = ManufacturingExplanationCodesV1.Lira;
        const string tlPerKg = ManufacturingExplanationCodesV1.LiraPerKilogram;
        ManufacturingTraceStepViewV1 Step(string code, string operation, string[] operands, decimal value, string unit,
            int? scale = null) => new(code, operation, operands, Text(value), unit, scale);
        var steps = new List<ManufacturingTraceStepViewV1>
        {
            Step(ManufacturingExplanationCodesV1.NetInputCost, ManufacturingExplanationCodesV1.Sum,
                [ManufacturingExplanationCodesV1.IssueAmounts], trace.NetInputCostTl, tl),
            Step(ManufacturingExplanationCodesV1.ConsumedKg, ManufacturingExplanationCodesV1.Sum,
                [ManufacturingExplanationCodesV1.IssueAmounts], trace.ConsumedKg, kg),
            Step(ManufacturingExplanationCodesV1.RawMaterialCostPerKg, ManufacturingExplanationCodesV1.Divide,
                [ManufacturingExplanationCodesV1.NetInputCost, ManufacturingExplanationCodesV1.ConsumedKg],
                trace.RawMaterialCostPerKgTl, tlPerKg),
            Step(ManufacturingExplanationCodesV1.UsableRemnantKg, ManufacturingExplanationCodesV1.Input, [],
                trace.UsableRemnantKg, kg),
            Step(ManufacturingExplanationCodesV1.UsableRemnantValue, ManufacturingExplanationCodesV1.Multiply,
                [ManufacturingExplanationCodesV1.UsableRemnantKg, ManufacturingExplanationCodesV1.RawMaterialCostPerKg],
                trace.UsableRemnantValueTl, tl),
            Step(ManufacturingExplanationCodesV1.WeighedScrapValue, ManufacturingExplanationCodesV1.Sum, [],
                trace.WeighedScrapValueTl, tl),
            Step(ManufacturingExplanationCodesV1.GoodProductKg, ManufacturingExplanationCodesV1.Input, [],
                trace.GoodProductKg, kg),
            Step(ManufacturingExplanationCodesV1.SecondQualityKg, ManufacturingExplanationCodesV1.Input, [],
                trace.SecondQualityKg, kg),
            Step(ManufacturingExplanationCodesV1.ProducedKg, ManufacturingExplanationCodesV1.Add,
                [ManufacturingExplanationCodesV1.GoodProductKg, ManufacturingExplanationCodesV1.SecondQualityKg],
                trace.ProducedKg, kg),
            Step(ManufacturingExplanationCodesV1.CostNumerator, ManufacturingExplanationCodesV1.Subtract,
                [ManufacturingExplanationCodesV1.NetInputCost, ManufacturingExplanationCodesV1.UsableRemnantValue,
                    ManufacturingExplanationCodesV1.WeighedScrapValue], trace.CostNumeratorTl, tl),
            Step(ManufacturingExplanationCodesV1.CostPerKgExact, ManufacturingExplanationCodesV1.Divide,
                [ManufacturingExplanationCodesV1.CostNumerator, ManufacturingExplanationCodesV1.ProducedKg],
                trace.CostPerKgTl, tlPerKg),
            Step(ManufacturingExplanationCodesV1.CostPerKgRecorded, ManufacturingExplanationCodesV1.RoundHalfAwayFromZero,
                [ManufacturingExplanationCodesV1.CostPerKgExact], recorded, tlPerKg,
                ProductionCostRoundingCalculatorV1.RecordedCostPerKgScale),
        };
        return new ProductionCostExplanationViewV1(ProductionCostExplanationViewV1.CurrentContractVersion, view.OrderId,
            view.OrderVersion, view.Status, null, view.CostBasis,
            ManufacturingExplanationCodesV1.ProductionCostCalculatorVersion, view.CostPerKgTl, steps,
            [.. trace.Issues.Select(static issue => new ProductionCostExplanationIssueViewV1(issue.IssueMovementId,
                Text(issue.IssuedKg), Text(issue.ReturnedKg), Text(issue.KeptKg), Text(issue.TlUnitCostPerKg),
                Text(issue.AmountTl)))],
            [.. trace.ScrapWeighings.Select(static weighing => new ProductionCostExplanationScrapViewV1(weighing.ScrapOutputId,
                Text(weighing.Kilograms), weighing.TlPerKg is { } price ? Text(price) : null, Text(weighing.ValueTl)))],
            view.CurrencyLines, ManufacturingExplanationCodesV1.ProductionCostRules);
    }

    /// <summary>
    /// WP11: the order's mass balance explained, under the mass balance read's grant
    /// (<see cref="ErpPermissions.ManufacturingOrdersRead"/>) and with its answers; one order read, the same calculator.
    /// </summary>
    public async Task<ManufacturingApiResult<MassBalanceExplanationViewV1>> ExplainMassBalanceAsync(Guid orderId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<MassBalanceExplanationViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<MassBalanceExplanationViewV1>();
        }

        var companyId = companyContext.CompanyId;
        ManufacturingApiResult<MassBalanceExplanationViewV1> answer;
        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            answer = order is null || order.CompanyId != companyId
                ? NotFoundResult<MassBalanceExplanationViewV1>()
                : new ManufacturingApiResult<MassBalanceExplanationViewV1>(ManufacturingApiStatus.Ok,
                    MassBalanceExplanation(order), false, null);
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            answer = new ManufacturingApiResult<MassBalanceExplanationViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                null);
        }

        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingOrdersRead, companyId, cancellationToken);
        return scope.IsGranted && HasCapturedContext(companyId, actorId)
            ? answer
            : Denied<MassBalanceExplanationViewV1>();
    }

    /// <summary>
    /// BAL-003 per planned parent roll, as <see cref="MassBalanceView"/> shows it: the inputs are the roll's own facts, the
    /// accounted mass and the difference are <see cref="MassBalance.Reconciliation"/>'s (the close's calculation).
    /// </summary>
    internal static MassBalanceExplanationViewV1 MassBalanceExplanation(ProductionOrder order) =>
        new(MassBalanceExplanationViewV1.CurrentContractVersion, order.Id, order.Version, MassBalanceEngineV1.FormulaVersion,
        [
            .. order.PlannedInputs.Select(input =>
            {
                var balance = order.BalanceOf(input.ParentRollId);
                var difference = balance.Reconciliation;
                const string kg = ManufacturingExplanationCodesV1.Kilograms;
                ManufacturingTraceStepViewV1 Step(string code, string operation, string[] operands, decimal value) =>
                    new(code, operation, operands, Text(value), kg, null);
                var steps = new List<ManufacturingTraceStepViewV1>
                {
                    Step(ManufacturingExplanationCodesV1.IssuedKg, ManufacturingExplanationCodesV1.Sum, [], balance.Issued.Value),
                    Step(ManufacturingExplanationCodesV1.ReturnedKg, ManufacturingExplanationCodesV1.Sum, [],
                        balance.Returned.Value),
                    Step(ManufacturingExplanationCodesV1.ActualInputKg, ManufacturingExplanationCodesV1.Subtract,
                        [ManufacturingExplanationCodesV1.IssuedKg, ManufacturingExplanationCodesV1.ReturnedKg],
                        balance.NetIssued.Value),
                    Step(ManufacturingExplanationCodesV1.GoodProductKg, ManufacturingExplanationCodesV1.Sum, [],
                        balance.OutputOf(OutputClass.GoodProduct).Value),
                    Step(ManufacturingExplanationCodesV1.SecondQualityKg, ManufacturingExplanationCodesV1.Sum, [],
                        balance.OutputOf(OutputClass.SecondQuality).Value),
                    Step(ManufacturingExplanationCodesV1.UsableRemnantKg, ManufacturingExplanationCodesV1.Sum, [],
                        balance.OutputOf(OutputClass.UsableRemnant).Value),
                    Step(ManufacturingExplanationCodesV1.ScrapKg, ManufacturingExplanationCodesV1.Sum, [],
                        balance.OutputOf(OutputClass.Scrap).Value),
                    Step(ManufacturingExplanationCodesV1.ProcessLossKg, ManufacturingExplanationCodesV1.Sum, [],
                        balance.OutputOf(OutputClass.ProcessLoss).Value),
                    Step(ManufacturingExplanationCodesV1.WorkInProgressKg, ManufacturingExplanationCodesV1.Input, [],
                        balance.WorkInProgress.Value),
                    Step(ManufacturingExplanationCodesV1.MeasurementCorrectionKg, ManufacturingExplanationCodesV1.Input, [],
                        MassBalance.RecordedMeasurementCorrections.Value),
                    Step(ManufacturingExplanationCodesV1.AccountedKg, ManufacturingExplanationCodesV1.Add,
                        [ManufacturingExplanationCodesV1.GoodProductKg, ManufacturingExplanationCodesV1.SecondQualityKg,
                            ManufacturingExplanationCodesV1.UsableRemnantKg, ManufacturingExplanationCodesV1.ScrapKg,
                            ManufacturingExplanationCodesV1.ProcessLossKg, ManufacturingExplanationCodesV1.WorkInProgressKg,
                            ManufacturingExplanationCodesV1.MeasurementCorrectionKg], difference.AccountedKg),
                    Step(ManufacturingExplanationCodesV1.DifferenceKg, ManufacturingExplanationCodesV1.Subtract,
                        [ManufacturingExplanationCodesV1.ActualInputKg, ManufacturingExplanationCodesV1.AccountedKg],
                        difference.DifferenceKg),
                };
                return new RollMassBalanceExplanationViewV1(input.ParentRollId, Text(difference.DifferenceKg), steps,
                    [.. order.Issues.Where(item => item.ParentRollId == input.ParentRollId).Select(item => item.IssueId)],
                    [.. order.Returns.Where(item => item.ParentRollId == input.ParentRollId).Select(item => item.ReturnId)],
                    [.. order.Outputs.Where(item => item.ParentRollId == input.ParentRollId).Select(item => item.OutputId)]);
            }),
        ], ManufacturingExplanationCodesV1.MassBalanceRules);
}
