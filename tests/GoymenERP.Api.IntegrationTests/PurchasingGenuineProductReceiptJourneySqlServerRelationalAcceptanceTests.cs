using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A real public Product card remains the same immutable identity/version through Purchasing and the
/// physical Inventory receipt. The test uses no synthetic Product provider and proves the receipt replay
/// through fresh public reads and the stored single-effect rows.
/// </summary>
public sealed class PurchasingGenuineProductReceiptJourneySqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyBActorId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;

    [SqlServerFact]
    public async Task Genuine_sac_product_flows_through_purchase_receipt_once_and_foreign_company_cannot_read_it()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var databaseConnection = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
        var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(databaseConnection,
                new ProductPilotIssuerBinding(ActorId, ActorExternalId),
                Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
        }, databaseName: databaseName);

        try
        {
            using (var startup = factory.CreateClient())
            using (var health = await startup.GetAsync("/health/live"))
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            await GrantDisposableJourneyAuthorityAsync(factory);

            using var companyA = factory.CreateClientFor(TestUsers.GroupAdmin, CompanyA);
            using var companyB = factory.CreateClientFor(TestUsers.CompanyB, CompanyB);
            companyA.DefaultRequestHeaders.Remove("Idempotency-Key");
            companyB.DefaultRequestHeaders.Remove("Idempotency-Key");

            var (supplierPartyId, supplierAccountId) = await CreateSupplierAsync(companyA);
            var genuine = GenuineSacDraft();

            // The committed 201 response is deliberately not read. Repeating the exact key/body must
            // recover the same Product identity instead of creating a second card.
            using (var lost = await SendCommandAsync(companyA, HttpMethod.Post, ProductApiV1.BasePath,
                       genuine, "purchase-genuine-product-create"))
                Assert.Equal(HttpStatusCode.Created, lost.StatusCode);
            using var productReplay = await SendCommandAsync(companyA, HttpMethod.Post,
                ProductApiV1.BasePath, genuine, "purchase-genuine-product-create");
            Assert.Equal(HttpStatusCode.Created, productReplay.StatusCode);
            AssertReplay(productReplay);
            var draft = (await productReplay.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.True(draft.IsReplay);
            Assert.Equal(("Draft", "1", ProductInitialCodeGroupV1.SacGroupId,
                    ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId),
                (draft.Card.Product.Lifecycle, draft.Card.Product.Version, draft.Card.Product.GroupId,
                    draft.Card.Product.FamilyId));

            using (var reopenedDraft = await companyA.GetAsync($"/api/v1/products/{draft.Card.Product.Id:D}"))
            {
                Assert.Equal(HttpStatusCode.OK, reopenedDraft.StatusCode);
                var card = (await reopenedDraft.Content.ReadFromJsonAsync<ProductCardView>())!;
                Assert.Equal((draft.Card.Product.Id, "1", ProductInitialTechnicalDefinitionV1.TemplateId,
                        "1", SettingsUnitIdentitiesV1.Kilogram, SettingsUnitIdentitiesV1.Kilogram),
                    (card.Product.Id, card.Product.Version, card.References.TemplateId,
                        card.References.TemplateVersion, card.References.Units!.Stock,
                        card.References.Units.Purchase));
                Assert.Equal(3m, card.References.Attributes!["thickness-mm"].GetDecimal());
                Assert.Equal(248m, card.References.Attributes["width-mm"].GetDecimal());
                Assert.Equal("S235JR", card.References.Attributes["steel-grade"].GetString());
                Assert.Equal("uncoated", card.References.Attributes["coating"].GetString());
                Assert.False(card.References.Attributes.ContainsKey("producer-grade"));
            }

            using var activation = await SendCommandAsync(companyA, HttpMethod.Post,
                $"/api/v1/products/{draft.Card.Product.Id:D}/activation",
                new ActivateProductRequest(draft.Card.Product.Version), "purchase-genuine-product-activate");
            Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
            var active = (await activation.Content.ReadFromJsonAsync<ProductMutationResponse>())!;
            Assert.Equal((draft.Card.Product.Id, "Active", "2"),
                (active.Card.Product.Id, active.Card.Product.Lifecycle, active.Card.Product.Version));

            using (var freshProduct = await companyA.GetAsync($"/api/v1/products/{active.Card.Product.Id:D}"))
            {
                Assert.Equal(HttpStatusCode.OK, freshProduct.StatusCode);
                var card = (await freshProduct.Content.ReadFromJsonAsync<ProductCardView>())!;
                Assert.Equal((active.Card.Product.Id, active.Card.Product.Version, (bool?)true),
                    (card.Product.Id, card.Product.Version, card.Product.Roles.Purchasable.Value));
            }

            var effectiveOn = new DateOnly(2026, 10, 7);
            var requestLineId = Guid.NewGuid();
            using var createdRequest = await SendCommandAsync(companyA, HttpMethod.Post,
                PurchasingApiV1.RequestsPath,
                new CreatePurchaseRequestRequest(effectiveOn,
                    [new(requestLineId, active.Card.Product.Id, active.Card.Product.Version,
                        SettingsUnitIdentitiesV1.Kilogram, "10")]),
                "purchase-genuine-request-create");
            Assert.Equal(HttpStatusCode.Created, createdRequest.StatusCode);
            var purchaseRequest = (await createdRequest.Content.ReadFromJsonAsync<PurchaseRequestResponse>())!;
            Assert.Equal((active.Card.Product.Id, active.Card.Product.Version),
                (Assert.Single(purchaseRequest.Detail.Lines).ProductId, purchaseRequest.Detail.Lines[0].ProductVersion));

            using var submittedRequest = await SendCommandAsync(companyA, HttpMethod.Post,
                $"/api/v1/purchasing/requests/{purchaseRequest.Detail.Request.Id:D}/submission",
                new SubmitPurchaseRequestRequest(purchaseRequest.Detail.Request.Version),
                "purchase-genuine-request-submit");
            Assert.Equal(HttpStatusCode.OK, submittedRequest.StatusCode);
            var submitted = (await submittedRequest.Content.ReadFromJsonAsync<PurchaseRequestResponse>())!;
            Assert.Equal("Submitted", submitted.Detail.Request.Status);

            var orderLineId = Guid.NewGuid();
            using var createdOrder = await SendCommandAsync(companyA, HttpMethod.Post,
                PurchasingApiV1.OrdersPath,
                new CreatePurchaseOrderRequest(supplierPartyId, supplierAccountId,
                    submitted.Detail.Request.Id, effectiveOn,
                    [new(orderLineId, active.Card.Product.Id, active.Card.Product.Version,
                        SettingsUnitIdentitiesV1.Kilogram, "10", requestLineId)]),
                "purchase-genuine-order-create");
            Assert.Equal(HttpStatusCode.Created, createdOrder.StatusCode);
            var order = (await createdOrder.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
            Assert.Equal((active.Card.Product.Id, active.Card.Product.Version),
                (Assert.Single(order.Detail.Lines).ProductId, order.Detail.Lines[0].ProductVersion));

            using var approvedOrder = await SendCommandAsync(companyA, HttpMethod.Post,
                $"/api/v1/purchasing/orders/{order.Detail.Order.Id:D}/approval",
                new ApprovePurchaseOrderRequest(order.Detail.Order.Version),
                "purchase-genuine-order-approve");
            Assert.Equal(HttpStatusCode.OK, approvedOrder.StatusCode);
            var approved = (await approvedOrder.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
            Assert.Equal("Approved", approved.Detail.Order.Status);
            var approvedLine = Assert.Single(approved.Detail.Lines);

            using var optionsResponse = await companyA.GetAsync(
                $"/api/v1/purchasing/orders/{approved.Detail.Order.Id:D}/lines/{approvedLine.LineId:D}/goods-receipt-options");
            Assert.Equal(HttpStatusCode.OK, optionsResponse.StatusCode);
            var options = (await optionsResponse.Content.ReadFromJsonAsync<PurchaseGoodsReceiptOptionsV1>())!;
            Assert.Equal((active.Card.Product.Id, active.Card.Product.Version,
                    SettingsUnitIdentitiesV1.Kilogram),
                (options.Source.ProductId, options.Source.ProductVersion, options.Source.UnitId));
            Assert.NotEmpty(options.Locations);
            var location = options.Locations[0];

            var receipt = new RecordPurchaseGoodsReceiptV1(
                approved.Detail.Order.Version, active.Card.Product.Version, Guid.NewGuid(), "10", "10",
                location.LocationId, Guid.NewGuid(), Guid.NewGuid(), "SAC-PURCHASE-GENUINE-1",
                "HEAT-GENUINE-1", "COIL-GENUINE-1", Guid.NewGuid(),
                new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
            var receiptPath = $"/api/v1/purchasing/orders/{approved.Detail.Order.Id:D}/lines/{approvedLine.LineId:D}/goods-receipts";
            using var recordedResponse = await SendCommandAsync(companyA, HttpMethod.Post, receiptPath, receipt,
                "purchase-genuine-goods-receipt");
            Assert.Equal(HttpStatusCode.Created, recordedResponse.StatusCode);
            var recorded = (await recordedResponse.Content.ReadFromJsonAsync<PurchaseGoodsReceiptResponseV1>())!;
            Assert.False(recorded.IsReplay);
            AssertReceiptIdentity(recorded.Receipt, receipt, active.Card.Product.Id, active.Card.Product.Version);
            AssertReceiptQuantity(recorded.Receipt);
            var stored = await StoredEffectsAsync(factory.DatabaseConnectionString, receipt);
            Assert.Equal(new StoredEffects(1, 1, 1, 1, 1), stored);

            var receiptDetailPath =
                $"/api/v1/purchasing/orders/{approved.Detail.Order.Id:D}/goods-receipts/{receipt.AcceptanceId:D}";
            using var detailResponse = await companyA.GetAsync(receiptDetailPath);
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var detail = (await detailResponse.Content.ReadFromJsonAsync<PurchaseGoodsReceiptDetailV1>())!;
            Assert.Equal(recorded.Receipt, detail.Receipt);
            Assert.Equal(stored, await StoredEffectsAsync(factory.DatabaseConnectionString, receipt));

            using var replayResponse = await SendCommandAsync(companyA, HttpMethod.Post, receiptPath, receipt,
                "purchase-genuine-goods-receipt");
            Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
            AssertReplay(replayResponse);
            var replayed = (await replayResponse.Content.ReadFromJsonAsync<PurchaseGoodsReceiptResponseV1>())!;
            Assert.True(replayed.IsReplay);
            Assert.Equal(recorded.Receipt, replayed.Receipt);
            Assert.Equal(stored, await StoredEffectsAsync(factory.DatabaseConnectionString, receipt));

            using var freshA = factory.CreateClientFor(TestUsers.GroupAdmin, CompanyA);
            using var reopenedDetailResponse = await freshA.GetAsync(receiptDetailPath);
            Assert.Equal(HttpStatusCode.OK, reopenedDetailResponse.StatusCode);
            var reopenedDetail = (await reopenedDetailResponse.Content.ReadFromJsonAsync<PurchaseGoodsReceiptDetailV1>())!;
            Assert.Equal(recorded.Receipt, reopenedDetail.Receipt);
            Assert.Equal(stored, await StoredEffectsAsync(factory.DatabaseConnectionString, receipt));

            using var listResponse = await freshA.GetAsync(
                $"/api/v1/purchasing/orders/{approved.Detail.Order.Id:D}/goods-receipts?limit=100");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var listed = (await listResponse.Content.ReadFromJsonAsync<PurchaseGoodsReceiptPageV1>())!;
            Assert.Equal(recorded.Receipt, Assert.Single(listed.Items));

            var storedResultJson = await ReceiptResultJsonAsync(
                factory.DatabaseConnectionString, receipt.AcceptanceId);
            try
            {
                await SetLegacyReceiptQuantityAsync(factory.DatabaseConnectionString, receipt.AcceptanceId);
                using var legacyResponse = await freshA.GetAsync(receiptDetailPath);
                Assert.Equal(HttpStatusCode.OK, legacyResponse.StatusCode);
                var legacy = (await legacyResponse.Content.ReadFromJsonAsync<PurchaseGoodsReceiptDetailV1>())!;
                Assert.Null(legacy.Receipt.StockItemQuantity);
                Assert.Equal(recorded.Receipt.MeasuredQuantityKg, legacy.Receipt.MeasuredQuantityKg);
                Assert.Equal(stored, await StoredEffectsAsync(factory.DatabaseConnectionString, receipt));
            }
            finally
            {
                await RestoreReceiptResultJsonAsync(
                    factory.DatabaseConnectionString, receipt.AcceptanceId, storedResultJson);
            }

            freshA.DefaultRequestHeaders.Add(InventoryStockPositionApiV1.ProductDisplayContractHeader,
                InventoryStockPositionApiV1.ProductDisplayContractVersion);
            using var positionsResponse = await freshA.GetAsync(
                $"{InventoryStockPositionApiV1.PositionsPath}?productId={active.Card.Product.Id:D}&limit=25");
            Assert.Equal(HttpStatusCode.OK, positionsResponse.StatusCode);
            var positions = (await positionsResponse.Content.ReadFromJsonAsync<InventoryStockPositionPageV1>())!;
            var position = Assert.Single(positions.Items);
            Assert.Equal((receipt.LabelId, recorded.Receipt.LabelVersion, receipt.LabelCode,
                    active.Card.Product.Id, active.Card.Product.Version, receipt.LotId, "10", "10", "0"),
                (position.LabelId, position.LabelVersion, position.LabelCode, position.ProductId,
                    position.ProductVersion, position.Lot.Id, position.PhysicalKg, position.FreePhysicalKg,
                    position.BlockedPhysicalKg));

            using var timelineResponse = await freshA.GetAsync($"{InventoryMovementTimelineApiV1.Path}?limit=50");
            Assert.Equal(HttpStatusCode.OK, timelineResponse.StatusCode);
            var timeline = (await timelineResponse.Content.ReadFromJsonAsync<InventoryMovementTimelinePageV1>())!;
            var movement = Assert.Single(timeline.Items, item => item.MovementId == receipt.MovementId);
            Assert.Equal((receipt.AcceptanceId, "purchasing.goods-acceptance"),
                (movement.SourceId, movement.SourceKind));
            var movementLine = Assert.Single(movement.Lines);
            Assert.Equal((receipt.LabelId, receipt.LabelCode, active.Card.Product.Id, "10"),
                (movementLine.LabelId, movementLine.LabelCode, movementLine.ProductId,
                    movementLine.QuantityKg));

            using var labelResponse = await freshA.GetAsync($"/api/v1/inventory/labels/{receipt.LabelId:D}");
            Assert.Equal(HttpStatusCode.OK, labelResponse.StatusCode);
            var label = (await labelResponse.Content.ReadFromJsonAsync<InventoryLabelViewV1>())!;
            Assert.Equal((receipt.LabelId, active.Card.Product.Id, receipt.LotId),
                (label.LabelId, label.ProductId, label.LotId));

            using var foreignLabel = await companyB.GetAsync($"/api/v1/inventory/labels/{receipt.LabelId:D}");
            Assert.Equal(HttpStatusCode.NotFound, foreignLabel.StatusCode);
            using var foreignPositionsResponse = await companyB.GetAsync(
                $"{InventoryStockPositionApiV1.PositionsPath}?productId={active.Card.Product.Id:D}&limit=25");
            Assert.Equal(HttpStatusCode.OK, foreignPositionsResponse.StatusCode);
            var foreignPositions =
                (await foreignPositionsResponse.Content.ReadFromJsonAsync<InventoryStockPositionLegacyPageV1>())!;
            Assert.Empty(foreignPositions.Items);
            using var foreignTimelineResponse = await companyB.GetAsync($"{InventoryMovementTimelineApiV1.Path}?limit=50");
            Assert.Equal(HttpStatusCode.OK, foreignTimelineResponse.StatusCode);
            var foreignTimeline = (await foreignTimelineResponse.Content.ReadFromJsonAsync<InventoryMovementTimelinePageV1>())!;
            Assert.DoesNotContain(foreignTimeline.Items, item => item.MovementId == receipt.MovementId);
            using var foreignReceipt = await companyB.GetAsync(receiptDetailPath);
            Assert.Equal(HttpStatusCode.NotFound, foreignReceipt.StatusCode);
            using var foreignReceiptList = await companyB.GetAsync(
                $"/api/v1/purchasing/orders/{approved.Detail.Order.Id:D}/goods-receipts?limit=100");
            Assert.Equal(HttpStatusCode.NotFound, foreignReceiptList.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnection, databaseName);
        }
    }

    private static CreateProductDraftRequest GenuineSacDraft() => new(
        "S235JR 3 mm × 248 mm kaplamasız sac",
        ProductInitialCodeGroupV1.SacGroupId,
        FamilyId: ProductInitialTechnicalDefinitionV1.HotRolledSlitCoilFamilyId,
        Roles: new ProductRoleInputs(Purchasable: new ProductRoleInput(true, "manual")),
        References: new ProductReferences(
            ProductInitialTechnicalDefinitionV1.TemplateId,
            "1",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["thickness-mm"] = JsonSerializer.SerializeToElement(3m),
                ["width-mm"] = JsonSerializer.SerializeToElement(248m),
                ["steel-grade"] = JsonSerializer.SerializeToElement("S235JR"),
                ["coating"] = JsonSerializer.SerializeToElement("uncoated"),
            },
            new ProductUnitReferences(Stock: SettingsUnitIdentitiesV1.Kilogram,
                Purchase: SettingsUnitIdentitiesV1.Kilogram)));

    private static async Task GrantDisposableJourneyAuthorityAsync(SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        foreach (var operation in Enum.GetValues<ProductMasterOperation>())
            identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                "Disposable genuine purchasing journey fixture; not a production grant",
                DateTimeOffset.UtcNow.AddMinutes(-1), null));

        if (!await identity.Users.AnyAsync(user => user.Id == ActorId))
            identity.Users.Add(new UserAccount(ActorId, "Disposable genuine purchasing journey actor", null));
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"PURCHASE-GENUINE-{roleId:N}",
            "Disposable genuine Product, Purchasing and Inventory SQL journey actor", true);
        role.AddPermission(ProductPermissionManifestV1.OperationalSelectionRead);
        role.AddPermission(PurchasingPermissionManifestV1.OrdersCreate);
        role.AddPermission(PurchasingPermissionManifestV1.OrdersRead);
        role.AddPermission(PurchasingPermissionManifestV1.OrdersApprove);
        role.AddPermission(PurchasingPermissionManifestV1.GoodsReceiptsRecord);
        role.AddPermission(ErpPermissions.InventoryLabelsRead);
        role.AddPermission(ErpPermissions.InventoryBalancesRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), ActorId, roleId, CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, CompanyA);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(assignment);

        var foreignReaderRoleId = Guid.NewGuid();
        var foreignReaderRole = new RoleDefinition(foreignReaderRoleId,
            $"PURCHASE-GENUINE-FOREIGN-READER-{foreignReaderRoleId:N}",
            "Disposable Company B Inventory reader for tenant non-disclosure proof", true);
        foreignReaderRole.AddPermission(ErpPermissions.InventoryLabelsRead);
        foreignReaderRole.AddPermission(ErpPermissions.InventoryBalancesRead);
        foreignReaderRole.AddPermission(PurchasingPermissionManifestV1.OrdersRead);
        var foreignReaderAssignment = new RoleAssignment(Guid.NewGuid(), CompanyBActorId,
            foreignReaderRoleId, CompanyB, DateTimeOffset.UtcNow.AddMinutes(-1), null,
            approvedByUserId: null);
        foreignReaderAssignment.AddScope(AccessScopeType.Company, CompanyB);
        identity.Roles.Add(foreignReaderRole);
        identity.RoleAssignments.Add(foreignReaderAssignment);
        await identity.SaveChangesAsync();
    }

    private static async Task<(Guid PartyId, Guid AccountId)> CreateSupplierAsync(HttpClient client)
    {
        using var response = await SendCommandAsync(client, HttpMethod.Post, "/api/v1/counterparties",
            new CreateCounterparty(PartyKind.LegalEntity, "Gerçek SAC Tedarikçisi A.Ş.",
                "Gerçek SAC Tedarikçisi", PartyRole.Supplier), "purchase-genuine-supplier-create");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var account = Assert.Single(json.RootElement.GetProperty("companyAccounts").EnumerateArray());
        Assert.Equal(CompanyA, account.GetProperty("companyId").GetGuid());
        return (json.RootElement.GetProperty("id").GetGuid(), account.GetProperty("id").GetGuid());
    }

    private static async Task<HttpResponseMessage> SendCommandAsync<T>(HttpClient client, HttpMethod method,
        string path, T body, string key)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static void AssertReplay(HttpResponseMessage response) =>
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Idempotency-Replayed")));

    private static void AssertReceiptIdentity(PurchaseGoodsReceiptViewV1 actual,
        RecordPurchaseGoodsReceiptV1 expected, Guid productId, string productVersion)
    {
        Assert.Equal((CompanyA, expected.AcceptanceId, productId, productVersion, expected.AcceptedQuantity,
                expected.LotId, expected.LabelId, expected.LabelCode, expected.MovementId,
                expected.LocationId, expected.MeasuredQuantityKg),
            (actual.CompanyId, actual.AcceptanceId, actual.ProductId, actual.ProductVersion,
                actual.AcceptedQuantity, actual.LotId, actual.LabelId, actual.LabelCode,
                actual.MovementId, actual.LocationId, actual.MeasuredQuantityKg));
    }

    private static void AssertReceiptQuantity(PurchaseGoodsReceiptViewV1 receipt)
    {
        var quantity = Assert.IsType<PurchaseGoodsReceiptQuantityV2>(receipt.StockItemQuantity);
        Assert.Equal((receipt.CompanyId, PurchaseGoodsReceiptApiV1.QuantitySubjectKind, receipt.ProductId),
            (quantity.CompanyId, quantity.Subject.Kind, quantity.Subject.Id));
        Assert.Equal((PurchaseGoodsReceiptApiV1.QuantitySourceKind, receipt.AcceptanceId,
                receipt.PurchaseOrderVersion),
            (quantity.Source.Kind, quantity.Source.Id, quantity.Source.Version));
        Assert.Equal((receipt.MeasuredQuantityKg, SettingsUnitIdentitiesV1.Kilogram,
                PurchaseGoodsReceiptApiV1.QuantityDimension),
            (quantity.BaseQuantity.Value, quantity.BaseQuantity.UnitId, quantity.BaseQuantity.Dimension));
        Assert.NotEqual(Guid.Empty, quantity.BaseQuantity.ProfileId);
        Assert.True(long.TryParse(quantity.BaseQuantity.ProfileVersion, out var profileVersion));
        Assert.True(profileVersion > 0);
        Assert.InRange(quantity.BaseQuantity.Scale, 0, PurchaseGoodsReceiptApiV1.MaximumKilogramScale);
        Assert.Null(quantity.AlternateQuantity);
    }

    private static async Task<StoredEffects> StoredEffectsAsync(string connectionString,
        RecordPurchaseGoodsReceiptV1 receipt)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return new(
            await CountAsync(connection, "[inventory].[Lots]", "[Id]", receipt.LotId),
            await CountAsync(connection, "[inventory].[Labels]", "[Id]", receipt.LabelId),
            await CountAsync(connection, "[inventory].[Movements]", "[Id]", receipt.MovementId),
            await CountAsync(connection, "[inventory].[MovementLines]", "[MovementId]", receipt.MovementId),
            await CountAsync(connection, "[purchasing].[PurchaseAcceptanceShares]", "[AcceptanceId]",
                receipt.AcceptanceId));
    }

    private static async Task<int> CountAsync(SqlConnection connection, string table, string column, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT_BIG(*) FROM {table} WHERE {column} = @id;";
        command.Parameters.AddWithValue("@id", id);
        return checked((int)(long)(await command.ExecuteScalarAsync())!);
    }

    private static async Task<string> ReceiptResultJsonAsync(string connectionString, Guid acceptanceId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResultJson FROM inventory.Commands WHERE CompanyId = @company "
            + "AND CommandName = @command AND TargetId = @acceptance;";
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@command", InventoryGoodsReceiptContractV1.CommandName);
        command.Parameters.AddWithValue("@acceptance", acceptanceId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task SetLegacyReceiptQuantityAsync(string connectionString, Guid acceptanceId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE inventory.Commands "
            + "SET ResultJson = JSON_MODIFY(ResultJson, '$.stockItemQuantity', NULL) "
            + "WHERE CompanyId = @company AND CommandName = @command AND TargetId = @acceptance;";
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@command", InventoryGoodsReceiptContractV1.CommandName);
        command.Parameters.AddWithValue("@acceptance", acceptanceId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task RestoreReceiptResultJsonAsync(string connectionString, Guid acceptanceId,
        string resultJson)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE inventory.Commands SET ResultJson = @result "
            + "WHERE CompanyId = @company AND CommandName = @command AND TargetId = @acceptance;";
        command.Parameters.AddWithValue("@result", resultJson);
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@command", InventoryGoodsReceiptContractV1.CommandName);
        command.Parameters.AddWithValue("@acceptance", acceptanceId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DropDatabaseAsync(string baseConnection, string databaseName)
    {
        if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = "master",
        }.ConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;";
        await command.ExecuteNonQueryAsync();
    }

    private sealed record StoredEffects(int Lots, int Labels, int Movements, int MovementLines,
        int PurchaseAcceptances);
}
