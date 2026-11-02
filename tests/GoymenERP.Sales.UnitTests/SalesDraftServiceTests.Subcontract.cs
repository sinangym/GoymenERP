using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D175…D178 (EK25; handoff S-3): the incoming subcontract service ("fason hizmet") order through the service — the flag at
/// create, only service lines (the Product owner's item type, SAT-D149), the free manual price on a line whose list holds neither a
/// price nor a floor (SAT-D178; still under the price-write name, SAT-D151), the existing rules when the list prices it, and the
/// customer lock (fail-closed). Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static CreateSalesOrderDraftRequest Subcontract(string? unitPrice = null, string? discount = null)
    {
        var request = RequestWithQuantity("5", null);
        return request with
        {
            SubcontractService = true,
            DueTerms = TestDueTerms.PointDue30Wire,
            Lines = [request.Lines[0] with { UnitPrice = unitPrice, PriceUnitId = unitPrice is null ? null : KgUnit, DiscountPercent = discount }],
        };
    }

    [Fact]
    public async Task A_subcontract_order_with_a_service_line_is_flagged_and_its_line_records_the_owners_item_type()
    {
        var itemTypes = new FakeItemTypes();
        var world = new World { ItemTypes = itemTypes };

        var created = await world.Service().CreateDraftAsync(Subcontract(), Metadata("k-fason"));

        Assert.True(created.Detail.Draft.SubcontractService);
        var stored = Assert.Single(world.Store.Drafts);
        Assert.True(stored.IsSubcontractService);
        Assert.Equal(SalesOrderDraft.ServiceItemType, Assert.Single(stored.Lines).ItemType);
        Assert.Equal([Product], itemTypes.Asked);

        // A normal order asks no item type and carries no flag (its rules are unchanged).
        itemTypes.Asked.Clear();
        var normal = await world.Service().CreateDraftAsync(RequestWithQuantity("5", null), Metadata("k-normal"));
        Assert.Null(normal.Detail.Draft.SubcontractService);
        Assert.Empty(itemTypes.Asked);
    }

    [Theory]
    [InlineData("Steel")]
    [InlineData("not-set")]
    [InlineData("not-found")]
    public async Task A_subcontract_order_refuses_a_goods_line_or_one_whose_item_type_is_not_a_service(string answer)
    {
        var world = new World { ItemTypes = new FakeItemTypes { Answer = answer } };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Subcontract(), Metadata("k-goods")));

        Assert.Equal((422, "lines[0].productId", SalesRuleCodes.SubcontractLineNotService),
            (refused.Status, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        Assert.Empty(world.Store.Drafts);
    }

    [Theory]
    [InlineData("unavailable", 503, "product_item_type_unavailable")]
    [InlineData("denied", 403, "forbidden")]
    [InlineData("missing-port", 503, "product_item_type_unavailable")]
    public async Task An_unreadable_item_type_never_admits_a_subcontract_line(string fault, int status, string code)
    {
        var world = new World { ItemTypes = fault == "missing-port" ? null : new FakeItemTypes { Answer = fault } };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Subcontract(), Metadata("k-x")));

        Assert.Equal((status, code), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Drafts);
    }

    [Fact]
    public async Task A_typed_price_on_a_service_line_the_list_holds_no_price_for_is_free_and_records_who_wrote_it()
    {
        var references = new FakePriceReferences { Missing = true };
        var world = new World { ItemTypes = new FakeItemTypes(), PriceReferences = references };

        var created = (await world.Service().CreateDraftAsync(Subcontract("37.5", "10"), Metadata("k-free"))).Detail;

        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Null(pricing.DiscountCheck);
        Assert.Equal(("TRY", Day, "37.5", Actor), (pricing.ListAbsence!.CurrencyCode, pricing.ListAbsence.EffectiveOn,
            pricing.ListAbsence.UnitPrice, pricing.ListAbsence.WrittenBy));
        Assert.Equal(new ProductPriceResolutionRequestV2(Product, KgUnit, 5m, "TRY", Day, new ProductPriceCustomerV2(Party, Account)),
            references.LastRequest);

        // No discount-limit routing (SAT-D36/D43 lifted for this line only): the order is submitted like an unpriced one.
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(created.Draft.Version), Metadata("k-free-submit"));
        Assert.Equal("PendingApproval", submitted.Detail.Draft.ApprovalState);
    }

    [Fact]
    public async Task K3_review_B_P2_1_a_quantity_revision_into_a_listed_band_puts_the_free_price_back_on_the_approval_path()
    {
        // The probe: 5 kg free at 10 (no list price for 5 kg) → the list prices 20+ kg (40, floor 35) → quantity 5 → 20 → submit.
        var references = new FakePriceReferences { Missing = true };
        var world = new World { ItemTypes = new FakeItemTypes(), PriceReferences = references };
        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("k-band"))).Detail;
        var free = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).ListAbsence!;
        Assert.Equal(("10", "5"), (free.UnitPrice, free.Quantity));

        Owners(world); // the quantity revision reads the loading owner (nothing loaded)
        references.Missing = false;
        references.Price = 40m;
        references.Floor = 35m;
        references.LowerQuantity = 20m;
        references.UpperQuantity = null;
        var revised = await world.Service().ReviseQuantityAsync(created.Draft.Id, new SalesDraftQuantityRevisionRequest(created.Draft.Version,
            "SENTETIK 20 kg", [new SalesDraftLineQuantityV1(created.Lines[0].LineId, "20")]), Metadata("k-band-qty"));

        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Null(pricing.ListAbsence);
        Assert.True(pricing.DiscountCheck!.RequiresApproval);
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id,
            Command(revised.Detail.Draft.Version), Metadata("k-band-submit")));
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, Assert.Single(blocked.Errors).Code);
    }

    [Fact]
    public async Task A_typed_price_on_a_service_line_still_needs_the_price_write_permission()
    {
        var world = new World { ItemTypes = new FakeItemTypes(), PriceReferences = new FakePriceReferences { Missing = true } };
        world.Authority.Denied.Add(PricePermission);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Subcontract("37.5"), Metadata("k-pw")));

        Assert.Equal((403, SalesDraftProblemCodesV1.PriceWriteForbidden), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Drafts);
    }

    [Theory]
    [InlineData("listed")]
    [InlineData("no-owner")]
    public async Task A_service_line_the_list_prices_or_an_unreadable_list_keeps_the_existing_approval_routing(string world_)
    {
        var world = new World
        {
            ItemTypes = new FakeItemTypes(),
            PriceReferences = world_ == "listed" ? new FakePriceReferences { Price = 40m, Floor = 35m } : null,
        };

        var created = (await world.Service().CreateDraftAsync(Subcontract("37.5"), Metadata($"k-{world_}"))).Detail;

        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Null(pricing.ListAbsence);
        Assert.True(pricing.DiscountCheck!.RequiresApproval);
        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id,
            Command(created.Draft.Version), Metadata($"k-{world_}-submit")));
        Assert.Equal(SalesRuleCodes.DiscountExceptionPending, Assert.Single(blocked.Errors).Code);
    }

    [Fact]
    public async Task A_commercial_revision_of_a_subcontract_line_frees_a_typed_price_the_list_has_no_price_for_and_a_normal_order_does_not()
    {
        var world = new World { ItemTypes = new FakeItemTypes(), PriceReferences = new FakePriceReferences { Missing = true } };
        var created = (await world.Service().CreateDraftAsync(Subcontract(), Metadata("k-rev"))).Detail;
        var lineId = created.Lines[0].LineId;

        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "41", KgUnit, null)), Metadata("k-rev-price"));

        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Equal(("41", (SalesDraftDiscountLimitCheckV1?)null), (pricing.ListAbsence!.UnitPrice, pricing.DiscountCheck));

        var normal = (await world.Service().CreateDraftAsync(RequestWithQuantity("5", null) with { DueTerms = TestDueTerms.PointDue30Wire },
            Metadata("k-rev-normal"))).Detail;
        await world.Service().ReviseCommercialAsync(normal.Draft.Id, Revision(normal.Draft.Version,
            new SalesDraftLineCommercialV1(normal.Lines[0].LineId, "41", KgUnit, null)), Metadata("k-rev-normal-price"));
        var normalPricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(normal.Draft.Id)).Lines);
        Assert.Null(normalPricing.ListAbsence);
        Assert.True(normalPricing.DiscountCheck!.RequiresApproval);
    }

    [Theory]
    [InlineData("no-port", 503)]
    [InlineData("unreadable", 503)]
    [InlineData("bound", 422)]
    [InlineData("not-bound", 200)]
    public async Task The_customer_of_a_subcontract_order_changes_only_when_no_plan_or_customer_goods_allocation_is_linked(string binding,
        int status)
    {
        var bindings = new FakeSubcontractBindings
        {
            Outcome = binding switch
            {
                "bound" => SalesSubcontractOrderBindingOutcomeV1.Bound,
                "not-bound" => SalesSubcontractOrderBindingOutcomeV1.NotBound,
                _ => SalesSubcontractOrderBindingOutcomeV1.Unreadable,
            },
        };
        var world = new World { ItemTypes = new FakeItemTypes(), SubcontractBindings = binding == "no-port" ? null : bindings };
        var created = (await world.Service().CreateDraftAsync(Subcontract(), Metadata("k-cust"))).Detail;
        var other = Guid.Parse("5c5c5c5c-5c5c-45c5-85c5-5c5c5c5c5c5c");

        var revise = () => world.Service().ReviseHeaderAsync(created.Draft.Id,
            HeaderRevision(created.Draft.Version, account: other, reason: "SENTETIK yanlış cari"), Metadata("k-cust-revise"));
        if (status == 200)
        {
            var revised = await revise();
            Assert.Equal(other, revised.Detail.Draft.CompanyAccountId);
            Assert.True(revised.Detail.Draft.SubcontractService);
        }
        else
        {
            var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(revise);
            Assert.Equal(status, refused.Status);
            Assert.Equal(status == 503 ? SalesDraftProblemCodesV1.SubcontractBindingUnavailable : "validation_failed", refused.Code);
            if (status == 422)
            {
                Assert.Equal(("companyAccountId", SalesRuleCodes.SubcontractCustomerLocked),
                    (Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
            }
        }

        Assert.Equal(binding is "no-port" ? [] : [(Company, created.Draft.Id)], bindings.Asked);
    }

    private sealed class FakeItemTypes : IProductItemTypeReaderV1
    {
        public string Answer { get; set; } = "Service";
        public List<Guid> Asked { get; } = [];

        public Task<ProductItemTypeReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            Asked.Add(productId);
            return Task.FromResult(Answer switch
            {
                "unavailable" => new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.Unavailable, null, null),
                "denied" => new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.Denied, null, null),
                "not-found" => new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.NotFound, null, null),
                "not-set" => new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.NotSet, null, 1),
                _ => new ProductItemTypeReadV1(ProductItemTypeOutcomeV1.Found, Answer, 1),
            });
        }
    }

    private sealed class FakeSubcontractBindings : ISalesSubcontractOrderBindingReadPortV1
    {
        public SalesSubcontractOrderBindingOutcomeV1 Outcome { get; set; }
        public List<(Guid Company, Guid Order)> Asked { get; } = [];

        public Task<SalesSubcontractOrderBindingAnswerV1> GetAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken = default)
        {
            Asked.Add((companyId, orderId));
            return Task.FromResult(new SalesSubcontractOrderBindingAnswerV1(Outcome));
        }
    }
}
