using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed record FinanceOfficialInvoiceImportAuditEnvelopesV1(
    FinanceAuditEnvelope Upload,
    FinanceAuditEnvelope Approve);

/// <summary>
/// Persistent producer for the approved official invoice/installment source. Upload and approval are
/// separate append-only facts. Only a different authorized actor's approval makes the batch visible
/// through the existing approved-source ports; pending data is never returned by those ports.
/// </summary>
public sealed class FinanceOfficialInvoiceImportApprovalServiceV1(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceOfficialInvoiceImportParserV1 parser,
    IFinanceOfficialInvoiceImportApprovalAuthorityV1 commandAuthority,
    IFinanceOpenItemSourceAuthority readAuthority,
    IAuditEventCollector auditEvents,
    FinanceOfficialInvoiceImportAuditEnvelopesV1 envelopes,
    TimeProvider timeProvider)
    : IFinanceOfficialInvoiceImportApprovalServiceV1,
      IApprovedFinanceOpenItemSourcePort,
      IApprovedFinanceOpenItemSourceCatalogPort,
      IFinanceOfficialInvoiceImportWorkObservationSourceV1
{
    public const string UploadCommand = "finance.official-invoice-import.upload";
    public const string ApproveCommand = "finance.official-invoice-import.approve";
    private const string EntityType = "Finance.OfficialInvoiceImportBatch";
    private const int IdentityMaximumLength = 128;
    private const int PageSize = 50;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinanceOfficialInvoiceImportApprovalResultV1> UploadAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialInvoiceImportPreviewRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Upload, request.BatchId,
                cancellationToken) is { } denied)
        {
            return denied;
        }

        if (!ValidIdentity(metadata.IdempotencyKey) || !ValidIdentity(context.ActorId)
            || !ValidIdentity(context.CorrelationId))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("context", "finance_value_does_not_fit"));
        }
        if (request.BatchId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("batchId", "identity_required"));
        }
        if (!ValidIdentity(request.BatchVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("batchVersion", "finance_open_item_batch_version_invalid"));
        }

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = UploadPayloadHash(request);
        if (await PriorAsync(context, UploadCommand, FinanceOfficialInvoiceImportAuthorityActionV1.Upload,
                request.BatchId, keyHash, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var preview = parser.Preview(context, request);
        if (!preview.IsValid || preview.Batch is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                [.. preview.Issues.Select(issue => Issue($"rows[{issue.RowNumber}].{issue.Field}", issue.Code))]);
        }

        var existing = await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.BatchId, cancellationToken);
        if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Upload, request.BatchId,
                cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (existing is not null)
        {
            return Refused(context, CommandOutcome.IdempotencyConflict, Issue("batchId", "batch_already_exists"));
        }

        var now = timeProvider.GetUtcNow();
        var row = new FinanceOfficialInvoiceImportBatchRow
        {
            Id = preview.Batch.BatchId,
            CompanyId = context.CompanyId,
            BatchVersion = preview.Batch.BatchVersion,
            BatchChecksum = preview.Batch.BatchChecksum,
            TemplateVersion = preview.TemplateVersion,
            FileBytes = request.Utf8Csv.ToArray(),
            SourceRowsJson = JsonSerializer.Serialize(preview.Batch.Rows, Json),
            RowCount = preview.Batch.Rows.Count,
            UploadedBy = context.ActorId,
            UploadedAtUtc = now,
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
        };
        var result = Succeeded(context, View(row, approval: null));
        dbContext.OfficialInvoiceImportBatches.Add(row);
        var command = AddCommand(context, UploadCommand, metadata, keyHash, payloadHash, row.Id, result, now);
        dbContext.OfficialInvoiceImportWorkDeliveries.Add(
            FinanceOfficialInvoiceImportWorkObservationFactoryV1.ToRow(
                FinanceOfficialInvoiceImportWorkObservationFactoryV1.CreateUpload(command, row)));

        if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Upload, row.Id, cancellationToken)
                is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return beforeSave;
        }

        Audit(context, metadata, row.Id, "finance.official-invoice-import.uploaded.v1", "Upload", null, 1,
            "{}", AuditJson(row, null), envelopes.Upload, "Resmî fatura/taksit dosyası onay beklemek üzere yüklendi");
        return await SaveAsync(context, UploadCommand, FinanceOfficialInvoiceImportAuthorityActionV1.Upload,
            row.Id, keyHash, payloadHash, cancellationToken) ?? result;
    }

    public async Task<FinanceOfficialInvoiceImportApprovalResultV1> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialInvoiceImportApproveRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Approve, request.BatchId,
                cancellationToken) is { } denied)
        {
            return denied;
        }
        if (!ValidIdentity(metadata.IdempotencyKey) || !ValidIdentity(request.ExpectedVersion)
            || !ValidIdentity(context.ActorId) || !ValidIdentity(context.CorrelationId))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("request", "finance_value_does_not_fit"));
        }

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(JsonSerializer.Serialize(request, Json));
        if (await PriorAsync(context, ApproveCommand, FinanceOfficialInvoiceImportAuthorityActionV1.Approve,
                request.BatchId, keyHash, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var row = await dbContext.OfficialInvoiceImportBatches
            .SingleOrDefaultAsync(candidate => candidate.Id == request.BatchId, cancellationToken);
        var existingApproval = await dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.BatchId == request.BatchId, cancellationToken);
        if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Approve, request.BatchId,
                cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (row is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("batchId", "batch_not_found"));
        }
        if (existingApproval is not null || !string.Equals(request.ExpectedVersion, "1", StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict"));
        }
        if (string.Equals(row.UploadedBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("approver", "approver_must_differ_from_uploader"));
        }

        var now = timeProvider.GetUtcNow();
        var approval = new FinanceOfficialInvoiceImportApprovalRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            BatchId = row.Id,
            UploadedBy = row.UploadedBy,
            ApprovedBy = context.ActorId,
            ApprovedAtUtc = now,
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
        };
        var result = Succeeded(context, View(row, approval));
        dbContext.OfficialInvoiceImportApprovals.Add(approval);
        var command = AddCommand(context, ApproveCommand, metadata, keyHash, payloadHash, row.Id, result, now);
        dbContext.OfficialInvoiceImportWorkDeliveries.Add(
            FinanceOfficialInvoiceImportWorkObservationFactoryV1.ToRow(
                FinanceOfficialInvoiceImportWorkObservationFactoryV1.CreateApproval(command, row, approval)));

        if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Approve, row.Id, cancellationToken)
                is { } beforeSave)
        {
            dbContext.ChangeTracker.Clear();
            return beforeSave;
        }

        Audit(context, metadata, row.Id, "finance.official-invoice-import.approved.v1", "Approve", 1, 2,
            AuditJson(row, null), AuditJson(row, approval), envelopes.Approve,
            "Yükleyenden farklı yetkili finans kullanıcısı resmî dosyayı onayladı");
        return await SaveAsync(context, ApproveCommand, FinanceOfficialInvoiceImportAuthorityActionV1.Approve,
            row.Id, keyHash, payloadHash, cancellationToken) ?? result;
    }

    public async Task<ReadResult<ApprovedFinanceOpenItemSourceBatch>> GetApprovedAsync(
        FinanceContext context,
        FinanceOpenItemSourceReference source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context)) return new(ReadOutcome.Denied, null);
        var decision = await readAuthority.CheckAsync(context, source.BatchId, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context)) return ReadRefusal<ApprovedFinanceOpenItemSourceBatch>(decision);

        var row = await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == source.BatchId, cancellationToken);
        var approved = row is null ? null : await dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.BatchId == source.BatchId, cancellationToken);
        decision = await readAuthority.CheckAsync(context, source.BatchId, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context)) return ReadRefusal<ApprovedFinanceOpenItemSourceBatch>(decision);
        if (row is null || approved is null
            || !string.Equals(row.BatchVersion, source.BatchVersion, StringComparison.Ordinal)
            || !string.Equals(row.BatchChecksum, source.BatchChecksum, StringComparison.Ordinal))
        {
            return new(ReadOutcome.NotFound, null);
        }
        return new(ReadOutcome.Found, Batch(row));
    }

    public async Task<ReadResult<ApprovedFinanceOpenItemSourcePage>> ListApprovedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context)) return new(ReadOutcome.Denied, null);
        if (!TryOffset(cursor, out var offset)) return new(ReadOutcome.NotFound, null);
        var decision = await readAuthority.CheckAsync(context, Guid.Empty, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context)) return ReadRefusal<ApprovedFinanceOpenItemSourcePage>(decision);

        var rows = await (from batch in dbContext.OfficialInvoiceImportBatches.AsNoTracking()
                          join approval in dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
                              on batch.Id equals approval.BatchId
                          orderby approval.ApprovedAtUtc descending, batch.Id
                          select batch)
            .Skip(offset).Take(PageSize + 1).ToListAsync(cancellationToken);
        decision = await readAuthority.CheckAsync(context, Guid.Empty, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context)) return ReadRefusal<ApprovedFinanceOpenItemSourcePage>(decision);

        var hasMore = rows.Count > PageSize;
        var items = rows.Take(PageSize).Select(Batch).ToArray();
        return new(ReadOutcome.Found, new ApprovedFinanceOpenItemSourcePage(context.CompanyId, items,
            hasMore ? checked(offset + PageSize).ToString(CultureInfo.InvariantCulture) : null));
    }

    public async Task<ReadResult<FinanceOfficialInvoiceImportWorkObservationV1>> GetAsync(
        FinanceContext context,
        Guid batchId,
        long observationOrdinal,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var action = observationOrdinal switch
        {
            1 => FinanceOfficialInvoiceImportAuthorityActionV1.Upload,
            2 => FinanceOfficialInvoiceImportAuthorityActionV1.Approve,
            _ => (FinanceOfficialInvoiceImportAuthorityActionV1?)null,
        };
        if (batchId == Guid.Empty || deliveryId == Guid.Empty || action is null)
        {
            return new(ReadOutcome.NotFound, null);
        }
        if (await ReadFenceAsync<FinanceOfficialInvoiceImportWorkObservationV1>(context, action.Value, batchId,
                cancellationToken) is { } refused)
        {
            return refused;
        }

        FinanceOfficialInvoiceImportWorkDeliveryRow? delivery;
        FinanceOfficialInvoiceImportCommandRow? command;
        FinanceOfficialInvoiceImportBatchRow? batch;
        FinanceOfficialInvoiceImportApprovalRow? approval;
        try
        {
            delivery = await dbContext.OfficialInvoiceImportWorkDeliveries.AsNoTracking()
                .SingleOrDefaultAsync(row => row.DeliveryId == deliveryId && row.BatchId == batchId
                    && row.ObservationOrdinal == observationOrdinal, cancellationToken);
            command = delivery is null ? null : await dbContext.OfficialInvoiceImportCommands.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == delivery.CommandReceiptId, cancellationToken);
            batch = delivery is null ? null : await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == delivery.BatchId, cancellationToken);
            approval = observationOrdinal == 2 && delivery is not null
                ? await dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.BatchId == delivery.BatchId, cancellationToken)
                : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailureAfterWorkReadAsync<FinanceOfficialInvoiceImportWorkObservationV1>(
                context, action.Value, batchId, cancellationToken);
        }

        if (await ReadFenceAsync<FinanceOfficialInvoiceImportWorkObservationV1>(context, action.Value, batchId,
                cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (delivery is null || command is null || batch is null)
        {
            return new(ReadOutcome.NotFound, null);
        }
        return FinanceOfficialInvoiceImportWorkObservationFactoryV1.TryRestore(
            delivery, command, batch, approval, out var observation)
            ? new(ReadOutcome.Found, observation)
            : new(ReadOutcome.Unavailable, null);
    }

    private async Task<FinanceOfficialInvoiceImportApprovalResultV1?> PriorAsync(
        FinanceContext context, string command, FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId,
        string keyHash, string payloadHash, CancellationToken cancellationToken)
    {
        var prior = await dbContext.OfficialInvoiceImportCommands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (await FenceAsync(context, action, batchId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (prior is null) return null;
        if (!string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal)
            || !string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }
        var stored = JsonSerializer.Deserialize<FinanceOfficialInvoiceImportApprovalResultV1>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı resmî fatura import sonucu okunamadı.");
        if (prior.ProducerSchemaVersion is null)
        {
            return stored with { IsReplay = true };
        }
        if (prior.ProducerSchemaVersion != FinanceOfficialInvoiceImportWorkObservationFactoryV1.PayloadSchemaVersion)
        {
            return Refused(context, CommandOutcome.Unavailable,
                Issue("delivery", "finance_work_delivery_unavailable"));
        }
        var delivery = await dbContext.OfficialInvoiceImportWorkDeliveries.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandReceiptId == prior.Id, cancellationToken);
        var batch = await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == prior.BatchId, cancellationToken);
        var approval = string.Equals(command, ApproveCommand, StringComparison.Ordinal)
            ? await dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.BatchId == prior.BatchId, cancellationToken)
            : null;
        if (await FenceAsync(context, action, batchId, cancellationToken) is { } afterDeliveryRead)
        {
            return afterDeliveryRead;
        }
        if (delivery is null || batch is null
            || !FinanceOfficialInvoiceImportWorkObservationFactoryV1.TryRestore(
                delivery, prior, batch, approval, out _))
        {
            return Refused(context, CommandOutcome.Unavailable,
                Issue("delivery", "finance_work_delivery_unavailable"));
        }
        if (stored.Outcome != CommandOutcome.Succeeded || stored.Resource is null
            || stored.Resource != View(batch, approval))
        {
            return Refused(context, CommandOutcome.Unavailable,
                Issue("delivery", "finance_work_delivery_unavailable"));
        }
        return stored with { IsReplay = true };
    }

    private async Task<FinanceOfficialInvoiceImportApprovalResultV1?> SaveAsync(
        FinanceContext context, string command, FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId,
        string keyHash, string payloadHash, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, command, action, batchId, keyHash, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
    }

    private async Task<FinanceOfficialInvoiceImportApprovalResultV1?> FenceAsync(
        FinanceContext context, FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        var decision = await commandAuthority.CheckAsync(context, action, batchId, cancellationToken);
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (decision == PermissionDecision.Allowed) return null;
        return decision == PermissionDecision.Denied
            ? Refused(context, CommandOutcome.Denied)
            : Refused(context, CommandOutcome.Unavailable, Issue("permission", "finance_permission_mapping_unavailable"));
    }

    private async Task<ReadResult<T>?> ReadFenceAsync<T>(
        FinanceContext context,
        FinanceOfficialInvoiceImportAuthorityActionV1 action,
        Guid batchId,
        CancellationToken cancellationToken) where T : class
    {
        if (!SameContext(context)) return new(ReadOutcome.Denied, null);
        var decision = await commandAuthority.CheckAsync(context, action, batchId, cancellationToken);
        if (!SameContext(context)) return new(ReadOutcome.Denied, null);
        return decision switch
        {
            PermissionDecision.Allowed => null,
            PermissionDecision.Unavailable => new(ReadOutcome.Unavailable, null),
            _ => new(ReadOutcome.Denied, null),
        };
    }

    private async Task<ReadResult<T>> FailureAfterWorkReadAsync<T>(
        FinanceContext context,
        FinanceOfficialInvoiceImportAuthorityActionV1 action,
        Guid batchId,
        CancellationToken cancellationToken) where T : class =>
        await ReadFenceAsync<T>(context, action, batchId, cancellationToken)
        ?? new(ReadOutcome.Unavailable, null);

    private FinanceOfficialInvoiceImportCommandRow AddCommand(
        FinanceContext context, string command, CommandMetadata metadata, string keyHash,
        string payloadHash, Guid batchId, FinanceOfficialInvoiceImportApprovalResultV1 result, DateTimeOffset now)
    {
        var row = new FinanceOfficialInvoiceImportCommandRow
        {
            Id = Guid.NewGuid(), CompanyId = context.CompanyId, CommandName = command,
            IdempotencyKeyHash = keyHash, ActorId = context.ActorId, PayloadHash = payloadHash, BatchId = batchId,
            ProducerSchemaVersion = FinanceOfficialInvoiceImportWorkObservationFactoryV1.PayloadSchemaVersion,
            ResultJson = JsonSerializer.Serialize(result, Json), CorrelationId = context.CorrelationId, RecordedAtUtc = now,
        };
        dbContext.OfficialInvoiceImportCommands.Add(row);
        return row;
    }

    private void Audit(FinanceContext context, CommandMetadata metadata, Guid batchId, string eventType,
        string operation, long? beforeVersion, long afterVersion, string before, string after,
        FinanceAuditEnvelope envelope, string reason)
        => auditEvents.Enqueue(new AuditEventDraft(context.CompanyId, eventType, EntityType, batchId.ToString("D"),
            operation, beforeVersion, afterVersion, AuditActorTypes.User, context.ActorId, envelope.PermissionCode,
            envelope.RoleCodes, before, after, reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent, context.CorrelationId, context.CausationId,
            metadata.IdempotencyKey));

    private bool SameContext(FinanceContext context) => currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == context.CompanyId
        && context.CompanyId != Guid.Empty && currentUser.CanAccessCompany(context.CompanyId);

    private static ApprovedFinanceOpenItemSourceBatch Batch(FinanceOfficialInvoiceImportBatchRow row) => new(
        row.Id, row.CompanyId, row.BatchVersion, row.BatchChecksum,
        JsonSerializer.Deserialize<ApprovedFinanceOpenItemSourceRow[]>(row.SourceRowsJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı resmî fatura kaynak satırları okunamadı."));

    private static FinanceOfficialInvoiceImportBatchViewV1 View(FinanceOfficialInvoiceImportBatchRow row,
        FinanceOfficialInvoiceImportApprovalRow? approval) => new(row.Id, row.CompanyId, row.BatchVersion,
        row.BatchChecksum, row.TemplateVersion,
        approval is null ? FinanceOfficialInvoiceImportApprovalStatusV1.Pending : FinanceOfficialInvoiceImportApprovalStatusV1.Approved,
        approval is null ? "1" : "2", row.RowCount, row.UploadedBy, row.UploadedAtUtc,
        approval?.ApprovedBy, approval?.ApprovedAtUtc);

    private static string AuditJson(FinanceOfficialInvoiceImportBatchRow row,
        FinanceOfficialInvoiceImportApprovalRow? approval) => JsonSerializer.Serialize(new
        {
            batchId = row.Id, row.BatchVersion, row.BatchChecksum, row.TemplateVersion, row.RowCount,
            row.UploadedBy, row.UploadedAtUtc, approvedBy = approval?.ApprovedBy, approvedAtUtc = approval?.ApprovedAtUtc,
            version = approval is null ? "1" : "2",
        }, Json);

    private static string UploadPayloadHash(FinanceOfficialInvoiceImportPreviewRequestV1 request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(request.BatchId.ToByteArray());
        hash.AppendData(Encoding.UTF8.GetBytes(request.BatchVersion));
        hash.AppendData(request.Utf8Csv.Span);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool ValidIdentity(string? value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= IdentityMaximumLength && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    private static bool TryOffset(string? cursor, out int offset)
    {
        if (cursor is null) { offset = 0; return true; }
        return int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset >= 0
            && string.Equals(offset.ToString(CultureInfo.InvariantCulture), cursor, StringComparison.Ordinal);
    }
    private static ValidationIssue Issue(string field, string code) => new(field, code);
    private static FinanceOfficialInvoiceImportApprovalResultV1 Succeeded(FinanceContext context,
        FinanceOfficialInvoiceImportBatchViewV1 resource) => new(CommandOutcome.Succeeded, resource, false, context.CorrelationId, []);
    private static FinanceOfficialInvoiceImportApprovalResultV1 Refused(FinanceContext context,
        CommandOutcome outcome, params ValidationIssue[] issues) => new(outcome, null, false, context.CorrelationId, issues);
    private static ReadResult<T> ReadRefusal<T>(PermissionDecision decision) where T : class => new(
        decision == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied, default);
}
