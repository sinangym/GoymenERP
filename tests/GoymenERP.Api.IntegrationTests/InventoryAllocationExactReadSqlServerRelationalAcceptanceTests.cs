using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.LogisticsRuntime;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// R03.3 source only. SQL/unit/HTTP execution, OpenAPI/inventory admission and K3 review are NOT_RUN.
// Reuses the existing approved disposable SQL Server harness; no new runner, environment or fixture is activated.
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Exact_allocation_read_returns_position_label_version_release_facts_and_no_write_effects()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-EXACT-FACTS", 100m);
        var id = Guid.NewGuid();
        InventoryPhysicalAllocationResultV1 created;
        await using (var write = run.Store(CompanyA))
        {
            created = await write.Store.CreateAsync(ReadRequest(id, Guid.NewGuid(), Guid.NewGuid(), source, "10"), Metadata(run));
        }

        await using var db = run.Context(CompanyA);
        var reads = PhysicalAllocationReads(db, CompanyA);
        var before = await AllocationEffectCountsAsync(run);
        var actual = await reads.ReadAsync(id);
        var row = await db.Allocations.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(created.Allocation, actual.Allocation);
        Assert.Equal((((StockPartyKind)row.OwnerKind).ToString(), row.OwnerId, ((StockPartyKind)row.CustodianKind).ToString(),
                row.CustodianId, row.LocationId),
            (actual.OwnerKind, actual.OwnerId, actual.CustodianKind, actual.CustodianId, actual.LocationId));
        Assert.Equal((await LabelVersionAsync(run, source.LabelId)).ToString(), actual.LabelVersion);
        Assert.Null(actual.ReleasedAtUtc);
        Assert.Null(actual.ReleaseReason);
        Assert.Equal(before, await AllocationEffectCountsAsync(run));

        await using (var write = run.Store(CompanyA))
        {
            await write.Store.ReleaseAsync(id, new(created.Allocation.Version, "SENTETIK exact release"), Metadata(run));
        }

        var afterRelease = await AllocationEffectCountsAsync(run);
        var released = await reads.ReadAsync(id);
        Assert.Equal(("Released", "0", "SENTETIK exact release"),
            (released.Allocation.Status, released.Allocation.RemainingKg, released.ReleaseReason));
        Assert.NotNull(released.ReleasedAtUtc);
        Assert.Equal((await LabelVersionAsync(run, source.LabelId)).ToString(), released.LabelVersion);
        Assert.Equal(afterRelease, await AllocationEffectCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Exact_read_and_logistics_find_an_allocation_outside_the_unchanged_100_item_window()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-EXACT-101", 200m);
        var order = Guid.NewGuid();
        var line = Guid.NewGuid();
        var ids = new HashSet<Guid>();
        for (var index = 0; index < 101; index++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            var current = source with { LabelVersion = await LabelVersionAsync(run, source.LabelId) };
            await using var write = run.Store(CompanyA);
            await write.Store.CreateAsync(ReadRequest(id, order, line, current, "1"), Metadata(run));
        }

        await using var db = run.Context(CompanyA);
        var reads = PhysicalAllocationReads(db, CompanyA);
        var page = await reads.SearchAsync(new(order, line));
        Assert.Equal(100, page.Items.Count);
        Assert.True(page.SearchWindowFull);
        // Pick the actually excluded ID; do not assume a timestamp/GUID ordering in the fixture.
        var excluded = Assert.Single(ids.Except(page.Items.Select(item => item.Allocation.AllocationId)));
        var before = await AllocationEffectCountsAsync(run);
        Assert.Equal(excluded, (await reads.ReadAsync(excluded)).Allocation.AllocationId);

        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var logistics = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options, company);
        using var services = new ServiceCollection().AddSingleton<IInventoryPhysicalAllocationExactReadsV1>(reads).BuildServiceProvider();
        var runner = new LogisticsRealizationTransactionRunner(logistics, services, TimeProvider.System);
        var found = await runner.ExecuteShipmentAsync(CompanyA,
            (stock, ct) => stock.FindAllocationAsync(order, line, excluded, ct), _ => Task.CompletedTask);
        Assert.NotNull(found);
        Assert.Equal(excluded, found.AllocationId);
        var wrongLine = await runner.ExecuteShipmentAsync(CompanyA,
            (stock, ct) => stock.FindAllocationAsync(order, Guid.NewGuid(), excluded, ct), _ => Task.CompletedTask);
        Assert.Null(wrongLine);
        var missing = await runner.ExecuteShipmentAsync(CompanyA,
            (stock, ct) => stock.FindAllocationAsync(order, line, Guid.NewGuid(), ct), _ => Task.CompletedTask);
        Assert.Null(missing);
        Assert.Equal(before, await AllocationEffectCountsAsync(run));
        Assert.Equal(100, (await reads.SearchAsync(new(order, line))).Items.Count);
    }

    [SqlServerFact]
    public async Task Exact_HTTP_read_reuses_permission_hides_foreign_unknown_ids_and_disables_caching()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-EXACT-HTTP", 100m);
        var id = Guid.NewGuid();
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.CreateAsync(ReadRequest(id, Guid.NewGuid(), Guid.NewGuid(), source, "10"), Metadata(run));
        }
        var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA, ErpPermissions.InventoryPhysicalAllocationWrite);
        using var client = ClientFor(run.Factory, actor, CompanyA);
        var before = await AllocationEffectCountsAsync(run);
        using (var response = await client.GetAsync($"/api/v1/inventory/physical-allocations/{id:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.Headers.CacheControl);
            Assert.True(response.Headers.CacheControl.NoStore);
            Assert.True(response.Headers.CacheControl.Private);
            var value = await response.Content.ReadFromJsonAsync<InventoryPhysicalAllocationExactReadV1>();
            Assert.NotNull(value);
            Assert.Equal((id, CompanyA), (value.Allocation.AllocationId, value.Allocation.CompanyId));
        }
        using (var deniedClient = ClientFor(run.Factory, TestUsers.CompanyAViewer, CompanyA))
        using (var denied = await deniedClient.GetAsync($"/api/v1/inventory/physical-allocations/{id:D}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var missing = await client.GetAsync($"/api/v1/inventory/physical-allocations/{Guid.NewGuid():D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var body = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.TryGetProperty("code", out _));
            Assert.False(body.RootElement.TryGetProperty("allocation", out _));
        }
        await using var foreign = run.Context(CompanyB);
        var hidden = await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() =>
            PhysicalAllocationReads(foreign, CompanyB).ReadAsync(id));
        Assert.Equal((404, InventoryPhysicalAllocationReadCodesV1.NotFound), (hidden.StatusCode, hidden.Code));
        Assert.Equal(before, await AllocationEffectCountsAsync(run));
    }

    [SqlServerFact]
    public async Task Exact_SQL_capture_refuses_allocation_or_label_changes_and_corrupt_product_facts()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-EXACT-TORN", 100m);
        var id = Guid.NewGuid();
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.CreateAsync(ReadRequest(id, Guid.NewGuid(), Guid.NewGuid(), source, "10"), Metadata(run));
        }
        foreach (var sql in new[]
        {
            "UPDATE inventory.Allocations SET Version = Version + 1 WHERE CompanyId = @company AND Id = @id",
            "UPDATE inventory.Labels SET Version = Version + 1 WHERE CompanyId = @company AND Id = @label",
        })
        {
            var interceptor = new ExactAfterCapture(sql.Contains("inventory.Labels", StringComparison.Ordinal)
                    ? "[inventory].[Labels]" : "[inventory].[Allocations]", () => ExecuteAsync(run, sql,
                ("@company", CompanyA), ("@id", id), ("@label", source.LabelId)));
            await using var db = run.Context(CompanyA, interceptor);
            var error = await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() =>
                PhysicalAllocationReads(db, CompanyA).ReadAsync(id));
            Assert.Equal((409, InventoryPhysicalAllocationReadCodesV1.ReadConflict), (error.StatusCode, error.Code));
            Assert.True(interceptor.Triggered);
        }
        await ExecuteAsync(run, "UPDATE inventory.Allocations SET DemandProductId = @product WHERE CompanyId = @company AND Id = @id",
            ("@product", Guid.NewGuid()), ("@company", CompanyA), ("@id", id));
        await using var corrupt = run.Context(CompanyA);
        var unavailable = await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() =>
            PhysicalAllocationReads(corrupt, CompanyA).ReadAsync(id));
        Assert.Equal(503, unavailable.StatusCode);
    }

    [Fact]
    public async Task Exact_service_rechecks_actor_company_and_permission_after_capture_including_failures()
    {
        foreach (var change in new[] { "actor", "company", "permission", "failed-read" })
        {
            var company = new ExactReadCompany(CompanyA);
            var user = new ExactReadUser(CompanyA);
            var authority = new ExactReadAuthority();
            var source = new ExactReadSource(() =>
            {
                if (change == "actor") user.Actor = "different-synthetic-actor";
                if (change == "company") company.Set(CompanyB);
                if (change is "permission" or "failed-read") authority.Granted = false;
                if (change == "failed-read") throw new InventoryPhysicalAllocationReadException(409, InventoryPhysicalAllocationReadCodesV1.ReadConflict);
                return ExactFixture();
            });
            var reads = new InventoryPhysicalAllocationReadService(company, user, authority, source);
            var error = await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => reads.ReadAsync(ExactFixtureId));
            Assert.Equal(403, error.StatusCode);
            Assert.Equal(1, source.Calls);
        }
    }

    [SqlServerFact]
    public async Task Exact_SQL_capture_is_discarded_when_the_authority_source_denies_after_capture()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-EXACT-REVOKED", 100m);
        var id = Guid.NewGuid();
        await using (var write = run.Store(CompanyA))
        {
            await write.Store.CreateAsync(ReadRequest(id, Guid.NewGuid(), Guid.NewGuid(), source, "10"), Metadata(run));
        }
        var company = new CompanyContext(); company.Set(CompanyA);
        var authority = new ExactReadAuthority();
        var capture = new ExactAfterCapture("[inventory].[Allocations]", () =>
        {
            authority.Granted = false;
            return Task.CompletedTask;
        });
        await using var db = run.Context(CompanyA, capture);
        var reads = new InventoryPhysicalAllocationReadService(company, new ExactReadUser(CompanyA), authority,
            new SqlInventoryPhysicalAllocationReader(db));
        var before = await AllocationEffectCountsAsync(run);
        var denied = await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => reads.ReadAsync(id));
        Assert.Equal((403, InventoryPhysicalAllocationReadCodesV1.Forbidden), (denied.StatusCode, denied.Code));
        Assert.True(capture.Triggered);
        Assert.Equal(before, await AllocationEffectCountsAsync(run));
        // Authority callback is synthetic. This proves SQL-capture/fence composition,
        // not a persisted Identity role/grant revocation journey or its execution.
    }

    [Fact]
    public async Task Exact_service_preserves_closed_401_403_404_409_503_and_cancellation_channels()
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        var user = new ExactReadUser(CompanyA);
        var authority = new ExactReadAuthority();
        var source = new ExactReadSource(() => ExactFixture());
        var reads = new InventoryPhysicalAllocationReadService(company, user, authority, source);
        user.Authenticated = false;
        Assert.Equal(401, (await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => reads.ReadAsync(ExactFixtureId))).StatusCode);
        user.Authenticated = true; authority.CompanyWide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => reads.ReadAsync(ExactFixtureId))).StatusCode);
        Assert.Equal(0, source.Calls);
        authority.CompanyWide = true;
        Assert.Equal(400, (await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => reads.ReadAsync(Guid.Empty))).StatusCode);
        foreach (var status in new[] { 404, 409, 503 })
        {
            var failing = new ExactReadSource(() => status == 404 ? null
                : throw new InventoryPhysicalAllocationReadException(status,
                    status == 409 ? InventoryPhysicalAllocationReadCodesV1.ReadConflict : InventoryPhysicalAllocationReadCodesV1.Unavailable));
            var service = new InventoryPhysicalAllocationReadService(company, user, authority, failing);
            Assert.Equal(status, (await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => service.ReadAsync(ExactFixtureId))).StatusCode);
        }
        var down = new InventoryPhysicalAllocationReadService(company, user, authority, null);
        Assert.Equal(503, (await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => down.ReadAsync(ExactFixtureId))).StatusCode);
        var legacy = new InventoryPhysicalAllocationReadService(company, user, authority, new BoundedOnlyExactReadSource());
        Assert.Equal(503, (await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => legacy.ReadAsync(ExactFixtureId))).StatusCode);
        Assert.Empty((await legacy.SearchAsync(new(Guid.NewGuid(), Guid.NewGuid()))).Items);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reads.ReadAsync(ExactFixtureId, cancellation.Token));
    }

    [Fact]
    public async Task Exact_service_rejects_wrong_identity_invalid_versions_and_fabricated_position_or_release_facts()
    {
        var valid = ExactFixture();
        foreach (var value in new[]
        {
            valid with { Allocation = valid.Allocation with { AllocationId = Guid.NewGuid() } },
            valid with { Allocation = valid.Allocation with { CompanyId = CompanyB } },
            valid with { Allocation = valid.Allocation with { DemandId = Guid.Empty } },
            valid with { Allocation = valid.Allocation with { RemainingKg = "999" } },
            valid with { LabelVersion = "0" }, valid with { OwnerKind = "1" }, valid with { OwnerId = Guid.Empty },
            valid with { CustodianKind = "Unknown" }, valid with { LocationId = Guid.Empty },
            valid with { ReleaseReason = "fabricated" },
        })
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var reads = new InventoryPhysicalAllocationReadService(company, new ExactReadUser(CompanyA),
                new ExactReadAuthority(), new ExactReadSource(() => value));
            Assert.Equal(503, (await Assert.ThrowsAsync<InventoryPhysicalAllocationReadException>(() => reads.ReadAsync(ExactFixtureId))).StatusCode);
        }
    }

    private static readonly Guid ExactFixtureId = Guid.Parse("e3a10000-0000-4000-8000-000000000001");
    private static InventoryPhysicalAllocationExactReadV1 ExactFixture()
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        return new(new(ExactFixtureId, CompanyA, "1", "Active", Guid.NewGuid(), Product,
            InventoryAllocationDemandKindsV1.SalesOrderLine, Guid.NewGuid(), Guid.NewGuid(), "7", "10", "10",
            "SENTETIK exact source", null, at, at), "1", "Company", CompanyA, "Company", CompanyA, Guid.NewGuid(), null, null);
    }

    private sealed class ExactReadUser(Guid company) : ICurrentUser
    {
        public bool Authenticated { get; set; } = true;
        public string Actor { get; set; } = "synthetic-exact-reader";
        public bool IsAuthenticated => Authenticated;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }

    private sealed class ExactReadCompany(Guid company) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class ExactReadAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public bool CompanyWide { get; set; } = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(ErpPermissions.InventoryPhysicalAllocationWrite, permission);
            return Task.FromResult(new PermissionScope(Granted, CompanyWide, new HashSet<string>(), new HashSet<Guid>(),
                new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class ExactReadSource(Func<InventoryPhysicalAllocationExactReadV1?> read)
        : IInventoryPhysicalAllocationReader, IInventoryPhysicalAllocationExactReader
    {
        public int Calls { get; private set; }
        public Task<InventoryPhysicalAllocationExactReadV1?> ReadExactAsync(Guid companyId, Guid allocationId, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(CompanyA, companyId); Assert.Equal(ExactFixtureId, allocationId);
            return Task.FromResult(read());
        }
        public Task<InventoryPhysicalAllocationReadWindow> SearchAsync(Guid companyId, Guid salesOrderId, Guid salesOrderLineId,
            int limit, CancellationToken cancellationToken) => throw new InvalidOperationException("Exact lookup must not scan a list.");
        public Task<InventoryLabelPhysicalAllocationsV1?> ReadLabelAsync(Guid companyId, Guid labelId, int limit,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Exact lookup must not scan a label.");
    }

    private sealed class BoundedOnlyExactReadSource : IInventoryPhysicalAllocationReader
    {
        public Task<InventoryPhysicalAllocationReadWindow> SearchAsync(Guid companyId, Guid salesOrderId, Guid salesOrderLineId,
            int limit, CancellationToken cancellationToken) => Task.FromResult(new InventoryPhysicalAllocationReadWindow([], false));
        public Task<InventoryLabelPhysicalAllocationsV1?> ReadLabelAsync(Guid companyId, Guid labelId, int limit,
            CancellationToken cancellationToken) => Task.FromResult<InventoryLabelPhysicalAllocationsV1?>(null);
    }

    private sealed class ExactAfterCapture(string tableToken, Func<Task> action) : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Triggered && command.CommandText.Contains(tableToken, StringComparison.Ordinal))
            {
                Triggered = true;
                await action();
            }
            return result;
        }
    }
}
