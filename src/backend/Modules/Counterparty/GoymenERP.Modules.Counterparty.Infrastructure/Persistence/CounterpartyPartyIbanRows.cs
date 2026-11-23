namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

/// <summary>
/// FNS-D121: one IBAN on a cari card's list in one company. Immutable except the single deactivation step (version 1 → 2,
/// with who/when/why). No deletion; a corrected IBAN is a new row.
/// </summary>
internal sealed class CounterpartyPartyIbanRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PartyId { get; set; }
    public long Version { get; set; }
    public string Iban { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string AddedBy { get; set; } = string.Empty;
    public DateTimeOffset AddedAtUtc { get; set; }
    public string? DeactivatedBy { get; set; }
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public string? DeactivationReason { get; set; }
}

internal sealed class CounterpartyPartyIbanCommandRow
{
    public Guid CompanyId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
