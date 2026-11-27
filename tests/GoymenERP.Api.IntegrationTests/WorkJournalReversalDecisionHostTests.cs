using System.Text.Json;
using System.Text.Json.Nodes;
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

// Actual native lifecycle over a controlled protocol port. These cases do not prove relational rollback or an actual HTTP journey.
public sealed class WorkJournalReversalDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ac500000-0000-4000-8000-000000000001"), Id = Guid.Parse("ac500000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("ac500000-0000-4000-8000-000000000003"), Maker = Guid.Parse("ac500000-0000-4000-8000-000000000004"),
        SourceJournal = Guid.Parse("ac500000-0000-4000-8000-000000000005"), Period = Guid.Parse("ac500000-0000-4000-8000-000000000006"),
        CancellationActor = Guid.Parse("ac500000-0000-4000-8000-000000000007");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Key = "journal-reversal-work-original", Correlation = "SENTETIK-journal-reversal-work";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Full_human_native_sixteen_fields_and_private_actor_evidence_preserve_original_approval_and_replay(bool cancellationSource)
    {
        using var rig = new Rig(cancellationSource); var preview = await rig.Preview();
        Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var human = Assert.IsType<WorkJournalReversalDecisionPreviewV1>(preview.AccountingJournalReversal);
        Assert.Equal(Id, human.Proposal.ProposalId); Assert.Equal(SourceJournal, human.Proposal.SourceJournalId);
        Assert.Equal(Period, human.Proposal.PeriodId); Assert.Equal("SENTETIK retained reversal reason", human.Proposal.Reason);
        var wire = JsonSerializer.SerializeToElement(human, Json);
        Assert.Equal(3, wire.EnumerateObject().Count()); Assert.Equal(16, wire.GetProperty("proposal").EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Number, wire.GetProperty("proposal").GetProperty("version").ValueKind);
        Assert.DoesNotContain("cancellationSourceActor", wire.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var snapshot = await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default);
        Assert.Equal<Guid?>(cancellationSource ? CancellationActor : null, snapshot!.JournalReversalProof!.CancellationSourceActorId);
        Assert.DoesNotContain("journalReversalProof", JsonSerializer.Serialize(snapshot, Json), StringComparison.OrdinalIgnoreCase);
        rig.Port.Reads = 0;
        var first = await rig.Decide(); Assert.Equal("Approved", first.State); Assert.False(first.Replayed);
        Assert.Equal(Correlation, first.OriginalCorrelationId); Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
        Assert.Equal(2, rig.Port.Reads); // Owner source + native receipt source; POST never performs the human GET again.
        Assert.Equal(1, rig.Port.Expected); Assert.Equal(Key, rig.Port.Key);
        var command = Assert.Single(rig.Port.Commands); using var native = JsonDocument.Parse(command.ResultJson);
        Assert.Equal(8, native.RootElement.EnumerateObject().Count()); Assert.Equal(16, native.RootElement.GetProperty("resource").EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Number, native.RootElement.GetProperty("resource").GetProperty("version").ValueKind);
        Assert.DoesNotContain("approvalReceipt", command.ResultJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, JsonSerializer.SerializeToElement(rig.Request, Json).EnumerateObject().Count());
        var terminal = await rig.Preview(); Assert.False(terminal.Pending); Assert.Equal("2", terminal.SourceVersion);
        Assert.Equal(2, terminal.AccountingJournalReversal!.Proposal.Version);
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData(false, null, "1")] [InlineData(true, "", "1")] [InlineData(true, "note", "1")]
    [InlineData(true, null, "0")] [InlineData(true, null, "01")] [InlineData(true, null, "1\n")]
    [InlineData(true, null, "+1")] [InlineData(true, null, "2147483647")] [InlineData(true, null, "2147483648")]
    public async Task Only_native_incrementable_canonical_Int32_approve_and_null_reason_reach_the_owner(bool approve, string? reason, string version)
    {
        using var rig = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with
            { Approve = approve, Reason = reason, ExpectedVersion = version }))).Status);
        Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Approvals); Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData("period")] [InlineData("normal")] [InlineData("loss")] [InlineData("overage")]
    [InlineData("manufacturing")] [InlineData("package")] [InlineData("customer-return")] [InlineData("plan-excess")] [InlineData("supplier-return")]
    public async Task No_other_owner_selection_is_accepted_by_the_four_field_reversal_command(string selection)
    {
        using var rig = new Rig(); var request = selection switch
        {
            "period" => rig.Request with { AccountingPeriodReopening = new("3") }, "normal" => rig.Request with { NormalJournalDraft = new(Period) },
            "loss" => rig.Request with { InventoryTransferLoss = new(1, At) }, "overage" => rig.Request with { InventoryTransferOverage = new(1, At) },
            "manufacturing" => rig.Request with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) },
            "package" => rig.Request with { ProductPackageStandard = new("2026-11-27") },
            "customer-return" => rig.Request with { LogisticsCustomerReturn = new(Guid.NewGuid()) },
            "plan-excess" => rig.Request with { LogisticsPlanExcess = new("1") }, _ => rig.Request with { SupplierReturnCostException = new() },
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Approvals);
    }

    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("source-id")] [InlineData("period-id")]
    [InlineData("maker")] [InlineData("version")] [InlineData("digest-shape")] [InlineData("preview-shape")] [InlineData("caption")]
    [InlineData("ref-id")] [InlineData("ref-link")] [InlineData("rule")] [InlineData("threshold")] [InlineData("cancel-actor")]
    public async Task Private_identity_and_source_formatter_must_match_before_native_dispatch(string fault)
    {
        using var rig = new Rig(); rig.Source.Change = snapshot =>
        {
            var p = snapshot.JournalReversalProof!;
            return snapshot with { JournalReversalProof = fault switch
            {
                "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "id" => p with { ProposalId = Guid.NewGuid() },
                "source-id" => p with { SourceJournalId = Guid.NewGuid() }, "period-id" => p with { PeriodId = Guid.NewGuid() },
                "maker" => p with { ProposedBy = Guid.NewGuid() }, "version" => p with { Version = 7 },
                "digest-shape" => p with { SourceFactsDigest = "wrong" }, "preview-shape" => p with { PreviewDigest = "wrong" },
                "cancel-actor" => p with { CancellationSourceActorId = Guid.Empty }, _ => p,
            }, Item = snapshot.Item with
            {
                DocumentLabel = fault == "caption" ? "foreign caption" : snapshot.Item.DocumentLabel,
                References = snapshot.Item.References.Select((r, i) => i != 0 ? r : fault switch
                    { "ref-id" => r with { RecordId = Guid.NewGuid() }, "ref-link" => r with { Link = "/wrong" }, _ => r }).ToArray(),
                Rules = fault is "rule" or "threshold" ? [snapshot.Item.Rules[0] with
                    { RuleCode = fault == "rule" ? "accounting.period-opening-approval-required" : snapshot.Item.Rules[0].RuleCode,
                        Threshold = fault == "threshold" ? "1" : null }] : snapshot.Item.Rules,
            } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Approvals); Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData("company")] [InlineData("reason")] [InlineData("version")] [InlineData("actor-proof")]
    public async Task Human_second_read_must_match_the_source_owned_snapshot(string fault)
    {
        using var rig = new Rig(); rig.Port.ReadChange = (source, read) => read != 2 ? source : fault switch
        {
            "company" => source with { Proposal = source.Proposal with { CompanyId = Guid.NewGuid() } },
            "reason" => source with { Proposal = source.Proposal with { Reason = "SENTETIK other retained reason" } },
            "version" => source with { Proposal = source.Proposal with { Version = 7 } },
            _ => source with { CancellationSourceActorId = CancellationActor },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status); Assert.Equal(0, rig.Port.Approvals);
    }

    [Theory] [InlineData("missing")] [InlineData("key")] [InlineData("payload")] [InlineData("company")] [InlineData("actor")]
    [InlineData("source")] [InlineData("journal")] [InlineData("period")] [InlineData("version")] [InlineData("correlation")]
    [InlineData("digest")] [InlineData("actor-unestablished")] [InlineData("actor-mismatch")]
    public async Task Post_native_provenance_failure_suppresses_success_then_same_key_reconciles_without_a_second_effect(string fault)
    {
        using var rig = new Rig(); rig.Port.AfterAnswer = answer =>
        {
            var v = answer.Resource!; var r = v.ApprovalReceipt!;
            r = fault switch
            {
                "key" => r with { IdempotencyKeyHash = "wrong" }, "payload" => r with { PayloadHash = "wrong" },
                "company" => r with { CompanyId = Guid.NewGuid() }, "actor" => r with { ActorId = Maker },
                "source" => r with { SourceJournalId = Guid.NewGuid() }, "journal" => r with { ReversalJournalId = Guid.NewGuid() },
                "period" => r with { PeriodId = Guid.NewGuid() }, "version" => r with { ResultVersion = 7 },
                "correlation" => r with { OriginalCorrelationId = "foreign" }, "digest" => r with { OriginalResultDigest = new string('B', 64) },
                "actor-unestablished" => r with { CancellationSourceActorProofEstablished = false },
                "actor-mismatch" => r with { CancellationSourceActorId = CancellationActor }, _ => r,
            };
            return answer with { Resource = v with { ApprovalReceipt = fault == "missing" ? null : r } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands); // Response suppression is an unknown outcome, not a rollback claim.
        rig.Port.AfterAnswer = null;
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Fact]
    public async Task Validly_shaped_source_digest_cannot_be_semantically_prechecked_from_the_scalar_carrier_but_original_key_recovers()
    {
        using var rig = new Rig(); rig.Source.Change = snapshot => snapshot with
            { JournalReversalProof = snapshot.JournalReversalProof! with { SourceFactsDigest = new string('B', 64) } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
        rig.Source.Change = null;
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData("unknown-root")] [InlineData("private-proof")]
    public async Task Malformed_legacy_command_never_becomes_a_Work_receipt(string fault)
    {
        using var rig = new Rig(); await rig.Decide(); var command = Assert.Single(rig.Port.Commands);
        var value = JsonNode.Parse(command.ResultJson)!;
        if (fault == "unknown-root") value["unknown"] = true; else value["resource"]!["approvalReceipt"] = new JsonObject();
        rig.Port.Commands[0] = command with { ResultJson = value.ToJsonString() };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Original_key_cannot_be_rebound_to_a_different_expected_version_or_actor(bool differentActor)
    {
        using var rig = new Rig(); await rig.Decide();
        if (differentActor) rig.User.Actor = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(differentActor ? rig.Request : rig.Request with { ExpectedVersion = "2" }));
        Assert.Equal(409, error.Status); Assert.Equal(AccountingApiCodes.IdempotencyConflict, error.Code);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData("maker")] [InlineData("cancel-maker")] [InlineData("permission")] [InlineData("scope")] [InlineData("company")]
    [InlineData("delegation")] [InlineData("late-permission")] [InlineData("late-company")] [InlineData("late-actor")]
    [InlineData("late-member")] [InlineData("late-delegation")]
    public async Task Existing_maker_KLN_D17_company_and_live_execution_fences_prevent_protocol_mutation(string fault)
    {
        using var rig = new Rig(true); Guid? delegation = fault is "delegation" or "late-delegation" ? Guid.NewGuid() : null;
        if (fault == "maker") rig.User.Actor = Maker;
        if (fault == "cancel-maker") rig.User.Actor = CancellationActor;
        if (fault == "permission") rig.Authority.Allowed = false;
        if (fault == "scope") rig.Authority.CompanyWide = false;
        if (fault == "company") rig.Company.Set(Guid.NewGuid());
        if (fault == "delegation") rig.Delegations.Live = false;
        rig.Port.BeforeApprove = () =>
        {
            if (fault == "late-permission") rig.Authority.Allowed = false;
            if (fault == "late-company") rig.Company.Set(Guid.NewGuid());
            if (fault == "late-actor") rig.User.Actor = Guid.NewGuid();
            if (fault == "late-member") rig.User.Member = false;
            if (fault == "late-delegation") rig.Delegations.Live = false;
        };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with { DelegationId = delegation }))).Status);
        Assert.Equal(0, rig.Port.Approvals); Assert.Empty(rig.Port.Commands);
    }

    [Fact]
    public async Task Live_delegation_reaches_only_the_native_approval_and_release_rechecks_authority()
    {
        using var live = new Rig(); Assert.Equal("Approved", (await live.Decide(live.Request with { DelegationId = Guid.NewGuid() })).State);
        using var lost = new Rig(); lost.Port.AfterAnswer = answer => { lost.Authority.Allowed = false; return answer; };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => lost.Decide())).Status);
        Assert.Equal(1, lost.Port.Approvals); lost.Authority.Allowed = true; lost.Port.AfterAnswer = null;
        Assert.True((await lost.Decide()).Replayed); Assert.Equal(1, lost.Port.Approvals);
    }

    [Theory] [InlineData(AccountingApiStatus.Denied, 403)] [InlineData(AccountingApiStatus.Unavailable, 503)]
    [InlineData(AccountingApiStatus.Conflict, 409)] [InlineData(AccountingApiStatus.NotFound, 404)]
    [InlineData(AccountingApiStatus.Invalid, 400)] [InlineData(AccountingApiStatus.Unprocessable, 422)]
    public async Task Native_refusal_and_original_correlation_are_preserved(AccountingApiStatus status, int expected)
    {
        using var rig = new Rig(); rig.Port.Refusal = new(status, null, true, "native-code", OriginalCorrelationId: "native-original");
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide()); Assert.Equal(expected, error.Status);
        Assert.Equal("native-code", error.Code); Assert.True(error.Replayed); Assert.Equal("native-original", error.OriginalCorrelationId);
        Assert.Equal(0, rig.Port.Approvals);
    }

    [Fact]
    public async Task Current_consumer_cancellation_never_releases_human_facts_and_missing_owner_is_unavailable()
    {
        using var rig = new Rig(); using var cancellation = new CancellationTokenSource();
        rig.Port.ReadChange = (source, _) => { cancellation.Cancel(); return source; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Host.PreviewAsync(WorkDecisionHostV1.JournalReversal, Id, cancellation.Token));
        Assert.Equal(0, rig.Port.Approvals);
        using var absent = new Rig(injectOwner: false);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => absent.Preview())).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => absent.Decide())).Status);
        Assert.Equal(0, absent.Port.Approvals);
    }

    [Fact]
    public async Task Journal_private_proof_is_rejected_on_every_other_kind_before_dispatch()
    {
        using var rig = new Rig(); var snapshot = await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default);
        // Only this cross-kind fixture permits the other kind's native permission read; no other command owner is supplied.
        rig.Authority.EnforcePermission = false;
        foreach (var kind in WorkDecisionHostV1.Kinds.Where(x => x != WorkDecisionHostV1.JournalReversal))
        {
            var source = new FixedSource(kind, snapshot!); var host = rig.HostWith(source);
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(kind, Id, default))).Status);
        }
        Assert.Equal(0, rig.Port.Approvals);
    }

    private sealed class Rig : IDisposable
    {
        public MutableCompany Company { get; } = new(); public MutableUser User { get; } = new(); public Authority Authority { get; } = new();
        public Delegations Delegations { get; } = new(); public ProtocolPort Port { get; } public SourceProxy Source { get; }
        public WorkDecisionHostV1 Host { get; } private readonly AccountingDbContext db;
        private readonly CorrelationContext correlation = new(); private readonly WorkDecisionExecutionFenceV1 execution = new();
        private readonly WorkDecisionAuthorityV1 authority; private readonly AccountingJournalReversalServiceV1 owner;
        public Rig(bool cancellationSource = false, bool injectOwner = true)
        {
            Company.Set(WorkJournalReversalDecisionHostTests.Company); correlation.Set(Correlation, null, "Web", null, null);
            authority = new(Authority, execution); Port = new(cancellationSource);
            owner = new(Port, Port, authority, Company, User, correlation, new FixedTime());
            db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("reversal-work-" + Guid.NewGuid()).Options, Company);
            Source = new(new AccountingJournalReversalWorkInboxSourceV1(db, new(Company, User, authority), owner));
            Host = new(Company, User, correlation, authority, [Source], Delegations, execution, null!, null!, accountingJournalReversals: injectOwner ? owner : null);
        }
        public WorkDecisionRequestV1 Request => new("1", true, null);
        public Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(WorkDecisionHostV1.JournalReversal, Id, default);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request = null) => Host.DecideAsync(WorkDecisionHostV1.JournalReversal, Id, request ?? Request, Key, default);
        public WorkDecisionHostV1 HostWith(IWorkInboxSourceV1 source) => new(Company, User, correlation, authority, [source], Delegations, execution, null!, null!, accountingJournalReversals: owner);
        public void Dispose() => db.Dispose();
    }
    private sealed class ProtocolPort : IAccountingJournalReversalProposalStoreV1, IAccountingJournalReversalDecisionStoreV1, IAccountingCommandLog
    {
        public AccountingJournalReversalProposalV1 Record = AccountingJournalReversalPolicyV1.Propose(Company, Maker,
            new(Id, SourceJournal, Period, new(2026, 11, 27), new(2026, 11, 27), null, "SENTETIK retained reversal reason"), At);
        public List<AccountingCommandRecord> Commands { get; } = []; public int Approvals, Reads, Expected; public string? Key;
        private readonly Guid? cancellationActor;
        public Action? BeforeApprove; public Func<AccountingJournalReversalDecisionSourceV1, int, AccountingJournalReversalDecisionSourceV1>? ReadChange;
        public Func<AccountingApiResult<AccountingJournalReversalProposalViewV1>, AccountingApiResult<AccountingJournalReversalProposalViewV1>>? AfterAnswer;
        public AccountingApiResult<AccountingJournalReversalProposalViewV1>? Refusal;
        public ProtocolPort(bool cancellationSource) => cancellationActor = cancellationSource ? CancellationActor : null;
        public async Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> RunApprovalAsync(
            Func<bool, Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            if (Refusal is not null) return Refusal;
            try { var answer = await attempt(false); return AfterAnswer?.Invoke(answer) ?? answer; }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, null); }
        }
        public async Task<AccountingJournalReversalDecisionSourceV1?> ReadJournalReversalDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            await fence(token); Reads++;
            var source = new AccountingJournalReversalDecisionSourceV1(Record, true, Record.ReversalJournalId is not null,
                Record.ReversalJournalId is null ? null : Correlation, cancellationActor);
            source = ReadChange?.Invoke(source, Reads) ?? source; await fence(token); return id == Id ? source : null;
        }
        public async Task<AccountingJournalReversalProposalV1> ApproveAndReverseAsync(Guid id, int version, Guid actor,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            BeforeApprove?.Invoke(); await fence(token); Expected = version; Key = metadata.IdempotencyKey;
            Record = AccountingJournalReversalPolicyV1.Approve(Record, new(id, version), actor, Guid.NewGuid(), At.AddMinutes(1));
            Approvals++; Commands.Add(metadata.Completion!(Record)); return Record;
        }
        public Task<AccountingCommandRecord?> FindCommandAsync(string name, string key, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(x => x.CommandName == name && x.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token = default) { Commands.Add(record); return Task.CompletedTask; }
        public Task<AccountingJournalReversalProposalV1?> FindAsync(Guid id, CancellationToken token = default) => Task.FromResult<AccountingJournalReversalProposalV1?>(id == Id ? Record : null);
        public Task<(AccountingJournalReversalProposalV1 Proposal, AccountingWriteOutcome Outcome)> CreateAsync(AccountingJournalReversalProposalV1 proposal, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new InvalidOperationException("No propose in this Work approval");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change; public int Reads;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int count, CancellationToken token) => inner.ListPendingAsync(context, count, token);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token)
        { Reads++; var snapshot = await inner.ReadDecisionAsync(context, id, token); return snapshot is null ? null : Change?.Invoke(snapshot) ?? snapshot; }
    }
    private sealed class FixedSource(string kind, WorkApprovalDecisionSnapshotV1 snapshot) : IWorkApprovalDecisionSourceV1
    {
        public string Module => "accounting"; public string Kind => kind; public string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int count, CancellationToken token) => throw new InvalidOperationException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot with { Item = snapshot.Item with { Kind = kind } });
    }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser
    { public Guid Actor = WorkJournalReversalDecisionHostTests.Actor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorityRevalidationService
    { public bool Allowed = true, CompanyWide = true, EnforcePermission = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
      { if (EnforcePermission) Assert.Equal(AccountingJournalReversalPermissionNamesV1.Approve, permission); return Task.FromResult(new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
    private sealed class Delegations : IWorkDelegationReaderV1
    { public bool Live = true; public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, AccountingJournalReversalPermissionNamesV1.Approve, Maker, Actor, At, At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null); }
    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => At.AddMinutes(1); }
}
