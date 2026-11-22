using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Application.Ai;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// WP-A3b Manufacturing READ tools over a SENTETIK fake of the owner's list/queue reads: per-status and per-process counts
/// with exact planned kg, a refused or work-center-scoped grant reads nothing, another company's queue or another status's
/// row releases nothing, and the scan cap withholds the counts.
/// </summary>
public sealed class ManufacturingAiReadToolsTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000004a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000004b2");
    private static readonly Guid WorkCenter = Guid.Parse("cccccccc-0000-4000-8000-0000000004c3");
    private static readonly Guid Plan = Guid.Parse("dddddddd-0000-4000-8000-0000000004d4");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_three_descriptors_are_internal_manufacturing_read_tools_with_the_owner_read_permissions()
    {
        var source = new FakeSource();
        var authority = new FakeAuthority();
        var orders = new ManufacturingOrderStatusAiTool(source, authority, new FixedTime(Now)).Descriptor;
        var plans = new ManufacturingPlanStatusAiTool(source, authority, new FixedTime(Now)).Descriptor;
        var queue = new ManufacturingWorkCenterQueueAiTool(source, authority, new FixedTime(Now)).Descriptor;

        foreach (var descriptor in new[] { orders, plans, queue })
        {
            Assert.Equal("Manufacturing", descriptor.OwnerModule);
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Equal(AiDataClassV1.Internal, descriptor.DataClass);
            Assert.Null(descriptor.OwnerCommandName);
        }

        Assert.Equal("manufacturing.order-status.v1", orders.ToolId);
        Assert.Equal(new[] { "manufacturing.orders.read" }, orders.RequiredPermissions);
        Assert.Equal(new[] { "limit", "status" }, orders.FieldAllowlist);
        Assert.Equal("manufacturing.plan-status.v1", plans.ToolId);
        Assert.Equal(new[] { "manufacturing.plans.read" }, plans.RequiredPermissions);
        Assert.Equal("manufacturing.work-center-queue.v1", queue.ToolId);
        Assert.Equal(new[] { "manufacturing.plans.read" }, queue.RequiredPermissions);
        Assert.Equal(new[] { "limit", "planId" }, queue.FieldAllowlist);
    }

    [Fact]
    public async Task Open_orders_are_counted_per_process_with_the_exact_planned_kg_over_every_page()
    {
        var source = new FakeSource();
        for (var i = 0; i < 120; i++)
        {
            source.Orders.Add(Order("Open", i % 3 == 0 ? "Slitting" : "Cutting", "1000.5"));
        }

        var tool = new ManufacturingOrderStatusAiTool(source, new FakeAuthority(ErpPermissions.ManufacturingOrdersRead), new FixedTime(Now));
        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal("Open", root.GetProperty("status").GetString());
        Assert.Equal(120, root.GetProperty("orderCount").GetInt32());
        Assert.Equal("120060", root.GetProperty("plannedInputKgTotal").GetString());
        var byProcess = root.GetProperty("byProcess").EnumerateArray()
            .ToDictionary(item => item.GetProperty("process").GetString()!, item => item.GetProperty("count").GetInt32());
        Assert.Equal(80, byProcess["Cutting"]);
        Assert.Equal(40, byProcess["Slitting"]);
        Assert.Equal(ManufacturingOrderStatusAiTool.DefaultLimit, root.GetProperty("items").GetArrayLength());
        Assert.Equal(new[] { "Open", "Open" }, source.OrderStatuses);
    }

    [Fact]
    public async Task An_order_row_in_another_status_or_an_unreadable_kg_releases_nothing()
    {
        var wrongStatus = new FakeSource();
        wrongStatus.Orders.Add(Order("Closed", "Cutting", "1"));
        var malformed = new FakeSource();
        malformed.Orders.Add(Order("Open", "Cutting", "NaN"));
        var authority = new FakeAuthority(ErpPermissions.ManufacturingOrdersRead);

        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new ManufacturingOrderStatusAiTool(wrongStatus, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new ManufacturingOrderStatusAiTool(malformed, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Fact]
    public async Task Orders_without_the_grant_or_with_a_work_center_scoped_grant_are_denied_and_the_list_is_not_read()
    {
        var source = new FakeSource();
        source.Orders.Add(Order("Open", "Cutting", "1"));

        Assert.Equal(AiToolOutcomeV1.Denied, (await new ManufacturingOrderStatusAiTool(source, new FakeAuthority(), new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Denied, (await new ManufacturingOrderStatusAiTool(source,
                new FakeAuthority(ErpPermissions.ManufacturingOrdersRead) { ScopedToWorkCenter = WorkCenter }, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Empty(source.OrderStatuses);
    }

    [Fact]
    public async Task An_owner_denial_is_denied_and_over_the_scan_cap_the_order_counts_are_withheld()
    {
        var denying = new FakeSource { Status = ManufacturingApiStatus.Denied };
        var many = new FakeSource();
        for (var i = 0; i < ManufacturingOrderStatusAiTool.ScanLimit + 1; i++)
        {
            many.Orders.Add(Order("Open", "Cutting", "1"));
        }

        var authority = new FakeAuthority(ErpPermissions.ManufacturingOrdersRead);
        Assert.Equal(AiToolOutcomeV1.Denied, (await new ManufacturingOrderStatusAiTool(denying, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        var incomplete = await new ManufacturingOrderStatusAiTool(many, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context());
        using var content = JsonDocument.Parse(incomplete.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("orderCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("plannedInputKgTotal").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("byProcess").ValueKind);
    }

    [Fact]
    public async Task Plans_default_to_draft_and_approved_and_count_each_status()
    {
        var source = new FakeSource();
        source.Plans.Add(PlanRow("Draft", "250.25"));
        source.Plans.Add(PlanRow("Draft", "250.25"));
        source.Plans.Add(PlanRow("Approved", "99.5"));
        source.Plans.Add(PlanRow("Cancelled", "1"));
        var tool = new ManufacturingPlanStatusAiTool(source, new FakeAuthority(ErpPermissions.ManufacturingPlansRead), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(3, root.GetProperty("planCount").GetInt32());
        Assert.Equal("600", root.GetProperty("plannedInputKgTotal").GetString());
        var byStatus = root.GetProperty("byStatus").EnumerateArray()
            .ToDictionary(item => item.GetProperty("status").GetString()!, item => item.GetProperty("count").GetInt32());
        Assert.Equal(2, byStatus["Draft"]);
        Assert.Equal(1, byStatus["Approved"]);
        Assert.Equal(new[] { "Draft", "Approved" }, source.PlanStatuses);
    }

    [Theory]
    [InlineData("{\"status\":\"open\"}", "status")]
    [InlineData("{\"status\":\"Paused\"}", "status")]
    [InlineData("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000004a1\"}", "arguments")]
    public async Task Plan_arguments_outside_the_closed_schema_are_invalid_before_any_read(string arguments, string field)
    {
        var source = new FakeSource();
        var authority = new FakeAuthority(ErpPermissions.ManufacturingPlansRead);

        var result = await new ManufacturingPlanStatusAiTool(source, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Equal(new[] { field }, result.InvalidFields);
        Assert.Empty(source.PlanStatuses);
        Assert.Empty(authority.Measured);
    }

    [Fact]
    public async Task The_work_center_queue_keeps_the_owner_order_and_totals_planned_processed_and_remaining_kg()
    {
        var other = Guid.Parse("eeeeeeee-0000-4000-8000-0000000004e5");
        var source = new FakeSource
        {
            Queue = new PlanWorkCenterQueueViewV1(1, Company, WorkCenter, Plan, 4,
            [
                new PlanWorkCenterQueueLineV1(other, new DateOnly(2026, 11, 25), 10, 1, "500", "100.5", "399.5", false, true),
                new PlanWorkCenterQueueLineV1(Plan, new DateOnly(2026, 11, 30), 5, 2, "300", "0", "300", false, true),
            ], 9, true),
        };
        var tool = new ManufacturingWorkCenterQueueAiTool(source, new FakeAuthority(ErpPermissions.ManufacturingPlansRead), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1($"{{\"planId\":\"{Plan:D}\"}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(2, root.GetProperty("planCount").GetInt32());
        Assert.Equal("800", root.GetProperty("plannedKgTotal").GetString());
        Assert.Equal("100.5", root.GetProperty("processedKgTotal").GetString());
        Assert.Equal("699.5", root.GetProperty("remainingKgTotal").GetString());
        Assert.True(root.GetProperty("hasManualOrder").GetBoolean());
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(other, items[0].GetProperty("planId").GetGuid());
        Assert.Equal(1, items[0].GetProperty("position").GetInt32());
        Assert.Equal(Plan, items[1].GetProperty("planId").GetGuid());
        Assert.Equal(WorkCenter, result.Source!.TargetId);
        Assert.Equal(Plan, Assert.Single(source.QueuePlans));
    }

    [Fact]
    public async Task Another_companys_queue_a_missing_plan_and_a_missing_plan_id_release_nothing()
    {
        var foreign = new FakeSource
        {
            Queue = new PlanWorkCenterQueueViewV1(1, OtherCompany, WorkCenter, Plan, 1,
                [new PlanWorkCenterQueueLineV1(Plan, null, 0, 1, "1", "0", "1", false, true)]),
        };
        var missing = new FakeSource { Status = ManufacturingApiStatus.NotFound };
        var authority = new FakeAuthority(ErpPermissions.ManufacturingPlansRead);
        var arguments = new AiToolInputV1($"{{\"planId\":\"{Plan:D}\"}}");

        Assert.Equal(AiToolOutcomeV1.Unavailable,
            (await new ManufacturingWorkCenterQueueAiTool(foreign, authority, new FixedTime(Now)).ReadAsync(arguments, Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.NotFound,
            (await new ManufacturingWorkCenterQueueAiTool(missing, authority, new FixedTime(Now)).ReadAsync(arguments, Context())).Outcome);
        var noPlan = await new ManufacturingWorkCenterQueueAiTool(missing, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context());
        Assert.Equal(AiToolOutcomeV1.Invalid, noPlan.Outcome);
        Assert.Equal(new[] { "planId" }, noPlan.InvalidFields);
    }

    [Fact]
    public async Task A_queue_read_after_which_the_plan_grant_is_revoked_releases_nothing()
    {
        var source = new FakeSource
        {
            Queue = new PlanWorkCenterQueueViewV1(1, Company, WorkCenter, Plan, 1,
                [new PlanWorkCenterQueueLineV1(Plan, null, 0, 1, "1", "0", "1", false, true)]),
        };
        var authority = new FakeAuthority(ErpPermissions.ManufacturingPlansRead) { RevokeAfterCalls = 1 };

        var result = await new ManufacturingWorkCenterQueueAiTool(source, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1($"{{\"planId\":\"{Plan:D}\"}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Single(source.QueuePlans);
    }

    private static AiServerContextV1 Context() => new(Company, "actor-sentetik", "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-wpa3b-sentetik", null, null, null);

    private static OrderSummaryViewV1 Order(string status, string process, string kg) => new(Guid.NewGuid(), Guid.NewGuid(),
        WorkCenter, process, status, 1, 2, kg, null, Now, null);

    private static PlanSummaryViewV1 PlanRow(string status, string kg) => new(Guid.NewGuid(), WorkCenter, "Slitting", status, 1, 1,
        kg, null, Now, status == "Approved" ? Now : null);

    private sealed class FakeSource : IManufacturingAiReadSourceV1
    {
        public List<OrderSummaryViewV1> Orders { get; } = [];
        public List<PlanSummaryViewV1> Plans { get; } = [];
        public PlanWorkCenterQueueViewV1? Queue { get; init; }
        public ManufacturingApiStatus Status { get; init; } = ManufacturingApiStatus.Ok;
        public List<string?> OrderStatuses { get; } = [];
        public List<string?> PlanStatuses { get; } = [];
        public List<Guid> QueuePlans { get; } = [];

        public Task<ManufacturingApiResult<OrderListViewV1>> ListOrdersAsync(int pageSize, string? cursor, string? status,
            CancellationToken cancellationToken)
        {
            OrderStatuses.Add(status);
            if (Status != ManufacturingApiStatus.Ok) return Task.FromResult(new ManufacturingApiResult<OrderListViewV1>(Status, null, false, null));
            // Unfiltered on purpose: an owner row in another status must be caught by the tool.
            var (items, next) = Page(Orders.ToArray(), pageSize, cursor);
            return Task.FromResult(new ManufacturingApiResult<OrderListViewV1>(ManufacturingApiStatus.Ok,
                new OrderListViewV1(items, next), false, null));
        }

        public Task<ManufacturingApiResult<PlanListViewV1>> ListPlansAsync(int pageSize, string? cursor, string? status,
            CancellationToken cancellationToken)
        {
            PlanStatuses.Add(status);
            if (Status != ManufacturingApiStatus.Ok) return Task.FromResult(new ManufacturingApiResult<PlanListViewV1>(Status, null, false, null));
            var (items, next) = Page(Plans.Where(plan => plan.Status == status).ToArray(), pageSize, cursor);
            return Task.FromResult(new ManufacturingApiResult<PlanListViewV1>(ManufacturingApiStatus.Ok,
                new PlanListViewV1(items, next), false, null));
        }

        public Task<ManufacturingApiResult<PlanWorkCenterQueueViewV1>> GetPlanWorkCenterQueueAsync(Guid planId,
            CancellationToken cancellationToken)
        {
            QueuePlans.Add(planId);
            return Task.FromResult(new ManufacturingApiResult<PlanWorkCenterQueueViewV1>(Status,
                Status == ManufacturingApiStatus.Ok ? Queue : null, false, null));
        }

        private static (T[] Items, string? Next) Page<T>(T[] all, int size, string? cursor)
        {
            var start = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
            var items = all.Skip(start).Take(size).ToArray();
            var end = start + items.Length;
            return (items, end < all.Length ? end.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        }
    }

    private sealed class FakeAuthority(params string[] granted) : IErpAuthorityRevalidationService
    {
        public List<string> Measured { get; } = [];
        public Guid? ScopedToWorkCenter { get; init; }
        public int? RevokeAfterCalls { get; init; }

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Measured.Add(permission);
            if (companyId != Company || !granted.Contains(permission, StringComparer.Ordinal)
                || (RevokeAfterCalls is { } limit && Measured.Count > limit))
            {
                return Task.FromResult(PermissionScope.Denied);
            }

            var workCenters = ScopedToWorkCenter is { } id ? new HashSet<Guid> { id } : new HashSet<Guid>();
            return Task.FromResult(new PermissionScope(true, ScopedToWorkCenter is null, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), workCenters));
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
