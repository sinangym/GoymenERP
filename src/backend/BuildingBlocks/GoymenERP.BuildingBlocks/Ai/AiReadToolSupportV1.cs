using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>
/// One argument a READ tool accepts (WP-A3c). Either an integer range with a default, or a closed set of exact strings
/// (ordinal) with no default (absent means "all"). Nothing else is accepted: no company, actor, free text or nesting.
/// </summary>
public sealed record AiReadToolArgumentV1
{
    private AiReadToolArgumentV1(string name, int? minimum, int? maximum, int? defaultValue, IReadOnlyList<string>? values)
    {
        Name = AiContractGuard.Text(name);
        Minimum = minimum;
        Maximum = maximum;
        Default = defaultValue;
        Values = values;
    }

    public string Name { get; }
    public int? Minimum { get; }
    public int? Maximum { get; }
    public int? Default { get; }
    public IReadOnlyList<string>? Values { get; }

    public static AiReadToolArgumentV1 Integer(string name, int minimum, int maximum, int defaultValue)
    {
        if (minimum > maximum || defaultValue < minimum || defaultValue > maximum)
        {
            throw new ArgumentException("Invalid integer argument range.", nameof(defaultValue));
        }

        return new(name, minimum, maximum, defaultValue, null);
    }

    public static AiReadToolArgumentV1 OneOf(string name, params string[] values)
    {
        if (values.Length == 0 || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException("A closed argument needs distinct values.", nameof(values));
        }

        return new(name, null, null, null, Array.AsReadOnly(values.Select(AiContractGuard.Text).ToArray()));
    }
}

/// <summary>The strictly read arguments: integers (defaults applied) and the chosen closed values (absent = null).</summary>
public sealed class AiReadToolArgumentsV1
{
    private readonly IReadOnlyDictionary<string, int> integers;
    private readonly IReadOnlyDictionary<string, string?> choices;

    private AiReadToolArgumentsV1(IReadOnlyDictionary<string, int> integers, IReadOnlyDictionary<string, string?> choices)
    {
        this.integers = integers;
        this.choices = choices;
    }

    public int Integer(string name) => integers[name];

    public string? Choice(string name) => choices[name];

    /// <summary>
    /// Reads an untrusted JSON object against <paramref name="specification"/>. An unknown or repeated property, a wrong
    /// type, an out-of-range integer or a value outside the closed set refuses the whole input, naming the field (or
    /// <c>arguments</c> when the field is not one the tool knows or the input is not an object).
    /// </summary>
    public static bool TryRead(string? json, IReadOnlyList<AiReadToolArgumentV1> specification,
        out AiReadToolArgumentsV1 arguments, out string invalidField)
    {
        ArgumentNullException.ThrowIfNull(specification);
        const string Whole = "arguments";
        var integers = specification.Where(item => item.Values is null).ToDictionary(item => item.Name, item => item.Default!.Value, StringComparer.Ordinal);
        var choices = specification.Where(item => item.Values is not null).ToDictionary(item => item.Name, _ => (string?)null, StringComparer.Ordinal);
        arguments = new(integers, choices);
        invalidField = Whole;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var item = specification.FirstOrDefault(candidate => string.Equals(candidate.Name, property.Name, StringComparison.Ordinal));
                if (item is null)
                {
                    invalidField = Whole;
                    return false;
                }

                invalidField = item.Name;
                if (!seen.Add(item.Name))
                {
                    return false;
                }

                if (item.Values is null)
                {
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var value)
                        || value < item.Minimum || value > item.Maximum)
                    {
                        return false;
                    }

                    integers[item.Name] = value;
                }
                else
                {
                    if (property.Value.ValueKind != JsonValueKind.String
                        || property.Value.GetString() is not { } text || !item.Values.Contains(text, StringComparer.Ordinal))
                    {
                        return false;
                    }

                    choices[item.Name] = text;
                }
            }

            invalidField = Whole;
            arguments = new(integers, choices);
            return true;
        }
    }
}

/// <summary>Small deterministic helpers shared by owner READ tools.</summary>
public static class AiReadToolSupportV1
{
    /// <summary>SAT-D10 / P7: the business day is the Europe/Istanbul calendar day. Without the zone nothing is guessed.</summary>
    public const string BusinessTimeZoneId = "Europe/Istanbul";

    /// <summary>The Europe/Istanbul calendar day of <paramref name="instant"/>, or null when the zone is not available.</summary>
    public static DateOnly? BusinessDay(DateTimeOffset instant)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>A stable source version: SHA-256 over the given ordered lines.</summary>
    public static string Digest(IEnumerable<string> lines) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
}
