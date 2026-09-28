using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryPhysicalAllocationReadTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Order = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Line = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Exact_sales_line_window_reuses_the_allocation_view_and_label_version()
    {
        var world = new World();
        world.Reader.HasMore = true;

        var answer = await world.Service().SearchAsync(new(Order, Line, 1));

        var item = Assert.Single(answer.Items);
        Assert.Same(world.Reader.Record.Allocation, item.Allocation);
        Assert.Equal("9", item.LabelVersion);
        Assert.True(answer.SearchWindowFull);
        Assert.Equal((Company, Order, Line, 1), world.Reader.Query);
        Assert.Equal(
            [ErpPermissions.InventoryPhysicalAllocationWrite, ErpPermissions.InventoryPhysicalAllocationWrite],
            world.Authority.Requests);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("line")]
    [InlineData("zero")]
    [InlineData("large")]
    public async Task Invalid_identity_or_limit_is_rejected_before_provider_read(string invalid)
    {
        var world = new World();
        var query = new InventoryPhysicalAllocationReadQueryV1(
            invalid == "order" ? Guid.Empty : Order,
            invalid == "line" ? Guid.Empty : Line,
            invalid == "zero" ? 0 : invalid == "large" ? 101 : 25);

        await Refused(world.Service().SearchAsync(query), 400, InventoryPhysicalAllocationReadCodesV1.InvalidQuery);

        Assert.Equal(0, world.Reader.Reads);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("scope")]
    public async Task Write_authority_must_be_company_wide_before_provider_read(string denial)
    {
        var world = new World();
        world.Authority.Granted = denial != "permission";
        world.Authority.CompanyWide = denial != "scope";

        await Refused(world.Service().SearchAsync(new(Order, Line)), 403,
            InventoryPhysicalAllocationReadCodesV1.Forbidden);

        Assert.Equal(0, world.Reader.Reads);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("membership")]
    [InlineData("authentication")]
    public async Task Awaited_owner_read_cannot_release_facts_after_scope_or_authority_changes(string change)
    {
        var world = new World();
        world.Reader.During = () =>
        {
            switch (change)
            {
                case "permission": world.Authority.Granted = false; break;
                case "company": world.Context.Set(Guid.NewGuid()); break;
                case "actor": world.User.UserId = Guid.NewGuid().ToString(); break;
                case "membership": world.User.Access = false; break;
                case "authentication": world.User.IsAuthenticated = false; break;
            }
        };

        await Refused(world.Service().SearchAsync(new(Order, Line)), 403,
            InventoryPhysicalAllocationReadCodesV1.Forbidden);

        Assert.Equal(1, world.Reader.Reads);
    }

    [Fact]
    public async Task Missing_failed_or_inconsistent_provider_fails_closed()
    {
        var world = new World();
        await Refused(new InventoryPhysicalAllocationReadService(
                world.Context, world.User, world.Authority, null).SearchAsync(new(Order, Line)),
            503, InventoryPhysicalAllocationReadCodesV1.Unavailable);

        world.Reader.Fail = true;
        await Refused(world.Service().SearchAsync(new(Order, Line)),
            503, InventoryPhysicalAllocationReadCodesV1.Unavailable);

        world.Reader.Fail = false;
        world.Reader.Record = world.Reader.Record with { LabelVersion = 0 };
        await Refused(world.Service().SearchAsync(new(Order, Line)),
            503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
    }

    [Fact]
    public async Task Late_denial_takes_precedence_over_provider_failure()
    {
        var world = new World();
        world.Reader.Fail = true;
        world.Reader.During = () => world.Authority.Granted = false;

        await Refused(world.Service().SearchAsync(new(Order, Line)),
            403, InventoryPhysicalAllocationReadCodesV1.Forbidden);
    }

    [Fact]
    public async Task Unauthenticated_or_unresolved_company_never_reaches_provider()
    {
        var world = new World();
        world.User.IsAuthenticated = false;
        await Refused(world.Service().SearchAsync(new(Order, Line)),
            401, InventoryPhysicalAllocationReadCodesV1.Unauthenticated);

        world.User.IsAuthenticated = true;
        world.Context.Set(Guid.Empty);
        await Refused(world.Service().SearchAsync(new(Order, Line)),
            403, InventoryPhysicalAllocationReadCodesV1.Forbidden);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    [InlineData("line")]
    [InlineData("version")]
    [InlineData("kind")]
    [InlineData("status")]
    [InlineData("precision")]
    [InlineData("remaining")]
    [InlineData("reason")]
    [InlineData("time")]
    public async Task Cross_scope_or_corrupt_provider_facts_fail_closed(string corruption)
    {
        var world = new World();
        var record = world.Reader.Record;
        var allocation = record.Allocation;
        world.Reader.Record = record with
        {
            Allocation = corruption switch
            {
                "company" => allocation with { CompanyId = Guid.NewGuid() },
                "order" => allocation with { DemandId = Guid.NewGuid() },
                "line" => allocation with { DemandLineId = Guid.NewGuid() },
                "version" => allocation with { Version = "0" },
                "kind" => allocation with { DemandKind = "manufacturing.plan" },
                "status" => allocation with { Status = "999", RemainingKg = "0" },
                "precision" => allocation with { RemainingKg = "1.0001" },
                "remaining" => allocation with { RemainingKg = "11" },
                "reason" => allocation with { Reason = " " },
                "time" => allocation with { UpdatedAtUtc = allocation.CreatedAtUtc.AddMinutes(-1) },
                _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
            },
        };

        await Refused(world.Service().SearchAsync(new(Order, Line)),
            503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
    }

    [Fact]
    public async Task Label_read_returns_the_owner_window_with_history_under_the_same_fresh_authority()
    {
        var world = new World();
        var label = Guid.NewGuid();
        var allocation = Allocation() with { LabelId = label };
        var change = new InventoryPhysicalAllocationChangeEntryV1(Guid.NewGuid(), InventoryAllocationChangeKindsV1.Resize,
            allocation.AllocationId, "2", label, "12", allocation.AllocationId, label, "10", "SENTETIK",
            DateTimeOffset.Parse("2026-09-24T09:00:00Z"));
        world.Reader.LabelAnswer = new InventoryLabelPhysicalAllocationsV1(label, "5",
            [new InventoryLabelPhysicalAllocationV1(allocation, null, null, [change])], false);

        var answer = await world.Service().ReadLabelAsync(label);

        Assert.Same(world.Reader.LabelAnswer, answer);
        Assert.Equal(
            [ErpPermissions.InventoryPhysicalAllocationWrite, ErpPermissions.InventoryPhysicalAllocationWrite],
            world.Authority.Requests);
    }

    [Fact]
    public async Task Label_read_of_an_unknown_label_is_404_and_an_empty_id_is_400_before_the_read()
    {
        var world = new World();

        await Refused(world.Service().ReadLabelAsync(Guid.NewGuid()), 404, InventoryPhysicalAllocationReadCodesV1.NotFound);
        Assert.Equal(1, world.Reader.Reads);

        await Refused(world.Service().ReadLabelAsync(Guid.Empty), 400, InventoryPhysicalAllocationReadCodesV1.InvalidQuery);
        Assert.Equal(1, world.Reader.Reads);
    }

    [Theory]
    [InlineData("foreign-allocation")]
    [InlineData("other-label")]
    [InlineData("released-without-reason")]
    [InlineData("unrelated-change")]
    public async Task Label_read_fails_closed_on_an_inconsistent_owner_answer(string fault)
    {
        var world = new World();
        var label = Guid.NewGuid();
        var allocation = Allocation() with { LabelId = label };
        allocation = fault switch
        {
            "foreign-allocation" => allocation with { CompanyId = Guid.NewGuid() },
            "other-label" => allocation with { LabelId = Guid.NewGuid() },
            "released-without-reason" => allocation with { Status = "Released", RemainingKg = "0" },
            _ => allocation,
        };
        IReadOnlyList<InventoryPhysicalAllocationChangeEntryV1> changes = fault == "unrelated-change"
            ?
            [
                new InventoryPhysicalAllocationChangeEntryV1(Guid.NewGuid(), InventoryAllocationChangeKindsV1.Move,
                    Guid.NewGuid(), "1", label, "5", Guid.NewGuid(), Guid.NewGuid(), "5", "SENTETIK",
                    DateTimeOffset.Parse("2026-09-24T09:00:00Z")),
            ]
            : [];
        world.Reader.LabelAnswer = new InventoryLabelPhysicalAllocationsV1(label, "5",
            [new InventoryLabelPhysicalAllocationV1(allocation,
                fault == "released-without-reason" ? DateTimeOffset.Parse("2026-09-24T09:00:00Z") : null, null, changes)],
            false);

        await Refused(world.Service().ReadLabelAsync(label), 503, InventoryPhysicalAllocationReadCodesV1.Unavailable);
    }

    [Fact]
    public void Registration_publishes_the_owner_contract_as_scoped()
    {
        var services = new ServiceCollection();

        services.AddInventoryPhysicalAllocationReads();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IInventoryPhysicalAllocationReadsV1)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => task);
        Assert.Equal((status, code), (error.StatusCode, error.Code));
    }

    private static InventoryPhysicalAllocationViewV1 Allocation() => new(
        Guid.NewGuid(),
        Company,
        "3",
        "Active",
        Guid.NewGuid(),
        Guid.NewGuid(),
        InventoryAllocationDemandKindsV1.SalesOrderLine,
        Order,
        Line,
        "7",
        "10",
        "10",
        "Onaylı satış satırı için fiziksel kaynak.",
        null,
        DateTimeOffset.Parse("2026-09-24T08:00:00Z"),
        DateTimeOffset.Parse("2026-09-24T08:00:00Z"));

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Reader Reader { get; } = new();
        public InventoryPhysicalAllocationReadService Service() => new(Context, User, Authority, Reader);
    }

    private sealed class Context : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = Guid.NewGuid().ToString();
        public bool Access { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => Access && AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public bool CompanyWide { get; set; } = true;
        public List<string> Requests { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(permission);
            return Task.FromResult(new PermissionScope(
                Granted,
                CompanyWide,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        }
    }

    private sealed class Reader : IInventoryPhysicalAllocationReader
    {
        public InventoryPhysicalAllocationReadRecord Record { get; set; } = new(Allocation(), 9);
        public Action? During { get; set; }
        public bool Fail { get; set; }
        public bool HasMore { get; set; }
        public int Reads { get; private set; }
        public (Guid CompanyId, Guid OrderId, Guid LineId, int Limit) Query { get; private set; }

        public async Task<InventoryPhysicalAllocationReadWindow> SearchAsync(
            Guid companyId,
            Guid salesOrderId,
            Guid salesOrderLineId,
            int limit,
            CancellationToken cancellationToken)
        {
            Reads++;
            Query = (companyId, salesOrderId, salesOrderLineId, limit);
            await Task.Yield();
            During?.Invoke();
            if (Fail)
            {
                throw new InvalidOperationException("Provider failure");
            }

            return new InventoryPhysicalAllocationReadWindow([Record], HasMore);
        }

        public InventoryLabelPhysicalAllocationsV1? LabelAnswer { get; set; }

        public Task<InventoryLabelPhysicalAllocationsV1?> ReadLabelAsync(
            Guid companyId,
            Guid labelId,
            int limit,
            CancellationToken cancellationToken)
        {
            Reads++;
            During?.Invoke();
            return Fail
                ? throw new InvalidOperationException("Provider failure")
                : Task.FromResult(LabelAnswer);
        }
    }
}
