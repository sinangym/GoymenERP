using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// SAT-D318 (owner decision of 2026-11-13): an input issue Inventory refused because it would take physical stock below
/// zero says which issue it was and where. The refusal stays what it was: the same status and code, not terminal, so
/// nothing is recorded and a retry of the same key is decided again. A stored, won or fenced answer never carries the
/// detail. The public view carries Inventory's usable kilograms with the requested ones (owner answer of 2026-11-13 on
/// Issue #66, SINAN-CEVAP (8) question 5), never its free physical or reserved kilograms.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private const string PhysicalStockCode = InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient;
    private const string InputIssueSourceKind = InventoryManufacturingRealizationContractV1.InputIssueSourceKind;

    [Fact]
    public async Task A_physical_stock_refusal_names_the_issue_its_label_and_place_records_nothing_and_a_retry_is_decided_again()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var issueId = Guid.NewGuid();
        var request = new IssueInventoryInputRequestV1(issueId, order.Version, Roll, Guid.NewGuid(), "12.500", At);
        fixture.InputIssues.Refusal = _ => PhysicalStockRefusal(order.Id, issueId, requestedKg: 12.500m);

        var refused = await fixture.Service.IssueInventoryInputAsync(order.Id, request, "issue-shortfall-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, PhysicalStockCode, false, (OrderViewV1?)null),
            (refused.Status, refused.Code, refused.IsReplay, refused.Resource));
        Assert.Equal(new ManufacturingStockShortfallViewV1("Issue", InputIssueSourceKind, order.Id, issueId, Roll,
            Product, Location, "12.5", "2"), refused.StockShortfall);
        Assert.Empty(order.Issues);
        Assert.Empty(fixture.Store.Commands);
        Assert.Empty(fixture.Store.AuditEvents);

        // Not terminal: once Inventory accepts, the same key is decided again, and no later answer carries the detail.
        fixture.InputIssues.Refusal = null;
        var issued = await fixture.Service.IssueInventoryInputAsync(order.Id, request, "issue-shortfall-1", Permission,
            CancellationToken.None);
        var replayed = await fixture.Service.IssueInventoryInputAsync(order.Id, request, "issue-shortfall-1", Permission,
            CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false), (issued.Status, issued.IsReplay));
        Assert.Null(issued.StockShortfall);
        Assert.Equal((ManufacturingApiStatus.Ok, true), (replayed.Status, replayed.IsReplay));
        Assert.Null(replayed.StockShortfall);
        Assert.Single(order.Issues);
        Assert.Single(fixture.Store.Commands);
        Assert.Equal(2, fixture.InputIssues.Commands.Count);
    }

    [Fact]
    public async Task A_physical_stock_refusal_after_the_same_keys_winner_committed_answers_from_the_winner_without_the_detail()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var issueId = Guid.NewGuid();
        fixture.InputIssues.Refusal = metadata =>
        {
            // The same key's other attempt commits while this one runs, and Inventory then refuses this one.
            var record = metadata.Completion!(order) with { OriginalCorrelationId = "corr-winner" };
            fixture.Store.Commands[(record.CommandName, record.IdempotencyKeyHash)] = record;
            return PhysicalStockRefusal(order.Id, issueId, requestedKg: 10m);
        };

        var result = await fixture.Service.IssueInventoryInputAsync(order.Id,
            new IssueInventoryInputRequestV1(issueId, order.Version, Roll, Guid.NewGuid(), "10", At),
            "issue-shortfall-2", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, true, "corr-winner"),
            (result.Status, result.IsReplay, result.OriginalCorrelationId));
        Assert.Null(result.StockShortfall);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("actor")]
    [InlineData("company")]
    public async Task A_physical_stock_refusal_after_the_authority_moved_is_denied_without_the_detail(string fault)
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var issueId = Guid.NewGuid();
        fixture.InputIssues.Refusal = _ =>
        {
            ApplyFreshFenceFault(fixture, fault);
            return PhysicalStockRefusal(order.Id, issueId, requestedKg: 10m);
        };

        var result = await fixture.Service.IssueInventoryInputAsync(order.Id,
            new IssueInventoryInputRequestV1(issueId, order.Version, Roll, Guid.NewGuid(), "10", At),
            "issue-shortfall-3", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied, false),
            (result.Status, result.Code, result.IsReplay));
        Assert.Null(result.StockShortfall);
        Assert.Empty(fixture.Store.Commands);
    }

    [Fact]
    public void The_port_carries_a_shortfall_only_on_inventorys_physical_stock_conflict()
    {
        var shortfall = PortShortfall(Guid.NewGuid(), Guid.NewGuid(), requestedKg: 10m);

        Assert.Same(shortfall, new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Conflict,
            PhysicalStockCode, stockShortfall: shortfall).StockShortfall);
        Assert.Null(new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Conflict,
            PhysicalStockCode).StockShortfall);
        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryRealizationPortException(
            ManufacturingApiStatus.Conflict, InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock,
            stockShortfall: shortfall));
        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryRealizationPortException(
            ManufacturingApiStatus.Unavailable, PhysicalStockCode, stockShortfall: shortfall));
    }

    [Fact]
    public void The_port_shortfall_names_real_identities_trimmed_kind_names_a_positive_request_and_usable_kg_below_it()
    {
        var order = Guid.NewGuid();
        var issue = Guid.NewGuid();
        var ten = new Kilograms(10m);

        foreach (var name in new[] { "", " ", " Issue", "Issue " })
        {
            Assert.Throws<ArgumentException>(() => new ManufacturingInventoryStockShortfallV1(name,
                InputIssueSourceKind, order, issue, Roll, Product, Location, ten, Kilograms.Zero, Kilograms.Zero, null));
            Assert.Throws<ArgumentException>(() => new ManufacturingInventoryStockShortfallV1("Issue", name, order,
                issue, Roll, Product, Location, ten, Kilograms.Zero, Kilograms.Zero, null));
        }

        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, Guid.Empty, issue, Roll, Product, Location, ten, Kilograms.Zero, Kilograms.Zero, null));
        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, Guid.Empty, Roll, Product, Location, ten, Kilograms.Zero, Kilograms.Zero, null));
        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, issue, Guid.Empty, Product, Location, ten, Kilograms.Zero, Kilograms.Zero, null));
        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, issue, Roll, Guid.Empty, Location, ten, Kilograms.Zero, Kilograms.Zero, null));
        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, issue, Roll, Product, Guid.Empty, ten, Kilograms.Zero, Kilograms.Zero, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, issue, Roll, Product, Location, Kilograms.Zero, Kilograms.Zero, Kilograms.Zero, null));

        // Inventory computes the usable kg; a figure no shortfall could carry is refused, not trusted.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, issue, Roll, Product, Location, ten, ten, ten, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, issue, Roll, Product, Location, ten, new Kilograms(12m), new Kilograms(20m),
            null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, issue, Roll, Product, Location, ten, new Kilograms(4m), new Kilograms(3m),
            null));
        var usable = new ManufacturingInventoryStockShortfallV1("Issue", InputIssueSourceKind, order, issue, Roll,
            Product, Location, ten, new Kilograms(9.999m), new Kilograms(15m), new Kilograms(5.001m));
        Assert.Equal((9.999m, 15m, 5.001m),
            (usable.AvailableKg.Value, usable.FreePhysicalKg.Value, usable.ReservedKg!.Value.Value));

        // A label holding no stock anywhere has no place to name; a source without a line is still a source.
        var nowhere = new ManufacturingInventoryStockShortfallV1("Issue", InputIssueSourceKind, order, null, Roll,
            Product, null, ten, Kilograms.Zero, Kilograms.Zero, new Kilograms(3m));
        Assert.Null(nowhere.SourceLineId);
        Assert.Null(nowhere.LocationId);
        Assert.Equal(3m, nowhere.ReservedKg!.Value.Value);
    }

    [Fact]
    public void The_public_view_keeps_the_identities_and_the_lossless_requested_and_usable_text_and_drops_the_rest()
    {
        var order = Guid.NewGuid();
        var issue = Guid.NewGuid();

        var view = ManufacturingCommandService.StockShortfallView(new ManufacturingInventoryStockShortfallV1("Issue",
            InputIssueSourceKind, order, issue, Roll, Product, null, new Kilograms(12.500m), new Kilograms(3.500m),
            new Kilograms(13m), new Kilograms(9.5m)));

        Assert.Null(ManufacturingCommandService.StockShortfallView(null));
        Assert.Equal(new ManufacturingStockShortfallViewV1("Issue", InputIssueSourceKind, order, issue, Roll, Product,
            null, "12.5", "3.5"), view);
        Assert.Equal(
            ["AvailableKg", "LabelId", "LocationId", "MovementKind", "ProductId", "RequestedKg", "SourceId",
                "SourceKind", "SourceLineId"],
            typeof(ManufacturingStockShortfallViewV1).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_detail_is_never_serialized_so_a_recorded_answer_stays_byte_for_byte_what_it_was()
    {
        var property = typeof(ManufacturingApiResult<OrderViewV1>).GetProperty(
            nameof(ManufacturingApiResult<OrderViewV1>.StockShortfall))!;
        Assert.Equal(JsonIgnoreCondition.Always, property.GetCustomAttribute<JsonIgnoreAttribute>()!.Condition);

        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var plain = new ManufacturingApiResult<OrderViewV1>(ManufacturingApiStatus.Conflict, null, false,
            PhysicalStockCode);
        var detail = new ManufacturingStockShortfallViewV1("Issue", InputIssueSourceKind, Guid.NewGuid(),
            Guid.NewGuid(), Roll, Product, Location, "1", "0");
        var detailed = plain with { StockShortfall = detail };
        Assert.Equal(JsonSerializer.Serialize(plain, web), JsonSerializer.Serialize(detailed, web));

        // A member smuggled into stored JSON is not read back either.
        var smuggled = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(plain, web))!.AsObject();
        smuggled["stockShortfall"] = JsonSerializer.SerializeToNode(detail, web);
        Assert.Null(JsonSerializer.Deserialize<ManufacturingApiResult<OrderViewV1>>(smuggled.ToJsonString(), web)!
            .StockShortfall);
    }

    private static ManufacturingInventoryRealizationPortException PhysicalStockRefusal(Guid orderId, Guid issueId,
        decimal requestedKg) =>
        new(ManufacturingApiStatus.Conflict, PhysicalStockCode,
            stockShortfall: PortShortfall(orderId, issueId, requestedKg));

    /// <summary>
    /// Inventory's detail as the host hands it over: 4 kg on the place, 2 kg of it held by other demands, 2 kg usable.
    /// </summary>
    private static ManufacturingInventoryStockShortfallV1 PortShortfall(Guid orderId, Guid issueId, decimal requestedKg) =>
        new("Issue", InputIssueSourceKind, orderId, issueId, Roll, Product, Location, new Kilograms(requestedKg),
            new Kilograms(2m), new Kilograms(4m), new Kilograms(2m));
}
