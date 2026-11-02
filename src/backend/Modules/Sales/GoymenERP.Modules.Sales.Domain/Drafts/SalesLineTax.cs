using System.Text.RegularExpressions;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D140 (EK19) / SAT-D147 (EK20) / SAT-D158 (EK21): the buyer's tax situation chosen PER ORDER (never read from the Cari card)
/// — a code of the Settings buyer situation list in force on the order's business date — with the exemption code the order chose
/// for a situation whose effect is "KDV %0 with an exemption code" and the buyer's document (no, date) a situation may require.
/// Whether the code, the exemption code and the document are allowed is decided at command time against the Settings lists in
/// force (only listed values; nothing is hard-coded here); this record holds only their shape. A new order starts "Normal"
/// (SAT-D140 implementation detail, reversible). A missing document never blocks the order: the invoice waits (SAT-D147).
/// </summary>
public sealed partial record SalesBuyerTax
{
    /// <summary>The Settings buyer situation code bound (<c>TaxMasterInputLimitsV1.BuyerSituationCodeMaximumLength</c>).</summary>
    public const int MaximumSituationLength = 64;

    /// <summary>The Settings code list code bound (<c>TaxMasterInputLimitsV1.ListCodeMaximumLength</c>).</summary>
    public const int MaximumCodeLength = 16;

    /// <summary>A technical bound of the buyer's document number (an identifier the customer presents, not a policy).</summary>
    public const int MaximumDocumentNoLength = 64;

    private SalesBuyerTax(string situation, string? exemptionCode, string? documentNo, DateOnly? documentDate)
    {
        Situation = situation;
        ExemptionCode = exemptionCode;
        DocumentNo = documentNo;
        DocumentDate = documentDate;
    }

    /// <summary>The buyer situation code (e.g. <c>Normal</c>, <c>ExportRegistered</c>).</summary>
    public string Situation { get; }

    /// <summary>SAT-D147: the exemption code the order chose from the exemption list; null = none chosen ("kaynak yok").</summary>
    public string? ExemptionCode { get; }

    /// <summary>SAT-D147: the buyer's document number (istisna belgesi / ihraç kayıtlı belge); null = none given.</summary>
    public string? DocumentNo { get; }

    /// <summary>SAT-D147: the buyer's document date; given exactly with <see cref="DocumentNo"/>.</summary>
    public DateOnly? DocumentDate { get; }

    /// <summary>
    /// The shape rules: a situation code of ASCII letters/digits starting with a letter (≤ 64), an exemption code of ASCII letters
    /// / digits (≤ 16, upper-cased as the Settings list stores it), a document number trimmed (1…64) given together with its date.
    /// </summary>
    public static SalesBuyerTax Create(string? situation, string? exemptionCode = null, string? documentNo = null,
        DateOnly? documentDate = null)
    {
        if (situation is null || situation.Length is 0 or > MaximumSituationLength || !SituationPattern().IsMatch(situation))
        {
            throw new SalesRuleException(SalesRuleCodes.TaxBuyerSituationInvalid, "Alıcı vergi durumu kodu geçersiz.");
        }

        string? code = null;
        if (exemptionCode is not null)
        {
            code = exemptionCode.Trim().ToUpperInvariant();
            if (code.Length is 0 or > MaximumCodeLength || !CodePattern().IsMatch(code))
            {
                throw new SalesRuleException(SalesRuleCodes.TaxExemptionCodeInvalid, "İstisna kodu geçersiz.");
            }
        }

        var number = string.IsNullOrWhiteSpace(documentNo) ? null : documentNo.Trim();
        if (number is { Length: > MaximumDocumentNoLength } || (number is null) != (documentDate is null))
        {
            throw new SalesRuleException(SalesRuleCodes.TaxBuyerDocumentIncomplete,
                "Alıcı belgesi numarası ve tarihi birlikte verilir (numara en fazla 64 karakter).");
        }

        return new SalesBuyerTax(situation, code, number, documentDate);
    }

    /// <summary>Whether the buyer's document (no, date) is on the order.</summary>
    public bool HasDocument => DocumentNo is not null && DocumentDate is not null;

    [GeneratedRegex("\\A[A-Za-z][A-Za-z0-9]*\\z")]
    private static partial Regex SituationPattern();

