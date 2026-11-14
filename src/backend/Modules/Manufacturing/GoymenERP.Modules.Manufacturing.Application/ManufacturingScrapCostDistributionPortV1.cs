using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// SAT-D307: the production receipt cost of one order distributed over its output labels, version 1. It is the
/// Manufacturing side of the production cost port that the Inventory average cost of a production receipt reads
/// (SAT-D305 (4), SAT-D307 "Uretim maliyet portu"). The port carries no authority: the caller resolves and fences the
/// company and passes it; the port reads that company's order only, writes nothing and answers per status, never with a
/// silent zero. Definition of the figures (SAT-D307 "tanimi Uretim port sozlesmesinde yazilir"):
/// <list type="bullet">
/// <item>net cost = sum(issued kg x the issue's stamped average cost) - sum(returned kg x the linked issue's stamped
/// cost); no labour or overhead in v1 (SAT-D305 (4), SAT-D317 (2));</item>
/// <item>cost per kg (good product and second quality, the same, SAT-D307 (1)-(2)) and the net raw-material kg cost (a
/// usable remnant, URT-D44, URT-D46) as <see cref="ProductionReceiptCostCalculatorV1"/> computes them;</item>
/// <item>each label's allocated cost = its kg x its class's cost per kg, as
/// <see cref="ProductionScrapCostDistributionCalculatorV1"/> computes it; a scrap label has no allocated cost
/// (<c>scrap_label_cost_not_decided</c>, URT-D8).</item>
/// </list>
/// Amounts are TL and unrounded in v1: the Money form (currency and scale of the money profile, SAT-D265 (4)) and the
/// rounding remainder are the port contract's rounding work (URT-S14), so a consumer must not post these amounts until
/// that version exists.
/// </summary>
public interface IManufacturingScrapCostDistributionPortV1
{
    Task<ManufacturingScrapCostDistributionResultV1> DistributeAsync(
        Guid companyId,
        Guid orderId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads one order's cost input and output labels for <see cref="IManufacturingScrapCostDistributionPortV1"/>. It is
/// Manufacturing-owned and implemented where the inputs live: the issue costs stamped by Inventory, the scrap-in-cost
/// setting (SAT-D305 (2)), each scrap weighing's month price (URT-D47) and the order's closed state and labels. It reads
/// the given company only, writes nothing and answers <see cref="ManufacturingScrapCostDistributionSourceStatusV1.Unavailable"/>
/// when any input cannot be read.
/// </summary>
public interface IManufacturingScrapCostDistributionSourceV1
{
    Task<ManufacturingScrapCostDistributionSourceReadV1> ReadAsync(
        Guid companyId,
        Guid orderId,
        CancellationToken cancellationToken);
}

public enum ManufacturingScrapCostDistributionSourceStatusV1
{
    Found = 1,
    NotFound = 2,
    Unavailable = 3,
}

public sealed record ManufacturingScrapCostDistributionSourceReadV1(
    ManufacturingScrapCostDistributionSourceStatusV1 Status,
    Guid CompanyId,
    Guid OrderId,
    ProductionScrapCostDistributionInput? Input);

public enum ManufacturingScrapCostDistributionStatusV1
{
    /// <summary>The distribution was computed; every line is present.</summary>
    Available = 1,

    /// <summary>The cost waits (an open order, a scrap price not entered); no line and no amount.</summary>
    Pending = 2,

    /// <summary>The inputs were read but give no cost (unreadable cost or labels); no line and no amount.</summary>
    Unreadable = 3,

    /// <summary>The inputs could not be read (fail closed); no line and no amount.</summary>
    Unavailable = 4,

    /// <summary>The company has no such order.</summary>
    NotFound = 5,
}

public static class ManufacturingScrapCostDistributionCodesV1
{
    public const string CompanyRequired = "company_required";
    public const string OrderRequired = "order_required";
    public const string SourceUnavailable = "cost_source_unavailable";
    public const string SourceScopeMismatch = "cost_source_scope_mismatch";
    public const string OrderNotFound = "order_not_found";
}

/// <summary>
/// One label's line: TL per kg and TL, unrounded. <see cref="AllocatedCostTl"/> is null on a scrap line, whose
/// <see cref="NotAllocatedCode"/> says why; <see cref="SeparateScrapExpenseTl"/> is that weighing's share of the separate
/// scrap expense, set only when scrap is out of the product cost (SAT-D316 (1)).
/// </summary>
public sealed record ManufacturingScrapCostDistributionLineV1(
    Guid OutputLabelId,
    OutputClass OutputClass,
    decimal Kilograms,
    decimal? UnitCostPerKgTl,
    decimal? AllocatedCostTl,
    decimal? SeparateScrapExpenseTl,
    string? AllocationBasis,
    string? NotAllocatedCode);

/// <summary>
/// The answer. Only <see cref="ManufacturingScrapCostDistributionStatusV1.Available"/> carries figures and lines; any
/// other status carries none (never zero) and <see cref="ReasonCode"/> says why. <see cref="NoteCodes"/> are the cost
/// notes (paint, other non-kg input not included, SAT-D327, URT-D45).
/// </summary>
public sealed record ManufacturingScrapCostDistributionResultV1(
    int ContractVersion,
    Guid CompanyId,
    Guid OrderId,
    ManufacturingScrapCostDistributionStatusV1 Status,
    string? CostBasis,
    decimal? CostPerKgTl,
    decimal? RawMaterialCostPerKgTl,
    decimal? WeighedScrapExpenseTl,
    decimal? AllocatedTotalTl,
    IReadOnlyList<ManufacturingScrapCostDistributionLineV1> Lines,
    IReadOnlyList<string> NoteCodes,
    string? ReasonCode)
{
    public const int CurrentContractVersion = 1;

    public static ManufacturingScrapCostDistributionResultV1 Empty(
        Guid companyId, Guid orderId, ManufacturingScrapCostDistributionStatusV1 status, string reasonCode) =>
        new(CurrentContractVersion, companyId, orderId, status, null, null, null, null, null, [], [], reasonCode);
}

/// <summary>
/// <see cref="IManufacturingScrapCostDistributionPortV1"/> over an <see cref="IManufacturingScrapCostDistributionSourceV1"/>:
/// it checks the scope, reads the inputs once and calls the single calculator; it computes nothing itself. An empty
/// company or order, a source that throws or answers for another company or order, or an unknown source status is
/// <see cref="ManufacturingScrapCostDistributionStatusV1.Unavailable"/> (fail closed). Cancellation is not swallowed.
/// </summary>
public sealed class ManufacturingScrapCostDistributionPort(IManufacturingScrapCostDistributionSourceV1 source)
    : IManufacturingScrapCostDistributionPortV1
{
    public async Task<ManufacturingScrapCostDistributionResultV1> DistributeAsync(
        Guid companyId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty)
        {
            return ManufacturingScrapCostDistributionResultV1.Empty(companyId, orderId,
                ManufacturingScrapCostDistributionStatusV1.Unavailable, ManufacturingScrapCostDistributionCodesV1.CompanyRequired);
        }

        if (orderId == Guid.Empty)
        {
            return ManufacturingScrapCostDistributionResultV1.Empty(companyId, orderId,
                ManufacturingScrapCostDistributionStatusV1.NotFound, ManufacturingScrapCostDistributionCodesV1.OrderRequired);
        }

        ManufacturingScrapCostDistributionSourceReadV1? read;
        try
        {
            read = await source.ReadAsync(companyId, orderId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable(companyId, orderId, ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
        }

        if (read is null)
        {
            return Unavailable(companyId, orderId, ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
        }

        if (read.CompanyId != companyId || read.OrderId != orderId)
        {
            return Unavailable(companyId, orderId, ManufacturingScrapCostDistributionCodesV1.SourceScopeMismatch);
        }

        switch (read.Status)
        {
            case ManufacturingScrapCostDistributionSourceStatusV1.NotFound when read.Input is null:
                return ManufacturingScrapCostDistributionResultV1.Empty(companyId, orderId,
                    ManufacturingScrapCostDistributionStatusV1.NotFound, ManufacturingScrapCostDistributionCodesV1.OrderNotFound);
            case ManufacturingScrapCostDistributionSourceStatusV1.Found when read.Input is not null:
                return Map(companyId, orderId, ProductionScrapCostDistributionCalculatorV1.Distribute(read.Input));
            default:
                return Unavailable(companyId, orderId, ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
        }
    }

    private static ManufacturingScrapCostDistributionResultV1 Unavailable(Guid companyId, Guid orderId, string code) =>
        ManufacturingScrapCostDistributionResultV1.Empty(companyId, orderId,
            ManufacturingScrapCostDistributionStatusV1.Unavailable, code);

    private static ManufacturingScrapCostDistributionResultV1 Map(
        Guid companyId, Guid orderId, ProductionScrapCostDistributionResult result)
    {
        switch (result.Status)
        {
            case ProductionReceiptCostStatus.Value when result.Cost is { } cost:
                var notes = new List<string>(2);
                if (cost.PaintCostNote is { } paint)
                {
                    notes.Add(paint.Code);
                }

                if (cost.NonKgInputCostNote is { } nonKg)
                {
                    notes.Add(nonKg.Code);
                }

                var lines = result.Lines
                    .Select(static line => new ManufacturingScrapCostDistributionLineV1(line.OutputLabelId,
                        line.OutputClass, line.Kilograms, line.UnitCostPerKg, line.AllocatedCost,
                        line.SeparateScrapExpense, line.AllocationBasis, line.NotAllocatedCode))
                    .ToArray();
                return new ManufacturingScrapCostDistributionResultV1(
                    ManufacturingScrapCostDistributionResultV1.CurrentContractVersion, companyId, orderId,
                    ManufacturingScrapCostDistributionStatusV1.Available, cost.CostBasis, cost.CostPerKg,
                    cost.RawMaterialCostPerKg, cost.WeighedScrapExpense, result.AllocatedTotal, lines, notes, null);
            case ProductionReceiptCostStatus.Pending:
                return ManufacturingScrapCostDistributionResultV1.Empty(companyId, orderId,
                    ManufacturingScrapCostDistributionStatusV1.Pending,
                    result.PendingCode ?? ProductionReceiptCostCodesV1.InputInvalid);
            default:
                return ManufacturingScrapCostDistributionResultV1.Empty(companyId, orderId,
                    ManufacturingScrapCostDistributionStatusV1.Unreadable,
                    result.UnreadableCode ?? ProductionReceiptCostCodesV1.InputInvalid);
        }
    }
}
