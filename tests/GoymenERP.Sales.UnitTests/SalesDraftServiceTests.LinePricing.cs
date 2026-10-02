using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// AT-018 / AT-021 on the order line (user decision 2026-09-30): the list price is the line's reference and default; the
/// person may overwrite it, give a discount (percent or amount) and a maturity surcharge (percent or amount); reference and
/// adjustments are read back with the calculator's net; a net below the floor is refused; the owner is asked under the
/// price fence in the draft's currency on its business date. Synthetic ports, NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static readonly Guid PriceList = Guid.Parse("abababab-abab-4bab-8bab-abababababab");

    [Fact]
    public async Task A_line_priced_from_the_list_keeps_the_reference_and_the_adjustments_and_reads_back_the_calculators_net()
    {
        var references = new FakePriceReferences { Price = 12.50m, Floor = 10m };
        var world = new World { PriceReferences = references };
        var created = (await world.Service().CreateDraftAsync(RequestWithQuantity("5", null), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;

        var offered = await world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId);
        var candidate = Assert.Single(offered.Items);
        Assert.Equal((PriceList, 1, "12.5", "10", "0", "10", "TRY", Day), (candidate.PriceListId, candidate.VersionNo,
            candidate.UnitPrice, candidate.FloorUnitPrice, candidate.LowerQuantity, candidate.UpperQuantity, offered.CurrencyCode, offered.On));

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, "10", new SalesDraftPriceReferenceRequestV1(PriceList, 1),
                SurchargePercent: "2")), Metadata("k-r1"));
        Assert.Equal("12.50", revised.Detail.Lines[0].UnitPrice);
        // The owner was asked for exactly this list version, the line's product and price unit, on the draft's business date.
        Assert.Equal((PriceList, 1, Product, KgUnit, 5m, Day), references.LastGet);

        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Equal(("Reference", "12.5", "10", "2", "11.475"), (pricing.PriceSource, pricing.PriceReference!.UnitPrice, pricing.DiscountPercent,
            pricing.SurchargePercent, pricing.NetUnitPrice));
        Assert.Equal(("SENTETIK liste", "10", "ExcludingVat", "0", "10"), (pricing.PriceReference.PriceListName,
            pricing.PriceReference.FloorUnitPrice, pricing.PriceReference.VatBasis, pricing.PriceReference.LowerQuantity,
            pricing.PriceReference.UpperQuantity));
    }

    [Fact]
    public async Task Quantity_at_a_touching_upper_bound_selects_the_second_tier_and_a_gap_has_no_automatic_price()
    {
        var references = new FakePriceReferences
        {
            Price = 12.50m,
            UpperQuantity = 10m,
            SecondPrice = 11m,
            SecondLowerQuantity = 10m,
            SecondUpperQuantity = 20m
        };
        var world = new World { PriceReferences = references };
        var created = (await world.Service().CreateDraftAsync(RequestWithQuantity("10", null), Metadata("k-tier"))).Detail;
        var lineId = created.Lines[0].LineId;

        var candidate = Assert.Single((await world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId)).Items);
        Assert.Equal(("11", "10", "20"), (candidate.UnitPrice, candidate.LowerQuantity, candidate.UpperQuantity));

        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "11", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1))),
            Metadata("k-tier-price"));
        var saved = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).PriceReference!;
        Assert.Equal(("10", "20"), (saved.LowerQuantity, saved.UpperQuantity));

        var gapReferences = new FakePriceReferences
        {
            Price = 12.50m,
            UpperQuantity = 10m,
            SecondPrice = 11m,
            SecondLowerQuantity = 11m,
            SecondUpperQuantity = 20m
        };
        var gapWorld = new World { PriceReferences = gapReferences };
        var gapDraft = (await gapWorld.Service().CreateDraftAsync(RequestWithQuantity("10", null), Metadata("k-gap"))).Detail;
        var gapLineId = gapDraft.Lines[0].LineId;
        Assert.Empty((await gapWorld.Service().GetLinePriceReferencesAsync(gapDraft.Draft.Id, gapLineId)).Items);
        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => gapWorld.Service().ReviseCommercialAsync(gapDraft.Draft.Id,
            Revision(gapDraft.Draft.Version,
                new SalesDraftLineCommercialV1(gapLineId, "11", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1))),
            Metadata("k-gap-price")));
        Assert.Equal(SalesDraftProblemCodesV1.PriceReferenceUnavailable, Assert.Single(unavailable.Errors).Code);
    }

    [Fact]
    public async Task An_overwritten_price_with_amount_adjustments_is_manual_and_keeps_the_reference_visible()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m } };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;

        await world.Service().ReviseCommercialAsync(created.Draft.Id, Revision(created.Draft.Version,
            new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1),
                DiscountAmount: "0.50", SurchargeAmount: "0.25")), Metadata("k-r1"));

        var pricing = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines);
        Assert.Equal(("Manual", "13.00", "12.5", "0.50", "0.25", "12.75"), (pricing.PriceSource, pricing.UnitPrice, pricing.PriceReference!.UnitPrice,
            pricing.DiscountAmount, pricing.SurchargeAmount, pricing.NetUnitPrice));
    }

    [Fact]
    public async Task A_net_below_the_list_floor_is_pending_until_a_different_actor_approves_every_line_with_a_reason()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m, Floor = 12m } };
        world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;

        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, "10",
                new SalesDraftPriceReferenceRequestV1(PriceList, 1))), Metadata("k-r1"));
        var pending = Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines).FloorException!;
        Assert.Equal((Actor, null, null), (pending.RequestedBy, pending.ApprovedBy, pending.Reason));

        var blocked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().SubmitDraftAsync(created.Draft.Id,
            new SalesDraftStateCommandRequest(revised.Detail.Draft.Version), Metadata("k-submit-blocked")));
        Assert.Equal(SalesRuleCodes.PriceFloorExceptionPending, Assert.Single(blocked.Errors).Code);

        world.User.Id = "synthetic-approver";
        var stale = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApprovePriceFloorExceptionsAsync(created.Draft.Id,
            new SalesDraftPriceFloorExceptionApprovalRequest("1",
                [new SalesDraftPriceFloorExceptionApprovalLineV1(lineId, "SENTETIK müşteri sözleşmesi")]), Metadata("k-floor-stale")));
        Assert.Equal(409, stale.Status);
        var approved = await world.Service().ApprovePriceFloorExceptionsAsync(created.Draft.Id,
            new SalesDraftPriceFloorExceptionApprovalRequest(revised.Detail.Draft.Version,
                [new SalesDraftPriceFloorExceptionApprovalLineV1(lineId, "SENTETIK müşteri sözleşmesi")]), Metadata("k-floor-approve"));
        Assert.Equal("synthetic-approver", Assert.Single((await world.Service().GetDraftLinePricingAsync(created.Draft.Id)).Lines)
            .FloorException!.ApprovedBy);
        await world.Service().SubmitDraftAsync(created.Draft.Id,
            new SalesDraftStateCommandRequest(approved.Detail.Draft.Version), Metadata("k-submit"));

        var replay = await world.Service().ApprovePriceFloorExceptionsAsync(created.Draft.Id,
            new SalesDraftPriceFloorExceptionApprovalRequest(revised.Detail.Draft.Version,
                [new SalesDraftPriceFloorExceptionApprovalLineV1(lineId, "SENTETIK müşteri sözleşmesi")]), Metadata("k-floor-approve"));
        Assert.True(replay.IsReplay);
        Assert.Single(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApprovePriceFloorExceptionsV1);
        var changedBody = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApprovePriceFloorExceptionsAsync(created.Draft.Id,
            new SalesDraftPriceFloorExceptionApprovalRequest(revised.Detail.Draft.Version,
                [new SalesDraftPriceFloorExceptionApprovalLineV1(lineId, "SENTETIK başka gerekçe")]), Metadata("k-floor-approve")));
        Assert.Equal(409, changedBody.Status);
    }

    [Fact]
    public async Task A_floor_exception_refuses_the_requester_and_a_revoked_approver_without_a_second_effect()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m, Floor = 12m } };
        world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-floor-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        var revised = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, "10",
                new SalesDraftPriceReferenceRequestV1(PriceList, 1))), Metadata("k-floor-revise"));
        var request = new SalesDraftPriceFloorExceptionApprovalRequest(revised.Detail.Draft.Version,
            [new SalesDraftPriceFloorExceptionApprovalLineV1(lineId, "SENTETIK gerekçe")]);

        var same = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApprovePriceFloorExceptionsAsync(
            created.Draft.Id, request, Metadata("k-floor-same")));
        Assert.Equal(SalesRuleCodes.PriceFloorExceptionSameActor, Assert.Single(same.Errors).Code);

        world.User.Id = "synthetic-approver";
        world.Store.OnStore = () => world.Authority.Denied.Add(SchedulePermission);
        var revoked = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApprovePriceFloorExceptionsAsync(
            created.Draft.Id, request, Metadata("k-floor-revoked")));
        Assert.Equal(403, revoked.Status);
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ApprovePriceFloorExceptionsV1);
    }

    [Fact]
    public async Task A_null_floor_exception_line_is_a_controlled_400_before_projection_or_store_access()
    {
        var world = new World();
        world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        var request = new SalesDraftPriceFloorExceptionApprovalRequest("1", [null!]);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApprovePriceFloorExceptionsAsync(
            Guid.NewGuid(), request, Metadata("k-floor-null")));

        Assert.Equal((400, "lines", "validation_failed"),
            (refused.Status, Assert.Single(refused.Errors).Field, Assert.Single(refused.Errors).Code));
        Assert.Empty(world.Store.Transitions);
    }

    [Fact]
    public async Task A_version_that_no_longer_prices_the_line_another_currency_or_two_discount_kinds_are_refused_before_the_save()
    {
        var references = new FakePriceReferences { Price = 12.50m, Missing = true };
        var world = new World { PriceReferences = references };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;
        var named = new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1));

        var gone = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, named), Metadata("k-r1")));
        Assert.Equal((422, "lines[0].priceReference", SalesDraftProblemCodesV1.PriceReferenceUnavailable),
            (gone.Status, Assert.Single(gone.Errors).Field, Assert.Single(gone.Errors).Code));

        references.Missing = false;
        references.Currency = "USD";
        var currency = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, named), Metadata("k-r2")));
        Assert.Equal(SalesDraftProblemCodesV1.PriceReferenceCurrencyMismatch, Assert.Single(currency.Errors).Code);

        references.Currency = "TRY";
        var both = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, named with { DiscountPercent = "5", DiscountAmount = "1" }), Metadata("k-r3")));
        Assert.Equal(("lines", SalesRuleCodes.AdjustmentKindConflict), (Assert.Single(both.Errors).Field, Assert.Single(both.Errors).Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
    }

    [Fact]
    public async Task Only_vat_exclusive_net_list_prices_are_offered_or_accepted()
    {
        var references = new FakePriceReferences { Price = 12.50m, VatBasis = "IncludingVat" };
        var world = new World { PriceReferences = references };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-vat"))).Detail;
        var lineId = created.Lines[0].LineId;

        Assert.Empty((await world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId)).Items);
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "12.50", KgUnit, null,
                new SalesDraftPriceReferenceRequestV1(PriceList, 1))), Metadata("k-vat-revision")));

        Assert.Equal((422, "lines[0].priceReference", SalesDraftProblemCodesV1.PriceReferenceUnavailable),
            (refused.Status, Assert.Single(refused.Errors).Field, Assert.Single(refused.Errors).Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
    }

    [Fact]
    public async Task Without_the_owner_the_reference_is_503_and_each_read_needs_its_own_price_name()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-1"))).Detail;
        var lineId = created.Lines[0].LineId;

        var unavailable = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId));
        Assert.Equal((503, SalesDraftProblemCodesV1.PriceListProviderUnavailable), (unavailable.Status, unavailable.Code));
        var revision = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "1", KgUnit, null, new SalesDraftPriceReferenceRequestV1(PriceList, 1))),
            Metadata("k-r1")));
        Assert.Equal(503, revision.Status);

        world.PriceReferences = new FakePriceReferences { Price = 1m };
        world.Authority.Denied.Add(PricePermission);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLinePriceReferencesAsync(created.Draft.Id, lineId))).Status);
        world.Authority.Denied.Clear();
        world.Authority.Denied.Add(PriceReadPermission);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetDraftLinePricingAsync(created.Draft.Id))).Status);
        world.Authority.Denied.Clear();
        Assert.Equal(404, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().GetLinePriceReferencesAsync(created.Draft.Id, Guid.NewGuid()))).Status);
    }

    [Fact]
    public async Task Unrepresentable_percentage_and_representable_overflow_are_field_refusals_with_no_effect()
    {
        var world = new World();
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("k-number-create"))).Detail;
        var lineId = created.Lines[0].LineId;

        var tiny = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "1", KgUnit,
                "0.00000000000000000000000000001")), Metadata("k-number-tiny")));
        Assert.Equal((422, "lines[0].discountPercent", SalesRuleCodes.DiscountPercentUnrepresentable),
            (tiny.Status, Assert.Single(tiny.Errors).Field, Assert.Single(tiny.Errors).Code));

        var overflow = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "79228162514264337593543950335", KgUnit,
                SurchargePercent: "100")), Metadata("k-number-overflow")));
        Assert.Equal((422, "lines[0].unitPrice", SalesRuleCodes.PriceCalculationOverflow),
            (overflow.Status, Assert.Single(overflow.Errors).Field, Assert.Single(overflow.Errors).Code));
        Assert.DoesNotContain(world.Store.Transitions, item => item.Command == SalesDraftCommands.ReviseCommercialV1);
    }

    private sealed class FakePriceReferences : IProductQuantityPriceReferenceReaderV1
    {
        public decimal Price { get; set; }
        public decimal? Floor { get; set; }
        public string Currency { get; set; } = "TRY";
        public bool Missing { get; set; }
        public decimal LowerQuantity { get; set; }
        public decimal? UpperQuantity { get; set; } = 10m;
        public decimal? SecondPrice { get; set; }
        public decimal SecondLowerQuantity { get; set; } = 10m;
        public decimal? SecondUpperQuantity { get; set; } = 20m;
        public string VatBasis { get; set; } = "ExcludingVat";
        public (Guid List, int Version, Guid Product, Guid Unit, decimal Quantity, DateOnly On)? LastGet { get; private set; }

        public Task<IReadOnlyList<ProductPriceReferenceV1>> SearchAsync(Guid productId, Guid priceUnitId, decimal quantity,
            string currencyCode, DateOnly on, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductPriceReferenceV1>>(Select(productId, priceUnitId, quantity, currencyCode) is { } selected
                ? [selected]
                : []);

        public Task<ProductPriceReferenceV1?> GetAsync(Guid priceListId, int versionNo, Guid productId, Guid priceUnitId,
            decimal quantity, DateOnly on, CancellationToken cancellationToken = default)
        {
            LastGet = (priceListId, versionNo, productId, priceUnitId, quantity, on);
            return Task.FromResult(priceListId != PriceList ? null : Select(productId, priceUnitId, quantity, Currency));
        }

        private ProductPriceReferenceV1? Select(Guid productId, Guid unitId, decimal quantity, string currencyCode)
        {
            if (Missing || currencyCode != Currency || unitId != KgUnit)
            {
                return null;
            }

            if (quantity >= LowerQuantity && (UpperQuantity is null || quantity < UpperQuantity))
            {
                return Reference(productId, unitId, Price, LowerQuantity, UpperQuantity);
            }

            return SecondPrice is { } second && quantity >= SecondLowerQuantity
                && (SecondUpperQuantity is null || quantity < SecondUpperQuantity)
                ? Reference(productId, unitId, second, SecondLowerQuantity, SecondUpperQuantity)
                : null;
        }

        private ProductPriceReferenceV1 Reference(Guid productId, Guid unitId, decimal price, decimal lower, decimal? upper) =>
            new(PriceList, "SENTETIK liste", Guid.NewGuid(), 1, Day, null, productId, unitId, Currency, "Sales", VatBasis, price, Floor,
                lower, upper);
    }
}
