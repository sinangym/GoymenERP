using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryPhysicalRefusalTerminalContractTests
{
    [Fact]
    public void Old_terminal_JSON_without_a_detail_keeps_its_original_code_and_empty_optional_detail()
    {
        const string old = "{\"status\":409,\"code\":\"inventory_physical_stock_insufficient\",\"errors\":[]}";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var logistics = JsonSerializer.Deserialize<LogisticsTerminalRefusal>(old, options)!;
        var purchase = JsonSerializer.Deserialize<PurchasingTerminalRefusal>(old, options)!;
        Assert.Null(logistics.Detail);
        Assert.Null(purchase.Detail);
        Assert.True(LogisticsPhysicalStockRefusalDetailV1.IsValid(logistics.Status, logistics.Code, logistics.Detail));
        Assert.True(PurchasingPhysicalStockRefusalDetailV1.IsValid(purchase.Status, purchase.Code, purchase.Detail));
    }

    [Fact]
    public void Original_quantity_message_survives_both_terminal_JSON_roundtrips_without_recomputing_current_stock()
    {
        const string detail = "Sistemde 480 kg var, 500 kg istendi.";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var logistics = JsonSerializer.Deserialize<LogisticsTerminalRefusal>(JsonSerializer.Serialize(
            new LogisticsTerminalRefusal(409, "inventory_physical_stock_insufficient", [], detail), options), options)!;
        var purchasing = JsonSerializer.Deserialize<PurchasingTerminalRefusal>(JsonSerializer.Serialize(
            new PurchasingTerminalRefusal(409, "inventory_goods_receipt_reversal_stock_not_available", [], detail), options), options)!;
        Assert.Equal(detail, logistics.Detail);
        Assert.Equal(detail, purchasing.Detail);
        Assert.True(LogisticsPhysicalStockRefusalDetailV1.IsValid(logistics.Status, logistics.Code, logistics.Detail));
        Assert.True(PurchasingPhysicalStockRefusalDetailV1.IsValid(purchasing.Status, purchasing.Code, purchasing.Detail));
    }

    [Theory]
    [InlineData("Sistemde -0 kg var, 500 kg istendi.")]
    [InlineData("Sistemde 480.0000 kg var, 500 kg istendi.")]
    [InlineData("Sistemde 480 kg var, 0 kg istendi.")]
    [InlineData("Sistemde 500 kg var, 480 kg istendi.")]
    [InlineData("Sistemde 480 kg var, 500 kg istendi.\n")]
    [InlineData("arbitrary exception message")]
    public void Terminal_quantity_detail_rejects_arbitrary_or_noncanonical_text(string detail)
    {
        Assert.False(LogisticsPhysicalStockRefusalDetailV1.IsValid(409, "inventory_physical_stock_insufficient", detail));
        Assert.False(PurchasingPhysicalStockRefusalDetailV1.IsValid(409, "inventory_goods_receipt_reversal_stock_not_available", detail));
    }

    [Theory]
    [InlineData(401, "inventory_physical_stock_insufficient")]
    [InlineData(503, "inventory_physical_stock_insufficient")]
    [InlineData(409, "inventory_allocation_insufficient")]
    public void A_nonphysical_or_nondisclosable_refusal_never_accepts_a_quantity_detail(int status, string code)
    {
        const string detail = "Sistemde 480 kg var, 500 kg istendi.";
        Assert.False(LogisticsPhysicalStockRefusalDetailV1.IsValid(status, code, detail));
        Assert.False(PurchasingPhysicalStockRefusalDetailV1.IsValid(status, code, detail));
    }
}
