namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

internal sealed class FinanceCreditLimitRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CreditAccountId { get; set; }
    public Guid LenderPartyId { get; set; }
    public Guid LenderCompanyPartyAccountId { get; set; }
    public int CreditGroup { get; set; }
    public Guid CurrencyId { get; set; }
    public long Version { get; set; }
    public long Revision { get; set; }
    public int Status { get; set; }
    public required string AmountText { get; set; }
    public required string SourceJson { get; set; }
    public Guid? PreviousLimitId { get; set; }
    public required string ProposedBy { get; set; }
    public DateTimeOffset ProposedAtUtc { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
}
