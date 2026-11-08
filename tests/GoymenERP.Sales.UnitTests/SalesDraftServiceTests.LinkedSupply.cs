using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// S-1 / MOD-03 over SYNTHETIC supply owners (Production U5-a, Purchasing MOD-03): the "bağlı üretim / bağlı satın alma"
/// categories of the cancellation (DEC-SAL-005) and force-close (No 30 / SAT-D12) previews come from
/// <see cref="ISalesOrderLinkedSupplyReadPortV1"/>. No adapter keeps today's "no source" (no blocking); an open link blocks; no
/// link is None; an unreadable, throwing, ambiguous or malformed owner is Unreadable and blocks (the command is 503). The
/// blocking rules themselves are unchanged.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Without_a_linked_supply_adapter_both_categories_stay_no_source_and_nothing_blocks()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        Owners(world);

        var cancel = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        var close = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);

        Assert.True(cancel.Cancellable);
        Assert.True(close.Closable);
        foreach (var source in new[] { SalesCancellationImpactSourcesV1.LinkedProduction, SalesCancellationImpactSourcesV1.LinkedPurchasing })
        {
            Assert.Equal(SalesCancellationImpactStatusesV1.NoSource, cancel.Sources.Single(entry => entry.Source == source).Status);
            Assert.Equal((SalesCancellationImpactStatusesV1.NoSource, false),
                close.Sources.Where(entry => entry.Source == source).Select(entry => (entry.Status, entry.Blocks)).Single());
        }
    }

    [Fact]
    public async Task An_owner_answering_no_open_link_is_none_and_the_order_cancels()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        Owners(world);
        var production = new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Production, (query, kind) => Answer(query, kind));
        var purchasing = new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Purchasing, (query, kind) => Answer(query, kind));
        world.LinkedSupplies.AddRange([production, purchasing]);
        world.Authority.Measured.Clear();

        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);

        Assert.True(preview.Cancellable);
        Assert.Equal((SalesCancellationImpactStatusesV1.None, SalesCancellationImpactStatusesV1.None),
            (Status(preview, SalesCancellationImpactSourcesV1.LinkedProduction), Status(preview, SalesCancellationImpactSourcesV1.LinkedPurchasing)));
        // Each owner is asked for the order in the server company, all lines; the cancel name is measured after each owner too.
        Assert.Equal([new SalesOrderLinkedSupplyQueryV1(Company, approved.Draft.Id, null)], production.Asked);
        Assert.Equal([new SalesOrderLinkedSupplyQueryV1(Company, approved.Draft.Id, null)], purchasing.Asked);
        Assert.Equal(6, world.Authority.Measured.Count);
        var cancelled = await world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "SENTETIK", preview.ImpactToken), Metadata("k-linked-none"));
        Assert.Equal("Cancelled", cancelled.Detail.Draft.State);
    }

    [Theory]
    [InlineData(SalesLinkedSupplyKindsV1.Production, SalesLinkedSupplyDocumentTypesV1.ProductionOrder, SalesCancellationImpactSourcesV1.LinkedProduction)]
    [InlineData(SalesLinkedSupplyKindsV1.Production, SalesLinkedSupplyDocumentTypesV1.ProductionPlan, SalesCancellationImpactSourcesV1.LinkedProduction)]
    [InlineData(SalesLinkedSupplyKindsV1.Purchasing, SalesLinkedSupplyDocumentTypesV1.PurchaseOrder, SalesCancellationImpactSourcesV1.LinkedPurchasing)]
    public async Task An_open_linked_supply_blocks_the_cancellation_and_the_force_close(string kind, string documentType, string source)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        world.LinkedSupplies.Add(new FakeLinkedSupply(kind, (query, answerKind) => Answer(query, answerKind,
            new SalesOrderLinkedSupplyItemV1(Guid.NewGuid(), Guid.NewGuid(), documentType, "SENTETIK-1", line, "Approved", "1250.5",
                KgUnit, null, null))));

        var cancel = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.False(cancel.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Present, Status(cancel, source));
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "SENTETIK", cancel.ImpactToken), Metadata("k-linked-cancel")));
        Assert.Equal((409, SalesDraftProblemCodesV1.CancellationBlocked), Status(blocked));
        Assert.Equal([new SalesDraftFieldError(source, SalesCancellationImpactStatusesV1.Present)], blocked.Errors);

        // SAT-D12: a linked purchase / production order keeps blocking a force-close.
        var close = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        Assert.False(close.Closable);
        Assert.Equal((SalesCancellationImpactStatusesV1.Present, true),
            close.Sources.Where(entry => entry.Source == source).Select(entry => (entry.Status, entry.Blocks)).Single());
        var closeBlocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "SENTETIK", close.ImpactToken), Metadata("k-linked-close")));
        Assert.Equal((409, SalesDraftProblemCodesV1.ForceCloseBlocked), Status(closeBlocked));
        Assert.Empty(world.Store.Cancellations);
    }

    public static TheoryData<string> LinkedSupplyFaults => new()
    {
        "unreadable", "adapter-no-source", "unknown-status", "present-without-items", "none-with-items", "quantity-without-unit",
        "unit-without-quantity", "negative-quantity", "empty-line-id", "throws", "null", "other-company", "other-order", "other-kind", "null-items", "null-item", "empty-id",
        "repeated-id", "foreign-line", "wrong-document-type", "unknown-document-type", "negative-kg", "exponent-kg", "two-adapters",
    };

    [Theory]
    [MemberData(nameof(LinkedSupplyFaults))]
    public async Task An_unreadable_or_malformed_owner_is_unreadable_blocks_and_the_commands_are_503(string fault)
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var line = approved.Lines[0].LineId;
        Owners(world);
        var item = new SalesOrderLinkedSupplyItemV1(Guid.NewGuid(), null, SalesLinkedSupplyDocumentTypesV1.ProductionOrder, null, line,
            null, null, null, null, null);
        Func<SalesOrderLinkedSupplyQueryV1, string, SalesOrderLinkedSupplyAnswerV1> answer = fault switch
        {
            "unreadable" => (query, kind) => new(SalesCancellationImpactStatusesV1.Unreadable, query.CompanyId, query.OrderId, kind, [],
                "manufacturing_store_unavailable"),
            "adapter-no-source" => (query, kind) => Answer(query, kind) with { Status = SalesCancellationImpactStatusesV1.NoSource },
            "unknown-status" => (query, kind) => Answer(query, kind) with { Status = "Maybe" },
            "present-without-items" => (query, kind) => Answer(query, kind) with { Status = SalesCancellationImpactStatusesV1.Present },
            "none-with-items" => (query, kind) => Answer(query, kind, item) with { Status = SalesCancellationImpactStatusesV1.None },
            "quantity-without-unit" => (query, kind) => Answer(query, kind, item with { OpenQuantity = "5" }),
            "unit-without-quantity" => (query, kind) => Answer(query, kind, item with { OpenQuantityUnitId = KgUnit }),
            "negative-quantity" => (query, kind) => Answer(query, kind, item with { OpenQuantity = "-5", OpenQuantityUnitId = KgUnit }),
            "empty-line-id" => (query, kind) => Answer(query, kind, item with { SupplyLineId = Guid.Empty }),
            "throws" => (_, _) => throw new InvalidOperationException("SENTETIK owner outage"),
            "null" => (_, _) => null!,
            "other-company" => (query, kind) => Answer(query, kind) with { CompanyId = Guid.NewGuid() },
            "other-order" => (query, kind) => Answer(query, kind) with { OrderId = Guid.NewGuid() },
            "other-kind" => (query, _) => Answer(query, SalesLinkedSupplyKindsV1.Purchasing),
            "null-items" => (query, kind) => Answer(query, kind) with { OpenItems = null! },
            "null-item" => (query, kind) => Answer(query, kind, null!),
            "empty-id" => (query, kind) => Answer(query, kind, item with { SupplyId = Guid.Empty }),
            "repeated-id" => (query, kind) => Answer(query, kind, item, item),
            "foreign-line" => (query, kind) => Answer(query, kind, item with { OrderLineId = Guid.NewGuid() }),
            "wrong-document-type" => (query, kind) => Answer(query, kind, item with { DocumentType = SalesLinkedSupplyDocumentTypesV1.PurchaseOrder }),
            "unknown-document-type" => (query, kind) => Answer(query, kind, item with { DocumentType = "Kanban" }),
            "negative-kg" => (query, kind) => Answer(query, kind, item with { PlannedKg = "-1" }),
            "exponent-kg" => (query, kind) => Answer(query, kind, item with { ActualKg = "1E3" }),
            _ => (query, kind) => Answer(query, kind),
        };
        world.LinkedSupplies.Add(new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Production, answer));
        if (fault == "two-adapters")
        {
            world.LinkedSupplies.Add(new FakeLinkedSupply(SalesLinkedSupplyKindsV1.Production, (query, kind) => Answer(query, kind)));
        }

        var cancel = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.False(cancel.Cancellable);
        Assert.Equal(SalesCancellationImpactStatusesV1.Unreadable, Status(cancel, SalesCancellationImpactSourcesV1.LinkedProduction));
        // The other kind has no adapter: still no source.
        Assert.Equal(SalesCancellationImpactStatusesV1.NoSource, Status(cancel, SalesCancellationImpactSourcesV1.LinkedPurchasing));
        var cancelRefused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CancelDraftAsync(approved.Draft.Id,
            new SalesDraftCancelRequest("4", "SENTETIK", cancel.ImpactToken), Metadata("k-linked-unreadable")));
        Assert.Equal((503, SalesDraftProblemCodesV1.CancellationImpactUnavailable), Status(cancelRefused));

        var close = await world.Service().GetForceCloseImpactAsync(approved.Draft.Id);
        Assert.False(close.Closable);
        Assert.Equal((SalesCancellationImpactStatusesV1.Unreadable, true), close.Sources
            .Where(entry => entry.Source == SalesCancellationImpactSourcesV1.LinkedProduction).Select(entry => (entry.Status, entry.Blocks)).Single());
        var closeRefused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ForceCloseDraftAsync(approved.Draft.Id,
            new SalesDraftForceCloseRequest("4", "SENTETIK", close.ImpactToken), Metadata("k-linked-unreadable-close")));
        Assert.Equal((503, SalesDraftProblemCodesV1.ForceCloseImpactUnavailable), Status(closeRefused));
        Assert.Empty(world.Store.Cancellations);
    }

    [Fact]
    public void The_linked_supply_contract_is_versioned_and_its_shape_is_pinned()
    {
        // A shape change is a new version (sales.order-linked-supply.v2), never an edit of v1 under the same name.
        Assert.Equal("sales.order-linked-supply.v1", SalesOrderLinkedSupplyContractV1.Name);
        Assert.Equal(["Production", "Purchasing"], SalesLinkedSupplyKindsV1.All);
        Assert.Equal(["CompanyId", "OrderId", "OrderLineIds"], Shape<SalesOrderLinkedSupplyQueryV1>());
        Assert.Equal(["Status", "CompanyId", "OrderId", "SupplyKind", "OpenItems", "ReasonCode"], Shape<SalesOrderLinkedSupplyAnswerV1>());
        Assert.Equal(["SupplyId", "SupplyLineId", "DocumentType", "DocumentNumber", "OrderLineId", "Status", "OpenQuantity",
                "OpenQuantityUnitId", "PlannedKg", "ActualKg"],
            Shape<SalesOrderLinkedSupplyItemV1>());
        Assert.Equal(["SupplyKind", "ReadOrderLinkedSupplyAsync"],
            typeof(ISalesOrderLinkedSupplyReadPortV1).GetMembers().Select(member => member.Name)
                .Where(name => !name.StartsWith("get_", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray()
                .Reverse());
        // The answer's status is the preview's own vocabulary (no second one).
        Assert.Equal(typeof(string), typeof(SalesOrderLinkedSupplyAnswerV1).GetProperty("Status")!.PropertyType);
        Assert.True(new[] { "ProductionPlan", "ProductionOrder", "PurchaseOrder" }.All(SalesLinkedSupplyDocumentTypesV1.IsKnown));
    }

    private static string[] Shape<T>() =>
        [.. typeof(T).GetConstructors().Single().GetParameters().Select(parameter => parameter.Name!)];

    private static string Status(SalesOrderDraftCancellationImpactResponse preview, string source) =>
        preview.Sources.Single(entry => entry.Source == source).Status;

    private static SalesOrderLinkedSupplyAnswerV1 Answer(SalesOrderLinkedSupplyQueryV1 query, string kind,
        params SalesOrderLinkedSupplyItemV1[] items) =>
        new(items.Length > 0 ? SalesCancellationImpactStatusesV1.Present : SalesCancellationImpactStatusesV1.None, query.CompanyId,
            query.OrderId, kind, items, null);

    /// <summary>A SYNTHETIC supply owner adapter: answers what the case gives it and records the queries.</summary>
    private sealed class FakeLinkedSupply(string kind,
        Func<SalesOrderLinkedSupplyQueryV1, string, SalesOrderLinkedSupplyAnswerV1> answer) : ISalesOrderLinkedSupplyReadPortV1
    {
        public List<SalesOrderLinkedSupplyQueryV1> Asked { get; } = [];

        public string SupplyKind { get; } = kind;

        /// <summary>The case's answer; a test may change it between two reads.</summary>
        public Func<SalesOrderLinkedSupplyQueryV1, string, SalesOrderLinkedSupplyAnswerV1> Answer { get; set; } = answer;

        public Task<SalesOrderLinkedSupplyAnswerV1> ReadOrderLinkedSupplyAsync(SalesOrderLinkedSupplyQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(query);
            return Task.FromResult(Answer(query, SupplyKind));
        }
    }
}
