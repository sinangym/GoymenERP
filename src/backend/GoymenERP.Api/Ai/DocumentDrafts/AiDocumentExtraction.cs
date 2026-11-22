using System.Globalization;
using System.Text.RegularExpressions;

namespace GoymenERP.Api.Ai.DocumentDrafts;

/// <summary>
/// A raw value as a tier read it, before any validation. The source is an <see cref="AiDocumentReadingTiersV1"/> code.
/// <paramref name="Canonical"/> marks a number already in the invariant canonical form of a schema (UBL decimal), read
/// without the locale ambiguity rule of free text.
/// </summary>
public sealed record AiExtractedValueV1(string Value, string Source, bool Canonical = false);

/// <summary>One line as read. Nothing here is trusted; <see cref="AiDocumentDraftValidator"/> decides every state.</summary>
public sealed class AiExtractedLineV1
{
    public AiExtractedValueV1? ProductCode { get; set; }

    /// <summary>The code is the counterparty's own item code (UBL SellersItemIdentification): never matched to ours.</summary>
    public bool ProductCodeIsCounterpartyCode { get; set; }

    public AiExtractedValueV1? Description { get; set; }
    public AiExtractedValueV1? Quantity { get; set; }
    public AiExtractedValueV1? Unit { get; set; }
    public AiExtractedValueV1? UnitPrice { get; set; }
    public AiExtractedValueV1? LineAmount { get; set; }
    public AiExtractedValueV1? Currency { get; set; }
    public AiExtractedValueV1? Section { get; set; }
    public AiExtractedValueV1? WallThickness { get; set; }
    public AiExtractedValueV1? Length { get; set; }
}

/// <summary>What the reading tiers produced. Header values may be filled by a later tier only while still empty.</summary>
public sealed class AiExtractedDocumentV1
{
    public AiExtractedValueV1? DocumentNumber { get; set; }
    public AiExtractedValueV1? DocumentDate { get; set; }
    public AiExtractedValueV1? Currency { get; set; }
    public AiExtractedValueV1? CounterpartyTaxId { get; set; }
    public AiExtractedValueV1? CounterpartyName { get; set; }
    public AiExtractedValueV1? PayableAmount { get; set; }
    public List<AiExtractedLineV1> Lines { get; } = [];

    /// <summary>Header field codes still empty after the deterministic tiers (the only ones tier 4 may suggest).</summary>
    public IReadOnlyList<string> OpenHeaderFields()
    {
        var open = new List<string>();
        if (DocumentNumber is null) open.Add(AiDraftFieldCodesV1.DocumentNumber);
        if (DocumentDate is null) open.Add(AiDraftFieldCodesV1.DocumentDate);
        if (Currency is null) open.Add(AiDraftFieldCodesV1.Currency);
        if (CounterpartyTaxId is null) open.Add(AiDraftFieldCodesV1.CounterpartyTaxId);
        if (CounterpartyName is null) open.Add(AiDraftFieldCodesV1.CounterpartyName);
        if (PayableAmount is null) open.Add(AiDraftFieldCodesV1.PayableAmount);
        return open;
    }

    /// <summary>Fills one header field only while it is still empty; an earlier, deterministic tier always wins.</summary>
    public bool TryFillHeader(string fieldCode, AiExtractedValueV1 value)
    {
        switch (fieldCode)
        {
            case AiDraftFieldCodesV1.DocumentNumber when DocumentNumber is null: DocumentNumber = value; return true;
            case AiDraftFieldCodesV1.DocumentDate when DocumentDate is null: DocumentDate = value; return true;
            case AiDraftFieldCodesV1.Currency when Currency is null: Currency = value; return true;
            case AiDraftFieldCodesV1.CounterpartyTaxId when CounterpartyTaxId is null: CounterpartyTaxId = value; return true;
            case AiDraftFieldCodesV1.CounterpartyName when CounterpartyName is null: CounterpartyName = value; return true;
            case AiDraftFieldCodesV1.PayableAmount when PayableAmount is null: PayableAmount = value; return true;
            default: return false;
        }
    }

    public bool IsEmpty => Lines.Count == 0 && OpenHeaderFields().Count == 6;
}

