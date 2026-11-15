using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Documents.Domain;

namespace GoymenERP.Documents.UnitTests;

/// <summary>An actor with a fixed id and company set.</summary>
internal sealed class FixedUser(string? userId, params Guid[] companies) : ICurrentUser
{
    public bool IsAuthenticated => CurrentUserId is not null;

    public string? UserId => CurrentUserId;

    public string? CurrentUserId { get; set; } = userId;

    public HashSet<Guid> MutableAllowedCompanyIds { get; } = [.. companies];

    public IReadOnlySet<Guid> AllowedCompanyIds => MutableAllowedCompanyIds;

    public bool CanAccessCompany(Guid companyId) => MutableAllowedCompanyIds.Contains(companyId);
}

/// <summary>The central authority as a set of company-wide grants that a test can revoke between measurements.</summary>
internal sealed class GrantedAuthority : IErpAuthorityRevalidationService
{
    public HashSet<(string Permission, Guid Company)> Grants { get; } = [];

    public int Measurements { get; private set; }

    public Action? OnMeasure { get; set; }

    public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
    {
        Measurements++;
        OnMeasure?.Invoke();
        return Task.FromResult(Grants.Contains((permission, companyId))
            ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK-DOCUMENTS"], StringComparer.Ordinal), new HashSet<Guid>(),
                new HashSet<Guid>(), new HashSet<Guid>())
            : PermissionScope.Denied);
    }
}

/// <summary>
/// One owner kind (the Cari owner unless another kind is given) as a set of (company, record) pairs the actor may read;
/// <paramref name="acceptsExisting"/> is the B-26 opt-in for linking an existing document and
/// <paramref name="acceptsGenerated"/> the F-026 opt-in for generated documents.
/// </summary>
internal sealed class FixedOwnerPort(
    ICompanyContext companyContext,
    HashSet<(Guid Company, Guid Party)> readable,
    Dictionary<(Guid Company, Guid Party), DocumentOwnerAccessV1> outcomes,
    string kind = DocumentOwnerLinkKindsV1.CounterpartyParty,
    bool acceptsExisting = false,
    bool acceptsGenerated = false) : IDocumentOwnerLinkPortV1
{
    public HashSet<(Guid Company, Guid Party)> Readable => readable;

    public int Calls { get; private set; }

    public Exception? Error { get; set; }

    public string Kind => kind;

    public bool AcceptsExistingDocumentLinks => acceptsExisting;

    public bool AcceptsGeneratedDocuments => acceptsGenerated;

    public Task<DocumentOwnerAccessV1> CheckReadableAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Error is not null)
        {
            throw Error;
        }

        if (companyContext.IsResolved && outcomes.TryGetValue((companyContext.CompanyId, ownerId), out var outcome))
        {
            return Task.FromResult(outcome);
        }

        return Task.FromResult(companyContext.IsResolved && Readable.Contains((companyContext.CompanyId, ownerId))
            ? DocumentOwnerAccessV1.Readable
            : DocumentOwnerAccessV1.NotFound);
    }
}

internal sealed class MemoryObjectStore : IDocumentObjectStoreV1
{
    public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

    public int Puts { get; private set; }

    public bool Fail { get; set; }

    public Action? BeforeRead { get; set; }

    public Task PutAsync(string key, ReadOnlyMemory<byte> content, string sha256, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new DocumentObjectStoreUnavailableException("SENTETIK");
        }

        Puts++;
        if (!Objects.TryGetValue(key, out var existing))
        {
            Objects[key] = content.ToArray();
        }
        else if (!existing.AsSpan().SequenceEqual(content.Span))
        {
            throw new DocumentObjectStoreUnavailableException("SENTETIK: another content under the key");
        }

        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        BeforeRead?.Invoke();
        if (Fail)
        {
            throw new DocumentObjectStoreUnavailableException("SENTETIK");
        }

        return Task.FromResult(Objects.TryGetValue(key, out var bytes) ? bytes.ToArray() : null);
    }
}

