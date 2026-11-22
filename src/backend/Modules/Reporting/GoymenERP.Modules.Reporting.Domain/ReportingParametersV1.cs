using System.Globalization;

namespace GoymenERP.Modules.Reporting.Domain;

/// <summary>
/// The parsed, validated parameters of one report run. Every value is parsed by the server from invariant text; an unknown
/// name is 400, a missing required value or an out-of-range value is 422 with the parameter id as the field. Nothing is
/// defaulted: a measure the decisions do not define stays the caller's explicit choice.
/// </summary>
public sealed class ReportingParameterSetV1
{
    private readonly IReadOnlyDictionary<string, object> values;

    private ReportingParameterSetV1(IReadOnlyDictionary<string, object> values, IReadOnlyDictionary<string, string> canonical)
    {
        this.values = values;
        Canonical = canonical;
    }

    /// <summary>The canonical text of every supplied parameter (ordinal key order), as recorded with an export.</summary>
    public IReadOnlyDictionary<string, string> Canonical { get; }

    public DateOnly Date(string id) => (DateOnly)values[id];

    public Guid Guid(string id) => (Guid)values[id];

    public Guid? OptionalGuid(string id) => values.TryGetValue(id, out var value) ? (Guid)value : null;

    public string Choice(string id) => (string)values[id];

    public int WholeNumber(string id) => (int)values[id];

    public decimal Decimal(string id) => (decimal)values[id];

    public IReadOnlyList<int> WholeNumbers(string id) => (IReadOnlyList<int>)values[id];

    public static ReportingParameterSetV1 Parse(ReportingReportDefinitionV1 report, IReadOnlyDictionary<string, string?>? raw) =>
        Parse(report, raw, requireAll: true);

    /// <summary>
    /// A saved view may keep only some parameters (e.g. no dates): every supplied value is still parsed exactly, but a missing
    /// required one is not refused here — the run refuses it.
    /// </summary>
    public static ReportingParameterSetV1 ParsePartial(ReportingReportDefinitionV1 report, IReadOnlyDictionary<string, string?>? raw) =>
        Parse(report, raw, requireAll: false);

    private static ReportingParameterSetV1 Parse(ReportingReportDefinitionV1 report, IReadOnlyDictionary<string, string?>? raw, bool requireAll)
    {
        ArgumentNullException.ThrowIfNull(report);
        raw ??= new Dictionary<string, string?>();
        foreach (var name in raw.Keys)
        {
            if (report.Parameters.All(parameter => !string.Equals(parameter.Id, name, StringComparison.Ordinal)))
                throw new ReportingException(400, ReportingProblemCodesV1.ParameterUnknown, name);
        }

        var parsed = new Dictionary<string, object>(StringComparer.Ordinal);
        var canonical = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in report.Parameters)
        {
            raw.TryGetValue(parameter.Id, out var text);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (parameter.Required && requireAll) throw new ReportingException(422, ReportingProblemCodesV1.ParameterRequired, parameter.Id);
                continue;
            }

            if (text.Length > 200 || text != text.Trim())
                throw new ReportingException(422, ReportingProblemCodesV1.ParameterInvalid, parameter.Id);
            var (value, normal) = ParseOne(parameter, text);
            parsed[parameter.Id] = value;
            canonical[parameter.Id] = normal;
        }

        // A period is bounded so one run's work is bounded (technical, not a business rule).
        if (parsed.TryGetValue("from", out var from) && parsed.TryGetValue("to", out var to))
        {
            var span = ((DateOnly)to).DayNumber - ((DateOnly)from).DayNumber;
            if (span < 0) throw new ReportingException(422, ReportingProblemCodesV1.ParameterInvalid, "to");
            if (span >= ReportingCatalogV1.MaximumPeriodDays) throw new ReportingException(422, ReportingProblemCodesV1.ParameterInvalid, "to");
        }

        return new ReportingParameterSetV1(parsed, new Dictionary<string, string>(canonical, StringComparer.Ordinal));
    }

    private static (object Value, string Canonical) ParseOne(ReportingParameterDefinitionV1 parameter, string text)
    {
        ReportingException Invalid() => new(422, ReportingProblemCodesV1.ParameterInvalid, parameter.Id);
        switch (parameter.Kind)
        {
            case ReportingParameterKindV1.Date:
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw Invalid();
                return (date, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            case ReportingParameterKindV1.Guid:
                if (!System.Guid.TryParseExact(text, "D", out var id) || id == System.Guid.Empty) throw Invalid();
                return (id, id.ToString("D"));
            case ReportingParameterKindV1.Choice:
                if (!parameter.Choices.Contains(text, StringComparer.Ordinal)) throw Invalid();
                return (text, text);
            case ReportingParameterKindV1.WholeNumber:
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var whole)
                    || whole.ToString(CultureInfo.InvariantCulture) != text || !InRange(parameter, whole)) throw Invalid();
                return (whole, text);
            case ReportingParameterKindV1.Decimal:
                if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                    || text.StartsWith('.') || text.EndsWith('.') || !InRange(parameter, number)) throw Invalid();
                return (number, number.ToString("0.############################", CultureInfo.InvariantCulture));
            case ReportingParameterKindV1.AscendingWholeNumbers:
                var parts = text.Split(',');
                if (parts.Length is < 1 or > ReportingCatalogV1.MaximumBucketBoundaries) throw Invalid();
                var list = new List<int>(parts.Length);
                foreach (var part in parts)
                {
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var boundary)
                        || boundary.ToString(CultureInfo.InvariantCulture) != part || !InRange(parameter, boundary)
                        || (list.Count > 0 && boundary <= list[^1])) throw Invalid();
                    list.Add(boundary);
                }

                return (list.AsReadOnly(), string.Join(',', list.Select(item => item.ToString(CultureInfo.InvariantCulture))));
            default:
                throw Invalid();
        }
    }

    private static bool InRange(ReportingParameterDefinitionV1 parameter, decimal value) =>
        (parameter.Minimum is not { } minimum || value >= minimum) && (parameter.Maximum is not { } maximum || value <= maximum);
}
