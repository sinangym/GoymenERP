using System.Text.RegularExpressions;
using GoymenERP.Api.Ai.Knowledge;

namespace GoymenERP.Api.Ai.Evaluation;

/// <summary>
/// WP-A2 person-name screen for free question text, independent of the decision index (WP-A4 keeps no name list in the
/// packaged index). AI-D44 / AI-D49: content holding a person name stays on a company-internal model. Three signals, each
/// only raising the class:
/// <list type="number">
/// <item>Known names supplied by the host (for example display names read from Identity's company-scoped
/// <c>IUserDirectoryReadPortV1</c>); matched whole-word, case-folded.</item>
/// <item>A common Turkish given name followed by a capitalized word (given name + surname).</item>
/// <item>A capitalized word followed by an honorific (<c>Bey</c>, <c>Hanım</c>) or preceded by <c>Sayın</c>.</item>
/// </list>
/// A bare pair of capitalized words is not a signal (titles such as "Satış Müdürü" would match). It is a screen, not a
/// guarantee: a name outside these shapes is not seen; a false hit only keeps an item local (fail-closed).
/// </summary>
public sealed partial class AiEvalPersonNameScreenV1
{
    public const string SignalKnownName = "kisi_adi_bilinen";
    public const string SignalNamePattern = "kisi_adi_deseni";

    /// <summary>Common Turkish given names (public, generic; not people of this company).</summary>
    public static readonly IReadOnlyList<string> CommonGivenNames =
    [
        "Ahmet", "Mehmet", "Mustafa", "Ali", "Hüseyin", "Hasan", "İbrahim", "İsmail", "Osman", "Yusuf", "Murat", "Ömer",
        "Ramazan", "Halil", "Süleyman", "Abdullah", "Mahmut", "Recep", "Salih", "Fatih", "Kadir", "Emre", "Hakan", "Adem",
        "Kemal", "Yaşar", "Bekir", "Musa", "Metin", "Orhan", "Serkan", "Burak", "Sinan", "Erkan", "Cem", "Volkan", "Gökhan",
        "Tolga", "Onur", "Selim", "Cengiz", "Levent", "Tuncay", "Kerem", "Emir", "Eren", "Barış", "Uğur", "Oğuz", "Serdar",
        "Fatma", "Ayşe", "Emine", "Hatice", "Zeynep", "Elif", "Meryem", "Şerife", "Zehra", "Sultan", "Hanife", "Merve",
        "Havva", "Özlem", "Esra", "Yasemin", "Leyla", "Hülya", "Derya", "Gül", "Sevgi", "Ebru", "Aslı", "Seda", "Gizem",
        "Büşra", "Kübra", "Tuğba", "Burcu", "Pınar", "Nur", "Melek", "Selin", "Ece", "Dilek", "Sibel", "Esin", "Nazlı",
    ];

    private readonly HashSet<string> givenNames;
    private readonly string[] knownNames;

    public AiEvalPersonNameScreenV1(IEnumerable<string>? knownPersonNames = null)
    {
        givenNames = CommonGivenNames.Select(AiDecisionIndex.Fold).ToHashSet(StringComparer.Ordinal);
        knownNames = [.. (knownPersonNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => AiDecisionIndex.Fold(name.Trim()))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(name => name.Length)];
    }

    public IReadOnlyList<string> Screen(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var signals = new List<string>();
        var folded = AiDecisionIndex.Fold(text);
        if (knownNames.Any(name => Regex.IsMatch(folded,
                $"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(name)}(?![\\p{{L}}\\p{{N}}])", RegexOptions.CultureInvariant)))
            signals.Add(SignalKnownName);

        var patterned = GivenNameSurname().Matches(text).Any(match => givenNames.Contains(AiDecisionIndex.Fold(match.Groups["given"].Value)))
                        || Honorific().IsMatch(text);
        if (patterned) signals.Add(SignalNamePattern);
        return signals.AsReadOnly();
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?<given>\p{Lu}\p{Ll}+)(?=\s+(?:\p{Lu}\p{Ll}+|\p{Lu}{2,})(?![\p{L}\p{N}]))", RegexOptions.CultureInvariant)]
    private static partial Regex GivenNameSurname();

    [GeneratedRegex(@"(?:(?<![\p{L}\p{N}])\p{Lu}\p{Ll}+\s+(?:Bey|Hanım|Hanim)(?![\p{L}\p{N}]))|(?:(?<![\p{L}\p{N}])Sayın\s+\p{Lu}\p{Ll}+)", RegexOptions.CultureInvariant)]
    private static partial Regex Honorific();
}
