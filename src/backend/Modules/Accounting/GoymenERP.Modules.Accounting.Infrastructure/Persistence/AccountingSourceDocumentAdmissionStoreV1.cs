using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// B-04b: the admission store of the source document acceptance port. Every write is ONE <c>SaveChanges</c> of the active
/// company: the admission row (the attempt record and, once final, the terminal refusal record), the draft voucher with
/// its lines when admitted, and the audit event. The draft goes only to Accounting's own tables; the open period that
/// covers the document date (if any) is stamped on it, otherwise it carries none ("dönem bekliyor", KLN-D15). A lost
/// race (the same key, the unique indexes or the row version) surfaces as <see cref="AccountingStoreCodes.ConcurrentChange"/>.
/// </summary>
public sealed class AccountingSourceDocumentAdmissionStoreV1(
    AccountingDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider)
    : IAccountingSourceDocumentAdmissionStoreV1
{
    private const int OpenPeriod = 1;
    private const int DraftStatus = 1;
    private const string EntityType = "Accounting.SourceDocumentAdmission";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<AccountingSourceDocumentAdmissionRecordV1?> FindAdmissionAsync(
        string keyHash,
        string documentKind,
        Guid invoiceId,
        long finalizedVersion,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        var rows = await dbContext.SourceDocumentAdmissions.AsNoTracking()
            .Where(row => row.IdempotencyKeyHash == keyHash
                || (row.DocumentKind == documentKind && row.InvoiceId == invoiceId && row.FinalizedVersion == finalizedVersion))
            .ToListAsync(cancellationToken);
        var found = rows.FirstOrDefault(row => string.Equals(row.IdempotencyKeyHash, keyHash, StringComparison.Ordinal))
            ?? rows.FirstOrDefault();
        return found is null ? null : Restore(found);
    }

    public async Task<AccountingSourceDocumentAdmissionRecordV1> WriteAsync(
        AccountingSourceDocumentAdmissionWriteV1 write,
        CancellationToken cancellationToken = default)
    {
        RequireCompany();
        ArgumentNullException.ThrowIfNull(write);
        RequireMetadata(write.Metadata);
        var request = write.Request ?? throw new ArgumentException("A request is required.", nameof(write));
        if (request.CompanyId != companyContext.CompanyId)
        {
            throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch,
                "The admission is not for the active company.");
        }

        if (write.ExpectedVersion < 0 || write.KeyHash is not { Length: 64 } || write.RequestHash is not { Length: 64 }
            || string.IsNullOrWhiteSpace(write.ReasonCode)
            || (write.Status == AccountingSourceDocumentAdmissionStatusV1.Admitted) != (write.Draft is not null))
        {
            throw new ArgumentException("A consistent admission write is required.", nameof(write));
        }

        var now = timeProvider.GetUtcNow();
        var companyId = companyContext.CompanyId;
        var existing = await dbContext.SourceDocumentAdmissions
            .SingleOrDefaultAsync(row => row.SourceDocumentId == request.SourceDocumentId, cancellationToken);
        AccountingSourceDocumentAdmissionRow row;
        string before;
        long? versionBefore;
        if (write.ExpectedVersion == 0)
        {
            if (existing is not null)
            {
                throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange,
                    "The source document was admitted by another write first; nothing was stored.");
            }

            row = new AccountingSourceDocumentAdmissionRow
            {
                CompanyId = companyId,
                SourceDocumentId = request.SourceDocumentId,
                DocumentKind = request.DocumentKind,
                InvoiceId = request.InvoiceId,
                FinalizedVersion = request.FinalizedVersion,
                IdempotencyKeyHash = write.KeyHash,
                RequestHash = write.RequestHash,
                SourceActorId = request.SourceActorId,
                Version = 1,
                AttemptCount = 1,
                FirstAttemptedAtUtc = now,
                CreatedAtUtc = now,
            };
            dbContext.SourceDocumentAdmissions.Add(row);
            before = "{}";
            versionBefore = null;
        }
        else
        {
            row = existing ?? throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange,
                "The waiting admission is gone; nothing was stored.");
            if (row.Version != write.ExpectedVersion
                || row.Status != (int)AccountingSourceDocumentAdmissionStatusV1.Waiting)
            {
                throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange,
                    "The admission changed first; nothing was stored.");
            }

            if (!string.Equals(row.RequestHash, write.RequestHash, StringComparison.Ordinal)
                || !string.Equals(row.IdempotencyKeyHash, write.KeyHash, StringComparison.Ordinal))
            {
                throw new AccountingStoreException(AccountingSourceDocumentStoreCodesV1.ContentConflict,
                    "The key was used with other document content.");
            }

            before = AuditJson(row);
            versionBefore = row.Version;
            row.Version += 1;
            row.AttemptCount = checked(row.AttemptCount + 1);
        }

        row.Status = (int)write.Status;
        row.ReasonCode = write.ReasonCode;
        row.LastAttemptedAtUtc = now;
        row.CorrelationId = write.Metadata.CorrelationId;
        row.UpdatedAtUtc = now;
        if (write.Draft is { } draft)
        {
            row.VoucherDraftId = draft.DraftId;
            await AddDraftAsync(request, draft, now, cancellationToken);
        }

        var (eventType, operation, reason) = write.Status switch
        {
            AccountingSourceDocumentAdmissionStatusV1.Admitted => ("accounting.source-document-admission.admitted.v1",
                "Admit", "Kaynak belge icin taslak fis olusturuldu"),
            AccountingSourceDocumentAdmissionStatusV1.Waiting => ("accounting.source-document-admission.waiting.v1",
                "Wait", "Kaynak belge hesap eslemesi bekliyor"),
            _ => ("accounting.source-document-admission.refused.v1", "Refuse", "Kaynak belge kabul edilmedi"),
        };
        Audit(write.Metadata, row, eventType, operation, versionBefore, before, AuditJson(row), reason);
        await SaveAsync(cancellationToken);
        return Restore(row);
    }

    private async Task AddDraftAsync(
        AccountingSourceDocumentAdmissionRequestV1 request,
        AccountingSourceDocumentVoucherDraftV1 draft,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var companyId = companyContext.CompanyId;
        var periodId = await dbContext.Periods.AsNoTracking()
            .Where(period => period.Status == OpenPeriod && period.StartsOn <= request.DocumentDate
                && period.EndsOn >= request.DocumentDate)
            .Select(period => (Guid?)period.Id)
            .FirstOrDefaultAsync(cancellationToken);
        _ = AccountingSourceDocumentAmountsV1.TryParse(request.DocumentTotals.Net, out var documentNet);
        _ = AccountingSourceDocumentAmountsV1.TryParse(request.DocumentTotals.Vat, out var documentVat);
        _ = AccountingSourceDocumentAmountsV1.TryParse(request.DocumentTotals.Withholding, out var documentWithholding);
        _ = AccountingSourceDocumentAmountsV1.TryParse(request.DocumentTotals.Payable, out var documentPayable);
        _ = AccountingSourceDocumentAmountsV1.TryParse(request.FunctionalTotals.Net, out var functionalNet);
        _ = AccountingSourceDocumentAmountsV1.TryParse(request.FunctionalTotals.Vat, out var functionalVat);
        _ = AccountingSourceDocumentAmountsV1.TryParse(request.FunctionalTotals.Withholding, out var functionalWithholding);
        _ = AccountingSourceDocumentAmountsV1.TryParse(request.FunctionalTotals.Payable, out var functionalPayable);
        dbContext.SourceDocumentVoucherDrafts.Add(new AccountingSourceDocumentVoucherDraftRow
        {
            CompanyId = companyId,
            Id = draft.DraftId,
            DocumentKind = request.DocumentKind,
            SourceDocumentId = request.SourceDocumentId,
            InvoiceId = request.InvoiceId,
            FinalizedVersion = request.FinalizedVersion,
            SourceActorId = request.SourceActorId,
            PartyId = request.PartyId,
            CurrencyId = request.CurrencyId,
            ExchangeRate = request.ExchangeRate,
            DocumentDate = request.DocumentDate,
            DueDate = request.DueDate,
            PeriodId = periodId,
            MappingVersion = draft.MappingVersion,
            Status = DraftStatus,
            DocumentNet = documentNet,
            DocumentVat = documentVat,
            DocumentWithholding = documentWithholding,
            DocumentPayable = documentPayable,
            FunctionalNet = functionalNet,
            FunctionalVat = functionalVat,
            FunctionalWithholding = functionalWithholding,
            FunctionalPayable = functionalPayable,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        dbContext.SourceDocumentVoucherDraftLines.AddRange(draft.Lines.Select(line =>
            new AccountingSourceDocumentVoucherDraftLineRow
            {
                CompanyId = companyId,
                DraftId = draft.DraftId,
                LineNumber = line.LineNumber,
                MappingKey = line.MappingKey,
                Side = line.Side,
                AccountId = line.AccountId,
                Amount = line.Amount,
                SourceLineNumber = line.SourceLineNumber,
                VatRateCode = line.VatRateCode,
            }));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange,
                "Another write changed the same admission first; nothing was stored.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new AccountingStoreException(AccountingStoreCodes.ConcurrentChange,
                "The same key or document was admitted by another write first; nothing was stored.", exception);
        }
    }

    private void Audit(
        AccountingCommandMetadata metadata,
        AccountingSourceDocumentAdmissionRow row,
        string eventType,
        string operation,
        long? versionBefore,
        string before,
        string after,
        string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(
            row.CompanyId,
            eventType,
            EntityType,
            row.SourceDocumentId.ToString("D"),
            operation,
            versionBefore,
            row.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            before,
            after,
            reason,
            null,
            AuditAiInvolvement.None,
            null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

    private void RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch,
                "Source document admissions require the active company context.");
        }
    }

    private static void RequireMetadata(AccountingCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.ActorId) || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || metadata.RoleCodes is null)
        {
            throw new ArgumentException("Admission writes require actor, permission, correlation and idempotency metadata.",
                nameof(metadata));
        }
    }

    private static AccountingSourceDocumentAdmissionRecordV1 Restore(AccountingSourceDocumentAdmissionRow row) =>
        new AccountingSourceDocumentAdmissionRecordV1(row.SourceDocumentId, row.DocumentKind, row.InvoiceId, row.FinalizedVersion, row.IdempotencyKeyHash,
            row.RequestHash, (AccountingSourceDocumentAdmissionStatusV1)row.Status, row.ReasonCode, row.VoucherDraftId,
            row.AttemptCount, row.Version, row.CorrelationId);

    private static string AuditJson(AccountingSourceDocumentAdmissionRow row) =>
        JsonSerializer.Serialize(new
        {
            row.SourceDocumentId,
            row.DocumentKind,
            row.InvoiceId,
            row.FinalizedVersion,
            status = ((AccountingSourceDocumentAdmissionStatusV1)row.Status).ToString(),
            row.ReasonCode,
            row.VoucherDraftId,
            row.AttemptCount,
            row.Version,
            row.SourceActorId,
        }, Json);
}
