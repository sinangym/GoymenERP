using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP1 D5: the four native D-05 kinds as Work inbox sources. SENTETIK data; the owner adapters are doubles.</summary>
public sealed class NativeApprovalWorkInboxSourceV1Tests
{
    private static readonly Guid Company = Guid.Parse("c7000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("c7000000-0000-0000-0000-000000000002");
    private static readonly Guid First = Guid.Parse("c8000000-0000-0000-0000-000000000001");
    private static readonly Guid Label = Guid.Parse("c9000000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("c9000000-0000-0000-0000-000000000002");
    private static readonly Guid Location = Guid.Parse("c9000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Submitted = DateTimeOffset.Parse("2026-11-20T07:00:00Z");
    private const string Maker = "maker";

    [Theory]
    [InlineData(NativeApprovalKindsV1.FinanceOfficialInvoiceImport, "finance", ErpPermissions.FinanceOfficialInvoiceImportApprove)]
    [InlineData(NativeApprovalKindsV1.InventoryStockCount, "inventory", ErpPermissions.InventoryStockCountApprove)]
    [InlineData(NativeApprovalKindsV1.InventoryStockScrap, "inventory", ErpPermissions.InventoryStockScrapApprove)]
    [InlineData(NativeApprovalKindsV1.SalesOrderDraft, "sales", ErpPermissions.SalesOrdersApprove)]
    public void Each_native_kind_declares_its_owner_module_and_the_owners_approve_permission(string kind, string module,
        string permission)
    {
        var source = new NativeApprovalWorkInboxSourceV1(new Adapter(kind));

        Assert.Equal((module, kind, permission, false), (source.Module, source.Kind, source.ApproverPermission,
            ((IWorkInboxSourceV1)source).AcceptsScopedApprovers));
        Assert.True(NativeApprovalQueueRulesV1.IsValidKind(source.Module, source.Kind));
        Assert.True(WorkInboxRulesV1.IsValidPermission(source.ApproverPermission));
    }

    [Fact]
    public async Task A_stock_count_is_listed_with_the_submitter_as_maker_the_owner_kilograms_and_its_detail_link()
    {
        var adapter = new Adapter(NativeApprovalKindsV1.InventoryStockCount)
        {
            Items = ctx => [Count(ctx.CompanyId, counted: "98.500", difference: "-1.5")],
        };
        var source = new NativeApprovalWorkInboxSourceV1(adapter);

        var result = await source.ListPendingAsync(Context(), 10, default);

        Assert.Equal((NativeApprovalQueueOutcomeV1.Found, Company, false), (result.Outcome, result.CompanyId, result.HasMore));
        var item = Assert.Single(result.Items!);
        Assert.Equal(("inventory", NativeApprovalKindsV1.InventoryStockCount, First, "ETK-1", Submitted, Maker),
            (item.Module, item.Kind, item.ItemId, item.DocumentLabel, item.WaitingSinceUtc, item.PreparedBy));
        Assert.Equal($"/onaylarim/{NativeApprovalKindsV1.InventoryStockCount}/{First:D}", item.Link);
        var rule = Assert.Single(item.Rules);
        Assert.Equal((NativeApprovalWorkInboxSourceV1.StockCountDifferenceRule, (string?)null, "98.5", "-1.5", "kg"),
            (rule.RuleCode, rule.Threshold, rule.Actual, rule.Deviation, rule.Unit));
        Assert.Equal(["inventory-label", "product", "location"], item.References.Select(reference => reference.Role).ToArray());
        Assert.True(WorkInboxRulesV1.IsValidRule(item.Module, rule));
        Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference)));
        Assert.Equal((Company, "checker", true, (string?)null), (adapter.Context!.CompanyId, adapter.Context.ActorId,
            adapter.Context.IncludeInventoryLabelCode, adapter.Cursor));
    }

    [Fact]
    public async Task A_sales_draft_and_an_invoice_import_carry_their_owner_rule_and_a_label()
    {
        var sales = new NativeApprovalWorkInboxSourceV1(new Adapter(NativeApprovalKindsV1.SalesOrderDraft)
        {
            Items = ctx => [new SalesOrderDraftNativeApprovalReadItemV1(First, ctx.CompanyId, "PendingApproval", "3", "2", "SD-0042",
                false, Maker, Submitted)],
        });
        var import = new NativeApprovalWorkInboxSourceV1(new Adapter(NativeApprovalKindsV1.FinanceOfficialInvoiceImport)
        {
            Items = ctx => [new FinanceInvoiceImportNativeApprovalReadItemV1(First, ctx.CompanyId, "PendingApproval", "1", "1",
                new string('a', 64), "1.0", 4, Maker, Submitted)],
        });

        var draft = Assert.Single((await sales.ListPendingAsync(Context(), 10, default)).Items!);
        var batch = Assert.Single((await import.ListPendingAsync(Context(), 10, default)).Items!);

        Assert.Equal(("SD-0042", NativeApprovalWorkInboxSourceV1.SalesOrderDraftRule), (draft.DocumentLabel, Assert.Single(draft.Rules).RuleCode));
        Assert.Equal(("…" + First.ToString("D")[^12..], NativeApprovalWorkInboxSourceV1.OfficialInvoiceImportRule),
            (batch.DocumentLabel, Assert.Single(batch.Rules).RuleCode));
    }

