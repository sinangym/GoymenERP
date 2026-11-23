using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// FNS-D121 (SINAN_KARARLARI_2026-11-22_EK_SORULAR.md; completes FNS-D114): a cari card's IBAN list in the context company
/// (IBAN, bank, description). The Counterparty owner keeps it: versioned, audited, never deleted (a wrong IBAN is
/// deactivated). The bank statement matching looks a statement IBAN up ONLY in this list; nothing is learned from a statement.
/// IBANs are Confidential personal data (AI-D42): no AI tool releases them and they have their own read permission.
/// </summary>
public static class CounterpartyPartyIbanPermissionsV1
{
    public const string Read = "counterparty.party-ibans.read";
    public const string Manage = "counterparty.party-ibans.manage";
}

/// <summary>Published codes of the IBAN list (422/409 bodies; 401/403/404/503 are code-less).</summary>
public static class CounterpartyPartyIbanCodesV1
{
    public const string RequestInvalid = "counterparty_party_iban_request_invalid";
    public const string IbanInvalid = "counterparty_party_iban_invalid";
    public const string BankNameRequired = "counterparty_party_iban_bank_name_required";
    public const string TextTooLong = "counterparty_party_iban_text_too_long";
    public const string ReasonRequired = "counterparty_party_iban_reason_required";
    public const string AlreadyListed = "counterparty_party_iban_already_listed";
    public const string PartyNotFound = "counterparty_party_iban_party_not_found";
    public const string IbanNotFound = "counterparty_party_iban_not_found";
    public const string Unavailable = "counterparty_party_iban_unavailable";
}

public static class CounterpartyPartyIbanLimitsV1
{
    public const int BankNameMaximumLength = 200;
    public const int DescriptionMaximumLength = 500;
    public const int ReasonMaximumLength = 1000;
    public const int MaximumActivePerParty = 50;
}

/// <summary>
/// FNS-D121: a Turkish IBAN (TR mod-97). The stored form has no spaces and is upper case; <c>TR</c> + 24 digits, ISO 13616
/// check digits valid. Other countries are refused (the rule names TR mod-97 only).
/// </summary>
public static class CounterpartyIbanV1
{
    public const int TurkishLength = 26;

    public static string Normalize(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : new string(text.Where(character => character is not (' ' or '\t' or '-')).Select(char.ToUpperInvariant).ToArray());

    public static bool IsValidTurkish(string? normalized)
    {
        if (normalized is null || normalized.Length != TurkishLength || normalized[0] != 'T' || normalized[1] != 'R'
            || !normalized.Skip(2).All(char.IsAsciiDigit))
            return false;
        var remainder = 0;
        for (var offset = 0; offset < normalized.Length; offset++)
        {
            var character = normalized[(offset + 4) % normalized.Length];
            remainder = char.IsAsciiDigit(character)
                ? (remainder * 10 + (character - '0')) % 97
                : (remainder * 100 + (character - 'A' + 10)) % 97;
        }

        return remainder == 1;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddCounterpartyPartyIbanV1(
    [property: JsonRequired] Guid IbanId,
    [property: JsonRequired] string Iban,
    [property: JsonRequired] string BankName,
    string? Description);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeactivateCounterpartyPartyIbanV1(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string Reason);

public sealed record CounterpartyPartyIbanViewV1(
    Guid Id,
    Guid CompanyId,
    Guid PartyId,
    string Iban,
    string BankName,
    string? Description,
    bool IsActive,
    string Version,
    string AddedBy,
    DateTimeOffset AddedAtUtc,
    string? DeactivatedBy,
    DateTimeOffset? DeactivatedAtUtc,
    string? DeactivationReason);

/// <summary>The party's list in the context company, active first then by IBAN (ordinal).</summary>
public sealed record CounterpartyPartyIbanListV1(Guid CompanyId, Guid PartyId, IReadOnlyList<CounterpartyPartyIbanViewV1> Ibans);

public enum CounterpartyPartyIbanOutcomeV1 { Unavailable, Denied, NotFound, ValidationFailed, VersionConflict, IdempotencyConflict, Succeeded }

public sealed record CounterpartyPartyIbanCommandResultV1(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CounterpartyPartyIbanOutcomeV1>))] CounterpartyPartyIbanOutcomeV1 Outcome,
    CounterpartyPartyIbanViewV1? Resource,
    string? Code,
    bool Replayed = false);

public sealed record CounterpartyPartyIbanListResultV1(
    [property: JsonConverter(typeof(JsonStringEnumConverter<CounterpartyPartyIbanOutcomeV1>))] CounterpartyPartyIbanOutcomeV1 Outcome,
    CounterpartyPartyIbanListV1? List);

/// <summary>
/// FNS-D121 commands and read. Every call is fenced on the server-resolved company and actor before and after each await;
/// commands carry an Idempotency-Key (same actor + payload replays), deactivation an expected version and a mandatory
/// reason; one save writes the row, the command record and one audit event.
/// </summary>
public interface ICounterpartyPartyIbanService
{
    Task<CounterpartyPartyIbanListResultV1> ListAsync(Guid partyId, CancellationToken cancellationToken);

    Task<CounterpartyPartyIbanCommandResultV1> AddAsync(Guid partyId, AddCounterpartyPartyIbanV1 request,
        string idempotencyKey, string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
        string? userAgent, CancellationToken cancellationToken);

    Task<CounterpartyPartyIbanCommandResultV1> DeactivateAsync(Guid partyId, Guid ibanId, DeactivateCounterpartyPartyIbanV1 request,
        string idempotencyKey, string correlationId, string? causationId, string sourceChannel, string? clientIpAddress,
        string? userAgent, CancellationToken cancellationToken);
}

/// <summary>
/// FNS-D121 owner read for other modules (the Finance statement matching): for each asked IBAN, the parties whose ACTIVE list
/// in the server-resolved company holds it. It carries no authority (the consumer fences its own permission before and
/// after) and creates nothing. Null means the source cannot answer (fail-closed: matching by IBAN stays off).
/// </summary>
public interface ICounterpartyPartyIbanSourceV1
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<Guid>>?> FindPartiesAsync(IReadOnlyCollection<string> ibans,
        CancellationToken cancellationToken);
}
