namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

internal sealed class AiCommandRecordRow
{
    public Guid CompanyId { get; set; }
    public Guid PreviewId { get; set; }
    public string ActorId { get; set; } = "";
    public int State { get; set; }
    public long Version { get; set; }
    public string BindingSha256 { get; set; } = "";
    public string RecordJson { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}

internal sealed class AiCommandEventRow
{
    public Guid CompanyId { get; set; }
    public Guid EventId { get; set; }
    public string ActorId { get; set; } = "";
    public int Kind { get; set; }
    public Guid? PreviewId { get; set; }
    public string? ToolId { get; set; }
    public string? ToolVersion { get; set; }
    public string? PreviewHash { get; set; }
    public Guid? ApprovalId { get; set; }
    public string? ReasonCode { get; set; }
    public string? ResultVersion { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? CausationId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
