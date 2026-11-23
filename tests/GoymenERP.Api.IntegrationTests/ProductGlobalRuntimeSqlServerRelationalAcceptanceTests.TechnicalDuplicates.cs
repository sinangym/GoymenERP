using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URN-D25 on a REAL SQL Server, through the composed host: the duplicate warning of a saved card lists the other ACTIVE cards of
/// its family with exactly its thickness, width, quality and coating; a draft twin and a card of another thickness are not
/// listed, and nothing is blocked. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductGlobalRuntimeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public Task Duplicate_warning_lists_active_cards_of_the_family_with_the_same_five_technical_values_only() =>
        WithDatabaseAsync(async (factory, _) =>
        {
            using var client = factory.CreateClientFor("group-bootstrap-admin", CompanyA);

            async Task<ProductMutationResponse> CreateAsync(string key, string name, decimal thickness, bool activate)
            {
                var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["thickness-mm"] = JsonSerializer.SerializeToElement(thickness),
                    ["width-mm"] = JsonSerializer.SerializeToElement(1250m),
                    ["length-mm"] = JsonSerializer.SerializeToElement(2500m),
                    ["steel-grade"] = JsonSerializer.SerializeToElement("S235JR"),
                    ["coating"] = JsonSerializer.SerializeToElement("uncoated"),
                };
                var draft = new CreateProductDraftRequest(name, ProductInitialCodeGroupV1.SacGroupId,
                    FamilyId: ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateFamilyId,
                    References: new ProductReferences(ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateTemplateId, "1",
                        attributes, new(Stock: SettingsUnitIdentitiesV1.Kilogram, Sales: SettingsUnitIdentitiesV1.Kilogram)));
                ProductMutationResponse created;
                using (var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products") { Content = JsonContent.Create(draft) })
                {
                    create.Headers.Add(ProductApiV1.IdempotencyHeader, $"urn-d25-{key}-create");
                    using var response = await client.SendAsync(create);
                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                    created = (await response.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
                }

                if (!activate) return created;
                using var activation = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/v1/products/{created.Card.Product.Id:D}/activation") { Content = JsonContent.Create(new ActivateProductRequest("1")) };
                activation.Headers.Add(ProductApiV1.IdempotencyHeader, $"urn-d25-{key}-activate");
                using var activated = await client.SendAsync(activation);
                Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
                return (await activated.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            }

            var activeTwin = await CreateAsync("active-twin", "SENTETIK URN-D25 aktif ikiz 3 × 1250", 3m, activate: true);
            await CreateAsync("other-thickness", "SENTETIK URN-D25 aktif 4 × 1250", 4m, activate: true);
            await CreateAsync("draft-twin", "SENTETIK URN-D25 taslak ikiz 3 × 1250", 3m, activate: false);
            var card = await CreateAsync("card", "SENTETIK URN-D25 yeni kart 3 × 1250", 3m, activate: false);

            using var read = await client.GetAsync(
                $"{ProductDuplicateRules.Path}?name={Uri.EscapeDataString(card.Card.Product.Name)}&excludeProductId={card.Card.Product.Id:D}");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var answer = (await read.Content.ReadFromJsonAsync<ProductDuplicateCandidates>())!;
            Assert.Empty(answer.ExactName);
            Assert.Equal([activeTwin.Card.Product.Id], answer.SameTechnical.Select(item => item.Id));
            Assert.False(answer.SameTechnicalHasMore);

            // An unsaved name check has no technical values to compare.
            using var unsaved = await client.GetAsync($"{ProductDuplicateRules.Path}?name={Uri.EscapeDataString("SENTETIK URN-D25 kaydedilmemiş")}");
            Assert.Equal(HttpStatusCode.OK, unsaved.StatusCode);
            Assert.Empty((await unsaved.Content.ReadFromJsonAsync<ProductDuplicateCandidates>())!.SameTechnical);

            // A warning, never a block: the card still activates.
            using var activation = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/products/{card.Card.Product.Id:D}/activation")
                { Content = JsonContent.Create(new ActivateProductRequest("1")) };
            activation.Headers.Add(ProductApiV1.IdempotencyHeader, "urn-d25-card-activate");
            using var activated = await client.SendAsync(activation);
            Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        });
}
