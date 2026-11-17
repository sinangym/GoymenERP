namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

internal sealed class CreditMovementProposalRow : IAccountingTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MovementId { get; set; }
    public bool IsReversal { get; set; }
    public required string SourceHash { get; set; }
    public required string SourceJson { get; set; }
    public required string MappingStatus { get; set; }
    public long? MappingVersion { get; set; }
    public required string WaitingReasonsJson { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
}
