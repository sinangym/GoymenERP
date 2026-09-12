using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.IntegrationTests;

// Controlled owner adapters only: not live authorization, persistence or SQL acceptance.
public sealed class GroupSharedCommandReplayTests
{
    private static readonly Guid Actor = Guid.Parse("53000000-0000-0000-0000-000000000001");
    private static readonly Guid Origin = Guid.Parse("54000000-0000-0000-0000-000000000001");
    private static readonly string Key = new('A', 64);
    private static readonly string Payload = new('B', 64);

    [Theory]
    [InlineData("valid")]
    [InlineData("denied")]
    [InlineData("subject")]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("actor")]
    [InlineData("origin")]
    [InlineData("revision")]
    [InlineData("trim")]
    [InlineData("control")]
    public void ReadContextRequiresExactReadAuthorityAndDoesNotGrantMutation(string mutation)
    {
        var snapshot = Snapshot(GroupSharedMasterOperation.Read);
        snapshot = mutation switch
        {
            "valid" => snapshot,
            "denied" => snapshot with { IsGranted = false },
            "subject" => snapshot with { Subject = (GroupSharedMasterKind)2 },
            "create" => snapshot with { Operation = GroupSharedMasterOperation.DraftCreate },
            "edit" => snapshot with { Operation = GroupSharedMasterOperation.DraftBasicEdit },
            "actor" => snapshot with { LocalActorId = Guid.Empty },
            "origin" => snapshot with { OriginCompanyId = Guid.Empty },
            "revision" => snapshot with { AuthorityRevision = "" },
            "trim" => snapshot with { AuthorityRevision = " rev " },
            "control" => snapshot with { AuthorityRevision = "rev\n" },
            _ => throw new ArgumentException(nameof(mutation)),
        };
        if (mutation == "valid")
        {
            var read = GroupSharedMasterReadContext.FromServerDecision(snapshot);
            Assert.Equal(Actor, read.LocalActorId);
            Assert.Equal(Origin, read.OriginCompanyId);
            Assert.Equal(GroupSharedMasterOperation.Read, read.Operation);
            Assert.Throws<UnauthorizedAccessException>(() => GroupSharedMasterCommandContext.FromServerDecision(
                snapshot, GroupSharedMasterOperation.DraftCreate));
        }
        else Assert.Throws<UnauthorizedAccessException>(() => GroupSharedMasterReadContext.FromServerDecision(snapshot));
    }

    [Fact]
    public async Task UnconfiguredAuthorityDoesNotReadAnyTerminal()
    {
        var reader = new Reader(Terminal());
        var resolver = new GroupSharedCommandReplayResolver(new UnconfiguredGroupSharedMasterAuthorizationService(), reader);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(
            GroupSharedMasterOperation.DraftCreate, Key, Payload));
        Assert.Equal(0, reader.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndMatchingOutcomesAreReturnedOnlyAfterFreshChecks(bool exists)
    {
        var authority = new Authority();
        var reader = new Reader(exists ? Terminal() : null);
        var result = await new GroupSharedCommandReplayResolver(authority, reader).ResolveAsync(
            GroupSharedMasterOperation.DraftCreate, Key, Payload);
        Assert.Equal(exists ? GroupSharedCommandReplayKind.MatchingReplay : GroupSharedCommandReplayKind.AuthorizedMissing, result.Kind);
        Assert.Equal(2, authority.Calls);
        Assert.Equal(1, reader.Reads);
        if (exists) Assert.Equal(" {\"id\":\"unchanged\"} ", result.Terminal!.ResponseJson);
        else Assert.Null(result.Terminal);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("origin")]
    [InlineData("payload")]
    [InlineData("key")]
    [InlineData("operation")]
    public async Task ConflictingStoredIntentDisclosesNoOldTerminal(string mutation)
    {
        var snapshot = Snapshot();
        if (mutation == "actor") snapshot = snapshot with { LocalActorId = Guid.NewGuid() };
        if (mutation == "origin") snapshot = snapshot with { OriginCompanyId = Guid.NewGuid() };
        if (mutation == "operation") snapshot = snapshot with { Operation = GroupSharedMasterOperation.DraftBasicEdit };
        var stored = Terminal(snapshot, mutation == "key" ? new string('C', 64) : Key,
            mutation == "payload" ? new string('D', 64) : Payload);
        var result = await new GroupSharedCommandReplayResolver(new Authority(), new Reader(stored)).ResolveAsync(
            GroupSharedMasterOperation.DraftCreate, Key, Payload);
        Assert.Equal(GroupSharedCommandReplayKind.Conflict, result.Kind);
        Assert.Null(result.Terminal);
        Assert.Equal(Actor, result.Context.LocalActorId);
        Assert.Equal(Origin, result.Context.OriginCompanyId);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("revision")]
    [InlineData("actor")]
    [InlineData("origin")]
    public async Task AuthorityChangeDuringLookupReleasesNeitherReplayNorMissing(string mutation)
    {
        foreach (var terminal in new GroupSharedCommandTerminal?[] { null, Terminal() })
        {
            var authority = new Authority
            {
                After = mutation switch
                {
                    "revoked" => Snapshot() with { IsGranted = false },
                    "revision" => Snapshot() with { AuthorityRevision = "rev-2" },
                    "actor" => Snapshot() with { LocalActorId = Guid.NewGuid() },
                    "origin" => Snapshot() with { OriginCompanyId = Guid.NewGuid() },
                    _ => throw new ArgumentException(nameof(mutation)),
                },
            };
            var reader = new Reader(terminal);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GroupSharedCommandReplayResolver(authority, reader)
                .ResolveAsync(GroupSharedMasterOperation.DraftCreate, Key, Payload));
            Assert.Equal(1, reader.Reads);
        }
    }

    [Fact]
    public async Task LaterRequestRechecksRatherThanCachingAnEarlierGrant()
    {
        var authority = new Authority();
        var reader = new Reader(Terminal());
        var resolver = new GroupSharedCommandReplayResolver(authority, reader);
        Assert.Equal(GroupSharedCommandReplayKind.MatchingReplay,
            (await resolver.ResolveAsync(GroupSharedMasterOperation.DraftCreate, Key, Payload)).Kind);
        authority.Before = Snapshot() with { IsGranted = false };
        authority.After = authority.Before;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(
            GroupSharedMasterOperation.DraftCreate, Key, Payload));
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task UnsupportedSchemaAndCancelledRequestNeverReturnStoredData()
    {
        var reader = new Reader(Terminal(schema: 2));
        var resolver = new GroupSharedCommandReplayResolver(new Authority(), reader);
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
            GroupSharedMasterOperation.DraftCreate, Key, Payload));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveAsync(
            GroupSharedMasterOperation.DraftCreate, Key, Payload, cancellation.Token));
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public void TerminalMetadataCannotSilentlyNormalizeMalformedEvidence()
    {
        var binding = Terminal().Binding;
        Assert.ThrowsAny<JsonException>(() => GroupSharedCommandTerminal.Create(binding, 201, "{", "corr", 1));
        Assert.Throws<ArgumentException>(() => GroupSharedCommandTerminal.Create(binding, 201, "{}", " corr ", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupSharedCommandTerminal.Create(binding, 100, "{}", "corr", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroupSharedCommandTerminal.Create(binding, 201, "{}", "corr", 0));
    }

    private static GroupSharedMasterAuthorizationSnapshot Snapshot(
        GroupSharedMasterOperation operation = GroupSharedMasterOperation.DraftCreate) =>
        new(true, GroupSharedMasterKind.Product, operation, Actor, Origin, "rev-1");

    private static GroupSharedCommandTerminal Terminal(GroupSharedMasterAuthorizationSnapshot? snapshot = null,
        string? key = null, string? payload = null, int schema = 1)
    {
        snapshot ??= Snapshot();
        var context = GroupSharedMasterCommandContext.FromServerDecision(snapshot, snapshot.Operation);
        return GroupSharedCommandTerminal.Create(GroupSharedCommandBinding.Create(context, key ?? Key, payload ?? Payload),
            201, " {\"id\":\"unchanged\"} ", "original-private-correlation", schema);
    }

    private sealed class Authority : IGroupSharedMasterAuthorizationService
    {
        public int Calls { get; private set; }
        public GroupSharedMasterAuthorizationSnapshot Before { get; set; } = Snapshot();
        public GroupSharedMasterAuthorizationSnapshot After { get; set; } = Snapshot();
        public Task<GroupSharedMasterAuthorizationSnapshot> GetSnapshotAsync(GroupSharedMasterKind subject,
            GroupSharedMasterOperation operation, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Calls == 1 ? Before : After);
        }
    }

    private sealed class Reader(GroupSharedCommandTerminal? terminal) : IGroupSharedCommandTerminalReader
    {
        public int Reads { get; private set; }
        public Task<GroupSharedCommandTerminal?> ReadAsync(GroupSharedCommandKey key, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(terminal);
        }
    }
}
