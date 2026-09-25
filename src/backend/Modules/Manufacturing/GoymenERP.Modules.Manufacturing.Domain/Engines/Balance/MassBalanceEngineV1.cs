namespace GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;

/// <summary>
/// BAL-003 inputs: the actual input mass and every category master v3 §28.9 reconciles it against. Each is
/// stated by the caller (a stated 0 is a value); a missing one is unavailable, never 0. The approved
/// measurement correction is signed (a correction can go either way).
/// </summary>
public sealed record MassDifferenceInputV1(
    decimal? ActualInputKg,
    decimal? GoodKg,
    decimal? SecondQualityKg,
    decimal? RecoverableByProductKg,
    decimal? ScrapKg,
    decimal? NonStockProcessLossKg,
    decimal? RemainingWipKg,
    decimal? ApprovedMeasurementCorrectionKg);

/// <summary>BAL-003, exact: the accounted mass and the signed difference (positive = mass nobody accounted for).</summary>
public sealed record MassDifferenceV1(string FormulaVersion, decimal AccountedKg, decimal DifferenceKg)
{
    public bool IsFullyAccounted => DifferenceKg == 0m;
}

/// <summary>BAL-004 inputs, both stated by the caller: which mass is "fire" and which is the raw material is the caller's definition.</summary>
public sealed record ScrapPercentInputV1(decimal? FireKg, decimal? RawMaterialKg);

/// <summary>BAL-004, unrounded.</summary>
public sealed record ScrapPercentV1(string FormulaVersion, decimal FirePercent);

/// <summary>BAL-006 inputs: the actual mass and the actual piece count, both measured facts.</summary>
public sealed record ActualUnitMassInputV1(decimal? ActualKg, int? ActualCount);

/// <summary>BAL-006, unrounded.</summary>
public sealed record ActualUnitMassV1(string FormulaVersion, decimal ActualUnitKg);

/// <summary>
/// The production mass-balance engine, version 1: master v3 §28.9 BAL-003, BAL-004 and BAL-006, pure and
/// deterministic, decimal throughout, on the same core as the five process engines.
/// <list type="bullet">
/// <item><b>BAL-003</b> (<see cref="Difference"/>): <c>Kütle Farkı = Toplam Fiilî Girdi − (İyi Ürün + İkinci Kalite
/// + Geri Kazanılabilir Yan Ürün + Hurda + Stok Dışı Proses Kaybı + Kalan WIP + Onaylı Ölçüm Düzeltmesi)</c>. The
/// difference keeps its sign and is never folded into a category ("açıklanamayan fark otomatik fire olmaz"). Exact:
/// sums and one subtraction of at-most-three-decimal masses.</item>
/// <item><b>BAL-004</b> (<see cref="ScrapPercent"/>): <c>FireKg / HammaddeKg × 100</c>, evaluated as
/// <c>FireKg × 100 / HammaddeKg</c> (one division). Both masses are the caller's: this engine does not decide which
/// output classes are "fire" or whether the raw material is the issued or the net issued mass.</item>
/// <item><b>BAL-006</b> (<see cref="ActualUnitMass"/>): <c>GerçekKg / GerçekAdet</c>.</item>
/// <item><b>Not in version 1:</b> BAL-005 (the distribution of an empty actual quantity needs the §28.1 rounding
/// stage, not decided), tolerance (Ö7: no approved profile) and BAL-001 / BAL-002 (the donor's reference
/// behaviour only).</item>
/// </list>
/// </summary>
public static class MassBalanceEngineV1
{
    public const string FormulaVersion = "GDC-BAL/1 (master v3 §28.9)";

    private const decimal Percent = 100m;

    public static EngineResult<MassDifferenceV1> Difference(MassDifferenceInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var actualInput = inputs.NonNegativeMeasure(input.ActualInputKg, "actualInputKg");
        var good = inputs.NonNegativeMeasure(input.GoodKg, "goodKg");
        var secondQuality = inputs.NonNegativeMeasure(input.SecondQualityKg, "secondQualityKg");
        var recoverable = inputs.NonNegativeMeasure(input.RecoverableByProductKg, "recoverableByProductKg");
        var scrap = inputs.NonNegativeMeasure(input.ScrapKg, "scrapKg");
        var processLoss = inputs.NonNegativeMeasure(input.NonStockProcessLossKg, "nonStockProcessLossKg");
        var wip = inputs.NonNegativeMeasure(input.RemainingWipKg, "remainingWipKg");
        var correction = inputs.SignedMeasure(input.ApprovedMeasurementCorrectionKg, "approvedMeasurementCorrectionKg");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<MassDifferenceV1>();
        }

        var accounted = good + secondQuality + recoverable + scrap + processLoss + wip + correction;
        return EngineResult<MassDifferenceV1>.Computed(new MassDifferenceV1(FormulaVersion, accounted, actualInput - accounted));
    }

    public static EngineResult<ScrapPercentV1> ScrapPercent(ScrapPercentInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var fire = inputs.NonNegativeMeasure(input.FireKg, "fireKg");
        var rawMaterial = inputs.Measure(input.RawMaterialKg, "rawMaterialKg");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<ScrapPercentV1>();
        }

        return EngineResult<ScrapPercentV1>.Computed(new ScrapPercentV1(FormulaVersion, fire * Percent / rawMaterial));
    }

    public static EngineResult<ActualUnitMassV1> ActualUnitMass(ActualUnitMassInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var actualKg = inputs.Measure(input.ActualKg, "actualKg");
        var count = inputs.Count(input.ActualCount, "actualCount");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<ActualUnitMassV1>();
        }

        return EngineResult<ActualUnitMassV1>.Computed(new ActualUnitMassV1(FormulaVersion, actualKg / count));
    }
}
