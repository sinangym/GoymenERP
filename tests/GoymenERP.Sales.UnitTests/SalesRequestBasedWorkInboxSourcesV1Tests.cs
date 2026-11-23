using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// WP4 (SAT-D475 / SAT-D476): only a REQUEST waits — the salesperson's conversion request of an expired quote and the over-delivery
/// request the shipment gate opened. The quote request names no maker (SAT-D44 (2)); the over-delivery request names its shipper
/// as the maker (SAT-D216) and carries the recorded quantities.
/// </summary>
public sealed class SalesRequestBasedWorkInboxSourcesV1Tests
{
    private static readonly Guid Quote = Guid.Parse("5a1e0000-0000-0000-000c-000000000001");
    private static readonly Guid Order = Guid.Parse("5a1e0000-0000-0000-000d-000000000001");

    [Fact]
    public async Task A_quote_conversion_request_waits_from_its_request_links_the_quote_and_names_no_maker()
    {
        var store = new SalesWorkInboxApprovalFakeStore { QuoteRequests = [QuoteRequest(2, T0)] };
        var source = QuoteSource(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        var item = Assert.Single(result.Items!);
        Assert.Equal(("sales.quote-conversion-request.v1", SalesPermissionManifestV1.QuotesExpiredConversionApprove),
            (source.Kind, source.ApproverPermission));
        Assert.Equal(("TKL-SNT-0001 #2", $"/satis/teklifler/{Quote:D}", T0.ToUniversalTime()),
            (item.DocumentLabel, item.Link, item.WaitingSinceUtc));
        Assert.Null(item.PreparedBy);
        Assert.Equal(new WorkApprovalRuleResultV1(SalesQuoteConversionRequestWorkInboxSourceV1.RequestedRule, null, null, null, null),
            Assert.Single(item.Rules));
        Assert.Equal(("sales-quote", (Guid?)Quote), (Assert.Single(item.References).Role, item.References[0].RecordId));
        AssertContractShape(source, result, ownerNamesMaker: false);
    }

    [Fact]
    public async Task An_over_delivery_request_names_its_shipper_as_maker_and_the_recorded_quantities()
    {
        var store = new SalesWorkInboxApprovalFakeStore { OverDeliveryRequests = [OverDeliveryRequest(1, T0)] };
        var source = OverDeliverySource(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        var item = Assert.Single(result.Items!);
        Assert.Equal(("sales.over-delivery-request.v1", SalesPermissionManifestV1.OrdersOverDeliveryApprove),
            (source.Kind, source.ApproverPermission));
        Assert.Equal(("SO-SNT-0007 #1", $"/satis/siparisler/{Order:D}"), (item.DocumentLabel, item.Link));
        Assert.Equal(Maker, item.PreparedBy);
        Assert.True(item.Excludes(Maker));
        Assert.False(item.Excludes(Approver));
        Assert.Equal(new WorkApprovalRuleResultV1(SalesOverDeliveryRequestWorkInboxSourceV1.RequestedRule, "100", "112.5", "12.5", null),
            Assert.Single(item.Rules));
        AssertContractShape(source, result);
    }

    [Fact]
    public async Task An_inconsistent_over_delivery_row_or_a_foreign_company_row_fails_closed()
    {
        var notAbove = new SalesWorkInboxApprovalFakeStore { OverDeliveryRequests = [OverDeliveryRequest(1, T0) with { RequestedTotal = "100" }] };
        var foreign = new SalesWorkInboxApprovalFakeStore { QuoteRequests = [QuoteRequest(1, T0) with { CompanyId = OtherCompany }] };

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable,
            (await OverDeliverySource(notAbove).ListPendingAsync(Context(), 10, default)).Outcome);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable,
            (await QuoteSource(foreign).ListPendingAsync(Context(), 10, default)).Outcome);
    }

    [Fact]
    public async Task Without_the_approver_permission_or_for_a_scoped_caller_both_kinds_are_denied_and_read_nothing()
    {
        var store = new SalesWorkInboxApprovalFakeStore { QuoteRequests = [QuoteRequest(1, T0)], OverDeliveryRequests = [OverDeliveryRequest(1, T0)] };
        var scope = new WorkInboxGrantScopeV1(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>());

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, (await QuoteSource(store, _ => false).ListPendingAsync(Context(), 10, default)).Outcome);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied,
            (await OverDeliverySource(store, _ => false).ListPendingAsync(Context(), 10, default)).Outcome);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied,
            (await OverDeliverySource(store).ListPendingAsync(Context() with { GrantScope = scope }, 10, default)).Outcome);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task The_store_is_asked_for_one_more_than_the_page()
    {
        var store = new SalesWorkInboxApprovalFakeStore
        {
            OverDeliveryRequests = [OverDeliveryRequest(1, T0), OverDeliveryRequest(2, T0.AddHours(1)), OverDeliveryRequest(3, T0.AddHours(2))],
        };

        var result = await OverDeliverySource(store).ListPendingAsync(Context(), 2, default);

        Assert.True(result.HasMore);
        Assert.Equal(3, Assert.Single(store.Calls).Take);
        Assert.Equal(2, result.Items!.Count);
    }

    private static SalesQuoteConversionRequestWorkInboxSourceV1 QuoteSource(SalesWorkInboxApprovalFakeStore store,
        Func<string, bool>? granted = null) =>
        new(store, new CompanyContext(Company), new User(Approver, Company), new Authority(granted ?? (_ => true)));

    private static SalesOverDeliveryRequestWorkInboxSourceV1 OverDeliverySource(SalesWorkInboxApprovalFakeStore store,
        Func<string, bool>? granted = null) =>
        new(store, new CompanyContext(Company), new User(Approver, Company), new Authority(granted ?? (_ => true)));

    private static SalesWorkInboxQuoteConversionRequestV1 QuoteRequest(int no, DateTimeOffset requestedAt) =>
        new(Guid.Parse($"5a1e0000-0000-0000-000e-{no:D12}"), Company, Quote, no, "TKL-SNT-0001", "SENTETIK-seller", requestedAt);

    private static SalesWorkInboxOverDeliveryRequestV1 OverDeliveryRequest(int no, DateTimeOffset requestedAt) =>
        new(Guid.Parse($"5a1e0000-0000-0000-000f-{no:D12}"), Company, Order, no, "SO-SNT-0007", "100", "112.5", Maker, requestedAt);
}
