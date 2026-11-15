using System.Text.Json.Serialization;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// KLN-D38 (Sinan, 2026-11-14) permission name, the Counterparty owner's spelling. The central catalogue (ErpPermissions)
/// registers it; registering grants nothing — no role, seed or user receives it (human gate). Company-wide only: the
/// succession is recorded on the server-resolved company's two accounts. Not Critical. It is not the account status
/// permission: making the old account passive also needs <c>counterparty.accounts.status.update</c>, which the command
/// requires in addition, so no existing grant is silently widened to record a successor.
/// </summary>
public static class CounterpartyPartySuccessionPermissionsV1
{
    /// <summary>Record that a new legal person's cari succeeds an old cari in the active company.</summary>
    public const string Record = "counterparty.parties.succession.record";
}

/// <summary>
/// KLN-D38 VKN/TCKN change flow. The decision's four rules and where each lives:
/// <list type="number">
/// <item>A wrong entry stays on the SAME cari and is fixed by the reasoned, approved CARI-DEC-004 change request
/// (maker-checker, AT-009). Approval now also appends a dated <c>Correction</c> revision carrying the request and its
/// reason; nothing is overwritten without history.</item>
/// <item>A NEW legal person (sole proprietorship → company, merger, death) gets a NEW cari: <see
/// cref="RecordPartySuccessionAsync"/> makes the old cari's account in the active company passive and links it to the new
/// one as predecessor → successor with the legal basis. It moves NO balance: the balance moves only by an approved
/// transfer voucher (devir fişi) through a Finance port, which keeps the legal basis and evidence; Counterparty never
/// writes Finance tables.</item>
/// <item>A type change with the same VKN keeps the same cari; the official legal name change (ALS-D95) is recorded as a
/// dated revision.</item>
/// <item>An invoice reads the identity valid on its document date (<see cref="ICounterpartyAccountIdentityAsOfSourceV1"/>).</item>
/// </list>
/// VKN/TCKN, legal name and e-belge status are kept dated. OPEN-030 (company VKN) is out of scope and stays open.
/// </summary>
public interface ICounterpartyIdentityHistoryService
{
    /// <summary>The dated identity history of the party and its successions in the active company; null without an account.</summary>
    Task<CounterpartyIdentityHistoryView?> GetIdentityHistoryAsync(Guid partyId, CancellationToken cancellationToken);

    /// <summary>
    /// Records that <c>successorPartyId</c> succeeds <paramref name="predecessorPartyId"/> in the active company: the
    /// predecessor's account becomes passive (it may already be passive) and the link is stored. Fenced on the
    /// PREDECESSOR's company-account version, one Idempotency-Key, one audit event.
    /// </summary>
    Task<CounterpartyCommandExecution> RecordPartySuccessionAsync(
        Guid predecessorPartyId,
        RecordCounterpartyPartySuccession command,
        CounterpartyCommandMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>Why a new legal person succeeds an old one. On the wire it is the member name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CounterpartyPartySuccessionKindV1>))]
public enum CounterpartyPartySuccessionKindV1
{
    /// <summary>Şahıs işletmesinin şirkete dönüşmesi.</summary>
    Conversion = 1,

    /// <summary>Birleşme.</summary>
    Merger = 2,

    /// <summary>Ölüm.</summary>
    Death = 3,
}

/// <summary>
/// <c>expectedVersion</c> is the PREDECESSOR's company-account version (decimal text) from the read the form opened on.
/// <c>successorPartyId</c> must be another cari with an ACTIVE account in the active company and a different (or no)
/// VKN/TCKN: the same VKN is the same legal person, which is rule 3 (same cari), not a succession. <c>legalBasis</c> is
/// required (trimmed, at most 1000 characters; e.g. the trade registry announcement). <c>effectiveOn</c> is the business
/// day the new legal person took over (yyyy-MM-dd); it may not be later than today's Europe/Istanbul business day.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordCounterpartyPartySuccession(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid SuccessorPartyId,
    [property: JsonRequired] CounterpartyPartySuccessionKindV1 Kind,
    [property: JsonRequired] string LegalBasis,
    [property: JsonRequired] DateOnly EffectiveOn);

