using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Documents.Infrastructure.Persistence;

/// <summary>
/// The vault's metadata store over <see cref="DocumentsDbContext"/>. An upload is ONE save: the blob row when the company
/// does not hold this content yet (physical dedup within the company), the document, its version 1, its owner link, the
/// recorded attempt and the audit event (with the number of earlier versions of the same content — AT-057 recorded).
/// A unique-key race is answered afresh: the same key answers the winner's document; a concurrent upload of the same
/// content in the same company re-reads the blob the winner stored and saves again, once. Reads are of the context
/// company only (the tenant filter). Nothing is updated or deleted.
/// </summary>
public sealed class DocumentsStore(DocumentsDbContext dbContext, ICompanyContext companyContext, IAuditEventCollector auditEvents)
    : IDocumentStore
{
    private const string EntityType = "Documents.Document";
    private const string UploadedEventType = "documents.document.uploaded.v1";
    private const int ActorMaximumLength = 128;
    private const int KeyMaximumLength = 200;
    private const int SaveRounds = 2;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<DocumentStoreResult?> FindReplayAsync(DocumentWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireResolvedCompany();
        var attempt = await FindAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        return attempt is null ? null : await ReplayAsync(attempt, envelope, requestHash, cancellationToken);
    }

    public async Task<DocumentStoreResult> StoreUploadAsync(DocumentWriteEnvelope envelope, string requestHash, NewDocumentUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();
        RequireUpload(upload, companyId);
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var correlationId = envelope.CorrelationId.Trim();

        for (var round = 1; ; round++)
        {
            var attempt = await FindAttemptAsync(DocumentCommands.UploadV1, keyHash, cancellationToken);
            if (attempt is not null)
            {
                return await ReplayAsync(attempt, envelope, requestHash, cancellationToken);
            }

            if (await dbContext.Documents.AsNoTracking().AnyAsync(row => row.Id == upload.DocumentId, cancellationToken))
            {
                throw Refused(DocumentStoreCodes.ConcurrentChange, "Bu belge kimliği şirketin başka bir belgesinde kullanılıyor.");
            }

            var blob = await dbContext.Blobs.AsNoTracking().SingleOrDefaultAsync(row => row.Sha256 == upload.Sha256, cancellationToken);
            if (blob is null)
            {
                dbContext.Blobs.Add(new ContentBlobRow
                {
                    CompanyId = companyId,
                    Sha256 = upload.Sha256,
                    SizeBytes = upload.SizeBytes,
                    ContentKind = (int)upload.Kind.Kind,
                    ObjectKey = upload.ObjectKey,
                    FirstStoredBy = actorId,
                    FirstStoredAtUtc = upload.UploadedAtUtc,
                });
            }
            else if (blob.SizeBytes != upload.SizeBytes || blob.ContentKind != (int)upload.Kind.Kind
                     || !string.Equals(blob.ObjectKey, upload.ObjectKey, StringComparison.Ordinal))
            {
                // The same SHA-256 with other facts is not the same content: nothing is linked to it.
                throw new InvalidOperationException("Aynı içerik özetine kayıtlı içerik bu yüklemeyle uyuşmuyor.");
            }

            var earlierVersions = await dbContext.Versions.AsNoTracking().CountAsync(row => row.Sha256 == upload.Sha256, cancellationToken);
            dbContext.Documents.Add(new DocumentRow
            {
                Id = upload.DocumentId,
                CompanyId = companyId,
                CreatedBy = actorId,
                CreatedAtUtc = upload.UploadedAtUtc,
                CorrelationId = correlationId,
            });
            dbContext.Versions.Add(new DocumentVersionRow
            {
                CompanyId = companyId,
                DocumentId = upload.DocumentId,
                VersionNo = 1,
                FileName = upload.FileName.Value,
                Sha256 = upload.Sha256,
                ScanState = (int)upload.ScanState,
                ScannerId = upload.ScannerId,
                UploadedBy = actorId,
                UploadedAtUtc = upload.UploadedAtUtc,
                CorrelationId = correlationId,
            });
            dbContext.Links.Add(new DocumentLinkRow
            {
                CompanyId = companyId,
                DocumentId = upload.DocumentId,
                OwnerKind = upload.Owner.Kind,
                OwnerId = upload.Owner.OwnerId,
                LinkedBy = actorId,
                LinkedAtUtc = upload.UploadedAtUtc,
            });
            dbContext.Attempts.Add(new DocumentAttemptRow
            {
                CompanyId = companyId,
                CommandName = DocumentCommands.UploadV1,
                IdempotencyKeyHash = keyHash,
                RequestHash = requestHash,
                ActorId = actorId,
                DocumentId = upload.DocumentId,
                CorrelationId = correlationId,
                RecordedAtUtc = upload.UploadedAtUtc,
            });
            Audit(envelope, companyId, upload, earlierVersions);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                // A unique key lost a race (the attempt key, or this company's blob of the same content). Nothing of this
                // round was saved; the next round re-reads: the same key answers the winner, the same content reuses its blob.
                dbContext.ChangeTracker.Clear();
                if (round >= SaveRounds)
                {
                    throw Refused(DocumentStoreCodes.ConcurrentChange, "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.",
                        exception);
                }

                continue;
            }

            var stored = await FindDocumentAsync(upload.DocumentId, cancellationToken)
                ?? throw new InvalidOperationException("Kaydedilen belge okunamadı.");
            return new DocumentStoreResult(stored, false, correlationId);
        }
    }

    public async Task<StoredDocument?> FindDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (documentId == Guid.Empty)
        {
            return null;
        }

        var rows = await Latest().Where(row => row.Id == documentId).ToListAsync(cancellationToken);
        return rows.Count switch
        {
            0 => null,
            1 => Map(rows[0]),
            // One owner link per document in this slice; a second would be a new, reviewed access rule (DEC-DOC-002).
            _ => throw new InvalidOperationException("Belgenin birden fazla kayıt bağlantısı bu sürümde desteklenmez."),
        };
    }

    public async Task<IReadOnlyList<StoredDocument>> ListByOwnerAsync(string ownerKind, Guid ownerId, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKind);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        RequireResolvedCompany();
        var rows = await Latest()
            .Where(row => row.OwnerKind == ownerKind && row.OwnerId == ownerId)
            .OrderByDescending(row => row.UploadedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(Map)];
    }

    public async Task<IReadOnlyList<StoredDocument>> FindByContentAsync(string sha256, Guid excludingDocumentId, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        RequireResolvedCompany();
        if (!DocumentContentHash.IsWellFormed(sha256))
        {
            throw new ArgumentException("İçerik özeti 64 küçük onaltılık karakter olmalıdır.", nameof(sha256));
        }

        var rows = await Latest()
            .Where(row => row.Sha256 == sha256 && row.Id != excludingDocumentId)
            .OrderByDescending(row => row.UploadedAtUtc)
            .ThenByDescending(row => row.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(Map)];
    }

    // ---- reads ----------------------------------------------------------------------------------

    /// <summary>Every document of the context company with its latest version, that version's blob and its owner link.</summary>
    private IQueryable<LatestDocumentRow> Latest() =>
        from document in dbContext.Documents.AsNoTracking()
        join version in dbContext.Versions.AsNoTracking()
            on new { document.CompanyId, DocumentId = document.Id } equals new { version.CompanyId, version.DocumentId }
        join blob in dbContext.Blobs.AsNoTracking()
            on new { version.CompanyId, version.Sha256 } equals new { blob.CompanyId, blob.Sha256 }
        join link in dbContext.Links.AsNoTracking()
            on new { document.CompanyId, DocumentId = document.Id } equals new { link.CompanyId, link.DocumentId }
        where version.VersionNo == dbContext.Versions
            .Where(candidate => candidate.CompanyId == document.CompanyId && candidate.DocumentId == document.Id)
            .Max(candidate => candidate.VersionNo)
        select new LatestDocumentRow
        {
            Id = document.Id,
            CompanyId = document.CompanyId,
            OwnerKind = link.OwnerKind,
            OwnerId = link.OwnerId,
            VersionNo = version.VersionNo,
            FileName = version.FileName,
            ContentKind = blob.ContentKind,
            SizeBytes = blob.SizeBytes,
            Sha256 = version.Sha256,
            ObjectKey = blob.ObjectKey,
            ScanState = version.ScanState,
            ScannerId = version.ScannerId,
            UploadedBy = version.UploadedBy,
            UploadedAtUtc = version.UploadedAtUtc,
        };

    private static StoredDocument Map(LatestDocumentRow row) =>
        new StoredDocument(row.Id, row.CompanyId, row.OwnerKind, row.OwnerId, row.VersionNo, row.FileName, (DocumentFileKind)row.ContentKind, row.SizeBytes,
            row.Sha256, row.ObjectKey, (DocumentScanState)row.ScanState, row.ScannerId, row.UploadedBy, row.UploadedAtUtc);

    private Task<DocumentAttemptRow?> FindAttemptAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.Attempts.AsNoTracking().SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash,
            cancellationToken);

    private async Task<DocumentStoreResult> ReplayAsync(DocumentAttemptRow attempt, DocumentWriteEnvelope envelope, string requestHash,
        CancellationToken cancellationToken)
    {
        // ADR-011 §3: another content or another actor under the same key is one 409 that discloses nothing.
        if (!string.Equals(attempt.ActorId, envelope.ActorId.Trim(), StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(DocumentStoreCodes.SameKeyPayloadMismatch, "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }

        var stored = await FindDocumentAsync(attempt.DocumentId, cancellationToken)
            ?? throw new InvalidOperationException("Kayıtlı denemenin belgesi bulunamadı.");
        return new DocumentStoreResult(stored, true, attempt.CorrelationId);
    }

    // ---- audit ----------------------------------------------------------------------------------

    private void Audit(DocumentWriteEnvelope envelope, Guid companyId, NewDocumentUpload upload, int earlierVersions) =>
        auditEvents.Enqueue(new AuditEventDraft(companyId, UploadedEventType, EntityType, upload.DocumentId.ToString("D"), "Upload", null, 1,
            AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, "{}", JsonSerializer.Serialize(new
            {
                ownerKind = upload.Owner.Kind,
                ownerId = upload.Owner.OwnerId,
                versionNo = 1,
                fileName = upload.FileName.Value,
                contentType = upload.Kind.ContentType,
                sizeBytes = upload.SizeBytes,
                sha256 = upload.Sha256,
                scanState = upload.ScanState.ToString(),
                scannerId = upload.ScannerId,
                // AT-057: how many versions of this company already held the same content when this one was stored.
                sameContentEarlierVersions = earlierVersions,
            }, Json), "Belge yüklendi", ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));

    // ---- guards ---------------------------------------------------------------------------------

    private Guid RequireResolvedCompany() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
            ? companyContext.CompanyId
            : throw Refused(DocumentStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan belge işlenmez.");

    private static void RequireUpload(NewDocumentUpload upload, Guid companyId)
    {
        if (upload.DocumentId == Guid.Empty || upload.SizeBytes < 1 || !DocumentContentHash.IsWellFormed(upload.Sha256)
            || upload.Owner is null || upload.FileName is null || upload.Kind is null
            || !Enum.IsDefined(upload.ScanState) || (upload.ScanState == DocumentScanState.Clean) != !string.IsNullOrWhiteSpace(upload.ScannerId))
        {
            throw new ArgumentException("Belge yüklemesi eksik veya tutarsız.", nameof(upload));
        }

        if (!string.Equals(upload.ObjectKey, DocumentObjectKeys.For(companyId, upload.Sha256), StringComparison.Ordinal))
        {
            throw Refused(DocumentStoreCodes.CompanyContextMismatch, "Nesne anahtarı aktif şirketin içerik anahtarı değil.");
        }
    }

    private static void RequireEnvelope(DocumentWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.PermissionCode)
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || string.IsNullOrWhiteSpace(envelope.SourceChannel) || envelope.RoleCodes is null)
        {
            throw new ArgumentException("Belge yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(envelope));
        }

        if (string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || !string.Equals(envelope.IdempotencyKey, envelope.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || envelope.IdempotencyKey.Length > KeyMaximumLength
            || envelope.ActorId.Trim().Length > ActorMaximumLength || envelope.CorrelationId.Trim().Length > ActorMaximumLength)
        {
            throw new ArgumentException("Idempotency-Key, aktör veya correlation sütununa uymuyor.", nameof(envelope));
        }
    }

    private static void RequireHash(string requestHash)
    {
        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length != 64 || !requestHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("İstek özeti 64 onaltılık karakter olmalıdır.", nameof(requestHash));
        }
    }

    private static void RequireCommand(string commandName)
    {
        if (!DocumentCommands.IsKnown(commandName))
        {
            throw new ArgumentException("Bilinmeyen komut adı.", nameof(commandName));
        }
    }

    /// <summary>ADR-011 §2: the attempt identity is the uppercase SHA-256 of the trimmed key; the raw key is never stored.</summary>
    public static string IdempotencyKeyHash(string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey.Trim())));

    private static DocumentStoreException Refused(string code, string message, Exception? inner = null) =>
        new DocumentStoreException(code, message, inner);

    /// <summary>A member-initialized projection (so the query composes over it); never tracked, never written.</summary>
    private sealed class LatestDocumentRow
    {
        public Guid Id { get; init; }

        public Guid CompanyId { get; init; }

        public string OwnerKind { get; init; } = string.Empty;

        public Guid OwnerId { get; init; }

        public int VersionNo { get; init; }

        public string FileName { get; init; } = string.Empty;

        public int ContentKind { get; init; }

        public long SizeBytes { get; init; }

        public string Sha256 { get; init; } = string.Empty;

        public string ObjectKey { get; init; } = string.Empty;

        public int ScanState { get; init; }

        public string? ScannerId { get; init; }

        public string UploadedBy { get; init; } = string.Empty;

        public DateTimeOffset UploadedAtUtc { get; init; }
    }
}
