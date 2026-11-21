namespace GoymenERP.Modules.Logistics.Domain.WeighingDifferences;

/// <summary>A weighing-difference rule refusal with its stable code. Nothing is partly applied when it is thrown.</summary>
public sealed class WeighingDifferenceRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The stable refusal codes of the sales weighing-difference record (SAT-D319..D325, SAT-D340..D344).</summary>
public static class WeighingDifferenceRuleCodes
{
    public const string UnitNotKilogram = "logistics_weighing_difference_unit_not_kilogram";
    public const string MeasurementMissing = "logistics_weighing_difference_measurement_missing";
    public const string MeasurementUnexpected = "logistics_weighing_difference_measurement_unexpected";
    public const string MeasurementDuplicate = "logistics_weighing_difference_measurement_duplicate";
    public const string NotPendingApproval = "logistics_weighing_difference_not_pending_approval";
    public const string SelfApproval = "logistics_weighing_difference_self_approval";
    public const string EnteredByUnknown = "logistics_weighing_difference_entered_by_unknown";
    public const string VersionConflict = "logistics_weighing_difference_version_conflict";
}

/// <summary>
/// The theoretical ↔ scale bands of DEC-STK-009 / OPEN-004 (warning, approval, block), read from Settings. In sales they are
/// recorded and reported only: they never stop the dispatch and never ask for an approval (SAT-D324). The values are never
/// written in code; an unreadable setting leaves the band level <see cref="WeighingDifferenceBandLevel.NotEvaluated"/>.
/// </summary>
public sealed record WeighingDifferenceBands
{
    public WeighingDifferenceBands(decimal warningPercent, decimal approvalPercent, decimal blockPercent)
    {
        if (warningPercent <= 0m || approvalPercent <= warningPercent || blockPercent <= approvalPercent)
            throw new ArgumentOutOfRangeException(nameof(warningPercent),
                "Kantar bantları sıfırdan büyük ve artan sırada olmalıdır.");
        WarningPercent = warningPercent;
        ApprovalPercent = approvalPercent;
        BlockPercent = blockPercent;
    }

    public decimal WarningPercent { get; }
    public decimal ApprovalPercent { get; }
    public decimal BlockPercent { get; }
}

/// <summary>
/// The settings a record is evaluated with. <see cref="ExcessApprovalRatePercent"/> is the single SAT-D344 value (the SAT-D319 (5)
/// starting value is a Settings value, never a code constant); null means Settings could not be read, which never auto-completes
/// a difference (fail-closed: the record waits for approval). <see cref="Bands"/> null means the bands were unreadable.
/// </summary>
public sealed record WeighingDifferencePolicy
{
    public WeighingDifferencePolicy(decimal? excessApprovalRatePercent, WeighingDifferenceBands? bands)
    {
        if (excessApprovalRatePercent is { } rate && (rate <= 0m || rate > WeighingDifferenceCalculator.PercentScale))
            throw new ArgumentOutOfRangeException(nameof(excessApprovalRatePercent),
                "Kantar farkı onay oranı sıfırdan büyük ve en fazla yüz olmalıdır.");
        ExcessApprovalRatePercent = excessApprovalRatePercent;
        Bands = bands;
    }

    public decimal? ExcessApprovalRatePercent { get; }
    public WeighingDifferenceBands? Bands { get; }

    public static WeighingDifferencePolicy Unreadable { get; } = new(null, null);
}

/// <summary>The SAT-D324 band the theoretical ↔ scale difference falls into (record and report only in sales).</summary>
public enum WeighingDifferenceBandLevel
{
    NotEvaluated = 0,
    None = 1,
    Warning = 2,
    Approval = 3,
    Block = 4,
}

/// <summary>What the SAT-D320/D341/D342 excess rule decided for a dispatch note.</summary>
public enum WeighingDifferenceDecision
{
    /// <summary>No product leaves more kg than the system holds (SAT-D322 (1), SAT-D342): no difference record, no approval.</summary>
    NoExcess = 1,

    /// <summary>The excess is below the approval rate: it is completed without approval (SAT-D319 (4)–(5)).</summary>
    WithinRate = 2,

    /// <summary>The excess is at or above the rate (SAT-D341), or the rate was unreadable: the sale waits for approval.</summary>
    ApprovalRequired = 3,
}

/// <summary>One dispatch-note line as the record sees it: the note's system kg of one product (WGT-002: the theoretical side).</summary>
public sealed record WeighingDifferenceNoteLine(int LineNo, Guid ShipmentId, Guid ProductId, Quantity Kg);