internal sealed class FixedScanner(DocumentScanVerdictV1 verdict) : IDocumentContentScannerV1
{
    public string ScannerId => "SENTETIK-scanner-v1";

    public int Scans { get; private set; }

    public Action? AfterScan { get; set; }

    public Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, DocumentFileKind kind, CancellationToken cancellationToken = default)
    {
        Scans++;
        AfterScan?.Invoke();
        return Task.FromResult(verdict);
    }
}

/// <summary>
/// An in-memory metadata store with the real store's contract: company-filtered reads, the attempt answered for the same
/// actor and request hash, one document per upload with its owner links (the upload's first; B-26 adds more). Every
/// document of every company is kept in one list so that a test can prove the company filter.
/// </summary>
internal sealed class MemoryDocumentStore(ICompanyContext companyContext, Harness.SharedWorld world) : IDocumentStore
{
    public List<StoredDocument> Documents => world.Documents;

    public List<(Guid Company, string Command, string Key, string RequestHash, string Actor, Guid DocumentId, int VersionNo, string Correlation)> Attempts =>
        world.Attempts;

    public List<DocumentWriteEnvelope> Envelopes => world.Envelopes;

    public Action? BeforeWrite { get; set; }

    public Action? BeforeFindByContent { get; set; }

    public Action? BeforeList { get; set; }

    public Task<DocumentStoreResult?> FindReplayAsync(DocumentWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default)
    {
        var attempt = Attempts.SingleOrDefault(candidate => candidate.Company == companyContext.CompanyId && candidate.Command == commandName
            && candidate.Key == envelope.IdempotencyKey);
        if (attempt == default)
        {
            return Task.FromResult<DocumentStoreResult?>(null);
        }

        if (attempt.Actor != envelope.ActorId || attempt.RequestHash != requestHash)
        {
            throw new DocumentStoreException(DocumentStoreCodes.SameKeyPayloadMismatch, "SENTETIK");
        }

        return Task.FromResult<DocumentStoreResult?>(new DocumentStoreResult(WithLinks(world.Versions.Single(document => document.Id == attempt.DocumentId
                && document.VersionNo == attempt.VersionNo)), true,
            attempt.Correlation));
    }

