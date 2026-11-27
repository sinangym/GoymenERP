using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class NormalJournalDraftLifecycleV1Tests
{
    [Fact]
    public async Task Decision_read_preserves_native_twenty_fields_and_never_selects_a_period_or_writes()
    {
        var port = new DecisionProtocolPort(); var user = new ApprovalUser(); var authority = new ApprovalAuthority();
        var detail = await ApprovalService(port, port.Inner, new ApprovalCompany(Company), user, authority)
            .ReadDecisionAsync(port.Inner.Record.Id, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, detail.Status); var value = Assert.IsType<NormalJournalDraftDecisionDetailV1>(detail.Resource);
        Assert.Null(value.Draft.PeriodId); Assert.Null(value.Draft.PostedJournalId); Assert.Equal(1, value.Draft.Version);
        Assert.Equal(2, value.Draft.Lines.Count); Assert.Equal("10", value.Draft.Lines[0].Amount);
        Assert.Equal(NormalJournalDraftServiceV1.NormalSourceFactsDigest(port.Inner.Record), value.SourceFactsDigest);
        Assert.Equal(NormalJournalDraftServiceV1.NormalPreviewDigest(value.Draft), value.PreviewDigest);
        Assert.Equal(20, JsonSerializer.SerializeToElement(value.Draft).EnumerateObject().Count());
        Assert.Equal(0, port.Inner.Approvals); Assert.Equal(0, port.Inner.Completions);
    }

    [Theory]
    [InlineData("permission")][InlineData("scoped")][InlineData("actor")][InlineData("company")][InlineData("member")][InlineData("cancel")]
    public async Task Last_completed_consumer_callback_cannot_release_stale_native_authority_or_cancelled_data(string fault)
    {
        var port = new DecisionProtocolPort(); var user = new ApprovalUser(); var company = new ApprovalCompany(Company);
        var authority = new ApprovalAuthority(); using var cancellation = new CancellationTokenSource(); var calls = 0;
        var service = ApprovalService(port, port.Inner, company, user, authority);
        Task Callback(CancellationToken _)
        {
            if (++calls == 6) // initial, owner entry/header/lines/membership, final service release: callback completes without throwing.
            {
                if (fault == "permission") authority.Allowed = false;
                if (fault == "actor") user.ActorId = Guid.NewGuid();
                if (fault == "company") company.Set(Guid.NewGuid());
                if (fault == "member") user.Member = false;
                if (fault == "cancel") cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
        var actualAuthority = fault == "scoped" ? new DecisionScopedAuthority() : (IErpAuthorityRevalidationService)authority;
        if (fault == "scoped") service = ApprovalService(port, port.Inner, company, user, actualAuthority);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(port.Inner.Record.Id, Callback, cancellation.Token));
        else Assert.Equal(AccountingApiStatus.Denied, (await service.ReadDecisionAsync(port.Inner.Record.Id, Callback, cancellation.Token)).Status);
        Assert.Equal(0, port.Inner.Approvals); Assert.Equal(0, port.Inner.Completions);
    }

    [Fact]
    public async Task Decision_read_denies_the_actual_maker_and_create_only_or_unsupported_owner_ports()
    {
        var port = new DecisionProtocolPort();
        // Use the same member actor as the draft maker without changing production company membership.
        var makerPort = new DecisionProtocolPort(Record());
        Assert.Equal(AccountingApiStatus.Denied, (await ApprovalService(makerPort, makerPort.Inner,
            new ApprovalCompany(Company), new ApprovalUser(), new ApprovalAuthority()).ReadDecisionAsync(makerPort.Inner.Record.Id, _ => Task.CompletedTask)).Status);
        Assert.Equal(AccountingApiStatus.Denied, (await ApprovalService(port, port.Inner, new ApprovalCompany(Company), new ApprovalUser(),
            new PermissionAuthority(Granted(), PermissionScope.Denied)).ReadDecisionAsync(port.Inner.Record.Id, _ => Task.CompletedTask)).Status);
        Assert.Equal(AccountingApiStatus.Unavailable, (await Service(new MemoryStore(Record()), new PermissionAuthority(Granted(), Granted()))
            .ReadDecisionAsync(port.Inner.Record.Id, _ => Task.CompletedTask)).Status);
        Assert.Equal(0, port.Inner.Approvals);
    }

    [Theory]
    [InlineData("company")][InlineData("source-company")][InlineData("line-company")][InlineData("unknown-status")]
    public async Task Decision_read_never_releases_foreign_or_structurally_unbound_native_source(string fault)
    {
        var original = Record() with { PreparedByUserId = Guid.NewGuid() };
        var record = fault switch
        {
            "company" => original with { CompanyId = Guid.NewGuid() },
            "source-company" => original with { Request = original.Request with { Source = new GoymenERP.Modules.Accounting.Domain.Journals.SourceReference(Guid.NewGuid(), original.Request.Source.SourceId, original.Request.Source.PostingPurpose) } },
            "line-company" => original with { Request = original.Request with { Lines = original.Request.Lines.Select(line => line with
                { Account = new GoymenERP.Modules.Accounting.Domain.Journals.AccountRef(Guid.NewGuid(), line.Account!.Value.AccountId) }).ToArray() } },
            _ => original with { Status = (NormalJournalDraftStatusV1)999 },
        };
        var port = new DecisionProtocolPort(record);
        var result = await ApprovalService(port, port.Inner, new ApprovalCompany(Company), new ApprovalUser(), new ApprovalAuthority())
            .ReadDecisionAsync(record.Id, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Unavailable, result.Status); Assert.Null(result.Resource);
        Assert.Equal(0, port.Inner.Approvals); Assert.Equal(0, port.Inner.Completions);
    }

    [Fact]
    public async Task Genuine_original_command_receipt_binds_human_period_and_is_omitted_from_unchanged_native_wire()
    {
        var port = new DecisionProtocolPort(); var service = ApprovalService(port, port.Inner, new ApprovalCompany(Company), new ApprovalUser(), new ApprovalAuthority());
        var period = Guid.NewGuid(); const string key = "normal-decision-original";
        var first = await service.ApproveAsync(port.Inner.Record.Id, 1, period, key);
        var replay = await service.ApproveAsync(port.Inner.Record.Id, 1, period, key);
        var receipt = Assert.IsType<NormalJournalDraftApprovalReceiptV1>(first.Resource!.ApprovalReceipt);
        Assert.Equal(period, receipt.PeriodId); Assert.Equal(1, receipt.ExpectedVersion); Assert.Equal(2, receipt.ResultVersion);
        Assert.Equal(Actor, receipt.ActorId); Assert.Equal(port.Inner.Record.Id, receipt.PostedJournalId);
        Assert.Equal(receipt, replay.Resource!.ApprovalReceipt); Assert.True(replay.IsReplay);
        Assert.Equal(receipt.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(1, port.Inner.Approvals); Assert.Equal(1, port.Inner.Completions);
        var json = JsonSerializer.Serialize(first, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("approvalReceipt", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, JsonSerializer.SerializeToElement(first).EnumerateObject().Count());
        Assert.Equal(20, JsonSerializer.SerializeToElement(first.Resource).EnumerateObject().Count());
        Assert.Null((await service.ApproveAsync(port.Inner.Record.Id, 1, Guid.NewGuid(), key)).Resource);
    }

    [Theory]
    [InlineData("command")][InlineData("key")][InlineData("actor")][InlineData("payload")][InlineData("company")]
    [InlineData("amount")][InlineData("period")][InlineData("version")][InlineData("resource")][InlineData("malformed")][InlineData("correlation")][InlineData("unknown-root")][InlineData("unknown-resource")][InlineData("private-proof")][InlineData("missing-membership")]
    public async Task Private_receipt_never_reconstructs_original_success_from_changed_source_or_malformed_command(string fault)
    {
        var port = new DecisionProtocolPort(); var service = ApprovalService(port, port.Inner, new ApprovalCompany(Company), new ApprovalUser(), new ApprovalAuthority());
        var period = Guid.NewGuid(); const string key = "normal-receipt-negative";
        var result = await service.ApproveAsync(port.Inner.Record.Id, 1, period, key);
        var command = Assert.IsType<AccountingCommandRecord>(await port.Inner.FindCommandAsync(NormalJournalCommandNamesV1.ApproveDraft, NormalJournalDraftServiceV1.ApprovalKeyHash(key)));
        var source = port.Inner.Record;
        command = fault switch
        {
            "command" => command with { CommandName = "different" }, "key" => command with { IdempotencyKeyHash = new string('A', 64) },
            "actor" => command with { ActorId = Guid.NewGuid().ToString("D") }, "payload" => command with { RequestHash = new string('B', 64) },
            "correlation" => command with { OriginalCorrelationId = "" }, "malformed" => command with { ResultJson = "{" },
            "resource" => command with { ResultJson = "{\"status\":0}" }, _ => command,
        };
        if (fault is "unknown-root" or "unknown-resource" or "private-proof")
        {
            var json = JsonNode.Parse(command.ResultJson)!;
            if (fault == "unknown-root") json["unexpected"] = true;
            else json["resource"]![fault == "private-proof" ? "approvalReceipt" : "unexpected"] = true;
            command = command with { ResultJson = json.ToJsonString() };
        }
        if (fault == "company") source = source with { CompanyId = Guid.NewGuid() };
        if (fault == "amount") source = source with { Request = source.Request with { Lines = source.Request.Lines.Select(x => x with { Amount = 11m }).ToArray() } };
        Assert.Null(NormalJournalDraftServiceV1.BindNormalApprovalReceipt(command, source, result,
            fault == "version" ? 2 : 1, fault == "period" ? Guid.NewGuid() : period, key, Actor, fault == "missing-membership" ? null : false));
        Assert.Equal(1, port.Inner.Approvals); Assert.Equal(1, port.Inner.Completions);
    }

    // Explicit synthetic native protocol fixture. No relational commit or Audit rollback claim.
    private sealed class DecisionProtocolPort(NormalJournalDraftRecordV1? initial = null)
        : IAccountingNormalJournalDraftStore, IAccountingNormalJournalDraftDecisionStoreV1
    {
        public MemoryStore Inner { get; } = new(initial ?? Record() with { PreparedByUserId = Guid.Parse("a0000000-0000-4000-8000-0000000000b2") });
        public Task<AccountingApiResult<NormalJournalDraftViewV1>> RunNormalApprovalAsync(Func<bool, Task<AccountingApiResult<NormalJournalDraftViewV1>>> attempt,
            Func<CancellationToken, Task> fence, CancellationToken token = default) => Inner.RunNormalApprovalAsync(attempt, fence, token);
        public async Task<NormalJournalDraftDecisionSourceV1?> ReadNormalDraftDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); var value = await Inner.FindNormalDraftAsync(id, token); await fence(token); await fence(token); await fence(token); return value is null ? null : new(value, false); }
        public Task<NormalJournalDraftRecordV1?> FindNormalDraftAsync(Guid id, CancellationToken token = default) => Inner.FindNormalDraftAsync(id, token);
        public Task<(NormalJournalDraftRecordV1 Record, AccountingWriteOutcome Outcome)> CreateNormalDraftAsync(NormalJournalDraftV1 draft,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => Inner.CreateNormalDraftAsync(draft, fence, metadata, token);
        public Task<NormalJournalDraftRecordV1> ApproveAndPostNormalDraftAsync(Guid id, long version, Guid period, Guid actor,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => Inner.ApproveAndPostNormalDraftAsync(id, version, period, actor, fence, metadata, token);
    }
    private sealed class DecisionScopedAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, false, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
}
