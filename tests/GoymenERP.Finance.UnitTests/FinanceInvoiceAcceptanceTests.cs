using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// F-019 / API-11 v2 (SAT-D288, SAT-D291, SAT-D294): the pure rules of the customer return acceptance — only a Sales
/// customer return is accepted, identities, tokens and exact amounts are required — the split of the return into the
/// reduction of the original invoice's open items and the customer's advance, the partial return over several outstanding
/// installments (FIN-S10: the user's selection applied exactly and never replaced by the prefill, FNS-D44; nearest due first
/// without one, FNS-D54; the prefill from the same calculator), and the stored model (one credit per return document,
/// append-only reductions, one concurrency row per original invoice, a forward-fix Down). SENTETIK values only.
/// </summary>
public sealed class FinanceInvoiceAcceptanceTests
{
    private static readonly Guid Party = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Account = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Currency = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void A_sales_customer_return_with_complete_identities_is_valid()
    {
        Assert.Empty(FinanceInvoiceAcceptanceService.Validate(Acceptance()));
    }

    [Fact]
    public void A_purchasing_producer_or_an_unknown_kind_is_unsupported()
    {
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(
                Acceptance() with { Producer = FinanceInvoiceProducerV1.Purchasing }),
            issue => issue.Code == FinanceInvoiceAcceptanceService.KindUnsupportedCode);
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(
                Acceptance() with { Kind = (FinanceInvoiceAcceptanceKindV2)9 }),
            issue => issue.Code == FinanceInvoiceAcceptanceService.KindUnsupportedCode);
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(
                Acceptance() with { Producer = (FinanceInvoiceProducerV1)9 }),
            issue => issue.Code == "finance_invoice_producer_invalid");
    }

    [Fact]
    public void The_original_invoice_link_and_the_identities_are_required()
    {
        var acceptance = Acceptance();

        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(acceptance with { OriginalInvoiceId = Guid.Empty }),
            issue => issue.Field == "acceptance" && issue.Code == "identity_required");
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(acceptance with { AcceptanceId = Guid.Empty }),
            issue => issue.Field == "acceptance" && issue.Code == "identity_required");
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(
                acceptance with { DocumentId = acceptance.OriginalInvoiceId }),
            issue => issue.Field == "acceptance" && issue.Code == "identity_required");
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(acceptance with { CurrencyId = Guid.Empty }),
            issue => issue.Field == "party" && issue.Code == "identity_required");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" v1")]
    [InlineData("v 1")]
    public void Both_versions_are_tokens(string version)
    {
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(Acceptance() with { DocumentVersion = version }),
            issue => issue.Field == "documentVersion" && issue.Code == "finance_invoice_version_invalid");
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(Acceptance() with { OriginalInvoiceVersion = version }),
            issue => issue.Field == "originalInvoiceVersion" && issue.Code == "finance_invoice_version_invalid");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,5")]
    [InlineData(" 10")]
    [InlineData("abc")]
    public void The_return_amount_is_positive_exact_text(string amount)
    {
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(Acceptance() with { Amount = amount }),
            issue => issue.Code == "finance_customer_return_amount_invalid");
    }

    [Fact]
    public void A_functional_amount_when_present_and_the_business_date_are_checked()
    {
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(Acceptance() with { FunctionalAmount = "0" }),
            issue => issue.Code == "finance_invoice_functional_amount_invalid");
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(Acceptance() with { BusinessDate = default }),
            issue => issue.Code == "finance_customer_return_business_date_invalid");
    }

    [Fact]
    public void A_return_within_the_only_outstanding_item_reduces_that_item_and_leaves_no_advance()
    {
        var item = Item("100.00", "100.00");

        var plan = FinanceInvoiceAcceptanceService.Plan([item], "40.00");

        Assert.Null(plan.Issue);
        var reduction = Assert.Single(plan.Reductions);
        Assert.Same(item, reduction.Item);
        Assert.True(FinanceExactDecimalText.Equal("60.00", reduction.After.Text));
        Assert.True(FinanceExactDecimalText.Equal("40.00", reduction.Reduced.Text));
        Assert.True(FinanceExactDecimalText.Equal("40.00", plan.Applied.Text));
        Assert.True(plan.Advance.IsZero);
        Assert.True(FinanceExactDecimalText.Equal("100.00", plan.InvoiceTotal.Text));
    }

    [Fact]
    public void A_return_above_the_outstanding_total_zeroes_every_item_and_the_rest_is_the_advance()
    {
        var first = Item("60.00", "20.00");
        var second = Item("40.00", "40.00");
        var paid = Item("50.00", "0", FinanceOpenItemStatuses.Closed);

        var plan = FinanceInvoiceAcceptanceService.Plan([first, second, paid], "75.00");

        Assert.Null(plan.Issue);
        Assert.Equal(2, plan.Reductions.Count);
        Assert.All(plan.Reductions, reduction => Assert.True(reduction.After.IsZero));
        Assert.True(FinanceExactDecimalText.Equal("20.00", plan.Reductions.Single(r => r.Item == first).Reduced.Text));
        Assert.True(FinanceExactDecimalText.Equal("40.00", plan.Reductions.Single(r => r.Item == second).Reduced.Text));
        Assert.True(FinanceExactDecimalText.Equal("60.00", plan.Applied.Text));
        Assert.True(FinanceExactDecimalText.Equal("15.00", plan.Advance.Text));
        Assert.True(FinanceExactDecimalText.Equal("150.00", plan.InvoiceTotal.Text));
    }

    [Fact]
    public void A_return_equal_to_the_outstanding_total_closes_every_item_without_an_advance()
    {
        var plan = FinanceInvoiceAcceptanceService.Plan([Item("60.00", "20.00"), Item("40.00", "40.00")], "60");

        Assert.Null(plan.Issue);
        Assert.Equal(2, plan.Reductions.Count);
        Assert.True(plan.Advance.IsZero);
    }

    [Fact]
    public void A_return_of_a_fully_paid_invoice_is_wholly_the_customers_advance()
    {
        var plan = FinanceInvoiceAcceptanceService.Plan(
            [Item("60.00", "0", FinanceOpenItemStatuses.Closed), Item("40.00", "0", FinanceOpenItemStatuses.Closed)],
            "25.00");

        Assert.Null(plan.Issue);
        Assert.Empty(plan.Reductions);
        Assert.True(plan.Applied.IsZero);
        Assert.True(FinanceExactDecimalText.Equal("25.00", plan.Advance.Text));
    }

    [Fact]
    public void A_partial_return_without_a_selection_over_several_installments_is_refused_not_split_by_due_date()
    {
        // FNS-D81: the user chooses the installment; the nearest-due order of FNS-D54 is not applied on its own.
        var later = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));
        var nearest = Item("60.00", "30.00", dueOn: new DateOnly(2026, 12, 1));
        var last = Item("50.00", "50.00", dueOn: new DateOnly(2027, 2, 1));

        var plan = FinanceInvoiceAcceptanceService.Plan([later, nearest, last], "50.00");

        Assert.Equal(("allocations", FinanceInvoiceAcceptanceService.AllocationRequiredCode),
            (plan.Issue?.Field, plan.Issue?.Code));
        Assert.Equal(FinanceCustomerReturnPlanBasis.None, plan.Basis);
        Assert.Empty(plan.Reductions);
    }

    [Fact]
    public void Installments_due_on_the_same_day_without_a_selection_are_refused_in_either_stored_order()
    {
        var day = new DateOnly(2026, 12, 1);
        var a = Item("40.00", "40.00", dueOn: day);
        var b = Item("40.00", "40.00", dueOn: day);

        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationRequiredCode,
            FinanceInvoiceAcceptanceService.Plan([a, b], "10.00").Issue?.Code);
        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationRequiredCode,
            FinanceInvoiceAcceptanceService.Plan([b, a], "10.00").Issue?.Code);
    }

    [Fact]
    public void A_partial_return_without_a_selection_over_the_only_outstanding_installment_reduces_it()
    {
        var paid = Item("60.00", "0", FinanceOpenItemStatuses.Closed, new DateOnly(2026, 11, 1));
        var open = Item("40.00", "40.00", dueOn: new DateOnly(2026, 12, 1));

        var plan = FinanceInvoiceAcceptanceService.Plan([paid, open], "15.00");

        Assert.Null(plan.Issue);
        Assert.Equal(FinanceCustomerReturnPlanBasis.SingleOutstanding, plan.Basis);
        var reduction = Assert.Single(plan.Reductions);
        Assert.Same(open, reduction.Item);
        Assert.True(FinanceExactDecimalText.Equal("15.00", reduction.Reduced.Text));
        Assert.True(FinanceExactDecimalText.Equal("25.00", reduction.After.Text));
        Assert.True(FinanceExactDecimalText.Equal("15.00", plan.Applied.Text));
        Assert.True(plan.Advance.IsZero);
        Assert.True(FinanceExactDecimalText.Equal("100.00", plan.InvoiceTotal.Text));
    }

    [Fact]
    public void A_partial_return_without_a_selection_is_refused_even_when_an_installment_has_no_due_date()
    {
        var plan = FinanceInvoiceAcceptanceService.Plan(
            [Item("60.00", "60.00", dueOn: new DateOnly(2026, 12, 1)), Item("40.00", "40.00")], "50.00");

        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationRequiredCode, plan.Issue?.Code);
        Assert.Empty(plan.Reductions);
    }

    [Fact]
    public void A_partial_return_with_the_users_selection_reduces_exactly_the_selected_parts_not_the_prefill()
    {
        var nearest = Item("60.00", "60.00", dueOn: new DateOnly(2026, 12, 1));
        var later = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));

        // Nearest due first would take the whole 50.00 from the first installment; the user chose otherwise.
        var plan = FinanceInvoiceAcceptanceService.Plan([nearest, later], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(later.Id, "40.00"), new FinanceInvoiceAcceptanceAllocationV2(nearest.Id, "10")]);

        Assert.Null(plan.Issue);
        Assert.Equal(FinanceCustomerReturnPlanBasis.UserSelection, plan.Basis);
        Assert.Equal([nearest, later], plan.Reductions.Select(r => r.Item).ToArray());
        // A part written as "10" is stored at the return amount's scale.
        Assert.Equal("10.00", plan.Reductions[0].Reduced.Text);
        Assert.True(FinanceExactDecimalText.Equal("50.00", plan.Reductions[0].After.Text));
        Assert.True(FinanceExactDecimalText.Equal("40.00", plan.Reductions[1].Reduced.Text));
        Assert.True(plan.Reductions[1].After.IsZero);
        Assert.True(FinanceExactDecimalText.Equal("50.00", plan.Applied.Text));
        Assert.True(plan.Advance.IsZero);
    }

    [Fact]
    public void A_selection_must_total_the_return_amount_to_the_kurus()
    {
        var first = Item("60.00", "60.00", dueOn: new DateOnly(2026, 12, 1));
        var second = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));

        var short_ = FinanceInvoiceAcceptanceService.Plan([first, second], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(first.Id, "30.00"), new FinanceInvoiceAcceptanceAllocationV2(second.Id, "19.99")]);
        var over = FinanceInvoiceAcceptanceService.Plan([first, second], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(first.Id, "30.00"), new FinanceInvoiceAcceptanceAllocationV2(second.Id, "20.01")]);

        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationTotalMismatchCode, short_.Issue?.Code);
        Assert.Empty(short_.Reductions);
        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationTotalMismatchCode, over.Issue?.Code);
        Assert.Empty(over.Reductions);
    }

    [Fact]
    public void A_selected_part_above_its_installments_open_amount_is_refused()
    {
        var first = Item("60.00", "20.00", dueOn: new DateOnly(2026, 12, 1));
        var second = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));

        var plan = FinanceInvoiceAcceptanceService.Plan([first, second], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(first.Id, "20.01"), new FinanceInvoiceAcceptanceAllocationV2(second.Id, "29.99")]);

        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationExceedsOpenAmountCode, plan.Issue?.Code);
        Assert.Empty(plan.Reductions);
    }

    [Fact]
    public void A_selected_item_that_is_not_an_outstanding_item_of_the_invoice_is_refused()
    {
        var open = Item("60.00", "60.00", dueOn: new DateOnly(2026, 12, 1));
        var paid = Item("40.00", "0", FinanceOpenItemStatuses.Closed, new DateOnly(2027, 1, 1));
        var other = Item("80.00", "80.00", dueOn: new DateOnly(2027, 2, 1));
        var outstanding = Item("50.00", "50.00", dueOn: new DateOnly(2027, 3, 1));

        var paidPlan = FinanceInvoiceAcceptanceService.Plan([open, paid, outstanding], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(open.Id, "40.00"), new FinanceInvoiceAcceptanceAllocationV2(paid.Id, "10.00")]);
        var otherPlan = FinanceInvoiceAcceptanceService.Plan([open, paid, outstanding], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(open.Id, "40.00"), new FinanceInvoiceAcceptanceAllocationV2(other.Id, "10.00")]);

        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationItemUnknownCode, paidPlan.Issue?.Code);
        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationItemUnknownCode, otherPlan.Issue?.Code);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1.00")]
    [InlineData("10.001")]
    [InlineData("abc")]
    public void A_selected_part_that_is_not_a_positive_amount_at_the_returns_scale_is_refused(string part)
    {
        var first = Item("60.00", "60.00", dueOn: new DateOnly(2026, 12, 1));
        var second = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));

        var plan = FinanceInvoiceAcceptanceService.Plan([first, second], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(first.Id, part), new FinanceInvoiceAcceptanceAllocationV2(second.Id, "40.00")]);

        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationInvalidCode, plan.Issue?.Code);
        Assert.Empty(plan.Reductions);
    }

    [Fact]
    public void A_selected_part_with_only_trailing_zeros_below_the_kurus_is_the_same_amount()
    {
        var first = Item("60.00", "60.00", dueOn: new DateOnly(2026, 12, 1));
        var second = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));

        var plan = FinanceInvoiceAcceptanceService.Plan([first, second], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(first.Id, "10.000"), new FinanceInvoiceAcceptanceAllocationV2(second.Id, "40.00")]);

        Assert.Null(plan.Issue);
        Assert.Equal("10.00", plan.Reductions[0].Reduced.Text);
    }

    [Fact]
    public void An_installment_selected_twice_is_refused()
    {
        var first = Item("60.00", "60.00", dueOn: new DateOnly(2026, 12, 1));
        var second = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));

        var plan = FinanceInvoiceAcceptanceService.Plan([first, second], "50.00",
            [new FinanceInvoiceAcceptanceAllocationV2(first.Id, "25.00"), new FinanceInvoiceAcceptanceAllocationV2(first.Id, "25.00")]);

        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationInvalidCode, plan.Issue?.Code);
    }

    [Fact]
    public void A_return_covering_the_whole_outstanding_amount_takes_no_selection()
    {
        var first = Item("60.00", "20.00", dueOn: new DateOnly(2026, 12, 1));
        var second = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));

        var plan = FinanceInvoiceAcceptanceService.Plan([first, second], "60.00",
            [new FinanceInvoiceAcceptanceAllocationV2(first.Id, "20.00"), new FinanceInvoiceAcceptanceAllocationV2(second.Id, "40.00")]);

        Assert.Equal(FinanceInvoiceAcceptanceService.AllocationNotApplicableCode, plan.Issue?.Code);
        Assert.Empty(plan.Reductions);
    }

    [Fact]
    public void The_prefill_lists_every_outstanding_installment_nearest_due_first_and_confirmed_it_is_the_plan()
    {
        var later = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));
        var nearest = Item("60.00", "30.00", dueOn: new DateOnly(2026, 12, 1));
        var last = Item("50.00", "50.00", dueOn: new DateOnly(2027, 2, 1));
        var paid = Item("10.00", "0", FinanceOpenItemStatuses.Closed, new DateOnly(2026, 11, 1));
        FinanceOpenItemRow[] items = [later, nearest, last, paid];

        var prefill = FinanceCustomerReturnInstallmentCalculator.Prefill(items, "50.00");
        // FNS-D81: the prefill is only a suggestion; the user confirms it by submitting it as the selection.
        var confirmed = prefill.Lines
            .Where(line => !line.Prefill.IsZero)
            .Select(line => new FinanceInvoiceAcceptanceAllocationV2(line.Item.Id, line.Prefill.Text))
            .ToArray();
        var plan = FinanceInvoiceAcceptanceService.Plan(items, "50.00", confirmed);

        Assert.Null(prefill.Issue);
        Assert.True(prefill.SelectionAllowed);
        Assert.Equal([nearest, later, last], prefill.Lines.Select(line => line.Item).ToArray());
        Assert.Equal([new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 1)],
            prefill.Lines.Select(line => line.DueOn).ToArray());
        Assert.True(FinanceExactDecimalText.Equal("30.00", prefill.Lines[0].Prefill.Text));
        Assert.True(FinanceExactDecimalText.Equal("20.00", prefill.Lines[1].Prefill.Text));
        Assert.True(prefill.Lines[2].Prefill.IsZero);
        Assert.True(FinanceExactDecimalText.Equal("30.00", prefill.Lines[0].Outstanding.Text));
        Assert.True(FinanceExactDecimalText.Equal("60.00", prefill.Lines[0].Original.Text));
        Assert.True(FinanceExactDecimalText.Equal("50.00", prefill.Applied.Text));
        Assert.True(prefill.Advance.IsZero);
        Assert.Equal(FinanceCustomerReturnPlanBasis.UserSelection, plan.Basis);
        // FNS-D81 fixes the selected amounts, not the stored order of the invoice rows.
        // The prefill above separately verifies the nearest-due display order.
        Assert.Equal(
            prefill.Lines.Where(line => !line.Prefill.IsZero).Select(line => (line.Item, line.Prefill.Text))
                .OrderBy(line => line.Item.Id),
            plan.Reductions.Select(r => (r.Item, r.Reduced.Text)).OrderBy(line => line.Item.Id));
    }

    [Fact]
    public void The_prefill_of_a_whole_return_closes_every_installment_shows_the_advance_and_allows_no_selection()
    {
        var first = Item("60.00", "20.00", dueOn: new DateOnly(2026, 12, 1));
        var second = Item("40.00", "40.00", dueOn: new DateOnly(2027, 1, 1));

        var prefill = FinanceCustomerReturnInstallmentCalculator.Prefill([second, first], "75.00");

        Assert.Null(prefill.Issue);
        Assert.False(prefill.SelectionAllowed);
        Assert.Equal([first, second], prefill.Lines.Select(line => line.Item).ToArray());
        Assert.All(prefill.Lines, line => Assert.Equal(0, line.Prefill.CompareTo(line.Outstanding)));
        Assert.True(FinanceExactDecimalText.Equal("60.00", prefill.Applied.Text));
        Assert.True(FinanceExactDecimalText.Equal("15.00", prefill.Advance.Text));
    }

    [Fact]
    public void The_prefill_refuses_an_invalid_amount_and_a_stored_state_it_does_not_understand()
    {
        Assert.Equal("finance_customer_return_amount_invalid",
            FinanceCustomerReturnInstallmentCalculator.Prefill([Item("60.00", "60.00", dueOn: new DateOnly(2026, 12, 1))], "0")
                .Issue?.Code);
        Assert.Equal(FinanceInvoiceAcceptanceService.StoredStateNotUnderstoodCode,
            FinanceCustomerReturnInstallmentCalculator.Prefill([Item("60.00", "60.00")], "10.00").Issue?.Code);
        Assert.Equal(FinanceInvoiceAcceptanceService.StoredStateNotUnderstoodCode,
            FinanceCustomerReturnInstallmentCalculator.Prefill(
                [Item("60.00", "70.00", dueOn: new DateOnly(2026, 12, 1))], "10.00").Issue?.Code);
    }

    [Fact]
    public void The_selection_shape_is_checked_before_any_read()
    {
        var item = Guid.NewGuid();

        Assert.Empty(FinanceInvoiceAcceptanceService.Validate(
            Acceptance() with { Allocations = [new FinanceInvoiceAcceptanceAllocationV2(item, "40.00")] }));
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(Acceptance() with { Allocations = [] }),
            issue => issue.Field == "allocations" && issue.Code == FinanceInvoiceAcceptanceService.AllocationInvalidCode);
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(
                Acceptance() with { Allocations = [new FinanceInvoiceAcceptanceAllocationV2(Guid.Empty, "40.00")] }),
            issue => issue.Code == FinanceInvoiceAcceptanceService.AllocationInvalidCode);
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(
                Acceptance() with { Allocations = [new FinanceInvoiceAcceptanceAllocationV2(item, "0")] }),
            issue => issue.Code == FinanceInvoiceAcceptanceService.AllocationInvalidCode);
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(
                Acceptance() with
                {
                    Allocations =
                    [
                        new FinanceInvoiceAcceptanceAllocationV2(item, "20.00"),
                        new FinanceInvoiceAcceptanceAllocationV2(item, "20.00"),
                    ],
                }),
            issue => issue.Code == FinanceInvoiceAcceptanceService.AllocationInvalidCode);
        Assert.Contains(FinanceInvoiceAcceptanceService.Validate(Acceptance() with { Allocations = [null!] }),
            issue => issue.Code == FinanceInvoiceAcceptanceService.AllocationInvalidCode);
    }

    [Fact]
    public void An_acceptance_without_a_selection_serialises_as_before()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Acceptance(), FinanceJson.CreateOptions());

        Assert.DoesNotContain("llocations", json, StringComparison.Ordinal);
        Assert.Contains("llocations", System.Text.Json.JsonSerializer.Serialize(
            Acceptance() with { Allocations = [new FinanceInvoiceAcceptanceAllocationV2(Guid.NewGuid(), "40.00")] },
            FinanceJson.CreateOptions()), StringComparison.Ordinal);
    }

    [Fact]
    public void The_prefill_question_has_the_acceptances_shape_rules()
    {
        var query = new FinanceCustomerReturnInstallmentQueryV2(FinanceInvoiceProducerV1.Sales,
            FinanceInvoiceAcceptanceKindV2.CustomerReturn, Guid.Parse("55555555-5555-4555-8555-555555555555"),
            Guid.Parse("66666666-6666-4666-8666-666666666666"), "v2", "40.00");

        Assert.Empty(FinanceInvoiceAcceptanceService.ValidateQuery(query));
        Assert.Contains(FinanceInvoiceAcceptanceService.ValidateQuery(query with { Producer = FinanceInvoiceProducerV1.Purchasing }),
            issue => issue.Code == FinanceInvoiceAcceptanceService.KindUnsupportedCode);
        Assert.Contains(FinanceInvoiceAcceptanceService.ValidateQuery(query with { OriginalInvoiceId = query.DocumentId }),
            issue => issue.Code == "identity_required");
        Assert.Contains(FinanceInvoiceAcceptanceService.ValidateQuery(query with { OriginalInvoiceVersion = " v2" }),
            issue => issue.Code == "finance_invoice_version_invalid");
        Assert.Contains(FinanceInvoiceAcceptanceService.ValidateQuery(query with { Amount = "-1" }),
            issue => issue.Code == "finance_customer_return_amount_invalid");
    }

    [Theory]
    [InlineData("100.00", "120.00", FinanceOpenItemStatuses.Open)]
    [InlineData("100.00", "0", FinanceOpenItemStatuses.Open)]
    [InlineData("100.00", "10.00", FinanceOpenItemStatuses.Closed)]
    [InlineData("100.00", "100.00", FinanceOpenItemStatuses.Cancelled)]
    [InlineData("x", "10.00", FinanceOpenItemStatuses.Open)]
    [InlineData("100.00", "-1", FinanceOpenItemStatuses.Open)]
    public void A_stored_item_that_does_not_read_back_is_refused_not_guessed(string original, string outstanding,
        int status)
    {
        var plan = FinanceInvoiceAcceptanceService.Plan([Item(original, outstanding, status)], "10.00");

        Assert.Equal(FinanceInvoiceAcceptanceService.StoredStateNotUnderstoodCode, plan.Issue?.Code);
    }

    [Fact]
    public void A_payable_item_is_not_understood_as_a_customer_invoice()
    {
        var item = Item("100.00", "100.00");
        item.Direction = FinanceOpenItemDirectionCodes.Payable;

        Assert.Equal(FinanceInvoiceAcceptanceService.StoredStateNotUnderstoodCode,
            FinanceInvoiceAcceptanceService.Plan([item], "10.00").Issue?.Code);
    }

    [Fact]
    public void The_report_must_name_the_invoices_own_party_account_and_currency()
    {
        var items = new[] { Item("100.00", "100.00") };

        Assert.Null(FinanceInvoiceAcceptanceService.Mismatch(items, Acceptance()));
        Assert.Equal(FinanceInvoiceAcceptanceService.PartyMismatchCode,
            FinanceInvoiceAcceptanceService.Mismatch(items, Acceptance() with { PartyId = Guid.NewGuid() })?.Code);
        Assert.Equal(FinanceInvoiceAcceptanceService.AccountMismatchCode,
            FinanceInvoiceAcceptanceService.Mismatch(items, Acceptance() with { CompanyAccountId = Guid.NewGuid() })?.Code);
        Assert.Equal(FinanceInvoiceAcceptanceService.CurrencyMismatchCode,
            FinanceInvoiceAcceptanceService.Mismatch(items, Acceptance() with { CurrencyId = Guid.NewGuid() })?.Code);
    }

    [Fact]
    public void The_functional_amount_is_present_exactly_when_the_invoice_is_foreign()
    {
        var local = new[] { Item("100.00", "100.00") };
        var foreign = Item("100.00", "100.00");
        foreign.ExchangeRateText = "35.5";
        foreign.FunctionalAmountText = "3550.00";

        Assert.Equal(FinanceInvoiceAcceptanceService.FunctionalAmountMismatchCode,
            FinanceInvoiceAcceptanceService.Mismatch(local, Acceptance() with { FunctionalAmount = "10.00" })?.Code);
        Assert.Equal(FinanceInvoiceAcceptanceService.FunctionalAmountMismatchCode,
            FinanceInvoiceAcceptanceService.Mismatch([foreign], Acceptance())?.Code);
        Assert.Null(FinanceInvoiceAcceptanceService.Mismatch([foreign], Acceptance() with { FunctionalAmount = "355.00" }));
        Assert.Equal(FinanceInvoiceAcceptanceService.StoredStateNotUnderstoodCode,
            FinanceInvoiceAcceptanceService.Mismatch([local[0], foreign], Acceptance())?.Code);
    }

    [Fact]
    public void The_stored_model_keeps_one_credit_per_return_document_and_one_state_per_original_invoice()
    {
        using var dbContext = Context();
        var model = dbContext.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;

        var credit = model.FindEntityType(typeof(FinanceCustomerReturnCreditRow))!;
        Assert.Equal("CustomerReturnCredits", credit.GetTableName());
        Assert.Contains(credit.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(FinanceCustomerReturnCreditRow.CompanyId), nameof(FinanceCustomerReturnCreditRow.IdempotencyKeyHash)]));
        Assert.Contains(credit.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(FinanceCustomerReturnCreditRow.CompanyId), nameof(FinanceCustomerReturnCreditRow.SourceKind),
                    nameof(FinanceCustomerReturnCreditRow.DocumentId)]));

        var item = model.FindEntityType(typeof(FinanceCustomerReturnCreditItemRow))!;
        Assert.Equal("CustomerReturnCreditItems", item.GetTableName());
        Assert.Equal(
            [nameof(FinanceCustomerReturnCreditItemRow.CompanyId), nameof(FinanceCustomerReturnCreditItemRow.CreditId),
                nameof(FinanceCustomerReturnCreditItemRow.OpenItemId)],
            item.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(item.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(FinanceOpenItemRow));

        var state = model.FindEntityType(typeof(FinanceCustomerReturnInvoiceStateRow))!;
        Assert.Equal("CustomerReturnInvoiceStates", state.GetTableName());
        Assert.True(state.FindProperty(nameof(FinanceCustomerReturnInvoiceStateRow.Version))!.IsConcurrencyToken);
        Assert.Equal(
            [nameof(FinanceCustomerReturnInvoiceStateRow.CompanyId),
                nameof(FinanceCustomerReturnInvoiceStateRow.OriginalInvoiceId)],
            state.FindPrimaryKey()!.Properties.Select(property => property.Name));

        Assert.All(new[] { credit, item, state }, entity => Assert.Single(entity.GetDeclaredQueryFilters()));
    }

    [Fact]
    public void The_migration_down_refuses_first_while_a_return_is_stored()
    {
        var migration = new AddCustomerReturnCredits();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[CustomerReturnCredits]", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[CustomerReturnInvoiceStates]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.DownOperations,
            operation => operation is DropTableOperation { Name: "CustomerReturnCredits" });
    }

    [Fact]
    public void An_invoice_return_state_may_change_only_its_returned_total_and_update_stamp()
    {
        using var context = Context();
        var state = new FinanceCustomerReturnInvoiceStateRow
        {
            CompanyId = TestCompany,
            OriginalInvoiceId = Guid.NewGuid(),
            ReturnedAmountText = "10.00",
            Version = 1,
            UpdatedBy = "SENTETIK-actor",
        };
        context.Attach(state);
        state.OriginalInvoiceId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_reversing_credit_and_its_reductions_are_append_only()
    {
        using var context = Context();
        var credit = new FinanceCustomerReturnCreditRow
        {
            Id = Guid.NewGuid(),
            CompanyId = TestCompany,
            SourceKind = FinanceCustomerReturnCreditRules.SourceKind,
            AmountText = "10.00",
        };
        context.Attach(credit);
        credit.AmountText = "11.00";
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        context.ChangeTracker.Clear();
        var reduction = new FinanceCustomerReturnCreditItemRow
        {
            CompanyId = TestCompany,
            CreditId = Guid.NewGuid(),
            OpenItemId = Guid.NewGuid(),
            ReducedAmountText = "10.00",
        };
        context.Attach(reduction);
        reduction.ReducedAmountText = "1.00";
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_return_of_a_wholly_transferred_item_reduces_the_successors_assignment_so_the_positions_still_read()
    {
        var item = Item("100.00", "100.00");
        var assignment = Assignment(item, "100.00");
        var plan = FinanceInvoiceAcceptanceService.Plan([item], "30.00");

        var transferred = FinanceInvoiceAcceptanceService.TransferredReductions(plan, [assignment]);

        Assert.Null(transferred.Issue);
        var reduction = Assert.Single(transferred.Reductions);
        Assert.Same(assignment, reduction.Assignment);
        Assert.True(FinanceExactDecimalText.Equal("30.00", reduction.Reduced.Text));
        // Without following the transfer the item would hold less than its successor's assignment and stop reading.
        Assert.Throws<InvalidOperationException>(() => new FinanceAllocationPositions(Party, Account, Currency,
            plan.Reductions[0].After.Text, [assignment]).All());
        assignment.AvailableAmountText = FinanceAllocationPositions.Amount(assignment.AvailableAmountText)
            .Subtract(reduction.Reduced).Text;
        var positions = new FinanceAllocationPositions(Party, Account, Currency, plan.Reductions[0].After.Text,
            [assignment]).All();
        Assert.True(FinanceAllocationPositions.Amount(positions[0].AvailableAmount).IsZero);
        Assert.True(FinanceExactDecimalText.Equal("70.00", positions[1].AvailableAmount));
    }

    [Fact]
    public void A_return_of_an_item_without_a_live_assignment_stays_on_the_original_account()
    {
        var untouched = Item("100.00", "100.00");
        var released = Item("50.00", "50.00");
        var plan = FinanceInvoiceAcceptanceService.Plan([untouched, released], "150.00");

        var transferred = FinanceInvoiceAcceptanceService.TransferredReductions(plan, [Assignment(released, "0.00")]);

        Assert.Null(transferred.Issue);
        Assert.Empty(transferred.Reductions);
    }

    [Fact]
    public void A_return_that_would_split_between_the_original_account_and_a_successor_is_refused()
    {
        var item = Item("100.00", "100.00");
        var plan = FinanceInvoiceAcceptanceService.Plan([item], "30.00");

        var transferred = FinanceInvoiceAcceptanceService.TransferredReductions(plan, [Assignment(item, "80.00")]);

        Assert.Empty(transferred.Reductions);
        Assert.Equal(FinanceInvoiceAcceptanceService.TransferredBalanceSplitCode, transferred.Issue!.Code);
    }

    [Fact]
    public void A_stored_assignment_above_the_items_outstanding_amount_is_not_understood()
    {
        var item = Item("100.00", "100.00");
        var plan = FinanceInvoiceAcceptanceService.Plan([item], "30.00");

        var transferred = FinanceInvoiceAcceptanceService.TransferredReductions(plan, [Assignment(item, "120.00")]);

        Assert.Empty(transferred.Reductions);
        Assert.Equal(FinanceInvoiceAcceptanceService.StoredStateNotUnderstoodCode, transferred.Issue!.Code);
    }

    private static FinancePartyBalanceAssignmentRow Assignment(FinanceOpenItemRow item, string available) => new()
    {
        CompanyId = TestCompany,
        Kind = FinanceBalanceAssignmentKinds.OpenItem,
        SourceId = item.Id,
        OriginalPartyId = item.PartyId,
        OriginalAccountId = item.CompanyAccountId,
        CurrentPartyId = Guid.Parse("77777777-7777-4777-8777-777777777777"),
        CurrentAccountId = Guid.Parse("88888888-8888-4888-8888-888888888888"),
        CurrencyId = item.CurrencyId,
        AvailableAmountText = available,
        LastTransferId = Guid.Parse("99999999-9999-4999-8999-999999999999"),
        Version = 1,
    };

    private static FinanceInvoiceAcceptanceV2 Acceptance() => new(
        Guid.Parse("44444444-4444-4444-8444-444444444444"),
        FinanceInvoiceProducerV1.Sales,
        FinanceInvoiceAcceptanceKindV2.CustomerReturn,
        Guid.Parse("55555555-5555-4555-8555-555555555555"),
        "v1",
        Guid.Parse("66666666-6666-4666-8666-666666666666"),
        "v2",
        Party,
        Account,
        Currency,
        "40.00",
        null,
        new DateOnly(2026, 11, 14));

    private static FinanceOpenItemRow Item(string original, string outstanding,
        int status = FinanceOpenItemStatuses.Open, DateOnly? dueOn = null) => new()
    {
        Id = Guid.NewGuid(),
        DueOn = dueOn,
        SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
        Direction = FinanceOpenItemDirectionCodes.Receivable,
        PartyId = Party,
        CompanyAccountId = Account,
        CurrencyId = Currency,
        OriginalAmountText = original,
        OutstandingAmountText = outstanding,
        Status = status,
        Version = 1,
    };

    private static readonly Guid TestCompany = Guid.Parse("0a000000-0000-4000-8000-0000000000ab");

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(TestCompany);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }
}
