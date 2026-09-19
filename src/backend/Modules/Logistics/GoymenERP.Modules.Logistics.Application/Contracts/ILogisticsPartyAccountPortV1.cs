namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// The Cari owner's answer about the instruction's customer at command time (LOG-4 D4): does this
/// company account belong to this party, and is it usable in the server-resolved company? A consumer
/// adapter boundary over the authorized Counterparty party read (the central host implements it, as
/// it does the Sales and Finance party ports); not a second catalogue. Identities and the owner's
/// versions only; nothing of the master is copied.
/// </summary>
public interface ILogisticsPartyAccountPortV1
{
    Task<LogisticsPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId, CancellationToken cancellationToken = default);
}

public enum LogisticsPartyAccountOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    NotFound = 2,
    Found = 3,
}

public sealed record LogisticsPartyAccountAnswerV1(LogisticsPartyAccountOutcomeV1 Outcome, LogisticsPartyAccountV1? Account);

public sealed record LogisticsPartyAccountV1(Guid PartyId, Guid CompanyAccountId, Guid CompanyId,
    bool PartyIsActive, bool AccountIsActive, string PartyVersion, string AccountVersion);
