namespace GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;

internal sealed class AiScheduledTaskRow
{
    public Guid CompanyId { get; set; }
    public Guid TaskId { get; set; }
    public string OwnerActorId { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public int Effect { get; set; }
    public int State { get; set; }
    public long Version { get; set; }
    public string ScheduleVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
internal sealed class AiScheduledTaskCommandRow
{
    public Guid CompanyId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public Guid TaskId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}
internal sealed class AiScheduledTaskRunRow
{
    public Guid CompanyId { get; set; }
    public Guid TaskId { get; set; }
    public DateTimeOffset SlotUtc { get; set; }
    public string ReceiptJson { get; set; } = string.Empty;
}
internal sealed class AiScheduledTaskEventRow
{
    public Guid CompanyId { get; set; }
    public Guid EventId { get; set; }
    public Guid TaskId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public long? Version { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
