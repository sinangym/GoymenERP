namespace GoymenERP.Modules.Inventory.Domain.FutureSupply;

public sealed record FutureSupplyReductionAllocation(Guid Id, long Version, decimal RemainingQuantity,
    decimal KilogramsPerUnit, DateOnly? ExpectedDeliveryDate, DateTimeOffset CreatedAtUtc);

public sealed record FutureSupplyReductionSelection(Guid Id, long ExpectedVersion, decimal Quantity, bool IsPhysical);
public sealed record FutureSupplyReductionStep(Guid Id, long ExpectedVersion, decimal Quantity);

public enum FutureSupplyReductionFailure { InvalidSelection, VersionConflict, PhysicalReleaseUnavailable, ConversionUnavailable }

public sealed class FutureSupplyReductionException(FutureSupplyReductionFailure failure) : Exception(failure.ToString())
{
    public FutureSupplyReductionFailure Failure { get; } = failure;
}

/// <summary>KLN-D28/30/31/32. Deterministic exact arithmetic; no state changes and no precision cutting.</summary>
public static class FutureSupplyDemandReduction
{
    public static IReadOnlyList<FutureSupplyReductionStep> Plan(decimal newOpenKg, decimal physicalKg,
        IReadOnlyList<FutureSupplyReductionAllocation> allocations,
        IReadOnlyList<FutureSupplyReductionSelection>? selections = null)
    {
        if (newOpenKg < 0m || physicalKg < 0m || allocations.Any(item => item.Id == Guid.Empty || item.Version < 1
                || item.RemainingQuantity <= 0m || item.KilogramsPerUnit <= 0m)
            || allocations.Select(item => item.Id).Distinct().Count() != allocations.Count)
        {
            throw new ArgumentException("Reduction sources must be complete, nonnegative and unique.");
        }

        var futureKg = allocations.Sum(item => item.RemainingQuantity * item.KilogramsPerUnit);
        var excessKg = Math.Max(0m, physicalKg + futureKg - newOpenKg);
        if (selections is not null)
        {
            if (selections.Select(item => item.Id).Distinct().Count() != selections.Count)
                throw Failure(FutureSupplyReductionFailure.InvalidSelection);
            var result = new List<FutureSupplyReductionStep>();
            decimal selectedKg = 0m;
            var physicalSelected = false;
            foreach (var selection in selections)
            {
                if (selection.IsPhysical)
                {
                    physicalSelected = true;
                    continue;
                }
                var allocation = allocations.SingleOrDefault(item => item.Id == selection.Id)
                    ?? throw Failure(FutureSupplyReductionFailure.InvalidSelection);
                if (selection.ExpectedVersion != allocation.Version)
                    throw Failure(FutureSupplyReductionFailure.VersionConflict);
                RequireExactQuantity(selection.Quantity);
                if (selection.Quantity <= 0m || selection.Quantity > allocation.RemainingQuantity)
                    throw Failure(FutureSupplyReductionFailure.InvalidSelection);
                selectedKg += selection.Quantity * allocation.KilogramsPerUnit;
                result.Add(new(allocation.Id, allocation.Version, selection.Quantity));
            }
            if (physicalSelected) throw Failure(FutureSupplyReductionFailure.PhysicalReleaseUnavailable);
            if (selectedKg != excessKg) throw Failure(FutureSupplyReductionFailure.InvalidSelection);
            return result;
        }

        // No partial writes when physical release would also be needed.
        if (excessKg > futureKg) throw Failure(FutureSupplyReductionFailure.PhysicalReleaseUnavailable);
        var steps = new List<FutureSupplyReductionStep>();
        foreach (var allocation in allocations.OrderByDescending(item => item.ExpectedDeliveryDate ?? DateOnly.MaxValue)
                     .ThenByDescending(item => item.CreatedAtUtc).ThenBy(item => item.Id))
        {
            if (excessKg == 0m) break;
            var quantity = Math.Min(allocation.RemainingQuantity, excessKg / allocation.KilogramsPerUnit);
            RequireExactQuantity(quantity);
            // Decimal division may round at scale 28; never silently leave a tiny over-allocation.
            var releaseKg = quantity * allocation.KilogramsPerUnit;
            if (releaseKg > excessKg) throw Failure(FutureSupplyReductionFailure.ConversionUnavailable);
            steps.Add(new(allocation.Id, allocation.Version, quantity));
            excessKg -= releaseKg;
        }
        if (excessKg != 0m) throw Failure(FutureSupplyReductionFailure.ConversionUnavailable);
        return steps;
    }

    private static void RequireExactQuantity(decimal quantity)
    {
        if (decimal.Round(quantity, FutureSupplyQuantity.MaximumDecimals, MidpointRounding.ToZero) != quantity)
            throw Failure(FutureSupplyReductionFailure.ConversionUnavailable);
    }

    private static FutureSupplyReductionException Failure(FutureSupplyReductionFailure failure) => new(failure);
}
