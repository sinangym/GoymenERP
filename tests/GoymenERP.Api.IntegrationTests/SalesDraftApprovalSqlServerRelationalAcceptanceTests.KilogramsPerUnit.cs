using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D437 (AK-4) on real SQL: a pinned theoretical kg/unit row is a tenant child of its order line, held by its value check, and
/// protected by the forward-fix Down (52051); an empty store rolls back freely. SENTETIK.
/// </summary>
public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task SAT_D437_the_piece_kilograms_migrations_Down_passes_with_an_empty_store_and_refuses_while_a_pinned_figure_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesPieceLineKilograms().DownOperations[0]).Sql;
        SalesOrderDraft draft;
        await using (var scope = run.Store(CompanyA))
        {
            draft = (await scope.Store.StoreDraftAsync(run.Envelope("kpu-create"), Hash("kpu-create"),
                number => Draft(Owners.For(CompanyA), number, T0))).Draft;
        }

        // Only kg drafts in the store: the guard passes.
        await run.ExecuteAsync(guard);

        var line = draft.Lines[0];
        const string insert = "INSERT INTO sales.DraftLineKilogramsPerUnit (CompanyId, DraftId, LineId, Revision, [LineNo], ProductId, UnitId, "
            + "KilogramsPerUnit, SourceProof, RecordedAtUtc) VALUES (@company, @draft, @line, 0, 1, @product, @unit, @value, N'{}', SYSDATETIMEOFFSET())";
        // A non-positive figure is refused by the table's own check.
        var zero = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(insert, ("@company", CompanyA), ("@draft", draft.Id),
            ("@line", line.Id), ("@product", line.Product.ProductId), ("@unit", line.OrderUnit.UnitId), ("@value", 0m)));
        Assert.Equal(547, zero.Number);
        await run.ExecuteAsync(insert, ("@company", CompanyA), ("@draft", draft.Id), ("@line", line.Id),
            ("@product", line.Product.ProductId), ("@unit", line.OrderUnit.UnitId), ("@value", 12.345m));

        // Forward fix: the Down guard refuses while a pinned figure exists and leaves it in place.
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52051, refused.Number);
        Assert.Equal(12.345m, await run.ScalarAsync<decimal>(
            "SELECT KilogramsPerUnit FROM sales.DraftLineKilogramsPerUnit WHERE DraftId = @id", ("@id", draft.Id)));
    }

    /// <summary>
    /// SAT-D474 / SAT-D477 on real SQL: an approval that resolved a kg/m table conflict with the card's ENTERED direction pins that
    /// figure in the approval's one save; the reversed figure, the difference and "table must be corrected" stay in the stored
    /// source proof and in the approval's audit event, and a reload reads them back unchanged.
    /// </summary>
    [SqlServerFact]
    public async Task SAT_D477_an_approval_pins_the_entered_direction_figure_and_keeps_the_table_conflict_warning_in_its_audit()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var pieceUnit = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5");
        var pieceProfile = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e6");
        SalesOrderDraft draft;
        await using (var scope = run.Store(CompanyA))
        {
            draft = (await scope.Store.StoreDraftAsync(run.Envelope("kpu-conflict-create"), Hash("kpu-conflict-create"), number =>
            {
                var created = SalesOrderDraft.Create(Guid.NewGuid(), owners.Company, number,
                    new SalesDraftHeader(owners.Party, owners.Account, Currency, Day, DueTerms: TestDueTerms.PointDue30),
                    new MoneyProfileSnapshot(owners.Company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2), T0);
                var unit = new UnitProfileSnapshot(owners.Company, pieceUnit, "adet", "count", pieceProfile, "1", false, 0, 0, true, From, null);
                var binding = new UnitBindingSnapshot(owners.Company, pieceUnit, pieceProfile, "1");
                created.AddLine(created.Version, new SalesDraftLineInput(Guid.NewGuid(),
                    new ProductSnapshot(Product, "7", "PRF-4060", "Sentetik profil 40x60", Day, binding, binding, binding), unit, "10", 10m), T0);
                return created;
            })).Draft;
            await scope.Store.UpdateDraftStateAsync(run.Envelope("kpu-conflict-submit"), Hash("kpu-conflict-submit"), SalesDraftCommands.SubmitV1,
                draft.Id, (d, now) => d.Submit(2, run.Actor, now), Submitted);
        }

        const string proof = "{\"contractVersion\":1,\"unitKind\":\"Piece\",\"weightTableVersion\":11,\"weightTableSource\":\"SENTETIK-tablo\","
            + "\"tableConflict\":{\"selection\":\"weight-table:entered-orientation\",\"selectedKilogramsPerUnit\":\"21\","
            + "\"otherKilogramsPerUnit\":\"21.6\",\"differenceKilogramsPerUnit\":\"0.6\",\"selectedKgPerMetre\":\"3.5\","
            + "\"otherKgPerMetre\":\"3.6\",\"tableCorrectionRequired\":true}}";
        var lineId = draft.Lines[0].Id;
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.UpdateDraftStateAsync(run.Envelope("kpu-conflict-approve", actor: "SENTETIK-approver"), Hash("kpu-conflict-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) =>
                {
                    d.Approve(3, "SENTETIK-approver", now);
                    d.PinKilogramsPerUnit([new SalesLineKilogramsPerUnitSnapshot(lineId, d.Revision, Product, pieceUnit, 21m, proof,
                        d.Approval.DecidedAtUtc!.Value)]);
                }, Approved);
        }

        Assert.Equal(21m, await run.ScalarAsync<decimal>(
            "SELECT KilogramsPerUnit FROM sales.DraftLineKilogramsPerUnit WHERE DraftId = @id AND LineId = @line", ("@id", draft.Id), ("@line", lineId)));
        Assert.Equal("weight-table:entered-orientation", await run.ScalarAsync<string>(
            "SELECT JSON_VALUE(SourceProof, '$.tableConflict.selection') FROM sales.DraftLineKilogramsPerUnit WHERE DraftId = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'sales.order-draft.approved.v1' "
            + "AND AfterJson LIKE N'%otherKilogramsPerUnit%21.6%' AND AfterJson LIKE N'%tableCorrectionRequired%'", ("@id", draft.Id.ToString("D"))));
        await using (var scope = run.Store(CompanyA))
        {
            var reloaded = (await scope.Store.FindDraftAsync(draft.Id))!;
            var pinned = Assert.Single(reloaded.KilogramsPerUnitSnapshots);
            Assert.Equal((21m, proof), (reloaded.KilogramsPerUnitOf(lineId)!.KilogramsPerUnit, pinned.SourceProof));
        }
    }
}
