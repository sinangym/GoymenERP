using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Native AccountingApiService + real owner source over an explicit synthetic store protocol.
// This does not execute the relational owned transaction, actual Audit writer or Finance carry effects.
public sealed class WorkAccountingPeriodDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ac470000-0000-4000-8000-000000000001"), Period = Guid.Parse("ac470000-0000-4000-8000-000000000002"),
        Proposal = Guid.Parse("ac470000-0000-4000-8000-000000000003"), Actor = Guid.Parse("ac470000-0000-4000-8000-000000000004"),
        Maker = Guid.Parse("ac470000-0000-4000-8000-000000000005"), Closure = Guid.Parse("ac470000-0000-4000-8000-000000000006");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Key = "period-work-original", Correlation = "SENTETIK-period-work";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Full_native_human_projection_and_original_key_decision_use_existing_owner_only(bool reopen)
    {
        using var rig = new Rig(reopen); var preview = await rig.Preview();
        Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var wire = JsonSerializer.SerializeToElement(preview, Json);
        if (reopen)
        {
            var h = Assert.IsType<WorkAccountingPeriodReopeningPreviewV1>(preview.AccountingPeriodReopening);
            Assert.Equal("1", h.Reopening.Version); Assert.Equal("3", h.Reopening.ClosedPeriodVersion); Assert.Equal("3", h.Period.Version);
            Assert.Equal(11, JsonSerializer.SerializeToElement(h.Reopening, Json).EnumerateObject().Count());
            Assert.Equal(13, JsonSerializer.SerializeToElement(h.Period, Json).EnumerateObject().Count());
            Assert.Equal(13, JsonSerializer.SerializeToElement(h.Closure, Json).EnumerateObject().Count());
            Assert.Equal(rig.Port.Reopening.Reason, h.Reopening.Reason); Assert.Equal(rig.Port.Close.CarryVoucherId, h.Closure.CarryVoucherId);
            Assert.Null(preview.AccountingPeriodOpening); Assert.False(h.LaterPeriodClosed);
            Assert.Equal(6, wire.GetProperty("accountingPeriodReopening").EnumerateObject().Count());
        }
        else
        {
            var h = Assert.IsType<WorkAccountingPeriodOpeningPreviewV1>(preview.AccountingPeriodOpening);
            Assert.Equal("1", h.Period.Version); Assert.Equal(13, JsonSerializer.SerializeToElement(h.Period, Json).EnumerateObject().Count());
            Assert.Null(preview.AccountingPeriodReopening);
        }
        rig.Port.Reads = 0; var first = await rig.Decide();
        Assert.Equal("Approved", first.State); Assert.False(first.Replayed); Assert.Equal(Correlation, first.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
        Assert.Equal(2, rig.Port.Reads); // Source decision read + retained receipt read; no second human GET on POST.
        Assert.Equal(1, rig.Port.Expected); Assert.Equal(reopen ? 3L : (long?)null, rig.Port.ExpectedPeriod); Assert.Equal(Key, rig.Port.Key);
        if (!reopen) Assert.Equal(2, JsonSerializer.Deserialize<AccountingApiResult<PeriodViewV1>>(rig.Port.Commands[0].ResultJson, Json)!.Resource!.Version);
        using var result = JsonDocument.Parse(rig.Port.Commands[0].ResultJson);
        Assert.Equal(8, result.RootElement.EnumerateObject().Count());
        Assert.Equal(reopen ? 11 : 13, result.RootElement.GetProperty("resource").EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Number, result.RootElement.GetProperty("resource").GetProperty("version").ValueKind);
        Assert.DoesNotContain("approvalReceipt", result.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Later_current_period_versions_never_replace_the_original_approval_receipt(bool reopen)
    {
        using var rig = new Rig(reopen); await rig.Decide(); var original = rig.Port.Commands[0].ResultJson;
        rig.Port.Record = rig.Port.Record with { Version = 9007199254740993L };
        var current = await rig.Preview();
        Assert.Equal("9007199254740993", reopen ? current.AccountingPeriodReopening!.Period.Version : current.AccountingPeriodOpening!.Period.Version);
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal("Approved", replay.State);
        Assert.Equal(original, Assert.Single(rig.Port.Commands).ResultJson); Assert.Equal(1, rig.Port.Approvals);
        Assert.Equal(9007199254740993L, rig.Port.Record.Version);
    }

    [Theory] [InlineData("0")] [InlineData("01")] [InlineData("+1")] [InlineData("1\n")]
    [InlineData("9223372036854775807")] [InlineData("9223372036854775808")]
    public async Task Each_expected_version_is_positive_canonical_and_incrementable_before_owner_reads(string version)
    {
        foreach (var reopen in new[] { false, true })
        {
            using var rig = new Rig(reopen); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with { ExpectedVersion = version }))).Status);
            Assert.Equal(0, rig.Source.Reads); Assert.Empty(rig.Port.Commands);
            if (reopen) Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with { AccountingPeriodReopening = new(version) }))).Status);
        }
    }

    [Theory] [InlineData("reject")] [InlineData("reason")] [InlineData("empty-reason")] [InlineData("missing-selection")]
    [InlineData("normal")] [InlineData("package")] [InlineData("loss")] [InlineData("overage")] [InlineData("plan")]
    public async Task Approve_only_null_reason_and_kind_specific_selection_are_enforced(string fault)
    {
        foreach (var reopen in new[] { false, true })
        {
            using var rig = new Rig(reopen); var r = rig.Request;
            r = fault switch { "reject" => r with { Approve = false }, "reason" => r with { Reason = "SENTETIK" }, "empty-reason" => r with { Reason = "" },
                "missing-selection" => r with { AccountingPeriodReopening = reopen ? null : new("3") },
                "normal" => r with { NormalJournalDraft = new(Guid.NewGuid()) }, "package" => r with { ProductPackageStandard = new("2026-11-27") },
                "loss" => r with { InventoryTransferLoss = new(1, At) }, "overage" => r with { InventoryTransferOverage = new(1, At) }, _ => r with { LogisticsPlanExcess = new("1") } };
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(r))).Status); Assert.Equal(0, rig.Source.Reads);
        }
    }

    [Fact]
    public async Task No_other_registered_kind_silently_discards_the_new_reopening_selection()
    {
        using var rig = new Rig();
        foreach (var kind in WorkDecisionHostV1.Kinds.Where(x => x != WorkDecisionHostV1.AccountingPeriodReopening))
            Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Host.DecideAsync(kind, Period,
                new("1", true, null, AccountingPeriodReopening: new("3")), Key, default))).Status);
        Assert.Equal(0, rig.Source.Reads); Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("maker")] [InlineData("version")]
    [InlineData("preview")] [InlineData("rule")] [InlineData("reference")] [InlineData("cross-proof")]
    [InlineData("caption")] [InlineData("link")] [InlineData("reference-role")] [InlineData("reference-label")] [InlineData("date")]
    public async Task Private_scalar_identity_rules_and_references_must_match_the_native_owner(string fault)
    {
        foreach (var reopen in new[] { false, true })
        {
            using var rig = new Rig(reopen); rig.Source.Change = s =>
            {
                var altered = s with { Item = s.Item with { Rules = fault == "rule" ? [s.Item.Rules[0] with { Actual = "1" }] : s.Item.Rules,
                    DocumentLabel = fault == "caption" ? "SENTETIK wrong period caption" : s.Item.DocumentLabel,
                    Link = fault == "link" ? "/SENTETIK-wrong-period" : s.Item.Link,
                    References = fault switch {
                        "reference" => s.Item.References.Select(x => x with { RecordId = Guid.NewGuid() }).ToArray(),
                        "reference-role" => s.Item.References.Select(x => x with { Role = "wrong-period-role" }).ToArray(),
                        "reference-label" => s.Item.References.Select(x => x with { Label = "SENTETIK wrong reference" }).ToArray(), _ => s.Item.References } } };
                if (reopen)
                {
                    var p = s.AccountingPeriodReopeningProof!; return altered with { AccountingPeriodReopeningProof = fault switch
                    { "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "maker" => p with { PreparedBy = Actor },
                        "version" => p with { Version = 7 }, "date" => p with { StartsOn = p.StartsOn.AddDays(1) },
                        "preview" => p with { PreviewDigest = "wrong" }, _ => p },
                        AccountingPeriodOpeningProof = fault == "cross-proof" ? new(Company, Period, Maker, At, new(2026, 11, 1), new(2026, 11, 30), "Proposed", 1, new('A',64), new('A',64)) : null };
                }
                var o = s.AccountingPeriodOpeningProof!; return altered with { AccountingPeriodOpeningProof = fault switch
                { "missing" => null, "company" => o with { CompanyId = Guid.NewGuid() }, "maker" => o with { PreparedBy = Actor },
                    "version" => o with { Version = 7 }, "date" => o with { StartsOn = o.StartsOn.AddDays(1) },
                    "preview" => o with { PreviewDigest = "wrong" }, _ => o },
                    AccountingPeriodReopeningProof = fault == "cross-proof" ? new(Company, Proposal, Period, Closure, Maker, At, new(2026,11,1),new(2026,11,30),1,3,3,"Closed",false,true,false,new('A',64),new('A',64)) : null };
            };
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Approvals);
        }
    }

    [Fact]
    public async Task Opening_source_digest_is_recomputed_from_all_six_retained_identity_facts_before_native_effect()
    {
        using var rig = new Rig(); rig.Source.Change = s => s with
        { AccountingPeriodOpeningProof = s.AccountingPeriodOpeningProof! with { SourceFactsDigest = new string('B', 64) } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Approvals); Assert.Empty(rig.Port.Commands);
    }

    [Fact]
    public async Task Reopening_full_source_digest_mismatch_suppresses_release_and_same_key_reconciles_one_completed_decision()
    {
        using var rig = new Rig(true); rig.Source.Change = s => s with
        { AccountingPeriodReopeningProof = s.AccountingPeriodReopeningProof! with { SourceFactsDigest = new string('B', 64) } };
        // The private scalar proof omits closure/reason hash inputs. Their semantic comparison occurs only
        // against the genuine native receipt after the command, without a second human read or a rollback claim.
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(1, rig.Port.Approvals); var original = Assert.Single(rig.Port.Commands);
        rig.Source.Change = null;
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal("Approved", replay.State);
        Assert.Equal(Correlation, replay.OriginalCorrelationId); Assert.Equal(1, rig.Port.Approvals);
        Assert.Equal(original, Assert.Single(rig.Port.Commands)); Assert.Equal(2, rig.Port.Reopening.Version);
    }

    [Theory] [InlineData("missing")] [InlineData("key")] [InlineData("payload")] [InlineData("actor")] [InlineData("company")]
    [InlineData("period")] [InlineData("proposal")] [InlineData("expected")] [InlineData("result")] [InlineData("source")] [InlineData("digest")]
    [InlineData("correlation")]
    public async Task Work_success_requires_genuine_exact_original_native_receipt(string fault)
    {
        foreach (var reopen in new[] { false, true })
        {
            using var rig = new Rig(reopen); rig.Port.ChangeReceipt = p => fault switch
            { "missing" => null, "key" => p with { IdempotencyKeyHash = "wrong" }, "payload" => p with { PayloadHash = "wrong" },
                "actor" => p with { ActorId = Maker }, "company" => p with { CompanyId = Guid.NewGuid() }, "period" => p with { PeriodId = Guid.NewGuid() },
                "proposal" => p with { ReopeningId = Guid.NewGuid() }, "expected" => p with { ExpectedVersion = 7 }, "result" => p with { ResultVersion = 7 },
                "source" => p with { SourceFactsDigest = new string('B',64) }, "digest" => p with { OriginalResultDigest = new string('B',64) },
                _ => p with { OriginalCorrelationId = "wrong" } };
            Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
            Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands); // Refused release does not undo a synthetic completed native result.
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Missing_optional_owner_never_releases_human_data_or_dispatches_a_command(bool reopen)
    {
        using var rig = new Rig(reopen, injectOwner: false);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData("maker")] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("delegation")]
    public async Task Current_scope_and_native_late_execution_fences_prevent_protocol_mutation(string fault)
    {
        foreach (var reopen in new[] { false, true })
        {
            using var rig = new Rig(reopen); var delegation = fault == "delegation" ? Guid.NewGuid() : (Guid?)null;
            if (fault == "maker") rig.User.ActorId = Maker;
            else rig.Port.BeforeApprove = () => { if (fault == "permission") rig.Authority.Allowed = false;
                if (fault == "company") rig.Company.Set(Guid.NewGuid()); if (fault == "actor") rig.User.ActorId = Guid.NewGuid();
                if (fault == "delegation") rig.Delegations.Live = false; };
            Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with { DelegationId = delegation }))).Status);
            Assert.Equal(0, rig.Port.Approvals); Assert.Empty(rig.Port.Commands);
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Second_human_read_race_and_cancellation_never_release_mixed_preview(bool reopen)
    {
        using var changed = new Rig(reopen); changed.Port.ChangeRead = count => { if (count == 2) changed.Port.Record = changed.Port.Record with { Version = changed.Port.Record.Version + 1 }; };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => changed.Preview())).Status);
        using var cancelled = new Rig(reopen); using var cancellation = new CancellationTokenSource(); cancelled.Port.ChangeRead = _ => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.Host.PreviewAsync(cancelled.Kind, cancelled.Id, cancellation.Token));
        Assert.Empty(cancelled.Port.Commands); Assert.Empty(changed.Port.Commands);
    }

    [Fact]
    public async Task Original_opening_maker_is_not_an_invented_reopening_exclusion()
    {
        using var rig = new Rig(true, openingMaker: Actor); Assert.Equal("Approved", (await rig.Decide()).State);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Only_human_GET_contains_full_native_facts_and_long_versions_are_lossless(bool reopen)
    {
        using var rig = new Rig(reopen); rig.Port.Record = rig.Port.Record with { Version = 9007199254740993L };
        if (reopen)
        {
            rig.Port.Reopening = rig.Port.Reopening with { ClosedPeriodVersion = 9007199254740993L };
            rig.Port.Close = rig.Port.Close with { ClosedPeriodVersion = 9007199254740993L };
        }
        var preview = await rig.Preview(); var json = JsonSerializer.Serialize(preview, Json);
        Assert.Contains("9007199254740993", json);
        var projected = JsonSerializer.SerializeToElement(preview, Json);
        if (reopen)
        {
            var human = projected.GetProperty("accountingPeriodReopening");
            Assert.Equal("9007199254740993", human.GetProperty("reopening").GetProperty("closedPeriodVersion").GetString());
            Assert.Equal("9007199254740993", human.GetProperty("closure").GetProperty("closedPeriodVersion").GetString());
            Assert.Equal("9007199254740993", human.GetProperty("period").GetProperty("version").GetString());
        }
        Assert.DoesNotContain("approvalReceipt", json, StringComparison.OrdinalIgnoreCase);
        var snapshot = await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), rig.Id, default);
        var snapshotJson = JsonSerializer.Serialize(snapshot, Json);
        Assert.DoesNotContain("accountingPeriodOpeningProof", snapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accountingPeriodReopeningProof", snapshotJson, StringComparison.OrdinalIgnoreCase);
        var body = JsonSerializer.SerializeToElement(rig.Request, Json); Assert.Equal(reopen ? 5 : 4, body.EnumerateObject().Count());
        if (reopen) { Assert.Single(body.GetProperty("accountingPeriodReopening").EnumerateObject()); Assert.Equal("3", body.GetProperty("accountingPeriodReopening").GetProperty("expectedPeriodVersion").GetString()); }
        Assert.DoesNotContain("sourceFactsDigest", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task An_explicitly_changed_frozen_period_selection_or_version_cannot_reuse_the_original_key(bool reopen)
    {
        using var rig = new Rig(reopen); await rig.Decide(); var r = reopen
            ? rig.Request with { AccountingPeriodReopening = new("4") } : rig.Request with { ExpectedVersion = "7" };
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(r))).Status);
        Assert.Equal(1, rig.Port.Approvals); Assert.Single(rig.Port.Commands);
    }

    [Theory] [InlineData(AccountingApiStatus.Denied,403)] [InlineData(AccountingApiStatus.Unavailable,503)]
    [InlineData(AccountingApiStatus.Conflict,409)] [InlineData(AccountingApiStatus.NotFound,404)]
    [InlineData(AccountingApiStatus.Invalid,400)] [InlineData(AccountingApiStatus.Unprocessable,422)]
    public async Task Original_native_terminal_refusal_is_propagated_without_a_fabricated_success(AccountingApiStatus status,int code)
    {
        foreach (var reopen in new[] { false,true })
        {
            using var rig=new Rig(reopen);rig.Port.Refusal=status;
            var failure=await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide());
            Assert.Equal(code,failure.Status);Assert.True(failure.Replayed);Assert.Equal("SENTETIK-native-original",failure.OriginalCorrelationId);
            Assert.Equal("native-refusal",failure.Code);Assert.Equal(0,rig.Port.Approvals);Assert.Empty(rig.Port.Commands);
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Reopening_human_wire_contains_the_actual_native_later_closed_fact(bool laterClosed)
    {
        using var rig=new Rig(true);rig.Port.LaterClosed=laterClosed;var p=await rig.Preview();
        Assert.Equal(laterClosed,p.AccountingPeriodReopening!.LaterPeriodClosed);
        Assert.Equal(!laterClosed,p.ApprovalSupported);
        var human=JsonSerializer.SerializeToElement(p,Json).GetProperty("accountingPeriodReopening");
        Assert.Equal(6,human.EnumerateObject().Count());Assert.Equal(laterClosed,human.GetProperty("laterPeriodClosed").GetBoolean());
        Assert.Equal(laterClosed?2:1,p.Rules.Count);Assert.Empty(rig.Port.Commands);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Permission_lost_after_completed_protocol_suppresses_release_without_claiming_rollback(bool reopen)
    {
        using var rig=new Rig(reopen);rig.Port.BeforeReturn=()=>rig.Authority.Allowed=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<WorkDecisionException>(()=>rig.Decide())).Status);
        Assert.Equal(1,rig.Port.Approvals);Assert.Single(rig.Port.Commands);
        rig.Port.BeforeReturn=null;rig.Authority.Allowed=true;
        var replay=await rig.Decide();Assert.True(replay.Replayed);Assert.Equal(Correlation,replay.OriginalCorrelationId);
        Assert.Equal(1,rig.Port.Approvals);Assert.Single(rig.Port.Commands);
    }

    private sealed class Rig : IDisposable
    {
        public readonly bool Reopen; public string Kind => Reopen ? WorkDecisionHostV1.AccountingPeriodReopening : WorkDecisionHostV1.AccountingPeriodOpening;
        public Guid Id => Reopen ? Proposal : Period;
        public MutableCompany Company { get; } = new(); public MutableUser User { get; } = new(); public Authority Authority { get; } = new();
        public Delegations Delegations { get; } = new(); public Port Port { get; } public SourceProxy Source { get; } public WorkDecisionHostV1 Host { get; }
        private readonly AccountingDbContext db;
        public Rig(bool reopen = false, bool injectOwner = true, Guid? openingMaker = null)
        {
            Reopen = reopen; Company.Set(WorkAccountingPeriodDecisionHostTests.Company); var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null);
            var execution = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(Authority, execution);
            Port = new(reopen, openingMaker); var owner = new AccountingApiService(Port, null!, Port, authority, Company, User, correlation);
            db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("period-work-" + Guid.NewGuid()).Options, Company);
            Source = new(reopen ? new AccountingPeriodReopeningWorkInboxSourceV1(db, new(Company, User, authority), owner)
                : new AccountingPeriodOpeningWorkInboxSourceV1(db, new(Company, User, authority), owner));
            Host = new(Company, User, correlation, authority, [Source], Delegations, execution, null!, null!, accountingPeriods: injectOwner ? owner : null);
        }
        public WorkDecisionRequestV1 Request => new("1", true, null, AccountingPeriodReopening: Reopen ? new("3") : null);
        public Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(Kind, Id, default);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? r = null) => Host.DecideAsync(Kind, Id, r ?? Request, Key, default);
        public void Dispose() => db.Dispose();
    }
    private sealed class Port : IAccountingPeriodStore, IAccountingPeriodDecisionStoreV1, IAccountingPeriodReopeningStoreV1, IAccountingCommandLog
    {
        public AccountingPeriodRecord Record; public AccountingPeriodReopeningViewV1 Reopening; public AccountingPeriodClosureViewV1 Close;
        public List<AccountingCommandRecord> Commands { get; } = []; public int Approvals, Reads; public long Expected; public long? ExpectedPeriod; public string? Key;
        public bool LaterClosed; public AccountingApiStatus? Refusal; public Action? BeforeReturn; public Action? BeforeApprove; public Action<int>? ChangeRead; public Func<AccountingPeriodApprovalReceiptV1, AccountingPeriodApprovalReceiptV1?>? ChangeReceipt;
        private Func<CancellationToken, Task>? executionFence;
        public Port(bool reopen, Guid? openingMaker)
        {
            var maker = openingMaker ?? Maker; var period = AccountingPeriod.Propose(Period, Company, new(2026,11,1),new(2026,11,30),maker,At);
            if (reopen) { period.Approve(maker == Actor ? Maker : Actor, At.AddMinutes(1)); period.Close(Maker, At.AddMinutes(2)); }
            Record = new(period, reopen ? 3 : 1);
            Reopening = new(Proposal,Company,Period,Closure,3,Maker,At.AddMinutes(3),"SENTETIK yeniden açma gerekçesi",null,null,1);
            Close = new(Closure,Company,Period,3,Guid.NewGuid(),Guid.NewGuid(),new string('A',64),0,Maker,At.AddMinutes(2),null,null,null);
        }
        public async Task<AccountingApiResult<T>> RunPeriodApprovalAsync<T>(string command, Func<bool, Task<AccountingApiResult<T>>> attempt, Func<CancellationToken,Task> fence, CancellationToken token = default) where T : class
        {
            executionFence = fence;
            if (Refusal is { } refusal) return new(refusal,null,true,"native-refusal",OriginalCorrelationId:"SENTETIK-native-original");
            try
            {
                await fence(token); var answer = await attempt(false); await fence(token); BeforeReturn?.Invoke();
                if (ChangeReceipt is not null && answer.Resource is PeriodViewV1 o && o.ApprovalReceipt is { } op)
                    return (AccountingApiResult<T>)(object)(((AccountingApiResult<PeriodViewV1>)(object)answer) with { Resource = o with { ApprovalReceipt = ChangeReceipt(op) } });
                if (ChangeReceipt is not null && answer.Resource is AccountingPeriodReopeningViewV1 r && r.ApprovalReceipt is { } rp)
                    return (AccountingApiResult<T>)(object)(((AccountingApiResult<AccountingPeriodReopeningViewV1>)(object)answer) with { Resource = r with { ApprovalReceipt = ChangeReceipt(rp) } });
                return answer;
            }
            catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied,null,false,null); }
        }
        public async Task<AccountingPeriodRecord?> ReadOpeningDecisionAsync(Guid id, Func<CancellationToken,Task> fence, CancellationToken token = default)
        { await fence(token); ChangeRead?.Invoke(++Reads); await fence(token); return id == Period ? Record : null; }
        public async Task<AccountingPeriodReopeningDecisionSourceV1?> ReadReopeningDecisionAsync(Guid id, Func<CancellationToken,Task> fence, CancellationToken token = default)
        { await fence(token); ChangeRead?.Invoke(++Reads); await fence(token); return id == Proposal ? new(Record, Reopening, Close) { LaterPeriodClosed = LaterClosed } : null; }
        public async Task<AccountingPeriodRecord> ApprovePeriodAsync(Guid id, long expected, Guid actor, AccountingCommandMetadata m, CancellationToken token = default)
        {
            BeforeApprove?.Invoke(); await executionFence!(token); Expected = expected; Key = m.IdempotencyKey;
            if (Record.Version != expected) throw new AccountingVersionConflictException(expected,Record.Version);
            Record.Period.Approve(actor,At.AddMinutes(5)); Record = Record with { Version = expected+1 }; Approvals++;
            Commands.Add(m.Completion!(Record)); return Record;
        }
        public async Task<AccountingPeriodReopeningViewV1> ApproveReopeningAsync(Guid period, Guid proposal, ApproveAccountingPeriodReopeningV1 request, Guid actor, AccountingCommandMetadata m, CancellationToken token)
        {
            BeforeApprove?.Invoke(); await executionFence!(token); Expected = request.ExpectedVersion; ExpectedPeriod = request.ExpectedPeriodVersion; Key=m.IdempotencyKey;
            if (Record.Version != request.ExpectedPeriodVersion) throw new AccountingVersionConflictException(request.ExpectedPeriodVersion,Record.Version);
            if (Reopening.Version != request.ExpectedVersion) throw new AccountingVersionConflictException(request.ExpectedVersion,Reopening.Version);
            if (Reopening.ProposedBy == actor) throw new InvalidOperationException("Synthetic protocol cannot approve its own proposal");
            Reopening = Reopening with { ApprovedBy=actor,ApprovedAtUtc=At.AddMinutes(5),Version=request.ExpectedVersion+1 };
            Close = Close with { ReopeningId=proposal,ReopenedBy=actor,ReopenedAtUtc=At.AddMinutes(5) };
            Record = new(AccountingPeriod.Restore(Period, Company, Record.Period.StartsOn, Record.Period.EndsOn,
                Record.Period.PreparedByUserId, Record.Period.PreparedAtUtc, Record.Period.ApprovedByUserId, Record.Period.ApprovedAtUtc, null, null), request.ExpectedPeriodVersion+1);
            Approvals++;Commands.Add(m.Completion!(Reopening));return Reopening;
        }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command,string key,CancellationToken token=default)=>Task.FromResult(Commands.SingleOrDefault(x=>x.CommandName==command&&x.IdempotencyKeyHash==key));
        public Task RecordCommandAsync(AccountingCommandRecord c,CancellationToken token=default){Commands.Add(c);return Task.CompletedTask;}
        public Task<AccountingPeriodRecord?> FindPeriodAsync(Guid id,CancellationToken token=default)=>Task.FromResult<AccountingPeriodRecord?>(id==Period?Record:null);
        public Task<(AccountingPeriodRecord Record,AccountingWriteOutcome Outcome)> ProposePeriodAsync(Guid id,DateOnly start,DateOnly end,Guid actor,AccountingCommandMetadata m,CancellationToken token=default)=>throw new InvalidOperationException("No propose in a Work decision");
        public Task<AccountingPeriodRecord> ClosePeriodAsync(Guid id,long expected,Guid actor,AccountingCommandMetadata m,CancellationToken token=default)=>throw new InvalidOperationException("No close in a Work decision");
        public Task<AccountingPage<AccountingPeriodRecord>> ListPeriodsAsync(int size,string? cursor,CancellationToken token=default)=>throw new InvalidOperationException("No general browse grant");
        public Task<AccountingPeriodRecord?> FindPeriodCoveringAsync(DateOnly date,CancellationToken token=default)=>throw new InvalidOperationException("No guessed period selection");
        public Task<AccountingPeriodLifecycleViewV1?> ReadLifecycleAsync(Guid period,CancellationToken token)=>throw new InvalidOperationException("No general lifecycle grant");
        public Task<AccountingPeriodReopeningViewV1> ProposeReopeningAsync(Guid period,ProposeAccountingPeriodReopeningV1 request,Guid actor,AccountingCommandMetadata m,CancellationToken token)=>throw new InvalidOperationException("No proposal creation");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public int Reads; public Func<WorkApprovalDecisionSnapshotV1,WorkApprovalDecisionSnapshotV1>? Change;
        public string Module=>inner.Module;public string Kind=>inner.Kind;public string ApproverPermission=>inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c,int count,CancellationToken t)=>inner.ListPendingAsync(c,count,t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c,Guid id,CancellationToken t)
        {Reads++;var s=await inner.ReadDecisionAsync(c,id,t);return s is null?null:Change?.Invoke(s)??s;}
    }
    private sealed class MutableCompany:ICompanyContext {public bool IsResolved=>CompanyId!=Guid.Empty;public Guid CompanyId{get;private set;}public void Set(Guid id)=>CompanyId=id;}
    private sealed class MutableUser:ICurrentUser {public Guid ActorId=Actor;public bool IsAuthenticated=>true;public string UserId=>ActorId.ToString("D");public IReadOnlySet<Guid> AllowedCompanyIds{get;}=new HashSet<Guid>{Company};public bool CanAccessCompany(Guid id)=>AllowedCompanyIds.Contains(id);}
    private sealed class Authority:IErpAuthorityRevalidationService
    {public bool Allowed=true;public Task<PermissionScope> RevalidatePermissionScopeAsync(string p,Guid c,CancellationToken token=default)
        =>Task.FromResult(new PermissionScope(Allowed&&p is AccountingPermissionManifestV1.PeriodsApprove or AccountingPermissionManifestV1.PeriodsReopen,true,new HashSet<string>(),new HashSet<Guid>(),new HashSet<Guid>(),new HashSet<Guid>()));}
    private sealed class Delegations:IWorkDelegationReaderV1
    {public bool Live=true;public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id,WorkInboxItemV1 item,CancellationToken t=default)=>Task.FromResult<WorkDelegationViewV1?>(Live?new(id,Company,item.Kind,item.Kind==WorkDecisionHostV1.AccountingPeriodOpening?AccountingPermissionManifestV1.PeriodsApprove:AccountingPermissionManifestV1.PeriodsReopen,Guid.NewGuid(),Guid.NewGuid(),At,DateTimeOffset.UtcNow.AddDays(1),"1",Guid.NewGuid(),At,"SENTETIK",null,null,null):null);}
}
