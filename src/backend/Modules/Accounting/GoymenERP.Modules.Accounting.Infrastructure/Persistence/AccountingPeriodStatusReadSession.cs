using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Domain.Periods;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>Accounting-owned read of one date in a caller-owned Serializable SQL transaction. The shared/range locks
/// survive disposal until that caller commits or rolls back, so a checked open period cannot close in the write gap.
/// This type grants no authority, exposes no writer, and never completes or owns either borrowed SQL handle.</summary>
public sealed class AccountingPeriodStatusReadSession : IAsyncDisposable
{
    private readonly AccountingDbContext db;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly ICompanyContext company;
    private readonly Guid companyId;
    private bool disposed;

    private AccountingPeriodStatusReadSession(AccountingDbContext db, DbConnection connection,
        DbTransaction transaction, ICompanyContext company, Guid companyId)
    { this.db = db; this.connection = connection; this.transaction = transaction; this.company = company; this.companyId = companyId; }

    public static async Task<AccountingPeriodStatusReadSession> EnlistAsync(DbConnection connection, DbTransaction transaction,
        ICompanyContext company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(company); cancellationToken.ThrowIfCancellationRequested();
        if (!company.IsResolved || company.CompanyId == Guid.Empty) throw new UnauthorizedAccessException();
        var companyId = company.CompanyId;
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("An Accounting period fence requires the caller's active Serializable SQL transaction.");
        var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var session = new AccountingPeriodStatusReadSession(db, connection, transaction, company, companyId);
            session.RequireActive(); return session;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public async Task<AccountingPeriodStatusAnswerV1> ReadAsync(DateOnly on, CancellationToken cancellationToken = default)
    {
        RequireActive();
        if (on == default) throw new ArgumentOutOfRangeException(nameof(on));
        var period = await db.Periods.AsNoTracking()
            .SingleOrDefaultAsync(row => row.StartsOn <= on && row.EndsOn >= on, cancellationToken);
        RequireActive();
        if (period is null) return new AccountingPeriodStatusAnswerV1(AccountingPeriodStatusOutcomeV1.None, null, null);
        if (period.CompanyId != companyId) throw new UnauthorizedAccessException();
        var status = (AccountingPeriodStatus)period.Status;
        if (!Enum.IsDefined(status)) throw new InvalidOperationException("The Accounting period status is unreadable.");
        return new AccountingPeriodStatusAnswerV1(status == AccountingPeriodStatus.Open ? AccountingPeriodStatusOutcomeV1.Open : AccountingPeriodStatusOutcomeV1.NotOpen,
            period.Id, status.ToString());
    }

    private void RequireActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!company.IsResolved || company.CompanyId != companyId) throw new UnauthorizedAccessException();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable || db.Database.CurrentTransaction is not { } current
            || !ReferenceEquals(current.GetDbTransaction(), transaction) || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("The borrowed Accounting period-fence transaction is no longer active.");
    }

    public async ValueTask DisposeAsync()
    { if (disposed) return; disposed = true; await db.DisposeAsync(); }
}
