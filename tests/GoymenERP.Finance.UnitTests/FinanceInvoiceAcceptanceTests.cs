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
/// reduction of the original invoice's open items and the customer's advance, the fail-closed refusal of a partial return
/// over several outstanding installments, and the stored model (one credit per return document, append-only reductions,
/// one concurrency row per original invoice, a forward-fix Down). SENTETIK values only.
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
    public void A_partial_return_over_several_outstanding_installments_is_refused_because_the_order_is_not_decided()
    {
        var plan = FinanceInvoiceAcceptanceService.Plan([Item("60.00", "60.00"), Item("40.00", "40.00")], "50.00");

        Assert.Equal(FinanceInvoiceAcceptanceService.InstallmentOrderNotDecidedCode, plan.Issue?.Code);
        Assert.Empty(plan.Reductions);
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
        int status = FinanceOpenItemStatuses.Open) => new()
    {
        Id = Guid.NewGuid(),
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
