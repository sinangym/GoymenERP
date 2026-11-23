using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// F-027 / EK-18 #76 permission name, the Counterparty owner's spelling. The central catalogue (ErpPermissions) registers
/// it; registering grants nothing — no role, seed or user receives it (the group-manager persona grant is a human gate).
/// Company-wide only, and the merge command measures it in EVERY company that holds an account for either party, so one
/// company's grant never merges a card another company relies on. Not Critical (#76).
/// </summary>
public static class CounterpartyPartyMergePermissionsV1
{
    /// <summary>Approve the merge of a duplicate Cari party into a surviving party (group level, no unmerge).</summary>
    public const string Approve = "counterparty.parties.merge.approve";
}

/// <summary>
/// F-027 / EK-18 #76-#77 (SINAN_KARARLARI_2026-10-21): a duplicate Cari party is merged into a surviving party at group
/// level by the holder of <c>counterparty.parties.merge.approve</c> (plus <c>counterparty.parties.read</c>) in every
/// company that holds an account for either party. The approval is recorded once (append-only), the duplicate becomes
/// passive and points at the survivor for good, and every such company receives a
/// <see cref="CounterpartyPartyMergedOutboxV1"/> event so each module moves its own references. There is no unmerge
/// (#77): a wrong merge is corrected forward with a new card and reversed links. Company accounts, balances and
/// documents are not moved or changed by this command.
/// </summary>
public interface ICounterpartyPartyMergeService
{
    /// <summary>
    /// The party's merge state, its same-number duplicate candidates in the active company and whether this reader may
    /// approve a merge here; null when the active company holds no account for the party.
    /// </summary>
    Task<CounterpartyPartyMergeView?> GetMergeAsync(Guid partyId, CancellationToken cancellationToken);

    /// <summary>Merges <see cref="ApproveCounterpartyPartyMerge.MergedPartyId"/> into the route's surviving party.</summary>
    Task<CounterpartyCommandExecution> ApproveMergeAsync(
        Guid survivorPartyId, ApproveCounterpartyPartyMerge command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>
/// The duplicate to merge into the route's surviving party. <c>expectedVersion</c> is the survivor's PARTY version and
/// <c>mergedPartyExpectedVersion</c> the duplicate's, both decimal text from the read the form opened on. <c>reason</c> is
/// required (1-1000 characters).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveCounterpartyPartyMerge(
    [property: JsonRequired] Guid MergedPartyId,
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string MergedPartyExpectedVersion,
    [property: JsonRequired] string Reason);

/// <summary>What the reader may do here, measured by the server for this actor in the active company at read time.</summary>
public sealed record CounterpartyPartyMergeViewer(bool CanApproveMerge);

/// <summary>
/// The surviving party a merged card now points at. <c>code</c> is given only when the active company also holds an
/// account for the survivor; otherwise it is null, so a card another company holds is never named here.
/// </summary>
public sealed record CounterpartyPartyMergeTarget(Guid PartyId, string? Code);

/// <summary>Another card of the active company carrying the same VKN/TCKN, not merged, with its version as text.</summary>
public sealed record CounterpartyPartyMergeCandidate(
    Guid PartyId,
    string Code,
    string LegalName,
    string Version);

public sealed record CounterpartyPartyMergeView(
    Guid PartyId,
    Guid CompanyId,
    string Code,
    string Version,
    string LegalName,
    CounterpartyPartyMergeTarget? MergedInto,
    IReadOnlyList<CounterpartyPartyMergeCandidate> Candidates,
    CounterpartyPartyMergeViewer Viewer);
