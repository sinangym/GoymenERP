namespace GoymenERP.Modules.Audit.Application.Contracts;

public interface IAuditEventCollector
{
    void Enqueue(AuditEventDraft auditEvent);
}
