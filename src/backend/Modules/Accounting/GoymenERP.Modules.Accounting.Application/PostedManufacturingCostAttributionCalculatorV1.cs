using System.Globalization;
using System.Numerics;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Frozen physical output shares; commercial invoice quantities are never accepted here.</summary>
public sealed record PostedCostAttributionWeightV1(Guid ShipmentId, Guid MovementId, int MovementLineNumber,
    AccountingInventoryCostRatioV1 NetQuantityKg, DateTimeOffset RecordedAtUtc, long? ProvenOriginalSequence = null);

public sealed record PostedCostAttributionAmountV1(PostedCostAttributionWeightV1 Source,
    AccountingInventoryCostRatioV1 ExactSignedAmount, decimal RoundedSignedAmount, decimal SignedResidue);

public sealed record PostedCostAttributionExactSourceV1(PostedCostAttributionWeightV1 Source,
    AccountingInventoryCostRatioV1 ExactSignedAmount);

public sealed record PostedCostAttributionCalculationV1(decimal SignedTotal, Guid CurrencyId, int Scale,
    AccountingInventoryCostRatioV1 ConsumedQuantityKg, IReadOnlyList<PostedCostAttributionAmountV1> Shares);

/// <summary>
/// The human attribution decision distributes ONE already-posted output's consumed amount. It creates no journal,
/// values no stock and is not the pending initial-recognition rounding rule. Different outputs are never pooled.
/// Half-away-from-zero is delegated to the shared Money owner. The signed remainder goes to the largest physical
/// share, then the earliest actual recorded shipment; an unproved exact chronology tie is not ordered by a GUID.
/// </summary>
public static class PostedManufacturingCostAttributionCalculatorV1
{
    public const int MaximumShares = 4096;

    public static bool TryCalculate(decimal signedTotal, Guid currencyId, int scale,
        AccountingInventoryCostRatioV1 expectedConsumedQuantityKg, IReadOnlyCollection<PostedCostAttributionWeightV1>? weights,
        out PostedCostAttributionCalculationV1? result, out string? reason)
        => TryCalculateCore(signedTotal, currencyId, scale, expectedConsumedQuantityKg, weights, null, out result, out reason);

    /// <summary>
    /// The separately approved purchase extension retains each shipment's actual exact cost; kg orders only the
    /// rounding residue. The supplied total is the owner's already rounded line amount, not a new stock valuation.
    /// </summary>
    public static bool TryCalculatePurchase(decimal signedTotal, Guid currencyId, int scale,
        AccountingInventoryCostRatioV1 expectedConsumedQuantityKg, IReadOnlyCollection<PostedCostAttributionExactSourceV1>? sources,
        out PostedCostAttributionCalculationV1? result, out string? reason)
    {
        result = null; reason = "attribution_source_invalid";
        if (signedTotal < 0 || sources is not { Count: > 0 and <= MaximumShares }
            || sources.Any(x => x is null || x.Source is null
                || !AccountingInventoryCostAmountsV1.TryRead(x.ExactSignedAmount, out var n, out _) || n < 0)
            || sources.Select(x => (x.Source.MovementId, x.Source.MovementLineNumber)).Distinct().Count() != sources.Count) return false;
        return TryCalculateCore(signedTotal, currencyId, scale, expectedConsumedQuantityKg, sources.Select(x => x.Source).ToArray(),
            sources.ToDictionary(x => (x.Source.MovementId, x.Source.MovementLineNumber), x => x.ExactSignedAmount), out result, out reason);
    }

