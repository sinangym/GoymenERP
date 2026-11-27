using System.Text;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

// Controlled native-port/host boundary; no HTTP, actual Identity, SQL or runtime acceptance claim.
public sealed class WorkOfficialInvoiceImportDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Id = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker", Key = "SENTETIK-original-key", Correlation = "SENTETIK-work";
    private static FinanceOfficialInvoiceImportPendingBatchDetailV1 Detail(bool terminal = false, int rows = 1)
    {
        var csv = new StringBuilder(FinanceOfficialInvoiceImportTemplateV1.Header);
        for (var i = 0; i < rows; i++) csv.Append('\n').Append($"{Guid.NewGuid():D},{Guid.NewGuid():D},{Guid.NewGuid():D},invoice-v1,{Guid.NewGuid():D},{Guid.NewGuid():D},{Guid.NewGuid():D},001250.5000,2026-12-31");
        var preview = new FinanceOfficialInvoiceImportParserV1().Preview(new(Company, Actor, Correlation, null), new(Id, "batch-v1", Encoding.UTF8.GetBytes(csv.ToString())));
        Assert.True(preview.IsValid);
        return new(new(Id, Company, "batch-v1", preview.SourceSha256, preview.TemplateVersion,
            terminal ? FinanceOfficialInvoiceImportApprovalStatusV1.Approved : FinanceOfficialInvoiceImportApprovalStatusV1.Pending,
            terminal ? "2" : "1", rows, Maker, At, terminal ? Actor : null, terminal ? At.AddHours(1) : null), preview);
    }
    private static FinanceOfficialInvoiceImportApprovalResultV1 Answer(FinanceOfficialInvoiceImportPendingBatchDetailV1 detail, bool replay = false)
    {
        var b = detail.Batch with { Version = "2", Status = FinanceOfficialInvoiceImportApprovalStatusV1.Approved, ApprovedBy = Actor, ApprovedAtUtc = At.AddHours(1) };
        var original = replay ? "SENTETIK-original-correlation" : Correlation;
        return new(CommandOutcome.Succeeded, b, replay, original, [])
        { DecisionReceipt = new(Company, Id, Guid.NewGuid(), FinanceOfficialInvoiceImportApprovalServiceV1.ApproveCommand,
            IdempotencyHash.ComputeKey(Key), FinanceOfficialInvoiceImportApprovalServiceV1.ApprovalPayloadHash(new(Id, "1")), Actor, "1", "2", original, At.AddHours(1)) };
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_approval_and_terminal_old_key_replay_preserve_frozen_version_key_and_original_receipt(bool replay)
    {
        var run = new Rig(replay); var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, default);
        Assert.False(preview.ApprovalReasonRequired); Assert.False(preview.RejectionSupported);
        Assert.Equal(run.Owner.Detail, preview.OfficialInvoiceImport);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, new("1", true, null), Key, default);
        Assert.Equal(("Approved", replay, run.Owner.Answer.OriginalCorrelationId), (result.State, result.Replayed, result.OriginalCorrelationId));
        Assert.Equal((Id, "1", Key, 1), (run.Owner.Request!.BatchId, run.Owner.Request.ExpectedVersion, run.Owner.Key, run.Owner.Calls));
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("batch")] [InlineData("approval")]
    [InlineData("command")] [InlineData("key")] [InlineData("hash")] [InlineData("actor")]
    [InlineData("expected")] [InlineData("result-version")] [InlineData("correlation")] [InlineData("time")]
    [InlineData("status")] [InlineData("checksum")] [InlineData("maker")] [InlineData("fresh-on-terminal")]
    public async Task Missing_forged_or_reconstructed_current_state_is_not_an_approval_receipt(string fault)
    {
        var run = new Rig(fault == "fresh-on-terminal"); var a = run.Owner.Answer; var p = a.DecisionReceipt!;
        p = fault switch { "company" => p with { CompanyId = Guid.NewGuid() }, "batch" => p with { BatchId = Guid.NewGuid() },
            "approval" => p with { ApprovalId = Guid.Empty }, "command" => p with { CommandName = FinanceOfficialInvoiceImportApprovalServiceV1.UploadCommand },
            "key" => p with { IdempotencyKeyHash = "wrong" }, "hash" => p with { PayloadHash = "wrong" },
            "actor" => p with { ActorId = Maker }, "expected" => p with { ExpectedVersion = "2" },
            "result-version" => p with { ResultVersion = "3" }, "correlation" => p with { OriginalCorrelationId = "wrong" },
            "time" => p with { RecordedAtUtc = At }, _ => p };
        var resource = fault switch { "status" => a.Resource! with { Status = FinanceOfficialInvoiceImportApprovalStatusV1.Pending },
            "checksum" => a.Resource! with { BatchChecksum = new string('0', 64) }, "maker" => a.Resource! with { UploadedBy = "other-maker" }, _ => a.Resource! };
        run.Owner.Answer = a with { Resource = resource, DecisionReceipt = fault == "missing" ? null : p, IsReplay = fault != "fresh-on-terminal" && a.IsReplay };
        if (fault == "fresh-on-terminal") run.Owner.Answer = run.Owner.Answer with { IsReplay = false, OriginalCorrelationId = Correlation };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, new("1", true, null), Key, default))).Status);
    }

    [Theory]
    [InlineData(false, null, "1")] [InlineData(true, "note", "1")] [InlineData(true, null, "2")] [InlineData(true, null, "01")]
    public async Task Only_the_exact_native_approve_body_is_supported(bool approve, string? reason, string version)
    {
        var run = new Rig();
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, new(version, approve, reason), Key, default))).Status);
        Assert.Equal(0, run.Owner.Calls);
    }

    [Theory]
    [InlineData("foreign")] [InlineData("version")] [InlineData("checksum")] [InlineData("rows")] [InlineData("amount")]
    [InlineData("changed-valid-amount")]
    public async Task Human_preview_must_match_the_same_company_version_and_canonical_native_rows(string fault)
    {
        var run = new Rig();
        run.Owner.HumanChange = detail => fault switch {
            "foreign" => detail with { Batch = detail.Batch with { CompanyId = Guid.NewGuid() } },
            "version" => detail with { Batch = detail.Batch with { Version = "2" } },
            "checksum" => detail with { Preview = detail.Preview with { SourceSha256 = new string('0', 64) } },
            "rows" => detail with { Preview = detail.Preview with { Batch = detail.Preview.Batch! with { Rows = [] } } },
            "changed-valid-amount" => detail with { Preview = detail.Preview with { Batch = detail.Preview.Batch! with { Rows = [detail.Preview.Batch.Rows[0] with { Amount = "999.50" }] } } },
            _ => detail with { Preview = detail.Preview with { Batch = detail.Preview.Batch! with { Rows = [detail.Preview.Batch.Rows[0] with { Amount = "1e2" }] } } } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, default))).Status);
    }

    [Fact]
    public async Task Human_full_rows_are_not_capped_by_references_or_exposed_in_snapshot_inbox_command_or_native_result_wire()
    {
        var run = new Rig(rows: 13); var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, default);
        Assert.Equal(13, preview.OfficialInvoiceImport!.Preview.Batch!.Rows.Count);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, Correlation, null), Id, default);
        var snapshotWire = JsonSerializer.Serialize(snapshot, options);
        Assert.DoesNotContain("officialInvoiceImportProof", snapshotWire); Assert.DoesNotContain("001250.5000", snapshotWire);
        var inbox = await run.Source.ListPendingAsync(new(Company, Actor, Correlation, null), 20, default);
        Assert.Contains("/onaylarim/", Assert.Single(inbox.Items!).Link); // Existing legacy detail, not the staged purpose route.
        Assert.DoesNotContain("001250.5000", JsonSerializer.Serialize(inbox, options));
        Assert.DoesNotContain("decisionReceipt", JsonSerializer.Serialize(run.Owner.Answer, FinanceJson.CreateOptions()));
        Assert.Contains("001250.5000", JsonSerializer.Serialize(preview, options));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, null), options));
        Assert.Equal(4, body.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("officialInvoiceImport", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.BankAccount, Id, "SENTETIK", "1", true, false, [], []), options));
    }

    [Fact]
    public async Task Maker_foreign_company_revoked_authority_or_delegation_never_executes_a_new_native_decision()
    {
        var maker = new Rig(actor: Maker);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => maker.Host.PreviewAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, default))).Status);
        var run = new Rig(); run.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, new("1", true, null, Guid.NewGuid()), Key, default))).Status);
        Assert.Equal(0, run.Owner.Calls); run.Delegations.Live = true;
        run.Owner.BeforeReturn = () => run.Authority.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, new("1", true, null), Key, default))).Status);
    }

    [Theory]
    [InlineData(CommandOutcome.Denied, 403)] [InlineData(CommandOutcome.Unavailable, 503)]
    [InlineData(CommandOutcome.VersionConflict, 409)] [InlineData(CommandOutcome.ValidationFailed, 422)]
    public async Task Native_refusal_keeps_its_code_and_original_correlation_without_a_fake_success(CommandOutcome outcome, int status)
    {
        var run = new Rig(); run.Owner.Answer = new(outcome, null, true, "SENTETIK-original-refusal", [new("request", "SENTETIK-native-code")]);
        var refused = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.OfficialInvoiceImport, Id, new("1", true, null), Key, default));
        Assert.Equal(status, refused.Status); Assert.Equal("SENTETIK-native-code", refused.Code);
        Assert.True(refused.Replayed); Assert.Equal("SENTETIK-original-refusal", refused.OriginalCorrelationId);
    }

    private sealed class Rig
    {
        public readonly Owner Owner; public readonly IWorkApprovalDecisionSourceV1 Source; public readonly Authority Authority = new();
        public readonly Delegations Delegations = new(); public WorkDecisionHostV1 Host { get; }
        public Rig(bool replay = false, string actor = Actor, int rows = 1)
        {
            Owner = new(Detail(replay, rows)); Owner.Answer = Answer(Owner.Detail, replay);
            Source = new NativeApprovalWorkInboxSourceV1(new FinanceInvoiceImportPendingApprovalAdapterV1(Owner));
            var company = new CompanyContext(); company.Set(Company); var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null);
            var fence = new WorkDecisionExecutionFenceV1();
            Host = new(company, new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, actor), correlation,
                new WorkDecisionAuthorityV1(Authority, fence), [Source], Delegations, fence, null!, null!,
                officialInvoiceImports: Owner, officialInvoiceImportReads: Owner);
        }
    }
    private sealed class Owner(FinanceOfficialInvoiceImportPendingBatchDetailV1 detail) : IFinanceOfficialInvoiceImportApprovalServiceV1,
        IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1, IFinanceOfficialInvoiceImportDecisionReadServiceV1
    {
        public FinanceOfficialInvoiceImportPendingBatchDetailV1 Detail = detail;
        public FinanceOfficialInvoiceImportApprovalResultV1 Answer = null!; public int Calls, Reads; public string? Key;
        public FinanceOfficialInvoiceImportApproveRequestV1? Request; public Action? BeforeReturn;
        public Func<FinanceOfficialInvoiceImportPendingBatchDetailV1, FinanceOfficialInvoiceImportPendingBatchDetailV1>? HumanChange;
        public Task<FinanceOfficialInvoiceImportApprovalResultV1> ApproveAsync(FinanceContext c, CommandMetadata m, FinanceOfficialInvoiceImportApproveRequestV1 r, CancellationToken t)
        { Calls++; Key = m.IdempotencyKey; Request = r; BeforeReturn?.Invoke(); return Task.FromResult(Answer); }
        public Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1>> GetDecisionAsync(FinanceContext c, Guid id, CancellationToken t)
        { Reads++; return Task.FromResult(new ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1>(ReadOutcome.Found, Reads > 1 && HumanChange is not null ? HumanChange(Detail) : Detail)); }
        public Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1>> ListAsync(FinanceContext c, string? cursor, CancellationToken t) =>
            Task.FromResult(new ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1>(ReadOutcome.Found, new(Company, [Detail.Batch], null)));
        public Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1>> GetAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<FinanceOfficialInvoiceImportApprovalResultV1> UploadAsync(FinanceContext c, CommandMetadata m, FinanceOfficialInvoiceImportPreviewRequestV1 r, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) =>
            Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, FinanceOfficialInvoiceImportPermissionNamesV1.Approve, Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken t = default)
        { Assert.Equal(FinanceOfficialInvoiceImportPermissionNamesV1.Approve, permission); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
}
