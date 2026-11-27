namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

internal sealed class AiFeedbackEventRow
{
    public Guid CompanyId { get; set; }
    public Guid AnswerId { get; set; }
    public long Version { get; set; }
    public long? PriorVersion { get; set; }
    public int Kind { get; set; }
    public string? ReasonCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset RetainUntilUtc { get; set; }
}
internal sealed class AiFeedbackIdentityRow
{
    public Guid CompanyId { get; set; }
    public Guid AnswerId { get; set; }
    public long Version { get; set; }
    public Guid ActorId { get; set; }
    public string CorrelationId { get; set; } = "";
    public string IdempotencyKeySha256 { get; set; } = "";
    public string RequestSha256 { get; set; } = "";
}