    private static bool TryCalculateCore(decimal signedTotal, Guid currencyId, int scale,
        AccountingInventoryCostRatioV1 expectedConsumedQuantityKg, IReadOnlyCollection<PostedCostAttributionWeightV1>? weights,
        IReadOnlyDictionary<(Guid, int), AccountingInventoryCostRatioV1>? exactSourceAmounts,
        out PostedCostAttributionCalculationV1? result, out string? reason)
    {
        result = null; reason = "attribution_source_invalid";
        if (currencyId == Guid.Empty || scale is < 0 or > Money.MaximumScale || !Money.FitsScale(signedTotal, scale)
            || weights is not { Count: > 0 and <= MaximumShares }
            || !AccountingInventoryCostAmountsV1.TryRead(expectedConsumedQuantityKg, out var expectedN, out _)
            || expectedN <= 0) return false;
        var rows = weights.ToArray();
        // The current Inventory shipment owner proves one physical movement line per definite shipment.
        // A future multi-line shipment requires its own explicit complete share contract, not an inferred split.
        if (rows.Any(x => x is null || x.ShipmentId == Guid.Empty || x.MovementId == Guid.Empty || x.MovementLineNumber < 1
                || x.RecordedAtUtc == default || x.RecordedAtUtc.Offset != TimeSpan.Zero || x.ProvenOriginalSequence is < 1
                || !AccountingInventoryCostAmountsV1.TryRead(x.NetQuantityKg, out var n, out _) || n <= 0)
            || rows.Select(x => x.ShipmentId).Distinct().Count() != rows.Length
            || rows.Select(x => (x.MovementId, x.MovementLineNumber)).Distinct().Count() != rows.Length) return false;
        try
        {
            var totalWeight = rows.Aggregate(AccountingInventoryCostAmountsV1.FromDecimal(0),
                (sum, row) => AccountingInventoryCostAmountsV1.Add(sum, row.NetQuantityKg));
            if (totalWeight != expectedConsumedQuantityKg) { reason = "attribution_quantity_incomplete"; return false; }
            AccountingInventoryCostAmountsV1.TryRead(totalWeight, out var weightN, out var weightD);
            var outputAmount = AccountingInventoryCostAmountsV1.FromDecimal(signedTotal);
            var amounts = new List<PostedCostAttributionAmountV1>(rows.Length);
            foreach (var row in rows)
            {
                var ratio = exactSourceAmounts is not null ? exactSourceAmounts[(row.MovementId, row.MovementLineNumber)]
                    : AccountingInventoryCostAmountsV1.Multiply(outputAmount,
                        AccountingInventoryCostAmountsV1.Multiply(row.NetQuantityKg, Ratio(weightD, weightN)));
                AccountingInventoryCostAmountsV1.TryRead(ratio, out var numerator, out var denominator);
                amounts.Add(new(row, ratio, Money.Round(numerator, denominator, currencyId, scale).Amount, 0));
            }
            var unitFactor = BigInteger.Pow(10, scale);
            BigInteger Units(decimal value)
            {
                AccountingInventoryCostAmountsV1.TryRead(AccountingInventoryCostAmountsV1.FromDecimal(value), out var n, out var d);
                var units = BigInteger.DivRem(n * unitFactor, d, out var remainder);
                if (remainder != 0) throw new ArgumentException("A posted amount must fit its money profile.");
                return units;
            }
            decimal Amount(BigInteger units) => Money.Round(units, unitFactor, currencyId, scale).Amount;
            var totalUnits = Units(signedTotal);
            var residue = totalUnits - amounts.Aggregate(BigInteger.Zero, (sum, value) => sum + Units(value.RoundedSignedAmount));
            var available = rows.ToList();
            while (residue != 0)
            {
                var reducing = residue.Sign != totalUnits.Sign;
                // A share already at zero has no remaining capacity to absorb an opposite-signed remainder.
                if (reducing) available.RemoveAll(row => amounts.Single(x => x.Source == row).RoundedSignedAmount == 0);
                if (available.Count == 0) { reason = "attribution_residue_unrepresentable"; return false; }
                var largest = available.Select(x => x.NetQuantityKg).Aggregate((left, right) => Compare(left, right) >= 0 ? left : right);
                var candidates = available.Where(x => Compare(x.NetQuantityKg, largest) == 0).ToArray();
                var earliest = candidates.Min(x => x.RecordedAtUtc);
                candidates = candidates.Where(x => x.RecordedAtUtc == earliest).ToArray();
                PostedCostAttributionWeightV1 winner;
                if (candidates.Length == 1) winner = candidates[0];
                else
                {
                    if (candidates.Any(x => !x.ProvenOriginalSequence.HasValue)
                        || candidates.Select(x => x.ProvenOriginalSequence!.Value).Distinct().Count() != candidates.Length)
                    {
                        var groupAmount = amounts.Where(x => candidates.Contains(x.Source))
                            .Aggregate(BigInteger.Zero, (sum, value) => sum + Units(value.RoundedSignedAmount));
                        // If every tied share is exhausted, their internal ordering cannot affect any assigned amount.
                        if (!reducing || BigInteger.Abs(residue) < BigInteger.Abs(groupAmount))
                        { reason = "attribution_chronology_unproven"; return false; }
                        foreach (var candidate in candidates)
                        {
                            var groupIndex = amounts.FindIndex(x => x.Source == candidate);
                            var applied = -Units(amounts[groupIndex].RoundedSignedAmount);
                            amounts[groupIndex] = amounts[groupIndex] with { RoundedSignedAmount = 0, SignedResidue = Amount(applied) };
                            residue -= applied; available.Remove(candidate);
                        }
                        continue;
                    }
                    winner = candidates.MinBy(x => x.ProvenOriginalSequence!.Value)!;
                }
                var index = amounts.FindIndex(x => x.Source == winner);
                var originalUnits = Units(amounts[index].RoundedSignedAmount);
                var appliedResidue = reducing && BigInteger.Abs(residue) > BigInteger.Abs(originalUnits)
                    ? -originalUnits : residue;
                amounts[index] = amounts[index] with { RoundedSignedAmount = Amount(originalUnits + appliedResidue),
                    SignedResidue = Amount(appliedResidue) };
                residue -= appliedResidue; available.Remove(winner);
            }
            if (amounts.Aggregate(BigInteger.Zero, (sum, value) => sum + Units(value.RoundedSignedAmount)) != totalUnits) return false;
            // This order is serialization only; it never selects the monetary remainder recipient.
            result = new(signedTotal, currencyId, scale, totalWeight,
                amounts.OrderBy(x => x.Source.ShipmentId).ThenBy(x => x.Source.MovementLineNumber).ToArray());
            reason = null; return true;
        }
        catch (Exception error) when (error is OverflowException or ArgumentException or MoneyRuleException)
        { reason = "attribution_amount_unrepresentable"; return false; }
    }

    private static int Compare(AccountingInventoryCostRatioV1 left, AccountingInventoryCostRatioV1 right)
    {
        AccountingInventoryCostAmountsV1.TryRead(left, out var ln, out var ld);
        AccountingInventoryCostAmountsV1.TryRead(right, out var rn, out var rd);
        return (ln * rd).CompareTo(rn * ld);
    }
    private static AccountingInventoryCostRatioV1 Ratio(BigInteger numerator, BigInteger denominator)
    {
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        return new((numerator / gcd).ToString(CultureInfo.InvariantCulture), (denominator / gcd).ToString(CultureInfo.InvariantCulture));
    }
}
