using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>
/// ALS-D62: Finance's single-use participant in the producer's already-open invoice-finalisation transaction (the goods
/// receipt → Inventory participant pattern). It stages and flushes the open items, the admission receipt and the audit
/// event on the host's connection but never commits, rolls back or closes the host's connection or transaction.
/// </summary>
public sealed class FinanceInvoiceOpenItemAdmissionParticipant : IFinanceInvoiceOpenItemAdmissionParticipantV1
{
    private readonly FinanceDbContext dbContext;
    private readonly FinanceInvoiceOpenItemAdmissionService service;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private int started;
    private bool disposed;

    private FinanceInvoiceOpenItemAdmissionParticipant(FinanceDbContext dbContext,
        FinanceInvoiceOpenItemAdmissionService service, DbConnection connection, DbTransaction transaction)
    {
        this.dbContext = dbContext;
        this.service = service;
        this.connection = connection;
        this.transaction = transaction;
    }

    public static async Task<FinanceInvoiceOpenItemAdmissionParticipant> EnlistAsync(DbConnection connection,
        DbTransaction transaction, Guid companyId, ICurrentUser currentUser, IFinancePartyAccountPort parties,
        FinanceAuditEnvelope envelope, CancellationToken cancellationToken = default, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(parties);
        ArgumentNullException.ThrowIfNull(envelope);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException("Finance requires a company and the owner's open transaction.");
        }

        var company = new CompanyContext();
        company.Set(companyId);
        var audit = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company))
            .Options;
        var dbContext = new FinanceDbContext(options, company);
        try
        {
            await dbContext.Database.UseTransactionAsync(transaction, cancellationToken);
            var service = new FinanceInvoiceOpenItemAdmissionService(dbContext, company, currentUser, parties, audit,
                envelope, clock ?? TimeProvider.System);
            return new FinanceInvoiceOpenItemAdmissionParticipant(dbContext, service, connection, transaction);
        }
        catch
        {
            await dbContext.DisposeAsync();
            throw;
        }
    }

    public Task<FinanceOpenItemAdmissionResult> StageAsync(FinanceContext context, CommandMetadata metadata,
        FinanceInvoiceOpenItemAdmissionV1 invoice, CancellationToken cancellationToken)
    {
        EnsureActive();
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("A Finance participant can execute only one admission attempt.");
        }

        return service.AdmitAsync(context, metadata, invoice, cancellationToken);
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || dbContext.Database.CurrentTransaction is not { } enlisted
            || !ReferenceEquals(enlisted.GetDbTransaction(), transaction))
        {
            throw new InvalidOperationException("The owner's Finance transaction is no longer active.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await dbContext.DisposeAsync();
    }
}
