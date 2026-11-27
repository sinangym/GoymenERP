using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Real native lifecycle and owner source over an explicit synthetic protocol. No SQL/transaction/HTTP acceptance claim.
public sealed class ApprovedSourceCatalogWorkDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ac510000-0000-4000-8000-000000000001"),
        Id = Guid.Parse("ac510000-0000-4000-8000-000000000002"), Actor = Guid.Parse("ac510000-0000-4000-8000-000000000003"),
        Maker = Guid.Parse("ac510000-0000-4000-8000-000000000004"), Currency = Guid.Parse("ac510000-0000-4000-8000-000000000005"),
        Debit = Guid.Parse("ac510000-0000-4000-8000-000000000006"), Credit = Guid.Parse("ac510000-0000-4000-8000-000000000007");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Key = "SENTETIK-catalog-work", Correlation = "SENTETIK-catalog-work-correlation";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Whole_human_package_and_original_active_receipt_survive_current_supersession_including_native_self_activation(bool self)
    {
        using var rig = new Rig(); if (self) rig.User.Actor = Maker;
        var preview = await rig.Preview(); Assert.True(preview.Pending); Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var human = Assert.IsType<WorkApprovedSourceCatalogPreviewV1>(preview.ApprovedSourceCatalog);
        Assert.Equal("1", human.ExpectedPackageVersion); Assert.Equal("0", human.ExpectedActiveCatalogVersion);
        Assert.Equal(2, human.Package.Accounts.Count); Assert.Equal(2, Assert.Single(human.Package.Bindings).Mappings.Count);
        var wire = JsonSerializer.SerializeToElement(human, Json);
        Assert.Equal(6, wire.EnumerateObject().Count()); Assert.Equal(15, wire.GetProperty("catalog").EnumerateObject().Count());
        Assert.Equal(JsonValueKind.String, wire.GetProperty("catalog").GetProperty("version").ValueKind);
        var snapshot = await rig.Source.ReadDecisionAsync(new(Company, rig.User.UserId!, Correlation, null), Id, default);
        Assert.False(snapshot!.Item.Excludes(rig.User.UserId!));
        Assert.False(JsonSerializer.SerializeToElement(snapshot, Json).TryGetProperty("approvedSourceCatalogProof", out _));
        var first = await rig.Decide(); Assert.Equal("Active", first.State); Assert.False(first.Replayed); Assert.Equal(Correlation, first.OriginalCorrelationId);
        Assert.Equal(1L, rig.Port.ExpectedPackage); Assert.Equal(0L, rig.Port.ExpectedActive); Assert.Equal(Key, rig.Port.Key);
        rig.Port.Supersede(); var current = await rig.Preview(); Assert.False(current.Pending); Assert.Equal("3", current.SourceVersion);
        Assert.Equal("3", current.ApprovedSourceCatalog!.Catalog.Version); Assert.Equal("2", current.ApprovedSourceCatalog.ExpectedActiveCatalogVersion);
        var beforeReplayReads = rig.Port.DecisionReads;
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal("Active", replay.State);
        Assert.Equal(Correlation, replay.OriginalCorrelationId); Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
        Assert.Equal(beforeReplayReads + 1, rig.Port.DecisionReads); // The source read only, never a second human GET during POST.
        using var retained = JsonDocument.Parse(rig.Port.Commands[0].ResultJson);
        Assert.Equal(8, retained.RootElement.EnumerateObject().Count());
        Assert.Equal(15, retained.RootElement.GetProperty("resource").EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Number, retained.RootElement.GetProperty("resource").GetProperty("version").ValueKind);
        Assert.DoesNotContain("activationReceipt", rig.Port.Commands[0].ResultJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, JsonSerializer.SerializeToElement(replay, Json).EnumerateObject().Count());
        Assert.Equal(5, JsonSerializer.SerializeToElement(rig.Request, Json).EnumerateObject().Count());
        Assert.Equal(4, JsonSerializer.SerializeToElement(new WorkDecisionRequestV1("1", true, null), Json).EnumerateObject().Count());
    }

    [Theory]
    [InlineData("0", "0")] [InlineData("01", "0")] [InlineData("+1", "0")] [InlineData("1\n", "0")]
    [InlineData("9223372036854775807", "0")] [InlineData("9223372036854775808", "0")]
    [InlineData("1", "-1")] [InlineData("1", "00")] [InlineData("1", "+0")] [InlineData("1", "0\n")]
    [InlineData("1", "9223372036854775807")] [InlineData("1", "9223372036854775808")]
    public async Task Both_incrementable_canonical_Int64_versions_are_required_before_source_read(string package, string active)
    {
        using var rig = new Rig(); var error = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with
            { ExpectedVersion = package, ApprovedSourceCatalogSelection = new(active) }));
        Assert.Equal(400, error.Status); Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData(9007199254740993L)] [InlineData(9223372036854775806L)]
    public async Task Full_Int64_frozen_versions_increment_exactly_and_replay_even_when_current_versions_reach_MaxValue(long expected)
    {
        using var rig = new Rig(); rig.Port.Record = rig.Port.Record with { Version = expected }; rig.Port.ActiveVersion = expected;
        var text = expected.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var request = rig.Request with { ExpectedVersion = text, ApprovedSourceCatalogSelection = new(text) };
        var first = await rig.Decide(request); Assert.False(first.Replayed);
        Assert.Equal(expected, rig.Port.ExpectedPackage); Assert.Equal(expected, rig.Port.ExpectedActive);
        var terminal = await rig.Preview(); var next = checked(expected + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(next, terminal.SourceVersion); Assert.Equal(next, terminal.ApprovedSourceCatalog!.Catalog.Version);
        Assert.Equal(next, terminal.ApprovedSourceCatalog.ExpectedActiveCatalogVersion);
        Assert.True((await rig.Decide(request)).Replayed); Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData("reject")] [InlineData("reason")] [InlineData("missing-selection")]
    [InlineData("normal")] [InlineData("period")] [InlineData("package")] [InlineData("manufacturing")]
    [InlineData("loss")] [InlineData("overage")] [InlineData("plan")] [InlineData("supplier")] [InlineData("return")]
    public async Task Only_approve_null_reason_and_the_catalog_selection_reach_the_owner(string fault)
    {
        using var rig = new Rig(); var request = fault switch
        {
            "reject" => rig.Request with { Approve = false }, "reason" => rig.Request with { Reason = "" },
            "missing-selection" => rig.Request with { ApprovedSourceCatalogSelection = null },
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
        foreach (var kind in WorkDecisionHostV1.Kinds.Where(kind => kind != WorkDecisionHostV1.ApprovedSourceCatalog))
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
            var p = s.ApprovedSourceCatalogProof!;
            return s with { ApprovedSourceCatalogProof = fault switch
            {
                "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "id" => p with { PackageId = Guid.NewGuid() },
                "maker" => p with { PreparedBy = Actor }, "version" => p with { PackageVersion = 7 }, "status" => p with { Status = "Active" },
                "active-negative" => p with { ActiveCatalogVersion = -1 }, "digest-shape" => p with { SourceFactsDigest = "bad" },
                "preview-shape" => p with { PreviewDigest = "bad" }, _ => p,
            }, Item = s.Item with { DocumentLabel = fault == "caption" ? "foreign" : s.Item.DocumentLabel,
                References = fault == "reference" ? [s.Item.References[0] with { RecordId = Guid.NewGuid() }] : s.Item.References,
                Rules = fault == "rule" ? [s.Item.Rules[0] with { Threshold = "1" }] : s.Item.Rules } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }

    [Fact]
    public async Task Private_catalog_proof_on_another_kind_and_polluted_exclusion_metadata_are_refused()
    {
        using var rig = new Rig(); var snapshot = await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default);
        var other = new FixedSource(snapshot!);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.HostWith(other).PreviewAsync(other.Kind, Id, default))).Status);
        rig.Source.Change = s => s with { Item = s.Item with { ExcludedDeciders = [Actor.ToString("D")] } };
        // The owner's closed formatter has no exclusions; this polluted source is unavailable before authority dispatch.
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Unhandled_mapping_private_proof_is_rejected_on_catalog_and_other_existing_kinds(bool catalog)
    {
        using var rig = new Rig(); var snapshot = (await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default))!;
        var mapping = new WorkPostingAccountMappingProofV1(Company, 1, Maker, At, "Prepared", 1, 0, new string('A', 64), new string('B', 64));
        if (catalog)
        {
            rig.Source.Change = s => s with { PostingAccountMappingProof = mapping };
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        }
        else
        {
            var source = new FixedSource(snapshot with { ApprovedSourceCatalogProof = null, PostingAccountMappingProof = mapping });
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.HostWith(source).PreviewAsync(source.Kind, Id, default))).Status);
        }
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData("body")] [InlineData("company")] [InlineData("active-version")]
    public async Task Human_read_must_match_the_source_snapshot_including_full_package_and_active_version(string fault)
    {
        using var rig = new Rig(); rig.Port.ReadChange = (source, count) => count != 2 ? source : fault switch
        {
            "company" => source with { Record = source.Record with { CompanyId = Guid.NewGuid() } },
            "active-version" => source with { ActiveCatalogVersion = 9 },
            _ => source with { Record = source.Record with { Package = Package("SENTETIK different stored name") } },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status); Assert.Equal(0, rig.Port.Effects);
    }

    [Theory] [InlineData("missing")] [InlineData("key")] [InlineData("payload")] [InlineData("company")] [InlineData("actor")]
    [InlineData("version")] [InlineData("active-version")] [InlineData("correlation")] [InlineData("digest")] [InlineData("source-digest")]
    public async Task Post_native_invalid_private_receipt_is_unknown_outcome_then_same_key_reconciles_once(string fault)
    {
        using var rig = new Rig(); rig.Port.AfterAnswer = answer =>
        {
            var v = answer.Resource!; var p = v.ActivationReceipt!;
            p = fault switch { "key" => p with { IdempotencyKeyHash = "wrong" }, "payload" => p with { PayloadHash = "wrong" },
                "company" => p with { CompanyId = Guid.NewGuid() }, "actor" => p with { ActorId = Maker },
                "version" => p with { ResultVersion = 9 }, "active-version" => p with { ExpectedActiveCatalogVersion = 9 },
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
        using var rig = new Rig(); rig.Source.Change = s => s with { ApprovedSourceCatalogProof = s.ApprovedSourceCatalogProof! with { SourceFactsDigest = new string('B', 64) } };
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
    public async Task A_new_key_cannot_activate_a_superseded_package_again()
    {
        using var rig = new Rig(); await rig.Decide(); rig.Port.Supersede();
        var request = rig.Request with { ExpectedVersion = "3", ApprovedSourceCatalogSelection = new("2") };
        var refusal = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(
            WorkDecisionHostV1.ApprovedSourceCatalog, Id, request, Key + "-new", default));
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
        public Delegations Delegations { get; } = new(); public SyntheticPort Port { get; } = new(); public SourceProxy Source { get; }
        public WorkDecisionHostV1 Host { get; } private readonly AccountingDbContext db; private readonly CorrelationContext correlation = new();
        private readonly WorkDecisionExecutionFenceV1 execution = new(); private readonly WorkDecisionAuthorityV1 authority; private readonly ApprovedAccountingSourceCatalogServiceV1 owner;
        public Rig(bool inject = true)
        {
            Company.Set(ApprovedSourceCatalogWorkDecisionHostTests.Company); correlation.Set(Correlation, null, "Web", null, null);
            authority = new(Authority, execution); owner = new(new ApprovedAccountingSourcePackageParserV1(), Port, Port, authority, Company, User, correlation, new FixedTime());
            db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("catalog-work-" + Guid.NewGuid()).Options, Company);
            Source = new(new AccountingApprovedSourceCatalogWorkInboxSourceV1(db, new(Company, User, authority), owner));
            Host = new(Company, User, correlation, authority, [Source], Delegations, execution, null!, null!, approvedSourceCatalogs: inject ? owner : null);
        }
        public WorkDecisionRequestV1 Request => new("1", true, null, ApprovedSourceCatalogSelection: new("0"));
        public Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(WorkDecisionHostV1.ApprovedSourceCatalog, Id, default);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request = null, CancellationToken token = default) => Host.DecideAsync(WorkDecisionHostV1.ApprovedSourceCatalog, Id, request ?? Request, Key, token);
        public WorkDecisionHostV1 HostWith(IWorkInboxSourceV1 source) => new(Company, User, correlation, authority, [source], Delegations, execution, null!, null!, approvedSourceCatalogs: owner);
        public void Dispose() => db.Dispose();
    }
    private sealed class SyntheticPort : IApprovedAccountingSourceCatalogStoreV1, IApprovedAccountingSourceCatalogDecisionStoreV1, IAccountingCommandLog
    {
        public ApprovedAccountingSourceCatalogRecordV1 Record = new(Id, Company, ApprovedAccountingSourceCatalogStatusV1.Prepared, 1, null,
            ApprovedAccountingSourcePackageTemplateV1.Version, "SENTETIK-plan-v1", new string('E', 64), new string('F', 64), Maker, At, null, null, Package());
        public long ActiveVersion, ExpectedPackage, ExpectedActive; public int Effects, DecisionReads; public string? Key; public Action? BeforeActivate;
        public List<AccountingCommandRecord> Commands { get; } = [];
        public Func<ApprovedAccountingSourceCatalogDecisionSourceV1, int, ApprovedAccountingSourceCatalogDecisionSourceV1>? ReadChange;
        public Func<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>, AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>>? AfterAnswer;
        public AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>? Refusal;
        public async Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>> RunActivationAsync(Func<bool, Task<AccountingApiResult<ApprovedAccountingSourceCatalogViewV1>>> attempt,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            if (Refusal is not null) return Refusal;
            try { var result = await attempt(false); await fence(token); return AfterAnswer?.Invoke(result) ?? result; }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); }
        }
        public async Task<ApprovedAccountingSourceCatalogRecordV1> ActivateAsync(Guid id, long version, long active, Guid actor, DateTimeOffset at,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            BeforeActivate?.Invoke(); await fence(token); token.ThrowIfCancellationRequested(); ExpectedPackage = version; ExpectedActive = active; Key = metadata.IdempotencyKey;
            if (Record.Version != version) throw new AccountingVersionConflictException(version, Record.Version);
            if (Record.Status != ApprovedAccountingSourceCatalogStatusV1.Prepared)
                throw new AccountingStoreException(ApprovedAccountingSourceCatalogCodesV1.NotPrepared, "SENTETIK prepared-only native protocol");
            if (ActiveVersion != active) throw new ApprovedAccountingSourceCatalogVersionConflictException(active, ActiveVersion);
            Record = Record with { Status = ApprovedAccountingSourceCatalogStatusV1.Active, Version = checked(version + 1), CatalogVersion = checked(active + 1), ActivatedByUserId = actor, ActivatedAtUtc = at };
            ActiveVersion = active + 1; Effects++; Commands.Add(metadata.Completion!(Record)); return Record;
        }
        public async Task<ApprovedAccountingSourceCatalogDecisionSourceV1?> ReadActivationDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); DecisionReads++; var source = new ApprovedAccountingSourceCatalogDecisionSourceV1(Record, ActiveVersion); source = ReadChange?.Invoke(source, DecisionReads) ?? source; await fence(token); return id == Id ? source : null; }
        public async Task<ApprovedAccountingSourceCatalogRecordV1?> ReadActivationReceiptSourceAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); var result = id == Id ? Record : null; await fence(token); return result; }
        public void Supersede() { Record = Record with { Status = ApprovedAccountingSourceCatalogStatusV1.Superseded, Version = 3 }; ActiveVersion = 2; }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(row => row.CommandName == command && row.IdempotencyKeyHash == key));
        public Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken token = default) { Commands.Add(record); return Task.CompletedTask; }
        public Task<ApprovedAccountingSourceCatalogRecordV1?> ReadAsync(Guid id, CancellationToken token = default) => throw new InvalidOperationException("No general catalog read");
        public Task<(ApprovedAccountingSourceCatalogRecordV1 Record, AccountingWriteOutcome Outcome)> PrepareAsync(Guid id, ApprovedAccountingSourcePackagePreviewV1 preview, Guid actor, DateTimeOffset at, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new InvalidOperationException("No prepare");
        public Task<NormalJournalPostingRuleV1?> ResolveAsync(Guid company, Guid source, string kind, string version, string profile, string profileVersion, Guid currency, CancellationToken token = default) => throw new InvalidOperationException("No posting lookup");
    }
    private static ApprovedAccountingSourcePackageV1 Package(string name = "SENTETIK account")
    {
        var mirror = new AccountPlanMirrorV1(Company, "SENTETIK-plan-v1", new string('E', 64), Maker, At,
            [new(new(Company, Debit), "SENTETIK-debit", name), new(new(Company, Credit), "SENTETIK-credit", name)]);
        var rule = new NormalJournalPostingRuleV1(mirror, "SENTETIK-purpose", "1", Maker, At,
            [new("debit", PostingSide.Debit, new(Company, Debit)), new("credit", PostingSide.Credit, new(Company, Credit))]);
        return new(mirror, [new(Company, Id, NormalJournalSourceKindsV1.RecordedManualReceipt, "1", "SENTETIK-profile", "1", Currency, rule)]);
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
        public string Module => "finance"; public string Kind => "finance.cash-account.v1"; public string ApproverPermission => ApprovedAccountingSourceCatalogPermissionNamesV1.Activate;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int count, CancellationToken t) => throw new InvalidOperationException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot with { Item = snapshot.Item with { Module = Module, Kind = Kind } });
    }
    private sealed class MutableCompany : ICompanyContext { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser { public Guid Actor = ApprovedSourceCatalogWorkDecisionHostTests.Actor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && AllowedCompanyIds.Contains(id); }
    private sealed class Authority : IErpAuthorityRevalidationService { public bool Allowed = true; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default) { Assert.Equal(ApprovedAccountingSourceCatalogPermissionNamesV1.Activate, permission); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
    private sealed class Delegations : IWorkDelegationReaderV1 { public bool Live = true; public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, ApprovedAccountingSourceCatalogPermissionNamesV1.Activate, Maker, Actor, At, At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null); }
    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => At.AddMinutes(1); }
}
