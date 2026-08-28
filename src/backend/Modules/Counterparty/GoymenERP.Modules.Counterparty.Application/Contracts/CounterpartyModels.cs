using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;

namespace GoymenERP.Modules.Counterparty.Application.Contracts;

public sealed record CompanyPartyAccountView(
    Guid Id,
    Guid CompanyId,
    PartyRole Roles,
    string? ExternalIntegrationCode,
    bool IsActive,
    long Version);

public sealed record PartyCoreView(
    Guid Id,
    string Code,
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    bool IsActive,
    long Version,
    IReadOnlyList<CompanyPartyAccountView> CompanyAccounts);

public sealed record PartyPage(
    IReadOnlyList<PartyCoreView> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record CreatePartyCommand(
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    PartyRole InitialRoles,
    string? ExternalIntegrationCode);

public sealed record CounterpartyCommandMetadata(
    Guid CompanyId,
    string ActorId,
    string CorrelationId,
    string? CausationId,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent,
    string IdempotencyKey);

public enum CreatePartyResult
{
    Succeeded = 1,
    IdempotencyConflict = 2,
}

public sealed record CreatePartyResponse(
    Guid PartyId,
    Guid CompanyPartyAccountId,
    string Code,
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    PartyRole Roles,
    string? ExternalIntegrationCode,
    bool IsActive,
    long Version);

public sealed record CreatePartyExecutionResult(
    CreatePartyResult Result,
    CreatePartyResponse? Response,
    bool IsReplay,
    string OriginalCorrelationId);