    [GeneratedRegex("\\A[A-Z0-9]+\\z")]
    private static partial Regex CodePattern();
}

/// <summary>Where a line's tax snapshot came from.</summary>
public static class SalesLineTaxOrigins
{
    /// <summary>The Settings default for the product's item type and the ORDER's buyer situation (SAT-D139/D140).</summary>
    public const string Default = "Default";

    /// <summary>SAT-D93/D97: the salesperson changed the line's tax basis with a reason (audited; approved with the order).</summary>
    public const string Override = "Override";
}

/// <summary>
/// SAT-D93 / SAT-D97: a line's changed tax basis — chosen ONLY from Settings lists: an item type default (demir / hurda / hizmet,
/// SAT-D139; null = the product's own item type), a buyer situation of the list in force (SAT-D158) and, for a situation whose
/// effect is "KDV %0 with an exemption code", the exemption code from the exemption list (SAT-D147). A free-typed rate or code is
/// never accepted. The reason is required (SAT-D97, audit); who changed it and when are the command's.
/// </summary>
public sealed record SalesLineTaxOverride(
    string? ItemType, string BuyerSituation, string? ExemptionCode, string Reason, string ChangedBy, DateTimeOffset ChangedAtUtc)
{
    /// <summary>The reason's technical bound (the Settings tax master's, SAT-D68 precedent).</summary>
    public const int MaximumReasonLength = 4000;

    /// <summary>Whether <paramref name="other"/> names the same basis and reason (who/when aside).</summary>
    public bool SameChoice(SalesLineTaxOverride? other) =>
        other is not null && ItemType == other.ItemType && BuyerSituation == other.BuyerSituation
        && ExemptionCode == other.ExemptionCode && Reason == other.Reason;
}

/// <summary>The resolution states the Settings line tax port answers (<c>TaxResolutionStatesV1</c>), kept as the snapshot's text.</summary>
public static class SalesLineTaxStates
{
    public const string Resolved = "Resolved";
    public const string Exempt = "Exempt";
    public const string ZeroRated = "ZeroRated";
    public const string NotApplied = "NotApplied";
    public const string NotRequired = "NotRequired";
    public const string Unresolved = "Unresolved";
}

/// <summary>Sales' own reasons why a line's tax cannot be used (beside the Settings port's published reasons, kept verbatim).</summary>
public static class SalesLineTaxReasons
{
    /// <summary>The line carries no tax snapshot (written before the tax wiring, or no Settings tax port was composed).</summary>
    public const string SnapshotMissing = "tax_snapshot_missing";

    /// <summary>The snapshot is resolved but a part needed for the rate is not (fail-closed; never a guessed part).</summary>
    public const string RateIncomplete = "tax_rate_incomplete";

    /// <summary>The Settings port answered "unresolved" without a reason.</summary>
    public const string Unresolved = "tax_unresolved";

    /// <summary>SAT-D147: the buyer situation requires the buyer's document and the order carries none (the invoice waits).</summary>
    public const string BuyerDocumentMissing = "buyer_document_missing";
}

