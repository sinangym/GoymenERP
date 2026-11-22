using System.Globalization;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Ai;

namespace GoymenERP.Api.Ai.DocumentDrafts;

/// <summary>The owner-port answers the validator needs; collected by the service before validation.</summary>
public sealed record AiDraftMatchesV1(
    AiCounterpartyMatchV1? Counterparty,
    IReadOnlyDictionary<int, AiProductMatchV1> ProductsByLine);

/// <summary>
/// Deterministic field validation of a document draft (B.3): section, wall thickness and length; unit; currency; tax
/// identity checksum and counterparty match; product match; amount reconciliation. Every check is a pure rule on the
/// read value — no tolerance is invented (a line amount that does not equal quantity × price EXACTLY is "doğrulanmadı",
/// because the rounding rule belongs to the owner's calculator), and no locale is guessed. A value that the quarantine
/// screen flags is never verified. A value suggested by the local model is never verified on its own.
/// </summary>
public static partial class AiDocumentDraftValidator
{
    /// <summary>
    /// ASSUMPTION-AI-DRAFT-UNIT-1: recognised unit spellings (UN/ECE Rec. 20 codes used by UBL-TR and common Turkish
    /// abbreviations) mapped to one canonical token, so "C62", "ADET" and "AD" compare equal. Recognition is not
    /// acceptance: a matched product's own unit list still decides.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Units = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["C62"] = "adet", ["NIU"] = "adet", ["H87"] = "adet", ["ADET"] = "adet", ["AD"] = "adet", ["ADT"] = "adet",
        ["KGM"] = "kg", ["KG"] = "kg",
        ["TNE"] = "ton", ["TON"] = "ton",
        ["MTR"] = "m", ["M"] = "m", ["MT"] = "m", ["METRE"] = "m",
        ["MMT"] = "mm", ["MM"] = "mm",
        ["MTK"] = "m2", ["M2"] = "m2",
        ["LTR"] = "lt", ["LT"] = "lt",
        ["PK"] = "paket", ["PA"] = "paket", ["PAKET"] = "paket",
        ["BOY"] = "boy",
    };

    /// <summary>Length units and their factor to millimetres (exact SI ratios, not business values).</summary>
    private static readonly IReadOnlyDictionary<string, decimal> LengthUnitsToMillimetres = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
    {
        ["mm"] = 1m, ["cm"] = 10m, ["m"] = 1000m, ["mt"] = 1000m, ["metre"] = 1000m,
    };

    // A profile written as WxHxT (box/rectangle) or ØDxT (round), optionally followed by a length and its unit.
    [GeneratedRegex(@"(?:(?:Ø|O|D)\s*(?<d>\d+(?:[.,]\d+)?)|(?<w>\d+(?:[.,]\d+)?)\s*[xX×*]\s*(?<h>\d+(?:[.,]\d+)?))\s*[xX×*]\s*(?<t>\d+(?:[.,]\d+)?)(?:\s*mm\b)?(?:\s+(?<l>\d+(?:[.,]\d+)?)\s*(?<lu>mm|cm|metre|mt|m)\b)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProfilePattern();

    [GeneratedRegex(@"\A\s*(?:(?:Ø|O|D)\s*(?<d>\d+(?:[.,]\d+)?)|(?<w>\d+(?:[.,]\d+)?)\s*[xX×*]\s*(?<h>\d+(?:[.,]\d+)?))\s*(?:mm)?\s*\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex SectionPattern();

    [GeneratedRegex(@"\A\s*(?<v>\d+(?:[.,]\d+)?)\s*(?<u>[A-Za-z]+)?\s*\z", RegexOptions.CultureInvariant)]
    private static partial Regex MeasurePattern();

    public static AiDocumentDraftResponseV1 Build(
        string draftKind,
        string readingTier,
        AiDraftSourceV1 source,
        IReadOnlyList<AiQuarantineFindingV1> documentFindings,
        AiExtractedDocumentV1 document,
        AiDraftMatchesV1 matches,
        AiDraftOpenInV1 openIn)
    {
        var headerCurrency = CurrencyField(document.Currency, null);
        var lines = document.Lines.Select((line, index) =>
            Line(index + 1, line, headerCurrency, matches.ProductsByLine.TryGetValue(index + 1, out var match) ? match : null)).ToArray();

        return new AiDocumentDraftResponseV1(
            draftKind,
            readingTier,
            source,
            [.. documentFindings.Select(FindingCode)],
            TextField(AiDraftFieldCodesV1.DocumentNumber, document.DocumentNumber),
            DateField(document.DocumentDate),
            headerCurrency,
            Counterparty(document, matches.Counterparty),
            lines,
            AmountField(AiDraftFieldCodesV1.PayableAmount, document.PayableAmount, allowZero: true),
            openIn,
            CreatesRecord: false);
    }

    public static string FindingCode(AiQuarantineFindingV1 finding) => finding switch
    {
        AiQuarantineFindingV1.InstructionLikeText => "instruction_like_text",
        AiQuarantineFindingV1.ToolCallLikeSyntax => "tool_call_like_syntax",
        AiQuarantineFindingV1.RoleOrPromptMarker => "role_or_prompt_marker",
        AiQuarantineFindingV1.InvisibleCharacters => "invisible_characters",
        AiQuarantineFindingV1.AuthorityOrRecipientChange => "authority_or_recipient_change",
        _ => "unknown",
    };

    private static AiDraftLineV1 Line(int number, AiExtractedLineV1 line, AiDraftFieldV1 headerCurrency, AiProductMatchV1? match)
    {
        var productCode = ProductField(line, match);
        var quantity = AmountField(AiDraftFieldCodesV1.Quantity, line.Quantity, allowZero: false);
        var unitPrice = AmountField(AiDraftFieldCodesV1.UnitPrice, line.UnitPrice, allowZero: true);
        var lineAmount = LineAmountField(line, quantity, unitPrice);
        // A line's own currency must be the document's: the price and amount are in it, and nothing converts.
        var lineCurrency = line.Currency is null ? null : CurrencyField(line.Currency, headerCurrency.Value);
        if (lineCurrency is { Status: AiDraftFieldStatusesV1.Invalid })
        {
            if (unitPrice.Value is not null) unitPrice = unitPrice with { Status = AiDraftFieldStatusesV1.Invalid, ReasonCode = lineCurrency.ReasonCode };
            if (lineAmount.Value is not null) lineAmount = lineAmount with { Status = AiDraftFieldStatusesV1.Invalid, ReasonCode = lineCurrency.ReasonCode };
        }

        var (section, wall, length) = Dimensions(line);
        var matched = match is { Outcome: AiDraftMatchOutcomeV1.Matched } && !line.ProductCodeIsCounterpartyCode;
        return new AiDraftLineV1(
            number,
            productCode,
            TextField(AiDraftFieldCodesV1.Description, line.Description, required: false),
            quantity,
            UnitField(line.Unit, matched ? match : null),
            unitPrice,
            lineAmount,
            section,
            wall,
            length,
            matched ? match!.ProductId : null,
            matched ? match!.ProductVersion : null);
    }

    private static AiDraftFieldV1 ProductField(AiExtractedLineV1 line, AiProductMatchV1? match)
    {
        if (line.ProductCode is not { } code) return Missing(AiDraftFieldCodesV1.ProductCode);
        if (Quarantined(code)) return Unverified(AiDraftFieldCodesV1.ProductCode, code, AiDraftReasonCodesV1.QuarantinedInstructionText);
        if (line.ProductCodeIsCounterpartyCode) return Unverified(AiDraftFieldCodesV1.ProductCode, code, AiDraftReasonCodesV1.ProductNotMatched);
        if (code.Source == AiDocumentReadingTiersV1.LocalModel) return Unverified(AiDraftFieldCodesV1.ProductCode, code, AiDraftReasonCodesV1.ModelSuggested);
        return match?.Outcome switch
        {
            AiDraftMatchOutcomeV1.Matched => Verified(AiDraftFieldCodesV1.ProductCode, code),
            AiDraftMatchOutcomeV1.NotFound => Unverified(AiDraftFieldCodesV1.ProductCode, code, AiDraftReasonCodesV1.ProductNotMatched),
            AiDraftMatchOutcomeV1.Ambiguous => Unverified(AiDraftFieldCodesV1.ProductCode, code, AiDraftReasonCodesV1.ProductMatchAmbiguous),
            _ => Unverified(AiDraftFieldCodesV1.ProductCode, code, AiDraftReasonCodesV1.ProductMatchUnavailable),
        };
    }

    private static AiDraftCounterpartyV1 Counterparty(AiExtractedDocumentV1 document, AiCounterpartyMatchV1? match)
    {
        var name = TextField(AiDraftFieldCodesV1.CounterpartyName, document.CounterpartyName);
        if (document.CounterpartyTaxId is not { } taxId) return new(Missing(AiDraftFieldCodesV1.CounterpartyTaxId), name, null);
        const string code = AiDraftFieldCodesV1.CounterpartyTaxId;
        if (Quarantined(taxId)) return new(Unverified(code, taxId, AiDraftReasonCodesV1.QuarantinedInstructionText), name, null);
        var check = TaxIdentity.Check(taxId.Value.Trim());
        if (check != null) return new(Invalid(code, taxId, check), name, null);
        if (taxId.Source == AiDocumentReadingTiersV1.LocalModel) return new(Unverified(code, taxId, AiDraftReasonCodesV1.ModelSuggested), name, null);
        return match?.Outcome switch
        {
            AiDraftMatchOutcomeV1.Matched when match.PartyId is { } partyId => new(Verified(code, taxId), name, partyId),
            AiDraftMatchOutcomeV1.NotFound or AiDraftMatchOutcomeV1.Ambiguous =>
                new(Unverified(code, taxId, AiDraftReasonCodesV1.CounterpartyNotMatched), name, null),
            _ => new(Unverified(code, taxId, AiDraftReasonCodesV1.CounterpartyMatchUnavailable), name, null),
        };
    }

    private static AiDraftFieldV1 TextField(string code, AiExtractedValueV1? value, bool required = true)
    {
        if (value is null) return required ? Missing(code) : Absent(code);
        if (Quarantined(value)) return Unverified(code, value, AiDraftReasonCodesV1.QuarantinedInstructionText);
        if (value.Source == AiDocumentReadingTiersV1.LocalModel) return Unverified(code, value, AiDraftReasonCodesV1.ModelSuggested);
        // A free-text value is read, not proven: only a structured field is verified as written.
        return value.Source == AiDocumentReadingTiersV1.StructuredFile
            ? Verified(code, value)
            : Unverified(code, value, null);
    }

    private static AiDraftFieldV1 DateField(AiExtractedValueV1? value)
    {
        const string code = AiDraftFieldCodesV1.DocumentDate;
        if (value is null) return Missing(code);
        if (Quarantined(value)) return Unverified(code, value, AiDraftReasonCodesV1.QuarantinedInstructionText);
        var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy" };
        if (!DateOnly.TryParseExact(value.Value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Invalid(code, value, AiDraftReasonCodesV1.DateInvalid);
        var normalized = value with { Value = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        return value.Source == AiDocumentReadingTiersV1.LocalModel
            ? Unverified(code, normalized, AiDraftReasonCodesV1.ModelSuggested)
            : Verified(code, normalized);
    }

    private static AiDraftFieldV1 CurrencyField(AiExtractedValueV1? value, string? headerCurrency)
    {
        const string code = AiDraftFieldCodesV1.Currency;
        if (value is null) return Missing(code);
        var normalized = value with { Value = value.Value.Trim().ToUpperInvariant() };
        if (normalized.Value.Length != 3 || !normalized.Value.All(character => character is >= 'A' and <= 'Z'))
            return Invalid(code, value, AiDraftReasonCodesV1.CurrencyInvalid);
        if (headerCurrency is not null && !string.Equals(headerCurrency, normalized.Value, StringComparison.Ordinal))
            return Invalid(code, normalized, AiDraftReasonCodesV1.CurrencyMismatch);
        return value.Source == AiDocumentReadingTiersV1.LocalModel
            ? Unverified(code, normalized, AiDraftReasonCodesV1.ModelSuggested)
            : Verified(code, normalized);
    }

    private static AiDraftFieldV1 AmountField(string code, AiExtractedValueV1? value, bool allowZero)
    {
        if (value is null) return Missing(code);
        var read = AiDocumentTextParser.ReadNumber(value);
        return read.State switch
        {
            AiNumberReadStateV1.Ambiguous => Unverified(code, value, AiDraftReasonCodesV1.NumberFormatAmbiguous),
            AiNumberReadStateV1.Read when read.Value < 0 => Invalid(code, value, AiDraftReasonCodesV1.PriceNegative),
            AiNumberReadStateV1.Read when read.Value == 0 && !allowZero => Invalid(code, value, AiDraftReasonCodesV1.QuantityNotPositive),
            AiNumberReadStateV1.Read when value.Source == AiDocumentReadingTiersV1.LocalModel =>
                Unverified(code, value with { Value = Canonical(read.Value!.Value) }, AiDraftReasonCodesV1.ModelSuggested),
            AiNumberReadStateV1.Read => Verified(code, value with { Value = Canonical(read.Value!.Value) }),
            _ => Invalid(code, value, AiDraftReasonCodesV1.NumberInvalid),
        };
    }

    private static AiDraftFieldV1 LineAmountField(AiExtractedLineV1 line, AiDraftFieldV1 quantity, AiDraftFieldV1 unitPrice)
    {
        var amount = AmountField(AiDraftFieldCodesV1.LineAmount, line.LineAmount, allowZero: true);
        if (amount.Status != AiDraftFieldStatusesV1.Verified
            || quantity.Status != AiDraftFieldStatusesV1.Verified || unitPrice.Status != AiDraftFieldStatusesV1.Verified)
        {
            return amount;
        }

        var product = Parse(quantity.Value!) * Parse(unitPrice.Value!);
        return product == Parse(amount.Value!)
            ? amount
            : amount with { Status = AiDraftFieldStatusesV1.Unverified, ReasonCode = AiDraftReasonCodesV1.LineAmountNotReconciled };
    }

    private static AiDraftFieldV1 UnitField(AiExtractedValueV1? value, AiProductMatchV1? match)
    {
        const string code = AiDraftFieldCodesV1.Unit;
        if (value is null) return Missing(code);
        if (!Units.TryGetValue(value.Value.Trim(), out var canonical)) return Invalid(code, value, AiDraftReasonCodesV1.UnitUnknown);
        if (match is not null)
        {
            var offered = match.UnitCodes.Select(unit => Units.TryGetValue(unit.Trim(), out var known) ? known : unit.Trim());
            if (!offered.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                return Unverified(code, value, AiDraftReasonCodesV1.UnitNotOfferedForProduct);
        }

        return value.Source == AiDocumentReadingTiersV1.LocalModel
            ? Unverified(code, value, AiDraftReasonCodesV1.ModelSuggested)
            : Verified(code, value);
    }

    /// <summary>
    /// Section (kesit), wall thickness (et kalınlığı) and length (boy) in millimetres. The description is parsed for a
    /// profile "WxHxT" / "ØDxT" with an optional length; explicit columns, when present, must agree with it exactly.
    /// Rules: every dimension is positive, twice the wall is less than the smallest side (or the diameter), and a
    /// length carries a known unit. A description without a profile leaves the three fields absent, not invalid.
    /// </summary>
    private static (AiDraftFieldV1 Section, AiDraftFieldV1 Wall, AiDraftFieldV1 Length) Dimensions(AiExtractedLineV1 line)
    {
        var parsed = line.Description is { } description ? ProfilePattern().Match(description.Value) : Match.Empty;
        var source = line.Description?.Source ?? line.Section?.Source ?? AiDocumentReadingTiersV1.StructuredFile;

        decimal? sideA = null, sideB = null, diameter = null, wall = null, lengthMm = null;
        string? lengthReason = null;
        if (parsed.Success)
        {
            if (parsed.Groups["d"].Success) diameter = Parse(parsed.Groups["d"].Value);
            else { sideA = Parse(parsed.Groups["w"].Value); sideB = Parse(parsed.Groups["h"].Value); }
            wall = Parse(parsed.Groups["t"].Value);
            if (parsed.Groups["l"].Success) lengthMm = Parse(parsed.Groups["l"].Value) * LengthUnitsToMillimetres[parsed.Groups["lu"].Value];
        }

        // Explicit columns.
        string? sectionReason = null, wallReason = null;
        if (line.Section is { } sectionValue)
        {
            var match = SectionPattern().Match(sectionValue.Value);
            if (!match.Success) sectionReason = AiDraftReasonCodesV1.SectionFormatInvalid;
            else
            {
                decimal? a = null, b = null, d = null;
                if (match.Groups["d"].Success) d = Parse(match.Groups["d"].Value);
                else { a = Parse(match.Groups["w"].Value); b = Parse(match.Groups["h"].Value); }
                if (parsed.Success && (a != sideA || b != sideB || d != diameter)) sectionReason = AiDraftReasonCodesV1.DimensionConflict;
                sideA ??= a; sideB ??= b; diameter ??= d;
            }
        }

        if (line.WallThickness is { } wallValue)
        {
            var (measure, reason) = Measure(wallValue.Value);
            if (reason is not null) wallReason = reason;
            else if (wall is not null && wall != measure) wallReason = AiDraftReasonCodesV1.DimensionConflict;
            else wall = measure;
        }

        if (line.Length is { } lengthValue)
        {
            var (measure, reason) = Measure(lengthValue.Value);
            if (reason is not null) lengthReason = reason;
            else if (lengthMm is not null && lengthMm != measure) lengthReason = AiDraftReasonCodesV1.DimensionConflict;
            else lengthMm = measure;
        }

        var hasSection = sideA is not null || diameter is not null;
        var sectionText = diameter is { } dd ? $"Ø{Canonical(dd)}" : hasSection ? $"{Canonical(sideA!.Value)}x{Canonical(sideB!.Value)}" : null;

        // Geometry rules.
        if (sectionReason is null && hasSection && (sideA <= 0 || sideB <= 0 || diameter <= 0))
            sectionReason = AiDraftReasonCodesV1.DimensionNotPositive;
        if (wallReason is null && wall is { } t)
        {
            if (t <= 0) wallReason = AiDraftReasonCodesV1.DimensionNotPositive;
            else if (hasSection && 2 * t >= (diameter ?? Math.Min(sideA!.Value, sideB!.Value)))
                wallReason = AiDraftReasonCodesV1.WallThicknessExceedsSection;
        }

        if (lengthReason is null && lengthMm is <= 0) lengthReason = AiDraftReasonCodesV1.DimensionNotPositive;

        return (
            DimensionField(AiDraftFieldCodesV1.Section, sectionText, sectionReason, source, line.Section ?? line.Description, hasSection),
            DimensionField(AiDraftFieldCodesV1.WallThickness, wall is { } w ? Canonical(w) : null, wallReason, source, line.WallThickness ?? line.Description, wall is not null || line.WallThickness is not null),
            DimensionField(AiDraftFieldCodesV1.Length, lengthMm is { } l ? Canonical(l) : null, lengthReason, source, line.Length ?? line.Description, lengthMm is not null || line.Length is not null));
    }

    private static AiDraftFieldV1 DimensionField(string code, string? value, string? reason, string source, AiExtractedValueV1? origin, bool present)
    {
        if (!present && reason is null) return Absent(code);
        var shown = value ?? origin?.Value;
        if (origin is not null && Quarantined(origin)) return new(code, shown, AiDraftFieldStatusesV1.Unverified, AiDraftReasonCodesV1.QuarantinedInstructionText, source);
        if (reason is not null) return new(code, shown, AiDraftFieldStatusesV1.Invalid, reason, source);
        return source == AiDocumentReadingTiersV1.LocalModel
            ? new(code, shown, AiDraftFieldStatusesV1.Unverified, AiDraftReasonCodesV1.ModelSuggested, source)
            : new(code, shown, AiDraftFieldStatusesV1.Verified, null, source);
    }

    /// <summary>A measure in millimetres; a bare number is millimetres (the trade's drawing unit).</summary>
    private static (decimal? Millimetres, string? Reason) Measure(string raw)
    {
        var match = MeasurePattern().Match(raw);
        if (!match.Success) return (null, AiDraftReasonCodesV1.DimensionFormatInvalid);
        var unit = match.Groups["u"].Success ? match.Groups["u"].Value : "mm";
        return LengthUnitsToMillimetres.TryGetValue(unit, out var factor)
            ? (Parse(match.Groups["v"].Value) * factor, null)
            : (null, AiDraftReasonCodesV1.LengthUnitUnknown);
    }

    private static decimal Parse(string value) => decimal.Parse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static string Canonical(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static bool Quarantined(AiExtractedValueV1 value) => AiExternalContentQuarantineV1.Screen(value.Value).Count != 0;

    private static AiDraftFieldV1 Verified(string code, AiExtractedValueV1 value) => new(code, value.Value, AiDraftFieldStatusesV1.Verified, null, value.Source);

    private static AiDraftFieldV1 Unverified(string code, AiExtractedValueV1 value, string? reason) =>
        new(code, value.Value, AiDraftFieldStatusesV1.Unverified, reason, value.Source);

    private static AiDraftFieldV1 Invalid(string code, AiExtractedValueV1 value, string reason) =>
        new(code, value.Value, AiDraftFieldStatusesV1.Invalid, reason, value.Source);

    private static AiDraftFieldV1 Missing(string code) =>
        new(code, null, AiDraftFieldStatusesV1.Unverified, AiDraftReasonCodesV1.Missing, AiDocumentReadingTiersV1.StructuredFile);

    /// <summary>An optional field the document simply does not carry (a profile size on a non-profile line).</summary>
    private static AiDraftFieldV1 Absent(string code) =>
        new(code, null, AiDraftFieldStatusesV1.Unverified, null, AiDocumentReadingTiersV1.StructuredFile);

    /// <summary>Turkish tax identity checks: VKN (10 digits) and TCKN (11 digits) check-digit algorithms.</summary>
    public static class TaxIdentity
    {
        public const int VknLength = 10;
        public const int TcknLength = 11;

        /// <summary>Null when the identity passes; otherwise a reason code.</summary>
        public static string? Check(string value)
        {
            if (!value.All(char.IsAsciiDigit) || value.Length is not (VknLength or TcknLength))
                return AiDraftReasonCodesV1.TaxIdFormatInvalid;
            var digits = value.Select(character => character - '0').ToArray();
            var valid = value.Length == VknLength ? IsValidVkn(digits) : IsValidTckn(digits);
            return valid ? null : AiDraftReasonCodesV1.TaxIdChecksumInvalid;
        }

        public static bool IsValidVkn(IReadOnlyList<int> d)
        {
            var sum = 0;
            for (var i = 0; i < VknLength - 1; i++)
            {
                var tmp = (d[i] + VknLength - (i + 1)) % 10;
                sum += tmp == 9 ? 9 : tmp * (1 << (VknLength - (i + 1))) % 9;
            }

            return (10 - sum % 10) % 10 == d[VknLength - 1];
        }

        public static bool IsValidTckn(IReadOnlyList<int> d)
        {
            if (d[0] == 0) return false;
            var odd = d[0] + d[2] + d[4] + d[6] + d[8];
            var even = d[1] + d[3] + d[5] + d[7];
            var tenth = ((odd * 7 - even) % 10 + 10) % 10;
            return tenth == d[9] && (d.Take(10).Sum() % 10) == d[10];
        }
    }
}
