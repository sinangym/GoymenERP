using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// The production cost of one order with its recorded figures, version 2 of the production cost port (SAT-D305 (4),
/// SAT-D307 "Uretim maliyet portu"; URT-D54, URT-D55, URT-D58, SINAN_KARARLARI_2026-11-14_URETIM_EK4; URT-D78…URT-D80,
/// SINAN_KARARLARI_2026-11-15_DALGA2_EK10). Version 1
/// (<see cref="IManufacturingScrapCostDistributionPortV1"/>) stays as it is and answers the unrounded figures. Version 2
/// adds:
/// <list type="bullet">
/// <item>the recorded figures of <see cref="ProductionCostRoundingCalculatorV1"/>: a kg cost at 4 decimals and a TL
/// amount at 2 decimals, each rounded once from the full-precision calculation (a label's amount is its unrounded kg
/// cost x kg, URT-D79), with the remainder on the good product line with the most kg so the TL that went in equals the
/// TL distributed (URT-D54 (1)-(2), URT-D80);</item>
/// <item>the currency lines of the issues that entered stock in a foreign currency, with the rate recorded then
/// (URT-D55 (2), display only);</item>
/// <item>the issues that keep the cost from being computed, with their reason: a stamped cost of zero
/// (<c>input_cost_zero</c>, URT-D58 (1)) or no rate recorded (<c>input_exchange_rate_missing</c>, URT-D55 (2)).</item>
/// </list>
/// A half is rounded away from zero, the one rounding rule of the system (URT-D78, SAT-D11): there is no company
/// setting and no rounding source, so the cost never waits for one. An order whose remainder has no good product line to
/// carry it is pending (<c>rounding_remainder_line_missing</c>, URT-D80). The port carries no authority: the caller
/// resolves and fences the company; the port reads that company's order only, writes nothing and never answers a
/// silent zero.
/// </summary>
public interface IManufacturingProductionCostPortV2
{
    Task<ManufacturingProductionCostResultV2> ReadAsync(
        Guid companyId,
        Guid orderId,
        CancellationToken cancellationToken);
}

/// <summary>
/// One recorded line: <see cref="RecordedUnitCostPerKgTl"/> at 4 decimals, <see cref="RecordedAmountTl"/> and
/// <see cref="RecordedSeparateScrapExpenseTl"/> at 2 decimals, <see cref="RemainderTl"/> the rounding remainder the line
/// carries (zero on every line but one). A scrap line has no unit cost or amount; <see cref="NotAllocatedCode"/> says why.
/// </summary>
public sealed record ManufacturingProductionCostLineV2(
    Guid OutputLabelId,
    OutputClass OutputClass,
    decimal Kilograms,
    decimal? RecordedUnitCostPerKgTl,
    decimal? RecordedAmountTl,
    decimal? RecordedSeparateScrapExpenseTl,
    decimal RemainderTl,
    string? AllocationBasis,
    string? NotAllocatedCode);

/// <summary>One converted issue (URT-D55 (2)); <see cref="ForeignAmount"/> is unrounded and display only.</summary>
public sealed record ManufacturingProductionCostCurrencyLineV2(
    Guid IssueMovementId,
    Guid CurrencyId,
    decimal? ForeignUnitCostPerKg,
    decimal? ExchangeRate,
    decimal TlUnitCostPerKg,
    decimal KeptKilograms,
    decimal? ForeignAmount,
    ProductionExactRatioV1? ForeignUnitCostRatio = null,
    ProductionExactRatioV1? ForeignAmountRatio = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ProductionExactRatioV1? ExchangeRateRatio = null);

/// <summary>One issue that keeps the cost from being computed and its reason code (URT-D55 (2), URT-D58 (1)).</summary>
public sealed record ManufacturingProductionCostIssueReasonV2(Guid IssueMovementId, string ReasonCode);

/// <summary>
/// The answer. Only <see cref="ManufacturingScrapCostDistributionStatusV1.Available"/> carries figures, lines and
/// currency lines; any other status carries none (never zero) and <see cref="ReasonCode"/> says why.
/// <see cref="IssueReasons"/> are set on an unreadable answer whose reason is <c>input_cost_zero</c> or
/// <c>input_exchange_rate_missing</c>, so the screen can name the input (URT-D58 (1)). <see cref="NoteCodes"/> are the
/// cost notes (SAT-D327, URT-D45). <see cref="InputTotalTl"/> always equals <see cref="DistributedTotalTl"/>.
/// </summary>
public sealed record ManufacturingProductionCostResultV2(
    int ContractVersion,
    Guid CompanyId,
    Guid OrderId,
    ManufacturingScrapCostDistributionStatusV1 Status,
    string? CostBasis,
    decimal? RecordedCostPerKgTl,
    decimal? RecordedRawMaterialCostPerKgTl,
    decimal? InputTotalTl,
    decimal? RecordedWeighedScrapExpenseTl,
    decimal? DistributedTotalTl,
    decimal? RemainderTl,
    Guid? RemainderLabelId,
    IReadOnlyList<ManufacturingProductionCostLineV2> Lines,
    IReadOnlyList<ManufacturingProductionCostCurrencyLineV2> CurrencyLines,
    IReadOnlyList<ManufacturingProductionCostIssueReasonV2> IssueReasons,
    IReadOnlyList<string> NoteCodes,
    string? ReasonCode)
{
    public const int CurrentContractVersion = 2;

    public static ManufacturingProductionCostResultV2 Empty(
        Guid companyId, Guid orderId, ManufacturingScrapCostDistributionStatusV1 status, string reasonCode) =>
        new(CurrentContractVersion, companyId, orderId, status, null, null, null, null, null, null, null, null,
            [], [], [], [], reasonCode);
}