    [Fact]
    public async Task The_owners_denial_and_a_scoped_caller_are_denied_and_a_broken_answer_is_unavailable()
    {
        var denied = new NativeApprovalWorkInboxSourceV1(new Adapter(NativeApprovalKindsV1.InventoryStockScrap)
        {
            Answer = _ => new(NativeApprovalReadOutcomeV1.Denied, null),
        });
        var scopedAdapter = new Adapter(NativeApprovalKindsV1.InventoryStockScrap);
        var scoped = new NativeApprovalWorkInboxSourceV1(scopedAdapter);
        var badKg = new NativeApprovalWorkInboxSourceV1(new Adapter(NativeApprovalKindsV1.InventoryStockCount)
        {
            Items = ctx => [Count(ctx.CompanyId, counted: "1,5", difference: "-1")],
        });
        var foreign = new NativeApprovalWorkInboxSourceV1(new Adapter(NativeApprovalKindsV1.InventoryStockCount)
        {
            Items = _ => [Count(OtherCompany, counted: "1", difference: "-1")],
        });

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, (await denied.ListPendingAsync(Context(), 10, default)).Outcome);
        var scopedContext = Context() with
        {
            GrantScope = new WorkInboxGrantScopeV1(new HashSet<Guid>(), new HashSet<Guid> { Location }, new HashSet<Guid>()),
        };
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, (await scoped.ListPendingAsync(scopedContext, 10, default)).Outcome);
        Assert.Equal(0, scopedAdapter.Calls);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, (await badKg.ListPendingAsync(Context(), 10, default)).Outcome);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, (await foreign.ListPendingAsync(Context(), 10, default)).Outcome);
    }

    [Fact]
    public async Task More_records_than_asked_or_a_next_cursor_say_the_owner_holds_more()
    {
        var two = new NativeApprovalWorkInboxSourceV1(new Adapter(NativeApprovalKindsV1.InventoryStockCount)
        {
            Items = ctx => [Count(ctx.CompanyId, "1", "-1"), Count(ctx.CompanyId, "2", "-1") with
            {
                ApprovalId = Guid.Parse("c8000000-0000-0000-0000-000000000002"),
            }],
        });
        var cursor = new NativeApprovalWorkInboxSourceV1(new Adapter(NativeApprovalKindsV1.InventoryStockCount)
        {
            Items = ctx => [Count(ctx.CompanyId, "1", "-1")],
            NextCursor = "next",
        });

        var limited = await two.ListPendingAsync(Context(), 1, default);
        var paged = await cursor.ListPendingAsync(Context(), 10, default);

        Assert.Equal((1, true), (limited.Items!.Count, limited.HasMore));
        Assert.True(paged.HasMore);
    }

    private static WorkInboxSourceContextV1 Context() => new(Company, "checker", "corr-1", "cause-1");

    private static InventoryStockCountNativeApprovalReadItemV1 Count(Guid company, string counted, string difference) => new(
        First, company, "PendingApproval", "7", Label, Product, Location, difference, "7", "Company", Guid.Parse("ca000000-0000-0000-0000-000000000001"),
        "Company", Guid.Parse("ca000000-0000-0000-0000-000000000001"), "None", "100", counted, "SENTETIK sayım", "ETK-1", Maker, Submitted);

    private sealed class Adapter(string kind) : INativeApprovalReadAdapterV1
    {
        public string Kind => kind;
        public Func<NativeApprovalReadContextV1, IReadOnlyList<NativeApprovalReadItemV1>>? Items { get; init; }
        public string? NextCursor { get; init; }
        public Func<NativeApprovalReadContextV1, NativeApprovalReadResultV1<NativeApprovalReadPageV1>>? Answer { get; init; }
        public NativeApprovalReadContextV1? Context { get; private set; }
        public string? Cursor { get; private set; } = "unset";
        public int Calls { get; private set; }

        public Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(NativeApprovalReadContextV1 context, string? cursor,
            CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            Cursor = cursor;
            return Task.FromResult(Answer?.Invoke(context)
                ?? new NativeApprovalReadResultV1<NativeApprovalReadPageV1>(NativeApprovalReadOutcomeV1.Found,
                    new NativeApprovalReadPageV1(context.CompanyId, Items?.Invoke(context) ?? [], NextCursor)));
        }

        public Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(NativeApprovalReadContextV1 context, Guid approvalId,
            CancellationToken cancellationToken) => throw new InvalidOperationException("The inbox never reads a typed detail.");
    }
}
