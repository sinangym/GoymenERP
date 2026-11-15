using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// B-09 on a REAL SQL Server, through the composed host: the V2 technical snapshot publishes the unchanged V1 snapshot of an
/// active cut-sheet card, first without an "Ebat/kalınlık" row (NoSource, binding "0"), then with a stored Sheet row (Stored,
/// binding "1", no kg/m), and refuses the stale "0" binding as a version conflict. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductGlobalRuntimeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Technical_snapshot_V2_binds_the_card_and_dimension_versions_and_refuses_a_stale_dimension_binding() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["thickness-mm"] = JsonSerializer.SerializeToElement(3m),
                ["width-mm"] = JsonSerializer.SerializeToElement(1250m),
                ["length-mm"] = JsonSerializer.SerializeToElement(2500.125m),
                ["steel-grade"] = JsonSerializer.SerializeToElement("S235JR"),
                ["coating"] = JsonSerializer.SerializeToElement("uncoated"),
            };
            var draft = new CreateProductDraftRequest(
                "SENTETIK S235JR 3 × 1250 × 2500,125 kesilmiş sac",
                ProductInitialCodeGroupV1.SacGroupId,
                FamilyId: ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateFamilyId,
                References: new ProductReferences(ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateTemplateId, "1",
                    attributes, new(Stock: SettingsUnitIdentitiesV1.Kilogram, Sales: SettingsUnitIdentitiesV1.Kilogram)));

            ProductMutationResponse created;
            using (var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products") { Content = JsonContent.Create(draft) })
            {
                create.Headers.Add(ProductApiV1.IdempotencyHeader, "product-technical-snapshot-v2-create");
                using var response = await client.SendAsync(create);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                created = (await response.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            }

            var productId = created.Card.Product.Id;
            ProductMutationResponse activated;
            using (var activate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{productId:D}/activation")
                   { Content = JsonContent.Create(new ActivateProductRequest("1")) })
            {
                activate.Headers.Add(ProductApiV1.IdempotencyHeader, "product-technical-snapshot-v2-activate");
                using var response = await client.SendAsync(activate);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                activated = (await response.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            }

            Assert.Equal(("Active", "2"), (activated.Card.Product.Lifecycle, activated.Card.Product.Version));

            await using (var setup = factory.Services.CreateAsyncScope())
            {
                var identity = setup.ServiceProvider.GetRequiredService<IdentityDbContext>();
                if (!await identity.Users.AnyAsync(user => user.Id == ActorId))
                    identity.Users.Add(new UserAccount(ActorId, "Disposable Product V2 snapshot actor", null));
                var roleId = Guid.NewGuid();
                var role = new RoleDefinition(roleId, $"PRODUCT-V2-SNAPSHOT-{roleId:N}",
                    "Disposable Product V2 technical snapshot actor", true);
                role.AddPermission(ProductPermissionManifestV1.OperationalSelectionRead);
                var assignment = new RoleAssignment(Guid.NewGuid(), ActorId, roleId, CompanyA,
                    DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
                assignment.AddScope(AccessScopeType.Company, CompanyA);
                identity.Roles.Add(role);
                identity.RoleAssignments.Add(assignment);
                await identity.SaveChangesAsync();
            }

            var effectiveOn = new DateOnly(2026, 10, 15);
            async Task<ProductManufacturingTechnicalSnapshotResultV2> ReadAsync(string? expectedDimensionVersion)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                accessor.HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(GoymenClaimTypes.UserId, ActorId.ToString("D")),
                        new Claim(GoymenClaimTypes.CompanyAccess, CompanyA.ToString("D")),
                    ], "IsolatedSqlFixture")),
                };
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
                return await scope.ServiceProvider.GetRequiredService<IProductManufacturingTechnicalSnapshotV2>()
                    .ReadAsync(new(productId, activated.Card.Product.Version, effectiveOn, expectedDimensionVersion), default);
            }

            var withoutRow = await ReadAsync(null);
            Assert.Empty(withoutRow.Errors);
            var noSource = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(withoutRow.Snapshot);
            Assert.Equal((productId, "2", 3m, 1250m, 2500.125m),
                (noSource.Technical.ProductId, noSource.Technical.ProductVersion, noSource.Technical.ThicknessMm,
                    noSource.Technical.WidthMm, noSource.Technical.LengthMm));
            Assert.Equal(new ProductManufacturingRecipeBindingV2(productId, "2",
                    ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateTemplateId, "1", "0"),
                noSource.RecipeBinding);
            Assert.Equal((ProductManufacturingDimensionSourceV2.NoSource, "0", (string?)null),
                (noSource.Dimension.Source, noSource.Dimension.Version, noSource.Dimension.SectionKind));
            Assert.Equal(new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.None, null),
                noSource.UnitWeight);

            await using (var sql = new SqlConnection(factory.DatabaseConnectionString))
            {
                await sql.OpenAsync();
                await using var insert = sql.CreateCommand();
                insert.CommandText =
                    """
                    INSERT INTO [product].[ProductCardDimensions] ([ProductId], [SectionKind], [ThicknessMm], [WidthMm], [LengthMm],
                        [HeightMm], [OuterDiameterMm], [WallThicknessMm], [KgPerMetre], [Version], [UpdatedAtUtc], [UpdatedBy],
                        [UpdatedInCompanyId])
                    VALUES (@productId, N'Sheet', 3.0, 1250.0, 2500.125, NULL, NULL, NULL, NULL, 1, SYSDATETIMEOFFSET(), N'sql',
                        @companyId);
                    """;
                insert.Parameters.AddWithValue("@productId", productId);
                insert.Parameters.AddWithValue("@companyId", CompanyA);
                Assert.Equal(1, await insert.ExecuteNonQueryAsync());
            }

            var stale = await ReadAsync("0");
            Assert.Null(stale.Snapshot);
            Assert.Equal(new ProductFieldError(ProductManufacturingTechnicalSnapshotCodesV2.ExpectedDimensionVersionField,
                ProductApiV1.VersionConflict), Assert.Single(stale.Errors));

            var bound = await ReadAsync("1");
            Assert.Empty(bound.Errors);
            var stored = Assert.IsType<ProductManufacturingTechnicalSnapshotV2>(bound.Snapshot);
            Assert.Equal(new ProductManufacturingRecipeBindingV2(productId, "2",
                    ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateTemplateId, "1", "1"),
                stored.RecipeBinding);
            Assert.Equal((ProductManufacturingDimensionSourceV2.Stored, "1", ProductDimensionSectionKindsV1.Sheet),
                (stored.Dimension.Source, stored.Dimension.Version, stored.Dimension.SectionKind));
            Assert.Equal((3m, 1250m, 2500.125m),
                (stored.Dimension.ThicknessMm!.Value, stored.Dimension.WidthMm!.Value, stored.Dimension.LengthMm!.Value));
            Assert.Equal(new ProductManufacturingUnitWeightV2(ProductManufacturingUnitWeightSourceV2.None, null),
                stored.UnitWeight);
        });
}
