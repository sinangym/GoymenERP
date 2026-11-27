using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class ExpensePaymentServiceTests
{
    private static readonly DateTimeOffset DecisionAt = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("1")] [InlineData("4")] [InlineData("9223372036854775806")]
    public void Expense_purpose_receipt_binds_actual_native_version_increment_and_keeps_five_field_wire(string expected)
    {
        var (row, result, context, metadata, request) = DecisionReceiptFixture(expected);
        var proof = ExpensePaymentService.RestoreDecisionReceipt(row, context, metadata, request, result);
        Assert.NotNull(proof); Assert.Equal(expected, proof.ExpectedVersion); Assert.Equal(result.Resource!.Version, proof.ResultVersion);
        Assert.Equal(row.PayloadHash, proof.PayloadHash); Assert.Equal(ExpensePaymentService.OriginalReceiptDigest(result), proof.OriginalReceiptDigest);
        var wire = JsonSerializer.Serialize(result with { DecisionReceipt = proof }, FinanceJson.CreateOptions()); Assert.Equal(row.ResultJson, wire);
        using var json = JsonDocument.Parse(wire); Assert.Equal(5, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("decisionReceipt", out _));
        Assert.Equal(proof, ExpensePaymentService.RestoreDecisionReceipt(row, context with { CorrelationId = "later" }, metadata, request, result with { IsReplay = true }));
        // Native timestamps are identities, not a new clock chronology policy.
        Assert.True(result.Resource.PreparedAtUtc > proof.RecordedAtUtc);
    }

    [Theory]
    [InlineData("company")] [InlineData("payment")] [InlineData("command")] [InlineData("actor")]
    [InlineData("key")] [InlineData("hash")] [InlineData("correlation")] [InlineData("time")]
    [InlineData("version")] [InlineData("resource")] [InlineData("missing")] [InlineData("extra")]
    [InlineData("recorded-substitute")] [InlineData("maker")]
    public void Expense_purpose_never_forges_an_original_receipt_from_missing_misbound_or_later_payment_data(string fault)
    {
        var (row, result, context, metadata, request) = DecisionReceiptFixture("4");
        switch (fault)
        {
            case "company": row.CompanyId = Guid.NewGuid(); break;
            case "payment": row.PaymentId = Guid.NewGuid(); break;
            case "command": row.CommandName = ExpensePaymentService.RecordCommand; break;
            case "actor": row.ActorId = "another"; break;
            case "key": row.IdempotencyKeyHash = "wrong"; break;
            case "hash": row.PayloadHash = "wrong"; break;
            case "correlation": row.CorrelationId = "wrong"; break;
            case "time": row.RecordedAtUtc = DecisionAt.AddSeconds(1); break;
            case "version": result = result with { Resource = result.Resource! with { Version = "6" } }; row.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "resource": result = result with { Resource = result.Resource! with { Id = Guid.NewGuid() } }; break;
            case "missing": result = result with { Resource = null }; row.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "extra": row.ResultJson = row.ResultJson[..^1] + ",\"unknown\":true}"; break;
            case "maker": result = result with { Resource = result.Resource! with { PreparedBy = context.ActorId } }; row.ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()); break;
            case "recorded-substitute": result = result with { Resource = result.Resource! with { Status = ExpensePaymentStatus.Recorded, Version = "6", RecordedBy = "recorder", RecordedAtUtc = DecisionAt, PaymentConfirmed = true, DocumentNumber = new("SENTETIK-number", "1") } }; break;
        }
        Assert.Null(ExpensePaymentService.RestoreDecisionReceipt(row, context, metadata, request, result));
    }

    [Fact]
    public void Expense_draft_facts_exclude_later_record_metadata_and_dynamic_treasury_labels_but_bind_full_preparation()
    {
        var (_, result, _, _, _) = DecisionReceiptFixture("4"); var approved = result.Resource!;
        var recorded = approved with { Status = ExpensePaymentStatus.Recorded, Version = "6", PaymentConfirmed = true,
            RecordedBy = "recorder", RecordedAtUtc = DecisionAt.AddHours(1), DocumentNumber = new("SENTETIK-number", "1") };
        Assert.Equal(ExpensePaymentService.DraftFactsDigest(approved), ExpensePaymentService.DraftFactsDigest(recorded));
        Assert.NotEqual(ExpensePaymentService.OriginalReceiptDigest(result), ExpensePaymentService.OriginalReceiptDigest(result with { Resource = recorded }));
        var first = new ExpensePaymentDecisionDetailV1(approved, "first bank", ["maker"], ExpensePaymentService.DraftFactsDigest(approved));
        Assert.NotEqual(ExpensePaymentService.DecisionPreviewDigest(first), ExpensePaymentService.DecisionPreviewDigest(first with { TreasuryAccountLabel = "other bank" }));
        Assert.NotEqual(first.DraftFactsDigest, ExpensePaymentService.DraftFactsDigest(approved with { Preparation = approved.Preparation with { Description = "changed" } }));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Expense_approval_only_preview_and_source_reuse_native_reviser_set_and_formatter_without_general_read(bool cash)
    {
        await using var run = await Run.StartAsync(); var id = await DecisionDraft(run, cash);
        await using (var editor = run.Scope("editor", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
            Assert.Equal("2", (await editor.Service.DraftSaveAsync(editor.Context, Key("purpose-revision"), (cash ? CashDraft() : Draft()) with { PaymentId = id, ExpectedVersion = "1" }, default)).Resource!.Version);
        await using var db = run.Context(); var company = new ApprovalCompany(Company); var user = new ApprovalUser(); var permission = new DecisionReadPermission();
        var core = ApprovalCore(db, company, user, permission, null); var context = new FinanceContext(Company, "checker", "purpose-context", "purpose-cause"); var calls = 0;
        var result = await core.ReadDecisionAsync(context, id, _ => { calls++; return Task.CompletedTask; }, default);
        Assert.Equal(ReadOutcome.Found, result.Outcome); Assert.Equal(10, calls); var detail = result.Resource!;
        Assert.Equal("2", detail.Payment.Version); Assert.Equal(cash ? "SENTETIK kasa" : "SENTETIK banka", detail.TreasuryAccountLabel); Assert.Equal(new[] { "editor", "maker" }, detail.Revisers);
        var authority = new FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority(true, true);
        var source = new FinanceExpensePaymentWorkInboxSourceV1(db, new(company, user, authority), core.WithApprovalSafety());
        var sourceContext = new WorkInboxSourceContextV1(Company, "checker", "purpose-context", "purpose-cause");
        var queue = await source.ListPendingAsync(sourceContext, 10, default); var snapshot = await source.ReadDecisionAsync(sourceContext, id, default);
        Assert.NotNull(snapshot); Assert.True(snapshot.Pending); Assert.Equal("2", snapshot.SourceVersion); Assert.Equal(new[] { "editor" }, snapshot.Item.ExcludedDeciders);
        Assert.True(ExpensePaymentService.MatchesDecisionItem(Assert.Single(queue.Items!), detail)); Assert.True(ExpensePaymentService.MatchesDecisionItem(snapshot.Item, detail));
        Assert.Equal(ExpensePaymentService.DecisionPreviewDigest(detail), snapshot.ExpensePaymentProof!.PreviewDigest);
        Assert.All(permission.Calls, call => { Assert.Equal(FinanceAction.ApproveExpensePayment, call.Action); Assert.Equal("purpose-context", call.Context.CorrelationId); Assert.Equal("purpose-cause", call.Context.CausationId); });
        Assert.False(ExpensePaymentService.MatchesDecisionItem(snapshot.Item with { ExcludedDeciders = [] }, detail));
        var wire = JsonSerializer.Serialize(snapshot, FinanceJson.CreateOptions()); Assert.DoesNotContain("expensePaymentProof", wire);
        Assert.DoesNotContain("SENTETIK kirtasiye", wire); Assert.DoesNotContain("profileId", wire);
        Assert.False(db.ChangeTracker.HasChanges());
        var denied = await core.DetailAuthorizedAsync(context, id, default); Assert.Equal(ReadOutcome.Denied, denied.Outcome);
    }

    [Fact]
    public async Task Expense_source_without_registered_guarded_owner_has_no_raw_table_read_fallback()
    {
        await using var run = await Run.StartAsync(); var id = await DecisionDraft(run); await using var db = run.Context();
        var company = new ApprovalCompany(Company); var user = new ApprovalUser();
        var source = new FinanceExpensePaymentWorkInboxSourceV1(db, new(company, user, new FinancePartyCreditLimitWorkInboxSourceV1Tests.FixedAuthority(true, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(new(Company, "checker", "uncomposed", null), id, default));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("maker")] [InlineData("editor")]
    public async Task Expense_human_preview_refuses_every_successful_draft_maker(string actor)
    {
        await using var run = await Run.StartAsync(); var id = await DecisionDraft(run);
        await using (var editor = run.Scope("editor", new AllowAll(), new UnavailableTreasuryBalanceGuard()))
            Assert.Equal(CommandOutcome.Succeeded, (await editor.Service.DraftSaveAsync(editor.Context, Key("purpose-editor"), Draft() with { PaymentId = id, ExpectedVersion = "1" }, default)).Outcome);
        await using var db = run.Context(); var user = new ApprovalUser { Actor = actor }; var core = ApprovalCore(db, new ApprovalCompany(Company), user, new DecisionReadPermission(), null);
        var result = await core.ReadDecisionAsync(new(Company, actor, "maker-context", null), id, _ => Task.CompletedTask, default);
        Assert.Equal(ReadOutcome.Denied, result.Outcome); Assert.Null(result.Resource); Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(3, "permission")] [InlineData(5, "actor")] [InlineData(7, "company")] [InlineData(9, "consumer")]
    public async Task Expense_human_preview_stops_at_each_late_owner_read_boundary_without_releasing_facts(int at, string fault)
    {
        await using var run = await Run.StartAsync(); var id = await DecisionDraft(run); await using var db = run.Context();
        var user = new ApprovalUser(); var company = new ApprovalCompany(Company); var permission = new DecisionReadPermission();
        var core = ApprovalCore(db, company, user, permission, null); var calls = 0;
        Task Fence(CancellationToken token)
        {
            if (++calls == at)
            {
                if (fault == "permission") permission.Allowed = false;
                if (fault == "actor") user.Actor = "other";
                if (fault == "company") company.Set(Guid.NewGuid());
                if (fault == "consumer") throw new InvalidOperationException("SENTETIK delegation revoked");
            }
            return Task.CompletedTask;
        }
        if (fault == "consumer") await Assert.ThrowsAsync<InvalidOperationException>(() => core.ReadDecisionAsync(new(Company, "checker", "late", null), id, Fence, default));
        else { var result = await core.ReadDecisionAsync(new(Company, "checker", "late", null), id, Fence, default); Assert.Equal(ReadOutcome.Denied, result.Outcome); Assert.Null(result.Resource); }
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Expense_final_native_version_fence_rejects_a_concurrent_draft_revision_instead_of_mixing_preparations()
    {
        await using var run = await Run.StartAsync(); var id = await DecisionDraft(run); await using var db = run.Context(); var calls = 0;
        var core = ApprovalCore(db, new ApprovalCompany(Company), new ApprovalUser(), new DecisionReadPermission(), null);
        var result = await core.ReadDecisionAsync(new(Company, "checker", "race", null), id, async _ =>
        {
            if (++calls != 6) return;
            await using var editor = run.Scope("editor", new AllowAll(), new UnavailableTreasuryBalanceGuard());
            Assert.Equal(CommandOutcome.Succeeded, (await editor.Service.DraftSaveAsync(editor.Context, Key("purpose-race"),
                Draft(description: "SENTETIK revised") with { PaymentId = id, ExpectedVersion = "1" }, default)).Outcome);
        }, default);
        Assert.Equal(ReadOutcome.Unavailable, result.Outcome); Assert.Null(result.Resource); Assert.False(db.ChangeTracker.HasChanges());
    }

    private static async Task<Guid> DecisionDraft(Run run, bool cash = false)
    {
        await using var maker = run.Scope("maker", new AllowAll(), new UnavailableTreasuryBalanceGuard());
        var result = await maker.Service.DraftSaveAsync(maker.Context, Key("purpose-draft"), cash ? CashDraft() : Draft(), default);
        Assert.Equal(CommandOutcome.Succeeded, result.Outcome); return result.Resource!.Id;
    }
    private sealed class DecisionReadPermission : IFinancePermissionPort
    {
        public bool Allowed = true; public List<(FinanceContext Context, FinanceAction Action)> Calls { get; } = [];
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token)
        { Calls.Add((context, action)); return Task.FromResult(Allowed && action == FinanceAction.ApproveExpensePayment ? PermissionDecision.Allowed : PermissionDecision.Denied); }
    }
    private static (ExpensePaymentCommandRow Row, ExpensePaymentCommandResult Result, FinanceContext Context, CommandMetadata Metadata, ApproveExpensePaymentRequest Request) DecisionReceiptFixture(string expected)
    {
        var id = Guid.NewGuid(); var context = new FinanceContext(Company, "checker", "original-correlation", null); var metadata = Key("purpose-key");
        var view = new ExpensePaymentView(id, Company, ExpensePaymentStatus.Approved, Draft().Preparation, "maker", DecisionAt.AddHours(1),
            "checker", DecisionAt, null, null, false, (long.Parse(expected, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture));
        var request = new ApproveExpensePaymentRequest(id, expected); var result = new ExpensePaymentCommandResult(CommandOutcome.Succeeded, view, false, context.CorrelationId, []);
        var row = new ExpensePaymentCommandRow { CompanyId = Company, Id = Guid.NewGuid(), PaymentId = id, CommandName = ExpensePaymentService.ApproveCommand,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey), PayloadHash = ExpensePaymentService.ApprovePayloadHash(request),
            ActorId = context.ActorId, CorrelationId = context.CorrelationId, RecordedAtUtc = DecisionAt, ResultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions()) };
        return (row, result, context, metadata, request);
    }
}
