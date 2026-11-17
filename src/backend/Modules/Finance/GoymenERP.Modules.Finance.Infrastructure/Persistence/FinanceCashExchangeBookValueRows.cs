namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

internal sealed class CashExchangeBookValueProposalRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid TransferId { get; set; }
    public string TransferSourceJson { get; set; } = string.Empty;
    public string TransferSourceHash { get; set; } = string.Empty;
    public string PreparationJson { get; set; } = string.Empty;
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
}

internal sealed class CashExchangeBookValueDecisionRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ProposalId { get; set; }
    public Guid TransferId { get; set; }
    public bool Approved { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public string DecidedBy { get; set; } = string.Empty;
    public DateTimeOffset DecidedAtUtc { get; set; }
    public string? Reason { get; set; }
}

internal sealed class CashExchangeBookValueCommandRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
}
