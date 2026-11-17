using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// Price-free current Sales owner facts for the host's existing Serializable transaction.
/// Uses the real aggregate reconstruction and accepted-revision rules. Only the read context
/// is owned here; disposal never commits, rolls back or closes the caller's handles.
/// </summary>
public sealed class SalesSubcontractServiceOrderReadSession : ISalesSubcontractServiceOrderReadPortV1, IAsyncDisposable
{
    private readonly SalesDbContext context;
    private readonly SalesSubcontractServiceOrderReadPort reader;
    private readonly SalesOrderLineReferencePort loadingReader;
    private readonly ICompanyContext company;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly Guid companyId;
    private bool disposed;

    private SalesSubcontractServiceOrderReadSession(SalesDbContext context, ICompanyContext company,
        DbConnection connection, DbTransaction transaction)
    {
        this.context = context; this.company = company; this.connection = connection;
        this.transaction = transaction; companyId = company.CompanyId;
        var store = new SalesStore(context, company, new ReadOnlyAuditCollector(), TimeProvider.System);
        reader = new(store, company); loadingReader = new(store, company);
    }

    public static async Task<SalesSubcontractServiceOrderReadSession> EnlistAsync(DbConnection connection,
        DbTransaction transaction, ICompanyContext company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(company);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction.Connection, connection) || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("The Sales source requires the resolved company's open Serializable transaction.");
        var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            var session = new SalesSubcontractServiceOrderReadSession(context, company, connection, transaction);
            if (!session.Active()) throw new InvalidOperationException("The borrowed Sales source transaction is unavailable.");
            return session;
        }
        catch { await context.DisposeAsync(); throw; }
    }

    public async Task<SalesSubcontractServiceOrderAnswerV1> GetAsync(Guid companyId, Guid orderId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); cancellationToken.ThrowIfCancellationRequested();
        if (companyId != this.companyId || !Active()) return Unavailable();
        try
        {
            var answer = await reader.GetAsync(companyId, orderId, cancellationToken);
            return Active() ? answer : Unavailable();
        }
        catch (OperationCanceledException) { throw; }
        catch { return Unavailable(); }
    }

    /// <summary>The existing approved/remainder/reapproval-cap owner rule in the same borrowed transaction.
    /// The action-purpose host measures actor/permission before and after this authority-free read.</summary>
    public async Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineForLoadingAsync(Guid companyId, Guid orderId, Guid lineId,
        DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); cancellationToken.ThrowIfCancellationRequested();
        if (companyId != this.companyId || !Active()) throw new InvalidOperationException("The Sales loading source transaction or company is unavailable.");
        var answer = await loadingReader.GetApprovedLineForLoadingAsync(orderId, lineId, effectiveOn, cancellationToken);
        if (!Active() || answer.Line is { } line && (line.CompanyId != companyId || line.OrderId != orderId || line.LineId != lineId))
            throw new InvalidOperationException("The Sales loading source changed scope.");
        return answer;
    }

    private bool Active()
    {
        try
        {
            return !disposed && company.IsResolved && company.CompanyId == companyId
                && connection.State == ConnectionState.Open && ReferenceEquals(transaction.Connection, connection)
                && transaction.IsolationLevel == IsolationLevel.Serializable
                && context.Database.CurrentTransaction is { } current
                && ReferenceEquals(current.GetDbTransaction(), transaction) && !context.ChangeTracker.HasChanges();
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { return false; }
    }

    private static SalesSubcontractServiceOrderAnswerV1 Unavailable() => new(
        SalesSubcontractServiceOrderOutcomeV1.Unreadable, null, SalesSubcontractServiceOrderUnreadableReasonsV1.StoreUnavailable);

    public async ValueTask DisposeAsync()
    { if (!disposed) { disposed = true; await context.DisposeAsync(); } }

    private sealed class ReadOnlyAuditCollector : IAuditEventCollector
    { public void Enqueue(AuditEventDraft draft) => throw new InvalidOperationException("A Sales source read cannot mutate audit or state."); }
}
