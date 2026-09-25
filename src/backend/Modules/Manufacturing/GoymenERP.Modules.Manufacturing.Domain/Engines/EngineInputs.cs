namespace GoymenERP.Modules.Manufacturing.Domain.Engines;

/// <summary>
/// Input checks shared by the engines. Every input is nullable: null means "not supplied" and makes
/// the result <see cref="EngineOutcome.Unavailable"/>, naming the parameter. Kilograms and nominal
/// millimetres follow Ö2 (at most three decimals, refused beyond, trailing zeros are not digits).
/// </summary>
internal sealed class EngineInputs
{
    private const int MeasureScale = 3;

    private readonly List<string> _missing = [];

    public string? RefusalCode { get; private set; }

    public string? RefusedParameter { get; private set; }

    public bool IsComplete => _missing.Count == 0 && RefusalCode is null;

    /// <summary>A kilogram or nominal millimetre: positive, at most three decimals.</summary>
    public decimal Measure(decimal? value, string parameter)
    {
        var present = Positive(value, parameter);
        if (value is { } supplied && supplied > 0m && decimal.Round(supplied, MeasureScale) != supplied)
        {
            Refuse(EngineCodes.PrecisionExceeded, parameter);
        }

        return present;
    }

    /// <summary>A positive parameter value (a density, a speed) whose precision is its source's.</summary>
    public decimal Positive(decimal? value, string parameter)
    {
        if (value is not { } supplied)
        {
            _missing.Add(parameter);
            return 0m;
        }

        if (supplied <= 0m)
        {
            Refuse(EngineCodes.ValueNotPositive, parameter);
        }

        return supplied;
    }

    /// <summary>A kilogram that may be a stated zero (e.g. no scrap) but never negative, at most three decimals (Ö2).</summary>
    public decimal NonNegativeMeasure(decimal? value, string parameter)
    {
        var present = NonNegative(value, parameter);
        if (value is { } supplied && supplied >= 0m && decimal.Round(supplied, MeasureScale) != supplied)
        {
            Refuse(EngineCodes.PrecisionExceeded, parameter);
        }

        return present;
    }

    /// <summary>A signed kilogram (a correction can go either way), at most three decimals (Ö2).</summary>
    public decimal SignedMeasure(decimal? value, string parameter)
    {
        if (value is not { } supplied)
        {
            _missing.Add(parameter);
            return 0m;
        }

        if (decimal.Round(supplied, MeasureScale) != supplied)
        {
            Refuse(EngineCodes.PrecisionExceeded, parameter);
        }

        return supplied;
    }

    /// <summary>A value that may be zero (a stated "none", e.g. no tolerance) but never negative.</summary>
    public decimal NonNegative(decimal? value, string parameter)
    {
        if (value is not { } supplied)
        {
            _missing.Add(parameter);
            return 0m;
        }

        if (supplied < 0m)
        {
            Refuse(EngineCodes.ValueNegative, parameter);
        }

        return supplied;
    }

    public int Count(int? value, string parameter)
    {
        if (value is not { } supplied)
        {
            _missing.Add(parameter);
            return 0;
        }

        if (supplied <= 0)
        {
            Refuse(EngineCodes.CountNotPositive, parameter);
        }

        return supplied;
    }

    public void Refuse(string code, string parameter)
    {
        // The first broken rule is the answer; later inputs cannot overwrite it.
        if (RefusalCode is null)
        {
            RefusalCode = code;
            RefusedParameter = parameter;
        }
    }

    /// <summary>The incomplete answer: a refusal wins over missing parameters (a supplied value is wrong).</summary>
    public EngineResult<T> Incomplete<T>()
        where T : class =>
        RefusalCode is not null
            ? EngineResult<T>.Refused(RefusalCode, RefusedParameter!)
            : EngineResult<T>.Unavailable(_missing);
}
