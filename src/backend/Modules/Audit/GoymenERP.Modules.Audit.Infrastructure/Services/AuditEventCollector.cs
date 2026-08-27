using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

public sealed class AuditEventCollector : IAuditEventCollector
{
    private readonly List<AuditEventDraft> _pending = [];

    internal IReadOnlyList<AuditEventDraft> Pending => _pending;

    public void Enqueue(AuditEventDraft auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        _pending.Add(auditEvent);
    }

    internal void Clear()
    {
        _pending.Clear();
    }
}
