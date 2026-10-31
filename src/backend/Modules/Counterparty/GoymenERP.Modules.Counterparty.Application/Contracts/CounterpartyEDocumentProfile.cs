using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// SAT-D102 permission name, the Counterparty owner's spelling. The central catalogue (ErpPermissions) registers it;
/// registering grants nothing — no role, seed or user receives it (human gate). Company-wide only: the e-belge profile
/// belongs to the server-resolved company's account. Not Critical. It is NOT the basic Cari edit
/// (<c>counterparty.parties.update.basic</c>): the e-belge status decides which official invoice type a customer gets,
/// so reusing an already granted name would silently widen existing grants.
/// </summary>
public static class CounterpartyEDocumentPermissionsV1
{
    /// <summary>Record the e-belge status check and set or clear the customer's e-Fatura scenario override.</summary>
    public const string Update = "counterparty.accounts.e-document.update";
}

/// <summary>
/// SAT-D102 (Sinan, 2026-10-28) and SAT-D119 (Sinan, 2026-10-31, EK17): the customer's e-belge profile — the
/// e-Fatura / e-Arşiv status (with who checked it and when) and an optional e-Fatura scenario overriding the Settings
/// default. SAT-D119 makes it a fact about the customer's VKN/TCKN, SHARED across all Göymen companies: it is a
/// group-level Counterparty record keyed by the tax identity number (<c>CustomerEDocumentProfile</c>), not a
/// per-company-account field, so a check recorded in one company is seen on the same-VKN customer's card in the
/// others. The allowed scenario codes and the default are Settings data
/// (<c>ISettingsEInvoiceScenarioPolicyProviderV1</c>, read per company); Cari never holds its own list. The command
/// takes a <c>partyId</c>, resolves the active company's account for it (existence fence) and the party's VKN, and
/// upserts the one VKN-keyed profile. Every command is the Counterparty command shape: company-wide permission first,
/// one Idempotency-Key (stored terminal answers replay), the PROFILE version as decimal text ("0" creates), one audit
/// event per real change. A party with no VKN cannot have an e-belge profile (fail-closed). The GİB mükellef query is
/// a follow-up; nothing here calls outside the ERP.
/// </summary>
public interface ICounterpartyEDocumentProfileService
{
    /// <summary>The active company's account of the party with its e-belge profile; null when the company holds no account.</summary>
    Task<CounterpartyAccountEDocumentView?> GetAccountEDocumentProfileAsync(
        Guid partyId,
        CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> RecordAccountEDocumentStatusAsync(
        Guid partyId,
        RecordCounterpartyEDocumentStatus command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> SetAccountEInvoiceScenarioOverrideAsync(
        Guid partyId,
        SetCounterpartyEInvoiceScenarioOverride command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>
/// The e-belge status of the customer. <c>Unknown</c> is explicit ("not checked"); nothing is inferred. On the wire it
/// is the member name ("Unknown", "EInvoice", "EArchive").
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CounterpartyEDocumentStatusV1>))]
public enum CounterpartyEDocumentStatusV1
{
    Unknown = 0,

    /// <summary>e-Fatura mükellefi.</summary>
    EInvoice = 1,

    /// <summary>e-Arşiv (not an e-Fatura taxpayer).</summary>
    EArchive = 2,
}

/// <summary>
/// <c>expectedVersion</c> is the company ACCOUNT version (decimal text). A known status records a new check (today's
/// check, by the caller) even when the value is unchanged; <c>Unknown</c> resets the status and its check.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordCounterpartyEDocumentStatus(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] CounterpartyEDocumentStatusV1 Status);

/// <summary>
/// <c>expectedVersion</c> is the company ACCOUNT version (decimal text). <c>scenarioCode</c> must be present: a code of
/// the Settings scenario list in force today, or an explicit null to clear the override (the Settings default applies).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetCounterpartyEInvoiceScenarioOverride(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string? ScenarioCode);

/// <summary>What a Settings key answered: a value, an empty key ("kaynak yok"), or a read that failed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CounterpartySettingsSourceStateV1>))]
public enum CounterpartySettingsSourceStateV1
{
    /// <summary>Settings could not be read (denied, malformed, not composed or failed); fail-closed.</summary>
    Unavailable = 0,

    /// <summary>The key is empty on the date (for example the default before Sinan enters it).</summary>
    NotSet = 1,

    /// <summary>The key has a value in force on the date.</summary>
    Set = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<CounterpartyEInvoiceScenarioOutcomeV1>))]
public enum CounterpartyEInvoiceScenarioOutcomeV1
{
    /// <summary>Settings could not be read: the scenario is unknown, never a guess.</summary>
    Unavailable = 0,

    /// <summary>No usable scenario: the invoice draft must ask for it (SAT-D102, fail-closed).</summary>
    Unresolved = 1,

    /// <summary>A scenario code is in force for this customer on the date.</summary>
    Resolved = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<CounterpartyEInvoiceScenarioSourceV1>))]
public enum CounterpartyEInvoiceScenarioSourceV1
{
    None = 0,

    /// <summary>The customer's own override on the Cari card.</summary>
    CustomerOverride = 1,

    /// <summary>The company's Settings default.</summary>
    SettingsDefault = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<CounterpartyEInvoiceScenarioUnresolvedReasonV1>))]
public enum CounterpartyEInvoiceScenarioUnresolvedReasonV1
{
    None = 0,

    /// <summary>No override and the Settings default is empty on the date.</summary>
    SettingsDefaultNotSet = 1,

    /// <summary>
    /// The customer has an override, but the Settings list in force on the date does not carry it (or there is no
    /// list): the override is not honoured and the default is NOT used in its place.
    /// </summary>
    CustomerOverrideNotListed = 2,
}

