using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// AT-009 permission names, the Counterparty owner's spelling. The central catalogue (ErpPermissions) registers them; a
/// name the catalogue does not know is never a grant. Both are company-wide only: the Cari card is read and written in
/// the server-resolved company, which has no narrower dimension.
/// </summary>
public static class CounterpartyTaxIdentityPermissionsV1
{
    /// <summary>Enter a party's first VKN/TCKN and propose a change of an existing one (the maker).</summary>
    public const string Update = "counterparty.parties.tax-identity.update";

    /// <summary>Approve or reject a proposed change (the checker; never the proposer).</summary>
    public const string Approve = "counterparty.parties.tax-identity.approve";
}

public interface ICounterpartyTaxIdentityService
{
    Task<CounterpartyTaxIdentityView?> GetTaxIdentityAsync(Guid partyId, CancellationToken cancellationToken);

    /// <summary>
    /// The duplicate warning for a candidate number: the OTHER Cari cards of the active company already carrying it.
    /// Null when the party has no account in the active company. The number must already be valid.
    /// </summary>
    Task<CounterpartyTaxIdentityDuplicateCheck?> CheckDuplicatesAsync(
        Guid partyId, string taxIdentityNumber, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> EnterTaxIdentityAsync(
        Guid partyId, EnterCounterpartyTaxIdentity command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> RequestTaxIdentityChangeAsync(
        Guid partyId, RequestCounterpartyTaxIdentityChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);

    Task<CounterpartyCommandExecution> DecideTaxIdentityChangeAsync(
        Guid partyId, DecideCounterpartyTaxIdentityChange command,
        CounterpartyCommandMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>First entry. <c>expectedVersion</c> is the PARTY version (decimal text) from the read the form opened on.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnterCounterpartyTaxIdentity(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string TaxIdentityNumber);

/// <summary>A change of an existing number, held pending until another person decides it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestCounterpartyTaxIdentityChange(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] string ProposedTaxIdentityNumber,
    [property: JsonRequired] string Reason);

/// <summary><c>decision</c> is <c>approve</c> or <c>reject</c>; the request is named by id and fenced on the party version.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideCounterpartyTaxIdentityChange(
    [property: JsonRequired] string ExpectedVersion,
    [property: JsonRequired] Guid ChangeRequestId,
    [property: JsonRequired] string Decision,
    string? Note = null);

public static class CounterpartyTaxIdentityDecisionsV1
{
    public const string Approve = "approve";
    public const string Reject = "reject";
}

public static class CounterpartyTaxIdentityKindsV1
{
    public const string Vkn = "vkn";
    public const string Tckn = "tckn";
}

public static class CounterpartyTaxIdentityChangeStatusesV1
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>Another Cari card of the ACTIVE company carrying the same number (a warning, never a refusal).</summary>
public sealed record CounterpartyTaxIdentityDuplicate(
    Guid PartyId, string Code, string LegalName, bool AccountIsActive);

public sealed record CounterpartyTaxIdentityDuplicateCheck(
    string TaxIdentityNumber, string Kind, IReadOnlyList<CounterpartyTaxIdentityDuplicate> Duplicates);

public sealed record CounterpartyTaxIdentityChangeView(
    Guid Id,
    string Status,
    string CurrentTaxIdentityNumber,
    string ProposedTaxIdentityNumber,
    string ProposedKind,
    string Reason,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote,
    IReadOnlyList<CounterpartyTaxIdentityDuplicate> ProposedDuplicates);

/// <summary>What the reader may do here, measured by the server for this actor and company at read time.</summary>
public sealed record CounterpartyTaxIdentityViewer(bool CanUpdate, bool CanApprove, bool IsRequesterOfPending);

public sealed record CounterpartyTaxIdentityView(
    Guid PartyId,
    Guid CompanyId,
    string Version,
    string? TaxIdentityNumber,
    string? Kind,
    IReadOnlyList<CounterpartyTaxIdentityDuplicate> Duplicates,
    CounterpartyTaxIdentityChangeView? PendingChange,
    CounterpartyTaxIdentityChangeView? LastDecidedChange,
    CounterpartyTaxIdentityViewer Viewer);

/// <summary>AT-009: the submitted number fails the VKN/TCKN rules. Answered 400 <c>tax_identity_invalid</c>; never stored.</summary>
public sealed class CounterpartyTaxIdentityInvalidException(string message) : ArgumentException(message);
