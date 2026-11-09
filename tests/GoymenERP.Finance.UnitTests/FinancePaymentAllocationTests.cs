using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// F3a / API-20 (DEC-FIN-006) without a database: a RECORDED manual bank payment closes only an open PAYABLE item of
/// the same company, account and currency, with the receipt allocation's exact text arithmetic; release restores both
/// balances. The model, the write guard and the migration Down guard are checked here; what SQL Server enforces is in
/// <c>FinancePaymentAllocationSqlServerRelationalAcceptanceTests</c>. SENTETIK.
/// </summary>
public sealed class FinancePaymentAllocationTests
{
    private static readonly Guid CompanyId = Guid.Parse("0a000000-0000-4000-8000-0000000000f3");
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid CurrencyId = Guid.NewGuid();

    [Fact]
    public void A_payment_closes_a_payable_with_lossless_arithmetic_and_a_partial_amount_leaves_it_open()
    {
        var payment = Payment("999999999999999999999999999999.50");
        var item = Payable("250.10");

        var full = FinanceAllocationPolicy.CreatePayment(payment, item, null, Request(payment, item, "250.10"));
        Assert.Equal("999999999999999999999999999749.40", full.SourceUnallocated);
        Assert.Equal("0.00", full.OpenItemOutstanding);
        Assert.Equal(FinanceOpenItemStatuses.Closed, full.OpenItemStatus);

        var partial = FinanceAllocationPolicy.CreatePayment(payment, item, null, Request(payment, item, "0.1"));
        Assert.Equal("250.00", partial.OpenItemOutstanding);
        Assert.Equal(FinanceOpenItemStatuses.Open, partial.OpenItemStatus);
    }

