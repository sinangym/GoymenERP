using System.Reflection;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SALES-A1 (DEC-SAL-001/002; Business 5737989917): the internal approval axis of a draft —
/// Preparing → PendingApproval → Approved | Rejected (→ PendingApproval again), each transition
/// naming the expected version and advancing it by one, carrying the audit facts; a submitted or
/// approved draft's lines are closed; the stored axis restores only when the facts fit the state.
/// The lifecycle (Draft) and the commercial revision (Rev.0) do not move. Every identity is synthetic.
/// </summary>
public sealed class SalesOrderDraftApprovalTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private const string Preparer = "synthetic-preparer";
    private const string Approver = "synthetic-approver";

    [Fact]
    public void A_draft_is_born_preparing_with_no_approval_facts_and_open_lines()
    {
        var draft = Draft();
        Assert.Equal((InternalApprovalState.Preparing, ApprovalFacts.None, true, SalesDraftState.Draft, 0, 2L),
            (draft.ApprovalState, draft.Approval, draft.LinesAreOpen, draft.State, draft.Revision, draft.Version));
    }

    [Fact]
    public void Submit_approve_moves_the_axis_records_who_and_when_and_advances_the_version_by_one_each()
    {
        var draft = Draft();
        draft.Submit(2, $" {Preparer} ", T0);
        Assert.Equal((InternalApprovalState.PendingApproval, 3L, Preparer, T0.ToUniversalTime(), false),
            (draft.ApprovalState, draft.Version, draft.Approval.SubmittedBy, draft.Approval.SubmittedAtUtc, draft.LinesAreOpen));
        Assert.Null(draft.Approval.DecidedBy);
        Assert.Equal(T0.ToUniversalTime(), draft.UpdatedAtUtc);

        // The DOMAIN does not refuse the submitter approving their own draft: D2 (5749046401) makes that a persona-based
        // authority rule measured by the service (SalesDraftServiceTests), not a state rule; here the state is decided.
        draft.Approve(3, Preparer, T0.AddMinutes(5));
        Assert.Equal((InternalApprovalState.Approved, 4L, Preparer, T0.AddMinutes(5).ToUniversalTime(), false),
            (draft.ApprovalState, draft.Version, draft.Approval.DecidedBy, draft.Approval.DecidedAtUtc, draft.LinesAreOpen));
        Assert.Null(draft.Approval.RejectionReason);
        // The other axes did not move.
        Assert.Equal((SalesDraftState.Draft, 0), (draft.State, draft.Revision));
    }

    [Fact]
    public void Reject_keeps_the_submission_records_the_decision_and_the_reason_and_reopens_the_lines_for_a_resubmission()
    {
        var draft = Draft();
        draft.Submit(2, Preparer, T0);
        draft.Reject(3, Approver, "  SENTETIK neden  ", T0.AddMinutes(1));
        Assert.Equal((InternalApprovalState.Rejected, 4L, Preparer, Approver, "SENTETIK neden", true),
            (draft.ApprovalState, draft.Version, draft.Approval.SubmittedBy, draft.Approval.DecidedBy, draft.Approval.RejectionReason, draft.LinesAreOpen));

        // Blank is "no reason", never a default text.
        var other = Draft();
        other.Submit(2, Preparer, T0);
        other.Reject(3, Approver, "   ", T0);
        Assert.Null(other.Approval.RejectionReason);

        // The same Rev.0 goes back for approval: the decision is cleared, the new submission is recorded.
        draft.Submit(4, Preparer, T0.AddMinutes(2));
        Assert.Equal((InternalApprovalState.PendingApproval, 5L, T0.AddMinutes(2).ToUniversalTime(), 0),
            (draft.ApprovalState, draft.Version, draft.Approval.SubmittedAtUtc, draft.Revision));
        Assert.Null(draft.Approval.DecidedBy);
        Assert.Null(draft.Approval.DecidedAtUtc);
        Assert.Null(draft.Approval.RejectionReason);
    }

    /// <summary>
    /// D2 (Sinan 5749046401): the preparer withdraws their own not-yet-approved draft into a terminal state of its own,
    /// with the withdrawer and the time in the decision columns and an optional reason; nothing is deleted, the lines
    /// and the revision stay. From Preparing (never submitted), from PendingApproval and — ASSUMPTION-SALESD2-2 — from
    /// Rejected; never from Approved (the easy path is not extended to an approved record) and never twice.
    /// </summary>
    [Theory]
    [InlineData("preparing")]
    [InlineData("pending")]
    [InlineData("rejected")]
    public void Withdraw_takes_back_an_unapproved_draft_into_its_own_terminal_state_and_deletes_nothing(string from)
    {
        var draft = Draft();
        var expected = 2L;
        if (from is "pending" or "rejected") { draft.Submit(expected++, Preparer, T0); }
        if (from is "rejected") { draft.Reject(expected++, Approver, "SENTETIK ret", T0); }
        var linesBefore = draft.Lines.Count;

        draft.Withdraw(expected, $" {Preparer} ", "  SENTETIK vazgeçildi  ", T0.AddMinutes(3));

        Assert.Equal((InternalApprovalState.Withdrawn, expected + 1, Preparer, T0.AddMinutes(3).ToUniversalTime(), "SENTETIK vazgeçildi"),
            (draft.ApprovalState, draft.Version, draft.Approval.DecidedBy, draft.Approval.DecidedAtUtc, draft.Approval.RejectionReason));
        // The submission facts are what they were: none from Preparing, the preparer's from the other two.
        Assert.Equal(from == "preparing" ? null : Preparer, draft.Approval.SubmittedBy);
        Assert.Equal((linesBefore, 0, SalesDraftState.Draft), (draft.Lines.Count, draft.Revision, draft.State));
        // Terminal: nothing moves after it, and nothing changes.
        var version = draft.Version;
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Submit(version, Preparer, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Approve(version, Approver, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Reject(version, Approver, null, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Withdraw(version, Preparer, null, T0)));
        Assert.Equal((InternalApprovalState.Withdrawn, version), (draft.ApprovalState, draft.Version));
    }

    [Fact]
    public void Withdraw_is_refused_for_an_approved_draft_a_stale_version_a_blank_actor_and_an_overlong_reason_and_changes_nothing()
    {
        var approved = Draft();
        approved.Submit(2, Preparer, T0);
        approved.Approve(3, Approver, T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => approved.Withdraw(4, Preparer, null, T0)));
        Assert.Equal((InternalApprovalState.Approved, 4L), (approved.ApprovalState, approved.Version));

        // An APPROVED HISTORY: approved, then a commercial revision brings it back to Preparing at Rev.1 — the current
        // state says "Preparing", the history says "was approved, may be linked": the easy path is refused, the archived
        // snapshot stays. (Sinan 5750987986 point 3; ReviseCommercial → Preparing verified at source.)
        var revised = Draft();
        revised.Submit(2, Preparer, T0);
        revised.Approve(3, Approver, T0);
        var archived = revised.ReviseCommercial(4, [Commercial(revised.Lines[0], "13.00", KgUnit, "5")], "synthetic-pricer", T0.AddMinutes(1));
        Assert.NotNull(archived);
        Assert.Equal((InternalApprovalState.Preparing, 1, 5L), (revised.ApprovalState, revised.Revision, revised.Version));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => revised.Withdraw(5, Preparer, null, T0)));
        Assert.Equal((InternalApprovalState.Preparing, 1, 5L, 1), (revised.ApprovalState, revised.Revision, revised.Version, revised.ArchivedRevisions.Count));
        Assert.Same(archived, revised.ArchivedRevisions[0]);

        var pending = Draft();
        pending.Submit(2, Preparer, T0);
        Assert.Throws<SalesVersionConflictException>(() => pending.Withdraw(2, Preparer, null, T0));
        Assert.Throws<ArgumentException>(() => pending.Withdraw(3, " ", null, T0));
        Assert.Equal(SalesRuleCodes.ApprovalReasonTooLong, CodeOf(() => pending.Withdraw(3, Preparer, new string('x', SalesOrderDraft.MaximumRejectionReasonLength + 1), T0)));
        Assert.Equal((InternalApprovalState.PendingApproval, 3L, null), (pending.ApprovalState, pending.Version, pending.Approval.DecidedBy));
        // Blank is "no reason", never a default text.
        pending.Withdraw(3, Preparer, "   ", T0);
        Assert.Null(pending.Approval.RejectionReason);
    }

    /// <summary>
    /// D2 (Sinan 5750987986 point 3d): Withdrawn is terminal only if EVERY mutation entry refuses it — the third
    /// population (after the recorded-ask pins and the Response callers). The population is pinned by reflection
    /// (every public instance method that is not a store restore or a Require* guard), then each entry is driven
    /// against a withdrawn draft: state conflict, nothing changed.
    /// </summary>
    [Fact]
    public void Withdrawn_is_terminal_at_every_mutation_entry_and_the_population_is_pinned()
    {
        var entries = typeof(SalesOrderDraft)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName
                && !method.Name.StartsWith("Restore", StringComparison.Ordinal)
                && !method.Name.StartsWith("Require", StringComparison.Ordinal))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        // DEC-SAL-005: the cancellation of an approved order is a mutation entry too (RequireCancellable only reads);
        // DEC-SAL-002: so is the quantity revision (RequireQuantityRevisable only reads).
        Assert.Equal(["AddLine", "Approve", "ApproveRemainderChange", "Cancel", "RecordCustomerAcceptance", "Reject", "RejectRemainderChange",
            "RequestRemainderChange", "RescheduleDelivery", "ReviseCommercial", "ReviseQuantities", "Submit", "Withdraw"], entries);

        var draft = Draft();
        draft.Withdraw(2, Preparer, "SENTETIK", T0);
        var before = (draft.ApprovalState, draft.Version, draft.Revision, draft.Approval, draft.Lines.Count);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.AddLine(3, Line(), T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Submit(3, Preparer, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Approve(3, Approver, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Reject(3, Approver, null, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Withdraw(3, Preparer, null, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RecordCustomerAcceptance(3, Approver, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ReviseCommercial(3, [Commercial(draft.Lines[0], "13.00", KgUnit, "5")], "synthetic-pricer", T0)));
        // DEC-SAL-003: the delivery-schedule update is a mutation entry too; a withdrawn draft's dates do not move.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RescheduleDelivery(3, Day.AddDays(5),
            [new LineScheduleInput(draft.Lines[0].Id, Day.AddDays(5), [])], "synthetic-planner", T0)));
        // DEC-SAL-005: a withdrawn draft was never approved — it is not cancelled either.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Cancel(3, Approver, "SENTETIK",
            new CancellationImpactSnapshot("t", [new("inventory.active-allocations", "None")]), T0)));
        // DEC-SAL-002: a withdrawn draft's quantities do not move.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ReviseQuantities(3,
            [new LineQuantityInput(draft.Lines[0].Id, "9", 9m)], "synthetic-reviser", "SENTETIK", T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RequestRemainderChange(3, draft.Lines[0].Id,
            RemainderChangeKind.Cancel, null, null, "SENTETIK", new RemainderFloor(1m, 1m, 1m), Approver, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.ApproveRemainderChange(3, 1,
            new RemainderFloor(1m, 1m, 1m), Approver, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.RejectRemainderChange(3, 1, Approver, "SENTETIK", T0)));
        Assert.Equal(before, (draft.ApprovalState, draft.Version, draft.Revision, draft.Approval, draft.Lines.Count));
        Assert.Equal(InternalApprovalState.Withdrawn, draft.ApprovalState);
    }

    [Fact]
    public void A_transition_from_the_wrong_state_is_a_state_conflict_and_changes_nothing()
    {
        var draft = Draft();
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Approve(2, Approver, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Reject(2, Approver, null, T0)));
        Assert.Equal((InternalApprovalState.Preparing, 2L, ApprovalFacts.None), (draft.ApprovalState, draft.Version, draft.Approval));

        draft.Submit(2, Preparer, T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Submit(3, Preparer, T0)));

        draft.Approve(3, Approver, T0);
        // DEC-SAL-002: an approved draft changes only through a revision (A2): no transition of A1 applies, no line moves.
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Submit(4, Preparer, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Approve(4, Approver, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.Reject(4, Approver, null, T0)));
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.AddLine(4, Line(), T0)));
        Assert.Equal((InternalApprovalState.Approved, 4L, 1), (draft.ApprovalState, draft.Version, draft.Lines.Count));
    }

    [Fact]
    public void A_pending_drafts_lines_are_closed_and_a_rejected_drafts_lines_are_open_again()
    {
        var draft = Draft();
        draft.Submit(2, Preparer, T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => draft.AddLine(3, Line(), T0)));
        draft.Reject(3, Approver, null, T0);
        draft.AddLine(4, Line(), T0);
        Assert.Equal((2, 5L), (draft.Lines.Count, draft.Version));
    }

    [Fact]
    public void A_stale_version_a_blank_actor_and_an_over_long_reason_are_refused_before_anything_moves()
    {
        var draft = Draft();
        var stale = Assert.Throws<SalesVersionConflictException>(() => draft.Submit(1, Preparer, T0));
        Assert.Equal((1L, 2L, SalesRuleCodes.VersionConflict), (stale.ExpectedVersion, stale.CurrentVersion, stale.Code));
        Assert.Throws<ArgumentException>(() => draft.Submit(2, " ", T0));
        Assert.Throws<ArgumentException>(() => draft.Submit(2, new string('a', SalesOrderDraft.MaximumActorLength + 1), T0));
        Assert.Equal((InternalApprovalState.Preparing, 2L), (draft.ApprovalState, draft.Version));

        draft.Submit(2, Preparer, T0);
        Assert.Equal(SalesRuleCodes.ApprovalReasonTooLong,
            CodeOf(() => draft.Reject(3, Approver, new string('x', SalesOrderDraft.MaximumRejectionReasonLength + 1), T0)));
        draft.Reject(3, Approver, new string('x', SalesOrderDraft.MaximumRejectionReasonLength), T0);
        Assert.Equal(InternalApprovalState.Rejected, draft.ApprovalState);
    }

    [Fact]
    public void A_draft_without_lines_cannot_be_submitted()
    {
        var empty = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-1", Header(), Money(), T0);
        Assert.Equal(SalesRuleCodes.DraftWithoutLines, CodeOf(() => empty.Submit(1, Preparer, T0)));
    }

    [Theory]
    [InlineData(InternalApprovalState.Preparing, 2L, true)]
    [InlineData(InternalApprovalState.PendingApproval, 3L, true)]
    [InlineData(InternalApprovalState.Approved, 4L, true)]
    [InlineData(InternalApprovalState.Rejected, 4L, true)]
    // D2: withdrawn — at least one step (from Preparing, no submission) or two (after a submission).
    [InlineData(InternalApprovalState.Withdrawn, 3L, true)]
    [InlineData(InternalApprovalState.Withdrawn, 2L, false)]
    [InlineData(InternalApprovalState.PendingApproval, 2L, false)]
    [InlineData(InternalApprovalState.Approved, 3L, false)]
    // ÜRÜN #338 P3-1 pinned "Preparing at 3 is refused" (no transition could have raised a Preparing draft above its
    // lines' 2). K3 Head B narrows that pin DELIBERATELY: a commercial write on a Preparing draft is a legitimate
    // step no stored fact counts, so 3 is now accepted; below the lines' version is still refused.
    [InlineData(InternalApprovalState.Preparing, 3L, true)]
    [InlineData(InternalApprovalState.Preparing, 1L, false)]
    public void The_store_restores_the_axis_only_with_consistent_facts_and_a_version_the_transitions_account_for(
        InternalApprovalState state, long storedVersion, bool accepted)
    {
        var draft = Draft();
        var facts = state switch
        {
            InternalApprovalState.Preparing => ApprovalFacts.None,
            InternalApprovalState.PendingApproval => new ApprovalFacts(Preparer, T0, null, null, null),
            InternalApprovalState.Approved => new ApprovalFacts(Preparer, T0, Approver, T0.AddMinutes(1), null),
            InternalApprovalState.Withdrawn => new ApprovalFacts(null, null, Preparer, T0.AddMinutes(1), null),
            _ => new ApprovalFacts(Preparer, T0, Approver, T0.AddMinutes(1), "SENTETIK neden"),
        };

        if (!accepted)
        {
            Assert.Throws<InvalidOperationException>(() => draft.RestoreApproval(state, facts, storedVersion, T0));
            return;
        }

        draft.RestoreApproval(state, facts, storedVersion, T0.AddMinutes(3));
        Assert.Equal((state, facts, storedVersion, T0.AddMinutes(3).ToUniversalTime()), (draft.ApprovalState, draft.Approval, draft.Version, draft.UpdatedAtUtc));
        // Restoring is not a transition: it cannot run twice, and the facts must fit the state.
        Assert.Throws<InvalidOperationException>(() => draft.RestoreApproval(state, facts, storedVersion, T0));
    }

    [Theory]
    [InlineData("approved-without-decision")]
    [InlineData("approved-with-reason")]
    [InlineData("preparing-with-submission")]
    [InlineData("pending-with-decision")]
    [InlineData("rejected-empty-reason")]
    [InlineData("withdrawn-without-decision")]
    [InlineData("withdrawn-half-submission")]
    [InlineData("undefined-state")]
    public void Inconsistent_stored_facts_are_refused_not_repaired(string fault)
    {
        var draft = Draft();
        var (state, facts) = fault switch
        {
            "approved-without-decision" => (InternalApprovalState.Approved, new ApprovalFacts(Preparer, T0, null, null, null)),
            "approved-with-reason" => (InternalApprovalState.Approved, new ApprovalFacts(Preparer, T0, Approver, T0, "x")),
            "preparing-with-submission" => (InternalApprovalState.Preparing, new ApprovalFacts(Preparer, T0, null, null, null)),
            "pending-with-decision" => (InternalApprovalState.PendingApproval, new ApprovalFacts(Preparer, T0, Approver, T0, null)),
            "rejected-empty-reason" => (InternalApprovalState.Rejected, new ApprovalFacts(Preparer, T0, Approver, T0, "")),
            // D2: a withdrawn row needs the withdrawer; a submission is all-or-nothing.
            "withdrawn-without-decision" => (InternalApprovalState.Withdrawn, new ApprovalFacts(Preparer, T0, null, null, null)),
            "withdrawn-half-submission" => (InternalApprovalState.Withdrawn, new ApprovalFacts(Preparer, null, Preparer, T0, null)),
            _ => ((InternalApprovalState)9, ApprovalFacts.None),
        };

        Assert.ThrowsAny<Exception>(() => draft.RestoreApproval(state, facts, 9, T0));
        Assert.Equal((InternalApprovalState.Preparing, ApprovalFacts.None, 2L), (draft.ApprovalState, draft.Approval, draft.Version));
    }

    [Theory]
    [InlineData(RemainderChangeKind.Revise)]
    [InlineData(RemainderChangeKind.Cancel)]
    public void Approved_remainder_change_keeps_delivered_facts_and_reopens_as_the_same_record(RemainderChangeKind kind)
    {
        var lineId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        var draft = Draft(Line(lineId));
        draft.Submit(2, Preparer, T0);
        draft.Approve(3, Approver, T0.AddMinutes(1));
        var approval = draft.Approval;
        var requestFloor = new RemainderFloor(0.7m, 0.6m, 0.5m);

        var requested = draft.RequestRemainderChange(4, lineId, kind,
            kind == RemainderChangeKind.Revise ? "1.1" : null,
            kind == RemainderChangeKind.Revise ? 1.1m : null,
            "SENTETIK müşteri kalanı değiştirdi", requestFloor, Preparer, T0.AddMinutes(2));

        Assert.Equal(("1.25", LineRemainderState.Open, 5L),
            (draft.Lines.Single().QuantityText, draft.Lines.Single().Remainder, draft.Version));
        Assert.Equal(0.5m, requested.Request.Floor.Delivered);

        var decisionFloor = new RemainderFloor(0.8m, 0.6m, 0.5m);
        var decided = draft.ApproveRemainderChange(5, requested.Request.ChangeNo, decisionFloor, Approver, T0.AddMinutes(3));
        var expectedQuantity = kind == RemainderChangeKind.Cancel ? 0.8m : 1.1m;
        var expectedText = kind == RemainderChangeKind.Cancel ? "0.8" : "1.1";
        var expectedRemainder = kind == RemainderChangeKind.Cancel ? LineRemainderState.Cancelled : LineRemainderState.Revised;

        Assert.Equal((expectedQuantity, expectedText, expectedRemainder, 6L, 0, InternalApprovalState.Approved),
            (draft.Lines.Single().Quantity.Value, draft.Lines.Single().QuantityText, draft.Lines.Single().Remainder,
                draft.Version, draft.Revision, draft.ApprovalState));
        Assert.Equal(approval, draft.Approval);
        Assert.Equal((0.5m, RemainderChangeState.Approved), (decided.Decision!.Floor!.Delivered, decided.State));

        var reopened = SalesOrderDraft.Create(draft.Id, Company, draft.DocumentNo, Header(), Money(), T0);
        reopened.AddLine(1, Line(lineId, expectedText, expectedQuantity), T0);
        reopened.RestoreRemainderChange(decided);
        reopened.RestoreApproval(InternalApprovalState.Approved, approval, draft.Version, draft.UpdatedAtUtc);

        Assert.Equal((draft.Id, expectedQuantity, expectedText, expectedRemainder, 6L, InternalApprovalState.Approved),
            (reopened.Id, reopened.Lines.Single().Quantity.Value, reopened.Lines.Single().QuantityText,
                reopened.Lines.Single().Remainder, reopened.Version, reopened.ApprovalState));
        Assert.Equal((requestFloor, decisionFloor),
            (reopened.RemainderChanges.Single().Request.Floor, reopened.RemainderChanges.Single().Decision!.Floor));
    }

    [Theory]
    [InlineData("current-quantity")]
    [InlineData("current-text")]
    [InlineData("decision-floor")]
    [InlineData("rejected-result")]
    public void Corrupt_remainder_history_is_refused_instead_of_repaired(string fault)
    {
        var lineId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        var storedText = fault == "current-text" ? "1.10" : fault == "current-quantity" ? "1.2" : "1.1";
        var storedValue = fault == "current-quantity" ? 1.2m : 1.1m;
        var draft = Draft(Line(lineId, storedText, storedValue));
        var request = new RemainderChangeRequest(1, 0, lineId, 1, RemainderChangeKind.Revise, "1.25", 1.1m, "1.1",
            "SENTETIK kalan", Preparer, T0, new RemainderFloor(0.7m, 0.6m, 0.5m));
        var decision = fault == "rejected-result"
            ? new RemainderChangeDecision(RemainderChangeState.Rejected, Approver, T0.AddMinutes(1), null, 1.1m, "1.1", null)
            : new RemainderChangeDecision(RemainderChangeState.Approved, Approver, T0.AddMinutes(1), null, 1.1m, "1.1",
                fault == "decision-floor" ? new RemainderFloor(0.7m, 0.8m, 0.5m) : new RemainderFloor(0.8m, 0.6m, 0.5m));

        if (fault is "decision-floor" or "rejected-result")
        {
            Assert.Throws<InvalidOperationException>(() => draft.RestoreRemainderChange(new RemainderChangeFacts(request, decision)));
            return;
        }

        draft.RestoreRemainderChange(new RemainderChangeFacts(request, decision));
        Assert.Throws<InvalidOperationException>(() => draft.RestoreApproval(InternalApprovalState.Approved,
            new ApprovalFacts(Preparer, T0, Approver, T0.AddMinutes(1), null), 6, T0.AddMinutes(2)));
    }

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    [Fact]
    public void New_order_waits_for_customer_acceptance_and_a_revision_requires_a_fresh_acceptance()
    {
        var draft = Draft(customerAcceptanceRequired: true);
        draft.Submit(2, Preparer, T0);
        draft.Approve(3, Approver, T0.AddMinutes(1));
        Assert.False(draft.IsCustomerAccepted);

        draft.RecordCustomerAcceptance(4, "synthetic-customer-acceptor", T0.AddMinutes(2));
        Assert.True(draft.IsCustomerAccepted);
        Assert.Equal((0, "synthetic-customer-acceptor", 5L),
            (draft.CustomerAcceptance!.Revision, draft.CustomerAcceptance.AcceptedBy, draft.Version));

        draft.ReviseCommercial(5, [Commercial(draft.Lines[0], "13.00", KgUnit, "5")], "synthetic-pricer", T0.AddMinutes(3));
        Assert.False(draft.IsCustomerAccepted);
        Assert.Equal(0, draft.CustomerAcceptance.Revision);
    }

    [Fact]
    public void Restore_refuses_a_current_revision_acceptance_beside_every_non_approved_state()
    {
        var cases = new (InternalApprovalState State, ApprovalFacts Facts, long Version)[]
        {
            (InternalApprovalState.Preparing, ApprovalFacts.None, 3),
            (InternalApprovalState.PendingApproval, new ApprovalFacts(Preparer, T0, null, null, null), 4),
            (InternalApprovalState.Rejected, new ApprovalFacts(Preparer, T0, Approver, T0.AddMinutes(1), "synthetic rejection"), 5),
        };

        foreach (var (state, facts, version) in cases)
        {
            var draft = Draft(customerAcceptanceRequired: true);
            draft.RestoreCustomerAcceptance(new CustomerAcceptanceFacts(0, "synthetic-customer-acceptor", T0));

            var failure = Assert.Throws<InvalidOperationException>(() =>
                draft.RestoreApproval(state, facts, version, T0.AddMinutes(2)));
            Assert.Contains("Güncel revizyon müşteri kabulü", failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Legacy_order_needs_no_synthetic_customer_acceptance()
    {
        var draft = Draft();
        draft.Submit(2, Preparer, T0);
        draft.Approve(3, Approver, T0);
        Assert.True(draft.IsCustomerAccepted);
        Assert.Null(draft.CustomerAcceptance);
    }

    private static SalesDraftHeader Header() => new(Guid.Parse("33333333-3333-4333-8333-333333333333"),
        Guid.Parse("44444444-4444-4444-8444-444444444444"), Guid.Parse("55555555-5555-4555-8555-555555555555"), Day);

    private static MoneyProfileSnapshot Money() => new(Company, Guid.Parse("55555555-5555-4555-8555-555555555555"),
        SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3", Day, true, new DateOnly(2026, 9, 1), null, 2);

    private static SalesDraftLineInput Line(Guid? lineId = null, string quantityText = "1.25", decimal quantity = 1.25m)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Guid.Parse("99999999-9999-4999-8999-999999999999"), "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        return new SalesDraftLineInput(lineId ?? Guid.NewGuid(), product, unit, quantityText, quantity);
    }

    /// <summary>A one-line draft at version 2 (1 at birth + 1 line).</summary>
    private static LineCommercialInput Commercial(SalesOrderDraftLine line, string? price, Guid? unit, string? discount) =>
        new(line.Id, new CommercialDraftFields(price, unit, discount));

    private static SalesOrderDraft Draft(SalesDraftLineInput? line = null, bool customerAcceptanceRequired = false)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001", Header(), Money(), T0,
            customerAcceptanceRequired);
        draft.AddLine(1, line ?? Line(), T0);
        return draft;
    }
}
