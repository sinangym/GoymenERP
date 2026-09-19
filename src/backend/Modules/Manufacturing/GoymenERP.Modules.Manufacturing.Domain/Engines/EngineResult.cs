namespace GoymenERP.Modules.Manufacturing.Domain.Engines;

public enum EngineOutcome
{
    /// <summary>Every input was present and valid; <see cref="EngineResult{T}.Value"/> holds the result.</summary>
    Computed = 1,

    /// <summary>
    /// A parameter the formula needs is missing ("veri eksik"): nothing is computed and no default is
    /// assumed. <see cref="EngineResult{T}.MissingParameters"/> names every missing one.
    /// </summary>
    Unavailable = 2,

    /// <summary>An input breaks a formula's rule or invariant; <see cref="EngineResult{T}.RefusalCode"/> says which.</summary>
    Refused = 3,
}

/// <summary>
/// The answer of a production engine (MFG-6). An engine never guesses: a missing parameter is
/// <see cref="EngineOutcome.Unavailable"/>, never a silent 0 or a built-in default, and a broken rule is
/// <see cref="EngineOutcome.Refused"/>, never a clamped value. A computed value is unrounded: the
/// rounding policy (master v3 §28.1) is not decided, so the engine rounds nothing.
/// </summary>
public sealed record EngineResult<T>
    where T : class
{
    private EngineResult(EngineOutcome outcome, T? value, IReadOnlyList<string> missing, string? refusalCode,
        string? refusedParameter)
    {
        Outcome = outcome;
        Value = value;
        MissingParameters = missing;
        RefusalCode = refusalCode;
        RefusedParameter = refusedParameter;
    }

    public EngineOutcome Outcome { get; }

    public T? Value { get; }

    public IReadOnlyList<string> MissingParameters { get; }

    public string? RefusalCode { get; }

    public string? RefusedParameter { get; }

    public static EngineResult<T> Computed(T value) =>
        new(EngineOutcome.Computed, value ?? throw new ArgumentNullException(nameof(value)), [], null, null);

    public static EngineResult<T> Unavailable(IEnumerable<string> missingParameters)
    {
        var missing = missingParameters.ToArray();
        if (missing.Length == 0)
        {
            throw new ArgumentException("An unavailable result names at least one missing parameter.", nameof(missingParameters));
        }

        return new(EngineOutcome.Unavailable, null, missing, null, null);
    }

    public static EngineResult<T> Refused(string code, string parameter) =>
        new(EngineOutcome.Refused, null, [], code, parameter);
}

/// <summary>The stable refusal codes of the production engines.</summary>
public static class EngineCodes
{
    /// <summary>A measure, mass, density or speed that must be positive is zero or negative.</summary>
    public const string ValueNotPositive = "engine_value_not_positive";

    /// <summary>A value that may be zero (a tolerance) is negative.</summary>
    public const string ValueNegative = "engine_value_negative";

    /// <summary>A kilogram or nominal millimetre input carries more than three decimals (Ö2): refused, never rounded.</summary>
    public const string PrecisionExceeded = "engine_precision_exceeded";

    /// <summary>A count (bands, coils) is not a positive whole number.</summary>
    public const string CountNotPositive = "engine_count_not_positive";

    /// <summary>SLT-004 ≤ net usable width is broken: the slit lines use more width than the coil has (master v3 §28.4).</summary>
    public const string UsedWidthExceedsNetWidth = "engine_used_width_exceeds_net_width";

    /// <summary>SLT-009: the bands asked for do not fit in the raw width.</summary>
    public const string BandsExceedRawWidth = "engine_bands_exceed_raw_width";

    /// <summary>The coil's inner diameter is not smaller than its maximum outer diameter.</summary>
    public const string InnerDiameterNotBelowMaximum = "engine_inner_diameter_not_below_maximum";

    /// <summary>Master v3 §28.8: the target thickness must be below the input thickness (cold rolling only thins).</summary>
    public const string TargetThicknessNotBelowInput = "engine_target_thickness_not_below_input";

    /// <summary>Master v3 §28.8: the scrap cannot equal or exceed the input mass.</summary>
    public const string ScrapNotBelowInputMass = "engine_scrap_not_below_input_mass";
}
