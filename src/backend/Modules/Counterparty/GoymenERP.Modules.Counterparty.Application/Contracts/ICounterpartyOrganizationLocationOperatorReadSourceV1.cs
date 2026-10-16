namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Cari-owned, record-bound display and selection source for Organization location operators.
/// The consumer must derive bindings and location identity from an already-authorized Organization
/// record or command. This is not a generic Cari list, card or arbitrary Party-id lookup and grants
/// no Organization or Cari permission.
/// </summary>
public interface ICounterpartyOrganizationLocationOperatorReadSourceV1
{
    Task<CounterpartyOrganizationLocationOperatorDisplayAnswerV1> ReadAuthorizedDisplaysAsync(
        CounterpartyOrganizationLocationOperatorDisplayRequestV1 request,
        CancellationToken cancellationToken = default);

    Task<CounterpartyOrganizationLocationOperatorCandidateAnswerV1> SearchAuthorizedCandidatesAsync(
        CounterpartyOrganizationLocationOperatorCandidateRequestV1 request,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyOrganizationLocationOperatorReadOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    InvalidCursor = 2,
    Success = 3,
}

/// <summary>
/// CompanyId and ActorId are copies of server-resolved context. Bindings are extracted from
/// already-authorized Organization location rows and never from arbitrary client Party ids.
/// </summary>
public sealed record CounterpartyOrganizationLocationOperatorDisplayRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<CounterpartyOrganizationLocationOperatorBindingV1> Bindings);

public sealed record CounterpartyOrganizationLocationOperatorBindingV1(
    Guid LocationId,
    Guid PartyId);

public sealed record CounterpartyOrganizationLocationOperatorDisplayAnswerV1(
    CounterpartyOrganizationLocationOperatorReadOutcomeV1 Outcome,
    IReadOnlyList<CounterpartyOrganizationLocationOperatorDisplayV1> Items);

public sealed record CounterpartyOrganizationLocationOperatorDisplayV1(
    Guid LocationId,
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName,
    bool IsCurrentlyEligible);

/// <summary>
/// LocationId is the exact server-authorized Organization location being edited. SessionBinding is
/// a stable server-owned operation identity; it is included in the opaque cursor binding.
/// </summary>
public sealed record CounterpartyOrganizationLocationOperatorCandidateRequestV1(
    Guid CompanyId,
    string ActorId,
    string SessionBinding,
    Guid LocationId,
    string? Query,
    int PageSize,
    string? Cursor);

public sealed record CounterpartyOrganizationLocationOperatorCandidateAnswerV1(
    CounterpartyOrganizationLocationOperatorReadOutcomeV1 Outcome,
    IReadOnlyList<CounterpartyOrganizationLocationOperatorCandidateV1> Items,
    string? NextCursor,
    bool HasMore);

public sealed record CounterpartyOrganizationLocationOperatorCandidateV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);
