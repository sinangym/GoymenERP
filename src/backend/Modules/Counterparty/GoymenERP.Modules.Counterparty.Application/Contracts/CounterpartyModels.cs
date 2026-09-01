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

/// <summary>
/// Read-only C-03 projection. Version values are opaque decimal strings so a
/// JavaScript client cannot silently round an Int64 concurrency token.
/// </summary>
public sealed record CounterpartyDetailAccountView(
    Guid Id,
    Guid CompanyId,
    PartyRole Roles,
    bool IsActive,
    string Version);

/// <summary>
/// Authorized Cari 360 core summary. Integration-owner fields are deliberately
/// absent until a dedicated read permission and redaction contract exist.
/// </summary>
public sealed record CounterpartyDetailView(
    Guid Id,
    string Code,
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    bool IsActive,
    string Version,
    IReadOnlyList<CounterpartyDetailAccountView> CompanyAccounts);

public sealed record CreatePartyDraft(
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    Guid InitialCompanyId,
    PartyRole InitialRoles,
    string? ExternalIntegrationCode);
