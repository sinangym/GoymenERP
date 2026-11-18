using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;

/// <summary>Dispose-only, price-free Manufacturing owner read in the host transaction; no other module table is accessed.</summary>
public sealed class ManufacturingSubcontractCompletionReadSessionV2 : IManufacturingSubcontractCompletionReadPortV2, IAsyncDisposable
{
    private readonly ManufacturingDbContext db;
    private readonly ManufacturingSubcontractCompletionReadPortV2 reader;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly ICompanyContext company;
    private readonly Guid companyId;
    private bool disposed;

    private ManufacturingSubcontractCompletionReadSessionV2(ManufacturingDbContext db, DbConnection connection,
        DbTransaction transaction, ICompanyContext company, Guid companyId)
    {
        this.db = db; this.connection = connection; this.transaction = transaction; this.company = company; this.companyId = companyId;
        reader = new ManufacturingSubcontractCompletionReadPortV2(db, company, TimeProvider.System);
    }

    public static async Task<ManufacturingSubcontractCompletionReadSessionV2> EnlistAsync(DbConnection connection,
        DbTransaction transaction, ICompanyContext company, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction); ArgumentNullException.ThrowIfNull(company);
        token.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction.Connection, connection) || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Completion proofs require the resolved company's active Serializable transaction.");
        var companyId = company.CompanyId;
        var db = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await db.Database.UseTransactionAsync(transaction, token);
            if (!company.IsResolved || company.CompanyId != companyId || connection.State != ConnectionState.Open
                || !ReferenceEquals(transaction.Connection, connection) || transaction.IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Completion proof scope changed while enlisting the owner read.");
            return new ManufacturingSubcontractCompletionReadSessionV2(db, connection, transaction, company, companyId);
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public async Task<ManufacturingSubcontractCompletionReadV2> GetAsync(Guid companyId, Guid orderId, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); token.ThrowIfCancellationRequested();
        var closed = new ManufacturingSubcontractCompletionReadV2(ManufacturingSubcontractCompletionOutcomeV1.Unavailable, companyId, orderId, null);
        if (!Active(companyId)) return closed;
        var answer = await reader.GetAsync(companyId, orderId, token);
        return Active(companyId) ? answer : closed;
    }

    public async Task<ManufacturingSubcontractCompletionPageV2> ReadBySalesLineAsync(Guid companyId, Guid orderId,
        Guid lineId, int pageIndex, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this); token.ThrowIfCancellationRequested();
        var closed = new ManufacturingSubcontractCompletionPageV2(ManufacturingSubcontractCompletionOutcomeV1.Unavailable,
            companyId, orderId, lineId, pageIndex, false, []);
        if (!Active(companyId)) return closed;
        var answer = await reader.ReadBySalesLineAsync(companyId, orderId, lineId, pageIndex, token);
        return Active(companyId) ? answer : closed;
    }

    private bool Active(Guid requested)
    {
        try
        {
            return requested == companyId && company.IsResolved && company.CompanyId == companyId
                && connection.State == ConnectionState.Open && ReferenceEquals(transaction.Connection, connection)
                && transaction.IsolationLevel == IsolationLevel.Serializable && db.Database.CurrentTransaction is { } current
                && ReferenceEquals(current.GetDbTransaction(), transaction) && !db.ChangeTracker.HasChanges();
        }
        catch (InvalidOperationException)
        {
            // A completed/disposed caller transaction is unreadable, never an authorization or empty-source success.
            return false;
        }
    }

    public async ValueTask DisposeAsync() { if (!disposed) { disposed = true; await db.DisposeAsync(); } }
}
