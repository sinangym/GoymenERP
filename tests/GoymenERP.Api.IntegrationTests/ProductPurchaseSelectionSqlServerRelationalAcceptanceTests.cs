using System.Text.Json;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Real SQL Product + released synthetic family + fresh SQL Identity authority + real Settings.
/// Only isolated test data is changed. No real family release or purchase authoring acceptance is claimed.</summary>
public sealed class ProductPurchaseSelectionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ProductId = ProductSalesCandidateFixture.ProductId;
    private static readonly Guid UnitId = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly DateOnly On = new(2026, 9, 23);
    private static ProductPurchaseSelectionV1 Selection => new(ProductId, "2", UnitId, On);

    [SqlServerFact]
    public async Task Real_purchase_record_family_profile_and_bounded_candidates_are_read_without_writes()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Request();
        var service = scope.ServiceProvider.GetRequiredService<IProductPurchaseEligibilityV1>();
        var answer = await service.EvaluateAsync(Selection, default);
        Assert.Empty(answer.Errors);
        Assert.Equal((ProductId, "2", UnitId, "kg", "1"), (answer.Snapshot!.ProductId, answer.Snapshot.ProductVersion,
            answer.Snapshot.PurchaseUnit.UnitId, answer.Snapshot.PurchaseUnit.Code, answer.Snapshot.PurchaseUnit.ProfileVersion));
        var candidates = scope.ServiceProvider.GetRequiredService<IProductPurchaseCandidatesV1>();
        var result = await candidates.SearchAsync(new("SYNS04", On, 1), default);
        Assert.Equal(ProductId, Assert.Single(result.Items).ProductId);
        Assert.False(result.SearchWindowFull);
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        Assert.True(db.Database.IsSqlServer());
        var stored = (await db.Products.AsNoTracking().SingleAsync()).ToRecord();
        Assert.Equal(2, stored.Version);
        Assert.True(stored.Purchasable.Value);
        Assert.False(stored.Sellable.Value); // A real purchase-only row; not Sales E1 under an alias.
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty((await candidates.SearchAsync(new("does-not-match", On), default)).Items);
        // More real rows than the examined window: ordering is stable and only purchase roles qualify.
        foreach (var purchasable in new[] { true, false })
        {
            var id = Guid.NewGuid();
            var code = purchasable ? "SYNS04-2" : "SYNS04-3";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [product].[Products]
                ([Id],[Code],[Name],[GroupId],[ClassId],[FamilyId],[PurchasableValue],[PurchasableProvenance],
                 [SellableValue],[SellableProvenance],[ProductionInputValue],[ProductionInputProvenance],
                 [ProductionOutputValue],[ProductionOutputProvenance],[Lifecycle],[Version],
                 [CreatedAtUtc],[UpdatedAtUtc],[ReferencesJson])
                SELECT {id},{code},[Name],[GroupId],[ClassId],[FamilyId],{purchasable},[PurchasableProvenance],
                 [SellableValue],[SellableProvenance],[ProductionInputValue],[ProductionInputProvenance],
                 [ProductionOutputValue],[ProductionOutputProvenance],[Lifecycle],[Version],
                 [CreatedAtUtc],[UpdatedAtUtc],[ReferencesJson]
                FROM [product].[Products] WHERE [Id]={ProductId}
                """);
        }
        var bounded = await candidates.SearchAsync(new("SYNS04", On, 1), default);
        Assert.Equal(ProductId, Assert.Single(bounded.Items).ProductId);
        Assert.True(bounded.SearchWindowFull);
        var full = await candidates.SearchAsync(new("SYNS04", On, 10), default);
        Assert.Equal(new[] { "SYNS04-1", "SYNS04-2" }, full.Items.Select(x => x.Code));
        Assert.False(full.SearchWindowFull);
    }

    [SqlServerFact]
    public async Task Real_stored_role_lifecycle_version_units_and_family_refuse_invalid_purchase_selections()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Request();
        var service = scope.ServiceProvider.GetRequiredService<IProductPurchaseEligibilityV1>();
        var candidates = scope.ServiceProvider.GetRequiredService<IProductPurchaseCandidatesV1>();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        async Task Refused(ProductPurchaseSelectionV1 selection, string field, string code)
        {
            var answer = await service.EvaluateAsync(selection, default);
            Assert.Null(answer.Snapshot);
            Assert.Contains(new ProductFieldError(field, code), answer.Errors);
        }
        await Refused(Selection with { ExpectedProductVersion = "1" }, "expectedProductVersion", "version_conflict");
        await Refused(Selection with { UnitId = Guid.NewGuid() }, "unitId", "unsupported_purchase_unit");
        await Refused(Selection with { EffectiveOn = new(2026, 9, 17) }, "references.units.purchase", "invalid_reference");
        await db.Products.ExecuteUpdateAsync(u => u.SetProperty(p => p.PurchasableValue, false));
        await Refused(Selection, "productId", "not_purchasable");
        Assert.Empty((await candidates.SearchAsync(new(null, On), default)).Items);
        await db.Products.ExecuteUpdateAsync(u => u.SetProperty(p => p.PurchasableValue, true).SetProperty(p => p.Lifecycle, 0));
        await Refused(Selection, "productId", "lifecycle_conflict");
        Assert.Empty((await candidates.SearchAsync(new(null, On), default)).Items);
        await db.Products.ExecuteUpdateAsync(u => u.SetProperty(p => p.Lifecycle, 1));
        var original = (await db.Products.AsNoTracking().SingleAsync()).ToRecord().RestoreReferences();
        var missing = JsonSerializer.Serialize(original with { Units = original.Units! with { Purchase = null } }, JsonOptions);
        await db.Products.ExecuteUpdateAsync(u => u.SetProperty(p => p.ReferencesJson, missing));
        await Refused(Selection, "references.units.purchase", "invalid_reference");
        Assert.Empty((await candidates.SearchAsync(new(null, On), default)).Items);
        var invalidFamily = JsonSerializer.Serialize(original with { TemplateVersion = "99" }, JsonOptions);
        await db.Products.ExecuteUpdateAsync(u => u.SetProperty(p => p.ReferencesJson, invalidFamily));
        var family = await service.EvaluateAsync(Selection, default);
        Assert.Null(family.Snapshot);
        Assert.Contains(family.Errors, e => e.Field == "references.templateId");
        Assert.Empty((await candidates.SearchAsync(new(null, On), default)).Items);
    }

    [SqlServerFact]
    public async Task Real_company_grant_is_not_borrowed_and_late_revocation_discards_actual_SQL_read()
    {
        await using var run = await Run.StartAsync();
        await using (var other = run.Request(TestCompanyIds.CompanyB))
        {
            var error = await Assert.ThrowsAsync<ProductOperationException>(() => other.ServiceProvider
                .GetRequiredService<IProductPurchaseCandidatesV1>().SearchAsync(new(null, On), default));
            Assert.Equal(403, error.StatusCode);
        }
        await using var request = run.Request();
        var p = request.ServiceProvider;
        var reader = new AfterRead(p.GetRequiredService<IProductPurchaseRecordReader>(), run.RevokeAsync);
        var service = new ProductPurchaseEligibilityService(p.GetRequiredService<ICompanyContext>(), run.User,
            p.GetRequiredService<IErpAuthorityRevalidationService>(), reader,
            p.GetRequiredService<IProductFamilyRequirementPolicyV1>(), p.GetRequiredService<ProductUnitProfileAdapter>());
        var denied = await Assert.ThrowsAsync<ProductOperationException>(() => service.EvaluateAsync(Selection, default));
        Assert.Equal((403, "product_access_denied"), (denied.StatusCode, denied.Code));
        Assert.True(reader.ObservedRealRecord);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductOperationException>(() => p.GetRequiredService<IProductPurchaseEligibilityV1>()
            .EvaluateAsync(Selection, default))).StatusCode);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed class AfterRead(IProductPurchaseRecordReader reader, Func<Task> after) : IProductPurchaseRecordReader
    {
        public bool ObservedRealRecord { get; private set; }
        public async Task<ProductStoredRecord?> ReadAsync(ProductPurchaseReadScope scope, Guid id, CancellationToken ct)
        {
            var result = await reader.ReadAsync(scope, id, ct);
            ObservedRealRecord = result?.Id == ProductId;
            await after();
            return result;
        }
    }
    private sealed class User(Guid id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => id.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
    private sealed class Run(SqlServerRelationalApiFactory factory, User user, Guid actor) : IAsyncDisposable
    {
        public User User => user;
        public static async Task<Run> StartAsync()
        {
            var actor = Guid.NewGuid();
            var user = new User(actor);
            var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(connection, services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(user);
                services.RemoveAll<ProductRuntimeOptions>();
                services.AddSingleton(new ProductRuntimeOptions(connection,
                    new ProductPilotIssuerBinding(actor, Guid.NewGuid()), new byte[32]));
                services.AddProductTechnicalCardFixtureStore();
                services.AddProductSalesCandidateFixtureStore();
                services.AddProductPurchaseSelection(p => p.GetRequiredService<ProductRuntimeOptions>().IsEnabled);
            });
            var run = new Run(factory, user, actor);
            try
            {
                using var client = factory.CreateClient();
                using var health = await client.GetAsync("/health/live");
                health.EnsureSuccessStatusCode();
                await using (var setup = run.Request())
                    await setup.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>().EnsureAsync(default);
                await using (var setup = run.Request())
                    await setup.ServiceProvider.GetRequiredService<IProductSalesCandidateFixtureStore>().EnsureAsync(default);
                await using (var setup = run.Request())
                {
                    var db = setup.ServiceProvider.GetRequiredService<ProductDbContext>();
                    var references = (await db.Products.AsNoTracking().SingleAsync()).ToRecord().RestoreReferences();
                    var purchase = JsonSerializer.Serialize(references with { Units = references.Units! with { Purchase = UnitId } }, JsonOptions);
                    // Test-only SQL row shaping; production save/activation policy is not changed.
                    await db.Products.ExecuteUpdateAsync(u => u.SetProperty(p => p.PurchasableValue, true)
                        .SetProperty(p => p.PurchasableProvenance, 1).SetProperty(p => p.SellableValue, false)
                        .SetProperty(p => p.ReferencesJson, purchase));
                    var identity = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();
                    var roleId = Guid.NewGuid();
                    var role = new RoleDefinition(roleId, $"PURCHASE-{roleId:N}", "Synthetic purchase SQL selection", true);
                    role.AddPermission(ProductPermissionManifestV1.OperationalSelectionRead);
                    var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, TestCompanyIds.CompanyA,
                        DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
                    assignment.AddScope(AccessScopeType.Company, TestCompanyIds.CompanyA);
                    identity.Users.Add(new UserAccount(actor, "Synthetic purchase SQL actor", null));
                    identity.Roles.Add(role);
                    identity.RoleAssignments.Add(assignment);
                    await identity.SaveChangesAsync();
                }
                return run;
            }
            catch { await run.DisposeAsync(); throw; }
        }
        public AsyncServiceScope Request(Guid? company = null)
        {
            var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company ?? TestCompanyIds.CompanyA);
            return scope;
        }
        public async Task RevokeAsync()
        {
            await using var scope = Request();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.RoleAssignments.Remove(await identity.RoleAssignments.Include(r => r.Scopes).SingleAsync(r => r.UserId == actor));
            await identity.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            var name = factory.DatabaseName;
            if (!Regex.IsMatch(name, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside this test database.");
            var builder = new SqlConnectionStringBuilder(factory.DatabaseConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }
}
