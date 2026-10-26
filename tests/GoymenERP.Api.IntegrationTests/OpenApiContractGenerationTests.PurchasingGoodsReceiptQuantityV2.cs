using System.Text.Json;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class OpenApiContractGenerationTests
{
    [Fact]
    public void Purchasing_goods_receipt_quantity_v2_schema_is_closed_required_and_explicitly_nullable()
    {
        var path = Path.Combine(FindRepositoryRoot(), "contracts", "openapi", "goymen-erp-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        AssertComponentSchemaMatrix(schemas,
            new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
            {
                ["PurchaseGoodsReceiptViewV1"] = ObjectSchema(
                    ["companyId", "acceptanceId", "purchaseOrderId", "purchaseOrderLineId",
                        "purchaseOrderVersion", "productId", "productVersion", "unitId",
                        "acceptedQuantity", "remainingQuantity", "lotId", "labelId", "labelCode",
                        "labelVersion", "movementId", "locationId", "measuredQuantityKg",
                        "initialBlock", "occurredAtUtc", "stockItemQuantity"],
                    ("companyId", "type:string;format:uuid"),
                    ("acceptanceId", "type:string;format:uuid"),
                    ("purchaseOrderId", "type:string;format:uuid"),
                    ("purchaseOrderLineId", "type:string;format:uuid"),
                    ("purchaseOrderVersion", "type:string"),
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"),
                    ("unitId", "type:string;format:uuid"),
                    ("acceptedQuantity", "type:string"),
                    ("remainingQuantity", "type:string"),
                    ("lotId", "type:string;format:uuid"),
                    ("labelId", "type:string;format:uuid"),
                    ("labelCode", "type:string"),
                    ("labelVersion", "type:string"),
                    ("movementId", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"),
                    ("measuredQuantityKg", "type:string"),
                    ("initialBlock", "type:string"),
                    ("occurredAtUtc", "type:string;format:date-time"),
                    ("stockItemQuantity", "oneOf:ref:PurchaseGoodsReceiptQuantityV2|type:null")),
                ["PurchaseGoodsReceiptQuantityV2"] = ObjectSchema(
                    ["companyId", "subject", "source", "baseQuantity", "alternateQuantity"],
                    ("companyId", "type:string;format:uuid"),
                    ("subject", "ref:PurchaseGoodsReceiptQuantitySubjectV2"),
                    ("source", "ref:PurchaseGoodsReceiptQuantitySourceV2"),
                    ("baseQuantity", "ref:PurchaseGoodsReceiptQuantityValueV2"),
                    ("alternateQuantity", "oneOf:ref:PurchaseGoodsReceiptAlternateQuantityV2|type:null")),
                ["PurchaseGoodsReceiptQuantitySubjectV2"] = ObjectSchema(
                    ["kind", "id"],
                    ("kind", "type:string"),
                    ("id", "type:string;format:uuid")),
                ["PurchaseGoodsReceiptQuantitySourceV2"] = ObjectSchema(
                    ["kind", "id", "version"],
                    ("kind", "type:string"),
                    ("id", "type:string;format:uuid"),
                    ("version", "type:string")),
                ["PurchaseGoodsReceiptQuantityValueV2"] = ObjectSchema(
                    ["value", "unitId", "profileId", "profileVersion", "dimension", "scale"],
                    ("value", "type:string"),
                    ("unitId", "type:string;format:uuid"),
                    ("profileId", "type:string;format:uuid"),
                    ("profileVersion", "type:string"),
                    ("dimension", "type:string"),
                    ("scale", "type:integer|string;format:int32")),
                ["PurchaseGoodsReceiptAlternateQuantityV2"] = ObjectSchema(
                    ["quantity", "conversionSource"],
                    ("quantity", "ref:PurchaseGoodsReceiptQuantityValueV2"),
                    ("conversionSource", "ref:PurchaseGoodsReceiptQuantitySourceV2")),
            });
    }
}
