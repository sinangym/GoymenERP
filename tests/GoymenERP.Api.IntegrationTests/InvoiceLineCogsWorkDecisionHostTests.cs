using System.Globalization;
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

// Actual native Application/source over an explicit synthetic command protocol; not SQL, Audit, HTTP or commit acceptance.
public sealed class InvoiceLineCogsWorkDecisionHostTests
{
    internal static readonly Guid Company = Guid.Parse("ac540000-0000-4000-8000-000000000001"), Id = Guid.Parse("ac540000-0000-4000-8000-000000000002"),
        Actor = Guid.Parse("ac540000-0000-4000-8000-000000000003"), Maker = Guid.Parse("ac540000-0000-4000-8000-000000000004"),
        Invoice = Guid.Parse("ac540000-0000-4000-8000-000000000005"), Line = Guid.Parse("ac540000-0000-4000-8000-000000000006");
    internal static readonly DateTimeOffset At = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Key = "SENTETIK-work-cogs", Correlation = "SENTETIK-work-cogs-correlation";

    [Fact]
    public async Task Full_human_preview_and_original_approved_receipt_keep_proposal_and_line_versions_separate()
    {
        using var rig = new Rig(); var preview = await rig.Preview(); var human = Assert.IsType<WorkInvoiceLineCogsPreviewV1>(preview.InvoiceLineCogs);
        Assert.True(preview.Pending); Assert.False(preview.ApprovalReasonRequired); Assert.False(preview.RejectionSupported);
        Assert.Equal("1", human.Proposal.Version); Assert.Equal("0", human.CurrentLineVersion); Assert.Equal("9007199254740993", human.Proposal.FinalizedVersion);
        Assert.Equal(rig.Port.Facts.Proposal.Reason, human.Proposal.Reason); Assert.Equal(rig.Port.Facts.Proposal.PostedEvidence!.Amount, human.Proposal.PostedEvidence!.Amount);
        var first = await rig.Decide(); Assert.Equal("Approved", first.State); Assert.False(first.Replayed); Assert.Equal(1, rig.Port.Effects);
        Assert.Equal(1L, rig.Port.Expected); Assert.Equal(Key, rig.Port.Key);
        Assert.All(rig.Authority.Permissions, permission => Assert.Equal(AccountingJournalReversalPermissionNamesV1.Approve, permission));
        rig.Port.Facts = rig.Port.Facts with { CurrentLineVersion = long.MaxValue }; rig.Port.LiveUnavailable = true;
        var sourceReads = rig.Port.LiveReads; var decisionReads = rig.Port.DecisionReads;
        var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands); Assert.Equal(sourceReads, rig.Port.LiveReads);
        Assert.Equal(decisionReads + 2, rig.Port.DecisionReads); // Source + private receipt query, no second POST human GET.
        var terminal = await rig.Preview(); Assert.Equal("2", terminal.SourceVersion); Assert.False(terminal.Pending); Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), terminal.InvoiceLineCogs!.CurrentLineVersion);
        Assert.Equal(4, JsonSerializer.SerializeToElement(rig.Request, Json).EnumerateObject().Count()); Assert.Equal(6, JsonSerializer.SerializeToElement(replay, Json).EnumerateObject().Count());
        Assert.DoesNotContain("approvalReceipt", rig.Port.Commands[0].ResultJson, StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData("0")] [InlineData("2")] [InlineData("01")] [InlineData("+1")] [InlineData("1\n")] [InlineData("1\r")]
    [InlineData("9223372036854775807")] [InlineData("9223372036854775808")] [InlineData("")]
    public async Task Only_original_native_proposal_version_one_is_a_command(string version)
    {
        using var rig = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(rig.Request with { ExpectedVersion = version }))).Status);
        Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Effects);
    }
    [Theory] [InlineData("reject")] [InlineData("reason")] [InlineData("mapping")] [InlineData("catalog")] [InlineData("period")]
    [InlineData("normal")] [InlineData("package")] [InlineData("manufacturing")] [InlineData("loss")] [InlineData("overage")]
    [InlineData("plan")] [InlineData("supplier")] [InlineData("return")]
    public async Task Approve_null_reason_four_field_body_refuses_every_foreign_selection_before_source_read(string fault)
    {
        using var rig = new Rig(); var request = fault switch
        {
            "reject" => rig.Request with { Approve = false }, "reason" => rig.Request with { Reason = "" },
            "mapping" => rig.Request with { PostingAccountMappingSelection = new("0") }, "catalog" => rig.Request with { ApprovedSourceCatalogSelection = new("0") },
            "period" => rig.Request with { AccountingPeriodReopening = new("1") }, "normal" => rig.Request with { NormalJournalDraft = new(Id) },
            "package" => rig.Request with { ProductPackageStandard = new("2026-11-28") }, "manufacturing" => rig.Request with { ManufacturingPlanReapproval = new([]) },
            "loss" => rig.Request with { InventoryTransferLoss = new(1, At) }, "overage" => rig.Request with { InventoryTransferOverage = new(1, At) },
            "plan" => rig.Request with { LogisticsPlanExcess = new("1") }, "supplier" => rig.Request with { SupplierReturnCostException = new() },
            _ => rig.Request with { LogisticsCustomerReturn = new(Id) },
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status); Assert.Equal(0, rig.Source.Reads); Assert.Equal(0, rig.Port.Effects);
    }
    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("maker")] [InlineData("status")]
    [InlineData("version")] [InlineData("digest")] [InlineData("preview")] [InlineData("caption")] [InlineData("reference")]
    [InlineData("rule")] [InlineData("scope")] [InlineData("cross-proof")]
    public async Task Scalar_identity_and_native_formatter_are_checked_before_dispatch(string fault)
    {
        using var rig = new Rig(); rig.Source.Change = s =>
        {
            var p = s.InvoiceLineCogsProof!;
            return s with { InvoiceLineCogsProof = fault switch
            {
                "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "id" => p with { ProposalId = Guid.NewGuid() },
                "maker" => p with { PreparedBy = Actor }, "status" => p with { Status = "Approved" },
                "digest" => p with { SourceFactsDigest = "bad" }, "preview" => p with { PreviewDigest = "bad" }, _ => p,
            }, SourceVersion = fault == "version" ? "01" : s.SourceVersion,
                ManufacturingCostDifferenceProof = fault == "cross-proof" ? new(Company, Id, Invoice, Id, Id, Id, Maker, At, "Prepared", new('A', 64), new('A', 64)) : null,
                Item = s.Item with { DocumentLabel = fault == "caption" ? "other" : s.Item.DocumentLabel,
                    References = fault == "reference" ? [s.Item.References[0] with { RecordId = Guid.NewGuid() }, s.Item.References[1]] : s.Item.References,
                    Rules = fault == "rule" ? [s.Item.Rules[0] with { Actual = "1" }] : s.Item.Rules,
                    Scope = fault == "scope" ? new(Id, null, null) : s.Item.Scope } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Effects);
    }
    [Fact]
    public async Task Valid_shape_source_digest_mismatch_after_native_success_is_unknown_and_original_key_reconciles_once()
    {
        using var rig = new Rig(); rig.Source.Change = s => s with { InvoiceLineCogsProof = s.InvoiceLineCogsProof! with { SourceFactsDigest = new('B', 64) } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
        rig.Source.Change = null; var replay = await rig.Decide(); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands); // No rollback claim after the post-native suppression.
    }
    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("actor")] [InlineData("key")] [InlineData("payload")]
    [InlineData("expected")] [InlineData("result")] [InlineData("digest")] [InlineData("correlation")] [InlineData("view")]
    public async Task Missing_or_forged_native_original_receipt_never_becomes_Work_success(string fault)
    {
        using var rig = new Rig(); rig.Port.AfterAnswer = answer =>
        {
            var v = answer.Resource!; var p = v.ApprovalReceipt!;
            return answer with { Resource = v with { ApprovalReceipt = fault switch
            {
                "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() }, "actor" => p with { ActorId = Maker },
                "key" => p with { IdempotencyKeyHash = new('B', 64) }, "payload" => p with { PayloadHash = new('B', 64) },
                "expected" => p with { ExpectedVersion = 2 }, "result" => p with { ResultVersion = 3 },
                "digest" => p with { OriginalResultDigest = new('B', 64) }, "correlation" => p with { OriginalCorrelationId = "other" }, _ => p,
            }, InvoiceId = fault == "view" ? Guid.NewGuid() : v.InvoiceId } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
    }
    [Theory] [InlineData("company")] [InlineData("id")] [InlineData("facts")] [InlineData("preview")]
    public async Task Human_second_read_cannot_replace_the_first_private_snapshot(string fault)
    {
        using var rig = new Rig(); rig.Port.ChangeRead = (facts, count) => count != 2 ? facts : fault switch
        {
            "company" => facts with { Proposal = facts.Proposal with { CompanyId = Guid.NewGuid() } },
            "id" => facts with { Proposal = facts.Proposal with { ProposalId = Guid.NewGuid() } },
            "facts" => facts with { SourceHash = new('B', 64) }, _ => facts with { CurrentLineVersion = 1 },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status); Assert.Equal(0, rig.Port.Effects);
    }
    [Theory] [InlineData("maker")] [InlineData("permission")] [InlineData("company")] [InlineData("module")] [InlineData("owner-permission")]
    public async Task Existing_companywide_approve_and_real_maker_boundaries_remain_closed(string fault)
    {
        using var rig = new Rig(); if (fault == "maker") rig.User.Actor = Maker; if (fault == "permission") rig.Authority.Allowed = false;
        if (fault == "company") rig.Company.Set(Guid.NewGuid()); if (fault == "module") rig.Source.ModuleOverride = "finance";
        if (fault == "owner-permission") rig.Source.PermissionOverride = AccountingPermissionManifestV1.PeriodsApprove;
        Assert.Equal(fault is "module" or "owner-permission" ? 503 : 403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status);
        Assert.Equal(0, rig.Port.Effects);
    }
    [Theory] [InlineData("permission")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Native_await_fence_honors_live_authority_delegation_and_cancellation_before_mutation(string fault)
    {
        using var rig = new Rig(); using var cts = new CancellationTokenSource(); var request = rig.Request with { DelegationId = fault == "delegation" ? Id : null };
        rig.Port.BeforeApprove = () => { if (fault == "permission") rig.Authority.Allowed = false; if (fault == "delegation") rig.Delegations.Live = false; if (fault == "cancel") cts.Cancel(); };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Decide(request, cts.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }
    [Theory] [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("cancel")]
    public async Task Human_owner_await_cannot_release_facts_after_current_scope_or_token_changes(string fault)
    {
        using var rig = new Rig(); using var cts = new CancellationTokenSource();
        rig.Port.ChangeRead = (facts, count) =>
        {
            if (count == 2)
            {
                if (fault == "permission") rig.Authority.Allowed = false;
                if (fault == "company") rig.Company.Set(Guid.NewGuid());
                if (fault == "actor") rig.User.Actor = Maker;
                if (fault == "cancel") cts.Cancel();
            }
            return facts;
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Preview(cts.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Preview())).Status);
        Assert.Equal(2, rig.Port.DecisionReads); Assert.Equal(0, rig.Port.Effects); Assert.Empty(rig.Port.Commands);
    }
    [Theory] [InlineData("permission")] [InlineData("delegation")] [InlineData("cancel")]
    public async Task Late_native_release_refusal_suppresses_response_and_original_key_reconciles_without_second_effect(string fault)
    {
        using var rig = new Rig(); using var cts = new CancellationTokenSource();
        var request = rig.Request with { DelegationId = fault == "delegation" ? Id : null };
        rig.Port.AfterAnswer = answer =>
        {
            if (fault == "permission") rig.Authority.Allowed = false;
            if (fault == "delegation") rig.Delegations.Live = false;
            if (fault == "cancel") cts.Cancel();
            return answer;
        };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Decide(request, cts.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide(request))).Status);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands); // Committed synthetic outcome, not a rollback claim.
        rig.Port.AfterAnswer = null; rig.Authority.Allowed = true; rig.Delegations.Live = true;
        var replay = await rig.Decide(request); Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId);
        Assert.Equal(1, rig.Port.Effects); Assert.Single(rig.Port.Commands);
    }
    [Theory] [InlineData("not-found", 404)] [InlineData("version", 409)] [InlineData("maker", 403)]
    public async Task Native_refusal_codes_and_durable_terminal_replay_are_propagated_without_success(string fault, int status)
    {
        using var rig = new Rig(); rig.Port.Fault = fault;
        var first = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide()); Assert.Equal(status, first.Status); Assert.Equal(0, rig.Port.Effects);
        if (fault == "maker") { Assert.Empty(rig.Port.Commands); return; }
        var replay = await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide()); Assert.Equal(status, replay.Status); Assert.True(replay.Replayed);
        Assert.Equal(first.Code, replay.Code); Assert.Equal(Correlation, replay.OriginalCorrelationId); Assert.Single(rig.Port.Commands); Assert.Equal(1, rig.Port.Refusals);
    }
    [Fact]
    public async Task Private_proof_on_another_kind_is_rejected_and_unconfigured_owner_never_calls_native_command()
    {
        using var rig = new Rig(inject: false); Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => rig.Decide())).Status); Assert.Equal(0, rig.Port.Effects);
        var snapshot = (await rig.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default))!;
        var other = new FixedSource(snapshot); var host = rig.WithSource(other);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(other.Kind, Id, default))).Status);
        var nativePreview = new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.BankAccount, Id, "SENTETIK", "1", true, false, [], []);
        Assert.DoesNotContain("invoiceLineCogs", JsonSerializer.Serialize(nativePreview, Json));
    }

    internal static InvoiceLineCogsDecisionSourceV1 NewFacts()
    {
        var source = new FinalizedInvoiceLineReferenceV1(Company, Invoice, Line, Id, 9007199254740993L, Id, Id, Id, 1, 1, new(2026, 11, 28), "SENTETIK invoice", Id, new('A', 64));
        var p = new InvoiceLineCogsCorrectionViewV1(Id, Company, Invoice, Line, source.FinalizedVersion, 1, 0, "Prepared", InvoiceLineCogsClassificationV1.Manufactured,
            new(Id, 9007199254740993L, long.MaxValue, "SENTETIK-plan", new('a', 64), new(Id, "620", "SENTETIK account")), Id,
            new(2026, 11, 27), new(2026, 11, 28), null, Maker, At, null, null, "SENTETIK reason",
            new(Company, Invoice, Line, source.FinalizedVersion, Id, 1, 2, Id, Id, Id, "100.0100", new('A', 64)), null, null);
        return new(p, p, source, new('A', 64), 0, false);
    }
    private sealed class Rig : IDisposable
    {
        public MutableCompany Company { get; } = new(); public MutableUser User { get; } = new(); public Authority Authority { get; } = new(); public Delegations Delegations { get; } = new();
        public Protocol Port { get; } = new(); public SourceProxy Source { get; } private readonly AccountingDbContext db;
        private readonly WorkDecisionExecutionFenceV1 execution = new(); private readonly CorrelationContext correlation = new(); private readonly WorkDecisionAuthorityV1 authority;
        public InvoiceLineCogsCorrectionServiceV1 Owner { get; } public WorkDecisionHostV1 Host { get; }
        public Rig(bool inject = true)
        {
            Company.Set(InvoiceLineCogsWorkDecisionHostTests.Company); correlation.Set(Correlation, null, "Web", null, null); authority = new(Authority, execution);
            Owner = new(Port, Port, Port, Port, authority, Company, User, correlation);
            db = new(new DbContextOptionsBuilder<AccountingDbContext>().UseInMemoryDatabase("cogs-host-" + Guid.NewGuid()).Options, Company);
            Source = new(new AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1(db, new(Company, User, authority), Owner));
            Host = new(Company, User, correlation, authority, [Source], Delegations, execution, null!, null!, invoiceLineCogs: inject ? Owner : null);
        }
        public WorkDecisionRequestV1 Request => new("1", true, null);
        public Task<WorkDecisionPreviewV1> Preview(CancellationToken token = default) => Host.PreviewAsync(WorkDecisionHostV1.InvoiceLineCogs, Id, token);
        public Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request = null, CancellationToken token = default) => Host.DecideAsync(WorkDecisionHostV1.InvoiceLineCogs, Id, request ?? Request, Key, token);
        public WorkDecisionHostV1 WithSource(IWorkInboxSourceV1 source) => new(Company, User, correlation, authority, [source], Delegations, execution, null!, null!, invoiceLineCogs: Owner);
        public void Dispose() => db.Dispose();
    }
    private sealed class Protocol : IInvoiceLineCogsCorrectionStoreV1, IInvoiceLineCogsDecisionStoreV1, IAccountingCommandLog, IAccountingFinalizedInvoiceLineSourceV1, IAccountingInvoiceLineProductReferenceV1
    {
        public InvoiceLineCogsDecisionSourceV1 Facts = NewFacts(); public int Effects, DecisionReads, LiveReads; public bool LiveUnavailable;
        public long Expected; public string? Key, Fault; public int Refusals; public Action? BeforeApprove; public Func<InvoiceLineCogsDecisionSourceV1, int, InvoiceLineCogsDecisionSourceV1?>? ChangeRead;
        public Func<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>, AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>? AfterAnswer;
        public List<AccountingCommandRecord> Commands { get; } = [];
        public async Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> RunApprovalAsync(Func<bool, Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>> attempt, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { try { await fence(token); var answer = await attempt(false); await fence(token); return AfterAnswer?.Invoke(answer) ?? answer; } catch (NormalJournalAuthorityDeniedException) { return new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied); } }
        public async Task<InvoiceLineCogsDecisionSourceV1?> ReadCogsDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); DecisionReads++; var answer = ChangeRead?.Invoke(Facts, DecisionReads) ?? Facts; await fence(token); return id == Facts.Proposal.ProposalId ? answer : null; }
        public async Task<InvoiceLineCogsCorrectionViewV1> ApproveAsync(FinalizedInvoiceLineSourceV1 source, Guid proposal, long expected, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            BeforeApprove?.Invoke(); await fence(token); token.ThrowIfCancellationRequested(); Expected = expected; Key = metadata.IdempotencyKey;
            if (Fault == "not-found") throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.NotFound, "SENTETIK");
            if (Fault == "maker") throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.SelfApproval, "SENTETIK");
            if (Fault == "version" || expected != 1 || proposal != Id) throw new AccountingVersionConflictException(expected, 2);
            if (Facts.Proposal.Status != "Prepared") throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.NotPrepared, "SENTETIK");
            if (Facts.CurrentLineVersion != Facts.Prepared.ExpectedLineVersion) throw new AccountingVersionConflictException(Facts.Prepared.ExpectedLineVersion, Facts.CurrentLineVersion);
            var approved = Facts.Prepared with { Version = 2, Status = "Approved", ApprovedBy = actor, ApprovedAtUtc = At.AddSeconds(1) };
            Facts = Facts with { Proposal = approved, RevisionBound = true, CurrentLineVersion = Facts.Prepared.ExpectedLineVersion + 1 };
            Effects++; Commands.Add(metadata.Completion!(approved)); return approved;
        }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string hash, CancellationToken token = default) => Task.FromResult(Commands.SingleOrDefault(x => x.CommandName == command && x.IdempotencyKeyHash == hash));
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command); return Task.CompletedTask; }
        public Task<FinalizedInvoiceLineSourceReadV1> GetAsync(Guid company, Guid invoice, Guid line, CancellationToken token = default)
        {
            LiveReads++; var s = Facts.Source;
            return Task.FromResult(new FinalizedInvoiceLineSourceReadV1(LiveUnavailable ? FinalizedInvoiceLineSourceOutcomeV1.Unavailable : FinalizedInvoiceLineSourceOutcomeV1.Available, company, invoice, line,
                LiveUnavailable ? null : new(company, invoice, line, s.FinalizationAdmissionId, s.FinalizedVersion, s.ProductId, s.ShipmentId, s.DispatchNoteId, s.DispatchNoteLineNo, s.InvoiceLineNo, s.InvoiceDate, s.OfficialInvoiceNumber, s.Ettn, "{}", s.Fingerprint)));
        }
        public Task<FinalizedInvoiceCogsSelectionReadV1> FindAsync(Guid company, string number, CancellationToken token = default) => throw new InvalidOperationException("No source browse");
        public Task<InvoiceLineCogsProductReferenceV1> GetAsync(Guid company, Guid product, CancellationToken token = default) => throw new InvalidOperationException("No product browse");
        public Task<InvoiceLineCogsOptionsV1> OptionsAsync(CancellationToken token = default) => throw new InvalidOperationException("No options");
        public async Task RecordAttemptAsync(AccountingCommandRecord record, Func<CancellationToken, Task> fence, CancellationToken token = default) { await fence(token); Refusals++; Commands.Add(record); }
        public Task<InvoiceLineCogsHistoryV1> ReadAsync(FinalizedInvoiceLineSourceV1 source, CancellationToken token = default) => throw new InvalidOperationException("No history");
        public Task<InvoiceLineCogsCorrectionViewV1?> FindAsync(Guid id, CancellationToken token = default) => throw new InvalidOperationException("No general read");
        public Task<InvoiceLineCogsCorrectionViewV1> PrepareAsync(FinalizedInvoiceLineSourceV1 source, PrepareInvoiceLineCogsCorrectionV1 request, Guid actor, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new InvalidOperationException("No prepare");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public int Reads; public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change; public string? ModuleOverride, PermissionOverride;
        public string Module => ModuleOverride ?? inner.Module; public string Kind => inner.Kind; public string ApproverPermission => PermissionOverride ?? inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int max, CancellationToken t) => inner.ListPendingAsync(c, max, t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) { Reads++; var answer = await inner.ReadDecisionAsync(c, id, t); return answer is null ? null : Change?.Invoke(answer) ?? answer; }
    }
    private sealed class FixedSource(WorkApprovalDecisionSnapshotV1 snapshot) : IWorkApprovalDecisionSourceV1
    {
        public string Module => "finance"; public string Kind => WorkDecisionHostV1.BankAccount; public string ApproverPermission => AccountingJournalReversalPermissionNamesV1.Approve;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int m, CancellationToken t) => throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot with { Item = snapshot.Item with { Module = Module, Kind = Kind } });
    }
    private sealed class MutableCompany : ICompanyContext { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class MutableUser : ICurrentUser { public Guid Actor = InvoiceLineCogsWorkDecisionHostTests.Actor; public bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company }; public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class Authority : IErpAuthorityRevalidationService { public bool Allowed = true; public List<string> Permissions { get; } = []; public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid id, CancellationToken token = default) { Permissions.Add(permission); return Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); } }
    private sealed class Delegations : IWorkDelegationReaderV1 { public bool Live = true; public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live ? new(id, Company, item.Kind, AccountingJournalReversalPermissionNamesV1.Approve, Maker, Actor, At.AddDays(-2), At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null); }
}
