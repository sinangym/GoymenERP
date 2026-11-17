using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed class AccountingSourceInvoiceCancellationStoreV1(AccountingDbContext db, ICompanyContext company,
    IAuditEventCollector audit, TimeProvider clock) : IAccountingSourceInvoiceCancellationStoreV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AccountingSourceInvoiceCancellationAnswerV1> CancelAsync(AccountingSourceInvoiceCancellationV1 request,
        AccountingCommandMetadata metadata, Func<CancellationToken, Task> freshAuthorityFence, CancellationToken cancellationToken = default)
    {
        if (!AccountingSourceInvoiceCancellationServiceV1.Valid(request)) throw Refuse("source_invoice_cancellation_invalid");
        if (metadata.ActorId != request.ActorId || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || metadata.CorrelationId != request.CorrelationId) throw Refuse("source_invoice_cancellation_invalid");
        RequireCompany(request.CompanyId);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { request.CompanyId, request.CancellationId,
            request.DocumentKind, request.SourceDocumentId, request.InvoiceId, request.FinalizedVersion, request.CancelledBy,
            request.CancelledAtUtc, request.Reason }, Json))));
        await freshAuthorityFence(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var previous = await db.SourceInvoiceCancellations.Where(x => x.SourceDocumentId == request.SourceDocumentId
                || x.CancellationId == request.CancellationId || (x.DocumentKind == request.DocumentKind
                && x.InvoiceId == request.InvoiceId && x.FinalizedVersion == request.FinalizedVersion)).ToListAsync(cancellationToken);
            if (previous.Count > 1) throw Refuse("source_invoice_cancellation_conflict");
            var prior = previous.SingleOrDefault();
            if (prior is not null)
            {
                if (prior.RequestHash != hash) throw Refuse("source_invoice_cancellation_conflict");
                await freshAuthorityFence(cancellationToken);
                RequireCompany(request.CompanyId);
                await transaction.CommitAsync(cancellationToken);
                return new("Recorded", true);
            }
            var admission = await db.SourceDocumentAdmissions.AsNoTracking().SingleOrDefaultAsync(x => x.SourceDocumentId == request.SourceDocumentId,
                cancellationToken);
            if (admission is not null && (admission.DocumentKind != request.DocumentKind || admission.InvoiceId != request.InvoiceId
                || admission.FinalizedVersion != request.FinalizedVersion)) throw Refuse("source_invoice_cancellation_conflict");
            var voucher = await db.SourceDocumentVoucherDrafts.SingleOrDefaultAsync(x => x.SourceDocumentId == request.SourceDocumentId, cancellationToken);
            if (voucher is not null && (voucher.DocumentKind != request.DocumentKind || voucher.InvoiceId != request.InvoiceId
                || voucher.FinalizedVersion != request.FinalizedVersion)) throw Refuse("source_invoice_cancellation_conflict");
            await freshAuthorityFence(cancellationToken);
            RequireCompany(request.CompanyId);
            db.SourceInvoiceCancellations.Add(new AccountingSourceInvoiceCancellationRow
            {
                CompanyId = request.CompanyId, CancellationId = request.CancellationId, DocumentKind = request.DocumentKind,
                SourceDocumentId = request.SourceDocumentId, InvoiceId = request.InvoiceId, FinalizedVersion = request.FinalizedVersion,
                CancelledBy = request.CancelledBy, CancelledAtUtc = request.CancelledAtUtc, Reason = request.Reason,
                RequestHash = hash, CorrelationId = request.CorrelationId, RecordedAtUtc = clock.GetUtcNow(),
            });
            var before = voucher?.Version;
            if (voucher?.Status == 1)
            {
                voucher.Status = 3;
                voucher.Version = checked(voucher.Version + 1);
                voucher.UpdatedAtUtc = clock.GetUtcNow();
            }
            // Posted entries remain intact. Reversal admission waits for its separately decided period/date rule.
            audit.Enqueue(new AuditEventDraft(request.CompanyId, "accounting.source-invoice.cancelled.v1", "Accounting.SourceVoucher",
                request.SourceDocumentId.ToString("D"), "CancelSource", before, voucher?.Version, AuditActorTypes.System,
                metadata.ActorId, metadata.PermissionCode, metadata.RoleCodes, "{}",
                JsonSerializer.Serialize(new { request.CancellationId, request.InvoiceId, voucherId = voucher?.Id,
                    status = voucher?.Status, reversalPending = voucher?.Status == 2 }, Json), request.Reason,
                JsonSerializer.Serialize(new { request.CancelledBy }, Json), AuditAiInvolvement.None, null,
                metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId,
                metadata.CausationId, metadata.IdempotencyKey));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new("Recorded");
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw Refuse(AccountingStoreCodes.ConcurrentChange);
        }
        catch (Exception exception) when (exception is SqlException { Number: 1205 }
            || exception is DbUpdateException { InnerException: SqlException { Number: 1205 } })
        {
            db.ChangeTracker.Clear();
            throw Refuse(AccountingStoreCodes.ConcurrentChange);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private void RequireCompany(Guid id)
    {
        if (!company.IsResolved || company.CompanyId != id || id == Guid.Empty) throw Refuse(AccountingStoreCodes.CompanyContextMismatch);
    }
    private static AccountingStoreException Refuse(string code) => new(code, "Kaynak fatura iptal kabulü tamamlanamadı.");
}
