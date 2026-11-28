using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

// Read-only selected-pair preview. This does not add a request/approval stage or alter the native merge payload/hash.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewCounterpartyPartyMergeV1(
    [property: JsonRequired] Guid MergedPartyId,
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string MergedPartyExpectedVersion);

public enum CounterpartyPartyMergePreviewStatusV1 { Found, NotFound, Changed, Unavailable }
public sealed record CounterpartyPartyMergePreviewResultV1(CounterpartyPartyMergePreviewStatusV1 Status,
    CounterpartyPartyMergeImpactPreviewV1? Preview);
public sealed record CounterpartyPartyMergeImpactPartyV1(Guid PartyId, string Code, string LegalName,
    string Version, bool IsActive, Guid? MergedIntoPartyId);
// These holding accounts remain separate and unchanged. No Finance balance/document count is inferred here.
public sealed record CounterpartyPartyMergeHoldingAccountV1(Guid CompanyId, Guid AccountId, Guid PartyId,
    string Version, bool IsActive);
public sealed record CounterpartyPartyMergeRecordChangesV1(Guid PartyId, Guid MergedIntoPartyId, bool IsActive);
public sealed record CounterpartyPartyMergeImpactPreviewV1(Guid CompanyId,
    CounterpartyPartyMergeImpactPartyV1 Survivor, CounterpartyPartyMergeImpactPartyV1 Merged,
    IReadOnlyList<CounterpartyPartyMergeHoldingAccountV1> HoldingAccounts, CounterpartyPartyMergeRecordChangesV1 Changes);
