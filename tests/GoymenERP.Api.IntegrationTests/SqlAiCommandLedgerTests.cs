using GoymenERP.Api.Ai.Commands;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.AiKnowledge.Domain;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Serialization and owner-port tests only; durable transactions are proven separately on SQL Server.</summary>
public sealed class SqlAiCommandLedgerTests
{
    internal static readonly Guid Company = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    internal static readonly DateTimeOffset At = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_new_adapter_recovers_the_complete_frozen_preview_and_approval_without_constructor_guessing()
    {
        var storage = new FakePersistence();
        var record = Record();
        var first = new SqlAiCommandLedger(storage);
        Assert.True(await first.TryAddAsync(record, [Event(record)]));
        var recovered = await new SqlAiCommandLedger(storage).GetAsync(Company, record.PreviewId);
        Assert.NotNull(recovered);
        Assert.Equal(record.Context, recovered.Context);
        Assert.Equal(record.PreviewHash, recovered.PreviewHash);
        Assert.Equal(record.PreviewIdentityJson, recovered.PreviewIdentityJson);
        Assert.Equal(record.CanonicalArgumentsJson, recovered.CanonicalArgumentsJson);
        Assert.Equal(record.Preview.CanonicalCommandJson, recovered.Preview.CanonicalCommandJson);
        Assert.Equal(record.Preview.DifferenceJson, recovered.Preview.DifferenceJson);
        Assert.Equal(record.Preview.Sources, recovered.Preview.Sources);
        Assert.Equal(record.ExpiresAtUtc, recovered.ExpiresAtUtc);
        var approved = record with { Version = 2, State = AiCommandStateV1.Approved, ApprovalId = Guid.NewGuid(),
            ApprovedBy = record.ActorId, ApprovedAtUtc = At.AddSeconds(1) };
        Assert.True(await first.TryReplaceAsync(approved, 1, Event(approved) with { Kind = AiCommandEventKindV1.Approved }));
        var reloaded = await new SqlAiCommandLedger(storage).GetAsync(Company, record.PreviewId);
        Assert.Equal(approved.ApprovalId, reloaded!.ApprovalId);
        Assert.Equal(2, reloaded.Version);
    }

    [Fact]
    public async Task The_adapter_refuses_a_foreign_or_corrupt_stored_record_instead_of_reconstructing_a_command()
    {
        var storage = new FakePersistence(); var record = Record();
        var ledger = new SqlAiCommandLedger(storage);
        await ledger.TryAddAsync(record, [Event(record)]);
        storage.Current = storage.Current! with { CompanyId = Guid.NewGuid() };
        await Assert.ThrowsAsync<AiCommandPersistenceUnavailableException>(() => ledger.GetAsync(Company, record.PreviewId));
        storage.Current = storage.Current with { CompanyId = Company, RecordJson = "{}" };
        await Assert.ThrowsAsync<AiCommandPersistenceUnavailableException>(() => ledger.GetAsync(Company, record.PreviewId));
    }

    [Fact]
    public async Task Changing_a_frozen_command_changes_the_binding_that_the_owner_must_compare()
    {
        var storage = new FakePersistence(); var record = Record();
        var ledger = new SqlAiCommandLedger(storage);
        await ledger.TryAddAsync(record, [Event(record)]);
        var initial = storage.Current!.BindingSha256;
        await ledger.TryReplaceAsync(record with { Version = 2, CanonicalArgumentsJson = "{\"changed\":true}" }, 1, Event(record));
        Assert.NotEqual(initial, storage.Current!.BindingSha256);
    }

    internal static AiCommandRecordV1 Record()
    {
        var id = Guid.NewGuid();
        var context = new AiServerContextV1(Company, "SENTETIK-actor", "SENTETIK-session", "scope-1", "authority-1",
            "SENTETIK-correlation", null, null, null);
        var preview = new AiPreviewV1(id, "SENTETIK-target", Guid.NewGuid(), "1", "{\"name\":\"SENTETIK ürün\"}",
            "{\"before\":\"a\",\"after\":\"b\"}",
            [new AiSourceV1("test.read.v1", "1", "SENTETIK-target", Guid.NewGuid(), "SENTETIK kaynak", "1", At, AiDataClassV1.Internal)],
            At, At.AddMinutes(15));
        return new(id, context, "test.rename.v1", "1", "{\"name\":\"SENTETIK ürün\"}", preview,
            "{\"identity\":\"SENTETIK\"}", new string('a', 64), preview.ExpiresAtUtc, AiCommandStateV1.Previewed, 1);
    }

    internal static AiCommandLedgerEventV1 Event(AiCommandRecordV1 record) => new(Guid.NewGuid(), Company, record.ActorId,
        AiCommandEventKindV1.Previewed, record.PreviewId, record.ToolId, record.ToolVersion, record.PreviewHash,
        null, null, null, "SENTETIK-ledger", null, At);

    private sealed class FakePersistence : IAiCommandPersistenceV1
    {
        public AiCommandStoredRecordV1? Current { get; set; }
        public bool IsAvailable => true;
        public Task<bool> TryAddAsync(AiCommandStoredRecordV1 record, IReadOnlyList<AiCommandStoredEventV1> events, CancellationToken cancellationToken = default)
        { Current = record; return Task.FromResult(true); }
        public Task<AiCommandStoredRecordV1?> GetAsync(Guid companyId, Guid previewId, CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task<bool> TryReplaceAsync(AiCommandStoredRecordV1 next, long expectedVersion, AiCommandStoredEventV1 auditEvent, CancellationToken cancellationToken = default)
        { Current = next; return Task.FromResult(true); }
        public Task<bool> AppendAsync(AiCommandStoredEventV1 auditEvent, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
