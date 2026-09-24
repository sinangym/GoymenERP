using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceAllocationPolicyTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid CurrencyId = Guid.NewGuid();

    [Fact]
    public void Create_uses_lossless_arithmetic_and_closes_only_the_fully_allocated_item()
    {
        var source = Source("999999999999999999999999999999.50");
        var item = OpenItem("250.10");
        var request = CreateRequest(source, item, "250.10");

        var result = FinanceAllocationPolicy.Create(source, item, null, request);

        Assert.Equal("999999999999999999999999999749.40", result.SourceUnallocated);
        Assert.Equal("0.00", result.OpenItemOutstanding);
        Assert.Equal(FinanceOpenItemStatuses.Closed, result.OpenItemStatus);
    }

    [Theory]
    [InlineData("1000.01", "1000", "finance_allocation_exceeds_source")]
    [InlineData("100.01", "100", "finance_allocation_exceeds_open_item")]
    public void Create_refuses_over_allocation_without_rounding(
        string amount,
        string limit,
        string code)
    {
        var source = Source(code.EndsWith("source", StringComparison.Ordinal) ? limit : "1000");
        var item = OpenItem(code.EndsWith("open_item", StringComparison.Ordinal) ? limit : "1000");

        var failure = Assert.Throws<FinanceAllocationPolicyException>(() =>
            FinanceAllocationPolicy.Create(source, item, null, CreateRequest(source, item, amount)));

        Assert.Equal(code, failure.Code);
    }

    [Fact]
    public void Create_refuses_another_account_currency_or_stale_version()
    {
        var source = Source("1000");
        var item = OpenItem("1000");
        var anotherAccountItem = OpenItem("1000");
        anotherAccountItem.CompanyAccountId = Guid.NewGuid();

        Assert.Equal("finance_allocation_reference_invalid",
            Assert.Throws<FinanceAllocationPolicyException>(() => FinanceAllocationPolicy.Create(
                source, anotherAccountItem, null,
                CreateRequest(source, item, "1"))).Code);
        Assert.Equal("version_conflict",
            Assert.Throws<FinanceAllocationPolicyException>(() => FinanceAllocationPolicy.Create(
                source, item, null, CreateRequest(source, item, "1") with { ExpectedOpenItemVersion = "2" })).Code);
    }

    [Fact]
    public void Release_restores_both_balances_without_creating_money()
    {
        var allocation = Allocation("250.10");
        var item = OpenItem("0.00");
        allocation.OpenItemId = item.Id;
        item.Status = FinanceOpenItemStatuses.Closed;
        item.Version = 2;
        var state = new FinanceSourceAllocationStateRow
        {
            CompanyId = CompanyId, SourceId = allocation.SourceId, CurrencyId = CurrencyId,
            OriginalAmountText = "1250.50", UnallocatedAmountText = "1000.40", Version = 1,
            UpdatedBy = "actor", UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var result = FinanceAllocationPolicy.Release(allocation, item, state,
            new ReleaseFinanceAllocationRequest(allocation.Id, "1", "2", "Yanlış dağıtım"));

        Assert.Equal("1250.50", result.SourceUnallocated);
        Assert.Equal("250.10", result.OpenItemOutstanding);
        Assert.Equal(FinanceOpenItemStatuses.Open, result.OpenItemStatus);
    }

    private static ReceiptDraftRow Source(string amount) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, CompanyAccountId = AccountId, CurrencyId = CurrencyId,
        Stage = ReceiptStages.Recorded, Version = 1, AmountText = amount,
    };

    private static FinanceOpenItemRow OpenItem(string outstanding) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, CompanyAccountId = AccountId, CurrencyId = CurrencyId,
        OriginalAmountText = outstanding == "0.00" ? "250.10" : outstanding,
        OutstandingAmountText = outstanding, Status = FinanceOpenItemStatuses.Open, Version = 1,
    };

    private static CreateFinanceAllocationRequest CreateRequest(
        ReceiptDraftRow source,
        FinanceOpenItemRow item,
        string amount) => new(
            Guid.NewGuid(), source.Id, "1", item.Id, "1", amount, "SENTETIK dağıtım");

    private static FinanceAllocationRow Allocation(string amount) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, SourceId = Guid.NewGuid(), OpenItemId = Guid.NewGuid(),
        CurrencyId = CurrencyId, AmountText = amount, Status = FinanceAllocationStatuses.Active, Version = 1,
    };
}
