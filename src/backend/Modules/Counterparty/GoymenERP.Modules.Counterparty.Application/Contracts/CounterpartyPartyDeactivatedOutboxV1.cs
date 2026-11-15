namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// F-027 / DEC-CAR-003 / CARI-AC-016: Counterparty's durable event for a cari that the active company made passive. It
/// is written in the same transaction as the account change, only when the account really moved from active to passive
/// (a no-op save, a reactivation, a refusal or an idempotent replay writes nothing). There is no separate approval: the
/// command itself carries the permission, the reason, the expected version, the Idempotency-Key and the audit event.
/// Persistence of this event is not publication; dispatch, retry, retention and consumers remain outside this command.
/// </summary>
public static class CounterpartyPartyDeactivatedOutboxV1
{
    public const string EventType = "counterparty.party.deactivated.v1";
    public const int SchemaVersion = 1;
    public const string SourceOperation = "counterparty.company-account.set-active-state.v1";
    public const string AggregateType = "Counterparty.CompanyPartyAccount";
}

/// <summary>
/// The passive cari in the producing company: the party, the trimmed reason given with the command and the company
/// account version after the change (the version a consumer can order or fence on).
/// </summary>
public sealed record CounterpartyPartyDeactivatedV1(
    Guid CompanyId,
    Guid PartyId,
    string Reason,
    long Version);
