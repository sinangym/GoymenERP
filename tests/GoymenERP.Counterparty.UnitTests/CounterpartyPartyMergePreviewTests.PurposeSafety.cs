using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Counterparty.UnitTests;

public sealed partial class CounterpartyPartyMergePreviewTests
{
    [Fact]
    public async Task Purpose_preview_reuses_all_five_native_fields_and_owner_digest_without_widening_old_wire()
    {
        await using var f = await Fixture.Create();
        var native = await f.Read(); var service = PurposeService(f, new PurposeWriter());
        var purpose = await service.PreviewPurposeAsync(f.Survivor.Id, new(f.Merged.Id, "1", "1"), _ => Task.CompletedTask);
        Assert.NotNull(native.Preview); Assert.NotNull(purpose.NativePreview.Preview);
        Assert.Equal(JsonSerializer.Serialize(native), JsonSerializer.Serialize(purpose.NativePreview));
        Assert.Equal(IdempotencyHash.ComputePayload(native.Preview), purpose.SeenImpactDigest);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(native.Preview));
        Assert.Equal(new[] { "Changes", "CompanyId", "HoldingAccounts", "Merged", "Survivor" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(0, f.Audit.Writes);
    }

    [Fact]
    public async Task Account_change_without_party_version_change_changes_seen_digest()
    {
        await using var f = await Fixture.Create(); var service = PurposeService(f, new PurposeWriter());
        var before = await service.PreviewPurposeAsync(f.Survivor.Id, new(f.Merged.Id, "1", "1"), _ => Task.CompletedTask);
        var account = await f.Db.CompanyPartyAccounts.SingleAsync(x => x.CompanyId == B && x.PartyId == f.Merged.Id);
        f.Company.Set(B); account.Deactivate(); await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear(); f.Company.Set(A);
        var after = await service.PreviewPurposeAsync(f.Survivor.Id, new(f.Merged.Id, "1", "1"), _ => Task.CompletedTask);
        Assert.NotEqual(before.SeenImpactDigest, after.SeenImpactDigest);
        Assert.Equal("1", after.NativePreview.Preview!.Survivor.Version); Assert.Equal("1", after.NativePreview.Preview.Merged.Version);
        Assert.Equal(0, f.Audit.Writes);
    }

    [Fact]
    public async Task Purpose_preview_canonical_guid_order_is_explicit_even_when_sqlguid_order_is_different()
    {
        var survivor = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var merged = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var ids = new[] { survivor, merged };
        Assert.Equal(new[] { merged, survivor }, ids.Order());
        Assert.Equal(new[] { survivor, merged }, ids.OrderBy(x => new System.Data.SqlTypes.SqlGuid(x)));
        await using var f = await Fixture.Create();
        f.Db.Parties.AddRange(new Party(survivor, PartyCode.FromSequence(990001), PartyKind.LegalEntity, "SENTETIK ordered survivor"),
            new Party(merged, PartyCode.FromSequence(990002), PartyKind.LegalEntity, "SENTETIK ordered duplicate"));
        f.Db.CompanyPartyAccounts.AddRange(
            new CompanyPartyAccount(Guid.NewGuid(), A, survivor, PartyRole.Customer),
            new CompanyPartyAccount(Guid.NewGuid(), A, merged, PartyRole.Customer));
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var purpose = await PurposeService(f, new PurposeWriter()).PreviewPurposeAsync(survivor, new(merged, "1", "1"), _ => Task.CompletedTask);
        var impact = Assert.IsType<CounterpartyPartyMergeImpactPreviewV1>(purpose.NativePreview.Preview);
        Assert.Equal(new[] { merged, survivor }, impact.HoldingAccounts.Select(x => x.PartyId));
        Assert.Equal(IdempotencyHash.ComputePayload(impact), purpose.SeenImpactDigest);
        Assert.Equal(0, f.Audit.Writes);
        // InMemory validates the deterministic formatter contract; the real SQL source below exercises provider ordering.
    }

    [Theory]
    [InlineData("missing-writer")]
    [InlineData("nonrelational")]
    [InlineData("dirty")]
    [InlineData("audit")]
    public async Task Purpose_refuses_unsafe_entry_without_discarding_caller_state(string fault)
    {
        await using var f = await Fixture.Create(); var writer = new PurposeWriter { HasPendingDrafts = fault == "audit" };
        var caller = new Party(Guid.NewGuid(), PartyCode.FromSequence(44), PartyKind.LegalEntity, "SENTETIK caller", null);
        if (fault == "dirty") f.Db.Parties.Add(caller);
        var result = await PurposeService(f, fault == "missing-writer" ? null : writer).ApprovePurposeAsync(f.Survivor.Id,
            new(f.Merged.Id, "1", "1", "SENTETIK duplicate"), new string('A', 64), Metadata(f), _ => Task.CompletedTask);
        Assert.Equal(CounterpartyPartyMergePurposeStatusV1.Unavailable, result.Status); Assert.Null(result.Execution);
        Assert.Equal(0, writer.Discards); Assert.Equal(fault == "audit", writer.HasPendingDrafts);
        if (fault == "dirty") Assert.Equal(EntityState.Added, f.Db.Entry(caller).State);
        Assert.Empty(f.Db.PartyMerges); Assert.Empty(f.Db.CommandExecutions); Assert.Equal(0, f.Audit.Writes);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("reason")]
    [InlineData("digest")]
    public async Task Authorized_malformed_purpose_preserves_pending_caller_audit_and_native_validation(string fault)
    {
        await using var f = await Fixture.Create(); var writer = new PurposeWriter { HasPendingDrafts = true };
        await Assert.ThrowsAsync<ArgumentException>(() => PurposeService(f, writer).ApprovePurposeAsync(f.Survivor.Id,
            new(f.Merged.Id, fault == "version" ? "0" : "1", "1", fault == "reason" ? "" : "SENTETIK duplicate"),
            fault == "digest" ? "bad" : new string('A', 64), Metadata(f), _ => Task.CompletedTask));
        Assert.True(writer.HasPendingDrafts); Assert.Equal(0, writer.Discards); Assert.Equal(0, f.Audit.Writes);
    }

    [Fact]
    public async Task Purpose_consumer_normal_completion_with_revoked_authority_never_releases_preview()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PurposeService(f, new PurposeWriter()).PreviewPurposeAsync(
            f.Survivor.Id, new(f.Merged.Id, "1", "1"), _ => { f.Authority.Answer = (_, _) => PermissionScope.Denied; return Task.CompletedTask; }));
        Assert.Equal(0, f.Audit.Writes);
    }

    [Fact]
    public async Task Unauthorized_malformed_purpose_does_not_disclose_validation_or_discard_caller_work()
    {
        await using var f = await Fixture.Create(); var writer = new PurposeWriter { HasPendingDrafts = true };
        var caller = new Party(Guid.NewGuid(), PartyCode.FromSequence(45), PartyKind.LegalEntity, "SENTETIK caller", null); f.Db.Parties.Add(caller);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => PurposeService(f, writer, false).ApprovePurposeAsync(f.Survivor.Id,
            new(Guid.Empty, "0", "0", ""), "bad", Metadata(f), _ => Task.CompletedTask));
        Assert.Equal(EntityState.Added, f.Db.Entry(caller).State); Assert.True(writer.HasPendingDrafts);
        Assert.Equal(0, writer.Discards); Assert.Equal(0, f.Audit.Writes);
    }

    private static CounterpartyCommandMetadata Metadata(Fixture f) => new(A, f.User.UserId!, "SENTETIK-correlation", null, "Web", null, null, "SENTETIK-merge-key");
    private static CounterpartyCommandService PurposeService(Fixture f, PurposeWriter? writer, bool authorized = true) =>
        new(f.Db, f.User, f.Company, new PurposeAuthorization(authorized), f.Authority, f.Audit, null!, null!,
            NullLogger<CounterpartyCommandService>.Instance, TimeProvider.System, identityDecisionAuditWriter: writer);
    private sealed class PurposeWriter : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; }
        public int Discards;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unit gate must not own SQL.");
        public void DiscardPending() { Discards++; HasPendingDrafts = false; }
    }
    private sealed class PurposeAuthorization(bool authorized = true) : IErpAuthorizationService
    {
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(authorized ? Granted() : PermissionScope.Denied);
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
