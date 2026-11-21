using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D426 on real SQL: the sale rate frozen at internal approval is stored with the line's margin snapshot, read back exactly,
/// refused by the all-or-nothing check when partial, and protected by the forward-fix Down (52013).
/// </summary>
public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task SAT_D426_approval_sale_rate_is_stored_read_back_all_or_nothing_and_its_down_refuses_while_a_rate_exists()
    {
        await using var run = await Run.StartAsync();
        var foreign = Guid.Parse("7a000000-0000-0000-0000-0000000000d1");
        var rateDay = new DateOnly(2026, 11, 20);
        SalesOrderDraft draft;
        await using (var scope = run.Store(CompanyA))
        {
            draft = (await scope.Store.StoreDraftAsync(run.Envelope("rate-create"), Hash("rate-create"),
                number => Draft(Owners.For(CompanyA), number, T0))).Draft;
            await scope.Store.UpdateDraftStateAsync(run.Envelope("rate-submit"), Hash("rate-submit"), SalesDraftCommands.SubmitV1,
                draft.Id, (item, now) => item.Submit(2, run.Actor, now), Submitted);
            await scope.Store.UpdateDraftStateAsync(run.Envelope("rate-approve"), Hash("rate-approve"), SalesDraftCommands.ApproveV1,
                draft.Id, (item, now) =>
                {
                    item.Approve(3, run.Actor, now);
                    item.PinMarginCosts([MarginPin(item, now) with { SaleRate = new SalesMarginSaleRate(foreign, 34.56789012m, rateDay) }]);
                }, Approved);
        }

        await using (var scope = run.Store(CompanyA))
        {
            var pinned = Assert.Single((await scope.Store.FindDraftAsync(draft.Id))!.MarginCosts);
            Assert.Equal(new SalesMarginSaleRate(foreign, 34.56789012m, rateDay), pinned.SaleRate);
        }

        // A partial rate is refused by the store's own check, whatever writes it.
        var partial = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE sales.DraftMarginCosts SET SaleRateDay = NULL WHERE DraftId = @id", ("@id", draft.Id)));
        Assert.Equal(547, partial.Number);

        // Forward fix: the Down guard refuses while a frozen rate exists and leaves it in place.
        var guard = Assert.IsType<SqlOperation>(new AddSalesMarginSaleRates().DownOperations[0]).Sql;
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(52013, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sales.DraftMarginCosts WHERE DraftId = @id AND SaleRateCurrencyId IS NOT NULL", ("@id", draft.Id)));

        // Without any frozen rate the same guard passes (a TL-only store rolls back freely).
        await run.ExecuteAsync(
            "UPDATE sales.DraftMarginCosts SET SaleRateCurrencyId = NULL, SaleExchangeRate = NULL, SaleRateDay = NULL WHERE DraftId = @id",
            ("@id", draft.Id));
        await run.ExecuteAsync(guard);
    }
}
