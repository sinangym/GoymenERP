using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Messaging;
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

// Actual Application owner/source over a controlled command protocol. No SQL, commit, Audit or HTTP acceptance.
public sealed class ManufacturingCostDifferenceWorkDecisionHostTests
{
    internal static readonly Guid Company = Guid.Parse("ac530000-0000-4000-8000-000000000001"),
        Id = Guid.Parse("ac530000-0000-4000-8000-000000000002"), Actor = Guid.Parse("ac530000-0000-4000-8000-000000000003"),
        Maker = Guid.Parse("ac530000-0000-4000-8000-000000000004"), Debit = Guid.Parse("ac530000-0000-4000-8000-000000000006"),
        Credit = Guid.Parse("ac530000-0000-4000-8000-000000000007");
    internal static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Key = "SENTETIK-cost-work", Correlation = "SENTETIK-cost-work-correlation";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData(2L)] [InlineData(7L)] [InlineData(9007199254740993L)] [InlineData(long.MaxValue - 1)]
    public async Task Full_owner_preview_and_genuine_original_posted_receipt_use_N_to_N_plus_one(long version)
    {
        using var rig = new Rig(version: version);
        var preview = await rig.Preview(); Assert.True(preview.Pending); Assert.False(preview.ApprovalReasonRequired); Assert.False(preview.RejectionSupported);
        var human = Assert.IsType<WorkManufacturingCostDifferencePreviewV1>(preview.ManufacturingCostDifference);
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), human.Proposal.Version);
        Assert.Equal("600", human.Proposal.SignedTlDifference); Assert.Equal("2 / 3", Assert.Single(human.Proposal.Outputs).RemainingQuantityKg);
        Assert.Equal("SENTETIK reason", human.Proposal.Reason); Assert.Equal(At, human.PreparedAtUtc);
        var snapshot = (await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default))!;
        var snapshotJson = JsonSerializer.Serialize(snapshot, Json);
        Assert.DoesNotContain("manufacturingCostDifferenceProof", snapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(human.SourceFactsDigest, snapshotJson); Assert.DoesNotContain("SENTETIK debit", snapshotJson);
        var first = await rig.Decide(); Assert.Equal("Posted", first.State); Assert.False(first.Replayed);
        Assert.Equal(version, rig.Port.Expected); Assert.Equal(Key, rig.Port.Key); Assert.Equal(1, rig.Port.Effects);
        Assert.Equal((version + 1).ToString(CultureInfo.InvariantCulture), (await rig.Preview()).SourceVersion);
        var humanReads = rig.Port.DecisionReads; var replay = await rig.Decide();
        Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId); Assert.Equal("Posted", replay.State);
        Assert.Equal(humanReads + 2, rig.Port.DecisionReads); // Source read + native receipt verification; no extra POST human GET.
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
        var native = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<AccountingApiResult<ManufacturingCostDifferenceViewV1>>(rig.Port.Commands[0].ResultJson, Json), Json);
        Assert.Equal(8, native.EnumerateObject().Count()); Assert.Equal(20, native.GetProperty("resource").EnumerateObject().Count());
        Assert.Equal(version + 1, native.GetProperty("resource").GetProperty("version").GetInt64());
        Assert.DoesNotContain("approvalReceipt", rig.Port.Commands[0].ResultJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, JsonSerializer.SerializeToElement(replay, Json).EnumerateObject().Count());
        Assert.Equal(4, JsonSerializer.SerializeToElement(rig.Request, Json).EnumerateObject().Count());
    }

    [Theory] [InlineData("0")] [InlineData("01")] [InlineData("+2")] [InlineData("2\n")] [InlineData("2\r")] [InlineData("2\r\n")]
    [InlineData("9223372036854775807")] [InlineData("9223372036854775808")] [InlineData("")]
    public async Task Invalid_or_nonincrementable_version_is_refused_before_source_read(string version)
    {
        using var rig = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with { ExpectedVersion = version }))).Status);
        Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("reject")] [InlineData("reason")] [InlineData("mapping")] [InlineData("catalog")] [InlineData("period")]
    [InlineData("normal")] [InlineData("package")] [InlineData("manufacturing")] [InlineData("loss")] [InlineData("overage")]
    [InlineData("plan")] [InlineData("supplier")] [InlineData("return")]
    public async Task Approve_null_reason_without_any_foreign_selection_is_the_only_command(string fault)
    {
        using var rig = new Rig(); var request = fault switch
        {
            "reject" => rig.Request with { Approve = false }, "reason" => rig.Request with { Reason = "" },
            "mapping" => rig.Request with { PostingAccountMappingSelection = new("0") }, "catalog" => rig.Request with { ApprovedSourceCatalogSelection = new("0") },
            "period" => rig.Request with { AccountingPeriodReopening = new("1") }, "normal" => rig.Request with { NormalJournalDraft = new(Guid.NewGuid()) },
            "package" => rig.Request with { ProductPackageStandard = new("2026-11-27") }, "manufacturing" => rig.Request with { ManufacturingPlanReapproval = new([]) },
            "loss" => rig.Request with { InventoryTransferLoss = new(1, At) }, "overage" => rig.Request with { InventoryTransferOverage = new(1, At) },
            "plan" => rig.Request with { LogisticsPlanExcess = new("1") }, "supplier" => rig.Request with { SupplierReturnCostException = new() },
            _ => rig.Request with { LogisticsCustomerReturn = new(Guid.NewGuid()) },
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("maker")] [InlineData("status")]
    [InlineData("version")] [InlineData("digest")] [InlineData("preview")] [InlineData("caption")] [InlineData("reference")]
    [InlineData("rule")] [InlineData("scope")] [InlineData("cross-proof")]
    public async Task Private_routing_proof_and_native_formatter_are_checked_before_dispatch(string fault)
    {
        using var rig = new Rig(); rig.Source.Change = s =>
        {
            var p = s.ManufacturingCostDifferenceProof!;
            return s with { ManufacturingCostDifferenceProof = fault switch
            {
                "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "id" => p with { ProposalId = Guid.NewGuid() },
                "maker" => p with { PreparedBy = Actor }, "status" => p with { Status = "Posted" },
                "digest" => p with { SourceFactsDigest = "bad" }, "preview" => p with { PreviewDigest = "bad" }, _ => p,
            }, SourceVersion = fault == "version" ? "02" : s.SourceVersion,
            PostingAccountMappingProof = fault == "cross-proof" ? new(Company, 1, Maker, At, "Prepared", 1, 0, new('A', 64), new('B', 64)) : null,
            Item = s.Item with { DocumentLabel = fault == "caption" ? "foreign" : s.Item.DocumentLabel,
                References = fault == "reference" ? [s.Item.References[0] with { RecordId = Guid.NewGuid() }, .. s.Item.References.Skip(1)] : s.Item.References,
                Rules = fault == "rule" ? [s.Item.Rules[0] with { Threshold = "1" }] : s.Item.Rules,
                Scope = fault == "scope" ? new(Guid.NewGuid(), null, null) : null } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }

    [Fact]
    public async Task Cost_private_proof_is_refused_on_a_foreign_kind_before_any_dispatch()
    {
        using var rig = new Rig(); var snapshot = (await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default))!;
        var other = new FixedSource(snapshot); var host = rig.HostWith(other);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(other.Kind, Id, default))).Status);
        Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("null")] [InlineData("company")] [InlineData("id")] [InlineData("amount")]
    [InlineData("outputs")] [InlineData("plan")] [InlineData("version")] [InlineData("time")]
    public async Task Second_human_read_must_match_the_frozen_source_and_never_release_changed_or_partial_facts(string fault)
    {
        using var rig = new Rig(); rig.Port.ChangeRead = (source, count) => count != 2 ? source : fault switch
        {
            "null" => null, "company" => source with { Proposal = source.Proposal with { CompanyId = Guid.NewGuid() } },
            "id" => source with { Proposal = source.Proposal with { ProposalId = Guid.NewGuid() } },
            "amount" => source with { Proposal = source.Proposal with { SignedTlDifference = "601" } },
            "outputs" => source with { Proposal = source.Proposal with { Outputs = [] } },
            "plan" => source with { Proposal = source.Proposal with { AccountPlan = null } },
            "version" => source with { Proposal = source.Proposal with { Version = source.Proposal.Version + 1 } },
            _ => source with { PreparedAtUtc = source.PreparedAtUtc!.Value.AddTicks(1) },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status);
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Native_fence_rechecks_current_authority_context_and_delegation_before_effect(string fault)
    {
        using var rig = new Rig(); using var cancellation = new CancellationTokenSource();
        rig.Port.BeforeApprove = () => { if (fault == "permission") rig.Authority.Allowed = false; if (fault == "company") rig.Company.Set(Guid.NewGuid());
            if (fault == "actor") rig.User.Actor = Maker; if (fault == "member") rig.User.Member = false;
            if (fault == "delegation") rig.Delegations.Live = false; if (fault == "cancel") cancellation.Cancel(); };
        var request = rig.Request with { DelegationId = Guid.NewGuid() };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Decide(request, cancellation.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Late_human_owner_read_cannot_release_after_context_or_authority_changes(string fault)
    {
        using var rig = new Rig(); using var cancellation = new CancellationTokenSource();
        rig.Port.AfterRead = count => { if (count != 2) return; if (fault == "permission") rig.Authority.Allowed = false;
            if (fault == "company") rig.Company.Set(Guid.NewGuid()); if (fault == "actor") rig.User.Actor = Maker;
            if (fault == "member") rig.User.Member = false; if (fault == "cancel") cancellation.Cancel(); };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Host.PreviewAsync(WorkDecisionHostV1.ManufacturingCostDifference, Id, cancellation.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status);
        Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("key")] [InlineData("payload")] [InlineData("actor")] [InlineData("version")]
    [InlineData("source")] [InlineData("result")] [InlineData("correlation")] [InlineData("time")]
    public async Task A_malformed_private_original_receipt_is_unavailable_after_effect_and_same_key_can_reconcile(string fault)
    {
        using var rig = new Rig(); rig.Port.AfterAnswer = answer =>
        {
            var view = answer.Resource!; var p = view.ApprovalReceipt!;
            return answer with { Resource = view with { ApprovalReceipt = fault switch
            {
                "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "key" => p with { IdempotencyKeyHash = new('E', 64) },
                "payload" => p with { PayloadHash = new('E', 64) }, "actor" => p with { ActorId = Maker }, "version" => p with { ResultVersion = p.ResultVersion + 1 },
                "source" => p with { SourceFactsDigest = new('E', 64) }, "result" => p with { OriginalResultDigest = new('E', 64) },
                "correlation" => p with { OriginalCorrelationId = "other" }, _ => p with { ApprovedAtUtc = At.ToOffset(TimeSpan.FromHours(1)) },
            } } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(1, rig.Port.Effects);
        rig.Port.AfterAnswer = null; var recovered = await rig.Decide(); Assert.True(recovered.Replayed); Assert.Equal(1, rig.Port.Effects);
    }

    [Fact]
    public async Task Canonical_altered_scalar_amount_requires_genuine_owner_match_not_scalar_self_certification()
    {
        using var rig = new Rig(); rig.Source.Change = s => s with { Item = s.Item with { Rules = [s.Item.Rules[0] with { Actual = "601" }] } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status); Assert.Equal(0, rig.Port.Effects);
        // Scalars alone establish shape. POST validates against the actual immutable native receipt after the single command.
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(1, rig.Port.Effects);
        rig.Source.Change = null; Assert.True((await rig.Decide()).Replayed); Assert.Equal(1, rig.Port.Effects);
    }

    [Fact]
    public async Task Maker_and_missing_owner_never_reach_native_mutation_or_general_browse()
    {
        using (var rig = new Rig()) { rig.User.Actor = Maker; Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Effects); }
        using (var rig = new Rig(inject: false)) { Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status);
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Effects); }
    }

    private sealed class Rig : IDisposable
    {
        public MutableCompany Company { get; } = new(); public MutableUser User { get; } = new(); public Authority Authority { get; } = new();
        public Delegations Delegations { get; } = new(); public Protocol Port { get; } = new(); public SourceProxy Source { get; }
        private readonly AccountingDbContext db; private readonly WorkDecisionExecutionFenceV1 execution = new();
        private readonly CorrelationContext correlation = new(); private readonly WorkDecisionAuthorityV1 authority;
        public ManufacturingCostDifferenceServiceV1 Owner { get; } public WorkDecisionHostV1 Host { get; }
        public Rig(long version = 2, bool inject = true)
        {
            Company.Set(ManufacturingCostDifferenceWorkDecisionHostTests.Company); correlation.Set(Correlation, null, "Web", null, null);
            Port.Source = NewSource(version); authority = new(Authority, execution); Owner = new(Port, Port, authority, Company, User, correlation);
            db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("cost-work-" + Guid.NewGuid()).Options, Company);
            Source = new(new AccountingManufacturingCostDifferenceWorkInboxSourceV1(db, new(Company, User, authority), Owner));
            Host = new(Company, User, correlation, authority, [Source], Delegations, execution, null!, null!, manufacturingCostDifferences: inject ? Owner : null);
        }
        public WorkDecisionRequestV1 Request => new(Port.ExpectedVersion.ToString(CultureInfo.InvariantCulture), true, null);
        public Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(WorkDecisionHostV1.ManufacturingCostDifference, Id, default);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request = null, CancellationToken token = default) => Host.DecideAsync(WorkDecisionHostV1.ManufacturingCostDifference, Id, request ?? Request, Key, token);
        public WorkDecisionHostV1 HostWith(IWorkInboxSourceV1 source) => new(Company, User, correlation, authority, [source], Delegations, execution, null!, null!, manufacturingCostDifferences: Owner);
        public void Dispose() => db.Dispose();
    }

    // Controlled native algorithm/Commands protocol; no relational safety or durability is represented by this fixture.
    private sealed class Protocol : IManufacturingCostDifferenceStoreV1, IManufacturingCostDifferenceDecisionStoreV1, IAccountingCommandLog
    {
        public ManufacturingCostDifferenceDecisionSourceV1 Source = NewSource(2); public long ExpectedVersion => Source.Proposal.Status == "Prepared" ? Source.Proposal.Version : Source.Proposal.Version - 1;
        public Func<ManufacturingCostDifferenceDecisionSourceV1, int, ManufacturingCostDifferenceDecisionSourceV1?>? ChangeRead;
        public int Effects, DecisionReads; public long Expected; public string? Key; public Action? BeforeApprove; public Action<int>? AfterRead;
        public Func<AccountingApiResult<ManufacturingCostDifferenceViewV1>, AccountingApiResult<ManufacturingCostDifferenceViewV1>>? AfterAnswer;
        public List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> RunApprovalAsync(Func<bool, Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { try { await fence(token); var answer = await attempt(false); await fence(token); return AfterAnswer?.Invoke(answer) ?? answer; }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); } }
        public async Task<ManufacturingCostDifferenceViewV1> ApproveAsync(Guid id, long version, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            BeforeApprove?.Invoke(); await fence(token); token.ThrowIfCancellationRequested();
            if (id != Source.Proposal.ProposalId || version != Source.Proposal.Version) throw new AccountingVersionConflictException(version, Source.Proposal.Version);
            Expected = version; Key = metadata.IdempotencyKey;
            var view = Source.Proposal with { Version = checked(version + 1), Status = "Posted", ApprovedBy = actor, JournalId = Guid.NewGuid() };
            Source = Source with { Proposal = view, ApprovedAtUtc = At.AddSeconds(1), JournalBound = true, JournalCorrelationId = metadata.CorrelationId };
            Effects++; Commands.Add(metadata.Completion!(view)); return view;
        }
        public async Task<ManufacturingCostDifferenceDecisionSourceV1?> ReadCostDifferenceDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); DecisionReads++; var answer = ChangeRead is null ? Source : ChangeRead(Source, DecisionReads); AfterRead?.Invoke(DecisionReads); await fence(token); return id == Source.Proposal.ProposalId ? answer : null; }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(x => x.CommandName == command && x.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public Task AcceptAsync(WorkInboxEventV1 envelope, ManufacturingCostDifferenceEventV1 source, CancellationToken token = default) => throw new InvalidOperationException("No intake");
        public Task<IReadOnlyList<ManufacturingCostDifferenceViewV1>> ListAsync(CancellationToken token = default) => throw new InvalidOperationException("No general browse");
        public Task<ManufacturingCostDifferenceViewV1?> FindAsync(Guid id, CancellationToken token = default) => throw new InvalidOperationException("No general read");
        public Task<ManufacturingCostDifferenceViewV1> PrepareAsync(PrepareManufacturingCostDifferenceV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new InvalidOperationException("No prepare");
    }

    internal static ManufacturingCostDifferenceDecisionSourceV1 NewSource(long version)
    {
        var source = new ManufacturingCostDifferenceEventV1(Company, Guid.NewGuid(), Guid.NewGuid(), "2", "4", 3, "SENTETIK-input",
            [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, null, null, 20m, 1400m, 600m, 4m, [], 2000m,
                new("2", "3"), new("1", "3"))], Id, false, Correlation);
        var raw = JsonSerializer.Serialize(source, Json); var plan = new ManufacturingCostDifferenceAccountPlanV1(Guid.NewGuid(), 9007199254740993L,
            long.MaxValue, "SENTETIK-plan", new('a', 64), new(Debit, "620", "SENTETIK debit"), new(Credit, "151", "SENTETIK credit"));
        var view = new ManufacturingCostDifferenceViewV1(Id, Company, source.ManufacturingOrderId, source.GenerationId, source.GenerationVersion,
            "Prepared", version, "600", ManufacturingCostDifferenceServiceV1.CostDifferenceOutputs(source), At.AddDays(-1), Guid.NewGuid(),
            new(2026, 11, 27), new(2026, 11, 26), Debit, Credit, Maker, null, null, "SENTETIK reason", plan);
        return new(view, raw, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), Correlation, Guid.NewGuid(), At, null, false, null);
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public int Reads; public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int max, CancellationToken t) => inner.ListPendingAsync(c, max, t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t)
        { Reads++; var answer = await inner.ReadDecisionAsync(c, id, t); return answer is null ? null : Change?.Invoke(answer) ?? answer; }
    }
    private sealed class FixedSource(WorkApprovalDecisionSnapshotV1 snapshot) : IWorkApprovalDecisionSourceV1
    {
        public string Module => "finance"; public string Kind => "finance.cash-account.v1"; public string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int max, CancellationToken t) => throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot with { Item = snapshot.Item with { Module = Module, Kind = Kind } });
    }
    private sealed class MutableCompany : ICompanyContext { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser { public Guid Actor = ManufacturingCostDifferenceWorkDecisionHostTests.Actor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class Authority : IErpAuthorityRevalidationService { public bool Allowed = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) { Assert.Equal(AccountingJournalReversalPermissionNamesV1.Approve, permission); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
    private sealed class Delegations : IWorkDelegationReaderV1 { public bool Live = true; public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, AccountingJournalReversalPermissionNamesV1.Approve, Maker, Actor, At, At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null); }
}
