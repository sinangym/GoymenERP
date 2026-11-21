using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FNS-D39: a pending official sales invoice record is rejected by an approver (not the recorder) with a written
/// reason, or withdrawn by its recorder. Nothing is deleted; the row keeps its trail and frees its official number,
/// ETTN and PDF content for a new record. An approved record never changes (FNS-D20).
/// </summary>
public sealed partial class FinanceOfficialSalesInvoiceServiceV1Tests
{
    private const string RejectedEvent = "finance.official-sales-invoice.rejected.v1";
    private const string WithdrawnEvent = "finance.official-sales-invoice.withdrawn.v1";
    private const string Reason = "Tutar faturadaki tutarla uyuşmuyor";

    public static TheoryData<string, string> MissingReasons => new()
    {
        { "empty", "reason_required" },
        { "whitespace", "reason_required" },
        { "padded", "reason_required" },
        { "null", "reason_required" },
        { "too-long", "finance_value_does_not_fit" },
    };

    [Theory]
    [MemberData(nameof(MissingReasons))]
    public async Task Reject_needs_a_written_reason_and_a_refused_reason_writes_nothing(string variant, string code)
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var reason = variant switch
        {
            "empty" => string.Empty,
            "whitespace" => "   ",
            "padded" => " " + Reason,
            "null" => null!,
            "too-long" => new string('R', 1001),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        var result = await fixture.Service("checker", new Documents()).RejectAsync(Context("checker"),
            new CommandMetadata("reject-key"), Reject() with { Reason = reason }, default);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal([new ValidationIssue("reason", code)], result.Issues);
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval, stored.Status);
        Assert.Null(stored.RejectionReason);
        Assert.Empty(await DecisionCommands(fixture));
        Assert.DoesNotContain(fixture.Audit.Events, e => e.EventType == RejectedEvent);
    }

    [Fact]
    public async Task A_reason_of_exactly_the_maximum_length_is_accepted()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var reason = new string('R', 1000);

        var result = await fixture.Service("checker", new Documents()).RejectAsync(Context("checker"),
            new CommandMetadata("reject-key"), Reject() with { Reason = reason }, default);

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.Equal(reason, result.Resource!.RejectionReason);
    }

    [Fact]
    public async Task The_recorder_cannot_reject_own_record_and_another_approver_rejects_it_once_with_the_trail()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var authority = new RecordingAuthority();
        var documents = new Documents();

        var own = await fixture.Service("maker", documents, authority)
            .RejectAsync(Context("maker"), new CommandMetadata("reject-own"), Reject(), default);
        fixture.Db.ChangeTracker.Clear();
        var ownCommands = (await DecisionCommands(fixture)).Count;
        var rejected = await fixture.Service("checker", documents, authority)
            .RejectAsync(Context("checker"), new CommandMetadata("reject-key"), Reject(), default);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal(CommandOutcome.ValidationFailed, own.Outcome);
        Assert.Equal([new ValidationIssue("approver", "approver_must_differ_from_recorder")], own.Issues);
        Assert.Equal(0, ownCommands);
        Assert.Equal(CommandOutcome.Succeeded, rejected.Outcome);
        Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.Rejected, rejected.Resource!.Status);
        Assert.Equal("2", rejected.Resource.Version);
        Assert.Equal("checker", rejected.Resource.RejectedBy);
        Assert.NotNull(rejected.Resource.RejectedAtUtc);
        Assert.Equal(Reason, rejected.Resource.RejectionReason);
        Assert.Null(rejected.Resource.ApprovedBy);
        Assert.Null(rejected.Resource.WithdrawnBy);
        // The rejection asks the Reject action (the host answers it with the Approve permission) and never the PDF.
        Assert.All(authority.Actions, action => Assert.Equal(FinanceOfficialSalesInvoiceAuthorityActionV1.Reject, action));
        Assert.NotEmpty(authority.Actions);
        Assert.Equal(0, documents.Calls);
        // Nothing is deleted: the row, its record receipt and the rejection receipt stay.
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.Rejected, stored.Status);
        Assert.Equal("maker", stored.RecordedBy);
        Assert.Equal(Reason, stored.RejectionReason);
        var command = Assert.Single(await DecisionCommands(fixture));
        Assert.Equal(FinanceOfficialSalesInvoiceServiceV1.RejectCommand, command.CommandName);
        Assert.Equal("checker", command.ActorId);
        var audit = Assert.Single(fixture.Audit.Events, e => e.EventType == RejectedEvent);
        Assert.Equal("Reject", audit.Operation);
        Assert.Equal(1, audit.EntityVersionBefore);
        Assert.Equal(2, audit.EntityVersionAfter);
        Assert.Equal("checker", audit.ActorId);
        Assert.Equal(FinanceOfficialSalesInvoicePermissionNamesV1.Approve, audit.PermissionCode);
        Assert.Equal("correlation-checker", audit.CorrelationId);
        Assert.Equal("reject-key", audit.IdempotencyKey);
        Assert.Contains("\"PendingApproval\"", audit.BeforeJson, StringComparison.Ordinal);
        Assert.Contains("\"Rejected\"", audit.AfterJson, StringComparison.Ordinal);
        using var after = System.Text.Json.JsonDocument.Parse(audit.AfterJson);
        Assert.Equal(Reason, after.RootElement.GetProperty("rejectionReason").GetString());
    }

    [Fact]
    public async Task Only_the_recorder_withdraws_its_own_pending_record_and_the_trail_stays()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var authority = new RecordingAuthority();
        var documents = new Documents();

        var other = await fixture.Service("checker", documents, authority)
            .WithdrawAsync(Context("checker"), new CommandMetadata("withdraw-other"), Withdraw(), default);
        fixture.Db.ChangeTracker.Clear();
        var otherCommands = (await DecisionCommands(fixture)).Count;
        var withdrawn = await fixture.Service("maker", documents, authority)
            .WithdrawAsync(Context("maker"), new CommandMetadata("withdraw-key"), Withdraw(), default);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal(CommandOutcome.ValidationFailed, other.Outcome);
        Assert.Equal([new ValidationIssue("withdrawer", "withdrawer_must_be_recorder")], other.Issues);
        Assert.Equal(0, otherCommands);
        Assert.Equal(CommandOutcome.Succeeded, withdrawn.Outcome);
        Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.Withdrawn, withdrawn.Resource!.Status);
        Assert.Equal("2", withdrawn.Resource.Version);
        Assert.Equal("maker", withdrawn.Resource.WithdrawnBy);
        Assert.NotNull(withdrawn.Resource.WithdrawnAtUtc);
        Assert.Null(withdrawn.Resource.RejectedBy);
        Assert.Null(withdrawn.Resource.RejectionReason);
        Assert.All(authority.Actions,
            action => Assert.Equal(FinanceOfficialSalesInvoiceAuthorityActionV1.Withdraw, action));
        Assert.NotEmpty(authority.Actions);
        Assert.Equal(0, documents.Calls);
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.Withdrawn, stored.Status);
        var command = Assert.Single(await DecisionCommands(fixture));
        Assert.Equal(FinanceOfficialSalesInvoiceServiceV1.WithdrawCommand, command.CommandName);
        var audit = Assert.Single(fixture.Audit.Events, e => e.EventType == WithdrawnEvent);
        Assert.Equal("Withdraw", audit.Operation);
        Assert.Equal(1, audit.EntityVersionBefore);
        Assert.Equal(2, audit.EntityVersionAfter);
        Assert.Equal("maker", audit.ActorId);
        Assert.Equal(FinanceOfficialSalesInvoicePermissionNamesV1.Withdraw, audit.PermissionCode);
        Assert.Equal("withdraw-key", audit.IdempotencyKey);
    }

    [Theory]
    [InlineData("approved", "reject")]
    [InlineData("approved", "withdraw")]
    [InlineData("rejected", "reject")]
    [InlineData("rejected", "withdraw")]
    [InlineData("withdrawn", "reject")]
    [InlineData("withdrawn", "withdraw")]
    public async Task A_record_that_is_no_longer_pending_cannot_be_rejected_or_withdrawn(string state, string decision)
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var moved = state switch
        {
            "approved" => await fixture.Service("checker", new Documents())
                .ApproveAsync(Context("checker"), new CommandMetadata("first"), Approve(), default),
            "rejected" => await fixture.Service("checker", new Documents())
                .RejectAsync(Context("checker"), new CommandMetadata("first"), Reject(), default),
            _ => await fixture.Service("maker", new Documents())
                .WithdrawAsync(Context("maker"), new CommandMetadata("first"), Withdraw(), default),
        };
        fixture.Db.ChangeTracker.Clear();
        var before = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        var auditBefore = fixture.Audit.Events.Count;

        // The current version ("2") is sent: the refusal is the state, not a stale version.
        var result = decision == "reject"
            ? await fixture.Service("third", new Documents()).RejectAsync(Context("third"),
                new CommandMetadata("second"), Reject() with { ExpectedVersion = "2" }, default)
            : await fixture.Service("maker", new Documents()).WithdrawAsync(Context("maker"),
                new CommandMetadata("second"), Withdraw() with { ExpectedVersion = "2" }, default);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal(CommandOutcome.Succeeded, moved.Outcome);
        Assert.Equal(CommandOutcome.VersionConflict, result.Outcome);
        Assert.Equal([new ValidationIssue("expectedVersion", "version_conflict")], result.Issues);
        var after = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.ApprovedBy, after.ApprovedBy);
        Assert.Equal(before.RejectedBy, after.RejectedBy);
        Assert.Equal(before.RejectionReason, after.RejectionReason);
        Assert.Equal(before.WithdrawnBy, after.WithdrawnBy);
        Assert.Equal(auditBefore, fixture.Audit.Events.Count);
        // The terminal refusal is recorded (§7) so the same key replays it.
        Assert.Contains(await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking().ToListAsync(),
            command => command.ResultJson.Contains("\"version_conflict\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("withdraw")]
    public async Task A_stale_expected_version_is_a_recorded_conflict_that_the_same_key_replays_after_the_record_moves(
        string decision)
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var actor = decision == "reject" ? "checker" : "maker";

        Task<FinanceOfficialSalesInvoiceResultV1> Decide(string correlation) => decision == "reject"
            ? fixture.Service(actor, new Documents()).RejectAsync(Context(actor) with { CorrelationId = correlation },
                new CommandMetadata("decide-key"), Reject() with { ExpectedVersion = "2" }, default)
            : fixture.Service(actor, new Documents()).WithdrawAsync(Context(actor) with { CorrelationId = correlation },
                new CommandMetadata("decide-key"), Withdraw() with { ExpectedVersion = "2" }, default);

        var result = await Decide($"correlation-{actor}");
        fixture.Db.ChangeTracker.Clear();
        var pending = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        var approved = await fixture.Service("third", new Documents())
            .ApproveAsync(Context("third"), new CommandMetadata("approve-key"), Approve(), default);
        fixture.Db.ChangeTracker.Clear();
        var replay = await Decide("correlation-retry");

        Assert.Equal(CommandOutcome.VersionConflict, result.Outcome);
        Assert.Equal([new ValidationIssue("expectedVersion", "version_conflict")], result.Issues);
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval, pending.Status);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(CommandOutcome.VersionConflict, replay.Outcome);
        Assert.Equal(result.Issues, replay.Issues);
        Assert.Equal($"correlation-{actor}", replay.OriginalCorrelationId);
        Assert.Single(await DecisionCommands(fixture));
        Assert.DoesNotContain(fixture.Audit.Events, e => e.EventType is RejectedEvent or WithdrawnEvent);
    }

    [Fact]
    public async Task Rejecting_or_withdrawing_an_unknown_record_is_a_recorded_refusal()
    {
        var fixture = Fixture.Create();

        var rejected = await fixture.Service("checker", new Documents())
            .RejectAsync(Context("checker"), new CommandMetadata("reject-key"), Reject(), default);
        var withdrawn = await fixture.Service("maker", new Documents())
            .WithdrawAsync(Context("maker"), new CommandMetadata("withdraw-key"), Withdraw(), default);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal([new ValidationIssue("invoiceId", "invoice_not_found")], rejected.Issues);
        Assert.Equal([new ValidationIssue("invoiceId", "invoice_not_found")], withdrawn.Issues);
        Assert.Equal(2, (await DecisionCommands(fixture)).Count);
        Assert.Empty(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("withdraw")]
    public async Task The_same_key_replays_the_decision_without_a_second_write_and_a_changed_body_conflicts(
        string decision)
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var actor = decision == "reject" ? "checker" : "maker";

        Task<FinanceOfficialSalesInvoiceResultV1> Decide(string correlation, string reason, string version) =>
            decision == "reject"
                ? fixture.Service(actor, new Documents()).RejectAsync(Context(actor) with { CorrelationId = correlation },
                    new CommandMetadata("decide-key"), Reject() with { Reason = reason, ExpectedVersion = version },
                    default)
                : fixture.Service(actor, new Documents()).WithdrawAsync(
                    Context(actor) with { CorrelationId = correlation }, new CommandMetadata("decide-key"),
                    Withdraw() with { ExpectedVersion = version }, default);

        var first = await Decide($"correlation-{actor}", Reason, "1");
        fixture.Db.ChangeTracker.Clear();
        var auditAfterFirst = fixture.Audit.Events.Count;
        var replay = await Decide("correlation-retry", Reason, "1");
        fixture.Db.ChangeTracker.Clear();
        var changed = await Decide("correlation-retry", "Başka bir gerekçe", "2");

        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.Equal(first.Resource, replay.Resource);
        Assert.Equal($"correlation-{actor}", replay.OriginalCorrelationId);
        Assert.Equal(CommandOutcome.IdempotencyConflict, changed.Outcome);
        Assert.Equal(auditAfterFirst, fixture.Audit.Events.Count);
        Assert.Single(await DecisionCommands(fixture));
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Reject_and_withdraw_are_refused_without_fresh_authority_before_the_shape_is_checked(
        PermissionDecision decision, CommandOutcome outcome)
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();

        var rejected = await fixture.Service("checker", new Documents(), new Authority(decision))
            .RejectAsync(Context("checker"), new CommandMetadata("reject-key"), Reject(), default);
        var emptyReason = await fixture.Service("checker", new Documents(), new Authority(decision))
            .RejectAsync(Context("checker"), new CommandMetadata("reject-key-2"), Reject() with { Reason = " " },
                default);
        var withdrawn = await fixture.Service("maker", new Documents(), new Authority(decision))
            .WithdrawAsync(Context("maker"), new CommandMetadata("withdraw-key"), Withdraw(), default);
        var emptyId = await fixture.Service("maker", new Documents(), new Authority(decision))
            .WithdrawAsync(Context("maker"), new CommandMetadata("withdraw-key-2"),
                Withdraw() with { InvoiceId = Guid.Empty }, default);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal(outcome, rejected.Outcome);
        Assert.Equal(outcome, emptyReason.Outcome);
        Assert.Equal(outcome, withdrawn.Outcome);
        Assert.Equal(outcome, emptyId.Outcome);
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval, stored.Status);
        Assert.Empty(await DecisionCommands(fixture));
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("withdraw")]
    public async Task Authority_revoked_at_the_last_fence_before_the_decision_save_leaves_the_record_pending(
        string decision)
    {
        var actor = decision == "reject" ? "checker" : "maker";
        async Task<FinanceOfficialSalesInvoiceResultV1> Decide(Fixture fixture, IFinanceOfficialSalesInvoiceAuthorityV1 authority) =>
            decision == "reject"
                ? await fixture.Service(actor, new Documents(), authority)
                    .RejectAsync(Context(actor), new CommandMetadata("decide-key"), Reject(), default)
                : await fixture.Service(actor, new Documents(), authority)
                    .WithdrawAsync(Context(actor), new CommandMetadata("decide-key"), Withdraw(), default);
        var probeFixture = Fixture.Create();
        await Record(probeFixture);
        probeFixture.Db.ChangeTracker.Clear();
        var probe = new Authority();
        var probed = await Decide(probeFixture, probe);
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var authority = new Authority([.. Enumerable.Repeat(PermissionDecision.Allowed, probe.Calls - 1),
            PermissionDecision.Denied]);

        var result = await Decide(fixture, authority);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal(CommandOutcome.Succeeded, probed.Outcome);
        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Equal(probe.Calls, authority.Calls);
        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval, stored.Status);
        Assert.Empty(await DecisionCommands(fixture));
        Assert.DoesNotContain(fixture.Audit.Events, e => e.EventType is RejectedEvent or WithdrawnEvent);
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("withdraw")]
    public async Task A_rejected_or_withdrawn_record_frees_its_official_number_ettn_and_pdf_while_a_live_one_still_collides(
        string decision)
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var second = Guid.Parse("20000000-0000-0000-0000-0000000000c1");
        var third = Guid.Parse("20000000-0000-0000-0000-0000000000c2");
        var secondPdf = Guid.Parse("50000000-0000-0000-0000-0000000000c1");
        var thirdPdf = Guid.Parse("50000000-0000-0000-0000-0000000000c2");
        fixture.Reserve(second);
        fixture.Reserve(third);
        var service = fixture.Service("maker", new Documents());

        var whilePending = await service.RecordAsync(Context("maker"), new CommandMetadata("record-pending"),
            Request() with { InvoiceId = second, PdfDocumentId = secondPdf }, default);
        fixture.Db.ChangeTracker.Clear();
        var decided = decision == "reject"
            ? await fixture.Service("checker", new Documents())
                .RejectAsync(Context("checker"), new CommandMetadata("decide-key"), Reject(), default)
            : await fixture.Service("maker", new Documents())
                .WithdrawAsync(Context("maker"), new CommandMetadata("decide-key"), Withdraw(), default);
        fixture.Db.ChangeTracker.Clear();
        // The same official number, ETTN and PDF content (same SHA-256) are recorded again on a new reserved id.
        var reused = await fixture.Service("maker", new Documents()).RecordAsync(Context("maker"),
            new CommandMetadata("record-reused"), Request() with { InvoiceId = second, PdfDocumentId = secondPdf },
            default);
        fixture.Db.ChangeTracker.Clear();
        // The new record is live again: a third record with the same identities still collides.
        var collides = await fixture.Service("maker", new Documents()).RecordAsync(Context("maker"),
            new CommandMetadata("record-third"), Request() with { InvoiceId = third, PdfDocumentId = thirdPdf },
            default);
        fixture.Db.ChangeTracker.Clear();

        Assert.Equal(CommandOutcome.ValidationFailed, whilePending.Outcome);
        Assert.Contains(whilePending.Issues, issue => issue.Code == "official_no_already_recorded");
        Assert.Equal(CommandOutcome.Succeeded, decided.Outcome);
        Assert.Equal(CommandOutcome.Succeeded, reused.Outcome);
        Assert.Equal("GYM2026000000001", reused.Resource!.OfficialNo);
        Assert.Equal(Ettn, reused.Resource.Ettn);
        Assert.Equal(Sha, reused.Resource.PdfSha256);
        Assert.Equal(FinanceOfficialSalesInvoiceStatusV1.PendingApproval, reused.Resource.Status);
        Assert.Equal(CommandOutcome.ValidationFailed, collides.Outcome);
        Assert.Contains(collides.Issues, issue => issue.Code == "official_no_already_recorded");
        var rows = await fixture.Db.OfficialSalesInvoices.AsNoTracking().OrderBy(row => row.Status).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval, rows[0].Status);
        Assert.Equal(decision == "reject" ? (int)FinanceOfficialSalesInvoiceStatusV1.Rejected
            : (int)FinanceOfficialSalesInvoiceStatusV1.Withdrawn, rows[1].Status);
    }

    [Theory]
    [InlineData("ettn")]
    [InlineData("pdf")]
    public async Task A_freed_ettn_or_pdf_alone_is_reusable_after_a_withdrawal(string identity)
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();
        var second = Guid.Parse("20000000-0000-0000-0000-0000000000c3");
        fixture.Reserve(second);
        var request = Request() with
        {
            InvoiceId = second,
            OfficialNo = "GYM2026000000009",
            PdfDocumentId = Guid.Parse("50000000-0000-0000-0000-0000000000c3"),
            Ettn = identity == "ettn" ? Ettn : Guid.NewGuid().ToString("D"),
        };
        var documents = identity == "pdf"
            ? new Documents()
            : new Documents(new FinanceOfficialSalesInvoicePdfCheckV1(
                FinanceOfficialSalesInvoicePdfOutcomeV1.Accepted, 1, OtherSha));

        var whilePending = await fixture.Service("maker", documents)
            .RecordAsync(Context("maker"), new CommandMetadata("record-pending"), request, default);
        fixture.Db.ChangeTracker.Clear();
        await fixture.Service("maker", new Documents())
            .WithdrawAsync(Context("maker"), new CommandMetadata("withdraw-key"), Withdraw(), default);
        fixture.Db.ChangeTracker.Clear();
        var reused = await fixture.Service("maker", documents)
            .RecordAsync(Context("maker"), new CommandMetadata("record-reused"), request, default);

        Assert.Equal(CommandOutcome.ValidationFailed, whilePending.Outcome);
        Assert.Contains(whilePending.Issues, issue => issue.Code == (identity == "ettn"
            ? "ettn_already_recorded" : "pdf_already_recorded"));
        Assert.Equal(CommandOutcome.Succeeded, reused.Outcome);
        Assert.Null(reused.ExistingOfficialNo);
    }

    [Fact]
    public async Task The_write_guard_accepts_only_a_rejection_by_another_actor_with_a_reason()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();

        var row = await fixture.Db.OfficialSalesInvoices.SingleAsync();
        row.Status = (int)FinanceOfficialSalesInvoiceStatusV1.Rejected;
        row.Version = 2;
        row.RejectedBy = row.RecordedBy;
        row.RejectedAtUtc = DateTimeOffset.UtcNow;
        row.RejectionReason = Reason;
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        row.RejectedBy = "checker";
        row.RejectionReason = " ";
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        row.RejectionReason = Reason;
        row.ApprovedBy = "checker";
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        row.ApprovedBy = null;
        fixture.Db.SaveChanges();
        fixture.Db.ChangeTracker.Clear();

        var rejected = await fixture.Db.OfficialSalesInvoices.SingleAsync();
        rejected.RejectionReason = "Değiştirilmiş gerekçe";
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        fixture.Db.ChangeTracker.Clear();
        fixture.Db.OfficialSalesInvoices.Remove(await fixture.Db.OfficialSalesInvoices.SingleAsync());
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        fixture.Db.ChangeTracker.Clear();

        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.Rejected, stored.Status);
        Assert.Equal(Reason, stored.RejectionReason);
    }

    [Fact]
    public async Task The_write_guard_accepts_only_a_withdrawal_by_the_recorder()
    {
        var fixture = Fixture.Create();
        await Record(fixture);
        fixture.Db.ChangeTracker.Clear();

        var row = await fixture.Db.OfficialSalesInvoices.SingleAsync();
        row.Status = (int)FinanceOfficialSalesInvoiceStatusV1.Withdrawn;
        row.Version = 2;
        row.WithdrawnBy = "checker";
        row.WithdrawnAtUtc = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        row.WithdrawnBy = row.RecordedBy;
        row.RejectionReason = Reason;
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        row.RejectionReason = null;
        fixture.Db.SaveChanges();
        fixture.Db.ChangeTracker.Clear();

        var withdrawn = await fixture.Db.OfficialSalesInvoices.SingleAsync();
        withdrawn.Status = (int)FinanceOfficialSalesInvoiceStatusV1.PendingApproval;
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
        fixture.Db.ChangeTracker.Clear();

        var stored = Assert.Single(await fixture.Db.OfficialSalesInvoices.AsNoTracking().ToListAsync());
        Assert.Equal((int)FinanceOfficialSalesInvoiceStatusV1.Withdrawn, stored.Status);
        Assert.Equal("maker", stored.WithdrawnBy);
    }

    [Fact]
    public void The_decision_migration_is_pure_ddl_up_and_its_rollback_refuses_first_while_a_decided_record_exists()
    {
        var migration = new AddOfficialSalesInvoiceRejectionAndWithdrawal();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51273", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[OfficialSalesInvoices] WHERE [Status] IN (3, 4)", guard,
            StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        var filtered = migration.UpOperations.OfType<CreateIndexOperation>().ToList();
        Assert.Equal(3, filtered.Count);
        Assert.All(filtered, index => Assert.Equal(FinanceDbContext.OfficialSalesInvoiceLiveFilter, index.Filter));
        var check = Assert.Single(migration.UpOperations.OfType<AddCheckConstraintOperation>());
        Assert.Equal(FinanceDbContext.OfficialSalesInvoiceApprovalCheck, check.Sql);
    }

    private static async Task<List<FinanceOfficialSalesInvoiceCommandRow>> DecisionCommands(Fixture fixture) =>
        await fixture.Db.OfficialSalesInvoiceCommands.AsNoTracking()
            .Where(command => command.CommandName == FinanceOfficialSalesInvoiceServiceV1.RejectCommand
                || command.CommandName == FinanceOfficialSalesInvoiceServiceV1.WithdrawCommand)
            .ToListAsync();

    private static FinanceOfficialSalesInvoiceRejectRequestV1 Reject() => new(Invoice, "1", Reason);

    private static FinanceOfficialSalesInvoiceWithdrawRequestV1 Withdraw() => new(Invoice, "1");

    /// <summary>Allows every check and remembers which action each check asked.</summary>
    private sealed class RecordingAuthority : IFinanceOfficialSalesInvoiceAuthorityV1
    {
        public List<FinanceOfficialSalesInvoiceAuthorityActionV1> Actions { get; } = [];

        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialSalesInvoiceAuthorityActionV1 action, Guid invoiceId, CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.FromResult(PermissionDecision.Allowed);
        }
    }
}
