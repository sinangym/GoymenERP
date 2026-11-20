using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Partial_invoicing_requires_explicit_source_shares_before_a_pending_return_is_saved()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var sources = new ReturnSourcesFake();
        var request = ReturnRequest();
        var service = world.Service(returns: returns, returnSources: sources);
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("allocation-required")));
        Assert.Equal((409, PurchaseSupplierReturnCostAllocationContractV1.Required), (error.Status, error.Code));
        Assert.Equal(0, returns.NewWrites);
        var replay = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.CreateSupplierReturnAsync(order, request, Metadata("allocation-required")));
        Assert.True(replay.IsReplay);
        Assert.Equal(1, sources.Calls);
    }

    [Fact]
    public async Task Supplier_return_source_selection_survives_pending_approval_detail_and_immutable_creation_replay()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var sources = new ReturnSourcesFake();
        var host = new ReturnAdjustmentHost();
        var allocation = sources.Allocation();
        var request = ReturnRequest() with { CostAllocation = allocation };
        var service = world.Service(returns: returns, reversals: host, returnSources: sources);
        var created = await service.CreateSupplierReturnAsync(order, request, Metadata("selected-return"));
        Assert.Equal(allocation, created.Return.CostAllocation);
        Assert.Equal(allocation, returns.Stored!.Return.CostAllocation);
        await world.AsApprover(() => service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId,
            new("1", null), Metadata("selected-approval")));
        Assert.Equal(allocation, host.Last!.CostAllocation);
        Assert.Equal(allocation, (await service.GetSupplierReturnAsync(order, request.ReturnId)).Return.CostAllocation);
        sources.Version = "changed-after-commit";
        var replay = await service.CreateSupplierReturnAsync(order, request, Metadata("selected-return"));
        Assert.True(replay.IsReplay);
        Assert.Equal(allocation, replay.Return.CostAllocation);
        Assert.Equal("PendingCostException", replay.Return.Status);
        Assert.Equal(1, sources.Calls);
        var mismatch = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.CreateSupplierReturnAsync(order,
            request with { CostAllocation = allocation with { ExpectedSourceVersion = "different" } }, Metadata("selected-return")));
        Assert.Equal(PurchasingProblemCodesV1.SameKeyPayloadMismatch, mismatch.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Supplier_return_source_preview_needs_fresh_read_authority_but_no_price_grant(bool revokeDuringRead)
    {
        var (world, order, returns) = await ReturnWorldAsync();
        world.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var sources = new ReturnSourcesFake { AfterRead = revokeDuringRead ? () => world.Authority.Denied.Add(ReadPermission) : null };
        var service = world.Service(returns: returns, returnSources: sources);
        if (revokeDuringRead)
            Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
                service.GetSupplierReturnCostSourcesAsync(order, Guid.NewGuid()))).Status);
        else
        {
            var preview = await service.GetSupplierReturnCostSourcesAsync(order, Guid.NewGuid());
            Assert.Equal("SENTETIK-FATURA", Assert.Single(preview.InvoicedSources).InvoiceNumber);
            Assert.Equal(new PurchaseSupplierReturnExactQuantityV1("1", "1"), preview.PendingQuantityKg);
        }
        Assert.Equal(0, returns.NewWrites);
    }

    [Fact]
    public async Task Supplier_return_preview_from_another_company_is_rejected_before_disclosure()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var service = world.Service(returns: returns, returnSources: new ReturnSourcesFake { WrongCompany = true });
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.GetSupplierReturnCostSourcesAsync(order, Guid.NewGuid()))).Status);
    }

    private sealed class ReturnSourcesFake : IPurchaseSupplierReturnCostSourceReadPortV1
    {
        private readonly Guid sourceId = Guid.NewGuid();
        public int Calls { get; private set; }
        public string Version { get; set; } = "source-v1";
        public Action? AfterRead { get; init; }
        public bool WrongCompany { get; init; }
        public bool FullyInvoiced { get; init; }
        public PurchaseSupplierReturnCostAllocationV1 Allocation() => new(Version, new(false, "1", "1"),
            [new(sourceId, false, "0.9", "1")]);
        public Task<PurchaseSupplierReturnCostSourcesV1> GetAsync(Guid companyId, Guid orderId, Guid goodsAcceptanceId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            AfterRead?.Invoke();
            return Task.FromResult(new PurchaseSupplierReturnCostSourcesV1(WrongCompany ? Guid.NewGuid() : companyId,
                goodsAcceptanceId, Guid.NewGuid(), Version, new(FullyInvoiced ? "0" : "1", "1"), new(FullyInvoiced ? "0" : "1", "1"),
                [new(sourceId, Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-FATURA", 1,
                    new(FullyInvoiced ? "19" : "9", "10"), new(FullyInvoiced ? "2" : "1", "1"),
                    WholePairAvailable: true)], PendingWholePairAvailable: true));
        }
    }

    [Fact]
    public async Task Supplier_return_legacy_pending_without_purchase_units_requires_new_explicit_request_but_remains_rejectable_and_replayable()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var request = ReturnRequest() with { ExpectedPurchaseOrderVersion = null, UndoQuantity = null, ReplacementExpected = false };
        await world.Service(returns: returns).CreateSupplierReturnAsync(order, request, Metadata("legacy-pending"));
        var sources = new ReturnSourcesFake { FullyInvoiced = true };
        var service = world.Service(returns: returns, returnSources: sources);
        var refused = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("legacy-missing-units"))));
        Assert.Equal((409, PurchaseSupplierReturnCostAllocationContractV1.Required), (refused.Status, refused.Code));
        Assert.Equal(0, returns.DecisionWrites);
        var originalReplay = await service.CreateSupplierReturnAsync(order, request, Metadata("legacy-pending"));
        Assert.True(originalReplay.IsReplay);
        Assert.Equal("PendingCostException", originalReplay.Return.Status);
        Assert.Equal(1, sources.Calls);
        var rejected = await world.AsApprover(() => service.RejectSupplierReturnCostExceptionAsync(order, request.ReturnId,
            new("1", "SENTETIK replace with explicit quantities"), Metadata("legacy-reject")));
        Assert.Equal("Rejected", rejected.Return.Status);
        Assert.Equal(1, returns.DecisionWrites);
    }
}
