using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Manufacturing.Application;

public sealed record ManufacturingUsdCostRateViewV1(DateOnly WeighingDay, DateOnly BulletinDay, string TryPerUsd,
    Guid RecordId, string RecordVersion, string Source);
public sealed record ManufacturingUsdCostLineViewV1(Guid OutputLabelId, string OutputClass, string Kilograms,
    string? UnitCostPerKgUsd, string? AmountUsd, string? SeparateScrapExpenseUsd, string RemainderUsd);
public sealed record ManufacturingProductionUsdCostViewV1(Guid OrderId, long OrderVersion, string Status, string? CostPerKgUsd,
    string? InputTotalUsd, string? DistributedTotalUsd, IReadOnlyList<ManufacturingUsdCostLineViewV1> Lines,
    IReadOnlyList<ManufacturingUsdCostRateViewV1> ScrapRates, string? ReasonCode);

public sealed partial class ManufacturingCommandService
{
    /// <summary>URT-D82/105: an independent USD display over actual USD issue stamps; no Accounting/SMM USD posting.</summary>
    public async Task<ManufacturingApiResult<ManufacturingProductionUsdCostViewV1>> GetProductionUsdCostAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty) return Invalid<ManufacturingProductionUsdCostViewV1>("orderId");
        if (!HasContext() || ActorGuid() is not { } actor) return Denied<ManufacturingProductionUsdCostViewV1>();
        var companyId = companyContext.CompanyId;
        if (await FenceCapturedAsync<ManufacturingProductionUsdCostViewV1>(ErpPermissions.ManufacturingReportsRead, companyId, actor, cancellationToken) is { } denied) return denied;
        var order = await orders.FindOrderAsync(orderId, cancellationToken);
        if (order is null) return NotFoundResult<ManufacturingProductionUsdCostViewV1>();
        ManufacturingProductionUsdCostViewV1 view;
        try
        {
            var read = costSource is null ? null : await costSource.ReadAsync(companyId, orderId, cancellationToken);
            if (read is null || read.CompanyId != companyId || read.OrderId != orderId || read.UsdInput is null
                || read.Provenance?.OrderVersion != order.Version)
                view = Empty("unavailable", read?.UsdUnavailableReason ?? "production_usd_source_unavailable");
            else
            {
                // Reuse the one numeric costing/rounding engine in the independently stamped USD dimension.
                // The original TL/currency facts remain untouched in the source evidence.
                var calculated = await new ManufacturingProductionCostPort(new UsdDisplaySource(read with { Input = read.UsdInput }))
                    .ReadAsync(companyId, orderId, cancellationToken);
                var status = calculated.Status switch { ManufacturingScrapCostDistributionStatusV1.Available => "available",
                    ManufacturingScrapCostDistributionStatusV1.Pending => "pending", ManufacturingScrapCostDistributionStatusV1.Unreadable => "unreadable", _ => "unavailable" };
                view = status == "available" ? new(orderId, order.Version, status, Text(calculated.RecordedCostPerKgTl!.Value),
                    Text(calculated.InputTotalTl!.Value), Text(calculated.DistributedTotalTl!.Value), calculated.Lines.Select(line => new ManufacturingUsdCostLineViewV1(
                        line.OutputLabelId, line.OutputClass.ToString(), Text(line.Kilograms), line.RecordedUnitCostPerKgTl is { } unit ? Text(unit) : null,
                        line.RecordedAmountTl is { } amount ? Text(amount) : null, line.RecordedSeparateScrapExpenseTl is { } expense ? Text(expense) : null,
                        Text(line.RemainderTl))).ToArray(), read.UsdRateEvidence.Select(rate => new ManufacturingUsdCostRateViewV1(rate.WeighingDay,
                        rate.BulletinDay, Text(rate.TryPerUsd), rate.RecordId, rate.RecordVersion, rate.Source)).ToArray(), null)
                    : Empty(status, calculated.ReasonCode ?? "production_usd_source_unavailable");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { view = Empty("unavailable", "production_usd_source_unavailable"); }
        if ((await orders.FindOrderAsync(orderId, cancellationToken))?.Version != order.Version) return new(ManufacturingApiStatus.Unavailable, null, false, null);
        if (await FenceCapturedAsync<ManufacturingProductionUsdCostViewV1>(ErpPermissions.ManufacturingReportsRead, companyId, actor, cancellationToken) is { } finalDenied) return finalDenied;
        return new(ManufacturingApiStatus.Ok, view, false, null);
        ManufacturingProductionUsdCostViewV1 Empty(string status, string reason) => new(orderId, order.Version, status, null, null, null, [], [], reason);
    }

    private sealed class UsdDisplaySource(ManufacturingScrapCostDistributionSourceReadV1 read) : IManufacturingScrapCostDistributionSourceV1
    {
        public Task<ManufacturingScrapCostDistributionSourceReadV1> ReadAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken) => Task.FromResult(read);
    }
}
