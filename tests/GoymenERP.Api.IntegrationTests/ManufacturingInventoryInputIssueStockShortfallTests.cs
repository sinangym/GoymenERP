using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Manufacturing.Application;
using InventoryKilograms = GoymenERP.Modules.Inventory.Domain.Quantities.Kilograms;
using ManufacturingKilograms = GoymenERP.Modules.Manufacturing.Domain.Common.Kilograms;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D318 (owner decision of 2026-11-13): the input-issue host carries Inventory's physical-stock shortfall to
/// Manufacturing only when it names exactly the issue the host asked for. Anything else leaves the refusal plain, with
/// the same status and code.
/// </summary>
public sealed class ManufacturingInventoryInputIssueStockShortfallTests
{
    private static readonly Guid Order = Guid.Parse("94000000-0000-4000-8000-000000000001");
    private static readonly Guid Issue = Guid.Parse("94000000-0000-4000-8000-000000000002");
    private static readonly Guid Label = Guid.Parse("94000000-0000-4000-8000-000000000003");
    private static readonly Guid Product = Guid.Parse("94000000-0000-4000-8000-000000000004");
    private static readonly Guid Location = Guid.Parse("94000000-0000-4000-8000-000000000005");
    private static readonly Guid Movement = Guid.Parse("94000000-0000-4000-8000-000000000006");
    private static readonly Guid Other = Guid.Parse("94000000-0000-4000-8000-000000000007");
    private static readonly DateTimeOffset OccurredAt = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_shortfall_naming_this_issue_is_carried_with_every_identity_and_inventorys_figures()
    {
        var carried = ManufacturingInventoryInputIssueHost.StockShortfall(
            Refusal(new Parts { ReservedKg = InventoryKilograms.Of(2m) }.Build()), Command(12.5m));

        Assert.NotNull(carried);
        Assert.Equal(nameof(StockMovementKind.Issue), carried.MovementKind);
        Assert.Equal(InventoryManufacturingRealizationContractV1.InputIssueSourceKind, carried.SourceKind);
        Assert.Equal((Order, (Guid?)Issue, Label, Product, (Guid?)Location),
            (carried.SourceId, carried.SourceLineId, carried.LabelId, carried.ProductId, carried.LocationId));
        Assert.Equal(new ManufacturingKilograms(12.5m), carried.RequestedKg);
        // Inventory's usable kg (4 kg free less 2 kg held by other demands) are carried as Inventory computed them.
        Assert.Equal(new ManufacturingKilograms(2m), carried.AvailableKg);
        Assert.Equal(new ManufacturingKilograms(4m), carried.FreePhysicalKg);
        Assert.Equal(new ManufacturingKilograms(2m), carried.ReservedKg);

        // The ledger's own refusal counts no reservation, and a label that holds no stock has no location.
        var ledger = ManufacturingInventoryInputIssueHost.StockShortfall(
            Refusal(new Parts { LocationId = null, FreePhysicalKg = InventoryKilograms.Zero }.Build()), Command(12.5m));

        Assert.NotNull(ledger);
        Assert.Null(ledger.LocationId);
        Assert.Null(ledger.ReservedKg);
        Assert.Equal((ManufacturingKilograms.Zero, ManufacturingKilograms.Zero),
            (ledger.AvailableKg, ledger.FreePhysicalKg));

        // Without a reservation the usable kg are the free physical kg.
        var unreserved = ManufacturingInventoryInputIssueHost.StockShortfall(Refusal(new Parts().Build()), Command(12.5m));

        Assert.NotNull(unreserved);
        Assert.Equal((new ManufacturingKilograms(4m), new ManufacturingKilograms(4m)),
            (unreserved.AvailableKg, unreserved.FreePhysicalKg));
    }

    [Fact]
    public void The_requested_mass_is_compared_by_value_so_its_scale_never_hides_the_detail()
    {
        var carried = ManufacturingInventoryInputIssueHost.StockShortfall(
            Refusal(new Parts { RequestedKg = InventoryKilograms.Positive(12.500m) }.Build()), Command(12.5m));

        Assert.NotNull(carried);
        Assert.Equal(12.5m, carried.RequestedKg.Value);
    }

