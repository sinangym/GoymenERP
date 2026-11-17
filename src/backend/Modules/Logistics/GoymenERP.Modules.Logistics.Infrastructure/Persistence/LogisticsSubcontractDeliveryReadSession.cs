using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// Read-only owner session for the caller's already open transaction. Used AFTER the Sales invoice range lock,
/// before any Logistics correction write. Never commits, rolls back, closes or owns the borrowed handles.
/// Reuses the real invariant-checking aggregate rebuild and the one delivery-weighing projection.
/// </summary>
public sealed class LogisticsSubcontractDeliveryReadSession : ILogisticsSubcontractDeliveryNetWeightReadPortV1, IAsyncDisposable
{
    private readonly LogisticsDbContext context;
    private readonly LogisticsSubcontractDeliveryNetWeightReadPort reader;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly Guid companyId;
    private bool disposed;

    private LogisticsSubcontractDeliveryReadSession(LogisticsDbContext context, CompanyContext company,
        DbConnection connection, DbTransaction transaction, Guid companyId)
    {
        this.context = context; this.connection = connection; this.transaction = transaction; this.companyId = companyId;
        reader = new(new LogisticsStore(context, company, new ReadOnlyAuditCollector(), TimeProvider.System), company);
    }

    public static async Task<LogisticsSubcontractDeliveryReadSession> EnlistAsync(DbConnection connection,
        DbTransaction transaction, Guid companyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("Delivery weighing reads require the owner's open company transaction.");
        var company = new CompanyContext(); company.Set(companyId);
        var context = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            return new(context, company, connection, transaction, companyId);
        }
        catch { await context.DisposeAsync(); throw; }
    }

    public async Task<LogisticsSubcontractDeliveryNetWeightReadV1> GetAsync(Guid companyId, Guid loadingInstructionId,
        Guid weighingId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); cancellationToken.ThrowIfCancellationRequested();
        if (companyId != this.companyId || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || context.Database.CurrentTransaction is not { } current || !ReferenceEquals(current.GetDbTransaction(), transaction))
            return LogisticsSubcontractDeliveryNetWeightReadV1.Unavailable(companyId, loadingInstructionId, weighingId,
                LogisticsSubcontractDeliveryNetWeightReasonCodesV1.RecordUnreadable);
        try { return await reader.GetAsync(companyId, loadingInstructionId, weighingId, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { return LogisticsSubcontractDeliveryNetWeightReadV1.Unavailable(companyId, loadingInstructionId, weighingId,
            LogisticsSubcontractDeliveryNetWeightReasonCodesV1.RecordUnreadable); }
    }

    public async ValueTask DisposeAsync() { if (!disposed) { disposed = true; await context.DisposeAsync(); } }
    private sealed class ReadOnlyAuditCollector : IAuditEventCollector
    { public void Enqueue(AuditEventDraft auditEvent) => throw new InvalidOperationException("The delivery reader cannot stage audit or mutations."); }
}