/// <summary>
/// MOD-03 → Sales (SAT-D73…D76, D82, D93, D133, D139, D140, D147, D158): one line's tax AS RESOLVED by the Settings line tax port
/// (<c>ISettingsLineTaxResolverV1</c>) for the order's business date — kept with the line so a later Settings change never
/// rewrites it (SAT-D82), re-resolved only at the line's create, a commercial revision or a change of the order's buyer situation.
/// Every part is the port's answer verbatim (states and reasons as its published text, rates as exact decimals): VAT
/// <c>Resolved</c> with a percent, <c>Exempt</c> ("KDV yok") or <c>ZeroRated</c> (SAT-D147 KDV %0), withholding <c>Resolved</c> with
/// the withheld share of the VAT in percent or <c>NotApplied</c>; the two code parts (exemption, withholding) <c>NotRequired</c>,
/// <c>Resolved</c> or <c>Unresolved</c> (the invoice waits, the rate stays usable). <see cref="State"/> <c>Unresolved</c> (with
/// <see cref="Reason"/>) means no default answered — then every other part is empty and the line's tax is "kaynak yok", never 0.
/// <see cref="Override"/> is set when the salesperson changed the basis with a reason (SAT-D93/D97).
/// </summary>
public sealed record SalesLineTaxSnapshot(
    Guid ProductId,
    DateOnly ResolvedOn,
    string BuyerSituation,
    string? ItemType,
    string State,
    string? Reason,
    string? Source,
    Guid? RecordId,
    long? RecordVersion,
    string? VatState,
    decimal? VatPercent,
    string? ExemptionCodeState,
    string? ExemptionCode,
    string? ExemptionCodeReason,
    string? WithholdingState,
    decimal? WithholdingPercent,
    string? WithholdingCodeState,
    string? WithholdingCode,
    string? WithholdingCodeReason,
    string? BuyerAdjustment,
    long? BuyerSituationListVersion,
    string? BuyerVatMode,
    bool BuyerDocumentRequired,
    SalesLineTaxOverride? Override = null)
{
    /// <summary><c>Default</c> or <c>Override</c>.</summary>
    public string Origin => Override is null ? SalesLineTaxOrigins.Default : SalesLineTaxOrigins.Override;

    /// <summary>
    /// The rate the ONE totals calculator applies (<see cref="SalesOrderTotalsCalculator"/>), or null when the snapshot cannot give
    /// one (see <see cref="UnavailableReason"/>): VAT <c>Resolved</c> → its percent, <c>Exempt</c> / <c>ZeroRated</c> → 0; withholding
    /// <c>Resolved</c> → its percent / 100 as the withheld share, <c>NotApplied</c> → none. Anything else is no rate — never a guess.
    /// </summary>
    public SalesLineTaxRate? Rate
    {
        get
        {
            if (State != SalesLineTaxStates.Resolved)
            {
                return null;
            }

            decimal vat;
            switch (VatState)
            {
                case SalesLineTaxStates.Resolved when VatPercent is { } percent and >= 0m and <= 100m:
                    vat = percent;
                    break;
                case SalesLineTaxStates.Exempt or SalesLineTaxStates.ZeroRated:
                    vat = 0m;
                    break;
                default:
                    return null;
            }

            decimal? share;
            switch (WithholdingState)
            {
                case SalesLineTaxStates.Resolved when WithholdingPercent is { } withheld and > 0m and <= 100m:
                    share = withheld / 100m;
                    break;
                case SalesLineTaxStates.NotApplied:
                    share = null;
                    break;
                default:
                    return null;
            }

            return SalesLineTaxRate.Of(vat, share);
        }
    }

    /// <summary>Why <see cref="Rate"/> is null (the port's reason verbatim, or Sales' own); null when a rate is given.</summary>
    public string? UnavailableReason => Rate is not null
        ? null
        : State == SalesLineTaxStates.Resolved ? SalesLineTaxReasons.RateIncomplete : Reason ?? SalesLineTaxReasons.Unresolved;

    /// <summary>
    /// What makes the line's invoice wait although its rate may be usable (SAT-D135/D147; the invoice gate itself is API-32's): no
    /// rate, an unresolved exemption or withholding code, or a buyer document the situation requires and <paramref name="buyer"/>
    /// (the order's buyer tax) does not carry. Each is a stable reason code.
    /// </summary>
    public IReadOnlyList<string> InvoiceBlockers(SalesBuyerTax? buyer)
    {
        var blockers = new List<string>();
        if (UnavailableReason is { } unavailable)
        {
            blockers.Add(unavailable);
        }

        if (ExemptionCodeState == SalesLineTaxStates.Unresolved)
        {
            blockers.Add(ExemptionCodeReason ?? SalesLineTaxReasons.Unresolved);
        }

        if (WithholdingCodeState == SalesLineTaxStates.Unresolved)
        {
            blockers.Add(WithholdingCodeReason ?? SalesLineTaxReasons.Unresolved);
        }

        // The document is the ORDER's (SAT-D147: "belge bilgisi siparişte istenir"); a line overridden to such a situation on an
        // order of another situation still needs it on the order.
        if (BuyerDocumentRequired && buyer?.HasDocument != true)
        {
            blockers.Add(SalesLineTaxReasons.BuyerDocumentMissing);
        }

        return [.. blockers.Distinct(StringComparer.Ordinal)];
    }
}
