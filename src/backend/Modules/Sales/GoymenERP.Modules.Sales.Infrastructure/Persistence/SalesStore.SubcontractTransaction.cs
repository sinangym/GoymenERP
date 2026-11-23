using System.Data;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    private async Task<T> InSubcontractTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        // This is an owner command, not a borrowed writer. Pending changes or another transaction would
        // otherwise let a failed invoice command persist unrelated work or commit someone else's handles.
        if (dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("A subcontract invoice command requires its own clean Sales transaction.");
        var companyId = RequireResolvedCompany();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await action();
            if (RequireResolvedCompany() != companyId) throw new UnauthorizedAccessException();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is SqlException { Number: 1205 }
            || exception is DbUpdateException { InnerException: SqlException { Number: 1205 } }
            || exception is InvalidOperationException
                { InnerException: DbUpdateException { InnerException: SqlException { Number: 1205 } } })
        {
            // SQL Server has already rolled back a deadlock victim. A second rollback can mask the refusal.
            // EF can wrap that exact update failure in InvalidOperationException; it remains a refused race, not a retry.
            dbContext.ChangeTracker.Clear();
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Fatura veya tartı başka bir işlemle değişti; yeniden okuyun.", exception);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task RequireCurrentSubcontractWeighingAsync(SalesSubcontractServiceInvoice invoice, CancellationToken cancellationToken)
    {
        var owner = subcontractWeighingCommitRead ?? throw new SalesDraftStoreException(
            SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Tartının işlem içi sahibi kullanılamıyor.");
        var transaction = dbContext.Database.CurrentTransaction ?? throw new InvalidOperationException("The Sales transaction is required.");
        var source = invoice.Source;
        var answer = await owner.ReadAsync(dbContext.Database.GetDbConnection(), transaction.GetDbTransaction(), invoice.CompanyId,
            source.LoadingInstructionId, source.WeighingId, cancellationToken);
        RequireCompany(invoice.CompanyId);
        if (answer is null || answer.CompanyId != invoice.CompanyId || answer.LoadingInstructionId != source.LoadingInstructionId
            || answer.WeighingId != source.WeighingId || answer.Status != SalesSubcontractDeliveryWeighingStatusV1.Available || answer.Value is not { } value)
            throw new SalesDraftStoreException(SalesDraftProblemCodesV1.OwnerReferenceUnavailable, "Tartının güncel kaynağı doğrulanamadı.");
        if (value.WeighingRevision != source.WeighingRevision || value.InstructionVersion != source.InstructionVersion
            || !string.Equals(value.NetKg, source.NetKgText, StringComparison.Ordinal)
            || value.Lines is null || invoice.Lines.Any(line => !value.Lines.Any(own => own.SourceOrderId == source.SalesOrderId
                && own.SourceOrderLineId == line.Input.SalesOrderLineId && own.ProductId == line.Input.ProductId && own.UnitId == line.Input.UnitId)))
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Fatura hazırlanırken tartı veya sevk talimatı değişti; yeniden okuyun.");
    }
}
