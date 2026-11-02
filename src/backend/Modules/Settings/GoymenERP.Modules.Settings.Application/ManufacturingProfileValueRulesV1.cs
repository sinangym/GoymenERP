using System.Globalization;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>One validated parameter value: a number or a choice, with the key's declared unit.</summary>
public sealed record ManufacturingParameterValueDataV1(string Key, string Unit, decimal? Number, string? Choice);

/// <summary>
/// Pure validation of Manufacturing parameter values and tolerance percents (API-01/02). It checks only the declared shape
/// (key known for the process, kind, scale, storage width); it never chooses a value, a default or a threshold. Numbers are
/// invariant decimal text: digits with an optional fraction, no sign, no exponent; a longer fraction is refused, never rounded.
/// </summary>
public static class ManufacturingProfileValueRulesV1
{
    private const decimal PercentMaximum = 100m;

    /// <summary>The engines' process names that may carry a work-center scope (every canonical process).</summary>
    public static bool IsKnownProcess(string? process) => SettingsManufacturingProcessesV1.IsCanonical(process);

    public static bool TryReadValues(string process, IReadOnlyList<ManufacturingParameterValueInputV1>? input,
        out IReadOnlyList<ManufacturingParameterValueDataV1> values, out string detail)
    {
        values = [];
        detail = string.Empty;
        if (!IsKnownProcess(process))
        {
            detail = "Proses tanımsız.";
            return false;
        }

        if (input is null || input.Count == 0)
        {
            detail = "Profil en az bir parametre değeri içermelidir.";
            return false;
        }

        var result = new List<ManufacturingParameterValueDataV1>(input.Count);
        foreach (var item in input)
        {
            if (item is null || ManufacturingParameterKeysV1.Find(item.Key) is not { } definition)
            {
                detail = "Parametre anahtarı tanımsız.";
                return false;
            }

            if (!definition.Processes.Contains(process, StringComparer.Ordinal))
            {
                detail = $"'{definition.Key}' parametresi bu proses için tanımlı değil.";
                return false;
            }

            if (!TryReadValue(definition, item.Value, out var value))
            {
                detail = $"'{definition.Key}' değeri geçersiz ({Describe(definition)}).";
                return false;
            }

            result.Add(value);
        }

        if (result.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count() != result.Count)
        {
            detail = "Bir parametre profilde bir kez yer alır.";
            return false;
        }

        values = result.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
        return true;
    }

    public static bool TryReadValue(ManufacturingParameterDefinitionV1 definition, string? text,
        out ManufacturingParameterValueDataV1 value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        value = null!;
        if (definition.Kind == ManufacturingParameterValueKindV1.Choice)
        {
            if (text is null || !definition.Choices.Contains(text, StringComparer.Ordinal))
            {
                return false;
            }

            value = new ManufacturingParameterValueDataV1(definition.Key, definition.Unit, null, text);
            return true;
        }

        if (!TryParse(text, definition.MaximumScale, ManufacturingProfileInputLimitsV1.ValueMaximumIntegerDigits,
                out var number) || !IsValid(definition, number))
        {
            return false;
        }

        value = new ManufacturingParameterValueDataV1(definition.Key, definition.Unit, number, null);
        return true;
    }

    /// <summary>A stored or catalog value checked against the registry (a mismatch is malformed data, never repaired).</summary>
    public static bool IsWellFormed(string process, ManufacturingParameterValueDataV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ManufacturingParameterKeysV1.Find(value.Key) is not { } definition
            || !definition.Processes.Contains(process, StringComparer.Ordinal)
            || !string.Equals(definition.Unit, value.Unit, StringComparison.Ordinal))
        {
            return false;
        }

        return definition.Kind == ManufacturingParameterValueKindV1.Choice
            ? value.Number is null && value.Choice is { } choice && definition.Choices.Contains(choice, StringComparer.Ordinal)
            : value.Choice is null && value.Number is { } number && Scale(number) <= definition.MaximumScale
              && IsValid(definition, number);
    }

    /// <summary>A tolerance percent: 0…100, at most six decimals.</summary>
    public static bool TryReadPercent(string? text, out decimal percent) =>
        TryParse(text, ManufacturingProfileInputLimitsV1.PercentMaximumScale, 3, out percent) && percent <= PercentMaximum;

    /// <summary>Lossless invariant text of a number (no trailing fraction zeros).</summary>
    public static string Format(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>The text of a value as it is listed (a number, or the choice).</summary>
    public static string Text(ManufacturingParameterValueDataV1 value) =>
        value.Number is { } number ? Format(number) : value.Choice!;

    private static bool IsValid(ManufacturingParameterDefinitionV1 definition, decimal number) => definition.Kind switch
    {
        ManufacturingParameterValueKindV1.Positive => number > 0m,
        ManufacturingParameterValueKindV1.NonNegative => number >= 0m,
        ManufacturingParameterValueKindV1.RatioUpToOne => number > 0m && number <= 1m,
        ManufacturingParameterValueKindV1.WholeCount => number > 0m && decimal.Truncate(number) == number
                                                        && number <= int.MaxValue,
        _ => false,
    };

    private static string Describe(ManufacturingParameterDefinitionV1 definition) => definition.Kind switch
    {
        ManufacturingParameterValueKindV1.Positive => $"sıfırdan büyük, en çok {definition.MaximumScale} ondalık, {definition.Unit}",
        ManufacturingParameterValueKindV1.NonNegative =>
            $"sıfır veya büyük, en çok {definition.MaximumScale} ondalık, {definition.Unit}",
        ManufacturingParameterValueKindV1.RatioUpToOne => "sıfırdan büyük ve en çok 1",
        ManufacturingParameterValueKindV1.WholeCount => "sıfırdan büyük tam sayı",
        _ => "seçeneklerden biri: " + string.Join(", ", definition.Choices),
    };

    private static bool TryParse(string? text, int maximumScale, int maximumIntegerDigits, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integer = point < 0 ? text : text[..point];
        var fraction = point < 0 ? string.Empty : text[(point + 1)..];
        if (integer.Length == 0 || integer.Length > maximumIntegerDigits || !integer.All(char.IsAsciiDigit)
            || (integer.Length > 1 && integer[0] == '0')
            || (point >= 0 && (fraction.Length == 0 || !fraction.All(char.IsAsciiDigit)))
            || fraction.TrimEnd('0').Length > maximumScale)
        {
            return false;
        }

        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static int Scale(decimal value) => Format(value) is var text && text.IndexOf('.', StringComparison.Ordinal) is var point
        && point >= 0 ? text.Length - point - 1 : 0;
}
