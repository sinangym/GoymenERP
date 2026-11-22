using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesRiskExceptionWorkInboxSourceV1Tests
{
    private static readonly Guid Order = Guid.Parse("5a1e0000-0000-0000-0003-000000000001");

    [Fact]
    public async Task Each_pending_exception_is_one_item_linked_to_its_order_without_invented_numbers()
    {
        var store = new Store { RiskExceptions = [Exception(1, T0), Exception(2, T0.AddHours(1))] };
        var source = Source(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal(2, result.Items!.Count);
        var item = result.Items[0];
        Assert.Equal(Exception(1, T0).Id, item.ItemId);
        Assert.Equal("SO-SNT-0001 #1", item.DocumentLabel);
        Assert.Equal($"/satis/siparisler/{Order:D}", item.Link);
        Assert.Equal(T0.ToUniversalTime(), item.WaitingSinceUtc);
        Assert.Equal(new WorkApprovalRuleResultV1(SalesRiskExceptionWorkInboxSourceV1.RiskExceptionRule, null, null, null, null),
            Assert.Single(item.Rules));
        var reference = Assert.Single(item.References);
        Assert.Equal(("sales-order-draft", "SO-SNT-0001", (Guid?)Order), (reference.Role, reference.Label, reference.RecordId));
        AssertContractShape(source, result);
    }

    [Fact]
    public async Task The_store_is_asked_for_the_callers_company_and_a_foreign_row_fails_closed()
    {
        var store = new Store { RiskExceptions = [Exception(1, T0) with { CompanyId = OtherCompany }] };

        var result = await Source(store).ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Equal(Company, Assert.Single(store.Calls).CompanyId);
    }

    [Fact]
    public async Task Without_the_company_wide_approver_permission_the_source_is_denied_and_reads_nothing()
    {
        var store = new Store { RiskExceptions = [Exception(1, T0)] };
        var source = Source(store, granted: permission => permission != SalesPermissionManifestV1.OrdersRiskExceptionApprove);

        var result = await source.ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task The_requesters_own_exception_is_returned_with_the_requester_as_prepared_by()
    {
        var store = new Store { RiskExceptions = [Exception(1, T0)] };

        var result = await Source(store, user: Maker).ListPendingAsync(Context(Maker), 10, default);

        Assert.Equal(Maker, Assert.Single(result.Items!).PreparedBy);
    }

    [Fact]
    public async Task The_store_is_asked_for_one_more_than_the_page_and_has_more_is_set()
    {
        var store = new Store { RiskExceptions = [Exception(1, T0), Exception(2, T0.AddHours(1)), Exception(3, T0.AddHours(2))] };

        var result = await Source(store).ListPendingAsync(Context(), 2, default);

        Assert.True(result.HasMore);
        Assert.Equal(3, Assert.Single(store.Calls).Take);
        Assert.Equal(new[] { Exception(1, T0).Id, Exception(2, T0).Id }, result.Items!.Select(item => item.ItemId).ToArray());
    }

    [Fact]
    public async Task A_failing_owner_read_is_unavailable()
    {
        var result = await Source(new Store { Failure = new InvalidOperationException("SENTETIK") })
            .ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    private static SalesRiskExceptionWorkInboxSourceV1 Source(Store store, Func<string, bool>? granted = null,
        string user = Approver) =>
        new(store, new CompanyContext(Company), new User(user, Company), new Authority(granted ?? (_ => true)));

    private static SalesWorkInboxRiskExceptionV1 Exception(int no, DateTimeOffset requestedAt) =>
        new(Guid.Parse($"5a1e0000-0000-0000-0004-{no:D12}"), Company, Order, no, "SO-SNT-0001", Maker, requestedAt);
}
