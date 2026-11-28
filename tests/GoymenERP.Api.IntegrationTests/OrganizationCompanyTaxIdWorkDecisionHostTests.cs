using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.WorkInbox;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;
using static GoymenERP.Api.IntegrationTests.InventoryCostWorkDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

// Real Work host, authority decorator and owner source; controlled native response/receipt protocol only.
// These tests do not prove the real Organization owned SQL transaction, audit or rollback.
public sealed class OrganizationCompanyTaxIdWorkDecisionHostTests
{
    private const string Kind = OrganizationCompanyTaxIdDecisionBindingV1.KindName;
    private const string Key = "SENTETIK-tax-work-key", Correlation = "SENTETIK-tax-work-correlation";
    [Theory]
    [InlineData(true, null)] [InlineData(false, null)] [InlineData(true, "  SENTETIK note  ")] [InlineData(false, "")]
    public async Task Human_and_single_native_command_keep_optional_note_company_version_and_original_key(bool approve, string? note)
    {
        using var r = new Rig(); var preview = await r.Preview();
        var human = Assert.IsType<WorkOrganizationCompanyTaxIdDecisionPreviewV1>(preview.OrganizationCompanyTaxId);
        Assert.Equal("7", preview.SourceVersion); Assert.Equal("7", human.CompanyVersion); Assert.Equal("7", human.CompanyVersionAtRequest);
        Assert.True(preview.Pending); Assert.True(preview.ApprovalSupported); Assert.True(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        Assert.Equal(2, r.Native.Reads);
        var request = r.Request with { Approve = approve, Reason = note }; var reads = r.Native.Reads;
        var answer = await r.Decide(request);
        Assert.Equal(approve ? "approved" : "rejected", answer.State); Assert.False(answer.Replayed);
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(1, r.Native.ReceiptReads); // Source only; no second human GET in POST.
        Assert.Equal(new DecideCompanyTaxIdChange(7, approve ? "approve" : "reject", note), r.Native.LastCommand);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands); Assert.Equal(Key, r.Native.LastKey);
        Assert.Equal(4, JsonSerializer.SerializeToElement(request, Json).EnumerateObject().Count());
        r.Native.CurrentCompanyVersion = 99; r.Native.CurrentVkn = "9000000041"; // Later company/new request does not rewrite this old request.
        reads = r.Native.Reads;
        var replay = await r.Decide(request);
        Assert.True(replay.Replayed); Assert.Equal(Correlation, replay.OriginalCorrelationId); Assert.Equal(answer.State, replay.State);
        Assert.Equal(reads + 1, r.Native.Reads); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        Assert.All(r.Authority.Permissions, permission => Assert.Equal(OrganizationCompanyTaxIdPermissionsV1.Approve, permission));
    }
    [Fact]
    public async Task Stale_pending_is_reject_only_but_terminal_virtual_item_does_not_claim_another_rejection_is_needed()
    {
        using var r = new Rig(); r.Native.CurrentCompanyVersion = 11;
        var preview = await r.Preview(); Assert.True(preview.Pending); Assert.False(preview.ApprovalSupported);
        Assert.True(preview.RejectionSupported); Assert.Contains(preview.Rules, x => x.RuleCode == OrganizationCompanyTaxIdChangeWorkInboxSourceV1.StaleRule);
        var denied = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(r.Request with { ExpectedVersion = "11" }));
        Assert.Equal(409, denied.Status); Assert.Equal(OrganizationProblemCodesV1.CompanyTaxIdChangeStale, denied.Code); Assert.Equal(0, r.Native.Effects);
        var rejected = await r.Decide(r.Request with { ExpectedVersion = "11", Approve = false }); Assert.Equal("rejected", rejected.State);
        var terminal = await r.Preview(); Assert.False(terminal.Pending);
        Assert.DoesNotContain(terminal.Rules, x => x.RuleCode == OrganizationCompanyTaxIdChangeWorkInboxSourceV1.StaleRule);
        Assert.Equal("11", terminal.SourceVersion); Assert.Equal("7", terminal.OrganizationCompanyTaxId!.CompanyVersionAtRequest);
    }
    [Fact]
    public async Task Max_company_version_remains_lossless_and_reject_does_not_increment_it()
    {
        using var r = new Rig(); r.Native.CurrentCompanyVersion = long.MaxValue;
        var preview = await r.Preview(); Assert.False(preview.ApprovalSupported); Assert.True(preview.RejectionSupported);
        Assert.Equal(long.MaxValue.ToString(CultureInfo.InvariantCulture), preview.OrganizationCompanyTaxId!.CompanyVersion);
        var rejected = await r.Decide(r.Request with { Approve = false, ExpectedVersion = long.MaxValue.ToString(CultureInfo.InvariantCulture) });
        Assert.Equal("rejected", rejected.State); Assert.Equal(long.MaxValue, r.Native.CurrentCompanyVersion); Assert.Equal(1, r.Native.Effects);
    }
    [Fact]
    public async Task First_placeholder_VKN_keeps_null_old_value_and_first_rule_without_an_extra_permission()
    {
        using var r = new Rig(); r.Native.FirstVkn();
        var preview = await r.Preview(); Assert.Null(preview.OrganizationCompanyTaxId!.Request.CurrentVkn);
        Assert.Contains(preview.Rules, x => x.RuleCode == OrganizationCompanyTaxIdChangeWorkInboxSourceV1.FirstVknRule);
        Assert.Equal("approved", (await r.Decide()).State); Assert.Equal(1, r.Native.Effects);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Native_maximum_optional_note_remains_accepted_on_either_decision(bool approve)
    {
        using var r = new Rig(); var note = new string('x', 1000);
        Assert.Equal(approve ? "approved" : "rejected", (await r.Decide(r.Request with { Approve = approve, Reason = note })).State);
        Assert.Equal(note, r.Native.LastCommand!.Note); Assert.Equal(1, r.Native.Effects);
    }
    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData("01")] [InlineData("+7")] [InlineData("7\n")] [InlineData("7\r")] [InlineData(" 7")] [InlineData("\t7")]
    [InlineData("9223372036854775808")] [InlineData("9223372036854775807")]
    public async Task Invalid_or_nonincrementable_approve_version_is_rejected_before_source(string version)
    {
        using var r = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(r.Request with { ExpectedVersion = version }))).Status);
        Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Calls);
    }
    [Theory]
    [InlineData("control")] [InlineData("long")] [InlineData("cancellation")] [InlineData("mapping")] [InlineData("catalog")]
    [InlineData("normal")] [InlineData("period")] [InlineData("package")] [InlineData("manufacturing")] [InlineData("loss")]
    [InlineData("overage")] [InlineData("plan")] [InlineData("supplier")] [InlineData("return")]
    public async Task Native_note_bounds_and_all_foreign_selectors_fail_before_any_owner_call(string fault)
    {
        using var r = new Rig(); var q = fault switch
        {
            "control" => r.Request with { Reason = "bad\nreason" }, "long" => r.Request with { Reason = new('x', 1001) },
            "cancellation" => r.Request with { InventoryCostCancellation = new("1") }, "mapping" => r.Request with { PostingAccountMappingSelection = new("0") },
            "catalog" => r.Request with { ApprovedSourceCatalogSelection = new("0") }, "normal" => r.Request with { NormalJournalDraft = new(Id) },
            "period" => r.Request with { AccountingPeriodReopening = new("1") }, "package" => r.Request with { ProductPackageStandard = new("2026-11-28") },
            "manufacturing" => r.Request with { ManufacturingPlanReapproval = new([]) }, "loss" => r.Request with { InventoryTransferLoss = new(1, At) },
            "overage" => r.Request with { InventoryTransferOverage = new(1, At) }, "plan" => r.Request with { LogisticsPlanExcess = new("1") },
            "supplier" => r.Request with { SupplierReturnCostException = new() }, _ => r.Request with { LogisticsCustomerReturn = new(Id) },
        };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(q))).Status); Assert.Equal(0, r.Source.Reads); Assert.Equal(0, r.Native.Calls);
    }
    [Theory]
    [InlineData(CommandResult.TaxIdInUse, 422)] [InlineData(CommandResult.TaxIdChangeStale, 409)]
    [InlineData(CommandResult.TaxIdChangeNotPending, 409)] [InlineData(CommandResult.TaxIdSelfDecision, 409)]
    [InlineData(CommandResult.IdempotencyConflict, 409)] [InlineData(CommandResult.VersionConflict, 409)]
    public async Task Native_canonical_terminal_refusals_retain_status_and_do_not_request_success_proof(CommandResult result, int status)
    {
        using var r = new Rig(); r.Native.Refusal = result;
        var refusal = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide()); Assert.Equal(status, refusal.Status);
        Assert.NotNull(refusal.Code); Assert.Equal(0, r.Native.Effects); Assert.Equal(0, r.Native.ReceiptReads);
    }
    [Theory]
    [InlineData("company")] [InlineData("id")] [InlineData("current-version")] [InlineData("request-version")] [InlineData("maker")]
    [InlineData("time")] [InlineData("status")] [InlineData("current")] [InlineData("first")] [InlineData("proposed")]
    [InlineData("source")] [InlineData("preview")] [InlineData("missing")]
    public async Task Snapshot_scalar_tampering_cannot_release_human_facts(string fault)
    {
        using var r = new Rig(); r.Source.Change = s => s with { OrganizationCompanyTaxIdProof = fault switch
        {
            "company" => s.OrganizationCompanyTaxIdProof! with { CompanyId = Guid.NewGuid() }, "id" => s.OrganizationCompanyTaxIdProof! with { RequestId = Guid.NewGuid() },
            "current-version" => s.OrganizationCompanyTaxIdProof! with { CurrentCompanyVersion = 8 }, "request-version" => s.OrganizationCompanyTaxIdProof! with { CompanyVersionAtRequest = 8 },
            "maker" => s.OrganizationCompanyTaxIdProof! with { RequestedBy = "other" }, "time" => s.OrganizationCompanyTaxIdProof! with { RequestedAtUtc = At.AddTicks(1) },
            "status" => s.OrganizationCompanyTaxIdProof! with { Status = "approved" }, "current" => s.OrganizationCompanyTaxIdProof! with { IsCurrent = false },
            "first" => s.OrganizationCompanyTaxIdProof! with { FirstVkn = true }, "proposed" => s.OrganizationCompanyTaxIdProof! with { ProposedVkn = "9000000041" },
            "source" => s.OrganizationCompanyTaxIdProof! with { SourceFactsDigest = new('A',64) }, "preview" => s.OrganizationCompanyTaxIdProof! with { PreviewDigest = new('B',64) }, _ => null,
        } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status); Assert.Equal(0, r.Native.Effects);
    }
    [Theory]
    [InlineData("company")] [InlineData("id")] [InlineData("expected")] [InlineData("row-version")] [InlineData("command")]
    [InlineData("key")] [InlineData("payload")] [InlineData("actor")] [InlineData("maker")] [InlineData("time")]
    [InlineData("decision")] [InlineData("note")] [InlineData("correlation")] [InlineData("source")] [InlineData("result")]
    [InlineData("proposed")] [InlineData("first")] [InlineData("missing-proposed")] [InlineData("missing-first")] [InlineData("missing")]
    public async Task Bad_original_receipt_after_native_commit_is_unknown_and_same_key_reconciles_once(string fault)
    {
        using var r = new Rig(); r.Native.ReceiptChange = p => fault switch
        {
            "company" => p with { CompanyId = Guid.NewGuid() }, "id" => p with { RequestId = Guid.NewGuid() }, "expected" => p with { ExpectedCompanyVersion = 8 },
            "row-version" => p with { RequestVersion = 3 }, "command" => p with { CommandName = "other" }, "key" => p with { IdempotencyKeyHash = new('A',64) },
            "payload" => p with { PayloadHash = new('B',64) }, "actor" => p with { ActorId = Maker.ToString("D") }, "maker" => p with { RequestedBy = "other" },
            "time" => p with { RequestedAtUtc = At.AddTicks(1) }, "decision" => p with { Decision = "reject" }, "note" => p with { Note = "other" },
            "correlation" => p with { OriginalCorrelationId = "other" }, "source" => p with { SourceFactsDigest = new('C',64) }, "result" => p with { OriginalResultDigest = new('D',64) },
            "proposed" => p with { ProposedVkn = "9000000041" }, "first" => p with { FirstVkn = true }, "missing-proposed" => p with { ProposedVkn = null },
            "missing-first" => p with { FirstVkn = null }, _ => null,
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide())).Status); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        r.Native.ReceiptChange = null; Assert.True((await r.Decide()).Replayed); Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }
    [Theory]
    [InlineData("maker")] [InlineData("permission")] [InlineData("scope")] [InlineData("company")] [InlineData("membership")]
    public async Task Current_authority_and_actual_source_maker_denial_remain403_before_command(string fault)
    {
        using var r = new Rig();
        if (fault == "maker") r.User.Actor = Maker;
        if (fault == "permission") r.Authority.Allowed = false;
        if (fault == "scope") r.Authority.CompanyWide = false;
        if (fault == "company") r.Company.Set(Guid.NewGuid());
        if (fault == "membership") r.User.Member = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status); Assert.Equal(0, r.Native.Calls);
    }
    [Theory]
    [InlineData("permission", false)] [InlineData("delegation", false)] [InlineData("cancel", false)]
    [InlineData("actor", false)] [InlineData("company", false)] [InlineData("membership", false)]
    [InlineData("permission", true)] [InlineData("delegation", true)] [InlineData("cancel", true)]
    [InlineData("actor", true)] [InlineData("company", true)] [InlineData("membership", true)]
    public async Task Native_and_receipt_release_fences_distinguish_pre_effect_denial_from_committed_unknown(string fault, bool committed)
    {
        using var r = new Rig(); using var cancel = new CancellationTokenSource();
        Action revoke = () =>
        {
            if (fault == "permission") r.Authority.Allowed = false; if (fault == "delegation") r.Delegations.Live = false; if (fault == "cancel") cancel.Cancel();
            if (fault == "actor") r.User.Actor = Guid.NewGuid(); if (fault == "company") r.Company.Set(Guid.NewGuid()); if (fault == "membership") r.User.Member = false;
        };
        if (committed) r.Native.BeforeReceiptReturn = revoke; else r.Native.BeforeMutation = revoke;
        var request = r.Request with { DelegationId = Id };
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Decide(request, cancel.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide(request))).Status);
        Assert.Equal(committed ? 1 : 0, r.Native.Effects);
        if (committed && fault != "cancel")
        {
            r.Authority.Allowed = true; r.Delegations.Live = true; r.User.Actor = Actor; r.User.Member = true; r.Company.Set(Company); r.Native.BeforeReceiptReturn = null;
            Assert.True((await r.Decide(request)).Replayed); Assert.Equal(1, r.Native.Effects);
        }
    }
    [Fact]
    public async Task Changed_second_human_read_and_missing_narrow_owner_do_not_release_success()
    {
        using var r = new Rig(); r.Native.BeforeRead = n => { if (n == 2) r.Native.Reason = "changed"; };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Preview())).Status); Assert.Equal(0, r.Native.Effects);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.BuildHost(null).PreviewAsync(Kind, Id, default))).Status);
    }
    [Theory]
    [InlineData("status")] [InlineData("created")] [InlineData("correlation")] [InlineData("unknown")]
    public async Task Noncanonical_native_answer_is_unknown_after_effect_and_can_only_reconcile_original_key(string fault)
    {
        using var r = new Rig(); r.Native.AnswerChange = a => fault switch
        {
            "status" => a with { Response = a.Response with { StatusCode = 200 } },
            "created" => a with { Response = a.Response with { Created = new(Id, 1) } },
            "correlation" => a with { OriginalCorrelationId = "other" },
            _ => a with { Response = a.Response with { Result = (CommandResult)999 } },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide())).Status); Assert.Equal(1, r.Native.Effects);
        r.Native.AnswerChange = null; Assert.True((await r.Decide()).Replayed); Assert.Equal(1, r.Native.Effects);
    }
    [Theory]
    [InlineData("source-voucher")] [InlineData("inventory-cost")] [InlineData("inventory-cancellation")]
    public async Task Other_new_accounting_private_carriers_are_rejected_before_the_native_command(string fault)
    {
        using var r = new Rig(); r.Source.Change = s => fault switch
        {
            "source-voucher" => s with { SourceVoucherProof = JsonSerializer.Deserialize<WorkSourceVoucherProofV1>("{}", Json) },
            "inventory-cost" => s with { InventoryCostProof = JsonSerializer.Deserialize<WorkInventoryCostProofV1>("{}", Json) },
            _ => s with { InventoryCostCancellationProof = JsonSerializer.Deserialize<WorkInventoryCostCancellationProofV1>("{}", Json) },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Decide())).Status); Assert.Equal(0, r.Native.Calls);
    }
    [Fact]
    public async Task Company_tax_proof_is_rejected_on_another_kind_even_when_that_kind_has_its_own_permission()
    {
        using var r = new Rig(); var s = Assert.IsType<WorkApprovalDecisionSnapshotV1>(await r.Source.ReadDecisionAsync(new(Company, Actor.ToString("D"), Correlation, null), Id, default));
        r.Authority.AllowOtherPermissions = true; var source = new GenericSalesSource(s.OrganizationCompanyTaxIdProof!);
        var host = r.BuildHost(r.Native, source);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(WorkDecisionHostV1.SalesOrder, Id, default))).Status);
        source.IncludeProof = false; Assert.Equal(WorkDecisionHostV1.SalesOrder, (await host.PreviewAsync(WorkDecisionHostV1.SalesOrder, Id, default)).Kind);
        Assert.Equal(0, r.Native.Calls);
    }
    [Fact]
    public async Task Another_key_after_terminal_request_keeps_native409_instead_of_reapplying_the_change()
    {
        using var r = new Rig(); Assert.Equal("approved", (await r.Decide()).State);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(Kind, Id,
            r.Request with { ExpectedVersion = "8" }, "SENTETIK-another-key", default));
        Assert.Equal(409, error.Status); Assert.Equal(OrganizationProblemCodesV1.CompanyTaxIdChangeNotPending, error.Code);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
    }

    internal static OrganizationCompanyTaxIdDecisionDetailV1 Facts(long current = 7, long requested = 7, string status = "pending", string reason = "SENTETIK human reason")
    {
        var view = new CompanyTaxIdChangeView(Id, "9000000017", "9000000033", reason, status, Maker.ToString("D"), At,
            status == "pending" ? null : Actor.ToString("D"), status == "pending" ? null : At.AddMinutes(1), null);
        var d = new OrganizationCompanyTaxIdDecisionDetailV1(Company, current, "9000000017", view, requested, current == requested, "", "");
        return Rehash(d);
    }
    internal static OrganizationCompanyTaxIdDecisionDetailV1 Rehash(OrganizationCompanyTaxIdDecisionDetailV1 d) => d with
    { SourceFactsDigest = OrganizationCompanyTaxIdDecisionFactsV1.SourceFactsDigest(d.CompanyId, d.Request, d.CompanyVersionAtRequest), PreviewDigest = OrganizationCompanyTaxIdDecisionFactsV1.PreviewDigest(d) };
    private sealed class Rig : IDisposable
    {
        internal readonly MutableCompany Company = new(); internal readonly MutableUser User = new(); internal readonly Authority Authority = new(); internal readonly Delegations Delegations = new();
        internal readonly Native Native; internal readonly SourceProxy Source; internal readonly WorkDecisionHostV1 Host;
        private readonly CorrelationContext correlation = new(); private readonly WorkDecisionExecutionFenceV1 execution = new(); private readonly WorkDecisionAuthorityV1 scopedAuthority;
        private readonly OrganizationDbContext db;
        internal Rig()
        {
            Company.Set(InventoryCostWorkDecisionHostTests.Company); correlation.Set(Correlation, null, "Web", null, null); scopedAuthority = new(Authority, execution);
            Native = new(this, scopedAuthority);
            db = new(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("SENTETIK-tax-host-" + Guid.NewGuid()).Options, Company, new());
            Source = new(new OrganizationCompanyTaxIdChangeWorkInboxSourceV1(db, new(Company, User, scopedAuthority), Native)); Host = BuildHost(Native);
        }
        internal WorkDecisionRequestV1 Request => new("7", true, null);
        internal WorkDecisionHostV1 BuildHost(IOrganizationCompanyTaxIdService? native, IWorkInboxSourceV1? source = null) => new(Company, User, correlation, scopedAuthority, [source ?? Source], Delegations, execution, null!, null!, organizationCompanyTaxIds: native);
        internal Task<WorkDecisionPreviewV1> Preview() => Host.PreviewAsync(Kind, Id, default);
        internal Task<WorkDecisionResultV1> Decide(WorkDecisionRequestV1? request = null, CancellationToken token = default) => Host.DecideAsync(Kind, Id, request ?? Request, Key, token);
        public void Dispose() => db.Dispose();
    }
    private sealed record Saved(CommandExecutionResult Answer, OrganizationCompanyTaxIdDecisionReceiptV1 Receipt, string ResponseJson);
    private sealed class Native(Rig rig, IErpAuthorityRevalidationService authority) : IOrganizationCompanyTaxIdService, IOrganizationCompanyTaxIdDecisionReadV1
    {
        internal long CurrentCompanyVersion = 7; internal string? CurrentVkn = "9000000017"; internal string Reason = "SENTETIK human reason";
        internal int Reads, ReceiptReads, Calls, Effects; internal DecideCompanyTaxIdChange? LastCommand; internal string? LastKey;
        internal Action<int>? BeforeRead; internal Action? BeforeMutation, BeforeReceiptReturn; internal CommandResult? Refusal;
        internal Func<OrganizationCompanyTaxIdDecisionReceiptV1, OrganizationCompanyTaxIdDecisionReceiptV1?>? ReceiptChange;
        internal Func<CommandExecutionResult, CommandExecutionResult>? AnswerChange;
        internal readonly Dictionary<string, Saved> Commands = [];
        private CompanyTaxIdChangeView current = Facts().Request;
        internal void FirstVkn() { current = current with { CurrentVkn = null }; CurrentVkn = null; }
        public async Task<OrganizationCompanyTaxIdDecisionReadResultV1> ReadDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            await fence(token); Reads++; BeforeRead?.Invoke(Reads); await fence(token);
            if (id != Id) return new(OrganizationCompanyTaxIdDecisionReadStatusV1.NotFound, null);
            if (rig.User.Actor == Maker) return new(OrganizationCompanyTaxIdDecisionReadStatusV1.Denied, null);
            var d = new OrganizationCompanyTaxIdDecisionDetailV1(InventoryCostWorkDecisionHostTests.Company, CurrentCompanyVersion, CurrentVkn,
                current with { Reason = Reason }, 7, CurrentCompanyVersion == 7 && CurrentVkn == current.CurrentVkn, "", "");
            return new(OrganizationCompanyTaxIdDecisionReadStatusV1.Found, Rehash(d));
        }
        public async Task<CommandExecutionResult> DecideCompanyTaxIdChangeAsync(Guid id, DecideCompanyTaxIdChange command, CommandMetadata metadata, CancellationToken token)
        {
            Calls++; LastCommand = command; LastKey = metadata.IdempotencyKey;
            BeforeMutation?.Invoke(); if (!await Fence(metadata, token)) return new(CommandResponses.For(CommandResult.NotFound), false, metadata.CorrelationId);
            var hash = OrganizationCompanyTaxIdDecisionBindingV1.DecisionPayloadHash(metadata.CompanyId, id, command);
            if (Commands.TryGetValue(metadata.IdempotencyKey, out var old))
            {
                var replay = old.Answer with { IsReplay = true };
                return old.Receipt.PayloadHash == hash && old.Receipt.ActorId == metadata.ActorId ? AnswerChange?.Invoke(replay) ?? replay
                    : new(CommandResponses.For(CommandResult.IdempotencyConflict), false, metadata.CorrelationId);
            }
            var refusal = Refusal ?? (current.Status != "pending" ? CommandResult.TaxIdChangeNotPending
                : command.ExpectedVersion != CurrentCompanyVersion ? CommandResult.VersionConflict
                : command.Decision == "approve" && CurrentCompanyVersion != 7 ? CommandResult.TaxIdChangeStale : (CommandResult?)null);
            if (refusal is { } failed) return new(failed == CommandResult.VersionConflict ? CommandResponses.VersionConflict(command.ExpectedVersion, CurrentCompanyVersion + 1) : CommandResponses.For(failed), false, metadata.CorrelationId);
            var source = Rehash(new(InventoryCostWorkDecisionHostTests.Company, CurrentCompanyVersion, CurrentVkn, current with { Reason = Reason }, 7, CurrentCompanyVersion == 7, "", ""));
            var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
            current = source.Request with { Status = command.Decision == "approve" ? "approved" : "rejected", DecidedBy = metadata.ActorId, DecidedAtUtc = At.AddMinutes(1), DecisionNote = note };
            if (command.Decision == "approve") { CurrentCompanyVersion++; CurrentVkn = current.ProposedVkn; }
            Effects++;
            var response = CommandResponses.For(CommandResult.Succeeded); var answer = new CommandExecutionResult(response, false, metadata.CorrelationId);
            var receipt = new OrganizationCompanyTaxIdDecisionReceiptV1(metadata.CompanyId, id, 7, command.ExpectedVersion, 2, command.Decision, note,
                current.RequestedBy, current.RequestedAtUtc, metadata.ActorId, current.DecidedAtUtc!.Value, OrganizationCompanyTaxIdDecisionBindingV1.DecisionCommand,
                OrganizationCompanyTaxIdDecisionBindingV1.DecisionKeyHash(metadata.IdempotencyKey), hash, metadata.ActorId, metadata.CorrelationId,
                At.AddMinutes(1), source.SourceFactsDigest, OrganizationCompanyTaxIdDecisionBindingV1.OriginalResultDigest(response))
                { ProposedVkn = current.ProposedVkn, FirstVkn = current.CurrentVkn is null };
            Commands.Add(metadata.IdempotencyKey, new(answer, receipt, JsonSerializer.Serialize(response))); return AnswerChange?.Invoke(answer) ?? answer;
        }
        public async Task<OrganizationCompanyTaxIdDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid id, DecideCompanyTaxIdChange command, string key,
            CommandExecutionResult original, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            ReceiptReads++; await fence(token);
            if (!Commands.TryGetValue(key, out var old) || old.Receipt.RequestId != id || old.Receipt.PayloadHash != OrganizationCompanyTaxIdDecisionBindingV1.DecisionPayloadHash(InventoryCostWorkDecisionHostTests.Company, id, command)
                || old.ResponseJson != JsonSerializer.Serialize(original.Response) || old.Answer.OriginalCorrelationId != original.OriginalCorrelationId) return null;
            var receipt = ReceiptChange?.Invoke(old.Receipt) ?? (ReceiptChange is null ? old.Receipt : null);
            BeforeReceiptReturn?.Invoke(); await fence(token); return receipt;
        }
        private async Task<bool> Fence(CommandMetadata metadata, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var scope = await authority.RevalidatePermissionScopeAsync(OrganizationCompanyTaxIdPermissionsV1.Approve, metadata.CompanyId, token); token.ThrowIfCancellationRequested();
            return scope.IsGranted && scope.IsCompanyWide && rig.Company.CompanyId == metadata.CompanyId && rig.User.UserId == metadata.ActorId && rig.User.Member;
        }
        public Task<CompanyTaxIdView?> GetCompanyTaxIdAsync(CancellationToken token) => throw new InvalidOperationException("No general tax list");
        public Task<CommandExecutionResult> RequestCompanyTaxIdChangeAsync(RequestCompanyTaxIdChange command, CommandMetadata metadata, CancellationToken token) => throw new InvalidOperationException("No request");
        public Task<CompanyTaxIdCorrectionsView?> GetCompanyTaxIdCorrectionsAsync(CancellationToken token) => throw new InvalidOperationException("No corrections");
        public Task<CommandExecutionResult> ApplyCompanyTaxIdCorrectionAsync(Guid id, ApplyCompanyTaxIdCorrection command, CommandMetadata metadata, CancellationToken token) => throw new InvalidOperationException("No correction mutation");
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        internal int Reads; internal Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int max, CancellationToken t) => inner.ListPendingAsync(c, max, t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t)
        { Reads++; var s = await inner.ReadDecisionAsync(c, id, t); return s is null ? null : Change?.Invoke(s) ?? s; }
    }
    private sealed class MutableCompany : ICompanyContext { public Guid CompanyId { get; private set; } public bool IsResolved => CompanyId != Guid.Empty; public void Set(Guid id) => CompanyId = id; }
    private sealed class GenericSalesSource(WorkOrganizationCompanyTaxIdProofV1 proof) : IWorkApprovalDecisionSourceV1
    {
        internal bool IncludeProof = true; public string Module => "sales"; public string Kind => WorkDecisionHostV1.SalesOrder; public string ApproverPermission => ErpPermissions.SalesOrdersApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int max, CancellationToken t) => throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(
            new(Company, new(Module, Kind, id, "SENTETIK sales", At, "/satis/siparisler", Maker.ToString("D"), [], []), "1", true)
            { OrganizationCompanyTaxIdProof = IncludeProof ? proof : null });
    }
    private sealed class MutableUser : ICurrentUser { internal Guid Actor = InventoryCostWorkDecisionHostTests.Actor; internal bool Member = true; public bool IsAuthenticated => true; public string UserId => Actor.ToString("D"); public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]); public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        internal bool Allowed = true, CompanyWide = true, AllowOtherPermissions; internal List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string p, Guid company, CancellationToken token = default)
        { Permissions.Add(p); return Task.FromResult(new PermissionScope(Allowed && (AllowOtherPermissions || p == OrganizationCompanyTaxIdPermissionsV1.Approve), CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        internal bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, Kind, OrganizationCompanyTaxIdPermissionsV1.Approve, Maker, Actor, At.AddYears(-1), At.AddYears(1), "1", Maker, At, "SENTETIK", null, null, null) : null);
    }
}
