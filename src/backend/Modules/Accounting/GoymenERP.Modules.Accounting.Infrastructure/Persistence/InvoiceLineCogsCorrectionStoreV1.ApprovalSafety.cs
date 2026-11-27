using GoymenERP.Modules.Accounting.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class InvoiceLineCogsCorrectionStoreV1
{
    public Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> RunApprovalAsync(
        Func<bool, Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>> attempt,
        Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default) =>
        approvalSafetyOwner is null
            ? Task.FromResult(new AccountingApiResult<InvoiceLineCogsCorrectionViewV1>(AccountingApiStatus.Unavailable, null, false, null))
            : approvalSafetyOwner.RunInvoiceLineCogsApprovalAsync(this, db, audit, auditWriter, attempt, freshFence, cancellationToken);
    private bool IsOwnedApproval => approvalSafetyOwner?.IsOwnedInvoiceLineCogsApproval(this, db, audit, auditWriter) == true;
    // Only the exact outer approval marker owns these extra fences. Prepare/read/cancellation keep their existing protocol.
    private Task ApprovalReadFenceAsync(CancellationToken token) => IsOwnedApproval
        ? approvalSafetyOwner!.InvoiceLineCogsApprovalFenceAsync(this, db, audit, auditWriter, token) : Task.CompletedTask;
    private async Task<T> ApprovalReadAsync<T>(Func<Task<T>> read, CancellationToken token)
    {
        await ApprovalReadFenceAsync(token);
        var answer = await read();
        await ApprovalReadFenceAsync(token);
        return answer;
    }
    private async Task ApprovalReadAsync(Func<Task> read, CancellationToken token)
    {
        await ApprovalReadFenceAsync(token);
        await read();
        await ApprovalReadFenceAsync(token);
    }
    private async Task SaveOwnedApprovalCommandAsync(AccountingCommandRecord command, CancellationToken token)
    {
        try
        {
            approvalSafetyOwner!.CompleteOwnedInvoiceLineCogsApproval(this, db, audit, auditWriter, command);
            await approvalSafetyOwner.SaveOwnedInvoiceLineCogsApprovalAsync(this, db, audit, auditWriter, token);
        }
        catch (DbUpdateConcurrencyException error) { throw new NormalJournalApprovalRaceException(AccountingStoreCodes.ConcurrentChange, error); }
        catch (DbUpdateException error) when (error.GetBaseException() is SqlException { Number: 2601 or 2627 })
        { throw new NormalJournalApprovalRaceException(AccountingStoreCodes.ConcurrentChange, error); }
    }
    private async Task<InvoiceLineCogsCorrectionViewV1> MutateOwnedApprovalAsync(FinalizedInvoiceLineSourceV1 source,
        Func<Task<InvoiceLineCogsCorrectionViewV1>> operation, AccountingCommandMetadata metadata, CancellationToken token)
    {
        try
        {
            await ApprovalReadFenceAsync(token); RequireCompany(source.CompanyId);
            if (await ApprovalReadAsync(() => db.SourceInvoiceCancellations.AsNoTracking().AnyAsync(
                    x => x.SourceDocumentId == source.FinalizationAdmissionId, token), token))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            var frozen = Encode(source);
            if (await ApprovalReadAsync(() => db.InvoiceLineCogsProposals.AnyAsync(x => x.InvoiceId == source.InvoiceId
                    && x.InvoiceLineId == source.InvoiceLineId && x.SourceJson != frozen, token), token))
                throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceChanged);
            var result = await ApprovalReadAsync(operation, token);
            RequireCompany(source.CompanyId);
            var command = metadata.Completion?.Invoke(result) ?? throw new InvalidOperationException("Owned invoice-line approval requires its exact native command result.");
            await SaveOwnedApprovalCommandAsync(command, token);
            return result;
        }
        catch (DbUpdateConcurrencyException error) { throw new NormalJournalApprovalRaceException(AccountingStoreCodes.ConcurrentChange, error); }
        catch (DbUpdateException error) when (error.GetBaseException() is SqlException { Number: 2601 or 2627 })
        { throw new NormalJournalApprovalRaceException(AccountingStoreCodes.ConcurrentChange, error); }
        catch (DbUpdateException error) when (error.InnerException is not SqlException { Number: 1205 })
        { throw Refuse(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable); }
    }
}
