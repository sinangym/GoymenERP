using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// INV-OPEN-2, ORG #341 P3-1 (#335 P3-1): the opening command's Product port over the REAL eligibility chain —
/// the stored card, the family policy over the registry, and the real approved Settings kg profiles — against a
/// real SQL Server. The family release is SYNTHETIC and lives only in this test's disposable database (never
/// seeded; Business Q6 decides the real one). The fresh-authority double grants the narrow selection read; the
/// grant itself is not what is under test here.
/// <list type="bullet">
/// <item>Company A (GDC, the approved kg profile): the counted kg card is stock-eligible.</item>
/// <item>Company B (no approved kg source): Settings' own answer passes through the port
/// (<c>settings_provider_unavailable</c>) — never <c>invalid_reference</c>, never eligible — and the opening
/// command answers any port failure as 503 <c>inventory_product_eligibility_unavailable</c>.</item>
/// </list>
/// </summary>
public sealed class InventoryOpeningProductEligibilitySqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid GroupId = Guid.Parse("84000000-0000-0000-0000-000000000031");
    private static readonly Guid HotCoil = Guid.Parse("84000000-0000-4000-8000-000000000041");
    private static readonly Guid TemplateId = Guid.Parse("84000000-0000-4000-8000-000000000051");
    private static readonly DateOnly CountedOn = new(2026, 9, 19);

    private static readonly ProductMasterOperation[] Operations =
        [ProductMasterOperation.Read, ProductMasterOperation.DraftCreate, ProductMasterOperation.DraftBasicEdit, ProductMasterOperation.Activate];

    [SqlServerFact]
    public Task A_kg_card_is_stock_eligible_in_GDC_and_company_B_gets_the_Settings_answer_through_the_port() =>
        WithDatabaseAsync(async factory =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var (productId, version) = await ActiveGenuineKgCardAsync(client, "opening-eligibility");

            var inA = await PortIn(factory, CompanyA).EvaluateAsync(productId, version, CountedOn, default);
            Assert.True(inA.IsEligible);
            Assert.Empty(inA.Issues);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var company = new CompanyContext();
                company.Set(CompanyA);
                var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
                var displayPort = new ProductStockDisplayReferenceService(company, new User(CompanyA),
                    new GrantedIn(CompanyA), new ProductReadStore(db), new SqlProductFamilyDefinitionSource(db));
                var stock = new InventoryStockPositionsV1([
                    new(CompanyA, Guid.NewGuid(), "1", "SQL-DISPLAY-1", productId, version, "Standard",
                        new(Guid.NewGuid(), "HEAT-SQL", "COIL-SQL"), "HEAT-SQL", "COIL-SQL", "Complete",
                        "Company", CompanyA, "Company", CompanyA, Guid.NewGuid(), "10", "10", "0", "0", "10"),
                ], false);

                var composed = await InventoryStockPositionDisplayHost.ComposeAsync(stock, displayPort, default);
                var display = Assert.Single(composed.Items).ProductDisplay;
                Assert.Equal((productId, version), (display.Identity!.ProductId, display.Identity.ProductVersion));
                Assert.Equal((1.5m, 1250m), (display.Technical!.ThicknessMm, display.Technical.WidthMm));
                Assert.Empty(display.Errors);

                var (syntheticProductId, syntheticVersion) = await ActiveSyntheticKgCardAsync(
                    client, "opening-eligibility-synthetic");
                var syntheticStock = new InventoryStockPositionsV1([
                    new(CompanyA, Guid.NewGuid(), "1", "SQL-SYNTHETIC-1", syntheticProductId,
                        syntheticVersion, "Standard", new(Guid.NewGuid(), "HEAT-SYN", "COIL-SYN"),
                        "HEAT-SYN", "COIL-SYN", "Complete", "Company", CompanyA, "Company", CompanyA,
                        Guid.NewGuid(), "10", "10", "0", "0", "10"),
                ], false);
                var unavailable = await Assert.ThrowsAsync<ProductOperationException>(
                    () => InventoryStockPositionDisplayHost.ComposeAsync(syntheticStock, displayPort, default));
                Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            }

            // Company B has no approved kg source: the Settings code passes through; it is not a field error.
            var inB = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => PortIn(factory, CompanyB).EvaluateAsync(productId, version, CountedOn, default));
            Assert.Equal("settings_provider_unavailable", inB.Code);
        });

    /// <summary>The port as the host composes it, over the real Product eligibility chain in one company.</summary>
    private static ProductInventoryStockPort PortIn(SqlServerRelationalApiFactory factory, Guid company)
    {
        var scope = factory.Services.CreateScope();
        var context = new CompanyContext();
        context.Set(company);
        var user = new User(company);
        var product = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
        return new ProductInventoryStockPort(new ProductStockEligibilityService(context, user, new GrantedIn(company),
            new ProductReadStore(product), new ProductFamilyRequirementPolicyV2(new SqlProductFamilyDefinitionSource(product)),
            new ProductUnitProfileAdapter(new ApprovedSettingsPrerequisiteProvider(context, user), context)));
    }

    private static Task<(Guid ProductId, string Version)> ActiveGenuineKgCardAsync(HttpClient client, string key) =>
        ActiveKgCardAsync(client, key,
            ProductInitialCodeGroupV1.SacGroupId,
            ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
            ProductInitialTechnicalDefinitionV1.TemplateId,
            new Dictionary<string, object?>
            {
                ["thickness-mm"] = 1.5m,
                ["width-mm"] = 1250m,
                ["steel-grade"] = "S235JR",
                ["coating"] = "uncoated",
            });

    private static Task<(Guid ProductId, string Version)> ActiveSyntheticKgCardAsync(HttpClient client, string key) =>
        ActiveKgCardAsync(client, key, GroupId, HotCoil, TemplateId,
            new Dictionary<string, object?>
            {
                ["syn-thickness-mm"] = 1.5m,
                ["syn-width-mm"] = 1250m,
                ["syn-grade"] = "SYN-A",
                ["syn-coating"] = "syn-none",
            });

    private static async Task<(Guid ProductId, string Version)> ActiveKgCardAsync(HttpClient client, string key,
        Guid groupId, Guid familyId, Guid templateId, IReadOnlyDictionary<string, object?> attributeValues)
    {
        var references = new ProductReferences(templateId, "1",
            attributeValues.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal),
            new ProductUnitReferences(Stock: SettingsUnitIdentitiesV1.Kilogram, Sales: SettingsUnitIdentitiesV1.Kilogram));
        var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDraftRequest($"SENTETIK {key}", groupId, FamilyId: familyId,
                References: references)),
        };
        create.Headers.Add("Idempotency-Key", key);
        using var created = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = (await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product;

        var activate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{draft.Id:D}/activation")
        {
            Content = JsonContent.Create(new ActivateProductRequest(draft.Version)),
        };
        activate.Headers.Add("Idempotency-Key", $"{key}-activate");
        using var activated = await client.SendAsync(activate);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var product = (await activated.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product;
        Assert.Equal("Active", product.Lifecycle);
        return (product.Id, product.Version);
    }

    /// <summary>A SYNTHETIC release: one family, one template with the four role fields.</summary>
    private static async Task ReleaseAsync(string connection)
    {
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = """
            INSERT INTO [product].[Families] ([Id], [Code], [Name], [Cins], [Tur], [Tip], [RuleSet], [SourceRef]) VALUES
                (@hot, 'SYN-HR-COIL', N'Sentetik sıcak rulo', N'Sentetik cins', N'Sentetik tür sıcak', N'Sentetik tip rulo', 'gdc-band-coil', 'SYNTHETIC-TEST');
            INSERT INTO [product].[Templates] ([Id], [Version], [Code], [Name], [SourceRef]) VALUES
                (@template, 1, 'SYN-BAND-COIL', N'Sentetik bant/rulo', 'SYNTHETIC-TEST');
            INSERT INTO [product].[TemplateFamilies] ([TemplateId], [TemplateVersion], [FamilyId]) VALUES (@template, 1, @hot);
            INSERT INTO [product].[DictionaryEntries] ([Dictionary], [Code], [DisplayName], [Status], [SourceRef], [ParentCode], [MassGm2], [IsExplicitNone]) VALUES
                ('syn-grades', N'SYN-A', N'Sentetik A', 1, 'SYNTHETIC-TEST', NULL, NULL, 0),
                ('syn-coatings', N'syn-none', N'Sentetik kaplamasız', 1, 'SYNTHETIC-TEST', NULL, NULL, 1);
            INSERT INTO [product].[TemplateFields] ([TemplateId], [TemplateVersion], [Key], [Position], [Kind], [Role], [Required], [Dictionary], [AllowsCandidate], [AppliesWhenField], [AppliesWhenCode], [ParentField]) VALUES
                (@template, 1, 'syn-thickness-mm', 1, 1, 'thickness', 1, NULL, 0, NULL, NULL, NULL),
                (@template, 1, 'syn-width-mm', 2, 1, 'width', 1, NULL, 0, NULL, NULL, NULL),
                (@template, 1, 'syn-grade', 3, 2, 'quality', 1, 'syn-grades', 0, NULL, NULL, NULL),
                (@template, 1, 'syn-coating', 4, 2, 'coating', 1, 'syn-coatings', 0, NULL, NULL, NULL);
            """;
        command.Parameters.AddWithValue("@hot", HotCoil);
        command.Parameters.AddWithValue("@template", TemplateId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
        var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(connection,
                new ProductPilotIssuerBinding(ActorId, ActorExternalId), Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
        }, databaseName: databaseName);
        try
        {
            using var startup = factory.CreateClient();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.True(identity.Database.IsSqlServer());
                foreach (var operation in Operations)
                    identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                        SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                        "Synthetic isolated SQL fixture; not a Sinan or production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null));
                await identity.SaveChangesAsync();
            }

            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var seed = sql.CreateCommand();
                seed.CommandText = "INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix]) VALUES (@id, N'SENTETIK opening eligibility group', 'POEL');";
                seed.Parameters.AddWithValue("@id", GroupId);
                Assert.Equal(1, await seed.ExecuteNonQueryAsync());
            }

            await ReleaseAsync(connection);
            await test(factory);
        }
        finally
        {
            await factory.DisposeAsync();
            if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;";
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>The narrow selection read, granted company-wide in exactly one company.</summary>
    private sealed class GrantedIn(Guid company) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == company && permission == ProductPermissionManifestV1.OperationalSelectionRead
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "SYNTHETIC-SELECTION" },
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "synthetic-opening-counter";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
