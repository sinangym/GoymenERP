using System.Text.RegularExpressions;

namespace GoymenERP.BuildingBlocks.Ai;

/// <summary>What the outbound free-text screen found. A kind only; the matched value is never kept or reported.</summary>
public enum AiOutboundTextFindingV1
{
    Iban = 1,
    EmailAddress = 2,
    DigitSequence = 3,
    CurrencyAmount = 4,
}

public sealed record AiOutboundTextScreenResultV1(IReadOnlyList<AiOutboundTextFindingV1> Findings)
{
    public bool IsClean => Findings.Count == 0;
}

/// <summary>
/// Screens a user's free-text question before it may go to an external model (SINAN_KARARLARI_2026-11-03_AI AI-D4
/// implementation note: for a general question only the user's text goes out; when a sensitive value is detected the
/// call is blocked and the user is reminded). The screen is best effort and deliberately stricter than the field
/// policy: free text has no reviewed field tag, so any detection refuses the external call instead of pseudonymizing
/// it. Personal contact data (e-mail, phone) never leaves (AI-D4). A clean result is not a permission; the caller still
/// applies <see cref="AiEgressFilterV1"/> with the Settings policy.
/// </summary>
public static partial class AiOutboundTextScreenV1
{
    // Pattern shapes (not business thresholds): an IBAN is a country code, two check digits and 11-30 alphanumerics
    // (ISO 13616); a run of at least seven digits covers phone, account and national id numbers in free text.
    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}(?:[ ]?[A-Z0-9]){11,30}\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex IbanPattern();

    [GeneratedRegex(@"[^\s@]+@[^\s@]+\.[^\s@]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?:[0-9][\s\-.()/]*){7,}", RegexOptions.CultureInvariant)]
    private static partial Regex DigitSequencePattern();

    [GeneratedRegex(
        @"(?:[₺$€£]\s?[0-9])|(?:[0-9][0-9.,]*\s?(?:₺|\$|€|£|(?<![A-Za-z])(?:TL|TRY|USD|EUR|GBP)(?![A-Za-z])))",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyAmountPattern();

    public static AiOutboundTextScreenResultV1 Screen(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var findings = new List<AiOutboundTextFindingV1>();
        if (IbanPattern().IsMatch(text)) findings.Add(AiOutboundTextFindingV1.Iban);
        if (EmailPattern().IsMatch(text)) findings.Add(AiOutboundTextFindingV1.EmailAddress);
        if (DigitSequencePattern().IsMatch(text)) findings.Add(AiOutboundTextFindingV1.DigitSequence);
        if (CurrencyAmountPattern().IsMatch(text)) findings.Add(AiOutboundTextFindingV1.CurrencyAmount);
        return new AiOutboundTextScreenResultV1(findings.AsReadOnly());
    }
}

/// <summary>
/// AI-D44: the data class of a free panel question's own text. Internal by default; Confidential when the text carries
/// personal data (an IBAN, an e-mail address, or a digit run such as a phone or national identity number). A source with a
/// higher class still wins in the gateway (the effective class is the highest of the text and every source).
/// </summary>
public static class AiQuestionDataClassV1
{
    public static AiDataClassV1 Classify(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var findings = AiOutboundTextScreenV1.Screen(question).Findings;
        return findings.Any(finding => finding is AiOutboundTextFindingV1.Iban or AiOutboundTextFindingV1.EmailAddress
            or AiOutboundTextFindingV1.DigitSequence)
            ? AiDataClassV1.Confidential
            : AiDataClassV1.Internal;
    }
}
