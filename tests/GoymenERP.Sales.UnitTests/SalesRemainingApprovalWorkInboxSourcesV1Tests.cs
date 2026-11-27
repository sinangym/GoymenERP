using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;
using static GoymenERP.Sales.UnitTests.SalesWorkInboxTestDoubles;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// WP4: the remaining Sales approval kinds in the one Work inbox — request, invoice, subcontract service invoice, customer
/// return and contract discount. Each lists exactly what its owner's decision accepts, names the owner's "may not decide" set
/// (D4), is Denied without every permission the owner's decision needs and for a scoped caller (D11), and fails closed on a
/// row that breaks the contract.
/// </summary>
public sealed class SalesRemainingApprovalWorkInboxSourcesV1Tests
{
    private const string Submitter = "SENTETIK-submitter";
    private static readonly Guid Order = Guid.Parse("5a1e0000-0000-0000-0005-000000000001");

    [Fact]
    public void Each_kind_names_its_owners_approver_permission_and_none_accepts_a_scoped_grant()
    {
        var store = new SalesWorkInboxApprovalFakeStore();
        var expected = new Dictionary<string, string>
        {
            ["sales.request.v1"] = SalesPermissionManifestV1.RequestsApprove,
            ["sales.invoice.v1"] = SalesPermissionManifestV1.InvoicesApprove,
            ["sales.subcontract-invoice.v1"] = SalesPermissionManifestV1.InvoicesApprove,
            ["sales.customer-return.v1"] = SalesPermissionManifestV1.ReturnsApprove,
            ["sales.contract-discount.v1"] = SalesPermissionManifestV1.ContractsDiscountApprove,
        };

        var sources = AllSources(store);

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), sources.Select(source => source.Kind).Order(StringComparer.Ordinal));
        Assert.All(sources, source =>
        {
            Assert.Equal("sales", source.Module);
            Assert.Equal(expected[source.Kind], source.ApproverPermission);
            Assert.True(NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind));
            Assert.False(source.AcceptsScopedApprovers);
        });
    }

    [Fact]
    public async Task A_scoped_caller_is_denied_by_every_new_kind_and_nothing_is_read()
    {
        var store = new SalesWorkInboxApprovalFakeStore();
        var scope = new WorkInboxGrantScopeV1(new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>());

        foreach (var source in AllSources(store))
        {
            var result = await source.ListPendingAsync(Context() with { GrantScope = scope }, 10, default);
            Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        }

        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_submitted_request_waits_from_its_creation_and_the_owner_names_no_maker()
    {
        var store = new SalesWorkInboxApprovalFakeStore { Requests = [Request(1, T0)] };
        var source = Sources.Request(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        var item = Assert.Single(result.Items!);
        Assert.Equal(("TLP-SNT-0001", $"/satis/talepler/{item.ItemId:D}", T0.ToUniversalTime()), (item.DocumentLabel, item.Link, item.WaitingSinceUtc));
        Assert.Null(item.PreparedBy);
        Assert.False(item.Excludes(Maker));
        Assert.Equal(new WorkApprovalRuleResultV1(SalesRequestWorkInboxSourceV1.ApprovalRule, null, null, null, null),
            Assert.Single(item.Rules));
        Assert.Equal(("sales-request", (Guid?)item.ItemId), (Assert.Single(item.References).Role, item.References[0].RecordId));
        AssertContractShape(source, result, ownerNamesMaker: false);
    }

    [Fact]
    public async Task A_pending_invoice_excludes_its_preparer_and_submitter_and_waits_from_the_submission()
    {
        var store = new SalesWorkInboxApprovalFakeStore { Invoices = [Invoice(1, T0, Submitter)] };
        var source = Sources.Invoice(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        var item = Assert.Single(result.Items!);
        Assert.Equal(("FTR-SNT-0001", $"/onaylarim/sales.invoice.v1/{item.ItemId:D}"), (item.DocumentLabel, item.Link));
        Assert.Equal(T0.AddHours(2).ToUniversalTime(), item.WaitingSinceUtc);
        Assert.Equal(Maker, item.PreparedBy);
        Assert.Equal([Submitter], item.ExcludedDeciders);
        Assert.True(item.Excludes(Maker));
        Assert.True(item.Excludes(Submitter));
        Assert.False(item.Excludes(Approver));
        AssertContractShape(source, result);
    }

    [Fact]
    public async Task An_invoice_submitted_by_its_preparer_names_no_second_excluded_decider()
    {
        var store = new SalesWorkInboxApprovalFakeStore { Invoices = [Invoice(1, T0, Maker)] };

        var item = Assert.Single((await Sources.Invoice(store).ListPendingAsync(Context(), 10, default)).Items!);

        Assert.Equal(Maker, item.PreparedBy);
        Assert.Null(item.ExcludedDeciders);
    }

    [Theory]
    [InlineData("sales.invoice.v1")]
    [InlineData("sales.subcontract-invoice.v1")]
    [InlineData("sales.customer-return.v1")]
    public async Task A_kind_whose_decision_needs_the_price_read_is_denied_without_it_and_reads_nothing(string kind)
    {
        var store = new SalesWorkInboxApprovalFakeStore
        {
            Invoices = [Invoice(1, T0, Submitter)],
            SubcontractInvoices = [Subcontract(1, T0)],
            Returns = [Return(1, T0)],
        };
        var source = AllSources(store, permission => permission != SalesPermissionManifestV1.OrdersPriceRead)
            .Single(candidate => candidate.Kind == kind);

        var result = await source.ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_pending_subcontract_invoice_opens_its_order_and_excludes_preparer_and_submitter()
    {
        var store = new SalesWorkInboxApprovalFakeStore { SubcontractInvoices = [Subcontract(1, T0)] };
        var source = Sources.Subcontract(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        var item = Assert.Single(result.Items!);
        Assert.Equal($"/onaylarim/sales.subcontract-invoice.v1/{item.ItemId:D}", item.Link);
        Assert.Equal(["sales-subcontract-invoice", "sales-order-draft"], item.References.Select(reference => reference.Role));
        Assert.Equal(Order, item.References[1].RecordId);
        Assert.True(item.Excludes(Maker) && item.Excludes(Submitter));
        AssertContractShape(source, result);
    }

    [Fact]
    public async Task A_pending_customer_return_links_its_screen_and_names_the_original_invoice()
    {
        var store = new SalesWorkInboxApprovalFakeStore { Returns = [Return(1, T0)] };
        var source = Sources.Return(store);

        var result = await source.ListPendingAsync(Context(), 10, default);

        var item = Assert.Single(result.Items!);
        Assert.Equal("MUS-IADE-0001", item.DocumentLabel);
        Assert.Equal($"/satis/iadeler/{item.ItemId:D}", item.Link);
        var original = Assert.Single(item.References, reference => reference.Role == "sales-invoice");
        Assert.Equal(("FTR-SNT-0009", "/satis/faturalar"), (original.Label, original.Link));
        Assert.True(item.Excludes(Maker) && item.Excludes(Submitter));
        AssertContractShape(source, result);
    }

    [Fact]
    public async Task A_contract_discount_over_the_limit_shows_the_owners_figures_only_with_the_price_read()
    {
        var contract = Contract(1, 10m, (1, 8m), (2, 12.5m), (3, 15m));
        var store = new SalesWorkInboxApprovalFakeStore { Contracts = [contract] };

        var shown = Assert.Single((await Sources.Contract(store).ListPendingAsync(Context(), 10, default)).Items!);
        var withheld = Assert.Single((await Sources.Contract(store,
                permission => permission != SalesPermissionManifestV1.OrdersPriceRead)
            .ListPendingAsync(Context(), 10, default)).Items!);

        Assert.Equal(
            [
                new WorkApprovalRuleResultV1(SalesContractDiscountWorkInboxSourceV1.OverLimitRule, "10", "12.5", "2.5", "%"),
                new WorkApprovalRuleResultV1(SalesContractDiscountWorkInboxSourceV1.OverLimitRule, "10", "15", "5", "%"),
            ],
            shown.Rules);
        Assert.All(withheld.Rules, rule => Assert.Equal(
            new WorkApprovalRuleResultV1(SalesContractDiscountWorkInboxSourceV1.OverLimitRule, null, null, null, null), rule));
        Assert.Equal(2, withheld.Rules.Count);
        Assert.Equal(("Sözleşme #1", Maker, T0.ToUniversalTime()), (shown.DocumentLabel, shown.PreparedBy, shown.WaitingSinceUtc));
        AssertContractShape(Sources.Contract(store), new WorkInboxSourceResultV1(NativeApprovalQueueOutcomeV1.Found, Company,
            [shown], false));
    }

    [Fact]
    public async Task A_contract_recorded_without_a_limit_waits_for_the_limit_undefined_rule()
    {
        var store = new SalesWorkInboxApprovalFakeStore { Contracts = [Contract(1, null, (1, 3m))] };

        var item = Assert.Single((await Sources.Contract(store).ListPendingAsync(Context(), 10, default)).Items!);

        Assert.Equal(new WorkApprovalRuleResultV1(SalesContractDiscountWorkInboxSourceV1.LimitUndefinedRule, null, null, null, null),
            Assert.Single(item.Rules));
    }

    [Fact]
    public async Task A_pending_contract_that_its_own_comparison_would_not_send_to_approval_fails_closed()
    {
        var store = new SalesWorkInboxApprovalFakeStore { Contracts = [Contract(1, 10m, (1, 5m))] };

        var result = await Sources.Contract(store).ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task A_pending_record_without_its_submission_time_or_submitter_fails_closed()
    {
        var noTime = new SalesWorkInboxApprovalFakeStore { Invoices = [Invoice(1, T0, Submitter) with { SubmittedAtUtc = null }] };
        var noSubmitter = new SalesWorkInboxApprovalFakeStore { Returns = [Return(1, T0) with { SubmittedBy = null }] };

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable,
            (await Sources.Invoice(noTime).ListPendingAsync(Context(), 10, default)).Outcome);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable,
            (await Sources.Return(noSubmitter).ListPendingAsync(Context(), 10, default)).Outcome);
    }

    [Fact]
    public async Task A_foreign_company_row_fails_closed_and_the_store_is_asked_for_the_callers_company()
    {
        var store = new SalesWorkInboxApprovalFakeStore { Requests = [Request(1, T0) with { CompanyId = OtherCompany }] };

        var result = await Sources.Request(store).ListPendingAsync(Context(), 10, default);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Equal(Company, Assert.Single(store.Calls).CompanyId);
    }

    [Fact]
    public async Task The_store_is_asked_for_one_more_than_the_page_and_the_oldest_come_first()
    {
        var store = new SalesWorkInboxApprovalFakeStore
        {
            Returns = [Return(1, T0), Return(2, T0.AddHours(1)), Return(3, T0.AddHours(2))],
        };

        var result = await Sources.Return(store).ListPendingAsync(Context(), 2, default);

        Assert.True(result.HasMore);
        Assert.Equal(3, Assert.Single(store.Calls).Take);
        Assert.Equal(new[] { Return(1, T0).Id, Return(2, T0).Id }, result.Items!.Select(item => item.ItemId).ToArray());
    }

    [Fact]
    public async Task Without_the_approver_permission_each_kind_is_denied_and_a_failing_read_is_unavailable()
    {
        var store = new SalesWorkInboxApprovalFakeStore();
        foreach (var source in AllSources(store, permission => permission == SalesPermissionManifestV1.OrdersPriceRead))
        {
            Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, (await source.ListPendingAsync(Context(), 10, default)).Outcome);
        }

        Assert.Empty(store.Calls);
        store.Failure = new InvalidOperationException("SENTETIK");
        foreach (var source in AllSources(store))
        {
            Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, (await source.ListPendingAsync(Context(), 10, default)).Outcome);
        }
    }

    private static IReadOnlyList<IWorkInboxSourceV1> AllSources(SalesWorkInboxApprovalFakeStore store,
        Func<string, bool>? granted = null) =>
        [
            Sources.Request(store, granted),
            Sources.Invoice(store, granted),
            Sources.Subcontract(store, granted),
            Sources.Return(store, granted),
            Sources.Contract(store, granted),
        ];

    [Fact]
    public async Task A_completed_subcontract_invoice_keeps_immutable_maker_and_version_metadata_for_native_replay()
    {
        var invoice = Subcontract(1, T0);
        var store = new SalesWorkInboxApprovalFakeStore { SubcontractDecision = new(invoice, 9, false) };
        var result = await Sources.Subcontract(store).ReadDecisionAsync(Context(), invoice.Id, default);
        Assert.NotNull(result);
        Assert.False(result.Pending);
        Assert.Equal("9", result.SourceVersion);
        Assert.Equal(Maker, result.Item.PreparedBy);
        Assert.True(result.Item.Excludes(Submitter));
        Assert.Equal($"/onaylarim/sales.subcontract-invoice.v1/{invoice.Id:D}", result.Item.Link);
        Assert.Equal(T0.ToUniversalTime(), result.Item.WaitingSinceUtc);
    }

    [Fact]
    public async Task A_subcontract_purpose_read_requires_fresh_price_authority_after_its_metadata_read()
    {
        var invoice = Subcontract(1, T0);
        var allowed = true;
        var store = new SalesWorkInboxApprovalFakeStore { SubcontractDecision = new(invoice, 9, false), OnDecisionRead = () => allowed = false };
        var source = Sources.Subcontract(store, permission => permission != SalesPermissionManifestV1.OrdersPriceRead || allowed);
        Assert.Null(await source.ReadDecisionAsync(Context(), invoice.Id, default));
        Assert.Single(store.Calls);
    }

    private static class Sources
    {
        public static SalesRequestWorkInboxSourceV1 Request(SalesWorkInboxApprovalFakeStore store, Func<string, bool>? granted = null) =>
            new(store, new CompanyContext(Company), new User(Approver, Company), new Authority(granted ?? (_ => true)));

        public static SalesInvoiceWorkInboxSourceV1 Invoice(SalesWorkInboxApprovalFakeStore store, Func<string, bool>? granted = null) =>
            new(store, new CompanyContext(Company), new User(Approver, Company), new Authority(granted ?? (_ => true)));

        public static SalesSubcontractInvoiceWorkInboxSourceV1 Subcontract(SalesWorkInboxApprovalFakeStore store,
            Func<string, bool>? granted = null) =>
            new(store, new CompanyContext(Company), new User(Approver, Company), new Authority(granted ?? (_ => true)));

        public static SalesCustomerReturnWorkInboxSourceV1 Return(SalesWorkInboxApprovalFakeStore store,
            Func<string, bool>? granted = null) =>
            new(store, new CompanyContext(Company), new User(Approver, Company), new Authority(granted ?? (_ => true)));

        public static SalesContractDiscountWorkInboxSourceV1 Contract(SalesWorkInboxApprovalFakeStore store,
            Func<string, bool>? granted = null) =>
            new(store, new CompanyContext(Company), new User(Approver, Company), new Authority(granted ?? (_ => true)));
    }

    private static SalesWorkInboxRequestV1 Request(int no, DateTimeOffset createdAt) =>
        new(Guid.Parse($"5a1e0000-0000-0000-0006-{no:D12}"), Company, $"TLP-SNT-{no:D4}", createdAt);

    private static SalesWorkInboxInvoiceV1 Invoice(int no, DateTimeOffset createdAt, string submitter) =>
        new(Guid.Parse($"5a1e0000-0000-0000-0007-{no:D12}"), Company, $"FTR-SNT-{no:D4}", Maker, submitter, createdAt.AddHours(2));

    private static SalesWorkInboxInvoiceV1 Subcontract(int no, DateTimeOffset submittedAt) =>
        new(Guid.Parse($"5a1e0000-0000-0000-0008-{no:D12}"), Company, $"FSN-SNT-{no:D4}", Maker, Submitter, submittedAt, Order,
            "SO-SNT-0005");

    private static SalesWorkInboxCustomerReturnV1 Return(int no, DateTimeOffset submittedAt) =>
        new(Guid.Parse($"5a1e0000-0000-0000-0009-{no:D12}"), Company, $"MUS-IADE-{no:D4}",
            Guid.Parse("5a1e0000-0000-0000-000a-000000000009"), "FTR-SNT-0009", Maker, Submitter, submittedAt);

    private static SalesWorkInboxContractV1 Contract(int no, decimal? limit, params (int No, decimal Rate)[] rules) =>
        new(Guid.Parse($"5a1e0000-0000-0000-000b-{no:D12}"), Company, no, Maker, T0, limit,
            [.. rules.Select(rule => new SalesContractDiscountRule(rule.No, SalesContractDiscountKind.Line, "SENTETIK", rule.Rate))]);
}
