using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// PRD-UNIT-1 against a REAL SQL Server and the real host: a global Product card's unit reference is the
/// Settings unit IDENTITY (company-independent; kg is <see cref="SettingsUnitIdentitiesV1.Kilogram"/>).
/// The card saves and reads back kg for stock and sales; the stock role must be the kilogram identity; an
/// unknown identity and a role not opened yet (purchase) are refused and nothing is stored. Whether a
/// company may use kg is its profile, checked at eligibility, not here.
/// </summary>
public sealed class ProductUnitReferenceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupId = Guid.Parse("82000000-0000-0000-0000-000000000061");

    private static readonly ProductMasterOperation[] DraftOperations =
        [ProductMasterOperation.Read, ProductMasterOperation.DraftCreate, ProductMasterOperation.DraftBasicEdit];

    [SqlServerFact]
    public Task A_card_holds_the_kilogram_identity_for_stock_and_sales_and_reads_it_back() =>
        WithDatabaseAsync(async factory =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            var options = (await client.GetFromJsonAsync<ProductReferenceOptions>(
                $"/api/v1/products/reference-options?groupId={GroupId:D}"))!;
            var kilogram = Assert.Single(options.Units);
            Assert.Equal((SettingsUnitIdentitiesV1.Kilogram, "kg", "kg", true),
                (kilogram.Id, kilogram.DisplayName, kilogram.Symbol, kilogram.IsKilogram));
            Assert.DoesNotContain("units", options.UnavailableCatalogs);

            using var created = await CreateAsync(client, "unit-reference-kg",
                new ProductUnitReferences(Stock: SettingsUnitIdentitiesV1.Kilogram, Sales: SettingsUnitIdentitiesV1.Kilogram));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<ProductMutationResponse>())!.Card.Product.Id;

            var stored = (await client.GetFromJsonAsync<ProductCardView>($"/api/v1/products/{id:D}"))!;
            Assert.Equal((SettingsUnitIdentitiesV1.Kilogram, SettingsUnitIdentitiesV1.Kilogram),
                (stored.References.Units!.Stock, stored.References.Units.Sales));
            Assert.Equal("kg", stored.Product.Labels.PrimaryStockUnit);
            Assert.Equal(new Dictionary<string, string> { ["stock"] = "kg", ["sales"] = "kg" }, stored.Product.Labels.Units);
        });

    [SqlServerFact]
    public Task An_unknown_identity_and_a_role_not_opened_yet_are_refused_and_nothing_is_stored() =>
        WithDatabaseAsync(async factory =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);

            using var unknown = await CreateAsync(client, "unit-reference-unknown",
                new ProductUnitReferences(Stock: Guid.Parse("82000000-0000-4000-8000-0000000000ee")));
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            Assert.Equal([("references.units.stock", "invalid_reference")], await FieldErrorsAsync(unknown));

            using var purchase = await CreateAsync(client, "unit-reference-purchase",
                new ProductUnitReferences(Purchase: SettingsUnitIdentitiesV1.Kilogram));
            Assert.Equal(HttpStatusCode.BadRequest, purchase.StatusCode);
            Assert.Equal([("references.units.purchase", "invalid_reference")], await FieldErrorsAsync(purchase));

            await using var sql = new SqlConnection(factory.DatabaseConnectionString);
            await sql.OpenAsync();
            await using var count = sql.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM [product].[Products]";
            Assert.Equal(0, (int)(await count.ExecuteScalarAsync())!);
        });

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string key, ProductUnitReferences units)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDraftRequest($"SENTETIK {key}", GroupId,
                References: new ProductReferences(Units: units))),
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task<(string Field, string Code)[]> FieldErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ProductApiV1.ValidationFailed, document.RootElement.GetProperty("code").GetString());
        return [.. document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => (error.GetProperty("field").GetString()!, error.GetProperty("code").GetString()!))];
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
                foreach (var operation in DraftOperations)
                    identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                        SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                        "Synthetic isolated SQL fixture; not a Sinan or production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null));
                await identity.SaveChangesAsync();
            }

            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var seed = sql.CreateCommand();
                seed.CommandText = "INSERT INTO [product].[CodeGroups] ([Id], [Name], [Prefix]) VALUES (@id, N'SENTETIK unit reference group', 'PUNT');";
                seed.Parameters.AddWithValue("@id", GroupId);
                Assert.Equal(1, await seed.ExecuteNonQueryAsync());
            }

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
}
