using System.Text.Json;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Api.IntegrationTests;

// Formatter contract only; HTTP, terminal persistence and relational execution remain separate evidence.
public sealed class InventoryPhysicalStockRefusalProblemTests
{
    private static readonly Guid Company = Guid.Parse("82300000-0000-4000-8000-000000000001");
    private static readonly Guid Movement = Guid.Parse("82300000-0000-4000-8000-000000000002");
    private static readonly Guid Label = Guid.Parse("82300000-0000-4000-8000-000000000003");
    private static readonly Guid Product = Guid.Parse("82300000-0000-4000-8000-000000000004");
    private static readonly Guid Location = Guid.Parse("82300000-0000-4000-8000-000000000005");
    private const string GoldenDetail = "Sistemde 0.125 kg var, 2.5 kg istendi.";

    [Fact]
    public void A_nested_owner_proof_preserves_the_original_code_and_correlation_and_exposes_only_stock_facts()
    {
        var extensions = Extensions();
        var detail = InventoryPhysicalStockRefusalProblem.Apply(409, Proof(), extensions);
        Assert.Equal(GoldenDetail, detail);
        Assert.Equal("synthetic-physical-conflict", extensions["code"]);
        Assert.Equal("synthetic-correlation", extensions["correlationId"]);
        Assert.Equal(3, extensions.Count);

        var stock = JsonSerializer.SerializeToElement(extensions["stockShortfall"]);
        Assert.Equal(["availableKg", "labelId", "locationId", "missingKg", "movementId", "productId", "requestedKg"],
            stock.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Movement, stock.GetProperty("movementId").GetGuid());
        Assert.Equal(Label, stock.GetProperty("labelId").GetGuid());
        Assert.Equal(Product, stock.GetProperty("productId").GetGuid());
        Assert.Equal(Location, stock.GetProperty("locationId").GetGuid());
        Assert.Equal("0.125", stock.GetProperty("availableKg").GetString());
        Assert.Equal("2.5", stock.GetProperty("requestedKg").GetString());
        Assert.Equal("2.375", stock.GetProperty("missingKg").GetString());
        Assert.DoesNotContain("synthetic-private-marker", stock.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private-marker", detail!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_persisted_detail_replays_the_same_sentence_without_reconstructing_a_lost_typed_proof()
    {
        var first = InventoryPhysicalStockRefusalProblem.Apply(409, Proof(), Extensions());
        var replay = Extensions();
        Assert.Equal(first, InventoryPhysicalStockRefusalProblem.Apply(409, new Exception("synthetic-replay"), replay, first));
        Assert.Equal(GoldenDetail, first);
        Assert.False(replay.ContainsKey("stockShortfall"));
        Assert.Equal(2, replay.Count);
    }

    [Fact]
    public void Legacy_null_detail_and_an_exception_message_that_looks_like_a_shortfall_do_not_invent_evidence()
    {
        var extensions = Extensions();
        Assert.Null(InventoryPhysicalStockRefusalProblem.Apply(409, new Exception(GoldenDetail), extensions));
        Assert.False(extensions.ContainsKey("stockShortfall"));
    }

    [Theory]
    [InlineData(409, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(422, false)]
    [InlineData(503, false)]
    public void A_legacy_signed_recorded_detail_replays_only_for_conflict_without_fabricating_a_typed_proof(int status, bool disclosed)
    {
        const string recorded = "Sistemde -0.125 kg var, 2.5 kg istendi.";
        var extensions = Extensions();
        Assert.Equal(disclosed ? recorded : null,
            InventoryPhysicalStockRefusalProblem.Apply(status, new Exception("synthetic-replay"), extensions, recorded));
        Assert.Equal(2, extensions.Count);
        Assert.False(extensions.ContainsKey("stockShortfall"));
        Assert.Null(InventoryPhysicalStockRefusalProblem.Apply(status, new Exception(recorded), Extensions()));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    [InlineData(503)]
    public void Other_statuses_never_disclose_stock_even_with_an_owner_proof_and_a_recorded_detail(int status)
    {
        var extensions = Extensions();
        Assert.Null(InventoryPhysicalStockRefusalProblem.Apply(status, Proof(), extensions, GoldenDetail));
        Assert.Equal(2, extensions.Count);
        Assert.False(extensions.ContainsKey("stockShortfall"));
    }

    [Theory]
    [InlineData("Sistemde -0 kg var, 1 kg istendi.")]
    [InlineData("Sistemde 1 kg var, 0 kg istendi.")]
    [InlineData("Sistemde 1.0001 kg var, 2 kg istendi.")]
    [InlineData("Sistemde 01 kg var, 2 kg istendi.")]
    [InlineData("Sistemde 1.00 kg var, 2 kg istendi.")]
    [InlineData("Sistemde 1 kg var, 2 kg istendi.\n")]
    [InlineData("Sistemde 1\0 kg var, 2 kg istendi.")]
    [InlineData("<script>unsafe</script>")]
    public void An_invalid_recorded_detail_is_not_exposed_as_a_physical_refusal(string detail)
    {
        Assert.Null(InventoryPhysicalStockRefusalProblem.Apply(409, new Exception("synthetic-replay"), Extensions(), detail));
    }

    [Fact]
    public void Legacy_advisory_shortfalls_without_a_failing_physical_line_do_not_become_a_new_proof()
    {
        var extensions = Extensions();
        Assert.Null(InventoryPhysicalStockRefusalProblem.Apply(409, Proof(lineNumber: null), extensions));
        Assert.False(extensions.ContainsKey("stockShortfall"));
    }

    private static Dictionary<string, object?> Extensions() => new()
    {
        ["code"] = "synthetic-physical-conflict", ["correlationId"] = "synthetic-correlation",
    };

    private static Exception Proof(int? lineNumber = 1)
    {
        var label = new MaterialLabel(Label, Company, "SYNTHETIC-LABEL", Product,
            Guid.Parse("82300000-0000-4000-8000-000000000006"));
        var position = new StockPosition(Company, new(StockPartyKind.Company, Company),
            new(StockPartyKind.Company, Company), Location, label, BlockState.Free);
        var shortfall = new StockShortfall(StockMovementKind.Issue, new("synthetic-physical-exit", Movement),
            position, Kilograms.Of(2.5m), Kilograms.Of(0.125m), lineNumber);
        var metadata = new InventoryCommandMetadata("synthetic-private-marker", "synthetic-permission", [],
            "synthetic-private-correlation", null, "synthetic-private-key", "synthetic-test");
        return new Exception("synthetic-host-wrapper", new InventoryNegativeStockAttemptEvidenceException(
            new(Movement, new string('a', 64), shortfall, label.Code,
                new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero), metadata),
            new InvalidOperationException("synthetic-private-marker")));
    }
}