    [Theory]
    [InlineData("other-order")]
    [InlineData("other-issue")]
    [InlineData("no-issue-line")]
    [InlineData("other-label")]
    [InlineData("other-quantity")]
    [InlineData("other-source-kind")]
    [InlineData("other-movement-kind")]
    public void A_shortfall_that_names_anything_but_this_issue_is_dropped(string mismatch)
    {
        var parts = mismatch switch
        {
            "other-order" => new Parts { SourceId = Other },
            "other-issue" => new Parts { SourceLineId = Other },
            "no-issue-line" => new Parts { SourceLineId = null },
            "other-label" => new Parts { LabelId = Other },
            "other-quantity" => new Parts { RequestedKg = InventoryKilograms.Positive(12.501m) },
            "other-source-kind" => new Parts { SourceKind = "manufacturing.input-return" },
            "other-movement-kind" => new Parts { MovementKind = StockMovementKind.Scrap },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, "Unknown mismatch."),
        };

        Assert.Null(ManufacturingInventoryInputIssueHost.StockShortfall(Refusal(parts.Build()), Command(12.5m)));
    }

    [Fact]
    public void A_refusal_without_the_detail_or_with_another_code_stays_plain()
    {
        Assert.Null(ManufacturingInventoryInputIssueHost.StockShortfall(
            InventoryManufacturingRealizationException.Refused(409,
                InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient), Command(12.5m)));
        Assert.Null(ManufacturingInventoryInputIssueHost.StockShortfall(
            InventoryManufacturingRealizationException.Refused(409,
                InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock), Command(12.5m)));
        Assert.Null(ManufacturingInventoryInputIssueHost.StockShortfall(
            InventoryManufacturingRealizationException.Refused(503,
                InventoryManufacturingRealizationCodesV1.Unavailable), Command(12.5m)));

        // The detail rides on the physical-stock conflict only; Inventory cannot attach it to any other refusal.
        Assert.Throws<InvalidOperationException>(() => InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.AllocationInsufficientFreeStock, new Parts().Build()));
    }

    [Fact]
    public void The_port_refusal_keeps_its_status_and_code_and_takes_the_detail_only_on_the_physical_stock_conflict()
    {
        var carried = ManufacturingInventoryInputIssueHost.StockShortfall(Refusal(new Parts().Build()), Command(12.5m));
        Assert.NotNull(carried);

        var refusal = new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Conflict,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, stockShortfall: carried);
        Assert.Equal(
            (ManufacturingApiStatus.Conflict, InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient),
            (refusal.Status, refusal.Code));
        Assert.Same(carried, refusal.StockShortfall);

        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryRealizationPortException(
            ManufacturingApiStatus.Conflict, InventoryManufacturingRealizationCodesV1.AllocationExceeded,
            stockShortfall: carried));
        Assert.Throws<ArgumentException>(() => new ManufacturingInventoryRealizationPortException(
            ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient,
            stockShortfall: carried));
    }

    private static InventoryManufacturingRealizationException Refusal(InventoryManufacturingStockShortfallV1 shortfall) =>
        InventoryManufacturingRealizationException.Refused(409,
            InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient, shortfall);

    private static ManufacturingInventoryInputIssuePortCommandV1 Command(decimal kilograms) => new(
        Order, 3, Issue, Label, Movement, new ManufacturingKilograms(kilograms), OccurredAt);

    /// <summary>The shortfall Inventory builds for this issue; each test changes only the part it is about.</summary>
    private sealed record Parts
    {
        public StockMovementKind MovementKind { get; init; } = StockMovementKind.Issue;
        public string SourceKind { get; init; } = InventoryManufacturingRealizationContractV1.InputIssueSourceKind;
        public Guid SourceId { get; init; } = Order;
        public Guid? SourceLineId { get; init; } = Issue;
        public Guid LabelId { get; init; } = Label;
        public Guid? LocationId { get; init; } = Location;
        public InventoryKilograms RequestedKg { get; init; } = InventoryKilograms.Positive(12.5m);
        public InventoryKilograms FreePhysicalKg { get; init; } = InventoryKilograms.Of(4m);
        public InventoryKilograms? ReservedKg { get; init; }

        public InventoryManufacturingStockShortfallV1 Build() => new(MovementKind, SourceKind, SourceId, SourceLineId,
            LabelId, Product, LocationId, RequestedKg, FreePhysicalKg, ReservedKg);
    }
}
