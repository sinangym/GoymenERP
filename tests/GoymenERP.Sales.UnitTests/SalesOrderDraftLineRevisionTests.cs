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
        Assert.Equal(3, migration.UpOperations.OfType<AddColumnOperation>().Count(column => column.IsNullable));
    }
}
