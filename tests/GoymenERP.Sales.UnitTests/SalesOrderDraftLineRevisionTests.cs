using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D167 (EK24): the line revision in the aggregate — add a line, remove an unshipped line (passive, never deleted), change an
/// unshipped line's product (a removal replaced by an added line); an approved order is archived and re-enters approval with a new
/// customer acceptance; removed lines leave every order read but keep their numbers; the rebuild restores them. Synthetic, NOT_ENV.
/// </summary>
public sealed class SalesOrderDraftLineRevisionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 3);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid KgUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid KgProfile = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid ProductA = Guid.Parse("99999999-9999-4999-8999-999999999991");
    private static readonly Guid ProductB = Guid.Parse("99999999-9999-4999-8999-999999999992");

    private static string CodeOf(Action action) => Assert.Throws<SalesRuleException>(action).Code;

    private static SalesOrderDraft Draft(bool acceptance = false) => SalesOrderDraft.Create(Guid.NewGuid(), Company, "SO-2026-000001",
        new SalesDraftHeader(Party, Account, Currency, Day, DueTerms: TestDueTerms.PointDue30),
        new MoneyProfileSnapshot(Company, Currency, SalesOrderDraft.MoneyOperation, Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "3",
            Day, true, new DateOnly(2026, 9, 1), null, 2), T0, customerAcceptanceRequired: acceptance);

    private static SalesDraftLineInput Line(Guid? product = null, string quantity = "10", Guid? lineId = null)
    {
        var unit = new UnitProfileSnapshot(Company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
        var binding = new UnitBindingSnapshot(Company, KgUnit, KgProfile, "4");
        var productId = product ?? ProductA;
        return new SalesDraftLineInput(lineId ?? Guid.NewGuid(), new ProductSnapshot(productId, "7", productId == ProductA ? "A-1" : "B-1",
            "Sentetik ürün", Day, binding, binding, binding), unit, quantity, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static (SalesOrderDraft Draft, SalesOrderDraftLine First, SalesOrderDraftLine Second) Approved(bool acceptance = false)
    {
        var draft = Draft(acceptance);
        var first = draft.AddLine(draft.Version, Line(), T0);
        var second = draft.AddLine(draft.Version, Line(ProductB, "20"), T0);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        return (draft, first, second);
    }

    [Fact]
    public void An_approved_orders_line_revision_archives_it_adds_and_passivates_lines_and_re_enters_approval_and_customer_acceptance()
    {
        var (draft, first, second) = Approved(acceptance: true);
        draft.RecordCustomerAcceptance(draft.Version, "SENTETIK-sales", T0);
        var version = draft.Version;
        var added = Line(ProductB, "5");

        var archived = draft.ReviseLines(version, [added], [new LineRemovalInput(first.Id)], "SENTETIK-writer", "  SENTETIK müşteri değişikliği  ", T0, Day);

        Assert.NotNull(archived);
        Assert.Equal(("SENTETIK müşteri değişikliği", 2), (archived!.Reason, archived.Lines.Count));
        Assert.Equal((1, InternalApprovalState.Preparing, version + 1, false), (draft.Revision, draft.ApprovalState, draft.Version, draft.IsCustomerAccepted));
        Assert.Equal([second.Id, added.LineId], draft.Lines.Select(line => line.Id));
        Assert.Equal([1, 2, 3], draft.AllLines.Select(line => line.LineNo));
        var removed = Assert.Single(draft.RemovedLines);
        Assert.Equal((first.Id, 1, (Guid?)null, true), (removed.Id, removed.RemovedInRevision, removed.ReplacedByLineId, removed.IsRemoved));
        Assert.Equal((1, 3), (draft.Lines[1].AddedInRevision, draft.Lines[1].LineNo));
        // A removed line is out of the order's totals (its lines are the active ones only).
        Assert.DoesNotContain(draft.Lines, line => line.Id == first.Id);
    }

    [Fact]
    public void A_product_change_is_a_removal_replaced_by_an_added_line_for_another_product()
    {
        var (draft, first, _) = Approved();
        var replacement = Line(ProductB, "10");

        draft.ReviseLines(draft.Version, [replacement], [new LineRemovalInput(first.Id, replacement.LineId)], "SENTETIK-writer", "SENTETIK ürün", T0, Day);

        Assert.Equal(replacement.LineId, Assert.Single(draft.RemovedLines).ReplacedByLineId);
        Assert.Contains(draft.Lines, line => line.Id == replacement.LineId && line.Product.ProductId == ProductB);
    }

    [Fact]
    public void A_line_revision_refuses_what_SAT_D167_does_not_allow()
    {
        var (draft, first, second) = Approved();
        var version = draft.Version;
        Assert.Equal(SalesRuleCodes.RevisionNoChange, CodeOf(() => draft.ReviseLines(version, [], [], "SENTETIK", "r", T0, Day)));
        Assert.Equal(SalesRuleCodes.RevisionReasonRequired, CodeOf(() => draft.ReviseLines(version, [Line()], [], "SENTETIK", " ", T0, Day)));
        Assert.Equal(SalesRuleCodes.RevisionLinesMismatch,
            CodeOf(() => draft.ReviseLines(version, [], [new LineRemovalInput(Guid.NewGuid())], "SENTETIK", "r", T0, Day)));
        Assert.Equal(SalesRuleCodes.LineRevisionNoLinesLeft,
            CodeOf(() => draft.ReviseLines(version, [], [new LineRemovalInput(first.Id), new LineRemovalInput(second.Id)], "SENTETIK", "r", T0, Day)));
        var same = Line(ProductA);
        Assert.Equal(SalesRuleCodes.LineReplacementSameProduct,
            CodeOf(() => draft.ReviseLines(version, [same], [new LineRemovalInput(first.Id, same.LineId)], "SENTETIK", "r", T0, Day)));
        Assert.Equal(SalesRuleCodes.LineReplacementInvalid,
            CodeOf(() => draft.ReviseLines(version, [], [new LineRemovalInput(first.Id, Guid.NewGuid())], "SENTETIK", "r", T0, Day)));
        Assert.Equal(SalesRuleCodes.DuplicateLineId, CodeOf(() => draft.ReviseLines(version, [Line(lineId: first.Id)], [], "SENTETIK", "r", T0, Day)));
        Assert.Throws<SalesVersionConflictException>(() => draft.ReviseLines(version - 1, [Line()], [], "SENTETIK", "r", T0, Day));
        // Nothing moved.
        Assert.Equal((0, InternalApprovalState.Approved, version, 2), (draft.Revision, draft.ApprovalState, draft.Version, draft.Lines.Count));

        // A pending order is refused (the decision comes first); a preparing one simply changes (no archive).
        var pending = Draft();
        pending.AddLine(pending.Version, Line(), T0);
        pending.Submit(pending.Version, "SENTETIK-writer", T0);
        Assert.Equal(SalesRuleCodes.StateConflict, CodeOf(() => pending.ReviseLines(pending.Version, [Line()], [], "SENTETIK", "r", T0, Day)));
        var preparing = Draft();
        preparing.AddLine(preparing.Version, Line(), T0);
        Assert.Null(preparing.ReviseLines(preparing.Version, [Line(ProductB)], [], "SENTETIK", "r", T0, Day));
        Assert.Equal((0, 2, 0), (preparing.Revision, preparing.Lines.Count, preparing.Lines[1].AddedInRevision));
    }

    [Fact]
    public void The_rebuild_restores_added_and_removed_lines_without_version_steps_and_schedules_name_their_own_lines()
    {
        var (draft, first, second) = Approved();
        var added = Line(ProductB, "5");
        draft.ReviseLines(draft.Version, [added], [new LineRemovalInput(first.Id, added.LineId)], "SENTETIK-writer", "r", T0, Day);
        draft.RescheduleDelivery(draft.Version, Day.AddDays(5),
            [.. draft.Lines.Select(line => new LineScheduleInput(line.Id, Day.AddDays(7), []))], "SENTETIK-planner", T0);

        var rebuilt = Draft();
        rebuilt.AddLine(rebuilt.Version, Line(lineId: first.Id), T0);
        rebuilt.AddLine(rebuilt.Version, Line(ProductB, "20", second.Id), T0);
        rebuilt.RestoreAddedLine(Line(ProductB, "5", added.LineId), 1);
        rebuilt.RestoreLineRemoval(first.Id, 1, added.LineId);
        rebuilt.RestoreRevision(1);
        rebuilt.RestoreArchivedRevision(draft.ArchivedRevisions[0]);
        rebuilt.RestoreDeliverySchedule(Assert.Single(draft.DeliverySchedules));
        rebuilt.RestoreApproval(InternalApprovalState.Preparing, ApprovalFacts.None, draft.Version, T0);

        Assert.Equal(draft.Lines.Select(line => (line.Id, line.LineNo, line.RequestedDeliveryDate)),
            rebuilt.Lines.Select(line => (line.Id, line.LineNo, line.RequestedDeliveryDate)));
        Assert.Equal((first.Id, 1, added.LineId), (rebuilt.RemovedLines[0].Id, rebuilt.RemovedLines[0].RemovedInRevision, rebuilt.RemovedLines[0].ReplacedByLineId));
        // Schedule 0 is the create's lines (with the removed one, without the added one).
        Assert.Equal([first.Id, second.Id], rebuilt.DeliveryScheduleHistory[0].Lines.Select(line => line.LineId));
    }

    [Fact]
    public void A_rejection_reverts_its_removals_in_the_order_and_the_rebuild_keeps_the_reinstatement()
    {
        // SAT-D211 (EK36): Rev.1 removes A (product change to C) and B; the rejection brings A and B back and takes C out.
        var (draft, first, second) = Approved();
        var replacement = Line(ProductB, "10");
        draft.ReviseLines(draft.Version, [replacement], [new LineRemovalInput(first.Id, replacement.LineId), new LineRemovalInput(second.Id)],
            "SENTETIK-writer", "r", T0, Day);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Reject(draft.Version, "SENTETIK-approver", "SENTETIK ret", T0);

        Assert.Equal([first.Id, second.Id], draft.Lines.Select(line => line.Id));
        Assert.All(draft.Lines, line => Assert.Equal((1, (int?)null, (Guid?)null), (line.ReinstatedInRevision!.Value, line.RemovedInRevision, line.ReplacedByLineId)));
        var passive = Assert.Single(draft.RemovedLines);
        Assert.Equal((replacement.LineId, 1, (Guid?)null), (passive.Id, passive.RemovedInRevision, passive.ReplacedByLineId));

        // A reinstated line leaves again only by a later revision's own removal.
        draft.ReviseLines(draft.Version, [], [new LineRemovalInput(second.Id)], "SENTETIK-writer", "r", T0, Day);
        Assert.Equal((1, 1), (second.RemovedInRevision, second.ReinstatedInRevision));

        // The rebuild: reinstatements first, then removals.
        var rebuilt = Draft();
        rebuilt.AddLine(rebuilt.Version, Line(lineId: first.Id), T0);
        rebuilt.AddLine(rebuilt.Version, Line(ProductB, "20", second.Id), T0);
        rebuilt.RestoreAddedLine(Line(ProductB, "10", replacement.LineId), 1);
        rebuilt.RestoreReinstatement(first.Id, 1);
        rebuilt.RestoreReinstatement(second.Id, 1);
        rebuilt.RestoreLineRemoval(replacement.LineId, 1, null);
        rebuilt.RestoreLineRemoval(second.Id, 1, null);
        Assert.Equal([first.Id], rebuilt.Lines.Select(line => line.Id));
        Assert.Equal((1, 1), (rebuilt.Lines[0].ReinstatedInRevision!.Value, rebuilt.RemovedLines.Single(line => line.Id == second.Id).ReinstatedInRevision!.Value));
    }

    /// <summary>Rev.1 removed <paramref name="removedId"/> (a product change to <paramref name="replacement"/> when given); the order was
    /// approved internally and the customer rejected it: Approved, customer-rejected, the revert still pending.</summary>
    private static (SalesOrderDraft Draft, SalesOrderDraftLine First, SalesOrderDraftLine Second) CustomerRejectedRemoval(SalesDraftLineInput? replacement = null)
    {
        var (draft, first, second) = Approved(acceptance: true);
        draft.RecordCustomerAcceptance(draft.Version, "SENTETIK-sales", T0);
        draft.ReviseLines(draft.Version, replacement is null ? [] : [replacement], [new LineRemovalInput(first.Id, replacement?.LineId)], "SENTETIK-writer", "r", T0, Day);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        draft.RecordCustomerRejection(draft.Version, "SENTETIK-sales", "SENTETIK musteri istemedi", T0);
        return (draft, first, second);
    }

    [Fact]
    public void A_quantity_price_or_header_revision_waits_while_a_customer_rejected_removal_waits_to_be_reinstated()
    {
        // K3 review A P1-1 / P2-1: their inputs were validated for the lines held NOW; the revert after the archive changes that set
        // (a reinstated line unnamed — the header tax lookup threw KeyNotFound —, a replacement written and then dropped).
        var replacement = Line(ProductB, "10");
        var (draft, first, second) = CustomerRejectedRemoval(replacement);
        Assert.True(draft.HasPendingReinstatement);
        var version = draft.Version;
        var archived = draft.ArchivedRevisions.Count;

        Assert.Equal(SalesRuleCodes.RevisionReinstatementPending, CodeOf(() => draft.ReviseQuantities(version,
            draft.Lines.Select(line => new LineQuantityInput(line.Id, "99", 99m)).ToArray(), "SENTETIK", "r", T0)));
        Assert.Equal(SalesRuleCodes.RevisionReinstatementPending, CodeOf(() => draft.ReviseCommercial(version,
            draft.Lines.Select(line => new LineCommercialInput(line.Id, new CommercialDraftFields("12.5", KgUnit, null))).ToArray(), "SENTETIK", T0)));
        var dueTerms = SalesDueTerms.Create([new SalesDueInstalment(1, SalesDueBasis.OrderDate, 45, "100")]);
        Assert.Equal(SalesRuleCodes.RevisionReinstatementPending, CodeOf(() => draft.ReviseHeader(version, new SalesHeaderRevisionInput(draft.Header.PartyId,
            draft.Header.CompanyAccountId, draft.Header.CurrencyId, null, draft.Header.DeliveryAddress, draft.Header.Contact, draft.Header.SalesOwnerUserId,
            dueTerms), "SENTETIK", "r", T0)));
        // Nothing moved: the passive replacement kept its quantity, the removed line stays passive, no archive, no version step.
        Assert.Equal((version, archived, 1), (draft.Version, draft.ArchivedRevisions.Count, draft.Revision));
        Assert.Equal("10", draft.Lines.Single(line => line.Id == replacement.LineId).QuantityText);
        Assert.Contains(draft.RemovedLines, line => line.Id == first.Id);

        // An empty line revision opens the next revision and reinstates; the three then go through on the new lines.
        var opened = draft.ReviseLines(version, [], [], "SENTETIK", "SENTETIK yeniden ac", T0, Day);
        Assert.NotNull(opened);
        Assert.False(draft.HasPendingReinstatement);
        Assert.Equal((2, InternalApprovalState.Preparing), (draft.Revision, draft.ApprovalState));
        Assert.Equal([first.Id, second.Id], draft.Lines.Select(line => line.Id));
        Assert.Equal(replacement.LineId, Assert.Single(draft.RemovedLines).Id);
        draft.ReviseQuantities(draft.Version, draft.Lines.Select(line => new LineQuantityInput(line.Id, "7", 7m)).ToArray(), "SENTETIK", "r", T0);
        Assert.All(draft.Lines, line => Assert.Equal("7", line.QuantityText));
    }

    [Fact]
    public void An_empty_line_revision_is_a_no_change_unless_a_reinstatement_waits_and_a_plain_customer_rejection_waits_for_nothing()
    {
        var (plain, _, _) = Approved(acceptance: true);
        plain.RecordCustomerAcceptance(plain.Version, "SENTETIK-sales", T0);
        Assert.False(plain.HasPendingReinstatement);
        Assert.Equal(SalesRuleCodes.RevisionNoChange, CodeOf(() => plain.ReviseLines(plain.Version, [], [], "SENTETIK", "r", T0, Day)));

        // A customer-rejected revision that removed nothing has nothing to reinstate: the quantity revision is free.
        var (draft, _, _) = Approved(acceptance: true);
        draft.RecordCustomerAcceptance(draft.Version, "SENTETIK-sales", T0);
        draft.ReviseQuantities(draft.Version, draft.Lines.Select(line => new LineQuantityInput(line.Id, "11", 11m)).ToArray(), "SENTETIK", "r", T0);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Approve(draft.Version, "SENTETIK-approver", T0);
        draft.RecordCustomerRejection(draft.Version, "SENTETIK-sales", "SENTETIK", T0);
        Assert.False(draft.HasPendingReinstatement);
        draft.ReviseQuantities(draft.Version, draft.Lines.Select(line => new LineQuantityInput(line.Id, "12", 12m)).ToArray(), "SENTETIK", "r", T0);
        Assert.Equal(2, draft.Revision);
    }

    [Fact]
    public void A_never_approved_order_and_a_line_added_and_removed_in_the_rejected_revision_are_not_reverted()
    {
        // No approved revision to ship under: a Rev.0 draft's own removal stays.
        var draft = Draft();
        var first = draft.AddLine(draft.Version, Line(), T0);
        draft.AddLine(draft.Version, Line(ProductB, "20"), T0);
        draft.ReviseLines(draft.Version, [], [new LineRemovalInput(first.Id)], "SENTETIK", "r", T0, Day);
        draft.Submit(draft.Version, "SENTETIK-writer", T0);
        draft.Reject(draft.Version, "SENTETIK-approver", null, T0);
        Assert.Equal(first.Id, Assert.Single(draft.RemovedLines).Id);

        // A line Rev.1 added and removed again was never approved: it stays passive after the rejection.
        var (approved, _, _) = Approved();
        var added = Line(ProductB, "3");
        approved.ReviseLines(approved.Version, [added], [], "SENTETIK", "r", T0, Day);
        approved.ReviseLines(approved.Version, [], [new LineRemovalInput(added.LineId)], "SENTETIK", "r", T0, Day);
        approved.Submit(approved.Version, "SENTETIK-writer", T0);
        approved.Reject(approved.Version, "SENTETIK-approver", null, T0);
        Assert.Equal(added.LineId, Assert.Single(approved.RemovedLines).Id);
    }

    [Fact]
    public void A_revision_never_writes_more_lines_than_the_rebuild_takes_and_never_passivates_a_line_with_remainder_history()
    {
        // K3 review A P1-1: 2 lines + 499 added would be 501 — refused before anything changes; 498 (500 in all) rebuild fine.
        var (draft, first, _) = Approved();
        var version = draft.Version;
        Assert.Equal(SalesRuleCodes.TooManyLines, CodeOf(() => draft.ReviseLines(version, [.. Enumerable.Range(0, 499).Select(_ => Line(ProductB, "1"))],
            [], "SENTETIK", "r", T0, Day)));
        Assert.Equal((version, 2, 0), (draft.Version, draft.Lines.Count, draft.Revision));
        draft.ReviseLines(version, [.. Enumerable.Range(0, 498).Select(_ => Line(ProductB, "1"))], [new LineRemovalInput(first.Id)], "SENTETIK", "r", T0, Day);
        Assert.Equal((500, 499), (draft.AllLines.Count, draft.Lines.Count));
        Assert.Equal(SalesRuleCodes.TooManyLines, CodeOf(() => draft.ReviseLines(draft.Version, [Line(ProductB, "1")], [], "SENTETIK", "r", T0, Day)));

        // K3 review A P3-1: a line with a remainder-change request (it shipped) is never removed, whatever the owners answered.
        var (other, line, _) = Approved();
        other.RequestRemainderChange(other.Version, line.Id, RemainderChangeKind.Cancel, null, null, "SENTETIK",
            new RemainderFloor(0.5m, 0.5m, 0.5m), "SENTETIK-requester", T0);
        Assert.Equal(SalesRuleCodes.LineRevisionLineShipped,
            CodeOf(() => other.ReviseLines(other.Version, [Line(ProductB)], [new LineRemovalInput(line.Id)], "SENTETIK", "r", T0, Day)));
    }

    [Fact]
    public void An_added_lines_snapshots_are_taken_for_the_revision_day_not_the_orders_business_date()
    {
        // SAT-D212 (EK36): the order's business date is Day; the line is added two days later.
        var (draft, _, _) = Approved();
        var revisionDay = Day.AddDays(2);
        Assert.Equal(SalesRuleCodes.SnapshotDateMismatch,
            CodeOf(() => draft.ReviseLines(draft.Version, [Line(ProductB)], [], "SENTETIK", "r", T0, revisionDay)));

        var later = Line(ProductB, "4");
        later = later with { Product = later.Product with { EffectiveOn = revisionDay } };
        draft.ReviseLines(draft.Version, [later], [], "SENTETIK", "r", T0, revisionDay);
        Assert.Equal(revisionDay, draft.Lines.Single(line => line.Id == later.LineId).Product.EffectiveOn);
        // The existing lines keep their own snapshots (taken for the business date).
        Assert.All(draft.Lines.Where(line => line.Id != later.LineId), line => Assert.Equal(Day, line.Product.EffectiveOn));
    }

    [Fact]
    public void The_line_revision_migration_down_refuses_with_51142_before_it_drops_anything()
    {
        var migration = new AddDraftLineRevisions();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51142,", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[sales].[DraftLines] WHERE [AddedInRevision] IS NOT NULL OR [RemovedInRevision] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.All(migration.DownOperations.Skip(1), operation => Assert.IsNotType<SqlOperation>(operation));
        Assert.Equal(4, migration.UpOperations.OfType<AddColumnOperation>().Count(column => column.IsNullable));
        Assert.Contains("OR [ReinstatedInRevision] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
    }
}
