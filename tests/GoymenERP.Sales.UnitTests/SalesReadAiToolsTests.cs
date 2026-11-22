using System.Reflection;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Sales.Application.Ai;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// WP-A3a Sales READ tools over SENTETIK owner reads: exact counts from the owner's pages only, the caller's company and
/// actor passed through, another company's row / an unknown state / a repeated row releasing nothing, a permission refusal
/// as Denied, an unscoped argument refused before the owner is read, and the scan cap withholding every count.
/// </summary>
public sealed class SalesReadAiToolsTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000b2");
    private static readonly Guid Party = Guid.Parse("cccccccc-0000-4000-8000-0000000000c3");
    private static readonly Guid Account = Guid.Parse("dddddddd-0000-4000-8000-0000000000d4");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private const string Actor = "11111111-1111-4111-8111-111111111111";

    [Fact]
    public void The_three_sales_descriptors_are_read_tools_with_the_owner_permissions_and_closed_field_lists()
    {
        var approvals = new SalesPendingApprovalsAiTool(new ApprovalOwner(), new FixedTime(Now)).Descriptor;
        var quotes = new SalesQuoteStatusAiTool(QuoteProxy.Create().Service, new FixedTime(Now)).Descriptor;
        var orders = new SalesOrderStatusAiTool(DraftProxy.Create().Service, new FixedTime(Now)).Descriptor;

        Assert.All(new[] { approvals, quotes, orders }, descriptor =>
        {
            Assert.Equal("Sales", descriptor.OwnerModule);
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Equal(AiDataClassV1.Internal, descriptor.DataClass);
            Assert.Null(descriptor.OwnerCommandName);
            Assert.DoesNotContain("companyId", descriptor.FieldAllowlist);
        });
        Assert.Equal(new[] { "sales.drafts.read", "sales.orders.approve" }, approvals.RequiredPermissions);
        Assert.Equal(new[] { "limit" }, approvals.FieldAllowlist);
        Assert.Equal(new[] { "sales.quotes.read" }, quotes.RequiredPermissions);
        Assert.Equal(new[] { "limit", "state" }, quotes.FieldAllowlist);
        Assert.Equal(new[] { "sales.drafts.read" }, orders.RequiredPermissions);
        Assert.Equal(new[] { "approvalState", "limit" }, orders.FieldAllowlist);
    }

    [Fact]
    public void The_sales_module_registers_the_three_read_tools_without_admitting_them()
    {
        var services = new ServiceCollection();
        services.AddSalesModule(new ConnectionOnlyConfiguration("Server=(local);Database=SENTETIK;Trusted_Connection=True;"));

        var tools = services.Where(descriptor => descriptor.ServiceType == typeof(IAiOwnerToolV1))
            .Select(descriptor => descriptor.ImplementationType).ToArray();
        Assert.Contains(typeof(SalesPendingApprovalsAiTool), tools);
        Assert.Contains(typeof(SalesQuoteStatusAiTool), tools);
        Assert.Contains(typeof(SalesOrderStatusAiTool), tools);
        Assert.All(services.Where(descriptor => descriptor.ServiceType == typeof(IAiOwnerToolV1)),
            descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
    }

    [Fact]
    public async Task Pending_approvals_are_counted_exactly_over_every_owner_page_with_the_callers_company_and_actor()
    {
        var owner = new ApprovalOwner();
        owner.Pages.Add(([Approval(1, own: true), Approval(2)], "c1"));
        owner.Pages.Add(([Approval(3)], null));
        var tool = new SalesPendingApprovalsAiTool(owner, new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{\"limit\":2}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.Equal(3, root.GetProperty("pendingCount").GetInt32());
        Assert.Equal(1, root.GetProperty("ownDraftCount").GetInt32());
        Assert.Equal(2, root.GetProperty("listedCount").GetInt32());
        Assert.Equal("SENTETIK-SO-1", root.GetProperty("items")[0].GetProperty("documentNo").GetString());
        Assert.DoesNotContain("submittedBy", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actor-sentetik", result.ContentJson!, StringComparison.Ordinal);
        Assert.Equal(new string?[] { null, "c1" }, owner.Cursors.ToArray());
        Assert.All(owner.Contexts, context => Assert.Equal((Company, Actor), (context.CompanyId, context.ActorId)));
        Assert.Equal(Company, result.Source!.TargetId);
        Assert.Equal(AiDataClassV1.Internal, result.Source.DataClass);
    }

    [Theory]
    [InlineData(SalesOrderDraftPendingApprovalReadOutcomeV1.Denied, AiToolOutcomeV1.Denied)]
    [InlineData(SalesOrderDraftPendingApprovalReadOutcomeV1.Unavailable, AiToolOutcomeV1.Unavailable)]
    [InlineData(SalesOrderDraftPendingApprovalReadOutcomeV1.NotFound, AiToolOutcomeV1.Unavailable)]
    public async Task A_refused_or_failed_owner_approval_read_releases_nothing(
        SalesOrderDraftPendingApprovalReadOutcomeV1 ownerOutcome, AiToolOutcomeV1 expected)
    {
        var owner = new ApprovalOwner { Refusal = ownerOutcome };

        var result = await new SalesPendingApprovalsAiTool(owner, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.ContentJson);
        Assert.Null(result.Source);
    }

    [Fact]
    public async Task Another_companys_approval_row_releases_nothing()
    {
        var owner = new ApprovalOwner();
        owner.Pages.Add(([Approval(1), Approval(2) with { CompanyId = OtherCompany }], null));

        var result = await new SalesPendingApprovalsAiTool(owner, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task Reaching_the_page_cap_withholds_the_approval_counts_and_marks_the_answer_incomplete()
    {
        var owner = new ApprovalOwner { Endless = true };

        var result = await new SalesPendingApprovalsAiTool(owner, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("pendingCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("ownDraftCount").ValueKind);
        Assert.Equal(SalesPendingApprovalsAiTool.PageLimit, owner.Cursors.Count);
    }

    [Theory]
    [InlineData("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000000a1\"}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":51}")]
    [InlineData("{\"limit\":\"5\"}")]
    [InlineData("[]")]
    [InlineData("nope")]
    public async Task Malformed_or_unscoped_approval_arguments_are_invalid_before_the_owner_is_read(string arguments)
    {
        var owner = new ApprovalOwner();

        var result = await new SalesPendingApprovalsAiTool(owner, new FixedTime(Now)).ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Empty(owner.Contexts);
    }

    [Fact]
    public async Task Quotes_are_counted_per_owner_state_and_listed_newest_first_without_prices()
    {
        var proxy = QuoteProxy.Create();
        proxy.Rows.AddRange([Quote(1, "Open"), Quote(2, "Open"), Quote(3, "Converted"), Quote(4, "Lost")]);
        var tool = new SalesQuoteStatusAiTool(proxy.Service, new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.Equal(2, root.GetProperty("openCount").GetInt32());
        Assert.Equal(1, root.GetProperty("convertedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("lostCount").GetInt32());
        Assert.Equal("SENTETIK-Q-4", root.GetProperty("items")[0].GetProperty("documentNo").GetString());
        Assert.DoesNotContain("price", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "Open", "Converted", "Lost" }, proxy.Queries.Select(query => query.State).ToArray());
        Assert.All(proxy.Queries, query => Assert.Equal(SalesQuoteStatusAiTool.PageSize, query.PageSize));
    }

    [Fact]
    public async Task An_asked_quote_state_reads_only_that_state_and_leaves_the_other_counts_null()
    {
        var proxy = QuoteProxy.Create();
        proxy.Rows.AddRange([Quote(1, "Open"), Quote(2, "Lost")]);

        var result = await new SalesQuoteStatusAiTool(proxy.Service, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"state\":\"Lost\"}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(1, root.GetProperty("lostCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("openCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("convertedCount").ValueKind);
        Assert.Equal("Lost", Assert.Single(proxy.Queries).State);
    }

    [Fact]
    public async Task A_quote_permission_refusal_is_denied_and_another_companys_quote_releases_nothing()
    {
        var refused = QuoteProxy.Create();
        refused.Failure = new SalesDraftServiceException(403, "forbidden");
        Assert.Equal(AiToolOutcomeV1.Denied,
            (await new SalesQuoteStatusAiTool(refused.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var foreign = QuoteProxy.Create();
        foreign.Rows.Add(Quote(1, "Open") with { CompanyId = OtherCompany });
        Assert.Equal(AiToolOutcomeV1.Unavailable,
            (await new SalesQuoteStatusAiTool(foreign.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var failing = QuoteProxy.Create();
        failing.Failure = new InvalidOperationException("SENTETIK owner failure");
        Assert.Equal(AiToolOutcomeV1.Unavailable,
            (await new SalesQuoteStatusAiTool(failing.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Theory]
    [InlineData("{\"state\":\"Expired\"}")]
    [InlineData("{\"state\":\"open\"}")]
    [InlineData("{\"partyId\":\"cccccccc-0000-4000-8000-0000000000c3\"}")]
    public async Task A_quote_state_outside_the_owner_vocabulary_or_an_unknown_field_is_invalid(string arguments)
    {
        var proxy = QuoteProxy.Create();

        var result = await new SalesQuoteStatusAiTool(proxy.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Empty(proxy.Queries);
    }

    [Fact]
    public async Task More_quotes_than_the_scan_cap_withhold_every_count()
    {
        var proxy = QuoteProxy.Create();
        for (var number = 1; number <= SalesQuoteStatusAiTool.ScanLimit + 1; number++)
        {
            proxy.Rows.Add(Quote(number, "Open"));
        }

        var result = await new SalesQuoteStatusAiTool(proxy.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("openCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("lostCount").ValueKind);
        Assert.Equal(20, content.RootElement.GetProperty("listedCount").GetInt32());
    }

    [Fact]
    public async Task Orders_are_counted_on_the_lifecycle_and_approval_axes_separately()
    {
        var proxy = DraftProxy.Create();
        proxy.Rows.AddRange([
            Order(1, "Draft", "Preparing"), Order(2, "Draft", "PendingApproval"), Order(3, "Draft", "Approved"),
            Order(4, "Cancelled", "Approved"), Order(5, "Closed", "Approved"), Order(6, "Draft", "Withdrawn")]);

        var result = await new SalesOrderStatusAiTool(proxy.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(6, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(4, root.GetProperty("lifecycle").GetProperty("draft").GetInt32());
        Assert.Equal(1, root.GetProperty("lifecycle").GetProperty("cancelled").GetInt32());
        Assert.Equal(1, root.GetProperty("lifecycle").GetProperty("closed").GetInt32());
        Assert.Equal(3, root.GetProperty("approval").GetProperty("approved").GetInt32());
        Assert.Equal(0, root.GetProperty("approval").GetProperty("rejected").GetInt32());
        Assert.Equal(1, root.GetProperty("approval").GetProperty("withdrawn").GetInt32());
        Assert.Null(Assert.Single(proxy.Queries).ApprovalState);
    }

    [Fact]
    public async Task An_approval_filter_is_passed_to_the_owner_and_the_unread_approval_counts_are_null()
    {
        var proxy = DraftProxy.Create();
        proxy.Rows.AddRange([Order(1, "Draft", "PendingApproval"), Order(2, "Draft", "Approved")]);

        var result = await new SalesOrderStatusAiTool(proxy.Service, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{\"approvalState\":\"PendingApproval\"}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        var approval = content.RootElement.GetProperty("approval");
        Assert.Equal(1, approval.GetProperty("pendingApproval").GetInt32());
        Assert.Equal(JsonValueKind.Null, approval.GetProperty("approved").ValueKind);
        Assert.Equal(JsonValueKind.Null, approval.GetProperty("preparing").ValueKind);
        Assert.Equal("PendingApproval", Assert.Single(proxy.Queries).ApprovalState);
    }

    [Theory]
    [InlineData("Shipped", "Approved")]
    [InlineData("Draft", "Escalated")]
    public async Task An_order_state_outside_the_owner_vocabulary_releases_nothing(string state, string approvalState)
    {
        var proxy = DraftProxy.Create();
        proxy.Rows.Add(Order(1, state, approvalState));

        var result = await new SalesOrderStatusAiTool(proxy.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task An_order_permission_refusal_is_denied_and_a_repeated_or_foreign_order_releases_nothing()
    {
        var refused = DraftProxy.Create();
        refused.Failure = new SalesDraftServiceException(401, "unauthenticated");
        Assert.Equal(AiToolOutcomeV1.Denied,
            (await new SalesOrderStatusAiTool(refused.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var repeated = DraftProxy.Create();
        repeated.Rows.AddRange([Order(1, "Draft", "Approved"), Order(1, "Draft", "Approved")]);
        Assert.Equal(AiToolOutcomeV1.Unavailable,
            (await new SalesOrderStatusAiTool(repeated.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var foreign = DraftProxy.Create();
        foreign.Rows.Add(Order(1, "Draft", "Approved") with { CompanyId = OtherCompany });
        Assert.Equal(AiToolOutcomeV1.Unavailable,
            (await new SalesOrderStatusAiTool(foreign.Service, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Fact]
    public async Task The_same_owner_answer_gives_the_same_bytes_and_source_version()
    {
        var proxy = DraftProxy.Create();
        proxy.Rows.AddRange([Order(1, "Draft", "Approved"), Order(2, "Draft", "Preparing")]);
        var tool = new SalesOrderStatusAiTool(proxy.Service, new FixedTime(Now));

        var first = await tool.ReadAsync(new AiToolInputV1("{}"), Context());
        var second = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(first.ContentJson, second.ContentJson);
        Assert.Equal(first.Source!.SourceVersion, second.Source!.SourceVersion);
        Assert.StartsWith("sha256:", first.Source.SourceVersion, StringComparison.Ordinal);
    }

    private static AiServerContextV1 Context() => new(Company, Actor, "v1.session-sentetik", $"company:{Company:D}",
        "roles:sentetik", "corr-sentetik", null, null, null);

    private static SalesOrderDraftPendingApprovalItemV1 Approval(int number, bool own = false) => new(
        Id(number), Company, $"SENTETIK-SO-{number}", $"v{number}", "0", "actor-sentetik", Now.AddMinutes(number), own);

    private static SalesQuoteSummaryView Quote(int number, string state) => new(
        Id(number), Company, $"SENTETIK-Q-{number}", state, $"v{number}", "0", Party, Account, Guid.Parse("eeeeeeee-0000-4000-8000-0000000000e5"),
        new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), Now.AddMinutes(number), 2, null);

    private static SalesOrderDraftSummaryView Order(int number, string state, string approvalState) => new(
        Id(number), Company, $"SENTETIK-SO-{number}", "Standard", state, $"v{number}", "0", Party, Account,
        Guid.Parse("eeeeeeee-0000-4000-8000-0000000000e5"), new DateOnly(2026, 11, 1), Now.AddMinutes(number), 1, approvalState);

    private static Guid Id(int number) => new($"00000000-0000-4000-8000-{number:D12}");

    /// <summary>Only the connection string the module reads at registration; every section is empty.</summary>
    private sealed class ConnectionOnlyConfiguration(string connectionString) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING" ? connectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() =>
            new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);

        public IConfigurationSection GetSection(string key) => new EmptySection(key);

        private sealed class EmptySection(string key) : IConfigurationSection
        {
            public string Key => key.Split(':')[^1];
            public string Path => key;
            public string? Value { get => null; set => throw new NotSupportedException(); }
            public string? this[string child] { get => null; set => throw new NotSupportedException(); }
            public IEnumerable<IConfigurationSection> GetChildren() => [];
            public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() =>
                new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
            public IConfigurationSection GetSection(string child) => new EmptySection($"{key}:{child}");
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A SENTETIK owner pending-approval read: fixed pages, or endless pages to reach the cap, or one refusal.</summary>
    private sealed class ApprovalOwner : ISalesOrderDraftPendingApprovalReadServiceV1
    {
        public List<(SalesOrderDraftPendingApprovalItemV1[] Items, string? Next)> Pages { get; } = [];
        public List<SalesOrderDraftPendingApprovalReadContextV1> Contexts { get; } = [];
        public List<string?> Cursors { get; } = [];
        public SalesOrderDraftPendingApprovalReadOutcomeV1? Refusal { get; init; }
        public bool Endless { get; init; }

        public Task<SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalPageV1>> ListAsync(
            SalesOrderDraftPendingApprovalReadContextV1 context, string? cursor, CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            Cursors.Add(cursor);
            if (Refusal is { } refusal)
            {
                return Task.FromResult(new SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalPageV1>(refusal, null));
            }

            var index = Cursors.Count - 1;
            (SalesOrderDraftPendingApprovalItemV1[] Items, string? Next) page = Endless
                ? (new[] { Approval(index + 1) }, $"c{index + 1}")
                : Pages[index];
            return Task.FromResult(new SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalPageV1>(
                SalesOrderDraftPendingApprovalReadOutcomeV1.Found,
                new SalesOrderDraftPendingApprovalPageV1(Company, page.Items, page.Next)));
        }

        public Task<SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalDetailV1>> GetAsync(
            SalesOrderDraftPendingApprovalReadContextV1 context, Guid draftId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The tool reads the list only.");
    }

    /// <summary>The owner's quote list only: pages of the asked size by exact state; any other member is not supported.</summary>
    public class QuoteProxy : DispatchProxy
    {
        public ISalesQuoteService Service => (ISalesQuoteService)(object)this;
        public List<SalesQuoteSummaryView> Rows { get; } = [];
        public List<SalesQuoteListQuery> Queries { get; } = [];
        public Exception? Failure { get; set; }

        public static QuoteProxy Create() => (QuoteProxy)(object)DispatchProxy.Create<ISalesQuoteService, QuoteProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ISalesQuoteService.GetQuoteListAsync))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            var query = (SalesQuoteListQuery)args![0]!;
            Queries.Add(query);
            if (Failure is not null)
            {
                return Task.FromException<SalesQuoteListResponse>(Failure);
            }

            var (page, next) = Page(Rows.Where(row => query.State is null || row.State == query.State).ToArray(), query.PageSize, query.Cursor);
            return Task.FromResult(new SalesQuoteListResponse(page, next));
        }
    }

    /// <summary>The owner's draft list only: pages of the asked size by exact approval state; any other member is not supported.</summary>
    public class DraftProxy : DispatchProxy
    {
        public ISalesDraftService Service => (ISalesDraftService)(object)this;
        public List<SalesOrderDraftSummaryView> Rows { get; } = [];
        public List<SalesOrderDraftListQuery> Queries { get; } = [];
        public Exception? Failure { get; set; }

        public static DraftProxy Create() => (DraftProxy)(object)DispatchProxy.Create<ISalesDraftService, DraftProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ISalesDraftService.GetDraftListAsync))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            var query = (SalesOrderDraftListQuery)args![0]!;
            Queries.Add(query);
            if (Failure is not null)
            {
                return Task.FromException<SalesOrderDraftListResponse>(Failure);
            }

            var (page, next) = Page(Rows.Where(row => query.ApprovalState is null || row.ApprovalState == query.ApprovalState).ToArray(),
                query.PageSize, query.Cursor);
            return Task.FromResult(new SalesOrderDraftListResponse(page, next));
        }
    }

    private static (T[] Page, string? Next) Page<T>(T[] matching, int pageSize, string? cursor)
    {
        var start = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
        var page = matching.Skip(start).Take(pageSize).ToArray();
        var next = start + page.Length < matching.Length
            ? (start + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;
        return (page, next);
    }
}