/// <summary>
/// The deterministic parser for text: locally extracted PDF text, OCR output, an e-mail body or a pasted text. It
/// reads labelled header values and line rows by fixed patterns; it never infers, never totals and never follows an
/// instruction in the text. A line row is "code description quantity unit price [currency]". A row that does not
/// match is not guessed at: it stays out of the draft.
/// </summary>
public static partial class AiDocumentTextParser
{
    [GeneratedRegex(@"(?:sipari[şs]|fatura|teyit|onay|order|invoice|confirmation)\s*(?:no|numaras[ıi]|number|#)\s*[:.]?\s*(?<v>[A-Z0-9][A-Z0-9/_-]{1,40})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DocumentNumberPattern();

    [GeneratedRegex(@"(?:tarih|tarihi|date)\s*[:.]?\s*(?<v>\d{4}-\d{2}-\d{2}|\d{2}[./]\d{2}[./]\d{4})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(?:vkn|tckn|v\.k\.n\.?|vergi\s*(?:kimlik\s*)?no)\s*[:.]?\s*(?<v>\d{10,11})(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TaxIdPattern();

    [GeneratedRegex(@"(?:para\s*birimi|d[öo]viz|currency)\s*[:.]?\s*(?<v>[A-Za-z]{3})(?![A-Za-z])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex(@"^\s*(?:m[üu][şs]teri|tedarik[çc]i|sat[ıi]c[ıi]|al[ıi]c[ıi]|firma|unvan|customer|supplier|vendor)\s*[:]\s*(?<v>[^\r\n]{2,120}?)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CounterpartyNamePattern();

    [GeneratedRegex(@"(?:genel\s*toplam|[öo]denecek\s*tutar|toplam\s*tutar|payable\s*amount|grand\s*total)\s*[:.]?\s*(?<v>\d[\d.,]*)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PayablePattern();

    [GeneratedRegex(@"^\s*(?<code>[A-Z0-9][A-Z0-9._/-]{1,40})\s+(?<desc>\S.*?)\s+(?<qty>\d[\d.,]*)\s+(?<unit>[A-Za-zÇĞİÖŞÜçğıöşü]{1,6}\d?)\s+(?<price>\d[\d.,]*)(?:\s+(?<cur>[A-Z]{3}))?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex LinePattern();

    public static AiExtractedDocumentV1 Parse(string text, string source, int maximumLines)
    {
        ArgumentNullException.ThrowIfNull(text);
        var document = new AiExtractedDocumentV1();
        document.DocumentNumber = First(DocumentNumberPattern(), text, source);
        document.DocumentDate = First(DatePattern(), text, source);
        document.CounterpartyTaxId = First(TaxIdPattern(), text, source);
        document.Currency = First(CurrencyPattern(), text, source) is { } currency
            ? currency with { Value = currency.Value.ToUpperInvariant() }
            : null;
        document.CounterpartyName = First(CounterpartyNamePattern(), text, source);
        document.PayableAmount = First(PayablePattern(), text, source);

        foreach (Match match in LinePattern().Matches(text))
        {
            if (document.Lines.Count >= maximumLines) break;
            var description = match.Groups["desc"].Value.Trim();
            document.Lines.Add(new AiExtractedLineV1
            {
                ProductCode = new(match.Groups["code"].Value, source),
                Description = new(description, source),
                Quantity = new(match.Groups["qty"].Value, source),
                Unit = new(match.Groups["unit"].Value, source),
                UnitPrice = new(match.Groups["price"].Value, source),
                Currency = match.Groups["cur"].Success ? new(match.Groups["cur"].Value, source) : null,
            });
        }

        return document;
    }

    private static AiExtractedValueV1? First(Regex pattern, string text, string source) =>
        pattern.Match(text) is { Success: true } match && match.Groups["v"].Value.Trim() is { Length: > 0 } value
            ? new AiExtractedValueV1(value, source)
            : null;

    /// <summary>
    /// Reads a document number: digits with at most ONE separator, '.' or ','. "12,5" and "12.5" are 12.5. A value
    /// whose only separator is followed by exactly three digits ("1.250") could be a thousands group or a decimal and
    /// is ambiguous; a value with two different separators or repeated separators is a thousands-grouped number and
    /// also ambiguous. Ambiguity is never resolved by guessing a locale.
    /// </summary>
    public static AiNumberReadV1 ReadNumber(AiExtractedValueV1? extracted)
    {
        if (extracted is { Canonical: true })
        {
            return decimal.TryParse(extracted.Value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var canonical)
                ? new(AiNumberReadStateV1.Read, canonical)
                : new(AiNumberReadStateV1.Invalid, null);
        }

        var raw = extracted?.Value;
        if (string.IsNullOrWhiteSpace(raw)) return new(AiNumberReadStateV1.Missing, null);
        var value = raw.Trim();
        if (value.Any(character => !char.IsAsciiDigit(character) && character is not '.' and not ','))
            return new(AiNumberReadStateV1.Invalid, null);
        var separators = value.Count(character => character is '.' or ',');
        if (separators > 1) return new(AiNumberReadStateV1.Ambiguous, null);
        if (separators == 1)
        {
            var index = value.IndexOfAny(['.', ',']);
            var fraction = value[(index + 1)..];
            if (index == 0 || fraction.Length == 0) return new(AiNumberReadStateV1.Invalid, null);
            if (fraction.Length == 3) return new(AiNumberReadStateV1.Ambiguous, null);
            value = value.Replace(',', '.');
        }

        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? new(AiNumberReadStateV1.Read, number)
            : new(AiNumberReadStateV1.Invalid, null);
    }
}

public enum AiNumberReadStateV1 { Missing = 1, Read = 2, Ambiguous = 3, Invalid = 4 }

public sealed record AiNumberReadV1(AiNumberReadStateV1 State, decimal? Value);
