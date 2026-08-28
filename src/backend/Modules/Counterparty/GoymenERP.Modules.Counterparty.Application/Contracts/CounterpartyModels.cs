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

public sealed record CreatePartyDraft(
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    Guid InitialCompanyId,
    PartyRole InitialRoles,
    string? ExternalIntegrationCode);
