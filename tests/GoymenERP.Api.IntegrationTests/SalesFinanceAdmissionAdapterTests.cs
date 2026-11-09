using GoymenERP.Api.Shell;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

// API-36: the host adapter maps Finance's command outcome onto Sales' small answer; only a succeeded admission is "admitted".
public sealed class SalesFinanceAdmissionAdapterTests
{
    [Theory]
    [InlineData(CommandOutcome.Succeeded, SalesFinanceAdmissionOutcomeV1.Admitted)]
    [InlineData(CommandOutcome.Denied, SalesFinanceAdmissionOutcomeV1.Refused)]
    [InlineData(CommandOutcome.ValidationFailed, SalesFinanceAdmissionOutcomeV1.Refused)]
    [InlineData(CommandOutcome.IdempotencyConflict, SalesFinanceAdmissionOutcomeV1.KeyConflict)]
    [InlineData(CommandOutcome.VersionConflict, SalesFinanceAdmissionOutcomeV1.Retry)]
    [InlineData(CommandOutcome.Unavailable, SalesFinanceAdmissionOutcomeV1.Unavailable)]
    public void A_finance_outcome_maps_onto_the_sales_answer_and_only_success_admits(CommandOutcome outcome,
        SalesFinanceAdmissionOutcomeV1 expected)
    {
        var answer = SalesFinanceAdmissionAdapterV1.Map(new FinanceOpenItemAdmissionResult(outcome, [], false, "synthetic-correlation", []));

        Assert.Equal(expected, answer.Outcome);
    }
}
