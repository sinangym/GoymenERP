namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Cari-owned exact eligibility read for an Organization location-operator assignment that the
/// consuming Organization command has already authorized. This is not a Cari list, search or
/// card read, grants no user authority and must not be used for an arbitrary presentation lookup.
/// </summary>
public interface ICounterpartyLocationOperatorEligibilitySourceV1
{
    Task<CounterpartyLocationOperatorEligibilityAnswerV1> ReadForLocationOperatorAsync(
        CounterpartyLocationOperatorEligibilityRequestV1 request,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyLocationOperatorEligibilityOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Ineligible = 3,
    Eligible = 4,
}

/// <summary>
/// CompanyId and ActorId are copies of the current server context, never caller overrides.
/// PartyId is the exact proposed external-location operator.
/// </summary>
public sealed record CounterpartyLocationOperatorEligibilityRequestV1(
    Guid CompanyId,
    string ActorId,
    Guid PartyId);

public sealed record CounterpartyLocationOperatorEligibilityAnswerV1(
    CounterpartyLocationOperatorEligibilityOutcomeV1 Outcome,
    CounterpartyLocationOperatorEligibilityV1? Operator);

/// <summary>
/// Current eligible supplier facts. Tolling is an independent role and never substitutes for
/// Supplier eligibility. The Organization owner persists its own relationship in a later slice.
/// </summary>
public sealed record CounterpartyLocationOperatorEligibilityV1(
    Guid PartyId,
    Guid CompanyAccountId,
    string Code,
    string LegalName,
    string? TradeName,
    string PartyVersion,
    string AccountVersion);
