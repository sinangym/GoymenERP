using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

// URT-S19 (B-27, DEC-PRDCT-009, API-42): the cost trace of one order. DEC-PRDCT-009 defers energy, labour and overhead
// and keeps the raw event data now: lot cost, duration, line, stoppage, fire, subcontract and sales-order link. This file
// gathers that raw data from the order's own stored facts. It computes NO amount: the production receipt cost is
// ProductionReceiptCostCalculatorV1's (through the cost port), the one calculator the receipt and any report read, so
// the trace never holds a second formula. Weighted average with the raw-material kg cost as basis (SAT-D305 (4)) is the
// method that calculator implements; the trace names it, it does not choose it. OPEN-008 stays open.

/// <summary>
/// One parent roll (lot) the order took input from, net of its returns: <see cref="ConsumedKg"/> = issued − returned.
/// The lot's unit cost is not here: it is Inventory's stamped issue cost, read through the cost port only.
/// The commercial party and account are the planned input's counterparty-owned material snapshot (a subcontract
/// owner), null for company-owned material. An order takes input from its planned rolls only, so every lot is one of
/// them.
/// </summary>
public sealed record ProductionCostTraceLotV1(
    Guid ParentRollId,
    Kilograms PlannedKg,
    Kilograms IssuedKg,
    Kilograms ReturnedKg,
    Kilograms ConsumedKg,
    int IssueCount,
    int ReturnCount,
    DateTimeOffset? FirstIssuedAtUtc,
    DateTimeOffset? LastIssuedAtUtc,
    Guid? CommercialPartyId,
    Guid? CommercialPartyAccountId);

/// <summary>
/// One output class of the order, each declaration counted net of its reversals (the mass-balance rule, F-009 D2).
/// <see cref="DeclarationCount"/> counts the declarations, <see cref="ReversalCount"/> their reversals.
/// </summary>
public sealed record ProductionCostTraceOutputV1(
    OutputClass Class,
    Kilograms NetKg,
    int DeclarationCount,
    int ReversalCount);

/// <summary>
/// The moments the order's stored facts carry. A duration is not derived from them here: how production time is
/// measured (setup, run, stoppage) belongs to the time records (API-33), which do not exist yet, so
/// <see cref="ProductionCostTraceCodesV1.TimeRecordsNotRecorded"/> is reported instead of an invented figure.
/// </summary>
public sealed record ProductionCostTraceTimelineV1(
    DateTimeOffset? FirstFactAtUtc,
    DateTimeOffset? LastFactAtUtc,
    DateTimeOffset? ClosedAtUtc,
    string StoppageCode,
    string DurationCode);

/// <summary>The order's cost trace (DEC-PRDCT-009 raw data), version 1. Amounts are not part of it.</summary>
public sealed record ProductionCostTraceV1(
    int ContractVersion,
    string MethodCode,
    Guid OrderId,
    Guid CompanyId,
    Guid PlanId,
    Guid WorkCenterId,
    ManufacturingProcess Process,
    OrderStatus Status,
    long Version,
    IReadOnlyList<ProductionCostTraceLotV1> Lots,
    Kilograms TotalIssuedKg,
    Kilograms TotalReturnedKg,
    Kilograms TotalConsumedKg,
    IReadOnlyList<ProductionCostTraceOutputV1> Outputs,
    ProductionCostTraceTimelineV1 Timeline);

public static class ProductionCostTraceCodesV1
{
    /// <summary>
    /// The method the production receipt cost calculator implements (SAT-D305 (4)): the weighted average of the consumed
    /// raw material's stamped kg costs, fire loaded on the product. Named, not chosen, here; OPEN-008 stays open.
    /// </summary>
    public const string MethodWeightedAverageRawMaterialKg = "weighted-average-raw-material-kg";

    /// <summary>No time record (setup, run, stoppage, maintenance; API-33) is stored by this module yet.</summary>
    public const string TimeRecordsNotRecorded = "time_records_not_recorded";
}

/// <summary>
/// Builds <see cref="ProductionCostTraceV1"/> from an order's stored facts. Deterministic: lots are in the planned
/// inputs' order (a planned roll without an issue is listed with zero kg, never dropped); output classes
/// are in their enum order and only the classes the order declared are listed. It reads; it never changes the order.
/// </summary>
public static class ProductionCostTraceBuilderV1
{
    public const int ContractVersion = 1;

    public static ProductionCostTraceV1 Build(ProductionOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var lots = order.PlannedInputs
            .Select(input => Lot(order, input.ParentRollId, input.PlannedMass, input.CommercialContext?.PartyId,
                input.CommercialContext?.CompanyPartyAccountId))
            .ToArray();

        var issued = Sum(lots.Select(lot => lot.IssuedKg));
        var returned = Sum(lots.Select(lot => lot.ReturnedKg));
        var consumed = Sum(lots.Select(lot => lot.ConsumedKg));

        var outputs = order.Outputs
            .GroupBy(output => output.Class)
            .OrderBy(group => group.Key)
            .Select(group => new ProductionCostTraceOutputV1(
                group.Key,
                Sum(group.Select(output => order.NetMassOf(output.OutputId))),
                group.Count(),
                order.OutputReversals.Count(reversal => group.Any(output => output.OutputId == reversal.OutputId))))
            .ToArray();

        var moments = order.Issues.Select(issue => issue.IssuedAtUtc)
            .Concat(order.Returns.Select(item => item.ReturnedAtUtc))
            .Concat(order.WipMeasurements.Select(item => item.MeasuredAtUtc))
            .Concat(order.Outputs.Select(item => item.DeclaredAtUtc))
            .Concat(order.OutputReversals.Select(item => item.ReversedAtUtc))
            .ToArray();

        var timeline = new ProductionCostTraceTimelineV1(
            moments.Length == 0 ? null : moments.Min(),
            moments.Length == 0 ? null : moments.Max(),
            order.ClosedAtUtc,
            ProductionCostTraceCodesV1.TimeRecordsNotRecorded,
            ProductionCostTraceCodesV1.TimeRecordsNotRecorded);

        return new ProductionCostTraceV1(ContractVersion, ProductionCostTraceCodesV1.MethodWeightedAverageRawMaterialKg,
            order.Id, order.CompanyId, order.PlanId, order.WorkCenterId, order.Process, order.Status, order.Version,
            lots, issued, returned, consumed, outputs, timeline);
    }

    private static ProductionCostTraceLotV1 Lot(ProductionOrder order, Guid rollId, Kilograms plannedKg, Guid? partyId,
        Guid? partyAccountId)
    {
        var issues = order.Issues.Where(issue => issue.ParentRollId == rollId).ToArray();
        var returns = order.Returns.Where(item => item.ParentRollId == rollId).ToArray();
        var issued = Sum(issues.Select(issue => issue.Mass));
        var returned = Sum(returns.Select(item => item.Mass));

        // The order refuses a return above the roll's net issue, so a stored order never gives a negative consumption;
        // one that did would not rebuild, and Kilograms refuses a negative value rather than clipping it to zero.
        return new ProductionCostTraceLotV1(rollId, plannedKg, issued, returned,
            new Kilograms(issued.Value - returned.Value), issues.Length, returns.Length,
            issues.Length == 0 ? null : issues.Min(issue => issue.IssuedAtUtc),
            issues.Length == 0 ? null : issues.Max(issue => issue.IssuedAtUtc),
            partyId, partyAccountId);
    }

    private static Kilograms Sum(IEnumerable<Kilograms> masses) =>
        masses.Aggregate(Kilograms.Zero, (total, mass) => total + mass);
}
