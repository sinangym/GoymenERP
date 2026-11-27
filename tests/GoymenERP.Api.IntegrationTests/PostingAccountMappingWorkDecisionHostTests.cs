using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Real native lifecycle and owner source over an explicit synthetic protocol. No SQL/transaction/HTTP acceptance claim.
public sealed class PostingAccountMappingWorkDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ac510000-0000-4000-8000-000000000001"),
        Id = AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(1), Actor = Guid.Parse("ac510000-0000-4000-8000-000000000003"),
        Maker = Guid.Parse("ac510000-0000-4000-8000-000000000004"),
        Debit = Guid.Parse("ac510000-0000-4000-8000-000000000006"), Credit = Guid.Parse("ac510000-0000-4000-8000-000000000007");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Key = "SENTETIK-mapping-work", Correlation = "SENTETIK-mapping-work-correlation";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Full_native_mapping_and_original_active_receipt_survive_current_supersession_and_plan_replacement()
    {
        using var rig = new Rig();
        var preview = await rig.Preview(); Assert.True(preview.Pending); Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var human = Assert.IsType<WorkPostingAccountMappingPreviewV1>(preview.PostingAccountMapping);
        Assert.Equal("1", human.ExpectedVersion); Assert.Equal("0", human.ExpectedActiveMappingVersion); Assert.Equal("1", human.Mapping.MappingVersion);
        Assert.Equal(2, human.Mapping.Rows.Count); Assert.Equal(2, human.CurrentActiveAccountPlan.Accounts.Count);
        Assert.NotEqual(human.PreparedAccountPlan, human.CurrentActiveAccountPlan.Reference);
        var wire = JsonSerializer.SerializeToElement(human, Json); Assert.Equal(7, wire.EnumerateObject().Count());
        Assert.Equal(12, wire.GetProperty("mapping").EnumerateObject().Count()); Assert.Equal(8, wire.GetProperty("mapping").GetProperty("rows")[0].EnumerateObject().Count());
        Assert.Equal(JsonValueKind.String, wire.GetProperty("mapping").GetProperty("version").ValueKind);
        var snapshot = (await rig.Source.ReadDecisionAsync(new(Company, rig.User.UserId!, Correlation, null), rig.ItemId, default))!;
        Assert.True(snapshot.Item.Excludes(Maker.ToString("D"))); Assert.False(snapshot.Item.Excludes(rig.User.UserId!));
        Assert.False(JsonSerializer.SerializeToElement(snapshot, Json).TryGetProperty("postingAccountMappingProof", out _));
        var first = await rig.Decide(); Assert.Equal("Active", first.State); Assert.False(first.Replayed); Assert.Equal(Correlation, first.OriginalCorrelationId);
        Assert.Equal(1L, rig.Port.ExpectedRow); Assert.Equal(0L, rig.Port.ExpectedActive); Assert.Equal(1L, rig.Port.NativeId); Assert.Equal(Key, rig.Port.Key);
        rig.Port.Supersede(); rig.Port.Plan = rig.Port.Plan with { Reference = new("replacement-plan", new string('D', 64)) };
        var current = await rig.Preview(); Assert.False(current.Pending); Assert.Equal("3", current.SourceVersion);
        Assert.Equal("3", current.PostingAccountMapping!.Mapping.Version); Assert.Equal("2", current.PostingAccountMapping.ExpectedActiveMappingVersion);
        var beforeReplayReads = rig.Port.DecisionReads; var replay = await rig.Decide();
        Assert.True(replay.Replayed); Assert.Equal("Active", replay.State); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands); Assert.Equal(beforeReplayReads + 1, rig.Port.DecisionReads); // Source read only, no second POST human read.
        using var retained = JsonDocument.Parse(rig.Port.Commands[0].ResultJson);
        Assert.Equal(8, retained.RootElement.EnumerateObject().Count()); Assert.Equal(12, retained.RootElement.GetProperty("resource").EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Number, retained.RootElement.GetProperty("resource").GetProperty("version").ValueKind);
        Assert.DoesNotContain("activationReceipt", rig.Port.Commands[0].ResultJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, JsonSerializer.SerializeToElement(replay, Json).EnumerateObject().Count());
        Assert.Equal(5, JsonSerializer.SerializeToElement(rig.Request, Json).EnumerateObject().Count());
        Assert.Equal(4, JsonSerializer.SerializeToElement(new WorkDecisionRequestV1("1", true, null), Json).EnumerateObject().Count());
    }

    [Fact]
    public async Task Native_mapping_maker_is_blocked_before_Work_dispatch_while_direct_native_self_refusal_still_replays_durably()
    {
        using var rig = new Rig(); rig.User.Actor = Maker;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
        var first = await rig.Owner.ActivateAsync(1, new(1, 0), Key); var replay = await rig.Owner.ActivateAsync(1, new(1, 0), Key);
        Assert.Equal(AccountingApiStatus.Denied, first.Status); Assert.Equal(AccountingPostingAccountMappingCodesV1.SelfActivation, first.Code);
        Assert.Equal("activation", first.Field); Assert.False(first.IsReplay); Assert.True(replay.IsReplay); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Null(first.Resource); Assert.Null(replay.Resource); Assert.Equal(0, rig.Port.Effects); Assert.Single(rig.Port.Commands);
    }

    [Theory]
    [InlineData("0", "0")] [InlineData("01", "0")] [InlineData("+1", "0")] [InlineData("1\n", "0")]
    [InlineData("9223372036854775807", "0")] [InlineData("9223372036854775808", "0")]
    [InlineData("1", "-1")] [InlineData("1", "00")] [InlineData("1", "+0")] [InlineData("1", "0\n")]
    [InlineData("1", "9223372036854775808")] [InlineData("1\r", "0")] [InlineData("1\r\n", "0")]
    [InlineData("1", "0\r")] [InlineData("1", "0\r\n")] [InlineData("1", "")] [InlineData("", "0")]
    public async Task Canonical_incrementable_row_version_and_nonnegative_active_identity_are_required_before_source_read(string package, string active)
    {
        using var rig = new Rig(); var error = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with
            { ExpectedVersion = package, PostingAccountMappingSelection = new(active) }));
        Assert.Equal(400, error.Status); Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData(1L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue)]
    public async Task Reversible_native_long_identity_and_observed_Max_active_identity_are_not_unsafe_number_or_incremented(long nativeId)
    {
        using var rig = new Rig(nativeId: nativeId); rig.Port.ActiveVersion = long.MaxValue;
        if (nativeId == long.MaxValue) rig.Port.ActiveVersion = 0;
        var active = rig.Port.ActiveVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var request = rig.Request with { PostingAccountMappingSelection = new(active) };
        var preview = await rig.Preview(); Assert.Equal(nativeId.ToString(System.Globalization.CultureInfo.InvariantCulture), preview.PostingAccountMapping!.Mapping.MappingVersion);
        var first = await rig.Decide(request); Assert.False(first.Replayed); Assert.Equal(nativeId, rig.Port.NativeId); Assert.Equal(1L, rig.Port.ExpectedRow);
        Assert.Equal(long.Parse(active, System.Globalization.CultureInfo.InvariantCulture), rig.Port.ExpectedActive);
        Assert.Equal("2", (await rig.Preview()).SourceVersion); Assert.True((await rig.Decide(request)).Replayed); Assert.Equal(1, rig.Port.Effects);
    }

    [Theory] [InlineData("reject")] [InlineData("reason")] [InlineData("missing-selection")]
    [InlineData("normal")] [InlineData("period")] [InlineData("package")] [InlineData("manufacturing")]
    [InlineData("loss")] [InlineData("overage")] [InlineData("plan")] [InlineData("supplier")] [InlineData("return")] [InlineData("catalog")]
    public async Task Only_approve_null_reason_and_the_mapping_selection_reach_the_owner(string fault)
    {
        using var rig = new Rig(); var request = fault switch
        {
            "reject" => rig.Request with { Approve = false }, "reason" => rig.Request with { Reason = "" },
            "missing-selection" => rig.Request with { PostingAccountMappingSelection = null },
            "catalog" => rig.Request with { ApprovedSourceCatalogSelection = new("0") },
            "normal" => rig.Request with { NormalJournalDraft = new(Guid.NewGuid()) }, "period" => rig.Request with { AccountingPeriodReopening = new("1") },
            "package" => rig.Request with { ProductPackageStandard = new("2026-11-27") }, "manufacturing" => rig.Request with { ManufacturingPlanReapproval = new([]) },
            "loss" => rig.Request with { InventoryTransferLoss = new(1, At) }, "overage" => rig.Request with { InventoryTransferOverage = new(1, At) },
            "plan" => rig.Request with { LogisticsPlanExcess = new("1") }, "supplier" => rig.Request with { SupplierReturnCostException = new() },
            _ => rig.Request with { LogisticsCustomerReturn = new(Guid.NewGuid()) },
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Effects);
    }

    [Fact]
    public async Task New_selection_is_rejected_for_every_existing_kind_before_any_source_read()
    {
        using var rig = new Rig();
        foreach (var kind in WorkDecisionHostV1.Kinds.Where(kind => kind != WorkDecisionHostV1.PostingAccountMapping))
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(kind, Id, rig.Request, Key, default))).Status);
        Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("maker")] [InlineData("version")]
    [InlineData("status")] [InlineData("active-negative")] [InlineData("digest-shape")] [InlineData("preview-shape")]
    [InlineData("caption")] [InlineData("reference")] [InlineData("rule")]
    public async Task Private_scalar_identity_and_owner_formatter_are_checked_before_native_dispatch(string fault)
    {
        using var rig = new Rig(); rig.Source.Change = s =>
        {
            var p = s.PostingAccountMappingProof!;
            return s with { PostingAccountMappingProof = fault switch
            {
                "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "id" => p with { MappingVersion = 9 },
                "maker" => p with { PreparedBy = Actor }, "version" => p with { Version = 7 }, "status" => p with { Status = "Active" },
                "active-negative" => p with { ActiveMappingVersion = -1 }, "digest-shape" => p with { SourceFactsDigest = "bad" },
                "preview-shape" => p with { PreviewDigest = "bad" }, _ => p,
            }, Item = s.Item with { DocumentLabel = fault == "caption" ? "foreign" : s.Item.DocumentLabel,
                References = fault == "reference" ? [s.Item.References[0] with { RecordId = Guid.NewGuid() }] : s.Item.References,
                Rules = fault == "rule" ? [s.Item.Rules[0] with { Threshold = "1" }] : s.Item.Rules } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }

    [Fact]
    public async Task Private_mapping_proof_on_another_kind_and_polluted_exclusion_metadata_are_refused()
    {
        using var rig = new Rig(); var snapshot = await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default);
        var other = new FixedSource(snapshot!);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.HostWith(other).PreviewAsync(other.Kind, Id, default))).Status);
        rig.Source.Change = s => s with { Item = s.Item with { ExcludedDeciders = [Actor.ToString("D")] } };
        // The owner's closed formatter has no exclusions; this polluted source is unavailable before authority dispatch.
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Effects);
    }

    [Fact]
    public async Task A_catalog_private_proof_cannot_be_attached_to_the_mapping_snapshot()
    {
        using var rig = new Rig(); rig.Source.Change = snapshot => snapshot with
        { ApprovedSourceCatalogProof = new(Company, Guid.NewGuid(), Maker, At, "Prepared", 1, 0, new string('A', 64), new string('B', 64)) };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("body")] [InlineData("company")] [InlineData("active-version")] [InlineData("prepared-plan")] [InlineData("current-plan")]
    public async Task Human_read_must_match_the_source_snapshot_including_full_mapping_both_plans_and_active_identity(string fault)
    {
        using var rig = new Rig(); rig.Port.ReadChange = (source, count) => count != 2 ? source : fault switch
        {
            "company" => source with { Record = source.Record with { CompanyId = Guid.NewGuid() } },
            "active-version" => source with { ActiveMappingVersion = 9 },
            "prepared-plan" => source with { Record = source.Record with { AccountPlanSourceVersion = "SENTETIK-other" } },
            "current-plan" => source with { CurrentActiveAccountPlan = source.CurrentActiveAccountPlan with { Accounts = [source.CurrentActiveAccountPlan.Accounts[0] with { Name = "changed name" }, source.CurrentActiveAccountPlan.Accounts[1]] } },
            _ => source with { Record = source.Record with { Rows = [source.Record.Rows[0] with { AccountCode = "SENTETIK-other" }, source.Record.Rows[1]] } },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("missing")] [InlineData("key")] [InlineData("payload")] [InlineData("company")] [InlineData("actor")]
    [InlineData("version")] [InlineData("active-version")] [InlineData("correlation")] [InlineData("digest")] [InlineData("source-digest")]
    [InlineData("identity")] [InlineData("command")]
    public async Task Post_native_invalid_private_receipt_is_unknown_outcome_then_same_key_reconciles_once(string fault)
    {
        using var rig = new Rig(); rig.Port.AfterAnswer = answer =>
        {
            var v = answer.Resource!; var p = v.ActivationReceipt!;
            p = fault switch { "identity" => p with { MappingVersion = 9 }, "command" => p with { CommandName = "other-command" }, "key" => p with { IdempotencyKeyHash = "wrong" }, "payload" => p with { PayloadHash = "wrong" },
                "company" => p with { CompanyId = Guid.NewGuid() }, "actor" => p with { ActorId = Maker },
                "version" => p with { ResultVersion = 9 }, "active-version" => p with { ExpectedActiveMappingVersion = 9 },
                "correlation" => p with { OriginalCorrelationId = "foreign" }, "digest" => p with { OriginalResultDigest = new string('B', 64) },
                "source-digest" => p with { SourceFactsDigest = new string('B', 64) }, _ => p };
            return answer with { Resource = v with { ActivationReceipt = fault == "missing" ? null : p } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands); // Suppressed response does not undo this synthetic completed decision.
        rig.Port.AfterAnswer = null; rig.Port.Supersede();
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
    }

    [Fact]
    public async Task Valid_shape_source_digest_tampering_is_post_native_suppression_not_a_precommand_rollback_claim()
    {
        using var rig = new Rig(); rig.Source.Change = s => s with { PostingAccountMappingProof = s.PostingAccountMappingProof! with { SourceFactsDigest = new string('B', 64) } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(1, rig.Port.Effects);
        rig.Source.Change = null; Assert.True((await rig.Decide()).Replayed); Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
    }

    [Fact]
    public async Task Stale_pending_active_version_and_its_original_terminal_key_are_owned_by_native_not_current_projection()
    {
        using var rig = new Rig(); rig.Port.ActiveVersion = 2;
        var first = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide()); Assert.Equal(409, first.Status); Assert.False(first.Replayed);
        rig.Port.ActiveVersion = 3;
        var replay = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide()); Assert.Equal(409, replay.Status); Assert.True(replay.Replayed);
        Assert.Equal(Correlation, replay.OriginalCorrelationId); Assert.Equal(0, rig.Port.Effects); Assert.Single(rig.Port.Commands);
    }

    [Fact]
    public async Task A_new_key_cannot_activate_a_superseded_mapping_again()
    {
        using var rig = new Rig(); await rig.Decide(); rig.Port.Supersede();
        var request = rig.Request with { ExpectedVersion = "3", PostingAccountMappingSelection = new("2") };
        var refusal = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(
            WorkDecisionHostV1.PostingAccountMapping, Id, request, Key + "-new", default));
        Assert.Equal(409, refusal.Status); Assert.Equal(1, rig.Port.Effects); Assert.Equal(2, rig.Port.Commands.Count);
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Completed_native_decision_is_not_released_after_authority_or_cancellation_loss(string fault)
    {
        using var rig = new Rig(); using var cancellation = new CancellationTokenSource();
        rig.Port.AfterAnswer = answer => { if (fault == "permission") rig.Authority.Allowed = false;
            if (fault == "company") rig.Company.Set(Guid.NewGuid()); if (fault == "actor") rig.User.Actor = Maker;
            if (fault == "member") rig.User.Member = false; if (fault == "delegation") rig.Delegations.Live = false;
            if (fault == "cancel") cancellation.Cancel(); return answer; };
        var request = rig.Request with { DelegationId = Guid.NewGuid() };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Decide(request, cancellation.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands); // Response suppression is not a rollback claim.
        rig.Port.AfterAnswer = null; rig.Authority.Allowed = true; rig.Company.Set(Company); rig.User.Actor = Actor;
        rig.User.Member = true; rig.Delegations.Live = true;
        var replay = await rig.Decide(request); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Native_await_fence_rechecks_captured_authority_and_explicit_delegation_before_effect(string fault)
    {
        using var rig = new Rig(); using var cancellation = new CancellationTokenSource();
        rig.Port.BeforeActivate = () => { if (fault == "permission") rig.Authority.Allowed = false; if (fault == "company") rig.Company.Set(Guid.NewGuid());
            if (fault == "actor") rig.User.Actor = Maker; if (fault == "member") rig.User.Member = false;
            if (fault == "delegation") rig.Delegations.Live = false; if (fault == "cancel") cancellation.Cancel(); };
        var request = rig.Request with { DelegationId = Guid.NewGuid() };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Decide(request, cancellation.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData(AccountingApiStatus.Denied, 403)] [InlineData(AccountingApiStatus.Unavailable, 503)]
    [InlineData(AccountingApiStatus.Conflict, 409)] [InlineData(AccountingApiStatus.NotFound, 404)]
    public async Task Native_refusals_propagate_and_missing_host_owner_never_dispatches(AccountingApiStatus status, int expected)
    {
        using var rig = new Rig(); rig.Port.Refusal = new(status, null, false, null);
        Assert.Equal(expected, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Effects);
        using var missing = new Rig(false);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => missing.Preview())).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => missing.Decide())).Status); Assert.Equal(0, missing.Port.Effects);
    }

    private sealed class Rig : IDisposable
    {
        public MutableCompany Company { get; } = new(); public MutableUser User { get; } = new(); public Authority Authority { get; } = new();
        public Delegations Delegations { get; } = new(); public SyntheticMappingProtocolPort Port { get; } = new(); public SourceProxy Source { get; }
        public WorkDecisionHostV1 Host { get; } public AccountingPostingAccountMappingServiceV1 Owner { get; }
        private readonly AccountingDbContext db; private readonly CorrelationContext correlation = new();
        private readonly WorkDecisionExecutionFenceV1 execution = new(); private readonly WorkDecisionAuthorityV1 authority;
        public Rig(bool inject = true, long nativeId = 1)
        {
            Company.Set(PostingAccountMappingWorkDecisionHostTests.Company); correlation.Set(Correlation, null, "Web", null, null);
            Port.Record = Port.Record with { MappingVersion = nativeId }; authority = new(Authority, execution);
            Owner = new(Port, Port, authority, Company, User, correlation, new FixedTime());
            db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("mapping-work-" + Guid.NewGuid()).Options, Company);
            Source = new(new AccountingPostingAccountMappingWorkInboxSourceV1(db, new(Company, User, authority), Owner));
            Host = new(Company, User, correlation, authority, [Source], Delegations, execution, null!, null!, postingAccountMappings: inject ? Owner : null);
        }
        public Guid ItemId => AccountingPostingAccountMappingDecisionBindingV1.ToWorkItemId(Port.Record.MappingVersion);
        public WorkDecisionRequestV1 Request => new("1", true, null, PostingAccountMappingSelection: new("0"));
        public Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(WorkDecisionHostV1.PostingAccountMapping, ItemId, default);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request = null, CancellationToken token = default) => Host.DecideAsync(WorkDecisionHostV1.PostingAccountMapping, ItemId, request ?? Request, Key, token);
        public WorkDecisionHostV1 HostWith(IWorkInboxSourceV1 source) => new(Company, User, correlation, authority, [source], Delegations, execution, null!, null!, postingAccountMappings: Owner);
        public void Dispose() => db.Dispose();
    }
    // Explicit algorithm/command-log protocol seam. It does not prove relational commit, rollback or real Audit.
    private sealed class SyntheticMappingProtocolPort : IAccountingPostingAccountMappingStoreV1, IAccountingPostingAccountMappingDecisionStoreV1, IAccountingCommandLog
    {
        public AccountingPostingAccountMappingRecordV1 Record = new(Company, 1, new DateOnly(2026, 11, 27), AccountingPostingAccountMappingStatusV1.Prepared, 1,
            "SENTETIK-prepared-plan", new string('E', 64), Maker, At, null, null,
            [new(1, AccountingPostingAccountMappingPurposesV1.SalesInvoice, AccountingPostingAccountMappingKeysV1.Receivable, null, null, null, Debit, "120.01"),
             new(2, AccountingPostingAccountMappingPurposesV1.SalesInvoice, AccountingPostingAccountMappingKeysV1.Revenue, null, null, null, Credit, "600.01")]);
        public AccountingPostingMappingDecisionPlanV1 Plan = new(Company, new("SENTETIK-current-plan", new string('F', 64)),
            [new(Debit, "120.02", "SENTETIK current debit"), new(Credit, "600.02", "SENTETIK current credit")]);
        public long ActiveVersion, ExpectedRow, ExpectedActive, NativeId; public int Effects, DecisionReads; public string? Key; public Action? BeforeActivate;
        public List<AccountingCommandRecord> Commands { get; } = [];
        public Func<AccountingPostingAccountMappingDecisionSourceV1, int, AccountingPostingAccountMappingDecisionSourceV1>? ReadChange;
        public Func<AccountingApiResult<AccountingPostingAccountMappingViewV1>, AccountingApiResult<AccountingPostingAccountMappingViewV1>>? AfterAnswer;
        public AccountingApiResult<AccountingPostingAccountMappingViewV1>? Refusal;
        public async Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> RunActivationAsync(Func<bool, Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>>> attempt,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            if (Refusal is not null) return Refusal;
            try { var result = await attempt(false); await fence(token); return AfterAnswer?.Invoke(result) ?? result; }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); }
        }
        public async Task<AccountingPostingAccountMappingRecordV1> ActivateAsync(long id, long version, long active, Guid actor, DateTimeOffset at,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            BeforeActivate?.Invoke(); await fence(token); token.ThrowIfCancellationRequested(); NativeId = id; ExpectedRow = version; ExpectedActive = active; Key = metadata.IdempotencyKey;
            if (Record.MappingVersion != id) throw new AccountingPostingAccountMappingRefusalException(AccountingPostingAccountMappingCodesV1.NotFound);
            if (Record.Version != version) throw new AccountingVersionConflictException(version, Record.Version);
            if (Record.Status != AccountingPostingAccountMappingStatusV1.Prepared) throw new AccountingPostingAccountMappingRefusalException(AccountingPostingAccountMappingCodesV1.NotPrepared);
            if (Record.PreparedByUserId == actor) throw new AccountingPostingAccountMappingRefusalException(AccountingPostingAccountMappingCodesV1.SelfActivation, "activation");
            if (ActiveVersion != active) throw new AccountingPostingAccountMappingSequenceConflictException("expectedActiveMappingVersion", active, ActiveVersion);
            Record = Record with { Status = AccountingPostingAccountMappingStatusV1.Active, Version = checked(version + 1), ActivatedByUserId = actor, ActivatedAtUtc = at };
            ActiveVersion = id; Effects++; Commands.Add(metadata.Completion!(Record)); return Record;
        }
        public async Task<AccountingPostingAccountMappingDecisionSourceV1?> ReadActivationDecisionAsync(long id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); DecisionReads++; var source = new AccountingPostingAccountMappingDecisionSourceV1(Record, ActiveVersion, Plan); source = ReadChange?.Invoke(source, DecisionReads) ?? source; await fence(token); return id == Record.MappingVersion ? source : null; }
        public async Task<AccountingPostingAccountMappingRecordV1?> ReadActivationReceiptSourceAsync(long id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); var result = id == Record.MappingVersion ? Record : null; await fence(token); return result; }
        public void Supersede() { Record = Record with { Status = AccountingPostingAccountMappingStatusV1.Superseded, Version = 3 }; ActiveVersion = Record.MappingVersion == 1 ? 2 : 1; }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(row => row.CommandName == command && row.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token = default) { Commands.Add(record); return Task.CompletedTask; }
        public Task<AccountingPostingAccountMappingRecordV1?> ReadAsync(long id, CancellationToken token = default) => throw new InvalidOperationException("No general mapping read");
        public Task<AccountingPostingAccountMappingRecordV1?> ReadActiveAsync(CancellationToken token = default) => throw new InvalidOperationException("No general active mapping read");
        public Task<AccountingPostingAccountMappingRecordV1> PrepareAsync(long latest, DateOnly from, IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> rows, Guid actor, DateTimeOffset at,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new InvalidOperationException("No prepare");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public int Reads; public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int count, CancellationToken token) => inner.ListPendingAsync(context, count, token);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token)
        { Reads++; var source = await inner.ReadDecisionAsync(context, id, token); return source is null ? null : Change?.Invoke(source) ?? source; }
    }
    private sealed class FixedSource(WorkApprovalDecisionSnapshotV1 snapshot) : IWorkApprovalDecisionSourceV1
    {
        public string Module => "finance"; public string Kind => "finance.cash-account.v1"; public string ApproverPermission => AccountingPostingAccountMappingPermissionNamesV1.Activate;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int count, CancellationToken t) => throw new InvalidOperationException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot with { Item = snapshot.Item with { Module = Module, Kind = Kind } });
    }
    private sealed class MutableCompany : ICompanyContext { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser { public Guid Actor = PostingAccountMappingWorkDecisionHostTests.Actor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorityRevalidationService { public bool Allowed = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) { Assert.Equal(AccountingPostingAccountMappingPermissionNamesV1.Activate, permission); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
    private sealed class Delegations : IWorkDelegationReaderV1 { public bool Live = true; public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, AccountingPostingAccountMappingPermissionNamesV1.Activate, Maker, Actor, At, At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null); }
    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => At.AddMinutes(1); }
}