[JsonConverter(typeof(JsonStringEnumConverter<CounterpartyIdentityAttributeV1>))]
public enum CounterpartyIdentityAttributeV1
{
    TaxIdentityNumber = 1,
    LegalName = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<CounterpartyIdentityRevisionKindV1>))]
public enum CounterpartyIdentityRevisionKindV1
{
    /// <summary>The value on file before the first dated change; applies "since always".</summary>
    Baseline = 1,

    /// <summary>The first VKN/TCKN entry of a party that had none; applies "since always".</summary>
    InitialEntry = 2,

    /// <summary>An approved, reasoned correction of a wrong VKN/TCKN on the same cari.</summary>
    Correction = 3,

    /// <summary>A dated change of the same legal person (e.g. a type change that keeps the VKN).</summary>
    DatedChange = 4,
}

/// <summary>
/// One dated identity revision. <c>effectiveFrom</c> is null for "since always". <c>reason</c> and
/// <c>recordedInActiveCompany</c> are shown only to the company that recorded the revision (the identity itself is
/// group-level, DEC-CAR-001; who decided and why stays in the deciding company, as SAT-D119 does for the e-belge check).
/// </summary>
public sealed record CounterpartyIdentityRevisionView(
    CounterpartyIdentityAttributeV1 Attribute,
    CounterpartyIdentityRevisionKindV1 Kind,
    string Sequence,
    string Value,
    DateOnly? EffectiveFrom,
    string? Reason,
    bool RecordedInActiveCompany,
    DateTimeOffset RecordedAtUtc);

/// <summary>One dated e-belge status of the party's current VKN (SAT-D119 profile).</summary>
public sealed record CounterpartyEDocumentStatusRevisionView(
    string Sequence,
    CounterpartyEDocumentStatusV1 Status,
    DateOnly EffectiveFrom,
    bool RecordedInActiveCompany,
    DateTimeOffset RecordedAtUtc);

/// <summary>A predecessor → successor link of the active company, with the other cari's code and legal name.</summary>
public sealed record CounterpartyPartySuccessionView(
    Guid PredecessorPartyId,
    Guid SuccessorPartyId,
    Guid OtherPartyId,
    string OtherPartyCode,
    string OtherPartyLegalName,
    CounterpartyPartySuccessionKindV1 Kind,
    string LegalBasis,
    DateOnly EffectiveOn,
    DateTimeOffset RecordedAtUtc);

/// <summary>What the reader may do here, measured by the server for this actor and company at read time.</summary>
public sealed record CounterpartyIdentityHistoryViewer(bool CanRecordSuccession);

/// <summary>
/// The party's dated identity history and its successions in the active company. <c>accountVersion</c> is the fence the
/// succession command sends back. <c>revisions</c> is empty until the first dated change: the current values then apply
/// "since always". <c>successor</c> is the cari that succeeded this one in the active company (null when none);
/// <c>predecessors</c> are the cari this one succeeded there.
/// </summary>
public sealed record CounterpartyIdentityHistoryView(
    Guid PartyId,
    Guid CompanyId,
    string Code,
    PartyKind Kind,
    string AccountVersion,
    bool AccountIsActive,
    string? TaxIdentityNumber,
    string LegalName,
    IReadOnlyList<CounterpartyIdentityRevisionView> Revisions,
    IReadOnlyList<CounterpartyEDocumentStatusRevisionView> EDocumentStatusRevisions,
    CounterpartyPartySuccessionView? Successor,
    IReadOnlyList<CounterpartyPartySuccessionView> Predecessors,
    CounterpartyIdentityHistoryViewer Viewer);