    [Fact]
    public void A_payment_continues_from_its_stored_unallocated_balance()
    {
        var payment = Payment("1000.00");
        var item = Payable("800.00");
        var state = State(payment, "300.00");

        var result = FinanceAllocationPolicy.CreatePayment(payment, item, state, Request(payment, item, "300"));
        Assert.Equal("0.00", result.SourceUnallocated);
        Assert.Equal("500.00", result.OpenItemOutstanding);

        var over = Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.CreatePayment(payment, item, state, Request(payment, item, "300.01")));
        Assert.Equal("finance_allocation_exceeds_source", over.Code);
    }

    [Fact]
    public void A_receivable_is_refused_because_a_payment_closes_only_a_payable()
    {
        // A customer's receivable with a supplier payment would be an offset: its own approved operation, never this.
        var payment = Payment("1000");
        var receivable = Payable("1000");
        receivable.Direction = FinanceOpenItemDirectionCodes.Receivable;

        var failure = Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.CreatePayment(payment, receivable, null, Request(payment, receivable, "1")));

        Assert.Equal("finance_allocation_direction_mismatch", failure.Code);
        Assert.Equal("openItemId", failure.Field);
    }

    [Theory]
    [InlineData("1000.01", "finance_allocation_exceeds_source")]
    [InlineData("500.01", "finance_allocation_exceeds_open_item")]
    [InlineData("0", "finance_allocation_amount_invalid")]
    [InlineData("-1", "finance_allocation_amount_invalid")]
    [InlineData("1,5", "finance_allocation_amount_invalid")]
    [InlineData(" 1", "finance_allocation_amount_invalid")]
    [InlineData("1e2", "finance_allocation_amount_invalid")]
    public void An_amount_that_is_not_positive_exact_text_or_exceeds_a_balance_is_refused(string amount, string code)
    {
        var payment = Payment(code == "finance_allocation_exceeds_source" ? "1000" : "5000");
        var item = Payable(code == "finance_allocation_exceeds_source" ? "5000" : "500");

        var failure = Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.CreatePayment(payment, item, null, Request(payment, item, amount)));

        Assert.Equal(code, failure.Code);
    }

    [Theory]
    [InlineData("payment-draft")]
    [InlineData("payment-approved")]
    [InlineData("other-account")]
    [InlineData("other-currency")]
    [InlineData("other-company")]
    [InlineData("item-closed")]
    [InlineData("item-cancelled")]
    public void A_payment_not_recorded_or_an_item_of_another_account_currency_company_or_state_is_refused(string fault)
    {
        var payment = Payment("100");
        var item = Payable("100");
        switch (fault)
        {
            case "payment-draft": payment.Status = ManualBankPaymentStatuses.Draft; break;
            case "payment-approved": payment.Status = ManualBankPaymentStatuses.Approved; break;
            case "other-account": item.CompanyAccountId = Guid.NewGuid(); break;
            case "other-currency": item.CurrencyId = Guid.NewGuid(); break;
            case "other-company": item.CompanyId = Guid.NewGuid(); break;
            case "item-closed": item.Status = FinanceOpenItemStatuses.Closed; break;
            default: item.Status = FinanceOpenItemStatuses.Cancelled; break;
        }

        var failure = Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.CreatePayment(payment, item, null, Request(payment, item, "1")));

        Assert.Equal("finance_allocation_reference_invalid", failure.Code);
    }

    [Theory]
    [InlineData("payment", "2")]
    [InlineData("payment", "0")]
    [InlineData("payment", "01x")]
    [InlineData("item", "2")]
    [InlineData("item", "")]
    public void A_stale_or_malformed_expected_version_is_a_version_conflict(string target, string version)
    {
        var payment = Payment("100");
        var item = Payable("100");
        var request = Request(payment, item, "1");
        request = target == "payment"
            ? request with { ExpectedPaymentVersion = version }
            : request with { ExpectedOpenItemVersion = version };

        var failure = Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.CreatePayment(payment, item, null, request));

        Assert.Equal("version_conflict", failure.Code);
    }

    [Fact]
    public void A_missing_reason_or_a_drifted_payment_state_is_refused()
    {
        var payment = Payment("100");
        var item = Payable("100");
        Assert.Equal("validation_failed", Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.CreatePayment(payment, item, null, Request(payment, item, "1") with { Reason = " " })).Code);

        var drifted = State(payment, "100");
        drifted.OriginalAmountText = "99";
        Assert.Equal("finance_allocation_state_invalid", Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.CreatePayment(payment, item, drifted, Request(payment, item, "1"))).Code);
    }

    [Fact]
    public void A_release_restores_the_payment_and_reopens_the_item_without_creating_money()
    {
        var payment = Payment("1250.50");
        var item = Payable("250.10");
        item.OutstandingAmountText = "0.00";
        item.Status = FinanceOpenItemStatuses.Closed;
        item.Version = 2;
        var state = State(payment, "1000.40");
        var allocation = Allocation(payment, item, "250.10");

        var result = FinanceAllocationPolicy.ReleasePayment(allocation, item, state,
            new ReleaseFinanceAllocationRequest(allocation.Id, "1", "2", "SENTETIK yanlis dagitim"));

        Assert.Equal("1250.50", result.SourceUnallocated);
        Assert.Equal("250.10", result.OpenItemOutstanding);
        Assert.Equal(FinanceOpenItemStatuses.Open, result.OpenItemStatus);
    }

    [Fact]
    public void A_release_of_a_released_allocation_a_stale_version_or_an_overflowing_balance_is_refused()
    {
        var payment = Payment("100");
        var item = Payable("100");
        item.OutstandingAmountText = "60";
        item.Version = 2;
        var state = State(payment, "60");
        var allocation = Allocation(payment, item, "40");
        var request = new ReleaseFinanceAllocationRequest(allocation.Id, "1", "2", "SENTETIK");

        Assert.Equal("version_conflict", Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.ReleasePayment(allocation, item, state, request with { ExpectedOpenItemVersion = "1" })).Code);

        var released = Allocation(payment, item, "40");
        released.Status = FinanceAllocationStatuses.Released;
        Assert.Equal("finance_allocation_state_invalid", Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.ReleasePayment(released, item, state, request with { AllocationId = released.Id })).Code);

        var full = State(payment, "100");
        Assert.Equal("finance_allocation_state_invalid", Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.ReleasePayment(allocation, item, full, request)).Code);

        item.OutstandingAmountText = "100";
        Assert.Equal("finance_open_item_balance_invalid", Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.ReleasePayment(allocation, item, state, request)).Code);
    }

    [Fact]
    public void The_model_keeps_payment_allocations_company_bound_text_money_and_release_only()
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model;
        var allocation = model.FindEntityType(typeof(FinancePaymentAllocationRow))!;
        Assert.Equal(("finance", "PaymentAllocations"), (allocation.GetSchema(), allocation.GetTableName()));
        Assert.Single(allocation.GetDeclaredQueryFilters());
        Assert.True(allocation.FindProperty(nameof(FinancePaymentAllocationRow.Version))!.IsConcurrencyToken);
        Assert.DoesNotContain(allocation.GetProperties(), property => property.ClrType == typeof(decimal));
        Assert.Equal(FinanceDbContext.AllocationStatusCheck,
            Assert.Single(allocation.GetCheckConstraints(), check => check.Name == "CK_PaymentAllocations_Status").Sql);
        Assert.Equal(FinanceDbContext.PaymentAllocationIdentityCheck,
            Assert.Single(allocation.GetCheckConstraints(), check => check.Name == "CK_PaymentAllocations_Identity").Sql);
        Assert.Contains(allocation.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(ManualBankPaymentRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PaymentId"]));
        Assert.Contains(allocation.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(FinanceOpenItemRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "OpenItemId"]));
        Assert.All(allocation.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        Assert.Contains(allocation.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(["CompanyId", "OpenItemId", "Status"]));

        var state = model.FindEntityType(typeof(FinancePaymentAllocationStateRow))!;
        Assert.Equal(["CompanyId", "PaymentId"], state.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(state.GetDeclaredQueryFilters());
        Assert.True(state.FindProperty(nameof(FinancePaymentAllocationStateRow.Version))!.IsConcurrencyToken);
        Assert.Contains(state.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(ManualBankPaymentRow)
            && key.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "PaymentId"]));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("reopen")]
    [InlineData("release-without-version")]
    [InlineData("state-original")]
    [InlineData("delete")]
    [InlineData("other-company")]
    public void The_write_guard_admits_only_a_versioned_release_and_a_balance_change(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "amount":
                Attach(context, NewAllocation()).AmountText = "1";
                break;
            case "reopen":
                var released = NewAllocation();
                released.Status = FinanceAllocationStatuses.Released;
                released.ReleasedBy = "SENTETIK";
                released.ReleasedAtUtc = DateTimeOffset.UtcNow;
                released.ReleaseReason = "SENTETIK";
                var tracked = Attach(context, released);
                tracked.Status = FinanceAllocationStatuses.Active;
                tracked.Version = 2;
                break;
            case "release-without-version":
                var active = Attach(context, NewAllocation());
                active.Status = FinanceAllocationStatuses.Released;
                active.ReleasedBy = "SENTETIK";
                active.ReleasedAtUtc = DateTimeOffset.UtcNow;
                active.ReleaseReason = "SENTETIK";
                break;
            case "state-original":
                var row = State(Payment("10"), "10");
                context.Attach(row);
                row.OriginalAmountText = "11";
                break;
            case "delete":
                context.PaymentAllocations.Remove(Attach(context, NewAllocation()));
                break;
            default:
                var foreign = NewAllocation();
                foreign.CompanyId = Guid.NewGuid();
                context.PaymentAllocations.Add(foreign);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_migration_is_pure_ddl_up_and_its_rollback_refuses_first_while_a_payment_allocation_is_stored()
    {
        var migration = new AddPaymentAllocations();

        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51051", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[PaymentAllocations]", guard, StringComparison.Ordinal);
        Assert.Contains("[finance].[PaymentAllocationStates]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
    }

    private static ManualBankPaymentRow Payment(string amount) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, CompanyAccountId = AccountId, CurrencyId = CurrencyId,
        Status = ManualBankPaymentStatuses.Recorded, Version = 3, AmountText = amount,
    };

    private static FinanceOpenItemRow Payable(string outstanding) => new()
    {
        Direction = FinanceOpenItemDirectionCodes.Payable,
        SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment,
        Id = Guid.NewGuid(), CompanyId = CompanyId, CompanyAccountId = AccountId, CurrencyId = CurrencyId,
        OriginalAmountText = outstanding, OutstandingAmountText = outstanding,
        Status = FinanceOpenItemStatuses.Open, Version = 1,
    };

    private static FinancePaymentAllocationStateRow State(ManualBankPaymentRow payment, string unallocated) => new()
    {
        CompanyId = CompanyId, PaymentId = payment.Id, CurrencyId = CurrencyId,
        OriginalAmountText = payment.AmountText, UnallocatedAmountText = unallocated, Version = 1,
        UpdatedBy = "SENTETIK", UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static FinancePaymentAllocationRow Allocation(ManualBankPaymentRow payment, FinanceOpenItemRow item,
        string amount) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, PaymentId = payment.Id, OpenItemId = item.Id,
        CurrencyId = CurrencyId, AmountText = amount, Reason = "SENTETIK", Status = FinanceAllocationStatuses.Active,
        Version = 1, CreatedBy = "SENTETIK", CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static FinancePaymentAllocationRow NewAllocation() => Allocation(Payment("10"), Payable("10"), "10");

    private static CreateFinancePaymentAllocationRequest Request(ManualBankPaymentRow payment, FinanceOpenItemRow item,
        string amount) => new(Guid.NewGuid(), payment.Id, "3", item.Id, "1", amount, "SENTETIK odeme dagitimi");

    private static T Attach<T>(FinanceDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyId);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }
}
