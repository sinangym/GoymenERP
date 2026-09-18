using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// T07(a) Inventory read API against a REAL SQL Server, through the real host: authentication,
/// company context, the permission policies and the Inventory store. Grants are synthetic test
/// roles on the registered names, created in this test's own database and never seeded; the stock
/// read is written through the store with synthetic identities (there is no write endpoint yet).
/// A positive answer here is API evidence only, not a user outcome: no real product card exists.
/// </summary>
public sealed class InventoryReadApiSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Product = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a91");
    private static readonly Guid Lot = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5a92");
    private static readonly DateTimeOffset At = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task A_reader_with_both_grants_sees_the_label_and_its_stock_position_by_position()
    {
        await using var run = await Run.StartAsync();
        var label = await run.StockedLabelAsync("SYN-T07A-READ");
        var reader = await run.GrantAsync(CompanyA, ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        using var client = run.Factory.CreateClientFor(reader, CompanyA);

        var labelResponse = await client.GetAsync($"/api/v1/inventory/labels/{label.Id}");
        Assert.Equal(HttpStatusCode.OK, labelResponse.StatusCode);
        Assert.Equal("private, no-store", labelResponse.Headers.CacheControl?.ToString());
        var view = (await labelResponse.Content.ReadFromJsonAsync<InventoryLabelViewV1>())!;
        Assert.Equal((label.Id, CompanyA, "SYN-T07A-READ", Product, Lot), (view.LabelId, view.CompanyId, view.Code, view.ProductId, view.LotId));
        Assert.Equal(nameof(LabelDocumentStatus.Complete), view.DocumentStatus);

        var balancesResponse = await client.GetAsync($"/api/v1/inventory/labels/{label.Id}/balances");
        Assert.Equal(HttpStatusCode.OK, balancesResponse.StatusCode);
        var balances = (await balancesResponse.Content.ReadFromJsonAsync<InventoryLabelBalancesV1>())!;
        // 1000.125 received, 100.5 then blocked: the text is exact, the totals are the positions'.
        Assert.Equal(("1000.125", "899.625", "100.5"), (balances.PhysicalKg, balances.FreeKg, balances.BlockedKg));
        Assert.Equal(["Free", "Blocked"], balances.Positions.Select(position => position.Block));
        Assert.All(balances.Positions, position => Assert.Equal(("Company", CompanyA), (position.OwnerKind, position.OwnerId)));
    }

    [SqlServerFact]
    public async Task Each_read_needs_its_own_grant()
    {
        await using var run = await Run.StartAsync();
        var label = await run.StockedLabelAsync("SYN-T07A-GRANT");
        var labelsOnly = await run.GrantAsync(CompanyA, ErpPermissions.InventoryLabelsRead);
        var balancesOnly = await run.GrantAsync(CompanyA, ErpPermissions.InventoryBalancesRead);
        var neither = await run.GrantAsync(CompanyA, ErpPermissions.OrganizationContextRead);

        using (var client = run.Factory.CreateClientFor(labelsOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/inventory/labels/{label.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/inventory/labels/{label.Id}/balances")).StatusCode);
        }

        using (var client = run.Factory.CreateClientFor(balancesOnly, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/inventory/labels/{label.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/inventory/labels/{label.Id}/balances")).StatusCode);
        }

        using (var client = run.Factory.CreateClientFor(neither, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/inventory/labels/{label.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/inventory/labels/{label.Id}/balances")).StatusCode);
        }
    }

    [SqlServerFact]
    public async Task Another_companys_label_is_not_found_and_the_company_context_is_required()
    {
        await using var run = await Run.StartAsync();
        var labelOfA = await run.StockedLabelAsync("SYN-T07A-TENANT");
        var readerOfB = await run.GrantAsync(CompanyB, ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        var readerOfA = await run.GrantAsync(CompanyA, ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);

        using (var client = run.Factory.CreateClientFor(readerOfB, CompanyB))
        {
            // Company A's label does not exist for company B: the same 404 as an unknown id.
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/inventory/labels/{labelOfA.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/inventory/labels/{labelOfA.Id}/balances")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/inventory/labels/{Guid.NewGuid()}")).StatusCode);
        }

        // B's reader naming company A is refused before any read: no access to that company.
        using (var client = run.Factory.CreateClientFor(readerOfB, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/inventory/labels/{labelOfA.Id}")).StatusCode);
        }

        // Without a company context the read does not run at all.
        using (var client = run.Factory.CreateClient())
        {
            client.DefaultRequestHeaders.Add("X-Development-User", readerOfA);
            client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/inventory/labels/{labelOfA.Id}")).StatusCode);
        }
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        private string Correlation { get; } = $"t07a-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // Idempotent if the host already applied Inventory's migrations.
            await using var context = run.Context(CompanyA);
            await context.Database.MigrateAsync();
            return run;
        }

        /// <summary>A synthetic label of company A with 1000.125 kg received and 100.5 kg blocked, through the store.</summary>
        public async Task<MaterialLabel> StockedLabelAsync(string code)
        {
            var location = await ScalarAsync<Guid>(
                "SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId = @c ORDER BY Id", ("@c", CompanyA));
            var label = new MaterialLabel(Guid.NewGuid(), CompanyA, code, Product, Lot);
            var owner = new StockOwner(StockPartyKind.Company, CompanyA);
            var custodian = new StockCustodian(StockPartyKind.Company, CompanyA);
            var free = new StockPosition(CompanyA, owner, custodian, location, label, BlockState.Free);
            var blocked = free.WithBlock(BlockState.Blocked);
            var writer = Guid.NewGuid();
            await using var scope = Store(writer);
            await scope.Store.SaveLabelAsync(label, Metadata(writer));
            await scope.Store.RecordMovementAsync(Movement(StockMovementKind.Receipt,
                new StockMovementLine(free, StockDirection.In, Kilograms.Positive(1000.125m))), Metadata(writer));
            await scope.Store.RecordMovementAsync(Movement(StockMovementKind.Block,
                new StockMovementLine(free, StockDirection.Out, Kilograms.Positive(100.5m)),
                new StockMovementLine(blocked, StockDirection.In, Kilograms.Positive(100.5m))), Metadata(writer));
            return label;
        }

        /// <summary>A new synthetic user holding a synthetic role with exactly these permissions in one company.</summary>
        public async Task<string> GrantAsync(Guid company, params string[] permissions)
        {
            var actor = Guid.NewGuid();
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"T07A-{roleId:N}", "Synthetic Inventory read test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Users.Add(new UserAccount(actor, "Synthetic Inventory reader", null));
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return actor.ToString("D");
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private InventoryDbContext Context(Guid company, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var options = new DbContextOptionsBuilder<InventoryDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            return new InventoryDbContext(options, companyContext);
        }

        private StoreScope Store(Guid writer)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            var collector = new AuditEventCollector();
            var dbContext = Context(CompanyA, new AuditSaveChangesInterceptor(collector, companyContext));
            var settings = new ApprovedSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(writer, CompanyA));
            return new StoreScope(dbContext, new InventoryStore(dbContext, companyContext, collector,
                new InventoryKilogramScale(settings), TimeProvider.System));
        }

        private InventoryCommandMetadata Metadata(Guid writer) => new(
            writer.ToString("D"), "synthetic.inventory.test-write", ["SYNTHETIC-INVENTORY-TEST"], Correlation,
            CausationId: null, IdempotencyKey: Guid.NewGuid().ToString("N"), SourceChannel: "Web");

        private static StockMovement Movement(StockMovementKind kind, params StockMovementLine[] lines) =>
            new(Guid.NewGuid(), CompanyA, kind, At, new StockSourceReference("synthetic.source", Guid.NewGuid()), lines);

        private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }
    }

    private sealed class StoreScope(InventoryDbContext dbContext, InventoryStore store) : IAsyncDisposable
    {
        public InventoryStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class FixedCurrentUser(Guid userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
