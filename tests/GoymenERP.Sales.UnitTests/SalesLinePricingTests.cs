using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Sales.UnitTests;

/// <summary>AT-018 / AT-021: the line price calculator, the stored pricing detail and its rollback refusal. SENTETIK.</summary>
public sealed class SalesLinePricingTests
{
    private static readonly Guid Unit = Guid.Parse("66666666-6666-4666-8666-666666666666");

    private static PriceReferenceSnapshot Reference(string price = "100", string? floor = null,
        string lowerQuantity = "10", string? upperQuantity = "20") =>
        new(Guid.Parse("abababab-abab-4bab-8bab-abababababab"), "SENTETIK", 2, new DateOnly(2026, 10, 1), null, "TRY", Unit,
            price, floor, "ExcludingVat", lowerQuantity, upperQuantity);

    [Theory]
    [InlineData("100", null, null, null, null, "100")]
    [InlineData("100", "10", null, null, null, "90")]
    [InlineData("100", null, "7.5", null, null, "92.5")]
    [InlineData("100", "10", null, "2", null, "91.8")]
    [InlineData("100", null, "10", null, "3.25", "93.25")]
    [InlineData("42000.50", "3", null, "1.5", null, "41351.592275")]
    public void The_net_is_list_or_manual_price_minus_the_discount_plus_the_surcharge_on_the_discounted_price_never_rounded(
        string price, string? discountPercent, string? discountAmount, string? surchargePercent, string? surchargeAmount, string net)
    {
        var fields = new CommercialDraftFields(price, Unit, discountPercent, null, discountAmount, surchargePercent, surchargeAmount);
        Assert.Equal(net, SalesLinePriceCalculator.NetUnitPriceText(fields));
    }

    [Fact]
    public void The_price_source_says_whether_the_reference_price_was_kept_or_overwritten()
    {
        Assert.Equal("Reference", new CommercialDraftFields("100.00", Unit, null, Reference("100")).PriceSource);
        Assert.Equal("Manual", new CommercialDraftFields("99", Unit, null, Reference("100")).PriceSource);
        Assert.Equal("Manual", new CommercialDraftFields("99", Unit, null).PriceSource);
        Assert.Null(CommercialDraftFields.None.PriceSource);
        Assert.Null(SalesLinePriceCalculator.NetUnitPrice(CommercialDraftFields.None));
    }

    [Fact]
    public void Decimal_text_must_be_exactly_representable_and_intermediate_arithmetic_never_rounds_or_wraps()
    {
        Assert.False(SalesLinePriceCalculator.TryParseExact("0.00000000000000000000000000001", out _));
        Assert.True(SalesLinePriceCalculator.TryParseExact("79228162514264337593543950335", out var maximum));
        Assert.Equal(decimal.MaxValue, maximum);
        Assert.Throws<OverflowException>(() => SalesLinePriceCalculator.NetUnitPrice(
            new CommercialDraftFields("1", Unit, "0.0000000000000000000000000001")));
        Assert.Throws<OverflowException>(() => SalesLinePriceCalculator.NetUnitPrice(
            new CommercialDraftFields("79228162514264337593543950335", Unit, null, SurchargePercent: "100")));
    }

    [Fact]
    public void The_pricing_detail_is_stored_only_when_present_and_restores_exactly()
    {
        Assert.Null(SalesLinePricingJson.Serialize(new CommercialDraftFields("1", Unit, "5")));
        var fields = new CommercialDraftFields("98", Unit, null, Reference("100", "95"), "2", null, "1.5");
        var json = SalesLinePricingJson.Serialize(fields)!;
        var restored = SalesLinePricingJson.Restore("98", Unit, null, json);
        Assert.Equal(fields, restored);
        Assert.Equal(("10", "20"), (restored.Reference!.LowerQuantity, restored.Reference.UpperQuantity));
        Assert.Throws<InvalidOperationException>(() => SalesLinePricingJson.Restore("98", Unit, null, "{\"unknown\":1}"));
        Assert.Throws<InvalidOperationException>(() => SalesLinePricingJson.Restore("98", Unit, null,
            "{\"reference\":null,\"discountAmount\":null,\"surchargePercent\":null,\"surchargeAmount\":null}"));
    }

    [Fact]
    public void The_floor_exception_round_trips_with_the_exact_price_snapshot_and_actor_facts()
    {
        var requestedAt = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var reference = Reference("100", "95");
        var exception = new PriceFloorExceptionSnapshot("maker", requestedAt, reference.PriceListId, reference.VersionNo,
            Unit, "90", "90", "95", "checker", requestedAt.AddMinutes(5), "SENTETIK gerekçe");
        var fields = new CommercialDraftFields("90", Unit, null, reference, FloorException: exception);

        Assert.Equal(fields, SalesLinePricingJson.Restore("90", Unit, null, SalesLinePricingJson.Serialize(fields)));
    }

    [Fact]
    public void The_line_pricing_migration_down_refuses_with_51064_before_it_drops_anything()
    {
        var migration = new AddLinePricing();
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51064,", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[sales].[DraftLines] WHERE [PricingJson] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[sales].[DraftRevisionLines] WHERE [PricingJson] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.All(migration.DownOperations.Skip(1), operation => Assert.IsNotType<SqlOperation>(operation));
        Assert.Equal(2, migration.UpOperations.OfType<AddColumnOperation>().Count(column => column.Name == "PricingJson" && column.IsNullable));
    }
}
