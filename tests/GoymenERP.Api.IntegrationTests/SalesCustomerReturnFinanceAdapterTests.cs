using GoymenERP.Api.Shell;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-S2 (API-11 v2, FNS-D44/FNS-D81): the host adapter from Sales' customer return port onto Finance's acceptance port v2 and its
/// installment read. Only a succeeded Finance outcome admits; a validation refusal carries Finance's first code; the acceptance
/// identity is the return document; the user's selection is passed through and an absent selection stays absent. SENTETIK.
/// </summary>
public sealed class SalesCustomerReturnFinanceAdapterTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid ReturnId = Guid.NewGuid();
    private static readonly Guid InvoiceId = Guid.NewGuid();
    private static readonly Guid OpenItemId = Guid.NewGuid();

    [Theory]
    [InlineData(CommandOutcome.Succeeded, SalesCustomerReturnFinanceOutcomeV1.Admitted)]
    [InlineData(CommandOutcome.ValidationFailed, SalesCustomerReturnFinanceOutcomeV1.Refused)]
    [InlineData(CommandOutcome.IdempotencyConflict, SalesCustomerReturnFinanceOutcomeV1.KeyConflict)]
    [InlineData(CommandOutcome.VersionConflict, SalesCustomerReturnFinanceOutcomeV1.Retry)]
    [InlineData(CommandOutcome.Denied, SalesCustomerReturnFinanceOutcomeV1.Denied)]
    [InlineData(CommandOutcome.Unavailable, SalesCustomerReturnFinanceOutcomeV1.Unavailable)]
    public void A_finance_acceptance_outcome_maps_onto_the_sales_answer_and_only_success_admits(CommandOutcome outcome,
        SalesCustomerReturnFinanceOutcomeV1 expected)
    {
        var answer = SalesCustomerReturnFinanceAdapterV1.Map(ReturnId,
            new FinanceInvoiceAcceptanceResultV2(outcome, null, [], null, false, "SENTETIK-corr", []));

        Assert.Equal((ReturnId, expected), (answer.CustomerReturnId, answer.Outcome));
        Assert.Equal(outcome == CommandOutcome.ValidationFailed ? "refused" : null, answer.ReasonCode);
    }

    [Fact]
    public void A_validation_refusal_carries_finances_first_code()
    {
        var answer = SalesCustomerReturnFinanceAdapterV1.Map(ReturnId, new FinanceInvoiceAcceptanceResultV2(
            CommandOutcome.ValidationFailed, null, [], null, false, "SENTETIK-corr",
            [new ValidationIssue("allocations", "finance_customer_return_allocation_sum_mismatch"), new ValidationIssue("amount", "other")]));

        Assert.Equal((SalesCustomerReturnFinanceOutcomeV1.Refused, "finance_customer_return_allocation_sum_mismatch"),
            (answer.Outcome, answer.ReasonCode));
    }

    [Fact]
    public void The_acceptance_is_the_return_document_and_carries_the_users_selection()
    {
        var acceptance = SalesCustomerReturnFinanceAdapterV1.ToAcceptance(Offer([new SalesCustomerReturnFinanceAllocationV1(OpenItemId, "12.00")]));

        Assert.Equal((ReturnId, FinanceInvoiceProducerV1.Sales, FinanceInvoiceAcceptanceKindV2.CustomerReturn, ReturnId, "3"),
            (acceptance.AcceptanceId, acceptance.Producer, acceptance.Kind, acceptance.DocumentId, acceptance.DocumentVersion));
        Assert.Equal((InvoiceId, "5", "12.00", (string?)null, new DateOnly(2026, 11, 16)),
            (acceptance.OriginalInvoiceId, acceptance.OriginalInvoiceVersion, acceptance.Amount, acceptance.FunctionalAmount,
                acceptance.BusinessDate));
        var allocation = Assert.Single(acceptance.Allocations!);
        Assert.Equal((OpenItemId, "12.00"), (allocation.OpenItemId, allocation.Amount));
    }

    [Fact]
    public void An_absent_selection_stays_absent_in_the_acceptance()
    {
        Assert.Null(SalesCustomerReturnFinanceAdapterV1.ToAcceptance(Offer(null)).Allocations);
    }

    [Fact]
    public void An_admitted_installment_prefill_is_carried_over_and_a_refused_one_has_no_amounts()
    {
        var admitted = SalesCustomerReturnFinanceAdapterV1.Map(new FinanceCustomerReturnInstallmentPrefillV2(CommandOutcome.Succeeded,
            [new FinanceCustomerReturnInstallmentV2(OpenItemId, new DateOnly(2026, 12, 1), "20.00", "20.00", "12.00")], true, "12.00",
            "0", []));

        Assert.Equal((SalesCustomerReturnFinanceOutcomeV1.Admitted, true, "12.00", "0"),
            (admitted.Outcome, admitted.SelectionAllowed, admitted.AppliedAmount, admitted.AdvanceAmount));
        var installment = Assert.Single(admitted.Installments);
        Assert.Equal((OpenItemId, new DateOnly(2026, 12, 1), "20.00", "20.00", "12.00"), (installment.OpenItemId, installment.DueOn,
            installment.OriginalAmount, installment.OutstandingAmount, installment.PrefillAmount));

        var refused = SalesCustomerReturnFinanceAdapterV1.Map(new FinanceCustomerReturnInstallmentPrefillV2(
            CommandOutcome.ValidationFailed, [new FinanceCustomerReturnInstallmentV2(OpenItemId, new DateOnly(2026, 12, 1), "20.00",
                "20.00", "12.00")], true, "12.00", "0", [new ValidationIssue("amount", "finance_customer_return_amount_exceeds")]));

        Assert.Equal((SalesCustomerReturnFinanceOutcomeV1.Refused, "finance_customer_return_amount_exceeds", false, (string?)null,
                (string?)null), (refused.Outcome, refused.ReasonCode, refused.SelectionAllowed, refused.AppliedAmount,
            refused.AdvanceAmount));
        Assert.Empty(refused.Installments);
    }

    private static SalesCustomerReturnFinanceOfferV1 Offer(IReadOnlyList<SalesCustomerReturnFinanceAllocationV1>? allocations) =>
        new(CompanyId, "SENTETIK-finalizer", "SENTETIK-corr", null, "SENTETIK-key", ReturnId, "3", InvoiceId, "5", Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "12.00", null, null, new DateOnly(2026, 11, 16), allocations);
}
