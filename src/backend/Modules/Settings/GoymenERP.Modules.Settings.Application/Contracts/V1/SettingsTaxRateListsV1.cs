namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>
/// SAT-D181 (EK26): the two Settings lists a Sales order line's reasoned tax change may pick from — the legal VAT rates and the
/// legal withholding ratios. Company-scoped (SAT-D133) and effective-dated like the code lists. Their values are entered by Sinan
/// or the müşavir (SAT-D148), maintained under the SAT-D146 proposal / approval flow (a later slice); none is hard-coded.
/// </summary>
public static class TaxRateListKindsV1
{
    /// <summary>KDV oranı listesi: each entry's <c>Percent</c> is a VAT rate (0…100).</summary>
    public const string VatRate = "VatRate";

    /// <summary>Tevkifat oranı listesi: each entry's <c>Percent</c> is the withheld share of the VAT in percent (&gt; 0…100, e.g. 50 for 5/10).</summary>
    public const string WithholdingRatio = "WithholdingRatio";

    public static IReadOnlyList<string> All { get; } = [VatRate, WithholdingRatio];

    public static bool IsKnown(string? kind) => kind is VatRate or WithholdingRatio;
}

/// <summary>
/// One entry of a rate list: its code (unique in the list version, e.g. "20" or "5/10"), its name and its exact percent. SAT-D181
/// (EK26): a withholding ratio entry names the GİB withholding code it is invoiced under (<paramref name="TaxCode"/>, a code of
/// the SAT-D148 withholding code list; null = not mapped yet → the code part is "kaynak yok" and the invoice waits); a VAT rate
/// entry carries none.
/// </summary>
public sealed record TaxRateListEntryViewV1(string Code, string Name, decimal Percent, string? TaxCode = null);

/// <summary>One immutable rate list version (the whole list). <c>IsWithdrawn</c>: withdrawn before it took effect; ignored.</summary>
public sealed record TaxRateListVersionDataV1(
    string Kind,
    long ListVersion,
    DateOnly EffectiveFrom,
    IReadOnlyList<TaxRateListEntryViewV1> Entries,
    bool IsWithdrawn = false);

/// <summary>
/// A rate list in force on a date: <c>Resolved</c> with its entries, or <c>Unresolved</c> (<c>rate_list_missing</c>) while none is
/// entered — then nothing can be picked (fail-closed, SAT-D148).
/// </summary>
public sealed record SettingsTaxRateListResolutionV1(
    Guid CompanyId,
    string Kind,
    DateOnly On,
    string State,
    long? ListVersion,
    DateOnly? EffectiveFrom,
    IReadOnlyList<TaxRateListEntryViewV1> Entries,
    string? Reason);

/// <summary>
/// SAT-D181: the read port of the two rate lists, consumed by the Sales order line. Company and actor are the server's: an actor
/// who cannot access the company is <c>settings_access_denied</c>; an unknown kind <c>settings_tax_master_request_invalid</c>. The
/// answer is reference data, not an authorization.
/// </summary>
public interface ISettingsTaxRateListReaderV1
{
    Task<SettingsTaxRateListResolutionV1> GetRateListAsync(string kind, DateOnly on, CancellationToken cancellationToken = default);
}

/// <summary>Why a rate list is unresolved.</summary>
public static class TaxRateListReasonsV1
{
    /// <summary>No version of the list is in force on the date (none entered yet, SAT-D148).</summary>
    public const string RateListMissing = "rate_list_missing";
}
