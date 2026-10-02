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
    public bool IsAuthenticated => userId is not null;

    public string? UserId => userId;

    public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(companies);

    public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
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

/// <summary>The Cari owner as a set of (company, party) pairs the actor may read.</summary>
internal sealed class FixedOwnerPort(
    ICompanyContext companyContext,
    HashSet<(Guid Company, Guid Party)> readable,
    Dictionary<(Guid Company, Guid Party), DocumentOwnerAccessV1> outcomes) : IDocumentOwnerLinkPortV1
{
    public HashSet<(Guid Company, Guid Party)> Readable => readable;

    public int Calls { get; private set; }

    public Exception? Error { get; set; }

    public string Kind => DocumentOwnerLinkKindsV1.CounterpartyParty;

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

    public Task<DocumentScanVerdictV1> ScanAsync(ReadOnlyMemory<byte> content, DocumentFileKind kind, CancellationToken cancellationToken = default)
    {
        Scans++;
        return Task.FromResult(verdict);
    }
}

/// <summary>
/// An in-memory metadata store with the real store's contract: company-filtered reads, the attempt answered for the same
/// actor and request hash, one document per upload with its single owner link. Every document of every company is kept
/// in one list so that a test can prove the company filter.
/// </summary>
internal sealed class MemoryDocumentStore(ICompanyContext companyContext, Harness.SharedWorld world) : IDocumentStore
{
    public List<StoredDocument> Documents => world.Documents;

    public List<(Guid Company, string Command, string Key, string RequestHash, string Actor, Guid DocumentId, string Correlation)> Attempts =>
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

        return Task.FromResult<DocumentStoreResult?>(new DocumentStoreResult(Documents.Single(document => document.Id == attempt.DocumentId), true,
            attempt.Correlation));
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
        Attempts.Add((companyId, DocumentCommands.UploadV1, envelope.IdempotencyKey, requestHash, envelope.ActorId, upload.DocumentId,
            envelope.CorrelationId));
        return new DocumentStoreResult(stored, false, envelope.CorrelationId);
    }

    public Task<StoredDocument?> FindDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Visible().SingleOrDefault(document => document.Id == documentId));

    public Task<IReadOnlyList<StoredDocument>> ListByOwnerAsync(string ownerKind, Guid ownerId, int limit, CancellationToken cancellationToken = default)
    {
        BeforeList?.Invoke();
        return Task.FromResult<IReadOnlyList<StoredDocument>>(
            [.. Visible().Where(document => document.OwnerKind == ownerKind && document.OwnerId == ownerId)
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

    private IEnumerable<StoredDocument> Visible() => Documents.Where(document => document.CompanyId == companyContext.CompanyId);
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

        Store = new MemoryDocumentStore(Company, World);
        Objects = World.Objects;
        Scanner = scanner;
        Service = new DocumentService(Company, User, Authority, Correlation, [Owner], Store, withObjects ? Objects : null,
            scanner, new DocumentsServiceBindings(maximumBytes), TimeProvider.System);
    }

    public SharedWorld World { get; }

    public CompanyContext Company { get; }

    public CorrelationContext Correlation { get; }

    public FixedUser User { get; }

    public GrantedAuthority Authority { get; } = new();

    public FixedOwnerPort Owner { get; }

    public MemoryDocumentStore Store { get; }

    public MemoryObjectStore Objects { get; }

    public IDocumentContentScannerV1? Scanner { get; }

    public DocumentService Service { get; }

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

        public List<StoredDocument> Documents { get; } = [];

        public List<(Guid Company, string Command, string Key, string RequestHash, string Actor, Guid DocumentId, string Correlation)> Attempts { get; } = [];

        public List<DocumentWriteEnvelope> Envelopes { get; } = [];
    }
}
