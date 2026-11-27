using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Controlled native-owner replies: Work consumer boundaries only; relational owner acceptance is separate.
public sealed class WorkExpensePaymentDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("ad000000-0000-4000-8000-000000000001"), Id = Guid.Parse("ad000000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Maker = "SENTETIK-maker", Editor = "SENTETIK-editor", Key = "expense-work-key", Correlation = "SENTETIK-work-correlation";
    private static ExpensePaymentDecisionDetailV1 Human(string version)
    {
        var payment = new ExpensePaymentView(Id, Company, ExpensePaymentStatus.Draft,
            new(ExpensePaymentSourceKind.Bank, Guid.NewGuid(), SingleExpenseItemCatalog.GeneralExpenseId, new("001250.50", Guid.NewGuid()),
                new(Guid.NewGuid().ToString("D"), "1"), new(2026, 11, 26), null, "SENTETIK reference", "SENTETIK private description"),
            Maker, At, null, null, null, null, false, version);
        return new(payment, "SENTETIK bank label", [Editor, Maker], ExpensePaymentService.DraftFactsDigest(payment));
    }
    private static ExpensePaymentCommandResult Answer(ExpensePaymentDecisionDetailV1 detail, string expected, bool replay)
    {
        var version = (long.Parse(expected, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
        var payment = detail.Payment with { Status = ExpensePaymentStatus.Approved, Version = version, ApprovedBy = Actor,
            ApprovedAtUtc = At.AddMinutes(-1), RecordedBy = null, RecordedAtUtc = null, PaymentConfirmed = false, DocumentNumber = null };
        var original = replay ? "SENTETIK-original-correlation" : Correlation;
        var result = new ExpensePaymentCommandResult(CommandOutcome.Succeeded, payment, replay, original, []);
        return result with { DecisionReceipt = new(Company, Id, ExpensePaymentService.ApproveCommand, IdempotencyHash.ComputeKey(Key),
            ExpensePaymentService.ApprovePayloadHash(new(Id, expected)), Actor, expected, version, original, At.AddMinutes(-1),
            detail.DraftFactsDigest, ExpensePaymentService.OriginalReceiptDigest(result)) };
    }

    [Theory]
    [InlineData("1")] [InlineData("4")] [InlineData("9223372036854775806")]
    public async Task Expense_approve_uses_native_N_to_N_plus_one_without_reason_reject_record_or_extra_human_POST_read(string expected)
    {
        using var run = new Rig(expected); var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.ExpensePayment, Id, default);
        Assert.Equal(run.Owner.Header, preview.ExpensePayment); Assert.Equal(expected, preview.SourceVersion);
        Assert.False(preview.RejectionSupported); Assert.False(preview.ApprovalReasonRequired);
        var reads = run.Owner.Reads; var result = await run.Decide();
        Assert.Equal("Approved", result.State); Assert.False(result.Replayed); Assert.Equal(Correlation, result.OriginalCorrelationId);
        Assert.Equal(reads + 1, run.Owner.Reads); Assert.Equal((Id, expected, Key, 1, 0), (run.Owner.LastId, run.Owner.Version, run.Owner.Key, run.Owner.Approvals, run.Owner.Records));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Original_key_replay_keeps_original_approved_result_when_current_source_is_approved_or_later_recorded(bool recorded)
    {
        using var run = new Rig("4", replay: true, recorded: recorded); run.Owner.HumanChange = _ => throw new InvalidOperationException("POST must not make a second human read");
        var result = await run.Decide(); Assert.True(result.Replayed); Assert.Equal("Approved", result.State);
        Assert.Equal("SENTETIK-original-correlation", result.OriginalCorrelationId);
        Assert.Equal(recorded ? "6" : "5", run.Owner.Header.Payment.Version); Assert.Equal("4", run.Owner.Version);
        Assert.Equal("5", run.Owner.Result.Resource!.Version); Assert.False(run.Owner.Result.Resource.PaymentConfirmed); Assert.Null(run.Owner.Result.Resource.DocumentNumber);
        Assert.Equal(1, run.Owner.Reads); Assert.Equal(1, run.Owner.Approvals); Assert.Equal(0, run.Owner.Records);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("payment")] [InlineData("command")] [InlineData("key")]
    [InlineData("hash")] [InlineData("actor")] [InlineData("expected")] [InlineData("version")] [InlineData("correlation")]
    [InlineData("time")] [InlineData("facts")] [InlineData("original-digest")] [InlineData("current-substitute")] [InlineData("maker")] [InlineData("fresh-terminal")]
    public async Task Original_native_receipt_is_required_and_current_record_or_misbound_provenance_never_becomes_Work_success(string fault)
    {
        using var run = new Rig(replay: fault is "fresh-terminal" or "current-substitute", recorded: fault is "fresh-terminal" or "current-substitute");
        var result = run.Owner.Result; var receipt = result.DecisionReceipt!;
        receipt = fault switch { "company" => receipt with { CompanyId = Guid.NewGuid() }, "payment" => receipt with { PaymentId = Guid.NewGuid() },
            "command" => receipt with { CommandName = ExpensePaymentService.RecordCommand }, "key" => receipt with { IdempotencyKeyHash = "wrong" },
            "hash" => receipt with { PayloadHash = "wrong" }, "actor" => receipt with { ActorId = Maker }, "expected" => receipt with { ExpectedVersion = "3" },
            "version" => receipt with { ResultVersion = "6" }, "correlation" => receipt with { OriginalCorrelationId = "wrong" },
            "time" => receipt with { RecordedAtUtc = At }, "facts" => receipt with { DraftFactsDigest = new string('b', 64) },
            "original-digest" => receipt with { OriginalReceiptDigest = new string('b', 64) }, _ => receipt };
        run.Owner.Result = result with { DecisionReceipt = fault == "missing" ? null : receipt,
            Resource = fault == "current-substitute" ? run.Owner.Header.Payment : fault == "maker" ? result.Resource! with { PreparedBy = Actor } : result.Resource,
            IsReplay = fault == "fresh-terminal" ? false : result.IsReplay };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Records);
    }

    [Theory]
    [InlineData("company")] [InlineData("payment")] [InlineData("version")] [InlineData("amount")] [InlineData("description")]
    [InlineData("profile")] [InlineData("status")] [InlineData("label")] [InlineData("revisers")] [InlineData("digest")]
    public async Task Human_GET_requires_full_same_native_preparation_reviser_set_label_and_preview_digest(string fault)
    {
        using var run = new Rig(); run.Owner.HumanChange = h => fault switch
        {
            "company" => h with { Payment = h.Payment with { CompanyId = Guid.NewGuid() } }, "payment" => h with { Payment = h.Payment with { Id = Guid.NewGuid() } },
            "version" => h with { Payment = h.Payment with { Version = "5" } }, "amount" => h with { Payment = h.Payment with { Preparation = h.Payment.Preparation with { Money = new("999", h.Payment.Preparation.Money.CurrencyId) } } },
            "description" => h with { Payment = h.Payment with { Preparation = h.Payment.Preparation with { Description = "other" } } },
            "profile" => h with { Payment = h.Payment with { Preparation = h.Payment.Preparation with { Profile = new("other", "1") } } },
            "status" => h with { Payment = h.Payment with { Status = ExpensePaymentStatus.Approved } }, "label" => h with { TreasuryAccountLabel = "other" },
            "revisers" => h with { Revisers = [Maker] }, _ => h with { DraftFactsDigest = new string('b', 64) },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.ExpensePayment, Id, default))).Status);
        Assert.Equal(0, run.Owner.Approvals); Assert.Equal(0, run.Owner.Records);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("facts")] [InlineData("preview")] [InlineData("cross-kind")] [InlineData("reference")] [InlineData("rule")]
    public async Task Snapshot_scalar_and_owner_reference_rule_proofs_are_kind_bound(string fault)
    {
        using var run = new Rig(); run.Source.Change = s =>
        {
            var p = s.ExpensePaymentProof!;
            return s with { ExpensePaymentProof = fault switch { "missing" => null, "company" => p with { CompanyId = Guid.NewGuid() },
                "facts" => p with { DraftFactsDigest = "wrong" }, "preview" => p with { PreviewDigest = "wrong" }, _ => p },
                ManualReceiptReversalProof = fault == "cross-kind" ? new(Company, Id, Guid.NewGuid(), Maker, At, "Pending", new string('a', 64), new string('b', 64)) : null,
                Item = s.Item with { References = fault == "reference" ? s.Item.References.Select(x => x.Role == "currency" ? x with { RecordId = Guid.Empty } : x).ToArray() : s.Item.References,
                    Rules = fault == "rule" ? [s.Item.Rules[0] with { Threshold = "1" }] : s.Item.Rules } };
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(0, run.Owner.Approvals);
    }

    [Theory]
    [InlineData("caption")] [InlineData("actual")] [InlineData("reference")] [InlineData("excluded")]
    public async Task Human_GET_rejects_otherwise_valid_but_owner_inconsistent_item_fields(string fault)
    {
        using var run = new Rig(); run.Source.Change = s => s with { Item = s.Item with
        {
            DocumentLabel = fault == "caption" ? "SENTETIK different label" : s.Item.DocumentLabel,
            Rules = fault == "actual" ? [s.Item.Rules[0] with { Actual = "999" }] : s.Item.Rules,
            References = fault == "reference" ? s.Item.References.Select(x => x.Role == "currency" ? x with { RecordId = Guid.NewGuid() } : x).ToArray() : s.Item.References,
            ExcludedDeciders = fault == "excluded" ? [] : s.Item.ExcludedDeciders,
        } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.ExpensePayment, Id, default))).Status);
        Assert.Equal(0, run.Owner.Approvals); Assert.Equal(0, run.Owner.Records);
    }

    [Fact]
    public async Task Another_registered_kind_cannot_carry_the_expense_private_proof()
    {
        using var run = new Rig(); var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, Correlation, null), Id, default);
        Assert.NotNull(snapshot); const string kind = "finance.cash-account.v1";
        var source = new OtherKindSource(snapshot with { Item = snapshot.Item with { Kind = kind } });
        run.Authority.Permission = ErpPermissions.FinanceCashAccountApprove;
        var user = new WorkInventoryTransferLossDecisionHostTests.LossUser(Company, Actor);
        var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null); var fence = new WorkDecisionExecutionFenceV1();
        var host = new WorkDecisionHostV1(run.Company, user, correlation, new WorkDecisionAuthorityV1(run.Authority, fence), [source], run.Delegations, fence, null!, null!);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync(kind, Id, default))).Status);
        Assert.Equal(0, run.Owner.Approvals); Assert.Equal(0, run.Owner.Records);
    }

    [Theory]
    [InlineData(false, null, "4")] [InlineData(true, "note", "4")] [InlineData(true, null, "0")]
    [InlineData(true, null, "04")] [InlineData(true, null, "4\n")] [InlineData(true, null, "9223372036854775807")]
    public async Task Expense_request_is_approve_only_null_reason_and_canonical_incrementable_native_Int64(bool approve, string? reason, string version)
    {
        using var run = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.ExpensePayment, Id, new(version, approve, reason), Key, default))).Status);
        Assert.Equal(0, run.Owner.Approvals); Assert.Equal(0, run.Owner.Records);
    }

    [Theory]
    [InlineData("loss")] [InlineData("overage")] [InlineData("manufacturing")] [InlineData("package")]
    public async Task Other_kind_selection_payload_cannot_enter_the_expense_native_approve_command(string selection)
    {
        using var run = new Rig(); var request = new WorkDecisionRequestV1("4", true, null);
        request = selection switch { "loss" => request with { InventoryTransferLoss = new(1, At) }, "overage" => request with { InventoryTransferOverage = new(1, At) },
            "package" => request with { ProductPackageStandard = new("2026-11-27") }, _ => request with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) } };
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.ExpensePayment, Id, request, Key, default))).Status);
        Assert.Equal(0, run.Owner.Approvals); Assert.Equal(0, run.Owner.Records);
    }

    [Theory]
    [InlineData(CommandOutcome.Denied, 403)] [InlineData(CommandOutcome.Unavailable, 503)] [InlineData(CommandOutcome.VersionConflict, 409)]
    [InlineData(CommandOutcome.IdempotencyConflict, 409)] [InlineData(CommandOutcome.ValidationFailed, 422)]
    public async Task Native_refusal_keeps_original_status_correlation_and_key_receipt(CommandOutcome outcome, int status)
    {
        using var run = new Rig(); run.Owner.Result = new(outcome, null, true, "SENTETIK-native-original", [new("request", "SENTETIK-code")]);
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide()); Assert.Equal(status, error.Status);
        Assert.True(error.Replayed); Assert.Equal("SENTETIK-native-original", error.OriginalCorrelationId); Assert.Equal("SENTETIK-code", error.Code);
    }

    [Theory]
    [InlineData(Maker)] [InlineData(Editor)]
    public async Task Maker_and_reviser_never_reach_an_approval_or_payment_record(string actor)
    {
        using var run = new Rig(actor: actor); Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status);
        Assert.Equal(0, run.Owner.Approvals); Assert.Equal(0, run.Owner.Records);
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")]
    public async Task Fresh_permission_and_server_company_loss_after_native_await_suppress_the_response(string fault)
    {
        using var run = new Rig(); run.Owner.BeforeReturn = () => { if (fault == "permission") run.Authority.Allowed = false; else run.Company.Set(Guid.NewGuid()); };
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); Assert.Equal(1, run.Owner.Approvals); Assert.Equal(0, run.Owner.Records);
    }

    [Fact]
    public async Task Companywide_and_live_native_delegation_fence_remain_required_for_original_key_replay()
    {
        using var run = new Rig(replay: true, recorded: true); run.Authority.CompanyWide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide())).Status); run.Authority.CompanyWide = true;
        run.Delegations.Live = false; Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide(Guid.NewGuid()))).Status);
        Assert.Equal(0, run.Owner.Approvals); run.Delegations.Live = true; run.Owner.BeforeReturn = () => run.Delegations.Live = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Decide(Guid.NewGuid()))).Status); Assert.Equal(0, run.Owner.Records);
    }

    [Fact]
    public async Task Human_preparation_and_private_receipt_are_GET_only_not_in_snapshot_command_result_or_other_kind_wire()
    {
        using var run = new Rig(); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = await run.Source.ReadDecisionAsync(new(Company, Actor, Correlation, null), Id, default);
        var wire = JsonSerializer.Serialize(snapshot, options); Assert.DoesNotContain("expensePaymentProof", wire); Assert.DoesNotContain("SENTETIK private description", wire); Assert.DoesNotContain("profileId", wire);
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.ExpensePayment, Id, default); Assert.Contains("SENTETIK private description", JsonSerializer.Serialize(preview, options));
        Assert.DoesNotContain("expensePayment", JsonSerializer.Serialize(new WorkDecisionPreviewV1(Company, WorkDecisionHostV1.BankAccount, Id, "SENTETIK", "1", true, false, [], []), options));
        using var native = JsonDocument.Parse(JsonSerializer.Serialize(run.Owner.Result, FinanceJson.CreateOptions())); Assert.Equal(5, native.RootElement.EnumerateObject().Count()); Assert.False(native.RootElement.TryGetProperty("decisionReceipt", out _));
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new WorkDecisionRequestV1("4", true, null), options)); Assert.Equal(4, body.RootElement.EnumerateObject().Count());
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(await run.Decide(), options)); Assert.Equal(6, result.RootElement.EnumerateObject().Count()); Assert.Equal(0, run.Owner.Records);
    }

    private sealed class Rig : IDisposable
    {
        public Owner Owner { get; } public SourceProxy Source { get; } public Authority Authority { get; } = new(); public Delegations Delegations { get; } = new();
        public WorkDecisionHostV1 Host { get; } public MutableCompany Company { get; } = new(); private readonly FinanceDbContext db; private readonly string expected;
        public Rig(string version = "4", bool replay = false, bool recorded = false, string actor = Actor)
        {
            expected = version; Owner = new() { Header = Human(version) }; Owner.Result = Answer(Owner.Header, version, replay);
            if (replay)
            {
                var payment = Owner.Result.Resource!;
                if (recorded) payment = payment with { Status = ExpensePaymentStatus.Recorded, Version = (long.Parse(payment.Version, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture),
                    RecordedBy = "SENTETIK-recorder", RecordedAtUtc = At.AddHours(1), PaymentConfirmed = true, DocumentNumber = new("SENTETIK-current-record-number", "1") };
                Owner.Header = Owner.Header with { Payment = payment };
            }
            Company.Set(WorkExpensePaymentDecisionHostTests.Company); var user = new WorkInventoryTransferLossDecisionHostTests.LossUser(WorkExpensePaymentDecisionHostTests.Company, actor);
            db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase("expense-work-" + Guid.NewGuid()).Options, Company);
            Source = new(new FinanceExpensePaymentWorkInboxSourceV1(db, new(Company, user, Authority), Owner));
            var correlation = new CorrelationContext(); correlation.Set(Correlation, null, "Web", null, null); var fence = new WorkDecisionExecutionFenceV1();
            Host = new(Company, user, correlation, new WorkDecisionAuthorityV1(Authority, fence), [Source], Delegations, fence, null!, null!, expensePayments: Owner);
            Owner.CommitFence = ct => fence.VerifyAsync(ct);
        }
        public Task<WorkDecisionResultV1> Decide(Guid? delegation = null) => Host.DecideAsync(WorkDecisionHostV1.ExpensePayment, Id, new(expected, true, null, delegation), Key, default);
        public void Dispose() => db.Dispose();
    }
    private sealed class SourceProxy(IWorkApprovalDecisionSourceV1 inner) : IWorkApprovalDecisionSourceV1
    {
        public Func<WorkApprovalDecisionSnapshotV1, WorkApprovalDecisionSnapshotV1>? Change;
        public string Module => inner.Module; public string Kind => inner.Kind; public string ApproverPermission => inner.ApproverPermission;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken t) => inner.ListPendingAsync(c, n, t);
        public async Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t)
        { var answer = await inner.ReadDecisionAsync(c, id, t); return answer is null || Change is null ? answer : Change(answer); }
    }
    private sealed class OtherKindSource(WorkApprovalDecisionSnapshotV1 snapshot) : IWorkApprovalDecisionSourceV1
    {
        public string Module => "finance"; public string Kind => "finance.cash-account.v1"; public string ApproverPermission => ErpPermissions.FinanceCashAccountApprove;
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 c, int n, CancellationToken t) => throw new NotSupportedException();
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 c, Guid id, CancellationToken t) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot);
    }

    private sealed class Owner : IExpensePaymentService, IExpensePaymentDecisionReadV1
    {
        public ExpensePaymentDecisionDetailV1 Header = null!; public ExpensePaymentCommandResult Result = null!;
        public int Reads, Approvals, Records; public Guid LastId; public string? Version, Key;
        public Action? BeforeReturn; public Func<ExpensePaymentDecisionDetailV1, ExpensePaymentDecisionDetailV1>? HumanChange; public Func<CancellationToken, Task<bool>>? CommitFence;
        public async Task<ReadResult<ExpensePaymentDecisionDetailV1>> ReadDecisionAsync(FinanceContext c, Guid id, Func<CancellationToken, Task> fence, CancellationToken t)
        { await fence(t); Reads++; var result = Reads > 1 && HumanChange is not null ? HumanChange(Header) : Header; await fence(t); return new(ReadOutcome.Found, result); }
        public async Task<ExpensePaymentCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, ApproveExpensePaymentRequest r, CancellationToken t)
        { Approvals++; LastId = r.PaymentId; Version = r.ExpectedVersion; Key = m.IdempotencyKey; BeforeReturn?.Invoke(); if (CommitFence is not null && !await CommitFence(t)) return new(CommandOutcome.Denied, null, false, Correlation, []); return Result; }
        public Task<ExpensePaymentCommandResult> RecordAsync(FinanceContext c, CommandMetadata m, RecordExpensePaymentRequest r, CancellationToken t) { Records++; throw new NotSupportedException(); }
        public Task<ExpensePaymentCommandResult> DraftSaveAsync(FinanceContext c, CommandMetadata m, ExpensePaymentDraftSaveRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<ExpensePaymentPage>> ListAuthorizedAsync(FinanceContext c, string? cursor, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<ExpensePaymentView>> DetailAuthorizedAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<ExpenseItemCatalogView>> ExpenseItemsAuthorizedAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<ExpensePaymentDraftContextView>> DraftContextAuthorizedAsync(FinanceContext c, ExpensePaymentSourceKind k, Guid id, DateOnly d, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; } public void Set(Guid id) => CompanyId = id; }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true; public string Permission = FinancePermissionNames.ExpensePaymentApprove;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken t = default)
        { Assert.Equal(Permission, permission); return Task.FromResult(new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Delegations : IWorkDelegationReaderV1
    {
        public bool Live = true;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken t = default) => Task.FromResult<WorkDelegationViewV1?>(Live
            ? new(id, Company, item.Kind, FinancePermissionNames.ExpensePaymentApprove, Guid.NewGuid(), Guid.NewGuid(), At, DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), At, "SENTETIK", null, null, null) : null);
    }
}
