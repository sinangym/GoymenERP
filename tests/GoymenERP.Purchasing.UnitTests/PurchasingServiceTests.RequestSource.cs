using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A1-2 (plan API-02; Üretim A-2, Satış S-A1, ALS-D16): the request line's source through the service and the wire — set at
/// create under the unchanged creation name, shown on the detail line and in the summary's kinds, strict on the wire, and
/// absent from the content hash of a manual line (ADR-011 attempts recorded before A1-2 keep replaying). SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class PurchasingServiceTests
{
    private static readonly Guid SalesOrder = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001");
    private static readonly Guid SalesOrderLine = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000002");
    private static readonly Guid ProductionPlan = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000003");
    private static readonly Guid ProductionPlanLine = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000004");

    [Fact]
    public async Task A_request_is_created_with_sourced_and_manual_lines_under_the_creation_name_and_shows_them()
    {
        var world = new World();
        var request = new CreatePurchaseRequestRequest(Day,
        [
            new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "5",
                new PurchaseRequestLineSourceRequest(PurchaseRequestSourceKindsV1.Production, ProductionPlan, ProductionPlanLine)),
            new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "6"),
            new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "7",
                new PurchaseRequestLineSourceRequest(PurchaseRequestSourceKindsV1.Sales, SalesOrder, SalesOrderLine)),
        ]);

        var created = await world.Service().CreateRequestAsync(request, Metadata("k-source"));

        // No new name: the request is created under the creation binding, as before A1-2.
        Assert.Equal(CreatePermission, world.Authority.LastPermission);
        Assert.Equal(("Draft", "4"), (created.Detail.Request.Status, created.Detail.Request.Version));
        Assert.Equal(["Manual", "Sales", "Production"], created.Detail.Request.SourceKinds);
        var lines = created.Detail.Lines;
        Assert.Equal(new PurchaseRequestLineSourceView("Production", ProductionPlan, ProductionPlanLine), lines[0].Source);
        Assert.Null(lines[1].Source);
        Assert.Equal(new PurchaseRequestLineSourceView("Sales", SalesOrder, SalesOrderLine), lines[2].Source);

        var detail = await world.Service().GetRequestDetailAsync(created.Detail.Request.Id);
        Assert.Equal(lines[0].Source, detail.Lines[0].Source);
        var list = await world.Service().GetRequestListAsync(new PurchaseRequestListQuery());
        Assert.Equal(["Manual", "Sales", "Production"], Assert.Single(list.Items).SourceKinds);

        // The same key and content replays the stored lines with their sources; the same key with another source is refused.
        var replay = await world.Service().CreateRequestAsync(request, Metadata("k-source", correlation: "retry"));
        Assert.True(replay.IsReplay);
        Assert.Equal(lines[2].Source, replay.Detail.Lines[2].Source);
        var changed = request with
        {
            Lines = [request.Lines[0] with { Source = request.Lines[0].Source! with { DocumentLineId = Guid.NewGuid() } }, request.Lines[1], request.Lines[2]],
        };
        var mismatch = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateRequestAsync(changed, Metadata("k-source")));
        Assert.Equal((409, PurchasingProblemCodesV1.SameKeyPayloadMismatch), (mismatch.Status, mismatch.Code));
    }

    [Fact]
    public async Task A_request_whose_lines_are_all_manual_names_only_Manual()
    {
        var world = new World();
        var created = await world.Service().CreateRequestAsync(RequestOf("5"), Metadata("k-manual"));
        Assert.Equal(["Manual"], created.Detail.Request.SourceKinds);
        Assert.Null(Assert.Single(created.Detail.Lines).Source);
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("sales")]
    [InlineData("")]
    [InlineData("Purchasing")]
    public async Task A_kind_the_wire_does_not_admit_is_a_400_at_the_service_too_and_nothing_is_stored(string kind)
    {
        var world = new World();
        var request = new CreatePurchaseRequestRequest(Day,
            [new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "5", new PurchaseRequestLineSourceRequest(kind, SalesOrder, SalesOrderLine))]);

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateRequestAsync(request, Metadata("k-bad")));
        Assert.Equal((400, "validation_failed"), (refusal.Status, refusal.Code));
        Assert.Empty(world.Store.Envelopes);
    }

    [Fact]
    public void The_line_source_on_the_wire_is_strict_and_a_manual_line_keeps_its_content_hash()
    {
        string Line(string source) =>
            $"{{\"lineId\":\"{Guid.NewGuid():D}\",\"productId\":\"{Product:D}\",\"expectedProductVersion\":\"7\",\"unitId\":\"{KgUnit:D}\",\"quantity\":\"6\"{source}}}";
        string Body(string source) => $"{{\"effectiveOn\":\"2026-09-19\",\"lines\":[{Line(source)}]}}";

        var sourced = PurchasingJson.ReadCreateRequest(Body($",\"source\":{{\"kind\":\"Sales\",\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{SalesOrderLine:D}\"}}"));
        Assert.Equal(new PurchaseRequestLineSourceRequest("Sales", SalesOrder, SalesOrderLine), sourced.Lines[0].Source);
        Assert.Null(PurchasingJson.ReadCreateRequest(Body(",\"source\":null")).Lines[0].Source);
        Assert.Null(PurchasingJson.ReadCreateRequest(Body("")).Lines[0].Source);

        foreach (var bad in new[]
        {
            $",\"source\":{{\"kind\":\"Manual\",\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{SalesOrderLine:D}\"}}",
            $",\"source\":{{\"kind\":\"SALES\",\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{SalesOrderLine:D}\"}}",
            $",\"source\":{{\"kind\":\"Sales\",\"documentId\":\"{Guid.Empty:D}\",\"documentLineId\":\"{SalesOrderLine:D}\"}}",
            $",\"source\":{{\"kind\":\"Sales\",\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{Guid.Empty:D}\"}}",
            $",\"source\":{{\"kind\":\"Sales\",\"documentId\":\"{SalesOrder:D}\"}}",
            $",\"source\":{{\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{SalesOrderLine:D}\"}}",
            $",\"source\":{{\"kind\":null,\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{SalesOrderLine:D}\"}}",
            $",\"source\":{{\"kind\":\"Sales\",\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{SalesOrderLine:D}\",\"version\":\"1\"}}",
            $",\"source\":{{\"kind\":2,\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{SalesOrderLine:D}\"}}",
            ",\"source\":\"Sales\"",
            $",\"Source\":{{\"kind\":\"Sales\",\"documentId\":\"{SalesOrder:D}\",\"documentLineId\":\"{SalesOrderLine:D}\"}}",
        })
        {
            Assert.Throws<System.Text.Json.JsonException>(() => PurchasingJson.ReadCreateRequest(Body(bad)));
        }

        // ADR-011: a manual line serialises exactly as before A1-2 (no "source" member), so its content hash is unchanged;
        // a sourced line hashes differently from the same line without a source.
        var lineId = Guid.Parse("b2b2b2b2-0000-4000-8000-000000000001");
        var manual = new CreatePurchaseRequestRequest(Day, [new PurchaseRequestLineRequest(lineId, Product, "7", KgUnit, "6")]);
        var canonical = System.Text.Json.JsonSerializer.Serialize(new { command = PurchasingCommands.RequestCreateV1, request = manual }, PurchasingJson.Options);
        Assert.DoesNotContain("source", canonical, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "{\"command\":\"" + PurchasingCommands.RequestCreateV1 + "\",\"request\":{\"effectiveOn\":\"2026-09-19\",\"lines\":[{\"lineId\":\"b2b2b2b2-0000-4000-8000-000000000001\","
            + $"\"productId\":\"{Product:D}\",\"expectedProductVersion\":\"7\",\"unitId\":\"{KgUnit:D}\",\"quantity\":\"6\"}}]}}}}",
            canonical);
        var withSource = manual with
        {
            Lines = [manual.Lines[0] with { Source = new PurchaseRequestLineSourceRequest("Stock", SalesOrder, SalesOrderLine) }],
        };
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.RequestCreateV1, manual),
            PurchasingRequestHash.Compute(PurchasingCommands.RequestCreateV1, withSource));
    }
}
