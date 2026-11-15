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
/// throws for a write failure (KIM-D11: the refusal stands, the alarm metric is incremented). F023-4: the standalone
/// path asks the <see cref="RefusedAttemptAggregator"/> whether the refusal is its own row; without one, or with the
/// shipped disabled setting, every refusal is its own row. With it enabled, each standalone refusal also appends the
/// ended window summaries of its own company through <see cref="AppendAggregationSummaryAsync"/>, so this file stays
/// the only caller of the standalone append.
/// </summary>
internal sealed class RefusedAttemptRecorder(
    IAuditEventCollector collector,
    ICompanyContext companyContext,
    IServiceScopeFactory scopeFactory,
    RefusedAttemptStandaloneWriteOptions standaloneWrite,
    RefusedAttemptAggregator? aggregator = null)
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

        RefusedAttemptMetrics.RecordStandaloneObserved(refusalClass);
        if (aggregator is { IsEnabled: true })
        {
            var admitted = aggregator.Admit(attempt!);
            await AppendEndedSummariesAsync(aggregator, companyContext.CompanyId, cancellationToken);
            if (!admitted)
            {
                return RefusedAttemptRecordResultV1.Aggregated();
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(standaloneWrite.Timeout);
        try
        {
            await AppendInCompanyScopeAsync(scopeFactory, companyContext.CompanyId, auditEvent, timeout.Token);
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

    private async Task AppendEndedSummariesAsync(
        RefusedAttemptAggregator enabledAggregator,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        foreach (var summary in enabledAggregator.TakeEnded(companyId))
        {
            await AppendAggregationSummaryAsync(scopeFactory, standaloneWrite, summary, cancellationToken);
        }
    }

    /// <summary>
    /// Appends the summary row of one closed aggregation window (F023-4) in a fresh scope bound to the summary's
    /// company, under the same short write timeout. It never throws: any failure, including the caller's cancellation,
    /// counts the folded refusals in <c>audit_refused_attempt_aggregation_lost</c> and returns <see langword="false"/>.
    /// </summary>
    internal static async Task<bool> AppendAggregationSummaryAsync(
        IServiceScopeFactory scopeFactory,
        RefusedAttemptStandaloneWriteOptions standaloneWrite,
        RefusedAttemptAggregationSummary summary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(standaloneWrite);
        ArgumentNullException.ThrowIfNull(summary);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var correlationId = RefusedAttemptAuditDraftMapper.AggregationSummaryCorrelationPrefix
                + Guid.NewGuid().ToString("N");
            var auditEvent = AuditEventFactory.Create(
                RefusedAttemptAuditDraftMapper.ToSummaryDraft(summary, correlationId));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(standaloneWrite.Timeout);
            await AppendInCompanyScopeAsync(scopeFactory, summary.Template.CompanyId, auditEvent, timeout.Token);
            return true;
        }
#pragma warning disable CA1031 // A lost summary is a metric, never an exception of the refusal path.
        catch (Exception)
#pragma warning restore CA1031
        {
            RefusedAttemptMetrics.RecordAggregationLost(summary.Template.RefusalClass, summary.Count);
            return false;
        }
    }

    private static async Task AppendInCompanyScopeAsync(
        IServiceScopeFactory scopeFactory,
        Guid companyId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await dbContext.AppendRefusedAttemptAsync(auditEvent, cancellationToken);
    }

    private static RefusedAttemptRecordResultV1 Fail(string? refusalClass, string failureKind)
    {
        RefusedAttemptMetrics.RecordWriteFailure(refusalClass, failureKind);
        return RefusedAttemptRecordResultV1.NotWritten(failureKind);
    }
}
