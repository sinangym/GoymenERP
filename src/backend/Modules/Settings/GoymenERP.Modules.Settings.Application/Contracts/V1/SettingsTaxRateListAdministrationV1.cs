namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// Technical bounds of a rate list input (storage and transport shape, not tax policy). The percent bounds and scale are the
/// tax master's (<see cref="TaxMasterInputLimitsV1.PercentMaximumScale"/>); the name bound is the code lists'.
/// </summary>
public static class TaxRateListInputLimitsV1
{
    /// <summary>An entry code: upper-case ASCII letters, digits and '/' (e.g. "20", "5/10").</summary>
    public const int CodeMaximumLength = 16;

    /// <summary>Entries one rate list version may carry.</summary>
    public const int MaximumEntries = 100;
}

/// <summary>
/// SAT-D181 (EK26): one entry of a rate list version. <c>Code</c>: upper-case ASCII letters, digits and '/' (1…16, unique in the
/// version, e.g. "20" or "5/10"); <c>Name</c>: the Turkish name (1…200); <c>Percent</c>: lossless decimal text (at most 4
/// decimals, never rounded) — a VAT rate 0…100, a withholding ratio above 0…100 (the withheld share of the VAT, e.g. "50" for
/// 5/10); <c>TaxCode</c>: only on a withholding ratio, the SAT-D148 GİB withholding code it is invoiced under (it must be in
/// the withholding code list in force on the version's date; omitted = not mapped yet, the invoice waits).
/// </summary>
public sealed record TaxRateListEntryV1(string? Code, string? Name, string? Percent, string? TaxCode = null);

/// <summary>
/// SAT-D181: publish a whole rate list version (it replaces the previous one from <c>EffectiveFrom</c> on; the same date rules
/// as a code list: omitted = today, a date before today is refused, a version on the date of a not-yet-effective one replaces
/// it). <c>ExpectedVersion</c> is the list's version ("0" while the company has none).
/// </summary>
public sealed record PublishTaxRateListRequestV1(
    string? ExpectedVersion,
    IReadOnlyList<TaxRateListEntryV1>? Entries,
    string? Reason,
    DateOnly? EffectiveFrom = null);

/// <summary>The VAT rate and withholding ratio lists of the server-resolved company on <c>BusinessDate</c>.</summary>
public sealed record TaxRateListsViewV1(
    Guid CompanyId,
    DateOnly BusinessDate,
    IReadOnlyList<TaxRateListViewV1> Lists);

/// <summary>
/// A rate list (one of <see cref="TaxRateListKindsV1"/>): its own version (the edit fence, "0" while none is entered), the
/// version in force today (null = "kaynak yok") and the scheduled ones.
/// </summary>
public sealed record TaxRateListViewV1(
    string Kind,
    string Version,
    TaxRateListVersionViewV1? InForce,
    IReadOnlyList<TaxRateListVersionViewV1> Scheduled);

public sealed record TaxRateListVersionViewV1(
    string ListVersion,
    DateOnly EffectiveFrom,
    IReadOnlyList<TaxRateListEntryWireViewV1> Entries,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc,
    string Reason);

/// <summary>One stored rate list entry; the percent as lossless decimal text.</summary>
public sealed record TaxRateListEntryWireViewV1(string Code, string Name, string Percent, string? TaxCode);
