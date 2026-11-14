using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>
/// <see cref="IRefusedAttemptRecorderV1"/> (F-023, F023-1). <see cref="Record"/> leaves the restricted draft on the
/// request's audit collector, so the module's own save writes it atomically with the attempt row. The standalone path
/// opens a fresh scope bound to the same resolved company and appends one row through the Audit context; it never
/// throws for a write failure (KIM-D11: the refusal stands, the alarm metric is incremented).
/// </summary>
internal sealed class RefusedAttemptRecorder(
    IAuditEventCollector collector,
    ICompanyContext companyContext,
    IServiceScopeFactory scopeFactory,
    RefusedAttemptStandaloneWriteOptions standaloneWrite)
    : IRefusedAttemptRecorderV1
{
    public void Record(RefusedAttemptV1 attempt)
    {
        var draft = RefusedAttemptAuditDraftMapper.ToDraft(attempt);
        if (!companyContext.IsResolved || companyContext.CompanyId != draft.CompanyId)
        {
            throw new InvalidOperationException("Ret kaydı aktif şirket bağlamıyla eşleşmelidir.");
        }

        collector.Enqueue(draft);
    }

    public async Task<RefusedAttemptRecordResultV1> RecordStandaloneAsync(
        RefusedAttemptV1 attempt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var refusalClass = attempt?.RefusalClass;
        AuditEvent auditEvent;
        try
        {
            auditEvent = AuditEventFactory.Create(RefusedAttemptAuditDraftMapper.ToDraft(attempt!));
        }
        catch (ArgumentException)
        {
            return Fail(refusalClass, RefusedAttemptRecordFailureKindsV1.InvalidAttempt);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId != auditEvent.CompanyId)
        {
            return Fail(refusalClass, RefusedAttemptRecordFailureKindsV1.CompanyContextMismatch);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(standaloneWrite.Timeout);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyContext.CompanyId);
            var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await dbContext.AppendRefusedAttemptAsync(auditEvent, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any store failure is mapped to the alarm metric; it must not change the refusal.
        catch (Exception)
#pragma warning restore CA1031
        {
            // A provider may surface the caller's cancellation as its own exception type.
            cancellationToken.ThrowIfCancellationRequested();
            return Fail(
                refusalClass,
                timeout.IsCancellationRequested
                    ? RefusedAttemptRecordFailureKindsV1.Timeout
                    : RefusedAttemptRecordFailureKindsV1.StoreUnavailable);
        }

        return RefusedAttemptRecordResultV1.Written();
    }

    public void NoteReplayedRefusal(string refusalClass)
    {
        RefusedAttemptMetrics.RecordReplay(refusalClass);
    }

    private static RefusedAttemptRecordResultV1 Fail(string? refusalClass, string failureKind)
    {
        RefusedAttemptMetrics.RecordWriteFailure(refusalClass, failureKind);
        return RefusedAttemptRecordResultV1.NotWritten(failureKind);
    }
}
