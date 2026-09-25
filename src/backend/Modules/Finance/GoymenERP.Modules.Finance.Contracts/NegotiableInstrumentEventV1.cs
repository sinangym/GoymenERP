namespace GoymenERP.Modules.Finance.Contracts;

public enum NegotiableInstrumentKind
{
    Cheque = 1,
    PromissoryNote = 2,
}

public enum NegotiableInstrumentDirection
{
    Receivable = 1,
    Payable = 2,
}

public enum NegotiableInstrumentState
{
    Portfolio = 1,
    AtBank = 2,
    Collateral = 3,
    Endorsed = 4,
    Collected = 5,
    Dishonored = 6,
    Returned = 7,
    Protested = 8,
    Reversed = 9,
}

/// <summary>
/// DEC-FIN-016 immutable event candidate. Transition authorization, persistence and any
/// resulting money movement remain separate command responsibilities.
/// </summary>
public sealed record NegotiableInstrumentEventV1(
    Guid EventId,
    Guid CompanyId,
    Guid InstrumentId,
    string ExpectedVersion,
    NegotiableInstrumentKind Kind,
    NegotiableInstrumentDirection Direction,
    NegotiableInstrumentState State,
    MoneyText FaceValue,
    ProfileReference Profile,
    DateOnly MaturityDate,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid DocumentId,
    string ActorId,
    DateTimeOffset OccurredAtUtc,
    string Reason,
    Guid? ReversesEventId);
