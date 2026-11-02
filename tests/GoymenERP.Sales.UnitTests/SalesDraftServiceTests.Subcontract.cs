using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D175/D176 (EK25), SAT-D195 (EK29) through the service: the "fason hizmet" flag at create, only service lines (the Product owner's item type,
/// SAT-D149), the per-kg manual price that never asks the price list and goes to the order's internal approval (no discount
/// routing; still under the price-write name, SAT-D151), and the customer lock (fail-closed). Synthetic ports, NOT_ENV.
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
    public async Task SAT_D195_a_per_kg_typed_price_never_asks_the_price_list_and_goes_to_the_orders_internal_approval()
    {
        var references = new FakePriceReferences { Price = 40m, Floor = 35m };
        var world = new World { ItemTypes = new FakeItemTypes(), PriceReferences = references };

        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("k-kg"))).Detail;

        // No list is read; no discount check is made (there is nothing to measure against).
        Assert.Null(references.LastRequest);
        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Equal(("10", "Manual", (SalesDraftPriceReferenceV1?)null, (SalesDraftDiscountLimitCheckV1?)null),
            (pricing.UnitPrice, pricing.PriceSource, pricing.PriceReference, pricing.DiscountCheck));

        // The order goes to its internal approval like any order (SAT-D175); a quantity change re-enters it — never a discount exception.
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(created.Draft.Version), Metadata("k-kg-submit"));
        Assert.Equal("PendingApproval", submitted.Detail.Draft.ApprovalState);
    }

    [Fact]
    public async Task SAT_D195_the_price_list_is_refused_for_a_subcontract_line_before_the_owner_is_asked()
    {
        var references = new FakePriceReferences { Price = 40m, Floor = 35m };
        var world = new World { ItemTypes = new FakeItemTypes(), PriceReferences = references };
        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("k-list"))).Detail;
        var lineId = created.Lines[0].LineId;

        var offered = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId));
        Assert.Equal((422, SalesRuleCodes.SubcontractPriceListNotUsed), (offered.Status, Assert.Single(offered.Errors).Code));

        var named = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "40", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1))),
            Metadata("k-list-rev")));
        Assert.Equal((422, "lines[0].priceReference", SalesRuleCodes.SubcontractPriceListNotUsed),
            (named.Status, Assert.Single(named.Errors).Field, named.Errors[0].Code));
        Assert.Null(references.LastRequest);

        // A typed per-kg price revision is accepted without a discount check.
        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "12", KgUnit, null)), Metadata("k-list-typed"));
        Assert.Null(Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).DiscountCheck);
    }

    [Fact]
    public async Task SAT_D195_a_subcontract_line_is_sold_and_priced_per_kilogram()
    {
        var world = new World { ItemTypes = new FakeItemTypes() };
        var request = Subcontract("10");
        var wrongUnit = request with { Lines = [request.Lines[0] with { PriceUnitId = Guid.NewGuid() }] };

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(wrongUnit, Metadata("k-unit")));

        Assert.Equal(422, refused.Status);
        Assert.Empty(world.Store.Drafts);
    }

    [Fact]
    public async Task A_typed_price_on_a_service_line_still_needs_the_price_write_permission()
    {
        var world = new World { ItemTypes = new FakeItemTypes() };
        world.Authority.Denied.Add(PricePermission);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateDraftAsync(Subcontract("37.5"), Metadata("k-pw")));

        Assert.Equal((403, SalesDraftProblemCodesV1.PriceWriteForbidden), (refused.Status, refused.Code));
        Assert.Empty(world.Store.Drafts);
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
