using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Transfers;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsTransferOutgoingDocumentServiceTests
{
    private static readonly Guid Company = Guid.Parse("99000000-0000-4000-8000-000000000001");
    private static readonly Guid Document = Guid.Parse("99000000-0000-4000-8000-000000000002");
    private static readonly Guid Transfer = Guid.Parse("99000000-0000-4000-8000-000000000003");
    private static readonly Guid Line = Guid.Parse("99000000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_requires_fresh_authority_before_commit_and_uses_existing_dispatch_note_permission()
    {
        var scope = new FakeScope();
        var store = new FakeStore();
        var service = Create(scope, store);

        var response = await service.CreateDraftAsync(
            new(Document, Transfer, [Line], null), Metadata("create-key"));

        Assert.Equal("Draft", response.Document.Status);
        Assert.Equal(1, response.Document.Version);
        Assert.Equal([LogisticsPermissionManifestV1.DispatchNotesCreate], scope.InitialPermissions);
        Assert.Equal([LogisticsPermissionManifestV1.DispatchNotesCreate], scope.FreshPermissions);
        Assert.True(store.BeforeCommitObserved);
    }

    [Fact]
    public async Task Finalize_uses_approve_permission_and_exposes_replay_without_another_transition()
    {
        var draft = TransferOutgoingDocument.Create(Company, Document, Transfer, [Line], null, "actor", Now);
        var scope = new FakeScope();
        var store = new FakeStore(draft, replay: true);
        var service = Create(scope, store);

        var response = await service.FinalizeAsync(Document, new(1), Metadata("finalize-key"));

        Assert.True(response.IsReplay);
        Assert.Equal("Effective", response.Document.Status);
        Assert.Equal([LogisticsPermissionManifestV1.DispatchNotesApprove], scope.InitialPermissions);
        Assert.Equal([LogisticsPermissionManifestV1.DispatchNotesApprove], scope.FreshPermissions);
        Assert.Equal(1, store.MutationCalls);
    }

    [Fact]
    public async Task Authority_loss_at_the_final_fence_returns_forbidden_and_store_does_not_complete()
    {
        var scope = new FakeScope { DenyFresh = true };
        var store = new FakeStore();
        var service = Create(scope, store);

        var error = await Assert.ThrowsAsync<LogisticsTransferOutgoingDocumentServiceException>(() =>
            service.CreateDraftAsync(new(Document, Transfer, [Line], null), Metadata("lost-key")));

        Assert.Equal((403, "forbidden"), (error.Status, error.Code));
        Assert.False(store.Completed);
    }

    private static LogisticsTransferOutgoingDocumentService Create(FakeScope scope, FakeStore store) => new(
        scope, store,
        new(LogisticsPermissionManifestV1.DispatchNotesCreate,
            LogisticsPermissionManifestV1.DispatchNotesApprove,
            LogisticsPermissionManifestV1.DispatchNotesCancel),
        new FixedCorrelation(), new FixedTimeProvider(Now));

    private static LogisticsCommandMetadata Metadata(string key) => new(key, "corr", null, Now);

    private sealed class FakeScope : ILogisticsTransferOutgoingDocumentScopeV1
    {
        public bool DenyFresh { get; init; }
        public List<string> InitialPermissions { get; } = [];
        public List<string> FreshPermissions { get; } = [];

        public Task<LogisticsTransferOutgoingDocumentScopeV1> RequireAsync(string permission,
            CancellationToken cancellationToken = default)
        {
            InitialPermissions.Add(permission);
            return Task.FromResult(new LogisticsTransferOutgoingDocumentScopeV1(Company, "actor", ["ROLE"]));
        }

        public Task RequireStillAsync(LogisticsTransferOutgoingDocumentScopeV1 scope, string permission,
            CancellationToken cancellationToken = default)
        {
            FreshPermissions.Add(permission);
            if (DenyFresh) throw new LogisticsTransferOutgoingDocumentServiceException(403, "forbidden");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore(TransferOutgoingDocument? existing = null, bool replay = false)
        : ILogisticsTransferOutgoingDocumentStoreV1
    {
        public bool BeforeCommitObserved { get; private set; }
        public bool Completed { get; private set; }
        public int MutationCalls { get; private set; }

        public async Task<TransferOutgoingDocumentStoreResultV1> StoreDraftAsync(LogisticsWriteEnvelope envelope,
            string requestHash, TransferOutgoingDocument document, Func<CancellationToken, Task> beforeCommit,
            CancellationToken cancellationToken = default)
        {
            await beforeCommit(cancellationToken);
            BeforeCommitObserved = true;
            Completed = true;
            return new(document, replay ? LogisticsWriteOutcome.Replayed : LogisticsWriteOutcome.Recorded,
                envelope.CorrelationId);
        }

        public async Task<TransferOutgoingDocumentStoreResultV1> MutateAsync(LogisticsWriteEnvelope envelope,
            string requestHash, string command, Guid documentId, Action<TransferOutgoingDocument, DateTimeOffset> mutate,
            Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            var document = existing ?? TransferOutgoingDocument.Create(
                Company, Document, Transfer, [Line], null, "actor", Now);
            mutate(document, Now.AddMinutes(1));
            await beforeCommit(cancellationToken);
            BeforeCommitObserved = true;
            Completed = true;
            return new(document, replay ? LogisticsWriteOutcome.Replayed : LogisticsWriteOutcome.Recorded,
                envelope.CorrelationId);
        }

        public Task<LogisticsTransferOutgoingDocumentOwnerSnapshotV1?> FindProofSnapshotAsync(Guid documentId,
            CancellationToken cancellationToken = default) => Task.FromResult<LogisticsTransferOutgoingDocumentOwnerSnapshotV1?>(null);
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr";
        public string? CausationId => null;
        public string SourceChannel => "Test";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-test";
        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent) { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