/// <summary>
/// One product's scale kg and the free company-owned physical kg Inventory still holds for it after the note's own shipments.
/// </summary>
public sealed record WeighingDifferenceProductMeasurement(Guid ProductId, Quantity ScaleKg, Quantity RemainingPhysicalKg);

/// <summary>One product's result. <see cref="DifferenceKg"/> is signed (scale − note); the reverse direction yields no excess (SAT-D320 (3)).</summary>
public sealed record WeighingDifferenceProductResult(
    Guid ProductId,
    Quantity NoteKg,
    Quantity ScaleKg,
    decimal DifferenceKg,
    Quantity RemainingPhysicalKg,
    Quantity PhysicallyCoveredKg,
    Quantity ExcessKg,
    int? TargetLineNo);

/// <summary>The deterministic evaluation of one dispatch note. Every screen, report and command reads this same result.</summary>
public sealed record WeighingDifferenceEvaluation(
    Quantity TheoreticalKg,
    Quantity ScaleKg,
    decimal DifferenceKg,
    WeighingDifferenceBandLevel BandLevel,
    WeighingDifferenceBands? Bands,
    IReadOnlyList<WeighingDifferenceProductResult> Products,
    Quantity TotalExcessKg,
    decimal? ApprovalRatePercent,
    WeighingDifferenceDecision Decision);

/// <summary>
/// The single backend calculator of the sales weighing difference (TEK formula rule: UI and reports never recompute it).
/// <list type="bullet">
/// <item>Theoretical kg is the dispatch-note total (WGT-002: difference = scale − note); bands compare |difference| with it,
/// at-or-above (SAT-D324, record only).</item>
/// <item>Per product, the kg above the note is first covered by free physical stock (SAT-D322 (1), SAT-D342); only the rest is
/// excess. Products never offset each other and the reverse direction never counts (SAT-D343, SAT-D320 (3)).</item>
/// <item>The excess total is measured on the dispatch-note total system kg (SAT-D320 (2), SAT-D321 (4)); exactly the rate needs
/// approval (SAT-D341). The comparison is exact (cross-multiplied, no rounding).</item>
/// <item>The target line of a product's excess is its line with the most kg, the first note line on a tie (SAT-D322 (3)–(4)).</item>
/// </list>
/// </summary>
public static class WeighingDifferenceCalculator
{
    public const int MaximumLines = 200;

    /// <summary>The most decimals a Settings percentage (rate or band) may carry; one with more is treated as unreadable.</summary>
    public const int PercentDecimals = 4;
    /// <summary>A percentage's whole: rates and bands are percentages of the note total.</summary>
    public const decimal PercentScale = 100m;

    public static WeighingDifferenceEvaluation Evaluate(
        IReadOnlyCollection<WeighingDifferenceNoteLine> noteLines,
        IReadOnlyCollection<WeighingDifferenceProductMeasurement> measurements,
        WeighingDifferencePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(noteLines);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(policy);
        RequireLines(noteLines);
        var byProduct = RequireMeasurements(noteLines, measurements);

        var kg = Quantity.Zero(QuantityUnit.Kilogram);
        var theoretical = kg;
        var scale = kg;
        var totalExcess = kg;
        var products = new List<WeighingDifferenceProductResult>();
        foreach (var group in noteLines.GroupBy(line => line.ProductId).OrderBy(group => group.Min(line => line.LineNo)))
        {
            var measurement = byProduct[group.Key];
            var noteKg = group.Aggregate(kg, (sum, line) => sum.Add(line.Kg));
            var difference = measurement.ScaleKg.Value - noteKg.Value;
            var above = difference > 0m ? Quantity.Of(difference, QuantityUnit.Kilogram) : kg;
            var covered = above.IsGreaterThan(measurement.RemainingPhysicalKg) ? measurement.RemainingPhysicalKg : above;
            var excess = above.Subtract(covered);
            int? target = excess.IsZero
                ? null
                : group.OrderByDescending(line => line.Kg.Value).ThenBy(line => line.LineNo).First().LineNo;
            products.Add(new WeighingDifferenceProductResult(group.Key, noteKg, measurement.ScaleKg, difference,
                measurement.RemainingPhysicalKg, covered, excess, target));
            theoretical = theoretical.Add(noteKg);
            scale = scale.Add(measurement.ScaleKg);
            totalExcess = totalExcess.Add(excess);
        }

        var signed = scale.Value - theoretical.Value;
        var band = Band(Math.Abs(signed), theoretical.Value, policy.Bands);
        var rate = policy.ExcessApprovalRatePercent;
        var decision = totalExcess.IsZero
            ? WeighingDifferenceDecision.NoExcess
            : rate is null || AtOrAbove(totalExcess.Value, theoretical.Value, rate.Value)
                ? WeighingDifferenceDecision.ApprovalRequired
                : WeighingDifferenceDecision.WithinRate;
        return new WeighingDifferenceEvaluation(theoretical, scale, signed, band, policy.Bands, products.AsReadOnly(),
            totalExcess, rate, decision);
    }

