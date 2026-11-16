using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// ALS-D111 + ALS-D113 permission names (replacing the one-step ALS-D95/ALS-D110 name), the Counterparty owner's spelling.
/// The central catalogue (ErpPermissions) registers them; registering grants nothing — no role, seed or user receives
/// either here, so the flow stays closed until a human grants it (ALS-D113 names the accounting specialist persona for
/// the proposal and the accounting manager or the general manager for the approval; the grant is a human gate).
/// Company-wide only: the party is reached only through the server-resolved company's account for it, and both names
/// are measured in that company. Not Critical. Neither is the basic Cari edit
/// (<c>counterparty.parties.update.basic</c>), so existing basic-edit grants are not silently widened to the official
/// legal name.
/// </summary>
public static class CounterpartyLegalNamePermissionsV1
{
    /// <summary>Propose a change of the Cari card's official legal name (the maker).</summary>
    public const string Propose = "counterparties.legal_name.propose";

    /// <summary>Approve or reject a proposed change (the checker; one approval is enough; never the proposer).</summary>
    public const string Approve = "counterparties.legal_name.approve";
}

/// <summary>
/// ALS-D111 + ALS-D113: the official legal name (resmi/hukuki unvan) of a Cari card is changed by maker-checker. The
/// holder of <c>counterparties.legal_name.propose</c> proposes a new name with a reason; the name does NOT change then.
/// A DIFFERENT holder of <c>counterparties.legal_name.approve</c> approves (one approval is enough) or rejects it; only
/// the approval writes the new name, as a dated identity revision with one audit event carrying the old and the new name.
/// Every command needs <c>counterparty.parties.read</c> too, is fenced on the PARTY version, carries one
/// Idempotency-Key (stored terminal answers replay) and is answered 404 when the active company holds no account for the
/// party. The party is shared by every Göymen company holding an account for it (DEC-CAR-001), so an approved change is
/// seen by all of them. VKN/TCKN stays in its own CARI-DEC-004 maker-checker flow; the trade name stays on its own
/// basic-edit command (ALS-D97).
/// </summary>
public interface ICounterpartyLegalNameService
{
    /// <summary>
    /// The card's legal name, the party version, the pending proposal and the last decided one; null when the active
    /// company holds no account for the party.
    /// </summary>
    Task<CounterpartyLegalNameView?> GetLegalNameAsync(Guid partyId, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> ProposeLegalNameChangeAsync(
        Guid partyId, ProposeCounterpartyLegalNameChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> DecideLegalNameChangeAsync(
        Guid partyId, DecideCounterpartyLegalNameChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>
/// <c>expectedVersion</c> is the PARTY version (decimal text) from the read the form opened on. <c>proposedLegalName</c>
/// is required, trimmed, at most 250 characters and different from the stored name; <c>reason</c> is required (at most
/// 1000 characters).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposeCounterpartyLegalNameChange(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string ProposedLegalName,
    [property: JsonRequired] string Reason);

/// <summary><c>decision</c> is <c>approve</c> or <c>reject</c>; the request is named by id and fenced on the party version.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideCounterpartyLegalNameChange(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid ChangeRequestId,
    [property: JsonRequired] string Decision,
    string? Note = null);

public static class CounterpartyLegalNameDecisionsV1
{
    public const string Approve = "approve";
    public const string Reject = "reject";
}

public static class CounterpartyLegalNameChangeStatusesV1
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public sealed record CounterpartyLegalNameChangeView(
    Guid Id,
    string Status,
    string CurrentLegalName,
    string ProposedLegalName,
    string Reason,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote);

/// <summary>What the reader may do here, measured by the server for this actor and company at read time.</summary>
public sealed record CounterpartyLegalNameViewer(bool CanPropose, bool CanApprove, bool IsRequesterOfPending);

public sealed record CounterpartyLegalNameView(
    Guid PartyId,
    Guid CompanyId,
    string Code,
    string Version,
    string LegalName,
    CounterpartyLegalNameChangeView? PendingChange,
    CounterpartyLegalNameChangeView? LastDecidedChange,
    CounterpartyLegalNameViewer Viewer);
