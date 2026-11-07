using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// One value of a captured parameter profile, exactly as Settings published it: the engine input name (the Settings key
/// IS the engine's parameter name, e.g. <c>densityKgPerCubicMetre</c>), its unit and either a number or a choice.
/// Üretim U2-a (URT-D9): <see cref="Source"/> is null for the profile's own value and <see cref="PlanSource"/> for a value
/// entered on the plan (the Slitting / Tube–Profile line speed), which replaced the profile's value at approval.
/// </summary>
public sealed record PlanParameterValueV1
{
    public const int MaximumKeyLength = 64;
    public const int MaximumUnitLength = 16;
    public const int MaximumChoiceLength = 32;

    /// <summary>The value was entered on the plan and wins over the profile for that plan (URT-D9).</summary>
    public const string PlanSource = "Plan";

    public const int MaximumSourceLength = 16;

    private static readonly Regex KeyGrammar = new("^[A-Za-z0-9]{1,64}$", RegexOptions.CultureInvariant);

    public PlanParameterValueV1(string key, string unit, decimal? number, string? choice, string? source = null)
    {
        if (source is not null && (source != PlanSource || number is null))
        {
            throw new ArgumentException("Parametre değerinin kaynağı yalnız planda girilen bir sayı olabilir.", nameof(source));
        }

        Source = source;

        if (string.IsNullOrEmpty(key) || !KeyGrammar.IsMatch(key))
        {
            throw new ArgumentException("Parametre anahtarı geçerli değil.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(unit) || unit.Length > MaximumUnitLength
            || !string.Equals(unit, unit.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Parametre birimi geçerli değil.", nameof(unit));
        }

        if ((number is null) == (choice is null)
            || choice is not null && (choice.Length == 0 || choice.Length > MaximumChoiceLength
                || !string.Equals(choice, choice.Trim(), StringComparison.Ordinal)))
        {
            throw new ArgumentException("Parametre değeri ya sayı ya seçim olmalıdır.", nameof(number));
        }

        Key = key;
        Unit = unit;
        Number = number;
        Choice = choice;
    }

    public string Key { get; }
    public string Unit { get; }
    public decimal? Number { get; }
    public string? Choice { get; }

    /// <summary>Null: the profile's value. <see cref="PlanSource"/>: entered on the plan.</summary>
    public string? Source { get; }
}

/// <summary>
/// DEC-PRDCT-008 / URT-D9 (Üretim U1-b): the Settings parameter profile a plan was approved with — the exact profile id,
/// version text, source (<c>Catalog</c> version 0 or a published <c>Settings</c> version), the line scope it came from
/// (null = the process profile), its window, the date it was selected for and every value. Captured once at approval
/// and never recalculated: a newer profile published later changes nothing of an approved plan; every later calculation
/// of the plan reads these values.
/// </summary>
public sealed record PlanParameterSnapshotV1
{
    public const int MaximumVersionLength = 32;
    public const int MaximumSourceLength = 16;
    public const string CatalogSource = "Catalog";
    public const string SettingsSource = "Settings";

    public PlanParameterSnapshotV1(
        Guid profileId,
        string profileVersion,
        string source,
        ManufacturingProcess process,
        Guid? workCenterId,
        DateOnly effectiveFrom,
        DateOnly? effectiveThrough,
        DateOnly selectedForDate,
        IEnumerable<PlanParameterValueV1> values)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Parametre profili kimliği boş olamaz.", nameof(profileId));
        }

        if (string.IsNullOrEmpty(profileVersion) || profileVersion.Length > MaximumVersionLength
            || !profileVersion.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Parametre profili sürümü geçerli değil.", nameof(profileVersion));
        }

        if (source is not (CatalogSource or SettingsSource)
            || source == CatalogSource && profileVersion != "0"
            || source == SettingsSource && profileVersion.TrimStart('0').Length == 0)
        {
            throw new ArgumentException("Parametre profili kaynağı sürümüyle uyuşmuyor.", nameof(source));
        }

        if (!Enum.IsDefined(process))
        {
            throw new ArgumentOutOfRangeException(nameof(process), process, "Tanımsız üretim süreci.");
        }

        if (workCenterId == Guid.Empty)
        {
            throw new ArgumentException("İş merkezi kimliği boş olamaz.", nameof(workCenterId));
        }

        if (effectiveThrough is { } through && through < effectiveFrom
            || selectedForDate < effectiveFrom || effectiveThrough is { } end && selectedForDate > end)
        {
            throw new ArgumentException("Parametre profili seçilen tarihte geçerli değil.", nameof(selectedForDate));
        }

        ArgumentNullException.ThrowIfNull(values);
        var list = values.ToArray();
        if (list.Length == 0 || list.Any(value => value is null)
            || list.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count() != list.Length)
        {
            throw new ArgumentException("Parametre profili en az bir ve her anahtarı bir kez taşımalıdır.", nameof(values));
        }

        ProfileId = profileId;
        ProfileVersion = profileVersion;
        Source = source;
        Process = process;
        WorkCenterId = workCenterId;
        EffectiveFrom = effectiveFrom;
        EffectiveThrough = effectiveThrough;
        SelectedForDate = selectedForDate;
        Values = list.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray();
    }