    /// <summary>The excess as a percentage of the note total, for display only; decisions never use this rounded view.</summary>
    public static decimal? ExcessRatioPercent(decimal totalExcessKg, decimal theoreticalKg) =>
        theoreticalKg <= 0m ? null : totalExcessKg * PercentScale / theoreticalKg;

    private static WeighingDifferenceBandLevel Band(decimal absoluteDifference, decimal theoretical,
        WeighingDifferenceBands? bands)
    {
        if (bands is null) return WeighingDifferenceBandLevel.NotEvaluated;
        if (AtOrAbove(absoluteDifference, theoretical, bands.BlockPercent)) return WeighingDifferenceBandLevel.Block;
        if (AtOrAbove(absoluteDifference, theoretical, bands.ApprovalPercent)) return WeighingDifferenceBandLevel.Approval;
        if (AtOrAbove(absoluteDifference, theoretical, bands.WarningPercent)) return WeighingDifferenceBandLevel.Warning;
        return WeighingDifferenceBandLevel.None;
    }

    private static bool AtOrAbove(decimal part, decimal whole, decimal percent) => part * PercentScale >= percent * whole;

    private static void RequireLines(IReadOnlyCollection<WeighingDifferenceNoteLine> lines)
    {
        if (lines.Count == 0 || lines.Count > MaximumLines)
            throw new ArgumentException("İrsaliye satırları boş olamaz ve sınırı aşamaz.", nameof(lines));
        if (lines.Any(line => line is null || line.LineNo < 1 || line.ShipmentId == Guid.Empty
                || line.ProductId == Guid.Empty))
            throw new ArgumentException("İrsaliye satırı eksik.", nameof(lines));
        if (lines.Select(line => line.LineNo).Distinct().Count() != lines.Count)
            throw new ArgumentException("İrsaliye satır numarası tekrar ediyor.", nameof(lines));
        if (lines.Any(line => line.Kg.Unit != QuantityUnit.Kilogram))
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.UnitNotKilogram,
                "Kantar farkı yalnız kilogram satırlarda ölçülür.");
        if (lines.Any(line => line.Kg.IsZero))
            throw new ArgumentException("İrsaliye satırı sıfır kg olamaz.", nameof(lines));
    }

    private static Dictionary<Guid, WeighingDifferenceProductMeasurement> RequireMeasurements(
        IReadOnlyCollection<WeighingDifferenceNoteLine> lines,
        IReadOnlyCollection<WeighingDifferenceProductMeasurement> measurements)
    {
        if (measurements.Any(item => item is null || item.ProductId == Guid.Empty))
            throw new ArgumentException("Kantar ölçümü eksik.", nameof(measurements));
        if (measurements.Any(item => item.ScaleKg.Unit != QuantityUnit.Kilogram
                || item.RemainingPhysicalKg.Unit != QuantityUnit.Kilogram))
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.UnitNotKilogram,
                "Kantar ve stok miktarı kilogram olmalıdır.");
        if (measurements.Select(item => item.ProductId).Distinct().Count() != measurements.Count)
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.MeasurementDuplicate,
                "Aynı ürün için birden fazla kantar ölçümü verildi.");
        var byProduct = measurements.ToDictionary(item => item.ProductId);
        var noteProducts = lines.Select(line => line.ProductId).ToHashSet();
        if (noteProducts.Any(product => !byProduct.ContainsKey(product)))
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.MeasurementMissing,
                "İrsaliyedeki her ürün için kantar ölçümü gerekir.");
        if (byProduct.Keys.Any(product => !noteProducts.Contains(product)))
            throw new WeighingDifferenceRuleException(WeighingDifferenceRuleCodes.MeasurementUnexpected,
                "İrsaliyede olmayan ürün için kantar ölçümü verildi.");
        return byProduct;
    }
}