    public Task<DocumentStoreResult?> FindLinkReplayAsync(DocumentWriteEnvelope envelope, string requestHash, Guid documentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(LinkReplay(envelope, requestHash, documentId));

    public async Task<DocumentStoreResult> StoreLinkAsync(DocumentWriteEnvelope envelope, string requestHash, NewDocumentLink link,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(beforeWrite);
        if (LinkReplay(envelope, requestHash, link.DocumentId) is { } replay)
        {
            return replay;
        }

        BeforeWrite?.Invoke();
        await beforeWrite(cancellationToken);
        var existing = Visible().SingleOrDefault(document => document.Id == link.DocumentId);
        if (existing is null || existing.VersionNo != link.ExpectedCurrentVersion)
        {
            throw new DocumentStoreException(DocumentStoreCodes.ConcurrentChange, "SENTETIK");
        }

        var links = world.Links[(existing.CompanyId, existing.Id)];
        if (links.Any(candidate => candidate.OwnerKind == link.Owner.Kind && candidate.OwnerId == link.Owner.OwnerId))
        {
            throw new DocumentStoreException(DocumentStoreCodes.AlreadyLinked, "SENTETIK");
        }

        Envelopes.Add(envelope);
        links.Add(new StoredDocumentLink(link.Owner.Kind, link.Owner.OwnerId));
        Attempts.Add((existing.CompanyId, DocumentCommands.LinkV1, envelope.IdempotencyKey, requestHash, envelope.ActorId, existing.Id,
            existing.VersionNo, envelope.CorrelationId));
        return new DocumentStoreResult(WithLinks(existing), false, envelope.CorrelationId);
    }

    /// <summary>The links each document carries now, primary first.</summary>
    public IReadOnlyList<StoredDocumentLink> LinksOf(Guid company, Guid documentId) =>
        world.Links.TryGetValue((company, documentId), out var links) ? [.. links] : [];

    private DocumentStoreResult? LinkReplay(DocumentWriteEnvelope envelope, string requestHash, Guid documentId)
    {
        var attempt = Attempts.SingleOrDefault(candidate => candidate.Company == companyContext.CompanyId
            && candidate.Command == DocumentCommands.LinkV1 && candidate.Key == envelope.IdempotencyKey);
        if (attempt == default)
        {
            return null;
        }

        if (attempt.Actor != envelope.ActorId || attempt.RequestHash != requestHash || attempt.DocumentId != documentId)
        {
            throw new DocumentStoreException(DocumentStoreCodes.SameKeyPayloadMismatch, "SENTETIK");
        }

        return new DocumentStoreResult(Visible().Single(document => document.Id == documentId), true, attempt.Correlation);
    }

    private StoredDocument WithLinks(StoredDocument document) =>
        world.Links.TryGetValue((document.CompanyId, document.Id), out var links) ? document with { Links = [.. links] } : document;

    public async Task<DocumentStoreResult?> FindVersionReplayAsync(DocumentWriteEnvelope envelope, string requestHash, Guid documentId,
        int resultVersionNo, CancellationToken cancellationToken = default)
    {
        var result = await FindReplayAsync(envelope, requestHash, DocumentCommands.AppendVersionV1, cancellationToken);
        if (result is not null && (result.Document.Id != documentId || result.Document.VersionNo != resultVersionNo))
        {
            throw new DocumentStoreException(DocumentStoreCodes.SameKeyPayloadMismatch, "SENTETIK");
        }

        return result;
    }

    public async Task<DocumentStoreResult> StoreUploadAsync(DocumentWriteEnvelope envelope, string requestHash, NewDocumentUpload upload,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeWrite);
        BeforeWrite?.Invoke();
        await beforeWrite(cancellationToken);
        var companyId = companyContext.CompanyId;
        Envelopes.Add(envelope);
        var stored = new StoredDocument(upload.DocumentId, companyId, upload.Owner.Kind, upload.Owner.OwnerId, 1, upload.FileName.Value,
            upload.Kind.Kind, upload.SizeBytes, upload.Sha256, upload.ObjectKey, upload.ScanState, upload.ScannerId, envelope.ActorId,
            upload.UploadedAtUtc);
        Documents.Add(stored);
        world.Versions.Add(stored);
        world.Links[(companyId, upload.DocumentId)] = [new StoredDocumentLink(upload.Owner.Kind, upload.Owner.OwnerId)];
        Attempts.Add((companyId, upload.Template is null ? DocumentCommands.UploadV1 : DocumentCommands.StoreGeneratedV1, envelope.IdempotencyKey,
            requestHash, envelope.ActorId, upload.DocumentId, 1, envelope.CorrelationId));
        if (upload.Template is { } template)
        {
            world.Templates[(companyId, upload.DocumentId)] = template;
        }

        return new DocumentStoreResult(WithLinks(stored), false, envelope.CorrelationId);
    }

    public async Task<DocumentStoreResult> StoreVersionAsync(DocumentWriteEnvelope envelope, string requestHash, int expectedCurrentVersion,
        NewDocumentVersion version, Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeWrite);
        BeforeWrite?.Invoke();
        await beforeWrite(cancellationToken);
        var existing = Visible().SingleOrDefault(document => document.Id == version.DocumentId);
        if (existing is null || existing.VersionNo != expectedCurrentVersion)
        {
            throw new DocumentStoreException(DocumentStoreCodes.ConcurrentChange, "SENTETIK");
        }

