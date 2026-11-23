using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D386 / SAT-D403 / SAT-D407 (STK-SC36-a) goldens: the document discount is distributed by line amount, each share is
/// rounded to kuruş, the remainder goes to the largest line (the first on a tie), and the shares always sum to the
/// document discount. The net line amount (line and document discount, VAT excluded) is the cost base. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryDocumentDiscountAllocationTests
{
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Usd = Guid.Parse("66666666-6666-4666-8666-666666666661");
    private const int Kurus = 2;

    [Fact]
    public void Shares_follow_the_line_amounts_and_the_remainder_goes_to_the_largest_line()
    {
        // 0.10 over 1 + 1 + 5: 0.014.. -> 0.01, 0.01 and 0.071.. -> 0.07, sum 0.09; the remainder 0.01 goes to the 5 line.
        var shares = InventoryDocumentDiscountAllocationV1.Allocate([Tl(1m), Tl(1m), Tl(5m)], Tl(0.10m));

        Assert.Equal([0.01m, 0.01m, 0.08m], shares.Select(share => share.DocumentDiscountShare.Amount).ToArray());
        Assert.Equal(0.10m, shares.Sum(share => share.DocumentDiscountShare.Amount));
        Assert.Equal([0.99m, 0.99m, 4.92m], shares.Select(share => share.NetLineAmount.Amount).ToArray());

        // A shares-only split with no remainder: 100 over 500 + 1500 + 1000 -> 16.67 + 50 + 33.33.
        var exact = InventoryDocumentDiscountAllocationV1.Allocate([Tl(500m), Tl(1500m), Tl(1000m)], Tl(100m));
        Assert.Equal([16.67m, 50m, 33.33m], exact.Select(share => share.DocumentDiscountShare.Amount).ToArray());
    }

    [Fact]
    public void The_remainder_goes_to_the_first_line_when_the_largest_amounts_tie()
    {
        // 10 / 3 = 3.333.. -> 3.33 each, sum 9.99; the remainder 0.01 goes to the first of the tied lines.
        var shares = InventoryDocumentDiscountAllocationV1.Allocate([Tl(100m), Tl(100m), Tl(100m)], Tl(10m));

        Assert.Equal([3.34m, 3.33m, 3.33m], shares.Select(share => share.DocumentDiscountShare.Amount).ToArray());
        Assert.Equal(10m, shares.Sum(share => share.DocumentDiscountShare.Amount));
    }

    [Fact]
    public void A_negative_remainder_is_taken_back_from_the_largest_line()
    {
        // 0.02 over three 0.01 lines: 0.00666.. -> 0.01 each (away from zero), sum 0.03; -0.01 back from the first tied line.
        var shares = InventoryDocumentDiscountAllocationV1.Allocate([Tl(0.01m), Tl(0.01m), Tl(0.01m)], Tl(0.02m));

        Assert.Equal([0m, 0.01m, 0.01m], shares.Select(share => share.DocumentDiscountShare.Amount).ToArray());
        Assert.Equal(0.02m, shares.Sum(share => share.DocumentDiscountShare.Amount));
    }

    [Fact]
    public void Shares_always_sum_to_the_document_discount()
    {
        var random = new Random(407);
        for (var run = 0; run < 200; run++)
        {
            var lines = Enumerable.Range(0, random.Next(1, 9))
                .Select(_ => Tl(random.Next(1, 1_000_000) / 100m))
                .ToArray();
            var total = lines.Sum(line => line.Amount);
            var discount = Tl(Math.Round(total * random.Next(0, 101) / 100m, Kurus, MidpointRounding.AwayFromZero));

            var shares = InventoryDocumentDiscountAllocationV1.Allocate(lines, discount);

            Assert.Equal(discount.Amount, shares.Sum(share => share.DocumentDiscountShare.Amount));
            Assert.All(shares, share => Assert.Equal(share.LineAmount.Amount - share.DocumentDiscountShare.Amount, share.NetLineAmount.Amount));
        }
    }

    [Fact]
    public void A_zero_document_discount_leaves_every_line_as_it_is()
    {
        var shares = InventoryDocumentDiscountAllocationV1.Allocate([Tl(12.5m), Tl(7m)], Tl(0m));

        Assert.Equal([12.5m, 7m], shares.Select(share => share.NetLineAmount.Amount).ToArray());
        Assert.All(shares, share => Assert.True(share.DocumentDiscountShare.IsZero));
    }

    [Fact]
    public void Mixed_currencies_negative_or_excess_discounts_and_no_lines_are_refused()
    {
        Assert.Throws<ArgumentException>(() => InventoryDocumentDiscountAllocationV1.Allocate([], Tl(1m)));
        Assert.Throws<MoneyRuleException>(() =>
            InventoryDocumentDiscountAllocationV1.Allocate([Tl(10m), Money.Of(10m, Usd, Kurus)], Tl(1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryDocumentDiscountAllocationV1.Allocate([Tl(10m)], Tl(-1m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryDocumentDiscountAllocationV1.Allocate([Tl(10m)], Tl(10.01m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryDocumentDiscountAllocationV1.Allocate([Tl(0m)], Tl(1m)));
    }

    private static Money Tl(decimal amount) => Money.Of(amount, Lira, Kurus);
}
