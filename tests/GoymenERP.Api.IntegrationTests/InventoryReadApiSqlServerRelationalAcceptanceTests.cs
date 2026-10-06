using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Settings.Application;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// T07(a) Inventory read API against a REAL SQL Server, through the real host: authentication,
/// company context, the permission policies and the Inventory store. The callers are the factory's
/// configured development users (the only ones the bootstrap handler accepts), linked to an ERP
/// account in this test's own database when they have none; grants are synthetic test roles on the
/// registered names, created there and never seeded (no seeded role holds them); the stock
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
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead);
        using var client = run.Factory.CreateClientFor(reader, CompanyA);

        var labelResponse = await client.GetAsync($"/api/v1/inventory/labels/{label.Id}");
        Assert.Equal(HttpStatusCode.OK, labelResponse.StatusCode);
        // The typed header re-orders its text; the directives are what matter.
        Assert.True(labelResponse.Headers.CacheControl is { Private: true, NoStore: true });
        var view = (await labelResponse.Content.ReadFromJsonAsync<InventoryLabelViewV1>())!;
        Assert.Equal((label.Id, CompanyA, "SYN-T07A-READ", Product, Lot), (view.LabelId, view.CompanyId, view.Code, view.ProductId, view.LotId));
        // The label's lot with the lot's own values (Business #66 5734060341, Q1): its heat, and an unknown
        // coil as null. The label's own heat stays its own (null here); nothing is derived from the other.
        Assert.Equal(new InventoryLotV1(Lot, "SYN-LOT-HEAT", null), view.Lot);
        Assert.Null(view.HeatNumber);
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
        var labelsOnly = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.InventoryLabelsRead);
        var balancesOnly = await run.GrantAsync(TestUsers.Unprovisioned, CompanyA, ErpPermissions.InventoryBalancesRead);
        // Company A's read-only user: access to the company, no Inventory permission (none is seeded).
        var neither = TestUsers.CompanyAViewer;

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
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead);
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ErpPermissions.InventoryLabelsRead,
            ErpPermissions.InventoryBalancesRead);

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

    [SqlServerFact]
    public async Task Parent_roll_reference_keeps_real_physical_block_document_ownership_facts_without_availability()
    {
        await using var run = await Run.StartAsync();
        var own = await run.StockedLabelAsync("SYN-R13-A", pending: true);
        var customer = await run.StockedLabelAsync("SYN-R13-B", customer: true);
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        var before = await run.EffectsAsync();
        await run.WithParentRollAsync(actor, CompanyA, async (service, _) =>
        {
            var window = await service.SearchAsync(new("SYN-R13", 1));
            Assert.True(window.SearchWindowFull);
            var item = Assert.Single(window.Items);
            Assert.Equal(own.Id, item.LabelId);
            Assert.Equal("3", item.LabelVersion);
            Assert.Equal("7", item.ProductVersion);
            Assert.Equal(("1000.125", "899.625", "100.5"),
                (item.PhysicalKg, item.UnblockedPhysicalKg, item.BlockedPhysicalKg));
            Assert.Equal("PendingDocuments", item.DocumentStatus);
            Assert.Equal(["MTC"], item.RequiredDocuments);
            Assert.Empty(item.AttachedDocuments);
            Assert.True(item.DraftSelectable);
            Assert.Null(item.DraftRefusalCode);
            Assert.True(item.ProductionUseRequiresRevalidation);
            Assert.Equal(new InventoryLotV1(Lot, "SYN-LOT-HEAT", null), item.Lot);
            Assert.Null(item.CoilNumber);
            Assert.All(item.Positions, p => Assert.Equal(("Company", CompanyA, "Company", CompanyA),
                (p.OwnerKind, p.OwnerId, p.CustodianKind, p.CustodianId)));
            var validated = await service.ValidateAsync(new(own.Id, "3"));
            Assert.Equal("7", validated.ProductVersion);
            Assert.Equal((CompanyA, own.Id, "3", true, (string?)null, true),
                (validated.CompanyId, validated.LabelId, validated.LabelVersion, validated.DraftSelectable,
                    validated.DraftRefusalCode, validated.ProductionUseRequiresRevalidation));
            var all = await service.SearchAsync(new("SYN-R13", 100));
            Assert.False(all.SearchWindowFull);
            var foreignOwner = Assert.Single(all.Items, i => i.LabelId == customer.Id);
            Assert.False(foreignOwner.DraftSelectable);
            Assert.NotEmpty(foreignOwner.Positions);
            var customerPosition = foreignOwner.Positions[0];
            Assert.Equal(("Counterparty", "Company", CompanyA),
                (customerPosition.OwnerKind, customerPosition.CustodianKind, customerPosition.CustodianId));
            Assert.NotEqual(Guid.Empty, customerPosition.OwnerId);
            Assert.All(foreignOwner.Positions, position =>
                Assert.Equal((customerPosition.OwnerKind, customerPosition.OwnerId,
                        customerPosition.CustodianKind, customerPosition.CustodianId),
                    (position.OwnerKind, position.OwnerId, position.CustodianKind, position.CustodianId)));
            await ParentRollRefused(service.ValidateAsync(new(customer.Id, "3")), 422, InventoryParentRollCodesV1.ContextRequired);
            var customerContext = new InventoryParentRollPositionContextV1(
                StockPartyKind.Counterparty, customerPosition.OwnerId,
                StockPartyKind.Company, customerPosition.CustodianId);
            IInventoryContextualParentRollReferencesV1 contextualPort = service;
            var contextual = await contextualPort.SearchAsync(new(customerContext, "SYN-R13", 100));
            var contextualCustomer = Assert.Single(contextual.Items, i => i.LabelId == customer.Id);
            Assert.True(contextualCustomer.DraftSelectable);
            Assert.Null(contextualCustomer.DraftRefusalCode);
            var contextualOwn = Assert.Single(contextual.Items, i => i.LabelId == own.Id);
            Assert.False(contextualOwn.DraftSelectable);
            Assert.Equal(InventoryParentRollCodesV1.ContextRequired, contextualOwn.DraftRefusalCode);
            var contextualSelection = new InventoryContextualParentRollSelectionV1(
                customer.Id, "3", customerContext);
            var validatedCustomer = await contextualPort.ValidateAsync(contextualSelection);
            Assert.Equal((customer.Id, true, (string?)null),
                (validatedCustomer.LabelId, validatedCustomer.DraftSelectable,
                    validatedCustomer.DraftRefusalCode));
            Assert.Equivalent(validatedCustomer, await contextualPort.ValidateAsync(contextualSelection), strict: true);
            await ParentRollRefused(contextualPort.ValidateAsync(new(customer.Id, "3",
                    customerContext with { OwnerId = Guid.NewGuid() })),
                422, InventoryParentRollCodesV1.ContextRequired);
            await ParentRollRefused(service.ValidateAsync(new(own.Id, "2")), 409, InventoryParentRollCodesV1.VersionConflict);
        });
        Assert.Equal(before, await run.EffectsAsync());
    }

    [SqlServerFact]
    public async Task Parent_roll_reference_refuses_legacy_missing_product_version_without_current_lookup_backfill()
    {
        await using var run = await Run.StartAsync();
        var label = await run.StockedLabelAsync($"SYN-R13-LEGACY-{Guid.NewGuid():N}");
        await run.ExecuteAsync("UPDATE inventory.Labels SET ProductVersion = NULL WHERE CompanyId = @c AND Id = @id",
            ("@c", CompanyA), ("@id", label.Id));
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);

        await run.WithParentRollAsync(actor, CompanyA, async (service, _) =>
            await ParentRollRefused(service.SearchAsync(new(label.Code, 10)), 503,
                InventoryParentRollCodesV1.Unavailable));
    }

    [SqlServerFact]
    public async Task Parent_roll_real_company_grants_and_post_read_revocation_do_not_disclose_foreign_or_revoked_stock()
    {
        await using var run = await Run.StartAsync();
        var label = await run.StockedLabelAsync("SYN-R13-FENCE");
        var actorA = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        var actorB = await run.GrantAsync(TestUsers.CompanyB, CompanyB,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        await run.WithParentRollAsync(actorB, CompanyB, async (service, _) =>
        {
            Assert.Empty((await service.SearchAsync(new())).Items);
            await ParentRollRefused(service.ValidateAsync(new(label.Id, "3")), 404, InventoryParentRollCodesV1.InvalidReference);
            await ParentRollRefused(service.ValidateAsync(new(Guid.NewGuid(), "3")), 404, InventoryParentRollCodesV1.InvalidReference);
        });
        var onlyLabel = await run.GrantAsync(TestUsers.NoAssignment, CompanyA, ErpPermissions.InventoryLabelsRead);
        await run.WithParentRollAsync(onlyLabel, CompanyA, async (service, _) =>
            await ParentRollRefused(service.SearchAsync(new()), 403, InventoryParentRollCodesV1.Forbidden));
        await run.WithParentRollAsync(actorA, CompanyA, async (service, reader) =>
        {
            await ParentRollRefused(service.ValidateAsync(new(label.Id, "3")), 403, InventoryParentRollCodesV1.Forbidden);
            Assert.True(reader.ObservedRealRecord);
        }, afterRead: () => run.RevokeAsync(actorA));
    }

    [SqlServerFact]
    public async Task Parent_roll_capture_rejects_a_real_label_version_change_between_physical_reads()
    {
        await using var run = await Run.StartAsync();
        var label = await run.StockedLabelAsync("SYN-R13-TORN");
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead);
        var interceptor = new AfterBalanceRead(() => run.TouchAsync(label.Id));
        await run.WithParentRollAsync(actor, CompanyA, async (service, _) =>
            await ParentRollRefused(service.ValidateAsync(new(label.Id, "3")), 409, InventoryParentRollCodesV1.ReadConflict),
            interceptor: interceptor);
        Assert.True(interceptor.Triggered);
        await run.WithParentRollAsync(actor, CompanyA, async (service, _) =>
        {
            await ParentRollRefused(service.ValidateAsync(new(label.Id, "3")), 409, InventoryParentRollCodesV1.VersionConflict);
            Assert.Equal("4", (await service.ValidateAsync(new(label.Id, "4"))).LabelVersion);
        });
    }

    private static async Task ParentRollRefused(Task task, int status, string code)
    {
        var error = await Assert.ThrowsAsync<InventoryParentRollException>(() => task);
        Assert.Equal((status, code), (error.StatusCode, error.Code));
    }

    private sealed class AfterBalanceRead(Func<Task> action) : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Triggered && command.CommandText.Contains("[inventory].[Balances]", StringComparison.Ordinal))
            {
                Triggered = true;
                await action();
            }
            return result;
        }
    }

    private sealed class AfterParentRollRead(IInventoryParentRollReader source, Func<Task>? after) : IInventoryParentRollReader
    {
        public bool ObservedRealRecord { get; private set; }
        public async Task<InventoryParentRollRecord?> ReadAsync(Guid company, Guid id, CancellationToken ct)
        {
            var result = await source.ReadAsync(company, id, ct);
            ObservedRealRecord = result is not null;
            if (after is not null) await after();
            return result;
        }
        public async Task<InventoryParentRollWindow> SearchAsync(Guid company, string q, int limit, CancellationToken ct)
        {
            var result = await source.SearchAsync(company, q, limit, ct);
            ObservedRealRecord = result.Records.Count > 0;
            if (after is not null) await after();
            return result;
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
        public async Task<MaterialLabel> StockedLabelAsync(string code, bool pending = false, bool customer = false)
        {
            var location = await ScalarAsync<Guid>(
                "SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId = @c ORDER BY Id", ("@c", CompanyA));
            var label = new MaterialLabel(Guid.NewGuid(), CompanyA, code, Product, Lot, "7",
                requiredDocuments: pending ? ["MTC"] : []);
            var owner = new StockOwner(customer ? StockPartyKind.Counterparty : StockPartyKind.Company, customer ? Guid.NewGuid() : CompanyA);
            var custodian = new StockCustodian(StockPartyKind.Company, CompanyA);
            var free = new StockPosition(CompanyA, owner, custodian, location, label, BlockState.Free);
            var blocked = free.WithBlock(BlockState.Blocked);
            var writer = Guid.NewGuid();
            await using var scope = Store(writer);
            // A first arrival: the lot and its label in one save (the lot is replayed on a second label).
            await scope.Store.SaveLotAsync(new MaterialLot(Lot, CompanyA, Product, heatNumber: "SYN-LOT-HEAT"), [label],
                Metadata(writer));
            await scope.Store.RecordMovementAsync(Movement(StockMovementKind.Receipt,
                new StockMovementLine(free, StockDirection.In, Kilograms.Positive(1000.125m))), Metadata(writer));
            await scope.Store.RecordMovementAsync(Movement(StockMovementKind.Block,
                new StockMovementLine(free, StockDirection.Out, Kilograms.Positive(100.5m)),
                new StockMovementLine(blocked, StockDirection.In, Kilograms.Positive(100.5m))), Metadata(writer));
            return label;
        }

        /// <summary>
        /// A synthetic role with exactly these permissions in one company, assigned to a configured
        /// development user (linked to a new ERP account first if it has none). Returns the header value.
        /// </summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Inventory reader", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor,
                    BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"T07A-{roleId:N}", "Synthetic Inventory read test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
        }

        public async Task WithParentRollAsync(string developmentUser, Guid company,
            Func<InventoryParentRollService, AfterParentRollRead, Task> test, Func<Task>? afterRead = null,
            IInterceptor? interceptor = null)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await identity.ExternalIdentities.SingleAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer
                && x.Subject == developmentUser);
            var user = new FixedCurrentUser(link.UserId, company);
            var context = new CompanyContext();
            context.Set(company);
            await using var inventory = Context(company, interceptor is null ? [] : [interceptor]);
            var reader = new AfterParentRollRead(new SqlInventoryParentRollReader(inventory), afterRead);
            await test(new InventoryParentRollService(context, user, new ErpAuthorizationService(identity, user), reader), reader);
        }

        public async Task RevokeAsync(string developmentUser)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var actor = await identity.ExternalIdentities.Where(x => x.Issuer == BootstrapIdentityOptions.Issuer
                && x.Subject == developmentUser).Select(x => x.UserId).SingleAsync();
            var assignments = await identity.RoleAssignments.Include(x => x.Scopes)
                .Where(x => x.UserId == actor && x.CompanyId == CompanyA).ToListAsync();
            identity.RoleAssignments.RemoveRange(assignments);
            await identity.SaveChangesAsync();
        }

        public async Task TouchAsync(Guid labelId)
        {
            var writer = Guid.NewGuid();
            await using var scope = Store(writer);
            var label = (await scope.Store.FindLabelAsync(labelId))!;
            label.Print(At.AddMinutes(1), writer.ToString());
            await scope.Store.SaveLabelAsync(label, Metadata(writer));
        }

        public Task<string> EffectsAsync() => ScalarAsync<string>(
            "SELECT CONCAT((SELECT COUNT(*) FROM inventory.Movements), ':', (SELECT COUNT(*) FROM audit.AuditEvents), ':', (SELECT SUM(Version) FROM inventory.Labels))");

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

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
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
