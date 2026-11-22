using System.Text.Json;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Application.Ai;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// AI-D45 <c>inventory.average-cost.v1</c> over a SENTETIK fake of the owner's cost port: only the cost-view grant
/// (company-wide) reads, the data is CONFIDENTIAL and never routable to a cloud model, the cost is the owner's exact value,
/// an unreadable or missing cost is never a zero, and another company's or another position's answer releases nothing.
/// </summary>
public sealed class InventoryAverageCostAiToolTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000006a1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000006b2");
    private static readonly Guid Product = Guid.Parse("cccccccc-0000-4000-8000-0000000006c3");
    private static readonly Guid Location = Guid.Parse("dddddddd-0000-4000-8000-0000000006d4");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 13, 0, 0, TimeSpan.Zero);
    private static readonly string Arguments = $"{{\"productId\":\"{Product:D}\",\"locationId\":\"{Location:D}\"}}";

    [Fact]
    public void The_descriptor_is_a_confidential_inventory_read_tool_behind_the_cost_view_permission_only()
    {
        var descriptor = Tool(new FakeCosts(), new FakeAuthority()).Descriptor;

        Assert.Equal("inventory.average-cost.v1", descriptor.ToolId);
        Assert.Equal("Inventory", descriptor.OwnerModule);
        Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
        Assert.Equal(AiDataClassV1.Confidential, descriptor.DataClass);
        Assert.Null(descriptor.OwnerCommandName);
        Assert.Equal(new[] { "inventory.average-cost.read" }, descriptor.RequiredPermissions);
        Assert.Equal(new[] { "locationId", "productId" }, descriptor.FieldAllowlist);
        Assert.False(AiGatewayPlacementRulesV1.PlacementAllows(AiModelPlacementV1.Cloud, descriptor.DataClass));
        Assert.True(AiGatewayPlacementRulesV1.PlacementAllows(AiModelPlacementV1.OnPremises, descriptor.DataClass));
    }

    [Fact]
    public async Task A_readable_cost_is_the_owners_exact_value_per_kg_with_a_confidential_source()
    {
        var costs = new FakeCosts { Answer = InventoryAverageCostReadAnswerV1.ForValue(Company, Location, Product, 41.2500m, 17) };

        var result = await Tool(costs, new FakeAuthority(ErpPermissions.InventoryAverageCostRead))
            .ReadAsync(new AiToolInputV1(Arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.Equal("Value", root.GetProperty("status").GetString());
        Assert.Equal("41.25", root.GetProperty("averageCostPerKg").GetString());
        Assert.Equal("kg", root.GetProperty("costUnit").GetString());
        Assert.Equal("purchase-moving-average", root.GetProperty("costBasis").GetString());
        Assert.Equal(AiDataClassV1.Confidential, result.Source!.DataClass);
        Assert.Equal((Location, Product), Assert.Single(costs.Reads));
    }

    [Fact]
    public async Task An_unreadable_or_missing_cost_is_said_as_such_and_never_becomes_zero()
    {
        var authority = new FakeAuthority(ErpPermissions.InventoryAverageCostRead);
        var unreadable = await Tool(new FakeCosts
        {
            Answer = InventoryAverageCostReadAnswerV1.ForUnreadable(Company, Location, Product,
                InventoryAverageCostUnreadableCodesV1.NegativeStock, 3),
        }, authority).ReadAsync(new AiToolInputV1(Arguments), Context());
        var missing = await Tool(new FakeCosts { Answer = InventoryAverageCostReadAnswerV1.ForNoSource(Company, Location, Product) },
            authority).ReadAsync(new AiToolInputV1(Arguments), Context());

        using var first = JsonDocument.Parse(unreadable.ContentJson!);
        Assert.False(first.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.RootElement.GetProperty("averageCostPerKg").ValueKind);
        Assert.Equal("negative_stock", first.RootElement.GetProperty("unreadableCode").GetString());
        using var second = JsonDocument.Parse(missing.ContentJson!);
        Assert.Equal("NoSource", second.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("averageCostPerKg").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("unreadableCode").ValueKind);
    }

    [Fact]
    public async Task Without_the_cost_view_grant_even_with_stock_reads_the_cost_is_denied_and_not_read()
    {
        var costs = new FakeCosts { Answer = InventoryAverageCostReadAnswerV1.ForValue(Company, Location, Product, 1m, 1) };
        var authority = new FakeAuthority(ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead,
            ErpPermissions.SalesOrdersMarginRead);

        var result = await Tool(costs, authority).ReadAsync(new AiToolInputV1(Arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.ContentJson);
        Assert.Empty(costs.Reads);
    }

    [Fact]
    public async Task A_location_scoped_cost_grant_is_not_company_wide_and_reads_nothing()
    {
        var costs = new FakeCosts { Answer = InventoryAverageCostReadAnswerV1.ForValue(Company, Location, Product, 1m, 1) };
        var authority = new FakeAuthority(ErpPermissions.InventoryAverageCostRead) { ScopedTo = Location };

        var result = await Tool(costs, authority).ReadAsync(new AiToolInputV1(Arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome);
        Assert.Empty(costs.Reads);
    }

    [Fact]
    public async Task Another_companys_or_another_positions_answer_or_a_revoked_grant_releases_nothing()
    {
        var authority = new FakeAuthority(ErpPermissions.InventoryAverageCostRead);
        var foreign = new FakeCosts { Answer = InventoryAverageCostReadAnswerV1.ForValue(OtherCompany, Location, Product, 1m, 1) };
        var otherProduct = new FakeCosts
        {
            Answer = InventoryAverageCostReadAnswerV1.ForValue(Company, Location, Guid.Parse("eeeeeeee-0000-4000-8000-0000000006e5"), 1m, 1),
        };
        var failing = new FakeCosts { Failure = new InvalidOperationException("SENTETIK") };
        var revoked = new FakeAuthority(ErpPermissions.InventoryAverageCostRead) { RevokeAfterCalls = 1 };
        var good = new FakeCosts { Answer = InventoryAverageCostReadAnswerV1.ForValue(Company, Location, Product, 1m, 1) };

        Assert.Equal(AiToolOutcomeV1.Unavailable, (await Tool(foreign, authority).ReadAsync(new AiToolInputV1(Arguments), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await Tool(otherProduct, authority).ReadAsync(new AiToolInputV1(Arguments), Context())).Outcome);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await Tool(failing, authority).ReadAsync(new AiToolInputV1(Arguments), Context())).Outcome);
        var late = await Tool(good, revoked).ReadAsync(new AiToolInputV1(Arguments), Context());
        Assert.Equal(AiToolOutcomeV1.Denied, late.Outcome);
        Assert.Null(late.ContentJson);
        Assert.Single(good.Reads);
    }

    [Theory]
    [InlineData("{}", "productId")]
    [InlineData("{\"productId\":\"cccccccc-0000-4000-8000-0000000006c3\"}", "locationId")]
    [InlineData("{\"productId\":\"cccccccc-0000-4000-8000-0000000006c3\",\"locationId\":\"x\"}", "locationId")]
    [InlineData("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000006a1\"}", "arguments")]
    public async Task Cost_arguments_outside_the_closed_schema_are_invalid_before_any_measurement_or_read(string arguments, string field)
    {
        var costs = new FakeCosts();
        var authority = new FakeAuthority(ErpPermissions.InventoryAverageCostRead);

        var result = await Tool(costs, authority).ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Equal(new[] { field }, result.InvalidFields);
        Assert.Empty(costs.Reads);
        Assert.Empty(authority.Measured);
    }

    private static InventoryAverageCostAiTool Tool(FakeCosts costs, FakeAuthority authority) =>
        new(costs, authority, new FixedTime(Now));

    private static AiServerContextV1 Context() => new(Company, "actor-sentetik", "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-ai-d45-sentetik", null, null, null);

    private sealed class FakeCosts : IInventoryAverageCostReadPortV1
    {
        public InventoryAverageCostReadAnswerV1? Answer { get; init; }
        public Exception? Failure { get; init; }
        public List<(Guid Location, Guid Product)> Reads { get; } = [];

        public Task<InventoryAverageCostReadAnswerV1> ReadAsync(Guid locationId, Guid productId, CancellationToken cancellationToken = default)
        {
            Reads.Add((locationId, productId));
            return Failure is not null ? Task.FromException<InventoryAverageCostReadAnswerV1>(Failure) : Task.FromResult(Answer!);
        }
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
