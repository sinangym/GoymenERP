using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Modules.Documents.Application;

/// <summary>
/// The vault's use cases (ERP-000G core, AT-065 first owner: the Cari party). The route binds nothing but the server
/// context and the wire; this service decides, with the Logistics service's fences:
/// <list type="bullet">
/// <item><b>Authority</b>: a fresh central measurement of the vault's own name (upload or read) before anything is read,
/// after every awaited owner/store read and right before the single save; none after the commit.</item>
/// <item><b>Owner</b>: the linked record's owner answers, through its declared port, whether the record exists in the
/// context company and the actor may read it. Anything else is 404 (non-disclosure) — a document is never more
/// visible than its owner record, and another company's document is not found.</item>
/// <item><b>Content</b> (DEC-DOC-009): a safe file name, an allowlisted kind proven by the bytes' signature (or the
/// structural check of a bank-statement kind), a kind every record the file is shown on accepts (FNS-D52: the standard
/// kinds unless the owner port opts in to more), a configured size bound; then the malware scan. Malware is refused and
/// nothing is stored; no scanner, or a scanner that cannot answer, stores the version as NotScanned — never served.</item>
/// <item><b>Replay</b> (ADR-011): the attempt of (company, command, key hash) answers first; another body or actor under
/// the same key is one 409.</item>
/// <item><b>Duplicates</b> (AT-057, DEC-DOC-002): the same content in the same company is a warning, never a refusal —
/// the new document is stored (the object once), the earlier documents the actor may read are listed, and the count
/// found at upload time is in the audit event. No retention or deletion exists (OPEN-018/021): nothing is removed.</item>
/// <item><b>Links</b> (B-26, DEC-DOC-002): an existing document may be linked to one more owner record without a second
/// upload, under its own name (<see cref="DocumentsPermissionManifestV1.FilesLink"/>), when the actor may read the
/// document through one of its records AND the new record, and the new record's owner opted in. A document with several
/// links is readable through ANY readable link (each answer shows only that readable record); a new version changes the
/// file on every linked record, so appending requires EVERY link to be readable.</item>
/// <item><b>Several links</b> (KLN-D49, DEC-DOC-002): (1) the document is read by whoever may read at least one linked
/// record; (2) a new version is added only by whoever may read every linked record; (3) a new link is made by whoever may
/// read one existing source record and the target record; (4) the first link (the upload's) is the primary one and never
/// moves. The company boundary holds on every link on its own: each link row is keyed company-first and bound to the
/// document of the same company, the store reads only the context company, and every owner port answers for the context
/// company — a record of another company is never readable through any link.</item>
/// <item><b>Existing documents</b> (KLN-D50): on a kind that opted in (the Cari party, the stock label, the purchase
/// order, the sales order…) the list measures <see cref="DocumentListResponse.CanLink"/>, and
/// <see cref="ListLinkCandidatesAsync"/> names the earlier documents with the same content the actor may link instead
/// of uploading the same file again; the link itself is <see cref="LinkAsync"/>.</item>
/// </list>
/// </summary>
public sealed partial class DocumentService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    IEnumerable<IDocumentOwnerLinkPortV1> ownerPorts,
    IDocumentStore store,
    IDocumentObjectStoreV1? objects,
    IDocumentContentScannerV1? scanner,
    DocumentsServiceBindings bindings,
    TimeProvider timeProvider) : IDocumentService
{
    private const int ListLimit = 200;
    private const int DefaultVersionPageSize = 50;
    private const int MaximumVersionPageSize = 100;
    private const int DuplicateLimit = 10;
    private static readonly IReadOnlyList<DocumentFileKind> AnyKnownKind = [.. DocumentContentPolicy.KnownKinds.Select(info => info.Kind)];
    private readonly IReadOnlyDictionary<string, IDocumentOwnerLinkPortV1> owners = IndexOwners(ownerPorts);

    public async Task<DocumentUploadResponse> UploadAsync(UploadDocumentRequest request, DocumentCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesUpload;
        var scope = await RequireAsync(Permission, cancellationToken);

        var owner = RequireOwnerLink(request.OwnerKind, request.OwnerId);
        var fileName = DocumentFileName.TryCreate(request.FileName)
            ?? throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed, [new("file", DocumentRuleCodes.FileNameInvalid)]);
        var content = request.Content ?? [];
        // KLN-D33: a narrowed branding admission policy must not change a recorded upload's immutable hash/answer.
        // Classify its closed original standard set first, then enforce CURRENT admission only on a new upload.
        // Other owners retain their existing FNS-D52 admission and replay semantics.
        var decision = DocumentContentPolicy.Classify(fileName, request.DeclaredContentType, content, bindings.MaximumUploadBytes,
            owner.Kind == DocumentBrandingContractV1.CompanyOwnerKind ? DocumentContentPolicy.StandardKinds
                : owners[owner.Kind].AcceptedFileKinds);
        if (!decision.IsAccepted)
        {
            throw new DocumentServiceException(422, decision.RefusalCode!, [new("file", decision.RefusalCode!)]);
        }

        var kind = decision.Kind!;
        var sha256 = DocumentContentHash.Compute(content);
        var requestHash = RequestHash(owner, fileName, kind, sha256);
        var envelope = Envelope(scope, metadata, Permission);

        DocumentStoreResult? replay;
        try
        {
            replay = await GuardedAsync(scope, Permission, () => store.FindReplayAsync(envelope, requestHash, DocumentCommands.UploadV1,
                cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (replay is not null)
        {
            return await AnswerAsync(replay, scope, Permission, requireReplayFence: true, cancellationToken);
        }

        if (!IsReadableOwner(await GuardedAsync(scope, Permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        var verdict = await ScanAsync(content, kind.Kind, cancellationToken);
        // The scanner is an awaited external boundary. Re-measure the captured actor/company/permission and the
        // requested owner before interpreting its result or producing any external effect.
        await RequireOwnerStillReadableAsync(scope, Permission, owner, cancellationToken);
        if (verdict == DocumentScanVerdictV1.Infected)
        {
            // Nothing is stored: not the object, not a version, not an attempt. The same key may be retried with other bytes.
            throw new DocumentServiceException(422, DocumentProblemCodesV1.ContentRejected, [new("file", DocumentProblemCodesV1.ContentRejected)]);
        }

        var scanState = verdict == DocumentScanVerdictV1.Clean ? DocumentScanState.Clean : DocumentScanState.NotScanned;
        var objectKey = DocumentObjectKeys.For(scope.CompanyId, sha256);
        var objectStore = objects ?? throw new DocumentServiceException(503, "service_unavailable");
        try
        {
            // The object first, then the metadata: a failed save leaves at most an unreferenced, immutable,
            // content-addressed object of this company (reconciliation is ERP-000N's), never a row without its bytes.
            await objectStore.PutAsync(objectKey, content, sha256, cancellationToken);
        }
        catch (DocumentObjectStoreUnavailableException)
        {
            throw new DocumentServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, Permission, cancellationToken);
        DocumentStoreResult stored;
        try
        {
            var upload = new NewDocumentUpload(Guid.NewGuid(), owner, fileName, kind,
                content.LongLength, sha256, objectKey, scanState, scanState == DocumentScanState.Clean ? scanner!.ScannerId : null,
                timeProvider.GetUtcNow());
            Task Fence(CancellationToken token) => RequireWritableOwnerStillAsync(scope, Permission, owner, token);
            Task Admission(CancellationToken token)
            { token.ThrowIfCancellationRequested(); RequireKindAccepted(owner.Kind, kind.Kind); return Task.CompletedTask; }
            // The store checks its original/race winner BEFORE new admission. Only a genuinely new append is refused
            // by a narrowed subset; the same-key winner keeps its original immutable answer and fresh authority fence.
            stored = owner.Kind == DocumentBrandingContractV1.CompanyOwnerKind
                ? await store.StoreUploadWithAdmissionAsync(envelope, requestHash, upload, Fence, Admission, cancellationToken)
                : await store.StoreUploadAsync(envelope, requestHash, upload, Fence, cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        return await AnswerAsync(stored, scope, Permission, stored.IsReplay, cancellationToken);
    }

    public async Task<DocumentVersionWriteResponse> AppendVersionAsync(Guid documentId, AppendDocumentVersionRequest request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesUpload;
        var scope = await RequireAsync(Permission, cancellationToken);
        if (documentId == Guid.Empty || request.ExpectedCurrentVersion < 1 || request.ExpectedCurrentVersion == int.MaxValue)
        {
            throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new(documentId == Guid.Empty ? "documentId" : "expectedCurrentVersion", DocumentProblemCodesV1.ValidationFailed)]);
        }

        var fileName = DocumentFileName.TryCreate(request.FileName)
            ?? throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new("file", DocumentRuleCodes.FileNameInvalid)]);
        var content = request.Content ?? [];
        // The linked records are not known yet: the bytes are checked against every known kind here, and the kind against
        // every linked record's accepted kinds once the records are known to be readable (below).
        var decision = DocumentContentPolicy.Classify(fileName, request.DeclaredContentType, content, bindings.MaximumUploadBytes,
            AnyKnownKind);
        if (!decision.IsAccepted)
        {
            throw new DocumentServiceException(422, decision.RefusalCode!, [new("file", decision.RefusalCode!)]);
        }

        var kind = decision.Kind!;
        var sha256 = DocumentContentHash.Compute(content);
        var requestHash = VersionRequestHash(documentId, request.ExpectedCurrentVersion, fileName, kind, sha256);
        var envelope = Envelope(scope, metadata, Permission);
        DocumentStoreResult? replay;
        try
        {
            replay = await GuardedAsync(scope, Permission,
                () => store.FindVersionReplayAsync(envelope, requestHash, documentId, checked(request.ExpectedCurrentVersion + 1), cancellationToken),
                cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (replay is not null)
        {
            var shown = await RequireReadableLinkAsync(replay.Document, scope, Permission, cancellationToken);
            return new DocumentVersionWriteResponse(View(replay.Document, scope.CompanyId, shown), true, replay.OriginalCorrelationId);
        }

        StoredDocument? current;
        try
        {
            current = await GuardedAsync(scope, Permission, () => store.FindDocumentAsync(documentId, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (current is null || current.CompanyId != scope.CompanyId)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        // B-26: a new version changes the file on EVERY linked record, so every linked record must be readable now.
        var linked = OwnerLinksOf(current);
        await RequireEveryLinkReadableAsync(scope, Permission, linked, cancellationToken);
        // FNS-D52: the new version is shown on every linked record, so every linked record must accept its kind.
        if (!linked.All(link => link.Kind == DocumentBrandingContractV1.CompanyOwnerKind
                ? DocumentContentPolicy.StandardKinds.Contains(kind.Kind) : Accepts(link.Kind, kind.Kind)))
        {
            throw new DocumentServiceException(422, DocumentRuleCodes.TypeNotAllowed, [new("file", DocumentRuleCodes.TypeNotAllowed)]);
        }

        if (current.VersionNo != request.ExpectedCurrentVersion)
        {
            throw new DocumentServiceException(409, DocumentProblemCodesV1.ConcurrentChange,
                [new("expectedCurrentVersion", DocumentProblemCodesV1.ConcurrentChange)]);
        }

        var verdict = await ScanAsync(content, kind.Kind, cancellationToken);
        // The scanner is an awaited external boundary. Re-measure the captured actor/company/permission and the
        // document owners before interpreting its result or producing any external effect.
        await RequireEveryLinkReadableAsync(scope, Permission, linked, cancellationToken);
        if (verdict == DocumentScanVerdictV1.Infected)
        {
            throw new DocumentServiceException(422, DocumentProblemCodesV1.ContentRejected,
                [new("file", DocumentProblemCodesV1.ContentRejected)]);
        }

        var scanState = verdict == DocumentScanVerdictV1.Clean ? DocumentScanState.Clean : DocumentScanState.NotScanned;
        var objectKey = DocumentObjectKeys.For(scope.CompanyId, sha256);
        var objectStore = objects ?? throw new DocumentServiceException(503, "service_unavailable");
        try
        {
            await objectStore.PutAsync(objectKey, content, sha256, cancellationToken);
        }
        catch (DocumentObjectStoreUnavailableException)
        {
            throw new DocumentServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, Permission, cancellationToken);
        try
        {
            var version = new NewDocumentVersion(documentId, fileName, kind, content.LongLength, sha256, objectKey, scanState,
                scanState == DocumentScanState.Clean ? scanner!.ScannerId : null, timeProvider.GetUtcNow());
            Task Fence(CancellationToken token) => RequireEveryLinkReadableAsync(scope, Permission, linked, token);
            var brandingLinks = linked.Where(x => x.Kind == DocumentBrandingContractV1.CompanyOwnerKind).ToArray();
            Task Admission(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                foreach (var owner in brandingLinks) RequireKindAccepted(owner.Kind, kind.Kind);
                return Task.CompletedTask;
            }
            var result = brandingLinks.Length > 0
                ? await store.StoreVersionWithAdmissionAsync(envelope, requestHash, request.ExpectedCurrentVersion, version,
                    Fence, Admission, cancellationToken)
                : await store.StoreVersionAsync(envelope, requestHash, request.ExpectedCurrentVersion, version, Fence, cancellationToken);
            return new DocumentVersionWriteResponse(View(result.Document, scope.CompanyId, linked[0]), result.IsReplay,
                result.OriginalCorrelationId);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }
    }

    public async Task<DocumentVersionHistoryResponse> ListVersionsAsync(Guid documentId, int? pageSize, string? cursor,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesRead;
        var scope = await RequireAsync(Permission, cancellationToken);
        var size = pageSize ?? DefaultVersionPageSize;
        if (size is < 1 or > MaximumVersionPageSize)
        {
            throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new("pageSize", DocumentProblemCodesV1.ValidationFailed)]);
        }

        int? beforeVersionNo = null;
        if (cursor is not null)
        {
            if (string.IsNullOrWhiteSpace(cursor) || !string.Equals(cursor, cursor.Trim(), StringComparison.Ordinal)
                || !int.TryParse(cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedCursor) || parsedCursor < 1)
            {
                throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                    [new("cursor", DocumentProblemCodesV1.ValidationFailed)]);
            }

            beforeVersionNo = parsedCursor;
        }

        var shownOwner = await FindReadableDocumentAsync(documentId, scope, Permission, cancellationToken);
        IReadOnlyList<StoredDocument> versions;
        try
        {
            versions = await GuardedAsync(scope, Permission,
                () => store.ListVersionsAsync(documentId, checked(size + 1), beforeVersionNo, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        await RequireOwnerStillReadableAsync(scope, Permission, shownOwner, cancellationToken);
        var hasMore = versions.Count > size;
        var page = versions.Take(size).ToArray();
        var nextCursor = hasMore
            ? page[^1].VersionNo.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
        return new DocumentVersionHistoryResponse(documentId,
            [.. page.Select(version => View(version, scope.CompanyId, shownOwner))], nextCursor);
    }

    public async Task<DocumentContentV1> GetVersionContentAsync(Guid documentId, int versionNo,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesRead;
        var scope = await RequireAsync(Permission, cancellationToken);
        if (documentId == Guid.Empty || versionNo < 1)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        StoredDocument? document;
        try
        {
            document = await GuardedAsync(scope, Permission,
                () => store.FindDocumentVersionAsync(documentId, versionNo, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (document is null)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        var readableOwner = await RequireReadableLinkAsync(document, scope, Permission, cancellationToken);
        return await ReadContentAsync(document, readableOwner, scope, Permission, cancellationToken);
    }

    public async Task<DocumentListResponse> ListAsync(string? ownerKind, Guid ownerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesRead;
        var scope = await RequireAsync(Permission, cancellationToken);
        var owner = RequireOwnerLink(ownerKind, ownerId);
        if (!IsReadableOwner(await GuardedAsync(scope, Permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        IReadOnlyList<StoredDocument> documents;
        try
        {
            documents = await GuardedAsync(scope, Permission, () => store.ListByOwnerAsync(owner.Kind, owner.OwnerId, ListLimit, cancellationToken),
                cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        // Whether the upload is offered: the upload name measured now, for this company (a measurement, not a grant).
        var upload = await authority.RevalidatePermissionScopeAsync(DocumentsPermissionManifestV1.FilesUpload, scope.CompanyId, cancellationToken);
        var canUpload = upload.IsGranted && upload.IsCompanyWide && Matches(scope.CompanyId, scope.ActorId);
        // KLN-D50: whether linking an earlier document is offered — the record's kind opted in AND the link name measured
        // now, for this company (a measurement, not a grant; the link command re-checks everything).
        var canLink = false;
        if (owners[owner.Kind].AcceptsExistingDocumentLinks)
        {
            var link = await authority.RevalidatePermissionScopeAsync(DocumentsPermissionManifestV1.FilesLink, scope.CompanyId, cancellationToken);
            canLink = link.IsGranted && link.IsCompanyWide && Matches(scope.CompanyId, scope.ActorId);
        }

        await RequireStillAsync(scope, Permission, cancellationToken);
        await RequireOwnerStillReadableAsync(scope, Permission, owner, cancellationToken);
        // B-26: a document linked to several records is listed under the record asked for, never under the others.
        return new DocumentListResponse([.. documents.Select(document => View(document, scope.CompanyId, owner))], canUpload,
            Limits(owner.Kind), canLink);
    }

    public async Task<DocumentContentV1> GetContentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesRead;
        var scope = await RequireAsync(Permission, cancellationToken);
        if (documentId == Guid.Empty)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        StoredDocument? document;
        try
        {
            document = await GuardedAsync(scope, Permission, () => store.FindDocumentAsync(documentId, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        // Another company's document is not found (the tenant filter); a document of this company none of whose owner
        // records the actor may read is not found either — the same answer, so neither is disclosed.
        if (document is null)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        var readableOwner = await RequireReadableLinkAsync(document, scope, Permission, cancellationToken);
        return await ReadContentAsync(document, readableOwner, scope, Permission, cancellationToken);
    }

    public async Task<DocumentLinkWriteResponse> LinkAsync(Guid documentId, LinkDocumentRequest request,
        DocumentCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesLink;
        var scope = await RequireAsync(Permission, cancellationToken);
        if (documentId == Guid.Empty || request.ExpectedCurrentVersion < 1)
        {
            throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new(documentId == Guid.Empty ? "documentId" : "expectedCurrentVersion", DocumentProblemCodesV1.ValidationFailed)]);
        }

        var target = RequireOwnerLink(request.OwnerKind, request.OwnerId);
        if (!owners[target.Kind].AcceptsExistingDocumentLinks)
        {
            // Fail-closed: only an owner that opted in takes an existing document (FNS-D36/D38: never the official invoice).
            throw new DocumentServiceException(422, DocumentProblemCodesV1.LinkNotAccepted,
                [new("ownerKind", DocumentProblemCodesV1.LinkNotAccepted)]);
        }

        var requestHash = LinkRequestHash(documentId, request.ExpectedCurrentVersion, target);
        var envelope = Envelope(scope, metadata, Permission);
        DocumentStoreResult? replay;
        try
        {
            replay = await GuardedAsync(scope, Permission,
                () => store.FindLinkReplayAsync(envelope, requestHash, documentId, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (replay is not null)
        {
            // The replay answers under the linked record, so that record must still be readable (non-disclosure).
            if (replay.Document.CompanyId != scope.CompanyId)
            {
                throw new DocumentServiceException(404, "not_found");
            }

            await RequireOwnerStillReadableAsync(scope, Permission, target, cancellationToken);
            return new DocumentLinkWriteResponse(View(replay.Document, scope.CompanyId, target), true, replay.OriginalCorrelationId);
        }

        StoredDocument? current;
        try
        {
            current = await GuardedAsync(scope, Permission, () => store.FindDocumentAsync(documentId, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (current is null)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        // The actor must see the document through one of its records AND the record it is linked to now.
        var source = await RequireReadableLinkAsync(current, scope, Permission, cancellationToken);
        await RequireOwnerStillReadableAsync(scope, Permission, target, cancellationToken);
        // FNS-D52: a record takes an existing document only of a kind it accepts (a statement file never lands on a
        // record whose type list did not grow), checked once both records are known to be readable (non-disclosure).
        if (!Accepts(target.Kind, current.Kind))
        {
            throw new DocumentServiceException(422, DocumentProblemCodesV1.LinkNotAccepted,
                [new("ownerKind", DocumentProblemCodesV1.LinkNotAccepted)]);
        }

        if (current.VersionNo != request.ExpectedCurrentVersion)
        {
            throw new DocumentServiceException(409, DocumentProblemCodesV1.ConcurrentChange,
                [new("expectedCurrentVersion", DocumentProblemCodesV1.ConcurrentChange)]);
        }

        if (current.OwnerLinks.Any(link => string.Equals(link.OwnerKind, target.Kind, StringComparison.Ordinal)
                && link.OwnerId == target.OwnerId))
        {
            throw new DocumentServiceException(409, DocumentProblemCodesV1.AlreadyLinked,
                [new("ownerId", DocumentProblemCodesV1.AlreadyLinked)]);
        }

        await RequireStillAsync(scope, Permission, cancellationToken);
        try
        {
            var result = await store.StoreLinkAsync(envelope, requestHash,
                new NewDocumentLink(documentId, request.ExpectedCurrentVersion, target, timeProvider.GetUtcNow()),
                async token =>
                {
                    await RequireOwnerStillReadableAsync(scope, Permission, source, token);
                    await RequireOwnerStillReadableAsync(scope, Permission, target, token);
                },
                cancellationToken);
            return new DocumentLinkWriteResponse(View(result.Document, scope.CompanyId, target), result.IsReplay,
                result.OriginalCorrelationId);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }
    }

    public async Task<DocumentLinkCandidatesResponse> ListLinkCandidatesAsync(string? ownerKind, Guid ownerId, string? sha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string Permission = DocumentsPermissionManifestV1.FilesLink;
        var scope = await RequireAsync(Permission, cancellationToken);
        var target = RequireOwnerLink(ownerKind, ownerId);
        if (!owners[target.Kind].AcceptsExistingDocumentLinks)
        {
            throw new DocumentServiceException(422, DocumentProblemCodesV1.LinkNotAccepted,
                [new("ownerKind", DocumentProblemCodesV1.LinkNotAccepted)]);
        }

        // The hash only SELECTS earlier documents (whose own hashes the server computed at upload); nothing is stored from it.
        if (!DocumentContentHash.IsWellFormed(sha256))
        {
            throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new(DocumentsApiV1.Sha256Query, DocumentProblemCodesV1.ValidationFailed)]);
        }

        // KLN-D49 (3): the target record must be readable; otherwise it does not exist for this actor (non-disclosure).
        if (!IsReadableOwner(await GuardedAsync(scope, Permission,
                () => CheckOwnerAsync(target.Kind, target.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        IReadOnlyList<StoredDocument> sameContent;
        try
        {
            // The store reads only the context company (tenant filter): another company's document is never a candidate.
            sameContent = await GuardedAsync(scope, Permission,
                () => store.FindByContentAsync(sha256!, Guid.Empty, DuplicateLimit, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        var readable = new Dictionary<(string, Guid), bool>();
        var candidates = new List<DocumentLinkCandidateView>();
        foreach (var candidate in sameContent)
        {
            if (candidate.CompanyId != scope.CompanyId || candidate.OwnerLinks.Any(link =>
                    string.Equals(link.OwnerKind, target.Kind, StringComparison.Ordinal) && link.OwnerId == target.OwnerId))
            {
                // Another company's row is never shown; a document already on the target is not offered again.
                continue;
            }

            if (!Accepts(target.Kind, candidate.Kind))
            {
                // FNS-D52: the link command would refuse a kind the target does not accept, so it is not offered either.
                continue;
            }

            // KLN-D49 (3): the candidate is named under its first record (primary first) the actor may read now; a document
            // none of whose records is readable is withheld entirely. An owner that cannot answer is not readable here: the
            // probe offers, it never refuses because of a third record (the link command decides with 409/503).
            foreach (var link in candidate.OwnerLinks)
            {
                var key = (link.OwnerKind, link.OwnerId);
                if (!readable.TryGetValue(key, out var isReadable))
                {
                    isReadable = IsReadableDuplicateOwner(await GuardedAsync(scope, Permission,
                        () => CheckOwnerAsync(link.OwnerKind, link.OwnerId, cancellationToken), cancellationToken));
                    readable[key] = isReadable;
                }

                if (isReadable)
                {
                    candidates.Add(new DocumentLinkCandidateView(candidate.Id, candidate.VersionNo, candidate.FileName,
                        DocumentContentPolicy.Info(candidate.Kind).ContentType, candidate.SizeBytes, candidate.UploadedAtUtc,
                        link.OwnerKind, link.OwnerId));
                    break;
                }
            }
        }

        await RequireStillAsync(scope, Permission, cancellationToken);
        await RequireOwnerStillReadableAsync(scope, Permission, target, cancellationToken);
        return new DocumentLinkCandidatesResponse(target.Kind, target.OwnerId, sha256!, candidates);
    }

    private async Task<DocumentOwnerLink> FindReadableDocumentAsync(Guid documentId, Scope scope, string permission,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        StoredDocument? document;
        try
        {
            document = await GuardedAsync(scope, permission, () => store.FindDocumentAsync(documentId, cancellationToken), cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        if (document is null)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        return await RequireReadableLinkAsync(document, scope, permission, cancellationToken);
    }

    /// <summary>
    /// B-26: the first owner link of the document (primary first) whose record the actor may read now. None readable is
    /// 404 (non-disclosure); when none is readable and an owner answered Conflict or Unavailable, that answer (409/503)
    /// stands, as it did for the single link.
    /// </summary>
    private async Task<DocumentOwnerLink> RequireReadableLinkAsync(StoredDocument document, Scope scope, string permission,
        CancellationToken cancellationToken)
    {
        if (document.CompanyId != scope.CompanyId)
        {
            throw new DocumentServiceException(404, "not_found");
        }

        DocumentServiceException? undecided = null;
        foreach (var owner in OwnerLinksOf(document))
        {
            var access = await GuardedAsync(scope, permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken);
            try
            {
                if (IsReadableOwner(access))
                {
                    return owner;
                }
            }
            catch (DocumentServiceException exception)
            {
                undecided ??= exception;
            }
        }

        throw undecided ?? new DocumentServiceException(404, "not_found");
    }

    private async Task RequireEveryLinkReadableAsync(Scope scope, string permission, IReadOnlyList<DocumentOwnerLink> links,
        CancellationToken cancellationToken)
    {
        foreach (var owner in links)
        {
            await RequireOwnerStillReadableAsync(scope, permission, owner, cancellationToken);
        }
    }

    /// <summary>The stored links as validated owner links, primary first. A stored link is always well formed.</summary>
    private static IReadOnlyList<DocumentOwnerLink> OwnerLinksOf(StoredDocument document) =>
        [.. document.OwnerLinks.Select(link => DocumentOwnerLink.TryCreate(link.OwnerKind, link.OwnerId)
            ?? throw new InvalidOperationException("Belge bağlantısı geçersiz olarak saklanmış."))];

    private async Task<DocumentContentV1> ReadContentAsync(StoredDocument document, DocumentOwnerLink readableOwner, Scope scope,
        string permission, CancellationToken cancellationToken)
    {
        // KLN-D60: a system-generated PDF is served like a clean one; NotScanned stays refused (DEC-DOC-009).
        if (!DocumentScanStates.IsServable(document.ScanState))
        {
            throw new DocumentServiceException(409, DocumentProblemCodesV1.NotScanned, [new("document", DocumentProblemCodesV1.NotScanned)]);
        }

        var objectStore = objects ?? throw new DocumentServiceException(503, "service_unavailable");
        byte[]? content;
        try
        {
            content = await objectStore.ReadAsync(document.ObjectKey, cancellationToken);
        }
        catch (DocumentObjectStoreUnavailableException)
        {
            throw new DocumentServiceException(503, "service_unavailable");
        }

        if (content is null || content.LongLength != document.SizeBytes
            || !string.Equals(DocumentContentHash.Compute(content), document.Sha256, StringComparison.Ordinal))
        {
            throw new DocumentServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        await RequireOwnerStillReadableAsync(scope, permission, readableOwner, cancellationToken);
        return new DocumentContentV1(document.Id, document.FileName, DocumentContentPolicy.Info(document.Kind).ContentType, content, document.Sha256);
    }

    // ---- answers --------------------------------------------------------------------------------

    private async Task<DocumentUploadResponse> AnswerAsync(DocumentStoreResult result, Scope scope, string permission,
        bool requireReplayFence, CancellationToken cancellationToken)
    {
        var document = result.Document;
        if (requireReplayFence && (document.CompanyId != scope.CompanyId
            || !IsReadableOwner(await GuardedAsync(scope, permission,
                () => CheckOwnerAsync(document.OwnerKind, document.OwnerId, cancellationToken), cancellationToken))))
        {
            throw new DocumentServiceException(404, "not_found");
        }

        IReadOnlyList<StoredDocument> sameContent;
        try
        {
            sameContent = requireReplayFence
                ? await GuardedAsync(scope, permission,
                    () => store.FindByContentAsync(document.Sha256, document.Id, DuplicateLimit, cancellationToken), cancellationToken)
                : await store.FindByContentAsync(document.Sha256, document.Id, DuplicateLimit, cancellationToken);
        }
        catch (DocumentStoreException exception)
        {
            throw Store(exception);
        }

        // Only the duplicates whose owner record the actor may read are named (DEC-DOC-002: authority is judged apart).
        var readable = new Dictionary<(string, Guid), bool>();
        var duplicates = new List<DocumentDuplicateView>();
        foreach (var candidate in sameContent)
        {
            // B-26: a duplicate linked to several records is named once, under its first record the actor may read.
            foreach (var link in candidate.OwnerLinks)
            {
                var key = (link.OwnerKind, link.OwnerId);
                if (!readable.TryGetValue(key, out var isReadable))
                {
                    // Duplicate disclosure is optional and happens after the primary document was committed. A transient
                    // conflict or unavailable duplicate owner must never turn that committed 201 into a refusal that
                    // makes the client discard its retry key; every non-readable duplicate is simply withheld.
                    isReadable = IsReadableDuplicateOwner(requireReplayFence
                        ? await GuardedAsync(scope, permission,
                            () => CheckOwnerAsync(link.OwnerKind, link.OwnerId, cancellationToken), cancellationToken)
                        : await CheckOwnerAsync(link.OwnerKind, link.OwnerId, cancellationToken));
                    readable[key] = isReadable;
                }

                if (isReadable)
                {
                    duplicates.Add(new DocumentDuplicateView(candidate.Id, candidate.FileName, link.OwnerKind, link.OwnerId,
                        candidate.UploadedAtUtc));
                    break;
                }
            }
        }

        if (requireReplayFence)
        {
            if (!IsReadableOwner(await GuardedAsync(scope, permission,
                    () => CheckOwnerAsync(document.OwnerKind, document.OwnerId, cancellationToken), cancellationToken)))
            {
                throw new DocumentServiceException(404, "not_found");
            }
        }

        return new DocumentUploadResponse(View(document, scope.CompanyId), duplicates, result.IsReplay, result.OriginalCorrelationId);
    }

    /// <summary>
    /// The wire view. <paramref name="shownOwner"/> is the record the answer is about (B-26: the readable link, the listed
    /// record or the newly linked one); without it the primary link is shown.
    /// </summary>
    private static DocumentView View(StoredDocument document, Guid companyId, DocumentOwnerLink? shownOwner = null)
    {
        if (document.CompanyId != companyId)
        {
            throw new InvalidOperationException("Belge okuması şirket bağlamı dışında kayıt döndü.");
        }

        var ownerKind = shownOwner?.Kind ?? document.OwnerKind;
        var ownerId = shownOwner?.OwnerId ?? document.OwnerId;
        return new DocumentView(document.Id, document.CompanyId, ownerKind, ownerId, document.VersionNo, document.FileName,
            DocumentContentPolicy.Info(document.Kind).ContentType, document.SizeBytes, document.Sha256, document.ScanState.ToString(),
            DocumentScanStates.IsServable(document.ScanState), document.UploadedBy, document.UploadedAtUtc);
    }

    /// <summary>The bounds of an upload to a record of <paramref name="ownerKind"/>: only the kinds that record accepts.</summary>
    private DocumentUploadLimitsView Limits(string ownerKind)
    {
        var kinds = DocumentContentPolicy.InfosOf(owners[ownerKind].AcceptedFileKinds);
        return new DocumentUploadLimitsView(bindings.MaximumUploadBytes, [.. kinds.SelectMany(kind => kind.Extensions)],
            [.. kinds.Select(kind => kind.ContentType).Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// FNS-D52: whether a record of <paramref name="ownerKind"/> accepts content of <paramref name="kind"/>; an unregistered
    /// owner kind accepts nothing.
    /// </summary>
    private bool Accepts(string ownerKind, DocumentFileKind kind) =>
        owners.TryGetValue(ownerKind, out var port) && port.AcceptedFileKinds.Contains(kind);

    private void RequireKindAccepted(string ownerKind, DocumentFileKind kind)
    {
        if (!Accepts(ownerKind, kind))
            throw new DocumentServiceException(422, DocumentRuleCodes.TypeNotAllowed, [new("file", DocumentRuleCodes.TypeNotAllowed)]);
    }

    // ---- owner, scan ----------------------------------------------------------------------------

    private DocumentOwnerLink RequireOwnerLink(string? kind, Guid ownerId)
    {
        if (DocumentOwnerLink.TryCreate(kind, ownerId) is not { } link || !owners.ContainsKey(link.Kind))
        {
            throw new DocumentServiceException(400, DocumentProblemCodesV1.ValidationFailed,
                [new(kind is null || !owners.ContainsKey(kind) ? "ownerKind" : "ownerId", DocumentProblemCodesV1.ValidationFailed)]);
        }

        return link;
    }

    private async Task<DocumentOwnerAccessV1> CheckOwnerAsync(string kind, Guid ownerId, CancellationToken cancellationToken)
    {
        if (!owners.TryGetValue(kind, out var port))
        {
            // A stored link whose owner port is gone: nothing is served on it.
            return DocumentOwnerAccessV1.NotFound;
        }

        try
        {
            return await port.CheckReadableAsync(ownerId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return DocumentOwnerAccessV1.Denied;
        }
        catch
        {
            return DocumentOwnerAccessV1.Unavailable;
        }
    }

    private static bool IsReadableOwner(DocumentOwnerAccessV1 access) => access switch
    {
        DocumentOwnerAccessV1.Readable => true,
        DocumentOwnerAccessV1.NotFound or DocumentOwnerAccessV1.Denied => false,
        DocumentOwnerAccessV1.Conflict => throw new DocumentServiceException(409, DocumentProblemCodesV1.ConcurrentChange,
            [new("owner", DocumentProblemCodesV1.ConcurrentChange)]),
        DocumentOwnerAccessV1.Unavailable => throw new DocumentServiceException(503, "service_unavailable"),
        _ => throw new DocumentServiceException(503, "service_unavailable"),
    };

    private static bool IsReadableDuplicateOwner(DocumentOwnerAccessV1 access) =>
        access == DocumentOwnerAccessV1.Readable;

    private async Task<DocumentScanVerdictV1> ScanAsync(byte[] content, DocumentFileKind kind, CancellationToken cancellationToken)
    {
        if (scanner is null)
        {
            return DocumentScanVerdictV1.Unavailable;
        }

        try
        {
            var verdict = await scanner.ScanAsync(content, kind, cancellationToken);
            return Enum.IsDefined(verdict) ? verdict : DocumentScanVerdictV1.Unavailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A scanner that fails did not say "clean".
            return DocumentScanVerdictV1.Unavailable;
        }
    }

    private static string RequestHash(DocumentOwnerLink owner, DocumentFileName fileName, DocumentFileKindInfo kind, string sha256) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', DocumentCommands.UploadV1, owner.Kind,
            owner.OwnerId.ToString("D"), fileName.Value, kind.ContentType, sha256))));

    private static string VersionRequestHash(Guid documentId, int expectedCurrentVersion, DocumentFileName fileName,
        DocumentFileKindInfo kind, string sha256) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            DocumentCommands.AppendVersionV1, documentId.ToString("D"), expectedCurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fileName.Value, kind.ContentType, sha256))));

    private static string LinkRequestHash(Guid documentId, int expectedCurrentVersion, DocumentOwnerLink target) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', DocumentCommands.LinkV1,
            documentId.ToString("D"), expectedCurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            target.Kind, target.OwnerId.ToString("D")))));

    private static IReadOnlyDictionary<string, IDocumentOwnerLinkPortV1> IndexOwners(IEnumerable<IDocumentOwnerLinkPortV1> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var index = new Dictionary<string, IDocumentOwnerLinkPortV1>(StringComparer.Ordinal);
        foreach (var port in ports)
        {
            if (DocumentOwnerLink.TryCreate(port.Kind, Guid.NewGuid()) is null || !index.TryAdd(port.Kind, port))
            {
                throw new InvalidOperationException($"Belge bağlantı türü geçersiz veya iki kez kayıtlı: {port.Kind}");
            }
        }

        return index;
    }

    private static DocumentServiceException Store(DocumentStoreException exception) => exception.Code switch
    {
        "document_publication_invalid" => new DocumentServiceException(400, GeneratedDocumentStoreCodesV1.TemplateInvalid),
        DocumentStoreCodes.SameKeyPayloadMismatch => new DocumentServiceException(409, DocumentProblemCodesV1.SameKeyPayloadMismatch,
            [new("Idempotency-Key", DocumentProblemCodesV1.SameKeyPayloadMismatch)]),
        DocumentStoreCodes.ConcurrentChange => new DocumentServiceException(409, DocumentProblemCodesV1.ConcurrentChange,
            [new("document", DocumentProblemCodesV1.ConcurrentChange)]),
        DocumentStoreCodes.CompanyContextMismatch => new DocumentServiceException(403, "forbidden"),
        DocumentStoreCodes.AlreadyLinked => new DocumentServiceException(409, DocumentProblemCodesV1.AlreadyLinked,
            [new("ownerId", DocumentProblemCodesV1.AlreadyLinked)]),
        _ => new DocumentServiceException(503, "service_unavailable"),
    };

    // ---- authority ------------------------------------------------------------------------------

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new DocumentServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new DocumentServiceException(403, "forbidden");
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new DocumentServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. scope.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new DocumentServiceException(403, "forbidden");
        }
    }

    private async Task RequireWritableOwnerStillAsync(Scope scope, string permission, DocumentOwnerLink owner,
        CancellationToken cancellationToken) =>
        await RequireOwnerStillReadableAsync(scope, permission, owner, cancellationToken);

    private async Task RequireOwnerStillReadableAsync(Scope scope, string permission, DocumentOwnerLink owner,
        CancellationToken cancellationToken)
    {
        if (!IsReadableOwner(await GuardedAsync(scope, permission,
                () => CheckOwnerAsync(owner.Kind, owner.OwnerId, cancellationToken), cancellationToken)))
        {
            throw new DocumentServiceException(404, "not_found");
        }
    }

    private async Task<T> GuardedAsync<T>(Scope scope, string permission, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == companyId && currentUser.CanAccessCompany(companyId);

    private DocumentWriteEnvelope Envelope(Scope scope, DocumentCommandMetadata metadata, string permission) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
}
