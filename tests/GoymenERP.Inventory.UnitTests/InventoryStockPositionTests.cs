using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryStockPositionTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Physical_components_and_active_allocations_are_reported_without_double_counting()
    {
        var world = new World();
        world.Reader.Record = Record(freeKg: 10.125m, blockedKg: 2m, allocatedKg: 4.5m);

        var item = Assert.Single((await world.Service().SearchAsync(new())).Items);

        Assert.Equal(("12.125", "10.125", "2", "4.5", "5.625"),
            (item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                item.ActiveAllocationKg, item.AvailableKg));
        Assert.Equal(("Company", Company, "Company", Company),
            (item.OwnerKind, item.OwnerId, item.CustodianKind, item.CustodianId));
        Assert.Equal("3", item.LabelVersion);
        Assert.Null(item.ProductVersion);
        Assert.Null(item.Classification);
        Assert.Equal(new InventoryLotV1(item.Lot.Id, "HEAT-1", null), item.Lot);
    }

    [Fact]
    public async Task Whole_label_quality_hold_and_active_allocation_are_composed_without_double_subtraction()
    {
        var world = new World();
        world.Reader.Record = Record(freeKg: 10m, blockedKg: 20m, allocatedKg: 10m,
            activeQualityHold: true);

        var item = Assert.Single((await world.Service().SearchAsync(new())).Items);

        Assert.Equal(("30", "10", "20", "10", "0"),
            (item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                item.ActiveAllocationKg, item.AvailableKg));
    }

    [Fact]
    public async Task Fully_allocated_quality_hold_keeps_truthful_free_components_and_zero_available()
    {
        var world = new World();
        world.Reader.Record = Record(freeKg: 30m, blockedKg: 0m, allocatedKg: 30m,
            activeQualityHold: true);

        var item = Assert.Single((await world.Service().SearchAsync(new())).Items);

        Assert.Equal(("30", "30", "0", "30", "0"),
            (item.PhysicalKg, item.FreePhysicalKg, item.BlockedPhysicalKg,
                item.ActiveAllocationKg, item.AvailableKg));
    }

    [Theory]
    [InlineData("missing-hold")]
    [InlineData("partial-hold")]
    [InlineData("over-allocation")]
    public async Task Allocation_overlap_requires_exact_whole_label_quality_hold_evidence(string corruption)
    {
        var world = new World();
        world.Reader.Record = Record(
            freeKg: corruption == "missing-hold" ? 0m : 10m,
            blockedKg: corruption == "missing-hold" ? 30m : 20m,
            allocatedKg: corruption == "over-allocation" ? 31m : 10m,
            activeQualityHold: corruption != "missing-hold",
            qualityHoldKg: corruption == "partial-hold" ? 20m : null);

        await Refused(world.Service().SearchAsync(new()), 503, InventoryStockPositionCodesV1.Unavailable);
    }

    [Fact]
    public async Task Customer_owned_stock_keeps_owner_separate_from_company_and_custodian()
    {
        var world = new World();
        world.Reader.Record = Record(customerOwned: true);

        var item = Assert.Single((await world.Service().SearchAsync(new())).Items);

        Assert.Equal("Counterparty", item.OwnerKind);
        Assert.NotEqual(Company, item.OwnerId);
        Assert.Equal(("Company", Company), (item.CustodianKind, item.CustodianId));
    }

    [Theory]
    [InlineData("place")]
    [InlineData("blocked")]
    [InlineData("negative")]
    [InlineData("over")]
    public async Task Corrupt_allocation_components_fail_closed(string corruption)
    {
        var world = new World();
        var record = Record(allocatedKg: corruption == "over" ? 11m : 1m);
        var allocation = Assert.Single(record.ActiveAllocations);
        world.Reader.Record = record with
        {
            ActiveAllocations = [allocation with
            {
                LocationId = corruption == "place" ? Guid.NewGuid() : allocation.LocationId,
                Block = corruption == "blocked" ? "Blocked" : allocation.Block,
                RemainingKg = corruption == "negative" ? -1m : allocation.RemainingKg,
            }],
        };

        await Refused(world.Service().SearchAsync(new()), 503, InventoryStockPositionCodesV1.Unavailable);
    }

    [Fact]
    public async Task Search_is_bounded_and_trimmed_and_empty_stock_is_a_valid_empty_answer()
    {
        var world = new World();
        foreach (var query in new[]
        {
            new InventoryStockPositionQueryV1(null, 0),
            new InventoryStockPositionQueryV1(null, 101),
            new InventoryStockPositionQueryV1(" " + new string('x', 65) + " ", 25),
        })
        {
            await Refused(world.Service().SearchAsync(query), 400, InventoryStockPositionCodesV1.InvalidQuery);
        }

        await world.Service().SearchAsync(new(" " + new string('x', 64) + " ", 25));
        Assert.Equal(new string('x', 64), world.Reader.Query.Q);

        world.Reader.HasMore = true;
        var page = await world.Service().SearchAsync(new(" SYN ", 1));
        Assert.True(page.SearchWindowFull);
        Assert.Equal(Company, world.Reader.CompanyId);
        Assert.Equal(("SYN", 1), (world.Reader.Query.Q, world.Reader.Query.Limit));
        Assert.False(string.IsNullOrWhiteSpace(page.NextCursor));

        world.Reader.Record = null;
        world.Reader.HasMore = false;
        page = await world.Service().SearchAsync(new());
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Filters_and_cursor_are_normalized_bound_and_forwarded_without_changing_quantity_semantics()
    {
        var world = new World();
        var record = Record(classification: LabelClassification.SecondQuality, productVersion: "17");
        world.Reader.Record = record;
        world.Reader.HasMore = true;
        var place = Assert.Single(record.Balances).Position;
        var first = await world.Service().SearchAsync(new(
            " ROLL ", 25, null, record.Label.ProductId, place.LocationId,
            InventoryLabelClassificationV1.SecondQuality, "Company", Company));

        var item = Assert.Single(first.Items);
        Assert.Equal(("17", "SecondQuality"), (item.ProductVersion, item.Classification));
        Assert.NotNull(first.NextCursor);
        Assert.Equal((record.Label.ProductId, place.LocationId, InventoryLabelClassificationV1.SecondQuality,
                StockPartyKind.Company, Company),
            (world.Reader.Query.ProductId, world.Reader.Query.LocationId, world.Reader.Query.Classification,
                world.Reader.Query.OwnerKind, world.Reader.Query.OwnerId));

        world.Reader.HasMore = false;
        await world.Service().SearchAsync(new(
            " ROLL ", 25, first.NextCursor, record.Label.ProductId, place.LocationId,
            InventoryLabelClassificationV1.SecondQuality, "Company", Company));
        Assert.NotNull(world.Reader.Query.After);
    }

    [Fact]
    public async Task Typed_product_filters_are_applied_before_the_public_page_and_can_match_after_the_first_window()
    {
        var world = new World();
        var first = Record(productVersion: "5");
        var matching = Record(productVersion: "8");
        world.Reader.Windows.Enqueue(new([first], true,
            new(first.Label.Code, first.Label.Id)));
        world.Reader.Windows.Enqueue(new([matching], false,
            new(matching.Label.Code, matching.Label.Id)));
        world.Filter.Matches.Add(matching.Label.Id);
        var familyId = Guid.NewGuid();

        var page = await world.Service().SearchAsync(new(
            Limit: 1,
            ProductFamilyId: familyId,
            ThicknessMinMm: 1.25m,
            ThicknessMaxMm: 2.5m,
            WidthMm: 1250m));

        Assert.Equal(matching.Label.Id, Assert.Single(page.Items).LabelId);
        Assert.False(page.SearchWindowFull);
        Assert.Null(page.NextCursor);
        Assert.Equal(2, world.Reader.Reads);
        Assert.All(world.Reader.Queries, query => Assert.Equal(100, query.Limit));
        Assert.All(world.Reader.Queries, query => Assert.Equal(
            (familyId, 1.25m, 2.5m, 1250m),
            (query.ProductFamilyId, query.ThicknessMinMm, query.ThicknessMaxMm, query.WidthMm)));
        Assert.Equal(2, world.Filter.Requests.Count);
        Assert.Equal(matching.Label.Id,
            Assert.Single(world.Filter.Requests[1].Selections).LabelId);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("precision")]
    [InlineData("reversed")]
    [InlineData("exact-and-range")]
    public async Task Invalid_typed_product_filters_are_rejected_before_SQL(string invalid)
    {
        var world = new World();
        var query = invalid switch
        {
            "zero" => new InventoryStockPositionQueryV1(ThicknessMm: 0m),
            "precision" => new InventoryStockPositionQueryV1(WidthMm: 1.0001m),
            "reversed" => new InventoryStockPositionQueryV1(
                ThicknessMinMm: 2m, ThicknessMaxMm: 1m),
            _ => new InventoryStockPositionQueryV1(
                WidthMm: 1250m, WidthMinMm: 1000m),
        };

        await Refused(world.Service().SearchAsync(query), 400, InventoryStockPositionCodesV1.InvalidQuery);
        Assert.Equal(0, world.Reader.Reads);
        Assert.Empty(world.Filter.Requests);
    }

    [Theory]
    [InlineData("owner-without-id")]
    [InlineData("id-without-owner")]
    [InlineData("bad-owner")]
    [InlineData("empty-product")]
    public async Task Invalid_filters_are_rejected_before_SQL(string invalid)
    {
        var world = new World();
        var query = invalid switch
        {
            "owner-without-id" => new InventoryStockPositionQueryV1(OwnerKind: "Company"),
            "id-without-owner" => new InventoryStockPositionQueryV1(OwnerId: Company),
            "bad-owner" => new InventoryStockPositionQueryV1(OwnerKind: "company", OwnerId: Company),
            _ => new InventoryStockPositionQueryV1(ProductId: Guid.Empty),
        };

        await Refused(world.Service().SearchAsync(query), 400, InventoryStockPositionCodesV1.InvalidQuery);
        Assert.Equal(0, world.Reader.Reads);
    }

    [Fact]
    public async Task Cursor_is_bound_to_company_actor_and_every_filter()
    {
        var world = new World();
        world.Reader.HasMore = true;
        var first = await world.Service().SearchAsync(new("A", 25));
        Assert.NotNull(first.NextCursor);

        await Refused(world.Service().SearchAsync(new("B", 25, first.NextCursor)),
            400, InventoryStockPositionCodesV1.InvalidQuery);
        world.User.UserId = Guid.NewGuid().ToString();
        await Refused(world.Service().SearchAsync(new("A", 25, first.NextCursor)),
            400, InventoryStockPositionCodesV1.InvalidQuery);
    }

    [Fact]
    public async Task Missing_cursor_provider_fails_closed_without_releasing_a_partial_window()
    {
        var world = new World();
        world.Reader.HasMore = true;
        await Refused(new InventoryStockPositionService(world.Context, world.User, world.Authority, world.Reader)
            .SearchAsync(new()), 503, InventoryStockPositionCodesV1.Unavailable);
        await Refused(new InventoryStockPositionService(world.Context, world.User, world.Authority, world.Reader)
            .SearchAsync(new(Cursor: "opaque")), 503, InventoryStockPositionCodesV1.Unavailable);
    }

    [Fact]
    public void Cursor_codec_binds_company_actor_limit_and_every_filter()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero));
        using var codec = CursorCodec(clock);
        var query = CursorQuery();
        var position = new InventoryStockPositionPosition("ROLL-0001", Guid.NewGuid());
        var actor = Guid.NewGuid().ToString("D");
        var cursor = codec.Protect(Company, actor, query, position);

        Assert.Equal(position, codec.Unprotect(Company, actor, query, cursor));
        foreach (var changed in new[]
        {
            query with { Q = "OTHER" },
            query with { Limit = query.Limit + 1 },
            query with { ProductId = Guid.NewGuid() },
            query with { LocationId = Guid.NewGuid() },
            query with { Classification = InventoryLabelClassificationV1.Scrap },
            query with { OwnerKind = StockPartyKind.Counterparty },
            query with { OwnerId = Guid.NewGuid() },
            query with { ProductFamilyId = Guid.NewGuid() },
            query with { ThicknessMm = 2m },
            query with { ThicknessMinMm = 1m },
            query with { ThicknessMaxMm = 3m },
            query with { WidthMm = 1000m },
            query with { WidthMinMm = 900m },
            query with { WidthMaxMm = 1100m },
        })
        {
            AssertInvalid(() => codec.Unprotect(Company, actor, changed, cursor));
        }

        AssertInvalid(() => codec.Unprotect(Guid.NewGuid(), actor, query, cursor));
        AssertInvalid(() => codec.Unprotect(Company, Guid.NewGuid().ToString("D"), query, cursor));
    }

    [Fact]
    public void Cursor_codec_refuses_tamper_noncanonical_transport_malformed_payload_and_invalid_keys()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero));
        using var codec = CursorCodec(clock);
        var query = CursorQuery();
        var actor = Guid.NewGuid().ToString("D");
        var cursor = codec.Protect(Company, actor, query,
            new InventoryStockPositionPosition("ROLL-0001", Guid.NewGuid()));
        var tampered = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');

        foreach (var invalid in new[]
        {
            tampered,
            " " + cursor,
            cursor + " ",
            "not-a-cursor",
            "AA==.AA",
            "e30.AA",
            cursor + ".extra",
        })
        {
            AssertInvalid(() => codec.Unprotect(Company, actor, query, invalid));
        }

        Assert.Throws<InvalidOperationException>(() => new InventoryStockPositionCursorCodec(
            new InventoryStockPositionCursorRuntimeOptions("not-base64"), clock));
        Assert.Throws<InvalidOperationException>(() => new InventoryStockPositionCursorCodec(
            new InventoryStockPositionCursorRuntimeOptions(Convert.ToBase64String(new byte[31])), clock));
    }

    [Fact]
    public void Cursor_codec_refuses_expired_and_future_issued_state()
    {
        var issuedAt = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(issuedAt);
        using var codec = CursorCodec(clock);
        var query = CursorQuery();
        var actor = Guid.NewGuid().ToString("D");
        var cursor = codec.Protect(Company, actor, query,
            new InventoryStockPositionPosition("ROLL-0001", Guid.NewGuid()));

        clock.UtcNow = issuedAt.AddMinutes(30);
        AssertInvalid(() => codec.Unprotect(Company, actor, query, cursor));
        clock.UtcNow = issuedAt.AddTicks(-1);
        AssertInvalid(() => codec.Unprotect(Company, actor, query, cursor));
    }

    [Theory]
    [InlineData("labels")]
    [InlineData("balances")]
    [InlineData("scoped")]
    public async Task Both_company_wide_read_permissions_are_required_before_SQL(string denial)
    {
        var world = new World();
        world.Authority.Denied = denial == "labels"
            ? ErpPermissions.InventoryLabelsRead
            : ErpPermissions.InventoryBalancesRead;
        world.Authority.CompanyWide = denial != "scoped";

        await Refused(world.Service().SearchAsync(new()), 403, InventoryStockPositionCodesV1.Forbidden);

        Assert.Equal(0, world.Reader.Reads);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("membership")]
    [InlineData("authentication")]
    public async Task Awaited_SQL_read_cannot_release_facts_after_scope_or_authority_changes(string change)
    {
        var world = new World();
        world.Reader.During = () =>
        {
            switch (change)
            {
                case "permission": world.Authority.Denied = ErpPermissions.InventoryBalancesRead; break;
                case "company": world.Context.Set(Guid.NewGuid()); break;
                case "actor": world.User.UserId = Guid.NewGuid().ToString(); break;
                case "membership": world.User.Access = false; break;
                case "authentication": world.User.IsAuthenticated = false; break;
            }
        };

        await Refused(world.Service().SearchAsync(new()), 403, InventoryStockPositionCodesV1.Forbidden);

        Assert.Equal(1, world.Reader.Reads);
    }

    [Fact]
    public async Task Missing_or_failed_provider_is_unavailable_and_late_denial_has_precedence()
    {
        var world = new World();
        await Refused(new InventoryStockPositionService(world.Context, world.User, world.Authority, null)
            .SearchAsync(new()), 503, InventoryStockPositionCodesV1.Unavailable);

        world.Reader.Fail = true;
        await Refused(world.Service().SearchAsync(new()), 503, InventoryStockPositionCodesV1.Unavailable);

        world.Reader.During = () => world.Authority.Denied = ErpPermissions.InventoryLabelsRead;
        await Refused(world.Service().SearchAsync(new()), 403, InventoryStockPositionCodesV1.Forbidden);
    }

    [Fact]
    public async Task Unauthenticated_or_unresolved_scope_never_reads_SQL()
    {
        var world = new World();
        world.Context.Set(Guid.Empty);
        await Refused(world.Service().SearchAsync(new()), 403, InventoryStockPositionCodesV1.Forbidden);
        world.User.IsAuthenticated = false;
        await Refused(world.Service().SearchAsync(new()), 401, InventoryStockPositionCodesV1.Unauthenticated);
        Assert.Equal(0, world.Reader.Reads);
    }

    private static async Task Refused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryStockPositionException>(() => task);
        Assert.Equal((status, code), (error.StatusCode, error.Code));
    }

    private static InventoryStockPositionCursorCodec CursorCodec(TimeProvider clock) => new(
        new InventoryStockPositionCursorRuntimeOptions(
            Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray())),
        clock);

    private static InventoryStockPositionReadQuery CursorQuery() => new(
        "ROLL", 25, Guid.NewGuid(), Guid.NewGuid(), InventoryLabelClassificationV1.SecondQuality,
        StockPartyKind.Company, Company, Guid.NewGuid(), 1.5m, null, null, null, 1000m, 1500m, null);

    private static void AssertInvalid(Action action)
    {
        var error = Assert.Throws<InventoryStockPositionException>(action);
        Assert.Equal((400, InventoryStockPositionCodesV1.InvalidQuery), (error.StatusCode, error.Code));
    }

    private static InventoryStockPositionRecord Record(
        decimal freeKg = 10m,
        decimal blockedKg = 0m,
        decimal allocatedKg = 0m,
        bool customerOwned = false,
        LabelClassification? classification = null,
        string? productVersion = null,
        bool activeQualityHold = false,
        decimal? qualityHoldKg = null)
    {
        var productId = Guid.NewGuid();
        var lot = new MaterialLot(Guid.NewGuid(), Company, productId, "HEAT-1", null);
        var label = new MaterialLabel(Guid.NewGuid(), Company, "SYN-ST-01", productId, lot.Id, productVersion);
        if (classification.HasValue)
        {
            label.Classify(classification.Value);
        }
        var owner = new StockOwner(
            customerOwned ? StockPartyKind.Counterparty : StockPartyKind.Company,
            customerOwned ? Guid.NewGuid() : Company);
        var custodian = new StockCustodian(StockPartyKind.Company, Company);
        var locationId = Guid.NewGuid();
        var balances = new List<StockBalance>();
        if (freeKg > 0m)
        {
            balances.Add(new StockBalance(
                new StockPosition(Company, owner, custodian, locationId, label, BlockState.Free),
                Kilograms.Positive(freeKg)));
        }
        if (blockedKg > 0m)
        {
            balances.Add(new StockBalance(
                new StockPosition(Company, owner, custodian, locationId, label, BlockState.Blocked),
                Kilograms.Positive(blockedKg)));
        }

        var allocations = allocatedKg == 0m
            ? Array.Empty<InventoryActiveAllocationRecord>()
            : [new InventoryActiveAllocationRecord(
                owner.Kind.ToString(), owner.Id, custodian.Kind.ToString(), custodian.Id,
                locationId, BlockState.Free.ToString(), allocatedKg)];
        var holds = activeQualityHold
            ? [new InventoryActivePhysicalHoldRecord(
                InventoryStore.QualityPhysicalHoldOwner,
                InventoryPhysicalHoldTokensV1.QualityInspectionSource,
                Guid.NewGuid(),
                owner.Kind.ToString(),
                owner.Id,
                custodian.Kind.ToString(),
                custodian.Id,
                locationId,
                productId,
                qualityHoldKg ?? freeKg + blockedKg)]
            : Array.Empty<InventoryActivePhysicalHoldRecord>();
        return new InventoryStockPositionRecord(3, label, lot, balances, allocations, holds);
    }

    private sealed class World
    {
        public Context Context { get; } = new();
        public User User { get; } = new();
        public Authority Authority { get; } = new();
        public Reader Reader { get; } = new();
        public ProductFilter Filter { get; } = new();
        private readonly InventoryStockPositionCursorCodec cursors = new(
            new InventoryStockPositionCursorRuntimeOptions(Convert.ToBase64String(new byte[32])),
            TimeProvider.System);
        public InventoryStockPositionService Service() => new(Context, User, Authority, Reader, cursors, Filter);
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
        public string? Denied { get; set; }
        public bool CompanyWide { get; set; } = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(
                permission != Denied,
                CompanyWide,
                new HashSet<string>(),
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
    }

    private sealed class Reader : IInventoryStockPositionReader
    {
        public Queue<InventoryStockPositionWindow> Windows { get; } = new();
        public List<InventoryStockPositionReadQuery> Queries { get; } = [];
        public InventoryStockPositionRecord? Record { get; set; } = InventoryStockPositionTests.Record();
        public Action? During { get; set; }
        public bool Fail { get; set; }
        public bool HasMore { get; set; }
        public int Reads { get; private set; }
        public Guid CompanyId { get; private set; }
        public InventoryStockPositionReadQuery Query { get; private set; } = null!;

        public async Task<InventoryStockPositionWindow> SearchAsync(
            Guid companyId,
            InventoryStockPositionReadQuery query,
            CancellationToken cancellationToken)
        {
            Reads++;
            CompanyId = companyId;
            Query = query;
            Queries.Add(query);
            await Task.Yield();
            During?.Invoke();
            if (Fail)
            {
                throw new InvalidOperationException("Provider failure");
            }

            if (Windows.Count > 0) return Windows.Dequeue();
            var records = Record is null ? Array.Empty<InventoryStockPositionRecord>() : [Record];
            return new InventoryStockPositionWindow(records, HasMore,
                records.Length == 0 ? null : new(records[^1].Label.Code, records[^1].Label.Id));
        }
    }

    private sealed class ProductFilter : IInventoryStockProductFilterV1
    {
        public HashSet<Guid> Matches { get; } = [];
        public List<InventoryStockProductFilterRequestV1> Requests { get; } = [];

        public Task<IReadOnlySet<Guid>> MatchAsync(
            InventoryStockProductFilterRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            IReadOnlySet<Guid> answer = request.Selections.Select(selection => selection.LabelId)
                .Where(Matches.Contains).ToHashSet();
            return Task.FromResult(answer);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
