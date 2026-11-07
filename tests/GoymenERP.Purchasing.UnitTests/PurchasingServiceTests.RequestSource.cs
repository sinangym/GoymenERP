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
    private static readonly Guid StockNeed = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000005");
    private static readonly Guid StockNeedLine = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000006");

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
                new PurchaseRequestLineSourceRequest(PurchaseRequestSourceKindsV1.Stock, StockNeed, StockNeedLine)),
        ]);

        var created = await world.Service().CreateRequestAsync(request, Metadata("k-source"));

        // No new name: the request is created under the creation binding, as before A1-2.
        Assert.Equal(CreatePermission, world.Authority.LastPermission);
        Assert.Equal(("Draft", "4"), (created.Detail.Request.Status, created.Detail.Request.Version));
        Assert.Equal(["Manual", "Stock", "Production"], created.Detail.Request.SourceKinds);
        var lines = created.Detail.Lines;
        Assert.Equal(new PurchaseRequestLineSourceView("Production", ProductionPlan, ProductionPlanLine), lines[0].Source);
        Assert.Null(lines[1].Source);
        Assert.Equal(new PurchaseRequestLineSourceView("Stock", StockNeed, StockNeedLine), lines[2].Source);

        var detail = await world.Service().GetRequestDetailAsync(created.Detail.Request.Id);
        Assert.Equal(lines[0].Source, detail.Lines[0].Source);
        var list = await world.Service().GetRequestListAsync(new PurchaseRequestListQuery());
        Assert.Equal(["Manual", "Stock", "Production"], Assert.Single(list.Items).SourceKinds);

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
    public async Task A_sales_sourced_line_is_refused_with_a_field_error_before_any_owner_read_and_nothing_is_stored()
    {
        // A1-2b (SAT-D219, SAT-D220; fail-closed): until MOD-03 reads ISalesOrderLineReferencePortV1 (only an Approved sales
        // order line is linked) and wires the Sales linked-supply read adaptor in the same delivery, a new line cannot record a
        // Sales source. Every such line is named; Stock, Production and manual lines beside it are not.
        var world = new World();
        var request = new CreatePurchaseRequestRequest(Day,
        [
            new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "5",
                new PurchaseRequestLineSourceRequest(PurchaseRequestSourceKindsV1.Sales, SalesOrder, SalesOrderLine)),
            new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "6",
                new PurchaseRequestLineSourceRequest(PurchaseRequestSourceKindsV1.Production, ProductionPlan, ProductionPlanLine)),
            new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "7"),
            new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "8",
                new PurchaseRequestLineSourceRequest(PurchaseRequestSourceKindsV1.Sales, SalesOrder, Guid.NewGuid())),
        ]);
        world.ResetOwnerCalls();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateRequestAsync(request, Metadata("k-sales")));

        Assert.Equal((422, "validation_failed"), (refusal.Status, refusal.Code));
        Assert.Equal(
            [new PurchasingFieldError("lines[0].source", "purchasing_request_source_sales_unavailable"),
             new PurchasingFieldError("lines[3].source", "purchasing_request_source_sales_unavailable")],
            refusal.Errors);
        Assert.Equal((0, 0), (world.Products.Calls, world.Settings.Calls));
        Assert.Empty(world.Store.Envelopes);

        // Not terminal: the same key with the Sales lines corrected (here: manual) is accepted.
        var corrected = request with
        {
            Lines = [request.Lines[0] with { Source = null }, request.Lines[1], request.Lines[2], request.Lines[3] with { Source = null }],
        };
        var created = await world.Service().CreateRequestAsync(corrected, Metadata("k-sales"));
        Assert.False(created.IsReplay);
        Assert.Equal(["Manual", "Production"], created.Detail.Request.SourceKinds);
    }

    [Fact]
    public async Task A_sales_sourced_attempt_recorded_before_A1_2b_still_replays_under_its_key()
    {
        // A1-2b refuses new Sales lines only: the same key and content of an attempt stored before A1-2b replays the stored
        // request (ADR-011) with its Sales source, and nothing new is written.
        var world = new World();
        var lineId = Guid.NewGuid();
        var request = new CreatePurchaseRequestRequest(Day,
            [new PurchaseRequestLineRequest(lineId, Product, "7", KgUnit, "5",
                new PurchaseRequestLineSourceRequest(PurchaseRequestSourceKindsV1.Sales, SalesOrder, SalesOrderLine))]);
        var built = GoymenERP.Modules.Purchasing.Domain.Requests.PurchaseRequest.Create(Guid.NewGuid(), Company, Actor, DateTimeOffset.UtcNow);
        built.AddLine(built.Version, lineId, Product, KgUnit,
            Quantity.Of(5m, QuantityUnit.Kilogram),
            DateTimeOffset.UtcNow,
            GoymenERP.Modules.Purchasing.Domain.Requests.PurchaseRequestLineSource.Of(
                GoymenERP.Modules.Purchasing.Domain.Requests.PurchaseRequestSourceKind.Sales, SalesOrder, SalesOrderLine));
        world.Store.PlantRequestAttempt("k-pre-a12b", Actor, PurchasingRequestHash.Compute(PurchasingCommands.RequestCreateV1, request), built, Day,
            new Dictionary<Guid, string> { [lineId] = "7" }, "SENTETIK-original");

        var replay = await world.Service().CreateRequestAsync(request, Metadata("k-pre-a12b", correlation: "retry"));

        Assert.True(replay.IsReplay);
        Assert.Equal(new PurchaseRequestLineSourceView("Sales", SalesOrder, SalesOrderLine), Assert.Single(replay.Detail.Lines).Source);
        Assert.Equal(["Sales"], replay.Detail.Request.SourceKinds);
        Assert.Empty(world.Store.Envelopes);

        // The stored request stays readable on the detail too.
        var detail = await world.Service().GetRequestDetailAsync(built.Id);
        Assert.Equal("Sales", Assert.Single(detail.Lines).Source!.Kind);
    }

    [Fact]
    public async Task A_caller_without_the_creation_permission_gets_403_before_the_sales_source_422()
    {
        var world = new World();
        world.Authority.Granted = false;
        var request = new CreatePurchaseRequestRequest(Day,
            [new PurchaseRequestLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "5",
                new PurchaseRequestLineSourceRequest(PurchaseRequestSourceKindsV1.Sales, SalesOrder, SalesOrderLine))]);
        world.ResetOwnerCalls();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().CreateRequestAsync(request, Metadata("k-sales-denied")));

        Assert.Equal(403, refusal.Status);
        Assert.Empty(refusal.Errors);
        Assert.Equal((0, 0), (world.Products.Calls, world.Settings.Calls));
        Assert.Empty(world.Store.Envelopes);
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
        // The hash itself, pinned: a manual line's content hash is the one recorded before A1-2.
        Assert.Equal("fc0e79a9b0356eb7c9864b4d5618f2857c3c460b4f47c0888feadda7bb320a9b",
            PurchasingRequestHash.Compute(PurchasingCommands.RequestCreateV1, manual));
        var withSource = manual with
        {
            Lines = [manual.Lines[0] with { Source = new PurchaseRequestLineSourceRequest("Stock", SalesOrder, SalesOrderLine) }],
        };
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.RequestCreateV1, manual),
            PurchasingRequestHash.Compute(PurchasingCommands.RequestCreateV1, withSource));
    }
}