/// <summary>
/// <see cref="IManufacturingProductionCostPortV2"/> over the order's cost source
/// (<see cref="IManufacturingScrapCostDistributionSourceV1"/>, the same as version 1): it checks the scope, reads the
/// source once and calls the single rounding calculator; it computes nothing itself. An empty company or order, a source
/// that throws or answers for another company or order, or an unknown source status is
/// <see cref="ManufacturingScrapCostDistributionStatusV1.Unavailable"/> (fail closed). Cancellation is not swallowed.
/// </summary>
public sealed class ManufacturingProductionCostPort(IManufacturingScrapCostDistributionSourceV1 source)
    : IManufacturingProductionCostPortV2
{
    public async Task<ManufacturingProductionCostResultV2> ReadAsync(
        Guid companyId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty)
        {
            return Unavailable(companyId, orderId, ManufacturingScrapCostDistributionCodesV1.CompanyRequired);
        }

        if (orderId == Guid.Empty)
        {
            return ManufacturingProductionCostResultV2.Empty(companyId, orderId,
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

        if (read is { Status: ManufacturingScrapCostDistributionSourceStatusV1.NotFound, Input: null })
        {
            return ManufacturingProductionCostResultV2.Empty(companyId, orderId,
                ManufacturingScrapCostDistributionStatusV1.NotFound, ManufacturingScrapCostDistributionCodesV1.OrderNotFound);
        }

        if (read is not { Status: ManufacturingScrapCostDistributionSourceStatusV1.Found, Input: { } input })
        {
            return Unavailable(companyId, orderId, ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
        }

        return Map(companyId, orderId, ProductionCostRoundingCalculatorV1.Round(input));
    }

    private static ManufacturingProductionCostResultV2 Unavailable(Guid companyId, Guid orderId, string code) =>
        ManufacturingProductionCostResultV2.Empty(companyId, orderId,
            ManufacturingScrapCostDistributionStatusV1.Unavailable, code);

    private static ManufacturingProductionCostResultV2 Map(
        Guid companyId, Guid orderId, ProductionCostRoundedResultV1 result)
    {
        switch (result.Status)
        {
            case ProductionReceiptCostStatus.Value when result.Distribution?.Cost is { } cost:
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
                    .Select(static line => new ManufacturingProductionCostLineV2(line.OutputLabelId, line.OutputClass,
                        line.Kilograms, line.RecordedUnitCostPerKg, line.RecordedAmountTl,
                        line.RecordedSeparateScrapExpenseTl, line.RemainderTl, line.AllocationBasis,
                        line.NotAllocatedCode))
                    .ToArray();
                var currencyLines = cost.CurrencyLines
                    .Select(static line => new ManufacturingProductionCostCurrencyLineV2(line.IssueMovementId,
                        line.CurrencyId, line.ForeignUnitCostPerKg, line.ExchangeRate, line.TlUnitCostPerKg,
                        line.KeptKilograms, line.ForeignAmount, line.ForeignUnitCostRatio, line.ForeignAmountRatio, line.ExchangeRateRatio))
                    .ToArray();
                return new ManufacturingProductionCostResultV2(
                    ManufacturingProductionCostResultV2.CurrentContractVersion, companyId, orderId,
                    ManufacturingScrapCostDistributionStatusV1.Available, cost.CostBasis,
                    result.RecordedCostPerKg, result.RecordedRawMaterialCostPerKg, result.InputTotalTl,
                    result.RecordedWeighedScrapExpenseTl, result.DistributedTotalTl, result.RemainderTl,
                    result.RemainderLabelId, lines, currencyLines, [], notes, null);
            case ProductionReceiptCostStatus.Pending:
                return ManufacturingProductionCostResultV2.Empty(companyId, orderId,
                    ManufacturingScrapCostDistributionStatusV1.Pending,
                    result.PendingCode ?? ProductionReceiptCostCodesV1.InputInvalid);
            default:
                var reasons = result.IssueReasons
                    .Select(static reason => new ManufacturingProductionCostIssueReasonV2(reason.IssueMovementId,
                        reason.Code))
                    .ToArray();
                return ManufacturingProductionCostResultV2.Empty(companyId, orderId,
                    ManufacturingScrapCostDistributionStatusV1.Unreadable,
                    result.UnreadableCode ?? ProductionReceiptCostCodesV1.InputInvalid) with
                {
                    IssueReasons = reasons,
                };
        }
    }
}