        var stored = new StoredDocument(version.DocumentId, existing.CompanyId, existing.OwnerKind, existing.OwnerId,
            expectedCurrentVersion + 1, version.FileName.Value, version.Kind.Kind, version.SizeBytes, version.Sha256, version.ObjectKey,
            version.ScanState, version.ScannerId, envelope.ActorId, version.UploadedAtUtc);
        Documents.RemoveAll(document => document.CompanyId == existing.CompanyId && document.Id == existing.Id);
        Documents.Add(stored);
        world.Versions.Add(stored);
        Attempts.Add((existing.CompanyId, DocumentCommands.AppendVersionV1, envelope.IdempotencyKey, requestHash, envelope.ActorId,
            version.DocumentId, stored.VersionNo, envelope.CorrelationId));
        return new DocumentStoreResult(WithLinks(stored), false, envelope.CorrelationId);
    }

    public Task<StoredDocument?> FindDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visible().SingleOrDefault(document => document.Id == documentId));

    public Task<StoredDocument?> FindDocumentVersionAsync(Guid documentId, int versionNo, CancellationToken cancellationToken = default) =>
        Task.FromResult(world.Versions.Where(document => document.CompanyId == companyContext.CompanyId && document.Id == documentId
            && document.VersionNo == versionNo).Select(WithLinks).SingleOrDefault());

    public Task<IReadOnlyList<StoredDocument>> ListVersionsAsync(Guid documentId, int limit, int? beforeVersionNo,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoredDocument>>(
            [.. world.Versions.Where(document => document.CompanyId == companyContext.CompanyId && document.Id == documentId)
                .Where(document => !beforeVersionNo.HasValue || document.VersionNo < beforeVersionNo.Value)
                .OrderByDescending(document => document.VersionNo).Take(limit).Select(WithLinks)]);

    public Task<IReadOnlyList<StoredDocument>> ListByOwnerAsync(string ownerKind, Guid ownerId, int limit, CancellationToken cancellationToken = default)
    {
        BeforeList?.Invoke();
        return Task.FromResult<IReadOnlyList<StoredDocument>>(
            [.. Visible().Where(document => document.OwnerLinks.Any(link => link.OwnerKind == ownerKind && link.OwnerId == ownerId))
                .OrderByDescending(document => document.UploadedAtUtc).Take(limit)]);
    }

    public Task<IReadOnlyList<StoredDocument>> FindByContentAsync(string sha256, Guid excludingDocumentId, int limit,
        CancellationToken cancellationToken = default)
    {
        BeforeFindByContent?.Invoke();
        return Task.FromResult<IReadOnlyList<StoredDocument>>(
            [.. Visible().Where(document => document.Sha256 == sha256 && document.Id != excludingDocumentId)
                .OrderByDescending(document => document.UploadedAtUtc).Take(limit)]);
    }

    private IEnumerable<StoredDocument> Visible() =>
        Documents.Where(document => document.CompanyId == companyContext.CompanyId).Select(WithLinks);
}

/// <summary>One request's wiring: a company, an actor, grants, the Cari owner, the stores and the scanner.</summary>
internal sealed class Harness
{
    public static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid CompanyB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public const string Actor = "SENTETIK-actor";

    public Harness(Guid company, IDocumentContentScannerV1? scanner = null, bool withObjects = true, string? actor = Actor, long maximumBytes = 4096,
        SharedWorld? world = null)
    {
        World = world ?? new SharedWorld();
        Company = new CompanyContext();
        Company.Set(company);
        Correlation = new CorrelationContext();
        Correlation.Set($"SENTETIK-{Guid.NewGuid():N}", null, SourceChannels.Web, "127.0.0.1", "SENTETIK-agent");
        User = new FixedUser(actor, CompanyA, CompanyB);
        Owner = new FixedOwnerPort(Company, World.Readable, World.OwnerOutcomes);
        LinkTarget = new FixedOwnerPort(Company, World.TargetReadable, World.TargetOutcomes, DocumentOwnerLinkKindsV1.ManufacturingOrder,
            acceptsExisting: true);
        ClosedTarget = new FixedOwnerPort(Company, World.TargetReadable, World.TargetOutcomes, DocumentOwnerLinkKindsV1.FinanceOfficialSalesInvoice);
        GeneratedTarget = new FixedOwnerPort(Company, World.GeneratedReadable, World.GeneratedOutcomes, DocumentOwnerLinkKindsV1.InventoryMaterialLabel,
            acceptsGenerated: true);

        Store = new MemoryDocumentStore(Company, World);
        Objects = World.Objects;
        Scanner = scanner;
        Service = new DocumentService(Company, User, Authority, Correlation, [Owner, LinkTarget, ClosedTarget, GeneratedTarget], Store, withObjects ? Objects : null,
            scanner, new DocumentsServiceBindings(maximumBytes), TimeProvider.System);
    }

