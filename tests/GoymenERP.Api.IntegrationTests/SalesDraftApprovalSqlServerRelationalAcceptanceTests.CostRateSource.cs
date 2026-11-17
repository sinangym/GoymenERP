using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesDraftApprovalSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_cost_rate_source_reads_actual_approved_order_identity_date_and_version_without_cross_company_or_write_effects()
    {
        await using var run = await Run.StartAsync();
        SalesOrderDraft draft;
        await using (var scope = run.Store(CompanyA))
        {
            draft = (await scope.Store.StoreDraftAsync(run.Envelope("rate-order"), Hash("rate-order"),
                number => Draft(Owners.For(CompanyA), number, T0))).Draft;
            await scope.Store.UpdateDraftStateAsync(run.Envelope("rate-submit"), Hash("rate-submit"), SalesDraftCommands.SubmitV1,
                draft.Id, (item, now) => item.Submit(2, run.Actor, now), Submitted);
            await scope.Store.UpdateDraftStateAsync(run.Envelope("rate-approve", actor: "SENTETIK-approver"), Hash("rate-approve"), SalesDraftCommands.ApproveV1,
                draft.Id, (item, now) => item.Approve(3, "SENTETIK-approver", now), Approved);
        }
        var before = await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id", ("@id", draft.Id));
        await using (var scope = run.Store(CompanyA))
        {
            var context = new CompanyContext(); context.Set(CompanyA);
            var reader = new SalesOrderCostRateSourceReadPortV1(scope.Store, context);
            var answer = await reader.GetAsync(CompanyA, draft.Id, draft.Lines[0].Id, 0);
            Assert.Equal(SalesOrderCostRateSourceOutcomeV1.Available, answer.Outcome);
            Assert.Equal(Day, answer.RateDay);
            Assert.Equal(4L, answer.OrderVersion);
            Assert.Equal(Currency, answer.OrderCurrencyId);
            Assert.Equal("TcmbForexSelling", answer.RateType);
            Assert.Equal(SalesOrderCostRateSourceOutcomeV1.Unavailable, (await reader.GetAsync(CompanyA, draft.Id, Guid.NewGuid(), 0)).Outcome);
        }
        await using (var scope = run.Store(CompanyB))
        {
            var context = new CompanyContext(); context.Set(CompanyB);
            var foreign = await new SalesOrderCostRateSourceReadPortV1(scope.Store, context).GetAsync(CompanyB, draft.Id, draft.Lines[0].Id, 0);
            Assert.Equal(SalesOrderCostRateSourceOutcomeV1.NotFound, foreign.Outcome);
            Assert.Null(foreign.RateDay);
        }
        Assert.Equal(before, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts WHERE DraftId=@id", ("@id", draft.Id)));
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
    }
}