/// <summary>
/// The effective e-Fatura scenario of a customer on a date: the customer override if present (and listed), else the
/// Settings default, else Unresolved. <c>ScenarioCode</c> is non-null exactly when Outcome is Resolved.
/// </summary>
public sealed record CounterpartyEInvoiceScenarioResolutionV1(
    CounterpartyEInvoiceScenarioOutcomeV1 Outcome,
    CounterpartyEInvoiceScenarioSourceV1 Source,
    string? ScenarioCode,
    CounterpartyEInvoiceScenarioUnresolvedReasonV1 UnresolvedReason);

/// <summary>
/// SAT-D102's one rule, in one place, for the Cari card and the read port alike: pure, no I/O. The Settings reads are
/// done by the caller and passed as states; this type never invents a code.
/// </summary>
public static class CounterpartyEInvoiceScenarioResolverV1
{
    public static CounterpartyEInvoiceScenarioResolutionV1 Resolve(
        string? overrideCode,
        CounterpartySettingsSourceStateV1 codeListState,
        IReadOnlyCollection<string> listedCodes,
        CounterpartySettingsSourceStateV1 defaultState,
        string? defaultCode)
    {
        ArgumentNullException.ThrowIfNull(listedCodes);
        if (overrideCode is not null)
        {
            return codeListState switch
            {
                CounterpartySettingsSourceStateV1.Set when listedCodes.Contains(overrideCode, StringComparer.Ordinal) =>
                    new(CounterpartyEInvoiceScenarioOutcomeV1.Resolved,
                        CounterpartyEInvoiceScenarioSourceV1.CustomerOverride,
                        overrideCode,
                        CounterpartyEInvoiceScenarioUnresolvedReasonV1.None),
                CounterpartySettingsSourceStateV1.Set or CounterpartySettingsSourceStateV1.NotSet =>
                    new(CounterpartyEInvoiceScenarioOutcomeV1.Unresolved,
                        CounterpartyEInvoiceScenarioSourceV1.CustomerOverride,
                        null,
                        CounterpartyEInvoiceScenarioUnresolvedReasonV1.CustomerOverrideNotListed),
                _ => Unavailable,
            };
        }

        return defaultState switch
        {
            CounterpartySettingsSourceStateV1.Set when defaultCode is not null =>
                new(CounterpartyEInvoiceScenarioOutcomeV1.Resolved,
                    CounterpartyEInvoiceScenarioSourceV1.SettingsDefault,
                    defaultCode,
                    CounterpartyEInvoiceScenarioUnresolvedReasonV1.None),
            CounterpartySettingsSourceStateV1.NotSet =>
                new(CounterpartyEInvoiceScenarioOutcomeV1.Unresolved,
                    CounterpartyEInvoiceScenarioSourceV1.None,
                    null,
                    CounterpartyEInvoiceScenarioUnresolvedReasonV1.SettingsDefaultNotSet),
            _ => Unavailable,
        };
    }

    private static CounterpartyEInvoiceScenarioResolutionV1 Unavailable { get; } = new(
        CounterpartyEInvoiceScenarioOutcomeV1.Unavailable,
        CounterpartyEInvoiceScenarioSourceV1.None,
        null,
        CounterpartyEInvoiceScenarioUnresolvedReasonV1.None);
}

/// <summary>The Settings scenario code list in force on <c>evaluatedOn</c>; <c>codes</c> is empty unless State is Set.</summary>
public sealed record CounterpartyEInvoiceScenarioCodesView(
    CounterpartySettingsSourceStateV1 State,
    IReadOnlyList<string> Codes);

/// <summary>The Settings default scenario in force on <c>evaluatedOn</c>; <c>scenarioCode</c> is non-null exactly when State is Set.</summary>
public sealed record CounterpartyEInvoiceScenarioDefaultView(
    CounterpartySettingsSourceStateV1 State,
    string? ScenarioCode);

public sealed record CounterpartyAccountEDocumentViewer(bool CanUpdate);

/// <summary>
/// The active company's account of a Cari card and the customer's VKN-shared e-belge profile (SAT-D119).
/// <c>taxIdentityNumberPresent</c> is false when the party has no VKN/TCKN: no profile can exist, the status is
/// Unknown and the scenario is Unresolved (fail-closed), and both commands answer 422. <c>version</c> is the shared
/// PROFILE version the commands are fenced on; it is "0" when no profile exists yet (the first write creates it).
/// <c>statusCheckedAtUtc</c>/<c>statusCheckedByUserId</c> are null exactly when the status is Unknown.
/// <c>evaluatedOn</c> is the Europe/Istanbul business day the Settings keys were read for.
/// </summary>
public sealed record CounterpartyAccountEDocumentView(
    Guid PartyId,
    Guid AccountId,
    Guid CompanyId,
    string Version,
    bool AccountIsActive,
    bool TaxIdentityNumberPresent,
    CounterpartyEDocumentStatusV1 Status,
    DateTimeOffset? StatusCheckedAtUtc,
    string? StatusCheckedByUserId,
    string? ScenarioOverrideCode,
    DateOnly EvaluatedOn,
    CounterpartyEInvoiceScenarioCodesView ScenarioCodes,
    CounterpartyEInvoiceScenarioDefaultView SettingsDefault,
    CounterpartyEInvoiceScenarioResolutionV1 EffectiveScenario,
    CounterpartyAccountEDocumentViewer Viewer);

/// <summary>
/// SAT-D102: the Settings owner could not be read while a scenario override was validated. Nothing is stored, so the
/// same request may be retried with the same Idempotency-Key; the host answers the platform 503 <c>service_unavailable</c>.
/// </summary>
public sealed class CounterpartySettingsUnavailableException()
    : Exception("e-Fatura senaryo ayarları okunamadı; değişiklik kaydedilmedi.");