    public SharedWorld World { get; }

    public CompanyContext Company { get; }

    public CorrelationContext Correlation { get; }

    public FixedUser User { get; }

    public GrantedAuthority Authority { get; } = new();

    public FixedOwnerPort Owner { get; }

    /// <summary>B-26: an owner kind that takes existing documents (the Manufacturing order stands for every opted-in kind).</summary>
    public FixedOwnerPort LinkTarget { get; }

    /// <summary>B-26: an owner kind that does not take existing documents (the official sales invoice, FNS-D36/D38).</summary>
    public FixedOwnerPort ClosedTarget { get; }

    /// <summary>F-026: an owner kind that takes generated PDFs (the Inventory material label stands for every opted-in kind).</summary>
    public FixedOwnerPort GeneratedTarget { get; }

    public MemoryDocumentStore Store { get; }

    public MemoryObjectStore Objects { get; }

    public IDocumentContentScannerV1? Scanner { get; }

    public DocumentService Service { get; }

    /// <summary>The same scoped service as the producing modules see it (F-026).</summary>
    public IGeneratedDocumentStorePortV1 Generated => Service;

    public Harness Grant(params string[] permissions)
    {
        foreach (var permission in permissions)
        {
            Authority.Grants.Add((permission, Company.CompanyId));
        }

        return this;
    }

    public static UploadDocumentRequest Upload(Guid party, byte[] content, string fileName = "Sözleşme.pdf", string? declared = "application/pdf") =>
        new(DocumentOwnerLinkKindsV1.CounterpartyParty, party, fileName, declared, content);

    public DocumentCommandMetadata Metadata(string key) => new(key, Correlation.CorrelationId, null);

    /// <summary>What several requests share: the object store and the owner records that exist per company.</summary>
    internal sealed class SharedWorld
    {
        public MemoryObjectStore Objects { get; } = new();

        public HashSet<(Guid Company, Guid Party)> Readable { get; } = [];

        public Dictionary<(Guid Company, Guid Party), DocumentOwnerAccessV1> OwnerOutcomes { get; } = [];

        public HashSet<(Guid Company, Guid Party)> TargetReadable { get; } = [];

        public Dictionary<(Guid Company, Guid Party), DocumentOwnerAccessV1> TargetOutcomes { get; } = [];

        public HashSet<(Guid Company, Guid Party)> GeneratedReadable { get; } = [];

        public Dictionary<(Guid Company, Guid Party), DocumentOwnerAccessV1> GeneratedOutcomes { get; } = [];

        public Dictionary<(Guid Company, Guid DocumentId), GeneratedDocumentTemplate> Templates { get; } = [];

        public Dictionary<(Guid Company, Guid DocumentId), List<StoredDocumentLink>> Links { get; } = [];

        public List<StoredDocument> Documents { get; } = [];

        public List<StoredDocument> Versions { get; } = [];

        public List<(Guid Company, string Command, string Key, string RequestHash, string Actor, Guid DocumentId, int VersionNo, string Correlation)> Attempts { get; } = [];

        public List<DocumentWriteEnvelope> Envelopes { get; } = [];
    }
}