    public Guid ProfileId { get; }
    public string ProfileVersion { get; }
    public string Source { get; }
    public ManufacturingProcess Process { get; }

    /// <summary>The line scope of the selected profile; null when the process profile (or the catalog) answered.</summary>
    public Guid? WorkCenterId { get; }

    public DateOnly EffectiveFrom { get; }
    public DateOnly? EffectiveThrough { get; }
    public DateOnly SelectedForDate { get; }
    public IReadOnlyList<PlanParameterValueV1> Values { get; }

    /// <summary>
    /// Üretim U2-a (URT-D9): the same snapshot with <paramref name="key"/> taken from the plan — the plan's value replaces
    /// the profile's (or is added when the profile has none) and carries <see cref="PlanParameterValueV1.PlanSource"/>.
    /// U2-b1: a replaced value keeps the profile's unit for that key, so the snapshot never mixes units for one key;
    /// <paramref name="unit"/> applies only to a key the profile does not carry.
    /// </summary>
    public PlanParameterSnapshotV1 WithPlanValue(string key, string unit, decimal number)
    {
        var replaced = Values.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.Ordinal));
        return new(ProfileId, ProfileVersion, Source, Process, WorkCenterId, EffectiveFrom, EffectiveThrough,
            SelectedForDate,
            Values.Where(value => !string.Equals(value.Key, key, StringComparison.Ordinal))
                .Append(new PlanParameterValueV1(key, replaced?.Unit ?? unit, number, choice: null,
                    PlanParameterValueV1.PlanSource)));
    }

    /// <summary>The numeric values by engine parameter name (choices are not engine numbers and are left out).</summary>
    public IReadOnlyDictionary<string, decimal> Numbers() =>
        Values.Where(value => value.Number is not null)
            .ToDictionary(value => value.Key, value => value.Number!.Value, StringComparer.Ordinal);

    public bool Equals(PlanParameterSnapshotV1? other) =>
        other is not null && ProfileId == other.ProfileId && ProfileVersion == other.ProfileVersion
        && Source == other.Source && Process == other.Process && WorkCenterId == other.WorkCenterId
        && EffectiveFrom == other.EffectiveFrom && EffectiveThrough == other.EffectiveThrough
        && SelectedForDate == other.SelectedForDate && Values.SequenceEqual(other.Values);

    public override int GetHashCode() =>
        Values.Aggregate(HashCode.Combine(ProfileId, ProfileVersion, Source, Process, WorkCenterId, EffectiveFrom,
            EffectiveThrough, SelectedForDate), (hash, value) => HashCode.Combine(hash, value));
}
