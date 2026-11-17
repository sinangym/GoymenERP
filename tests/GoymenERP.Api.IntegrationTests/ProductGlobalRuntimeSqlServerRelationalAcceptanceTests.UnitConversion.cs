using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ProductGlobalRuntimeSqlServerRelationalAcceptanceTests
{
    /// <summary>
    /// REAL SQL Product card + real dimension reader + real Settings typed identities. Unit-use profiles and the density/
    /// rounding policy are explicit synthetic owner replies, not production grants or a claim of Settings SQL acceptance.
    /// </summary>
    [SqlServerFact]
    public Task Purchase_unit_conversion_reads_real_card_dimensions_and_keeps_piece_remainder_and_source_versions() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var create = await CreateAsync(client, "unit-source-create", "SENTETIK birim çevrim sac");
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var productId = (await create.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Id;
            await using (var sql = new SqlConnection(factory.DatabaseConnectionString))
            {
                await sql.OpenAsync();
                await using var setup = sql.CreateCommand();
                setup.CommandText = "UPDATE product.Products SET ReferencesJson=@refs WHERE Id=@id; "
                    + "INSERT INTO product.ProductCardDimensions (ProductId,SectionKind,ThicknessMm,WidthMm,LengthMm,"
                    + "HeightMm,OuterDiameterMm,WallThicknessMm,KgPerMetre,Version,UpdatedAtUtc,UpdatedBy,UpdatedInCompanyId) "
                    + "VALUES (@id,N'Sheet',2,1000,2000,NULL,NULL,NULL,NULL,1,SYSDATETIMEOFFSET(),N'sql',@company);";
                setup.Parameters.AddWithValue("@refs", JsonSerializer.Serialize(new ProductReferences(Units:
                    new(Stock: SettingsUnitIdentitiesV1.Kilogram, Purchase: SettingsUnitIdentitiesV1.Piece,
                        Price: SettingsUnitIdentitiesV1.Piece)), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                setup.Parameters.AddWithValue("@id", productId); setup.Parameters.AddWithValue("@company", CompanyA);
                Assert.Equal(2, await setup.ExecuteNonQueryAsync());
            }
            await using var scope = factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var accessor = services.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            try
            {
                accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(GoymenClaimTypes.UserId, ActorId.ToString("D")),
                    new Claim(GoymenClaimTypes.CompanyAccess, CompanyA.ToString("D"))], "IsolatedSqlFixture")) };
                var company = services.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
                var records = new SqlProductPurchaseReader(services.GetRequiredService<ProductDbContext>(),
                    services.GetRequiredService<ProductReadStore>());
                var fixture = new UnitConversionSourcePolicyFixture();
                var source = new ProductPurchaseUnitConversionService(company, services.GetRequiredService<ICurrentUser>(),
                    records, services.GetRequiredService<IProductDimensionReaderV1>(),
                    services.GetRequiredService<ISettingsPhysicalUnitSemanticsV1>(), new(fixture, company), fixture);
                var result = await source.ConvertAsync(new(productId, 100m, SettingsUnitIdentitiesV1.Kilogram,
                    SettingsUnitIdentitiesV1.Piece, UnitConversionSourcePolicyFixture.On));
                Assert.Equal((ProductPurchaseUnitConversionOutcomeV1.Available, 3m, 5.8m),
                    (result.Outcome, result.Quantity, result.RemainderKg));
                Assert.Equal((productId, "1", 1L, SettingsUnitIdentitiesV1.Piece),
                    (result.Source!.ProductId, result.Source.ProductVersion, result.Source.DimensionVersion, result.Source.DefaultPriceUnitId));
                var equalBoundary = await source.CheckWeighbridgeAsync(new(productId, 1m, SettingsUnitIdentitiesV1.Piece,
                    31.714m, UnitConversionSourcePolicyFixture.On));
                Assert.True(equalBoundary.WithinTolerance);
                accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
                Assert.Equal(ProductPurchaseUnitConversionOutcomeV1.Denied,
                    (await source.ReadAsync(productId, UnitConversionSourcePolicyFixture.On)).Outcome);
            }
            finally { accessor.HttpContext = previous; }
        });

    private sealed class UnitConversionSourcePolicyFixture : ISettingsPrerequisiteProvider, ISettingsUnitConversionPolicyProviderV1
    {
        public static readonly DateOnly On = new(2026, 11, 17);
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UnitProfileV1(CompanyA, unitId, "SENTETIK", "synthetic", unitId == SettingsUnitIdentitiesV1.Kilogram,
                new(Guid.Parse("3d9ff870-2ba8-4e62-9ca5-d99e41578b7e"), 1), 3, true, On, null));
        public Task<SettingsUnitConversionPolicyV1> GetUnitConversionPolicyAsync(DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsUnitConversionPolicyV1(CompanyA, new(Guid.Parse("380bc696-5223-49c1-ae60-0211559bfdd8"), 3),
                7.85m, 3, MidpointRounding.AwayFromZero, 2, MidpointRounding.AwayFromZero, MidpointRounding.ToNegativeInfinity,
                1m, SettingsWeighbridgeToleranceBoundaryV1.Inclusive, SettingsWeighbridgeToleranceBaseV1.TheoreticalKg, On, null));
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
