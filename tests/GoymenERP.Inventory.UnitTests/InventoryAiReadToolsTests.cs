using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Application.Ai;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// WP-A3b Inventory READ tools over SENTETIK fakes of the Inventory owner reads: exact kg totals from the owner pages, a
/// refused or site-scoped grant reads nothing, another company's row releases nothing, a full window or scan cap withholds
/// the counts, and no actor, reason or cost leaves the tool.
/// </summary>
public sealed class InventoryAiReadToolsTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000003a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000003b2");
    private static readonly Guid Product = Guid.Parse("cccccccc-0000-4000-8000-0000000003c3");
    private static readonly Guid Location = Guid.Parse("dddddddd-0000-4000-8000-0000000003d4");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] PositionPermissions = [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead];
    private static readonly string[] CountPermissions =
        [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead, InventoryPermissionManifestV1.StockCountApprove];

    [Fact]
    public void The_three_descriptors_are_internal_inventory_read_tools_with_closed_fields_and_owner_permissions()
    {
        var authority = new FakeAuthority();
        var positions = new InventoryStockPositionsAiTool(new FakePositions(), authority, new FixedTime(Now)).Descriptor;
        var attempts = new InventoryNegativeStockAttemptsAiTool(new FakeAttempts(), authority, new FixedTime(Now)).Descriptor;
        var counts = new InventoryPendingStockCountsAiTool(new FakeCounts(), authority, new FixedTime(Now)).Descriptor;

        foreach (var descriptor in new[] { positions, attempts, counts })
        {
            Assert.Equal("Inventory", descriptor.OwnerModule);
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Equal(AiDataClassV1.Internal, descriptor.DataClass);
            Assert.Null(descriptor.OwnerCommandName);
            Assert.Equal("1", descriptor.Version);
        }

        Assert.Equal("inventory.stock-positions.v1", positions.ToolId);
        Assert.Equal(new[] { "labelCode", "limit", "locationId", "productId" }, positions.FieldAllowlist);
        Assert.Equal(PositionPermissions.Order(StringComparer.Ordinal), positions.RequiredPermissions);
        Assert.Equal("inventory.negative-stock-attempts.v1", attempts.ToolId);
        Assert.Equal(new[] { "limit", "locationId", "openOnly", "productId" }, attempts.FieldAllowlist);
        Assert.Equal("inventory.pending-stock-counts.v1", counts.ToolId);
        Assert.Equal(new[] { "limit" }, counts.FieldAllowlist);
        Assert.Equal(CountPermissions.Order(StringComparer.Ordinal), counts.RequiredPermissions);
    }

    [Fact]
    public async Task Stock_positions_give_exact_kg_totals_over_every_owner_page_and_list_only_the_limit()
    {
        var owner = new FakePositions();
        for (var i = 0; i < 150; i++)
        {
            owner.Add(Position(Company, $"L-{i:D4}", "10.250", "8.250", "2.000", "1.125", "7.125"));
        }

        var tool = new InventoryStockPositionsAiTool(owner, new FakeAuthority(PositionPermissions), new FixedTime(Now));
        var result = await tool.ReadAsync(new AiToolInputV1("{\"limit\":5}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.Equal("kg", root.GetProperty("unit").GetString());
        Assert.Equal(150, root.GetProperty("labelCount").GetInt32());
        Assert.Equal("1537.5", root.GetProperty("physicalKg").GetString());
        Assert.Equal("1237.5", root.GetProperty("freePhysicalKg").GetString());
        Assert.Equal("300", root.GetProperty("blockedPhysicalKg").GetString());
        Assert.Equal("168.75", root.GetProperty("activeAllocationKg").GetString());
        Assert.Equal("1068.75", root.GetProperty("availableKg").GetString());
        Assert.Equal(5, root.GetProperty("items").GetArrayLength());
        Assert.Equal(2, owner.Queries.Count);
        Assert.Equal(AiDataClassV1.Internal, result.Source!.DataClass);
        Assert.Equal(Company, result.Source.TargetId);
        Assert.DoesNotContain("heat", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owner", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stock_positions_pass_the_label_product_and_location_filters_to_the_owner_read()
    {
        var owner = new FakePositions();
        owner.Add(Position(Company, "L-0001", "1", "1", "0", "0", "1"));
        var tool = new InventoryStockPositionsAiTool(owner, new FakeAuthority(PositionPermissions), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1(
            $"{{\"labelCode\":\"L-0001\",\"productId\":\"{Product:D}\",\"locationId\":\"{Location:D}\"}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        var query = Assert.Single(owner.Queries);
        Assert.Equal("L-0001", query.Q);
        Assert.Equal(Product, query.ProductId);
        Assert.Equal(Location, query.LocationId);
        Assert.Equal(InventoryStockPositionsAiTool.PageSize, query.Limit);
    }

    [Fact]
    public async Task Stock_positions_over_the_scan_cap_withhold_every_total_and_say_incomplete()
    {
        var owner = new FakePositions();
        for (var i = 0; i < InventoryStockPositionsAiTool.ScanLimit + 50; i++)
        {
            owner.Add(Position(Company, $"L-{i:D5}", "1", "1", "0", "0", "1"));
        }

        var tool = new InventoryStockPositionsAiTool(owner, new FakeAuthority(PositionPermissions), new FixedTime(Now));
        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.False(root.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("labelCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("physicalKg").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("availableKg").ValueKind);
        Assert.Equal(InventoryStockPositionsAiTool.DefaultLimit, root.GetProperty("listedCount").GetInt32());
    }

    [Fact]
    public async Task Stock_positions_without_the_balance_grant_are_denied_and_read_nothing()
    {
        var owner = new FakePositions();
        var tool = new InventoryStockPositionsAiTool(owner, new FakeAuthority(ErpPermissions.InventoryLabelsRead), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.ContentJson);
        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task A_grant_scoped_to_one_site_or_location_is_not_company_wide_and_reads_nothing()
    {
        var owner = new FakePositions();
        owner.Add(Position(Company, "L-0001", "1", "1", "0", "0", "1"));
        var authority = new FakeAuthority(PositionPermissions) { ScopedTo = Location };
        var tool = new InventoryStockPositionsAiTool(owner, authority, new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1($"{{\"locationId\":\"{Location:D}\"}}"), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task Another_companys_stock_row_releases_nothing()
    {
        var owner = new FakePositions();
        owner.Add(Position(Company, "L-0001", "1", "1", "0", "0", "1"));
        owner.Add(Position(OtherCompany, "L-0002", "1", "1", "0", "0", "1"));
        var tool = new InventoryStockPositionsAiTool(owner, new FakeAuthority(PositionPermissions), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task An_unreadable_kg_text_or_an_owner_refusal_releases_nothing()
    {
        var malformed = new FakePositions();
        malformed.Add(Position(Company, "L-0001", "1e3", "1", "0", "0", "1"));
        var refusing = new FakePositions { Refusal = new InventoryStockPositionException(403, InventoryStockPositionCodesV1.Forbidden) };
        var authority = new FakeAuthority(PositionPermissions);

        Assert.Equal(AiToolOutcomeV1.Unavailable,
            (await new InventoryStockPositionsAiTool(malformed, authority, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Denied,
            (await new InventoryStockPositionsAiTool(refusing, authority, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Theory]
    [InlineData("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000003a1\"}", "arguments")]
    [InlineData("{\"limit\":0}", "limit")]
    [InlineData("{\"limit\":51}", "limit")]
    [InlineData("{\"productId\":\"00000000-0000-0000-0000-000000000000\"}", "productId")]
    [InlineData("{\"labelCode\":\" L-1\"}", "labelCode")]
    [InlineData("[]", "arguments")]
    public async Task Stock_position_arguments_outside_the_closed_schema_are_invalid_before_any_read(string arguments, string field)
    {
        var owner = new FakePositions();
        var authority = new FakeAuthority(PositionPermissions);
        var tool = new InventoryStockPositionsAiTool(owner, authority, new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Equal(new[] { field }, result.InvalidFields);
        Assert.Empty(owner.Queries);
        Assert.Empty(authority.Measured);
    }

    [Fact]
    public async Task Negative_stock_attempts_count_open_ones_and_total_the_missing_kg_without_the_actor()
    {
        var owner = new FakeAttempts();
        owner.Items.Add(Attempt(Company, "5.5", resolved: false));
        owner.Items.Add(Attempt(Company, "2.25", resolved: false));
        var tool = new InventoryNegativeStockAttemptsAiTool(owner, new FakeAuthority(PositionPermissions), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.True(root.GetProperty("openOnly").GetBoolean());
        Assert.Equal(2, root.GetProperty("attemptCount").GetInt32());
        Assert.Equal(2, root.GetProperty("openCount").GetInt32());
        Assert.Equal("7.75", root.GetProperty("missingKgTotal").GetString());
        Assert.True(Assert.Single(owner.Queries).OpenOnly);
        Assert.DoesNotContain("actor", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("decidedBy", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_full_owner_window_withholds_the_attempt_counts_and_a_settled_row_under_open_only_releases_nothing()
    {
        var full = new FakeAttempts { WindowFull = true };
        full.Items.Add(Attempt(Company, "1", resolved: false));
        var wrong = new FakeAttempts();
        wrong.Items.Add(Attempt(Company, "1", resolved: true));
        var authority = new FakeAuthority(PositionPermissions);

        var incomplete = await new InventoryNegativeStockAttemptsAiTool(full, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context());
        using var content = JsonDocument.Parse(incomplete.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("attemptCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("missingKgTotal").ValueKind);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new InventoryNegativeStockAttemptsAiTool(wrong, authority, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Fact]
    public async Task Negative_stock_attempts_of_another_company_or_without_a_grant_release_nothing()
    {
        var foreign = new FakeAttempts();
        foreign.Items.Add(Attempt(OtherCompany, "1", resolved: false));
        var owner = new FakeAttempts();

        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new InventoryNegativeStockAttemptsAiTool(foreign,
            new FakeAuthority(PositionPermissions), new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Denied, (await new InventoryNegativeStockAttemptsAiTool(owner,
            new FakeAuthority(), new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Empty(owner.Queries);
    }

    [Fact]
    public async Task Pending_stock_counts_total_surplus_shortage_and_net_exactly_without_reason_or_submitter()
    {
        var owner = new FakeCounts();
        owner.Pages.Add([Count(Company, "100", "102.5", "2.5"), Count(Company, "50", "49.25", "-0.75")]);
        owner.Pages.Add([Count(Company, "10", "9", "-1")]);
        var tool = new InventoryPendingStockCountsAiTool(owner, new FakeAuthority(CountPermissions), new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(3, root.GetProperty("pendingCount").GetInt32());
        Assert.Equal("2.5", root.GetProperty("surplusKgTotal").GetString());
        Assert.Equal("-1.75", root.GetProperty("shortageKgTotal").GetString());
        Assert.Equal("0.75", root.GetProperty("netDifferenceKg").GetString());
        Assert.Equal(2, owner.Cursors.Count);
        Assert.Equal(Company, owner.Contexts[0].CompanyId);
        Assert.DoesNotContain("reason", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("submittedBy", result.ContentJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pending_stock_counts_need_the_approve_grant_and_an_owner_denial_or_foreign_row_releases_nothing()
    {
        var owner = new FakeCounts();
        owner.Pages.Add([Count(Company, "1", "2", "1")]);
        var denied = new FakeCounts { Outcome = InventoryPendingApprovalReadOutcomeV1.Denied };
        var foreign = new FakeCounts();
        foreign.Pages.Add([Count(OtherCompany, "1", "2", "1")]);
        var zero = new FakeCounts();
        zero.Pages.Add([Count(Company, "1", "1", "0")]);

        Assert.Equal(AiToolOutcomeV1.Denied, (await new InventoryPendingStockCountsAiTool(owner,
            new FakeAuthority(PositionPermissions), new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Empty(owner.Cursors);
        Assert.Equal(AiToolOutcomeV1.Denied, (await new InventoryPendingStockCountsAiTool(denied,
            new FakeAuthority(CountPermissions), new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new InventoryPendingStockCountsAiTool(foreign,
            new FakeAuthority(CountPermissions), new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new InventoryPendingStockCountsAiTool(zero,
            new FakeAuthority(CountPermissions), new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);
    }

    [Fact]
    public async Task A_grant_revoked_while_the_owner_read_ran_releases_nothing()
    {
        var owner = new FakePositions();
        owner.Add(Position(Company, "L-0001", "1", "1", "0", "0", "1"));
        var authority = new FakeAuthority(PositionPermissions) { RevokeAfterCalls = PositionPermissions.Length };
        var tool = new InventoryStockPositionsAiTool(owner, authority, new FixedTime(Now));

        var result = await tool.ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Single(owner.Queries);
        Assert.Null(result.ContentJson);
    }

    private static AiServerContextV1 Context() => new(Company, "actor-sentetik", "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-wpa3b-sentetik", null, null, null);

    private static InventoryStockPositionV1 Position(Guid company, string code, string physical, string free, string blocked,
        string allocated, string available) => new(company, Guid.NewGuid(), "7", code, Product, "3", "Prime",
        new InventoryLotV1(Guid.NewGuid(), "HEAT-SENTETIK", null), "HEAT-SENTETIK", null, "Complete", "Company", company,
        "Company", company, Location, physical, free, blocked, allocated, available);

    private static InventoryNegativeStockAttemptViewV1 Attempt(Guid company, string missing, bool resolved) => new(
        Guid.NewGuid(), company, Guid.NewGuid(), "inventory.movement.record.v1", "actor-sentetik", Now, Product, Location,
        Guid.NewGuid(), "L-NEG", "Issue", "Sales", Guid.NewGuid(), null, "10", "0", missing,
        resolved ? new InventoryNegativeStockAttemptResolutionV1(Guid.NewGuid(), Now, "decider-sentetik") : null);

    private static InventoryStockCountPendingApprovalItemV1 Count(Guid company, string book, string counted, string difference) => new(
        Guid.NewGuid(), company, Guid.NewGuid(), "L-COUNT", "4", "Company", company, "Company", company, Location, Product,
        "Free", book, counted, difference, "SENTETIK neden", "submitter-sentetik", Now);

    private sealed class FakePositions : IInventoryStockPositionsV1
    {
        private readonly List<InventoryStockPositionV1> rows = [];
        public List<InventoryStockPositionQueryV1> Queries { get; } = [];
        public Exception? Refusal { get; init; }

        public void Add(InventoryStockPositionV1 row) => rows.Add(row);

        public Task<InventoryStockPositionsV1> SearchAsync(InventoryStockPositionQueryV1 query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (Refusal is not null)
            {
                throw Refusal;
            }

            var start = query.Cursor is null ? 0 : int.Parse(query.Cursor, System.Globalization.CultureInfo.InvariantCulture);
            var page = rows.Skip(start).Take(query.Limit).ToArray();
            var more = start + page.Length < rows.Count;
            return Task.FromResult(new InventoryStockPositionsV1(page, more,
                more ? (start + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
        }
    }

    private sealed class FakeAttempts : IInventoryNegativeStockAttemptsV1
    {
        public List<InventoryNegativeStockAttemptViewV1> Items { get; } = [];
        public List<InventoryNegativeStockAttemptQueryV1> Queries { get; } = [];
        public bool WindowFull { get; init; }

        public Task<InventoryNegativeStockAttemptPageV1> SearchAsync(InventoryNegativeStockAttemptQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new InventoryNegativeStockAttemptPageV1(Company, Items.ToArray(), WindowFull));
        }
    }

    private sealed class FakeCounts : IInventoryStockCountPendingApprovalReadServiceV1
    {
        public List<InventoryStockCountPendingApprovalItemV1[]> Pages { get; } = [];
        public List<string?> Cursors { get; } = [];
        public List<InventoryPendingApprovalReadContextV1> Contexts { get; } = [];
        public InventoryPendingApprovalReadOutcomeV1 Outcome { get; init; } = InventoryPendingApprovalReadOutcomeV1.Found;

        public Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalPageV1>> ListAsync(
            InventoryPendingApprovalReadContextV1 context, string? cursor, CancellationToken cancellationToken)
        {
            Cursors.Add(cursor);
            Contexts.Add(context);
            if (Outcome != InventoryPendingApprovalReadOutcomeV1.Found)
            {
                return Task.FromResult(new InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalPageV1>(Outcome, null));
            }

            var index = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
            var items = Pages.Count == 0 ? [] : Pages[index];
            var next = index + 1 < Pages.Count ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return Task.FromResult(new InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalPageV1>(
                Outcome, new InventoryStockCountPendingApprovalPageV1(context.CompanyId, items, next)));
        }

        public Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalDetailV1>> GetAsync(
            InventoryPendingApprovalReadContextV1 context, Guid countId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAuthority(params string[] granted) : IErpAuthorityRevalidationService
    {
        public List<string> Measured { get; } = [];
        public Guid? ScopedTo { get; init; }
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

            var locations = ScopedTo is { } location ? new HashSet<Guid> { location } : new HashSet<Guid>();
            return Task.FromResult(new PermissionScope(true, ScopedTo is null, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), locations, new HashSet<Guid>()));
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
