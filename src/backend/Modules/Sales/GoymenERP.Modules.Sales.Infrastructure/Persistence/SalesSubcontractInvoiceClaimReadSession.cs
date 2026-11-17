using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// SAT-D378: price-free Sales owner evidence in the caller's Serializable transaction. The unfiltered-by-state
/// company/instruction/weighing range is read before the Logistics weighing is locked or changed. Even an empty
/// range remains locked until the caller completes its transaction. Invoice creation and lifecycle writes take
/// this same range first. Disposing this session never completes or owns the borrowed transaction/connection.
/// </summary>
public sealed class SalesSubcontractInvoiceClaimReadSession : IAsyncDisposable
{
    private readonly SalesDbContext db;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly ICompanyContext company;
    private readonly Guid companyId;
    private bool disposed;

    private SalesSubcontractInvoiceClaimReadSession(SalesDbContext db, DbConnection connection,
        DbTransaction transaction, ICompanyContext company, Guid companyId)
    { this.db = db; this.connection = connection; this.transaction = transaction; this.company = company; this.companyId = companyId; }

    public static async Task<SalesSubcontractInvoiceClaimReadSession> EnlistAsync(DbConnection connection,
        DbTransaction transaction, ICompanyContext company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(company); cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty) throw new UnauthorizedAccessException();
        var companyId = company.CompanyId;
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("A Sales invoice claim requires the caller's active Serializable SQL transaction.");
        var db = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var session = new SalesSubcontractInvoiceClaimReadSession(db, connection, transaction, company, companyId);
            session.RequireActive(); return session;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public async Task<SalesSubcontractInvoiceClaimV1> LockAndReadAsync(Guid instructionId, Guid weighingId,
        CancellationToken cancellationToken = default)
    {
        RequireActive();
        if (instructionId == Guid.Empty || weighingId == Guid.Empty) throw new ArgumentException("The instruction and weighing identities are required.");
        var result = await ReadRangeAsync(db, companyId, instructionId, weighingId, cancellationToken);
        RequireActive(); return result;
    }

    internal static async Task<SalesSubcontractInvoiceClaimV1> ReadRangeAsync(SalesDbContext db, Guid companyId,
        Guid instructionId, Guid weighingId, CancellationToken cancellationToken)
    {
        // No state predicate: rejected/cancelled histories still contribute to the source version. SQL Server's
        // Serializable range protects both existing state changes and insertion into an initially empty range.
        var rows = await db.SubcontractServiceInvoices.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.LoadingInstructionId == instructionId && row.WeighingId == weighingId)
            .Select(row => new { row.Id, row.State, row.Version }).ToListAsync(cancellationToken);
        if (rows.Any(row => row.Id == Guid.Empty || row.Version < 1 || row.State is < 1 or > 6))
            throw new InvalidOperationException("The Sales invoice claim source is invalid.");
        var ordered = rows.OrderBy(row => row.Id).ToArray();
        var proof = new StringBuilder().Append(companyId.ToString("D")).Append('|')
            .Append(instructionId.ToString("D")).Append('|').Append(weighingId.ToString("D"));
        foreach (var row in ordered)
            proof.Append('|').Append(row.Id.ToString("D")).Append(':').Append(row.State.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(row.Version.ToString(CultureInfo.InvariantCulture));
        var active = ordered.Where(row => row.State is 1 or 2 or 3 or 6).Select(row => row.Id).ToArray();
        return new(companyId, instructionId, weighingId, SalesSubcontractInvoiceClaimStatusV1.Available,
            active.Length != 0, active, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof.ToString()))));
    }

    private void RequireActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!company.IsResolved || company.CompanyId != companyId) throw new UnauthorizedAccessException();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable || db.Database.CurrentTransaction is not { } current
            || !ReferenceEquals(current.GetDbTransaction(), transaction) || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("The borrowed Sales invoice-claim transaction is no longer active.");
    }

    public async ValueTask DisposeAsync()
    { if (disposed) return; disposed = true; await db.DisposeAsync(); }
}
