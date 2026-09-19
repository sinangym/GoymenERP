using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests.Engines;

/// <summary>
/// GDC-BAL/1 (master v3 §28.9 BAL-003 / 004 / 006). §28.9 states no golden example, so none is invented:
/// every case uses explicit inputs with exact expected values. MFG-4's close runs on BAL-003; its behaviour
/// stays pinned by <see cref="WipCloseTests"/> and <see cref="OrderTargetAndCloseTests"/>.
/// </summary>
public sealed class MassBalanceEngineTests
{
    [Fact]
    public void Bal003_reconciles_every_category_and_keeps_the_differences_sign()
    {
        // 1000 = 700 + 100 + 50 + 40 + 10 + 60 + 0 → 960 accounted, 40 kg nobody accounted for.
        var gap = MassBalanceEngineV1.Difference(new MassDifferenceInputV1(1000m, 700m, 100m, 50m, 40m, 10m, 60m, 0m)).Value!;
        Assert.Equal((MassBalanceEngineV1.FormulaVersion, 960m, 40m, false),
            (gap.FormulaVersion, gap.AccountedKg, gap.DifferenceKg, gap.IsFullyAccounted));

        // More declared than issued: the difference is negative, never clamped.
        var over = MassBalanceEngineV1.Difference(new MassDifferenceInputV1(1000m, 900m, 100m, 0m, 0.125m, 0m, 0m, 0m)).Value!;
        Assert.Equal(-0.125m, over.DifferenceKg);

        // Exactly accounted, with a signed approved correction.
        var exact = MassBalanceEngineV1.Difference(new MassDifferenceInputV1(1000m, 990m, 0m, 0m, 15m, 0m, 0m, -5m)).Value!;
        Assert.Equal((1000m, 0m, true), (exact.AccountedKg, exact.DifferenceKg, exact.IsFullyAccounted));
    }

    [Fact]
    public void Bal003_names_every_missing_category_and_refuses_a_fourth_decimal_or_a_negative_mass()
    {
        Assert.Equal(
            ["actualInputKg", "goodKg", "secondQualityKg", "recoverableByProductKg", "scrapKg", "nonStockProcessLossKg",
                "remainingWipKg", "approvedMeasurementCorrectionKg"],
            MassBalanceEngineV1.Difference(new MassDifferenceInputV1(null, null, null, null, null, null, null, null)).MissingParameters);

        var negative = MassBalanceEngineV1.Difference(new MassDifferenceInputV1(1000m, 900m, 0m, 0m, -1m, 0m, 0m, 0m));
        Assert.Equal((EngineOutcome.Refused, EngineCodes.ValueNegative, "scrapKg"),
            (negative.Outcome, negative.RefusalCode, negative.RefusedParameter));
        var precision = MassBalanceEngineV1.Difference(new MassDifferenceInputV1(1000m, 900m, 0m, 0m, 0m, 0m, 0m, 0.0001m));
        Assert.Equal((EngineCodes.PrecisionExceeded, "approvedMeasurementCorrectionKg"),
            (precision.RefusalCode, precision.RefusedParameter));
    }

    [Fact]
    public void Bal004_is_fire_over_raw_material_times_100_both_stated_by_the_caller()
    {
        Assert.Equal(4m, MassBalanceEngineV1.ScrapPercent(new ScrapPercentInputV1(40m, 1000m)).Value!.FirePercent);
        Assert.Equal(0m, MassBalanceEngineV1.ScrapPercent(new ScrapPercentInputV1(0m, 1000m)).Value!.FirePercent);
        // Multiply first, divide once: 1 × 100 / 3 is the one quotient, not (1 / 3) × 100.
        Assert.Equal(100m / 3m, MassBalanceEngineV1.ScrapPercent(new ScrapPercentInputV1(1m, 3m)).Value!.FirePercent);

        Assert.Equal(["fireKg"], MassBalanceEngineV1.ScrapPercent(new ScrapPercentInputV1(null, 1000m)).MissingParameters);
        Assert.Equal((EngineCodes.ValueNotPositive, "rawMaterialKg"),
            Refusal(MassBalanceEngineV1.ScrapPercent(new ScrapPercentInputV1(40m, 0m))));
    }

    [Fact]
    public void Bal006_is_the_actual_mass_over_the_actual_count_and_a_missing_count_is_unavailable()
    {
        Assert.Equal(12.5m, MassBalanceEngineV1.ActualUnitMass(new ActualUnitMassInputV1(125m, 10)).Value!.ActualUnitKg);
        Assert.Equal(["actualCount"], MassBalanceEngineV1.ActualUnitMass(new ActualUnitMassInputV1(125m, null)).MissingParameters);
        Assert.Equal((EngineCodes.CountNotPositive, "actualCount"),
            Refusal(MassBalanceEngineV1.ActualUnitMass(new ActualUnitMassInputV1(125m, 0))));
    }

    [Fact]
    public void Every_output_class_is_a_bal003_category_so_the_roll_balance_drops_nothing()
    {
        // A roll with one kilogram in each class and nothing else: the difference is exactly what was issued
        // minus the five kilograms. A class added later without a BAL-003 category would leave it out and fail here.
        var classes = Enum.GetValues<OutputClass>();
        var balance = new MassBalance(Guid.NewGuid(), new Kilograms(100m), Kilograms.Zero,
            classes.ToDictionary(outputClass => outputClass, _ => new Kilograms(1m)).AsReadOnly(), Kilograms.Zero);

        Assert.Equal(balance.DeclaredOutputs.Value, balance.Reconciliation.AccountedKg);
        Assert.Equal(100m - classes.Length, balance.Unexplained);
        Assert.Equal(MassBalanceEngineV1.FormulaVersion, balance.Reconciliation.FormulaVersion);
    }

    private static (string?, string?) Refusal<T>(EngineResult<T> result)
        where T : class => (result.RefusalCode, result.RefusedParameter);
}
