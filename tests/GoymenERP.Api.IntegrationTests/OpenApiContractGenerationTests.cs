using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.OpenApi;
using GoymenERP.Api.Contracts;
using GoymenERP.BuildingBlocks.OpenApi;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class OpenApiContractGenerationTests
{
    private const string DocumentName = "v1";
    private const string CanonicalFileName = "openapi.json";

    private static readonly IReadOnlyList<OperationExpectation> ExpectedOperations =
    [
        new("get", "/health/live", "GetLivenessV1",
            ["200:text/plain", "400:application/problem+json", "500:application/problem+json", "503:application/problem+json"]),
        new("get", "/health/ready", "GetReadinessV1",
            ["200:text/plain", "400:application/problem+json", "500:application/problem+json", "503:application/problem+json"]),
        new("get", "/api/v1/audit/events", "GetAuditEventsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json"),
            ParameterValues:
            [
                "query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$;normalized-minimum:1;normalized-maximum:100",
                "query:cursor:false:type:string;canonicalization:trim;canonical-max-length:128",
                "query:correlationId:false:type:string;canonicalization:trim;canonical-max-length:128",
                "query:entityType:false:type:string;canonicalization:trim;canonical-max-length:160",
                "query:entityId:false:type:string;canonicalization:trim;canonical-max-length:128",
            ]),
        new("get", "/api/v1/organization/companies", "GetAuthorizedCompaniesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/organization/context", "GetOrganizationContextV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/organization/sites", "GetOrganizationSitesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/organization/locations", "GetOrganizationLocationsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json"),
            ParameterValues:
            [
                "query:page:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$",
                "query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$",
            ]),
        new("get", "/api/v1/organization/locations/{id}", "GetOrganizationLocationV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/inventory/labels/{labelId}", "GetInventoryLabelV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:labelId:true:type:string;format:uuid"]),
        new("get", "/api/v1/inventory/labels/{labelId}/balances", "GetInventoryLabelBalancesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:labelId:true:type:string;format:uuid"]),
        new("get", "/api/v1/inventory/labels/{labelId}/detail", "GetInventoryLabelDetailV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:labelId:true:type:string;format:uuid"]),
        new("get", "/api/v1/inventory/references/parent-rolls", "GetInventoryParentRollCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string;canonicalization:trim;canonical-max-length:64",
                "query:limit:false:type:integer;format:int32",
            ]),
        new("get", "/api/v1/inventory/positions", "GetInventoryStockPositionsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string;canonicalization:trim;canonical-max-length:64",
                "query:limit:false:type:integer;format:int32",
            ]),
        new("get", "/api/v1/inventory/physical-allocations", "GetInventoryPhysicalAllocationsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:salesOrderId:true:type:string;format:uuid",
                "query:salesOrderLineId:true:type:string;format:uuid",
                "query:limit:false:type:integer;format:int32",
            ]),
        new("get", "/api/v1/inventory/references/products", "GetInventoryProductCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string",
                "query:effectiveOn:true:type:string;format:date",
                "query:limit:false:type:integer;format:int32",
            ]),
        new("get", "/api/v1/inventory/references/locations", "GetInventoryLocationCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string",
                "query:limit:false:type:integer;format:int32",
            ]),
        new("post", "/api/v1/inventory/physical-allocations", "CreateInventoryPhysicalAllocationV1",
            AuthenticatedResponses("201:application/json", "200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/CreateInventoryPhysicalAllocationRequestV1"),
        new("post", "/api/v1/inventory/physical-allocations/{allocationId}/release", "ReleaseInventoryPhysicalAllocationV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/ReleasePhysicalAllocationV1",
            ["path:allocationId:true:type:string;format:uuid"]),
        // INV-OPEN-1 part 2 (T07 = A): record an opening count (ADR-011) and read one back.
        new("post", "/api/v1/inventory/opening-counts", "RecordInventoryOpeningCountV1",
            AuthenticatedResponses("201:application/json", "200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/OpeningCountRequestV1"),
        new("get", "/api/v1/inventory/opening-counts/{countId}", "GetInventoryOpeningCountV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:countId:true:type:string;format:uuid"]),
        // R19 / AT-038: record one label split or merge (ADR-011); its closed codes are the owner's, read below.
        new("post", "/api/v1/inventory/label-transformations", "RecordInventoryLabelTransformationV1",
            AuthenticatedResponses("201:application/json", "200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/TransformInventoryLabelsV1"),
        new("put", "/api/v1/organization/locations/{id}/active-state", "SetOrganizationLocationActiveStateV1",
            AuthenticatedResponses("204:", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/SetLocationActiveState",
            ["path:id:true:type:string;format:uuid"]),
        // ORG master edit: create and edit sites, locations and work centres (the active-state protocol: an
        // Idempotency-Key, expectedVersion on every edit, code-less 404/409, 422 organization_code_in_use).
        new("post", "/api/v1/organization/sites", "CreateOrganizationSiteV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "422:application/problem+json"),
            "#/components/schemas/CreateSite"),
        new("put", "/api/v1/organization/sites/{id}", "UpdateOrganizationSiteV1",
            AuthenticatedResponses("204:", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "422:application/problem+json"),
            "#/components/schemas/UpdateSite",
            ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/organization/locations", "CreateOrganizationLocationV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "422:application/problem+json"),
            "#/components/schemas/CreateLocation"),
        new("put", "/api/v1/organization/locations/{id}", "UpdateOrganizationLocationV1",
            AuthenticatedResponses("204:", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "422:application/problem+json"),
            "#/components/schemas/UpdateLocation",
            ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/organization/work-centers", "CreateOrganizationWorkCenterV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "422:application/problem+json"),
            "#/components/schemas/CreateWorkCenter"),
        new("put", "/api/v1/organization/work-centers/{id}", "UpdateOrganizationWorkCenterV1",
            AuthenticatedResponses("204:", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:", "422:application/problem+json"),
            "#/components/schemas/UpdateWorkCenter",
            ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/organization/work-centers", "GetOrganizationWorkCentersV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        // ORG-04: one work center's card, the location card's shape (code-less 404 for foreign/unknown/out-of-scope).
        new("get", "/api/v1/organization/work-centers/{id}", "GetOrganizationWorkCenterV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/identity/me", "GetCurrentIdentityV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json")),
        new("get", "/api/v1/counterparties", "GetCounterpartiesV1",
            AuthenticatedResponses(
                "200:application/json",
                "400:application/problem+json",
                "409:application/problem+json",
                "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string;canonicalization:unicode-trim+nfc",
                "query:kind:false:type:string;pattern:^(LegalEntity|NaturalPerson)$",
                "query:role:false:type:array;items:type:string",
                "query:roleMatch:false:type:string;pattern:^(any|all)$",
                "query:partyState:false:type:string;pattern:^(active|inactive|all)$",
                "query:accountState:false:type:string;pattern:^(active|inactive|all)$",
                "query:sort:false:type:string;pattern:^(code|legalName|updatedAt)$",
                "query:direction:false:type:string;pattern:^(asc|desc)$",
                "query:pageSize:false:type:integer",
                "query:cursor:false:type:string",
                "query:includeCount:false:type:boolean",
            ]),
        new("get", "/api/v1/counterparties/company-account-link-candidates",
            "GetCounterpartyCompanyAccountLinkCandidatesV1",
            AuthenticatedResponses(
                "200:application/json",
                "400:application/problem+json",
                "409:application/problem+json",
                "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string;canonicalization:unicode-trim+nfc",
                "query:pageSize:false:type:integer",
                "query:cursor:false:type:string",
            ]),
        new("post", "/api/v1/counterparties", "CreateCounterpartyV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/CreateCounterparty"),
        new("get", "/api/v1/counterparties/{partyId}", "GetCounterpartyV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json"),
            ParameterValues:
            [
                "path:partyId:true:type:string;format:uuid",
            ]),
        new("post", "/api/v1/counterparties/{partyId}/company-account", "CreateCounterpartyCompanyAccountV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/CreateCompanyPartyAccount",
            ["path:partyId:true:type:string;format:uuid;non-empty-uuid:true"]),
        new("put", "/api/v1/counterparties/{partyId}/company-account/active-state", "SetCounterpartyCompanyAccountActiveStateV1",
            AuthenticatedResponses("204:", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/SetCompanyPartyAccountActiveState",
            ["path:partyId:true:type:string;format:uuid;non-empty-uuid:true"]),
        new("get", "/api/v1/counterparties/{partyId}/contact-information", "GetCounterpartyContactInformationV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json"),
            ParameterValues: ["path:partyId:true:type:string;format:uuid;non-empty-uuid:true"]),
        new("put", "/api/v1/counterparties/{partyId}/contact-information", "UpdateCounterpartyContactInformationV1",
            AuthenticatedResponses("204:", "400:application/problem+json", "404:application/problem+json", "409:application/problem+json", "415:"),
            "#/components/schemas/UpdateCounterpartyContactInformation",
            ["path:partyId:true:type:string;format:uuid;non-empty-uuid:true"]),
        new("get", "/api/v1/shell/capabilities", "GetShellCapabilitiesV1",
            AuthenticatedResponses(
                "200:application/json",
                "400:application/problem+json",
                "503:application/problem+json")),
        new("get", "/api/v1/finance/manual-receipts/draft-context",
            "GetFinanceManualReceiptDraftContextV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json",
                "404:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:treasuryAccountId:true:type:string;format:uuid",
                "query:partyId:true:type:string;format:uuid",
                "query:companyAccountId:true:type:string;format:uuid",
                "query:occurredOn:true:type:string;format:date",
            ]),
        new("get", "/api/v1/sales/references/accounts", "GetSalesAccountReferencesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json",
                "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string",
                "query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$;normalized-minimum:1;normalized-maximum:100",
                "query:cursor:false:type:string",
            ]),
        new("get", "/api/v1/sales/references/currency", "GetSalesCurrencyReferenceV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json",
                "422:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:currencyId:true:type:string;format:uuid",
                "query:effectiveOn:true:type:string;format:date",
            ]),
        new("get", "/api/v1/sales/references/product", "GetSalesProductReferenceV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json",
                "404:application/problem+json", "409:application/problem+json",
                "422:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:productId:true:type:string;format:uuid",
                "query:expectedProductVersion:true:type:string",
                "query:orderUnitId:true:type:string;format:uuid",
                "query:effectiveOn:true:type:string;format:date",
            ]),
        new("get", "/api/v1/manufacturing/references/output-products",
            "GetManufacturingOutputProductCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json",
                "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string",
                "query:effectiveOn:true:type:string;format:date",
                "query:limit:false:type:integer;format:int32",
            ]),
        new("get", "/api/v1/manufacturing/references/output-locations",
            "GetManufacturingOutputLocationCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json",
                "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string",
                "query:limit:false:type:integer;format:int32",
            ]),
        new("post", "/api/v1/manufacturing/plans", "SaveManufacturingPlanV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "404:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/SavePlanRequestV1"),
        new("get", "/api/v1/manufacturing/plans/{planId}", "GetManufacturingPlanV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json",
                "503:application/problem+json"),
            ParameterValues: ["path:planId:true:type:string;format:uuid"]),
        new("post", "/api/v1/manufacturing/plans/{planId}/approval", "ApproveManufacturingPlanV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/ApprovePlanRequestV1", ["path:planId:true:type:string;format:uuid"]),
        new("post", "/api/v1/manufacturing/orders", "CreateManufacturingOrderV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/CreateOrderRequestV1"),
        new("post", "/api/v1/manufacturing/orders/{orderId}/input-issues", "IssueManufacturingInputV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/IssueInventoryInputRequestV1", ["path:orderId:true:type:string;format:uuid"]),
        new("post", "/api/v1/manufacturing/orders/{orderId}/input-returns", "ReturnManufacturingInputV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/ReturnInventoryInputRequestV1", ["path:orderId:true:type:string;format:uuid"]),
        new("post", "/api/v1/manufacturing/orders/{orderId}/wip-measurements", "RecordManufacturingWorkInProgressV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/RecordFactRequestV1", ["path:orderId:true:type:string;format:uuid"]),
        new("post", "/api/v1/manufacturing/orders/{orderId}/outputs", "DeclareManufacturingOutputV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/DeclareInventoryOutputRequestV1", ["path:orderId:true:type:string;format:uuid"]),
        new("get", "/api/v1/manufacturing/orders/{orderId}", "GetManufacturingOrderV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json",
                "503:application/problem+json"),
            ParameterValues: ["path:orderId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/manual-receipts/drafts", "SaveFinanceManualReceiptDraftV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/DraftSaveRequest"),
        new("post", "/api/v1/finance/manual-bank-payments", "SaveFinanceManualBankPaymentDraftV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/ManualBankPaymentDraftSaveRequest"),
        new("get", "/api/v1/finance/manual-bank-payments", "GetFinanceManualBankPaymentsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:cursor:false:type:string"]),
        new("get", "/api/v1/finance/manual-bank-payments/{paymentId}", "GetFinanceManualBankPaymentV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:paymentId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/manual-bank-payments/{paymentId}/approval", "ApproveFinanceManualBankPaymentV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/ApproveManualBankPaymentRequest",
            ParameterValues: ["path:paymentId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/manual-bank-payments/{paymentId}/record", "RecordFinanceManualBankPaymentV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/RecordManualBankPaymentRequest",
            ParameterValues: ["path:paymentId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/allocations", "CreateFinanceAllocationV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/CreateFinanceAllocationRequest"),
        new("post", "/api/v1/finance/allocations/{allocationId}/release", "ReleaseFinanceAllocationV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/ReleaseFinanceAllocationRequest",
            ParameterValues: ["path:allocationId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/manual-receipts/{sourceId}/record", "RecordFinanceManualReceiptV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/RecordRequest", ["path:sourceId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/manual-receipts/{sourceId}/reversal-proposals",
            "ProposeFinanceManualReceiptReversalV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json",
                "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/ProposeReversalRequest", ["path:sourceId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/manual-receipts/reversal-proposals/{proposalId}/approval",
            "ApproveFinanceManualReceiptReversalV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json",
                "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/ApproveReversalRequest", ["path:proposalId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/manual-receipts/reversal-proposals/{proposalId}/rejection",
            "RejectFinanceManualReceiptReversalV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json",
                "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/RejectReversalRequest", ["path:proposalId:true:type:string;format:uuid"]),
        new("get", "/api/v1/finance/manual-receipts/reversal-proposals/pending",
            "GetPendingFinanceManualReceiptReversalsV1",
            AuthenticatedResponses("200:application/json", "503:application/problem+json")),
        new("get", "/api/v1/finance/manual-receipts/reversal-proposals/{proposalId}",
            "GetFinanceManualReceiptReversalV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json",
                "503:application/problem+json"),
            ParameterValues: ["path:proposalId:true:type:string;format:uuid"]),
        new("get", "/api/v1/finance/manual-receipts", "GetFinanceManualReceiptsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:cursor:false:type:string"]),
        new("get", "/api/v1/finance/manual-receipts/{sourceId}", "GetFinanceManualReceiptV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:sourceId:true:type:string;format:uuid"]),
        new("get", "/api/v1/finance/bank-accounts", "GetFinanceBankAccountsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json")),
        new("get", "/api/v1/sales/drafts", "GetSalesOrderDraftsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string", "query:approvalState:false:type:string"]),
        new("post", "/api/v1/sales/drafts", "CreateSalesOrderDraftV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/CreateSalesOrderDraftRequest"),
        new("get", "/api/v1/sales/drafts/{id}", "GetSalesOrderDraftV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        // SALES-A1: the internal-approval state command (strict {expectedVersion} body, replayable 200/409).
        new("post", "/api/v1/sales/drafts/{id}/submission", "SubmitSalesOrderDraftV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/SalesDraftStateCommandRequest", ["path:id:true:type:string;format:uuid"]),
        // SALES-A1: the internal-approval state command (strict {expectedVersion} body, replayable 200/409).
        new("post", "/api/v1/sales/drafts/{id}/approval", "ApproveSalesOrderDraftV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/SalesDraftStateCommandRequest", ["path:id:true:type:string;format:uuid"]),
        // SALES-A1: the internal-approval state command (strict {expectedVersion} body, replayable 200/409).
        new("post", "/api/v1/sales/drafts/{id}/rejection", "RejectSalesOrderDraftV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/SalesDraftStateCommandRequest", ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/sales/drafts/{id}/withdrawal", "WithdrawSalesOrderDraftV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/SalesDraftStateCommandRequest", ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/sales/drafts/{id}/commercial-revision", "ReviseSalesOrderDraftCommercialV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/SalesDraftCommercialRevisionRequest", ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/sales/drafts/{id}/revisions", "GetSalesOrderDraftRevisionsV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        // DEC-SAL-003: the planner moves the requested delivery dates without re-approval; the history reads every schedule.
        new("post", "/api/v1/sales/drafts/{id}/delivery-schedule", "RescheduleSalesOrderDraftDeliveryV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/SalesDraftDeliveryScheduleRequest", ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/sales/drafts/{id}/delivery-schedules", "GetSalesOrderDraftDeliverySchedulesV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        // AT-024: delivered and remaining quantity per line, read afresh from the delivery owner (Logistics).
        new("get", "/api/v1/sales/drafts/{id}/delivery-progress", "GetSalesOrderDraftDeliveryProgressV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/bank-accounts", "ProposeFinanceBankAccountV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/BankAccountProposeRequest"),
        new("post", "/api/v1/finance/bank-accounts/{accountId}/approval", "ApproveFinanceBankAccountV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/BankAccountApprovalBody", ["path:accountId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/bank-accounts/{accountId}/iban-change", "ProposeFinanceBankAccountIbanChangeV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/BankAccountIbanChangeBody", ["path:accountId:true:type:string;format:uuid"]),
        new("post", "/api/v1/finance/bank-accounts/{accountId}/iban-change/approval", "ApproveFinanceBankAccountIbanChangeV1",
            AuthenticatedResponses("200:application/json", "422:application/json", "409:application/json", "400:application/problem+json", "415:", "503:application/problem+json"),
            "#/components/schemas/BankAccountApprovalBody", ["path:accountId:true:type:string;format:uuid"]),
        new("get", "/api/v1/finance/bank-accounts/master", "GetFinanceBankAccountMasterV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json")),
        new("get", "/api/v1/finance/bank-accounts/currency-candidates", "GetFinanceBankAccountCurrencyCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json")),
        new("get", "/api/v1/logistics/loading-instructions", "GetLoadingInstructionsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string", "query:status:false:type:string"]),
        new("post", "/api/v1/logistics/loading-instructions", "CreateLoadingInstructionV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/CreateLoadingInstructionRequest"),
        new("get", "/api/v1/logistics/loading-instructions/{id}", "GetLoadingInstructionV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/logistics/loading-instructions/{id}/issue", "IssueLoadingInstructionV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/IssueLoadingInstructionRequest", ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/logistics/loading-instructions/{id}/lines/{lineId}/loads", "RecordLoadingInstructionLoadV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/RecordLoadRequest", ["path:id:true:type:string;format:uuid", "path:lineId:true:type:string;format:uuid"]),
        new("post", "/api/v1/logistics/loading-instructions/{id}/lines/{lineId}/shipments", "RecordLoadingInstructionShipmentV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/RecordShipmentRequest", ["path:id:true:type:string;format:uuid", "path:lineId:true:type:string;format:uuid"]),
        new("post", "/api/v1/logistics/loading-instructions/{id}/lines/{lineId}/deliveries", "RecordLoadingInstructionDeliveryV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/RecordDeliveryRequest", ["path:id:true:type:string;format:uuid", "path:lineId:true:type:string;format:uuid"]),
        new("get", "/api/v1/purchasing/requests", "GetPurchaseRequestsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string", "query:status:false:type:string"]),
        new("post", "/api/v1/purchasing/requests", "CreatePurchaseRequestV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/CreatePurchaseRequestRequest"),
        new("get", "/api/v1/purchasing/requests/{id}", "GetPurchaseRequestV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/purchasing/requests/{id}/submission", "SubmitPurchaseRequestV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/SubmitPurchaseRequestRequest", ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/purchasing/orders", "GetPurchaseOrdersV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string", "query:status:false:type:string"]),
        new("post", "/api/v1/purchasing/orders", "CreatePurchaseOrderV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/CreatePurchaseOrderRequest"),
        new("get", "/api/v1/purchasing/orders/{id}", "GetPurchaseOrderV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid"]),
        new("post", "/api/v1/purchasing/orders/{id}/approval", "ApprovePurchaseOrderV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/ApprovePurchaseOrderRequest", ["path:id:true:type:string;format:uuid"]),
        new("get", "/api/v1/purchasing/orders/{id}/goods-receipts", "GetPurchaseGoodsReceiptsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid", "query:limit:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$"]),
        new("get", "/api/v1/purchasing/orders/{id}/lines/{lineId}/goods-receipt-options", "GetPurchaseGoodsReceiptOptionsV1",
            AuthenticatedResponses("200:application/json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:id:true:type:string;format:uuid", "path:lineId:true:type:string;format:uuid"]),
        new("post", "/api/v1/purchasing/orders/{id}/lines/{lineId}/goods-receipts", "RecordPurchaseGoodsReceiptV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/RecordPurchaseGoodsReceiptV1",
            ["path:id:true:type:string;format:uuid", "path:lineId:true:type:string;format:uuid"]),
        new("get", "/api/v1/purchasing/references/products", "GetPurchasingProductCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string",
                "query:effectiveOn:true:type:string;format:date",
                "query:limit:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$",
            ]),
        new("get", "/api/v1/accounting/periods", "GetAccountingPeriodsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string"]),
        new("post", "/api/v1/accounting/periods", "ProposeAccountingPeriodV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/ProposePeriodRequestV1"),
        new("get", "/api/v1/accounting/periods/{periodId}", "GetAccountingPeriodV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:periodId:true:type:string;format:uuid"]),
        new("post", "/api/v1/accounting/periods/{periodId}/approval", "ApproveAccountingPeriodV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/PeriodCommandRequestV1", ["path:periodId:true:type:string;format:uuid"]),
        new("post", "/api/v1/accounting/periods/{periodId}/close", "CloseAccountingPeriodV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/PeriodCommandRequestV1", ["path:periodId:true:type:string;format:uuid"]),
        new("get", "/api/v1/accounting/journals", "GetAccountingJournalsV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:periodId:false:type:string;format:uuid", "query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string"]),
        new("get", "/api/v1/accounting/journals/{journalId}", "GetAccountingJournalV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            ParameterValues: ["path:journalId:true:type:string;format:uuid"]),
        new("get", "/api/v1/accounting/trial-balance", "GetAccountingTrialBalanceV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:fromAccountingDate:true:type:string;format:date", "query:throughAccountingDate:true:type:string;format:date", "query:currencyId:false:type:string;format:uuid"]),
        new("get", "/api/v1/accounting/posting-queues", "GetAccountingPostingQueuesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json")),
        new("post", "/api/v1/accounting/normal-journal-drafts", "CreateAccountingNormalJournalDraftV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/CreateNormalJournalDraftRequestV1"),
        new("get", "/api/v1/accounting/normal-journal-drafts/{draftId}", "GetAccountingNormalJournalDraftV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:draftId:true:type:string;format:uuid"]),
        new("post", "/api/v1/accounting/normal-journal-drafts/{draftId}/approval", "ApproveAccountingNormalJournalDraftV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/ApproveNormalJournalDraftRequestV1", ["path:draftId:true:type:string;format:uuid"]),
        new("post", "/api/v1/accounting/journal-reversal-proposals", "ProposeAccountingJournalReversalV1",
            AuthenticatedResponses("201:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/ProposeAccountingJournalReversalRequestV1"),
        new("post", "/api/v1/accounting/journal-reversal-proposals/{proposalId}/approval", "ApproveAccountingJournalReversalV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "404:application/problem+json", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json"),
            "#/components/schemas/ApproveAccountingJournalReversalRequestV1", ["path:proposalId:true:type:string;format:uuid"]),
        new("get", "/api/v1/manufacturing/orders/{orderId}/mass-balance", "GetManufacturingOrderMassBalanceV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["path:orderId:true:type:string;format:uuid"]),
        new("get", "/api/v1/manufacturing/orders", "ListManufacturingOrdersV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string", "query:status:false:type:string"]),
        // MFG-PLAN-LIST: the order list's rules, the plan read's grant.
        new("get", "/api/v1/manufacturing/plans", "ListManufacturingPlansV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string", "query:status:false:type:string"]),
        new("post", "/api/v1/manufacturing/orders/{orderId}/close", "CloseManufacturingOrderV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "415:", "409:application/problem+json", "422:application/problem+json", "503:application/problem+json", "404:application/problem+json"),
            "#/components/schemas/CloseOrderRequestV1", ["path:orderId:true:type:string;format:uuid"]),
        new("get", "/api/v1/logistics/loading-instructions/approved-order-lines", "GetLogisticsApprovedOrderLinesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:pageSize:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$", "query:cursor:false:type:string"]),
        new("get", "/api/v1/sales/references/contact-information", "GetSalesContactInformationReferencesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "404:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:partyId:true:type:string;format:uuid", "query:companyAccountId:true:type:string;format:uuid"]),
        new("get", "/api/v1/sales/references/currencies", "GetSalesCurrencyReferencesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues: ["query:effectiveOn:true:type:string;format:date"]),
        new("get", "/api/v1/sales/references/products", "GetSalesProductCandidatesV1",
            AuthenticatedResponses("200:application/json", "400:application/problem+json", "503:application/problem+json"),
            ParameterValues:
            [
                "query:q:false:type:string",
                "query:effectiveOn:true:type:string;format:date",
                "query:limit:false:type:integer|string;format:int32;pattern:^-?(?:0|[1-9]\\d*)$",
            ]),
        .. ProductOperations,
    ];

    private static readonly IReadOnlyDictionary<string, string> ExpectedSuccessSchemas =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GetLivenessV1"] = "type:string",
            ["GetReadinessV1"] = "type:string",
            ["GetAuditEventsV1"] = "ref:AuditEventPage",
            ["GetAuthorizedCompaniesV1"] = "array:CompanySummary",
            ["GetOrganizationContextV1"] = "ref:CompanyContextResponse",
            ["GetOrganizationSitesV1"] = "array:SiteSummary",
            ["GetOrganizationLocationsV1"] = "ref:PageResultOfLocationSummary",
            ["GetOrganizationLocationV1"] = "ref:LocationSummary",
            ["GetInventoryLabelV1"] = "ref:InventoryLabelViewV1",
            ["GetInventoryLabelBalancesV1"] = "ref:InventoryLabelBalancesV1",
            ["GetInventoryLabelDetailV1"] = "ref:InventoryLabelDetailV1",
            ["GetInventoryParentRollCandidatesV1"] = "ref:InventoryParentRollCandidatesV1",
            ["GetInventoryStockPositionsV1"] = "ref:InventoryStockPositionsV1",
            ["GetInventoryPhysicalAllocationsV1"] = "ref:InventoryPhysicalAllocationReadsV1",
            ["GetInventoryProductCandidatesV1"] = "ref:ProductStockCandidatesV1",
            ["GetInventoryLocationCandidatesV1"] = "ref:OrganizationLocationCandidatesV1",
            ["CreateInventoryPhysicalAllocationV1"] = "ref:InventoryPhysicalAllocationViewV1",
            ["ReleaseInventoryPhysicalAllocationV1"] = "ref:InventoryPhysicalAllocationViewV1",
            ["RecordInventoryOpeningCountV1"] = "ref:OpeningCountViewV1",
            ["GetInventoryOpeningCountV1"] = "ref:OpeningCountViewV1",
            ["RecordInventoryLabelTransformationV1"] = "ref:InventoryLabelTransformationViewV1",
            ["SetOrganizationLocationActiveStateV1"] = "none",
            ["CreateOrganizationSiteV1"] = "ref:OrganizationCreatedRecord",
            ["UpdateOrganizationSiteV1"] = "none",
            ["CreateOrganizationLocationV1"] = "ref:OrganizationCreatedRecord",
            ["UpdateOrganizationLocationV1"] = "none",
            ["CreateOrganizationWorkCenterV1"] = "ref:OrganizationCreatedRecord",
            ["UpdateOrganizationWorkCenterV1"] = "none",
            ["GetOrganizationWorkCentersV1"] = "array:WorkCenterSummary",
            ["GetOrganizationWorkCenterV1"] = "ref:WorkCenterDetail",
            ["GetCurrentIdentityV1"] = "ref:IdentityProfile",
            ["GetCounterpartiesV1"] = "ref:CounterpartyPage",
            ["GetCounterpartyCompanyAccountLinkCandidatesV1"] =
                "ref:CounterpartyLinkCandidatePage",
            ["CreateCounterpartyV1"] = "ref:CounterpartyMutationView",
            ["GetCounterpartyV1"] = "ref:CounterpartyDetailView",
            ["CreateCounterpartyCompanyAccountV1"] = "ref:CounterpartyMutationView",
            ["SetCounterpartyCompanyAccountActiveStateV1"] = "none",
            ["GetCounterpartyContactInformationV1"] = "ref:CounterpartyContactInformationView",
            ["UpdateCounterpartyContactInformationV1"] = "none",
            ["GetShellCapabilitiesV1"] = "ref:ShellCapabilitySnapshot",
            ["GetFinanceManualReceiptDraftContextV1"] = "ref:DraftReadContextView",
            ["GetSalesAccountReferencesV1"] = "ref:SalesAccountOptionsV1",
            ["GetSalesCurrencyReferenceV1"] = "ref:SalesCurrencyReferenceV1",
            ["GetSalesProductReferenceV1"] = "ref:SalesProductReferenceV1",
            ["GetSalesProductCandidatesV1"] = "ref:ProductSalesCandidatesV1",
            ["GetManufacturingOutputProductCandidatesV1"] =
                "ref:ManufacturingOutputProductCandidatesV1",
            ["GetManufacturingOutputLocationCandidatesV1"] =
                "ref:ManufacturingOutputLocationCandidatesV1",
            ["SaveManufacturingPlanV1"] = "ref:PlanViewV1",
            ["GetManufacturingPlanV1"] = "ref:PlanViewV1",
            ["ApproveManufacturingPlanV1"] = "ref:PlanViewV1",
            ["CreateManufacturingOrderV1"] = "ref:OrderViewV1",
            ["IssueManufacturingInputV1"] = "ref:OrderViewV1",
            ["ReturnManufacturingInputV1"] = "ref:OrderViewV1",
            ["RecordManufacturingWorkInProgressV1"] = "ref:OrderViewV1",
            ["DeclareManufacturingOutputV1"] = "ref:OrderViewV1",
            ["GetManufacturingOrderV1"] = "ref:OrderViewV1",
            ["SaveFinanceManualReceiptDraftV1"] = "ref:CommandResult",
            ["RecordFinanceManualReceiptV1"] = "ref:CommandResult",
            ["SaveFinanceManualBankPaymentDraftV1"] = "ref:ManualBankPaymentCommandResult",
            ["GetFinanceManualBankPaymentsV1"] = "ref:ManualBankPaymentPage",
            ["GetFinanceManualBankPaymentV1"] = "ref:ManualBankPaymentView",
            ["ApproveFinanceManualBankPaymentV1"] = "ref:ManualBankPaymentCommandResult",
            ["RecordFinanceManualBankPaymentV1"] = "ref:ManualBankPaymentCommandResult",
            ["CreateFinanceAllocationV1"] = "ref:FinanceAllocationCommandResult",
            ["ReleaseFinanceAllocationV1"] = "ref:FinanceAllocationCommandResult",
            ["ProposeFinanceManualReceiptReversalV1"] = "ref:ReversalProposalCommandResult",
            ["ApproveFinanceManualReceiptReversalV1"] = "ref:ReversalProposalCommandResult",
            ["RejectFinanceManualReceiptReversalV1"] = "ref:ReversalProposalCommandResult",
            ["GetPendingFinanceManualReceiptReversalsV1"] = "ref:ReversalProposalPage",
            ["GetFinanceManualReceiptReversalV1"] = "ref:ReversalProposalView",
            ["GetFinanceManualReceiptsV1"] = "ref:SourcePage",
            ["GetFinanceManualReceiptV1"] = "ref:SourceDetail",
            ["GetFinanceBankAccountsV1"] = "ref:BankAccountPage",
            ["GetSalesOrderDraftsV1"] = "ref:SalesOrderDraftListResponse",
            ["CreateSalesOrderDraftV1"] = "ref:SalesOrderDraftResponse",
            ["GetSalesOrderDraftV1"] = "ref:SalesOrderDraftDetailView",
            ["SubmitSalesOrderDraftV1"] = "ref:SalesOrderDraftResponse",
            ["ApproveSalesOrderDraftV1"] = "ref:SalesOrderDraftResponse",
            ["RejectSalesOrderDraftV1"] = "ref:SalesOrderDraftResponse",
            ["WithdrawSalesOrderDraftV1"] = "ref:SalesOrderDraftResponse",
            ["ReviseSalesOrderDraftCommercialV1"] = "ref:SalesOrderDraftResponse",
            ["GetSalesOrderDraftRevisionsV1"] = "ref:SalesOrderDraftRevisionsResponse",
            ["RescheduleSalesOrderDraftDeliveryV1"] = "ref:SalesOrderDraftResponse",
            ["GetSalesOrderDraftDeliverySchedulesV1"] = "ref:SalesOrderDraftDeliverySchedulesResponse",
            ["GetSalesOrderDraftDeliveryProgressV1"] = "ref:SalesOrderDraftDeliveryProgressResponse",
            ["ProposeFinanceBankAccountV1"] = "ref:BankAccountCommandResult",
            ["ApproveFinanceBankAccountV1"] = "ref:BankAccountCommandResult",
            ["ProposeFinanceBankAccountIbanChangeV1"] = "ref:BankAccountCommandResult",
            ["ApproveFinanceBankAccountIbanChangeV1"] = "ref:BankAccountCommandResult",
            ["GetFinanceBankAccountMasterV1"] = "ref:BankAccountMasterPage",
            ["GetFinanceBankAccountCurrencyCandidatesV1"] = "ref:BankAccountCurrencyCandidates",
            ["GetLoadingInstructionsV1"] = "ref:LoadingInstructionListResponse",
            ["CreateLoadingInstructionV1"] = "ref:LoadingInstructionResponse",
            ["GetLoadingInstructionV1"] = "ref:LoadingInstructionDetailView",
            ["IssueLoadingInstructionV1"] = "ref:LoadingInstructionResponse",
            ["RecordLoadingInstructionLoadV1"] = "ref:LoadingInstructionResponse",
            ["RecordLoadingInstructionShipmentV1"] = "ref:LoadingInstructionResponse",
            ["RecordLoadingInstructionDeliveryV1"] = "ref:LoadingInstructionResponse",
            ["GetPurchaseRequestsV1"] = "ref:PurchaseRequestListResponse",
            ["CreatePurchaseRequestV1"] = "ref:PurchaseRequestResponse",
            ["GetPurchaseRequestV1"] = "ref:PurchaseRequestDetailView",
            ["SubmitPurchaseRequestV1"] = "ref:PurchaseRequestResponse",
            ["GetPurchaseOrdersV1"] = "ref:PurchaseOrderListResponse",
            ["CreatePurchaseOrderV1"] = "ref:PurchaseOrderResponse",
            ["GetPurchaseOrderV1"] = "ref:PurchaseOrderDetailView",
            ["ApprovePurchaseOrderV1"] = "ref:PurchaseOrderResponse",
            ["GetPurchaseGoodsReceiptsV1"] = "ref:PurchaseGoodsReceiptPageV1",
            ["GetPurchaseGoodsReceiptOptionsV1"] = "ref:PurchaseGoodsReceiptOptionsV1",
            ["RecordPurchaseGoodsReceiptV1"] = "ref:PurchaseGoodsReceiptResponseV1",
            ["GetPurchasingProductCandidatesV1"] = "ref:ProductPurchaseCandidatesV1",
            ["GetAccountingPeriodsV1"] = "ref:PeriodPageV1",
            ["ProposeAccountingPeriodV1"] = "ref:PeriodViewV1",
            ["GetAccountingPeriodV1"] = "ref:PeriodViewV1",
            ["ApproveAccountingPeriodV1"] = "ref:PeriodViewV1",
            ["CloseAccountingPeriodV1"] = "ref:PeriodViewV1",
            ["GetAccountingJournalsV1"] = "ref:JournalPageV1",
            ["GetAccountingJournalV1"] = "ref:JournalDetailV1",
            ["GetAccountingTrialBalanceV1"] = "ref:TrialBalanceReportV1",
            ["GetAccountingPostingQueuesV1"] = "ref:PostingQueuesV1",
            ["CreateAccountingNormalJournalDraftV1"] = "ref:NormalJournalDraftViewV1",
            ["GetAccountingNormalJournalDraftV1"] = "ref:NormalJournalDraftViewV1",
            ["ApproveAccountingNormalJournalDraftV1"] = "ref:NormalJournalDraftViewV1",
            ["ProposeAccountingJournalReversalV1"] = "ref:AccountingJournalReversalProposalViewV1",
            ["ApproveAccountingJournalReversalV1"] = "ref:AccountingJournalReversalProposalViewV1",
            ["GetManufacturingOrderMassBalanceV1"] = "ref:MassBalanceViewV1",
            ["ListManufacturingOrdersV1"] = "ref:OrderListViewV1",
            ["ListManufacturingPlansV1"] = "ref:PlanListViewV1",
            ["CloseManufacturingOrderV1"] = "ref:OrderViewV1",
            ["GetLogisticsApprovedOrderLinesV1"] = "ref:LogisticsApprovedOrderLinePage",
            ["GetSalesContactInformationReferencesV1"] = "ref:SalesContactInformationOptionsV1",
            ["GetSalesCurrencyReferencesV1"] = "ref:SalesCurrencyOptionsV1",
        };

    private static readonly HashSet<string> RequiredCompanyOperations =
    [
        "UpdateCounterpartyContactInformationV1",
        "GetCounterpartyContactInformationV1",
        "GetAuditEventsV1",
        "GetOrganizationContextV1",
        "GetOrganizationSitesV1",
        "GetOrganizationLocationsV1",
        "GetOrganizationLocationV1",
        "SetOrganizationLocationActiveStateV1",
        "CreateOrganizationSiteV1",
        "UpdateOrganizationSiteV1",
        "CreateOrganizationLocationV1",
        "UpdateOrganizationLocationV1",
        "CreateOrganizationWorkCenterV1",
        "UpdateOrganizationWorkCenterV1",
        "GetOrganizationWorkCentersV1",
        "GetOrganizationWorkCenterV1",
        "GetInventoryLabelV1",
        "GetInventoryLabelBalancesV1",
        "GetInventoryLabelDetailV1",
        "GetInventoryParentRollCandidatesV1",
        "GetInventoryStockPositionsV1",
        "GetInventoryPhysicalAllocationsV1",
        "GetInventoryProductCandidatesV1",
        "GetInventoryLocationCandidatesV1",
        "CreateInventoryPhysicalAllocationV1",
        "ReleaseInventoryPhysicalAllocationV1",
        "RecordInventoryOpeningCountV1",
        "GetInventoryOpeningCountV1",
        "RecordInventoryLabelTransformationV1",
        "GetCounterpartyCompanyAccountLinkCandidatesV1",
        "CreateCounterpartyV1",
        "CreateCounterpartyCompanyAccountV1",
        "SetCounterpartyCompanyAccountActiveStateV1",
        "GetFinanceManualReceiptDraftContextV1",
        "SaveFinanceManualBankPaymentDraftV1",
        "GetFinanceManualBankPaymentsV1",
        "GetFinanceManualBankPaymentV1",
        "ApproveFinanceManualBankPaymentV1",
        "RecordFinanceManualBankPaymentV1",
        "CreateFinanceAllocationV1",
        "ReleaseFinanceAllocationV1",
        "GetSalesAccountReferencesV1",
        "GetSalesCurrencyReferenceV1",
        "GetSalesProductReferenceV1",
        "GetSalesProductCandidatesV1",
        "GetManufacturingOutputProductCandidatesV1",
        "GetManufacturingOutputLocationCandidatesV1",
        "SaveManufacturingPlanV1",
        "GetManufacturingPlanV1",
        "ApproveManufacturingPlanV1",
        "CreateManufacturingOrderV1",
        "IssueManufacturingInputV1",
        "ReturnManufacturingInputV1",
        "RecordManufacturingWorkInProgressV1",
        "DeclareManufacturingOutputV1",
        "GetManufacturingOrderV1",
        "SaveFinanceManualReceiptDraftV1",
        "RecordFinanceManualReceiptV1",
        "ProposeFinanceManualReceiptReversalV1",
        "ApproveFinanceManualReceiptReversalV1",
        "RejectFinanceManualReceiptReversalV1",
        "GetPendingFinanceManualReceiptReversalsV1",
        "GetFinanceManualReceiptReversalV1",
        "GetFinanceManualReceiptsV1",
        "GetFinanceManualReceiptV1",
        "GetFinanceBankAccountsV1",
        "GetSalesOrderDraftsV1",
        "CreateSalesOrderDraftV1",
        "GetSalesOrderDraftV1",
        "SubmitSalesOrderDraftV1",
        "ApproveSalesOrderDraftV1",
        "RejectSalesOrderDraftV1",
        "WithdrawSalesOrderDraftV1",
        "ReviseSalesOrderDraftCommercialV1",
        "GetSalesOrderDraftRevisionsV1",
        "RescheduleSalesOrderDraftDeliveryV1",
        "GetSalesOrderDraftDeliverySchedulesV1",
        "GetSalesOrderDraftDeliveryProgressV1",
        "ProposeFinanceBankAccountV1",
        "ApproveFinanceBankAccountV1",
        "ProposeFinanceBankAccountIbanChangeV1",
        "ApproveFinanceBankAccountIbanChangeV1",
        "GetFinanceBankAccountMasterV1",
        "GetFinanceBankAccountCurrencyCandidatesV1",
        "GetLoadingInstructionsV1",
        "CreateLoadingInstructionV1",
        "GetLoadingInstructionV1",
        "IssueLoadingInstructionV1",
        "RecordLoadingInstructionLoadV1",
        "RecordLoadingInstructionShipmentV1",
        "RecordLoadingInstructionDeliveryV1",
        "GetPurchaseRequestsV1",
        "CreatePurchaseRequestV1",
        "GetPurchaseRequestV1",
        "SubmitPurchaseRequestV1",
        "GetPurchaseOrdersV1",
        "CreatePurchaseOrderV1",
        "GetPurchaseOrderV1",
        "ApprovePurchaseOrderV1",
        "GetPurchaseGoodsReceiptsV1",
        "GetPurchaseGoodsReceiptOptionsV1",
        "RecordPurchaseGoodsReceiptV1",
        "GetPurchasingProductCandidatesV1",
        "GetAccountingPeriodsV1",
        "ProposeAccountingPeriodV1",
        "GetAccountingPeriodV1",
        "ApproveAccountingPeriodV1",
        "CloseAccountingPeriodV1",
        "GetAccountingJournalsV1",
        "GetAccountingJournalV1",
        "GetAccountingTrialBalanceV1",
        "GetAccountingPostingQueuesV1",
        "CreateAccountingNormalJournalDraftV1",
        "GetAccountingNormalJournalDraftV1",
        "ApproveAccountingNormalJournalDraftV1",
        "ProposeAccountingJournalReversalV1",
        "ApproveAccountingJournalReversalV1",
        "GetManufacturingOrderMassBalanceV1",
        "ListManufacturingOrdersV1",
        "ListManufacturingPlansV1",
        "CloseManufacturingOrderV1",
        "GetLogisticsApprovedOrderLinesV1",
        "GetSalesContactInformationReferencesV1",
        "GetSalesCurrencyReferencesV1",
    ];

    private static readonly HashSet<string> OptionalCompanyOperations =
    [
        "GetCounterpartiesV1",
        "GetCounterpartyV1",
        "GetShellCapabilitiesV1",
    ];

    private static readonly HashSet<string> IdempotentOperations =
    [
        "RecordInventoryOpeningCountV1",
        "CreateInventoryPhysicalAllocationV1",
        "ReleaseInventoryPhysicalAllocationV1",
        "RecordInventoryLabelTransformationV1",
        "UpdateCounterpartyContactInformationV1",
        "SetOrganizationLocationActiveStateV1",
        "CreateOrganizationSiteV1",
        "UpdateOrganizationSiteV1",
        "CreateOrganizationLocationV1",
        "UpdateOrganizationLocationV1",
        "CreateOrganizationWorkCenterV1",
        "UpdateOrganizationWorkCenterV1",
        "CreateCounterpartyV1",
        "CreateCounterpartyCompanyAccountV1",
        "SetCounterpartyCompanyAccountActiveStateV1",
        "SaveManufacturingPlanV1",
        "ApproveManufacturingPlanV1",
        "CreateManufacturingOrderV1",
        "IssueManufacturingInputV1",
        "ReturnManufacturingInputV1",
        "RecordManufacturingWorkInProgressV1",
        "DeclareManufacturingOutputV1",
        "SaveFinanceManualReceiptDraftV1",
        "RecordFinanceManualReceiptV1",
        "SaveFinanceManualBankPaymentDraftV1",
        "ApproveFinanceManualBankPaymentV1",
        "RecordFinanceManualBankPaymentV1",
        "CreateFinanceAllocationV1",
        "ReleaseFinanceAllocationV1",
        "ProposeFinanceManualReceiptReversalV1",
        "ApproveFinanceManualReceiptReversalV1",
        "RejectFinanceManualReceiptReversalV1",
        "CreateSalesOrderDraftV1",
        "SubmitSalesOrderDraftV1",
        "ApproveSalesOrderDraftV1",
        "RejectSalesOrderDraftV1",
        "WithdrawSalesOrderDraftV1",
        "ReviseSalesOrderDraftCommercialV1",
        "RescheduleSalesOrderDraftDeliveryV1",
        "ProposeFinanceBankAccountV1",
        "ApproveFinanceBankAccountV1",
        "ProposeFinanceBankAccountIbanChangeV1",
        "ApproveFinanceBankAccountIbanChangeV1",
        "CreateLoadingInstructionV1",
        "IssueLoadingInstructionV1",
        "RecordLoadingInstructionLoadV1",
        "RecordLoadingInstructionShipmentV1",
        "RecordLoadingInstructionDeliveryV1",
        "CreatePurchaseRequestV1",
        "SubmitPurchaseRequestV1",
        "CreatePurchaseOrderV1",
        "ApprovePurchaseOrderV1",
        "RecordPurchaseGoodsReceiptV1",
        "ProposeAccountingPeriodV1",
        "ApproveAccountingPeriodV1",
        "CloseAccountingPeriodV1",
        "CreateAccountingNormalJournalDraftV1",
        "ApproveAccountingNormalJournalDraftV1",
        "ProposeAccountingJournalReversalV1",
        "ApproveAccountingJournalReversalV1",
        "CloseManufacturingOrderV1",
    ];

    private static readonly IReadOnlyDictionary<string, HashSet<int>>
        ReplayableResponseStatuses =
            new Dictionary<string, HashSet<int>>(StringComparer.Ordinal)
            {
                ["SetOrganizationLocationActiveStateV1"] =
                [
                    StatusCodes.Status204NoContent,
                    StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict,
                ],
                // ORG master edit: every final answer is stored and replayed, as the active-state command's.
                ["CreateOrganizationSiteV1"] =
                [StatusCodes.Status201Created, StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity],
                ["UpdateOrganizationSiteV1"] =
                [StatusCodes.Status204NoContent, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity],
                ["CreateOrganizationLocationV1"] =
                [StatusCodes.Status201Created, StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity],
                ["UpdateOrganizationLocationV1"] =
                [StatusCodes.Status204NoContent, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity],
                ["CreateOrganizationWorkCenterV1"] =
                [StatusCodes.Status201Created, StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity],
                ["UpdateOrganizationWorkCenterV1"] =
                [StatusCodes.Status204NoContent, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity],
                ["UpdateCounterpartyContactInformationV1"] =
                [StatusCodes.Status204NoContent, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["CreateCounterpartyV1"] = [StatusCodes.Status201Created],
                ["CreateCounterpartyCompanyAccountV1"] =
                [
                    StatusCodes.Status201Created,
                    StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict,
                ],
                ["SetCounterpartyCompanyAccountActiveStateV1"] =
                [
                    StatusCodes.Status204NoContent,
                    StatusCodes.Status404NotFound,
                    StatusCodes.Status409Conflict,
                ],
                ["SaveManufacturingPlanV1"] =
                    [StatusCodes.Status201Created, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["ApproveManufacturingPlanV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["CreateManufacturingOrderV1"] = [StatusCodes.Status201Created, StatusCodes.Status404NotFound],
                ["IssueManufacturingInputV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["ReturnManufacturingInputV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["RecordManufacturingWorkInProgressV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["DeclareManufacturingOutputV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                // ADR-011 §7 (#325): a recorded 409/422 terminal refusal is replayed too.
                ["SaveFinanceManualReceiptDraftV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                // FIN-T05: Record replays the same three — the recorded terminal refusals as well as
                // the success, so a retried key never records a second money event (ADR-011 §7).
                ["RecordFinanceManualReceiptV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["SaveFinanceManualBankPaymentDraftV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["ApproveFinanceManualBankPaymentV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["RecordFinanceManualBankPaymentV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["CreateFinanceAllocationV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["ReleaseFinanceAllocationV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["ProposeFinanceManualReceiptReversalV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["ApproveFinanceManualReceiptReversalV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["RejectFinanceManualReceiptReversalV1"] =
                [
                    StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity,
                ],
                ["CreateSalesOrderDraftV1"] = [StatusCodes.Status201Created, StatusCodes.Status409Conflict],
                ["SubmitSalesOrderDraftV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["ApproveSalesOrderDraftV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["RejectSalesOrderDraftV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["WithdrawSalesOrderDraftV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["ReviseSalesOrderDraftCommercialV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["RescheduleSalesOrderDraftDeliveryV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["ProposeFinanceBankAccountV1"] = [StatusCodes.Status200OK],
                ["ApproveFinanceBankAccountV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity],
                ["ProposeFinanceBankAccountIbanChangeV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity],
                ["ApproveFinanceBankAccountIbanChangeV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity],
                // INV-OPEN-1 part 2: the count (200 on a replay) and the recorded terminal refusals (§7).
                ["RecordInventoryOpeningCountV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity],
                ["CreateInventoryPhysicalAllocationV1"] = [StatusCodes.Status200OK],
                ["ReleaseInventoryPhysicalAllocationV1"] = [StatusCodes.Status200OK],
                ["RecordInventoryLabelTransformationV1"] = [StatusCodes.Status200OK],
                ["CreateLoadingInstructionV1"] = [StatusCodes.Status201Created, StatusCodes.Status409Conflict],
                ["IssueLoadingInstructionV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["RecordLoadingInstructionLoadV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["RecordLoadingInstructionShipmentV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["RecordLoadingInstructionDeliveryV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["CreatePurchaseRequestV1"] = [StatusCodes.Status201Created, StatusCodes.Status409Conflict],
                ["SubmitPurchaseRequestV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["CreatePurchaseOrderV1"] = [StatusCodes.Status201Created, StatusCodes.Status409Conflict],
                ["ApprovePurchaseOrderV1"] = [StatusCodes.Status200OK, StatusCodes.Status409Conflict],
                ["RecordPurchaseGoodsReceiptV1"] = [StatusCodes.Status201Created, StatusCodes.Status409Conflict],
                ["ProposeAccountingPeriodV1"] = [StatusCodes.Status201Created],
                ["ApproveAccountingPeriodV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["CloseAccountingPeriodV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["CreateAccountingNormalJournalDraftV1"] = [StatusCodes.Status201Created],
                ["ApproveAccountingNormalJournalDraftV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["ProposeAccountingJournalReversalV1"] = [StatusCodes.Status201Created],
                ["ApproveAccountingJournalReversalV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
                ["CloseManufacturingOrderV1"] = [StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict],
            };

    private static readonly HashSet<string> EmptyBodyPossible400Operations =
    [
        "CreateInventoryPhysicalAllocationV1",
        "ReleaseInventoryPhysicalAllocationV1",
        "UpdateCounterpartyContactInformationV1",
        "GetAuditEventsV1",
        "RecordInventoryLabelTransformationV1",
        "GetOrganizationLocationsV1",
        "SetOrganizationLocationActiveStateV1",
        "CreateOrganizationSiteV1",
        "UpdateOrganizationSiteV1",
        "CreateOrganizationLocationV1",
        "UpdateOrganizationLocationV1",
        "CreateOrganizationWorkCenterV1",
        "UpdateOrganizationWorkCenterV1",
        "CreateCounterpartyV1",
        "CreateCounterpartyCompanyAccountV1",
        "SetCounterpartyCompanyAccountActiveStateV1",
        "GetFinanceManualReceiptDraftContextV1",
        "GetSalesAccountReferencesV1",
        "GetSalesCurrencyReferenceV1",
        "GetSalesProductReferenceV1",
        "GetSalesProductCandidatesV1",
        "SaveManufacturingPlanV1",
        "ApproveManufacturingPlanV1",
        "CreateManufacturingOrderV1",
        "IssueManufacturingInputV1",
        "ReturnManufacturingInputV1",
        "RecordManufacturingWorkInProgressV1",
        "DeclareManufacturingOutputV1",
        "SaveFinanceManualReceiptDraftV1",
        "RecordFinanceManualReceiptV1",
        "SaveFinanceManualBankPaymentDraftV1",
        "GetFinanceManualBankPaymentsV1",
        "ApproveFinanceManualBankPaymentV1",
        "RecordFinanceManualBankPaymentV1",
        "CreateFinanceAllocationV1",
        "ReleaseFinanceAllocationV1",
        "ProposeFinanceManualReceiptReversalV1",
        "ApproveFinanceManualReceiptReversalV1",
        "RejectFinanceManualReceiptReversalV1",
        "GetPendingFinanceManualReceiptReversalsV1",
        "GetFinanceManualReceiptReversalV1",
        "GetFinanceManualReceiptsV1",
        "GetFinanceManualReceiptV1",
        "GetFinanceBankAccountsV1",
        "GetSalesOrderDraftsV1",
        "CreateSalesOrderDraftV1",
        "SubmitSalesOrderDraftV1",
        "ApproveSalesOrderDraftV1",
        "RejectSalesOrderDraftV1",
        "WithdrawSalesOrderDraftV1",
        "ReviseSalesOrderDraftCommercialV1",
        "GetSalesOrderDraftRevisionsV1",
        "RescheduleSalesOrderDraftDeliveryV1",
        "GetSalesOrderDraftDeliverySchedulesV1",
        "GetSalesOrderDraftDeliveryProgressV1",
        "ProposeFinanceBankAccountV1",
        "ApproveFinanceBankAccountV1",
        "ProposeFinanceBankAccountIbanChangeV1",
        "ApproveFinanceBankAccountIbanChangeV1",
        "GetFinanceBankAccountMasterV1",
        "GetFinanceBankAccountCurrencyCandidatesV1",
        "RecordInventoryOpeningCountV1",
        "GetLoadingInstructionsV1",
        "CreateLoadingInstructionV1",
        "IssueLoadingInstructionV1",
        "RecordLoadingInstructionLoadV1",
        "RecordLoadingInstructionShipmentV1",
        "RecordLoadingInstructionDeliveryV1",
        "GetPurchaseRequestsV1",
        "CreatePurchaseRequestV1",
        "SubmitPurchaseRequestV1",
        "GetPurchaseOrdersV1",
        "CreatePurchaseOrderV1",
        "ApprovePurchaseOrderV1",
        "RecordPurchaseGoodsReceiptV1",
        "GetPurchasingProductCandidatesV1",
        "ProposeAccountingPeriodV1",
        "ApproveAccountingPeriodV1",
        "CloseAccountingPeriodV1",
        "CreateAccountingNormalJournalDraftV1",
        "ApproveAccountingNormalJournalDraftV1",
        "ProposeAccountingJournalReversalV1",
        "ApproveAccountingJournalReversalV1",
        "CloseManufacturingOrderV1",
        "GetLogisticsApprovedOrderLinesV1",
        "GetSalesContactInformationReferencesV1",
        "GetSalesCurrencyReferencesV1",
    ];

    private static readonly HashSet<string> CodeLessProblemResponses =
    [
        "GetOrganizationLocationV1:404",
        "GetOrganizationWorkCenterV1:404",
        "GetInventoryLabelV1:404",
        "GetInventoryLabelBalancesV1:404",
        "GetInventoryLabelDetailV1:404",
        "GetInventoryOpeningCountV1:404",
        "CreateInventoryPhysicalAllocationV1:404",
        "ReleaseInventoryPhysicalAllocationV1:404",
        "SetOrganizationLocationActiveStateV1:404",
        "SetOrganizationLocationActiveStateV1:409",
        "CreateOrganizationSiteV1:404",
        "CreateOrganizationSiteV1:409",
        "UpdateOrganizationSiteV1:404",
        "UpdateOrganizationSiteV1:409",
        "CreateOrganizationLocationV1:404",
        "CreateOrganizationLocationV1:409",
        "UpdateOrganizationLocationV1:404",
        "UpdateOrganizationLocationV1:409",
        "CreateOrganizationWorkCenterV1:404",
        "CreateOrganizationWorkCenterV1:409",
        "UpdateOrganizationWorkCenterV1:404",
        "UpdateOrganizationWorkCenterV1:409",
        "GetFinanceManualReceiptDraftContextV1:404",
        "GetSalesProductReferenceV1:404",
        "SaveManufacturingPlanV1:404",
        "GetManufacturingPlanV1:404",
        "ApproveManufacturingPlanV1:404",
        "CreateManufacturingOrderV1:404",
        "IssueManufacturingInputV1:404",
        "ReturnManufacturingInputV1:404",
        "RecordManufacturingWorkInProgressV1:404",
        "DeclareManufacturingOutputV1:404",
        "GetManufacturingOrderV1:404",
        "GetFinanceManualReceiptV1:404",
        "GetFinanceManualBankPaymentV1:404",
        "GetFinanceManualReceiptReversalV1:404",
        "GetSalesOrderDraftV1:404",
        "SubmitSalesOrderDraftV1:404",
        "ApproveSalesOrderDraftV1:404",
        "RejectSalesOrderDraftV1:404",
        "WithdrawSalesOrderDraftV1:404",
        "ReviseSalesOrderDraftCommercialV1:404",
        "GetSalesOrderDraftRevisionsV1:404",
        "RescheduleSalesOrderDraftDeliveryV1:404",
        "GetSalesOrderDraftDeliverySchedulesV1:404",
        "GetSalesOrderDraftDeliveryProgressV1:404",
        "GetLoadingInstructionV1:404",
        "IssueLoadingInstructionV1:404",
        "RecordLoadingInstructionLoadV1:404",
        "RecordLoadingInstructionShipmentV1:404",
        "RecordLoadingInstructionDeliveryV1:404",
        "GetPurchaseRequestV1:404",
        "SubmitPurchaseRequestV1:404",
        "GetPurchaseOrderV1:404",
        "ApprovePurchaseOrderV1:404",
        "GetPurchaseGoodsReceiptsV1:404",
        "GetPurchaseGoodsReceiptOptionsV1:404",
        "RecordPurchaseGoodsReceiptV1:404",
        "GetAccountingPeriodV1:404",
        "ApproveAccountingPeriodV1:404",
        "CloseAccountingPeriodV1:404",
        "GetAccountingJournalV1:404",
        "GetAccountingNormalJournalDraftV1:404",
        "ApproveAccountingNormalJournalDraftV1:404",
        "ApproveAccountingJournalReversalV1:404",
        "GetManufacturingOrderMassBalanceV1:404",
        "CloseManufacturingOrderV1:404",
        "GetSalesContactInformationReferencesV1:404",
    ];

    private static readonly HashSet<string> MixedProblemResponses =
    [
        "CreateAccountingNormalJournalDraftV1:400",
        "ApproveAccountingNormalJournalDraftV1:400",
        "ProposeAccountingJournalReversalV1:400",
        "ApproveAccountingJournalReversalV1:400",
        "GetAuditEventsV1:400",
        "SetOrganizationLocationActiveStateV1:400",
        "CreateOrganizationSiteV1:400",
        "UpdateOrganizationSiteV1:400",
        "CreateOrganizationLocationV1:400",
        "UpdateOrganizationLocationV1:400",
        "CreateOrganizationWorkCenterV1:400",
        "UpdateOrganizationWorkCenterV1:400",
        "GetFinanceManualReceiptDraftContextV1:400",
        "SaveManufacturingPlanV1:400",
        "ApproveManufacturingPlanV1:400",
        "CreateManufacturingOrderV1:400",
        "IssueManufacturingInputV1:400",
        "ReturnManufacturingInputV1:400",
        "RecordManufacturingWorkInProgressV1:400",
        "DeclareManufacturingOutputV1:400",
        "SaveFinanceManualReceiptDraftV1:400",
        "RecordFinanceManualReceiptV1:400",
        "GetFinanceManualReceiptsV1:400",
        "GetFinanceManualReceiptV1:400",
        "GetFinanceBankAccountsV1:400",
        "SaveFinanceManualBankPaymentDraftV1:400",
        "GetFinanceManualBankPaymentsV1:400",
        "ApproveFinanceManualBankPaymentV1:400",
        "RecordFinanceManualBankPaymentV1:400",
        "CreateFinanceAllocationV1:400",
        "ReleaseFinanceAllocationV1:400",
        "ProposeFinanceBankAccountV1:400",
        "ApproveFinanceBankAccountV1:400",
        "ProposeFinanceBankAccountIbanChangeV1:400",
        "ApproveFinanceBankAccountIbanChangeV1:400",
        "GetFinanceBankAccountMasterV1:400",
        "GetFinanceBankAccountCurrencyCandidatesV1:400",
        "ProposeAccountingPeriodV1:400",
        "ApproveAccountingPeriodV1:400",
        "CloseAccountingPeriodV1:400",
        "CloseManufacturingOrderV1:400",
    ];

    // MFG-API (#313): every Manufacturing write advertises the endpoint's one conflict and one
    // unprocessable code set (ManufacturingEndpoints.ConflictCodes / ManufacturingProblemCodes).
    private static readonly string[] ManufacturingConflictCodes =
    [
        "idempotency_conflict", "manufacturing_concurrent_change", "manufacturing_fact_id_conflict",
        "manufacturing_order_id_conflict", "manufacturing_plan_id_conflict",
        "manufacturing_version_conflict",
        InventoryManufacturingRealizationCodesV1.LabelVersionConflict,
        InventoryManufacturingRealizationCodesV1.ParentLabelNotReleasable,
        InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict,
        InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient,
        InventoryManufacturingRealizationCodesV1.ParentInputNotIssued,
        InventoryManufacturingRealizationCodesV1.ReturnExceedsIssued,
        InventoryManufacturingRealizationCodesV1.OutputLabelAlreadyRegistered,
        InventoryManufacturingRealizationCodesV1.OutputLotOrLabelConflict,
        InventoryManufacturingRealizationCodesV1.IdempotencyConflict,
        InventoryManufacturingRealizationCodesV1.ConcurrentChange,
    ];

    private static readonly string[] ManufacturingBadRequestCodes =
    [
        "manufacturing_request_invalid",
        InventoryManufacturingRealizationCodesV1.InvalidRequest,
        InventoryManufacturingRealizationCodesV1.InvalidVersion,
        InventoryManufacturingRealizationCodesV1.InvalidQuantity,
    ];

    private static readonly string[] ManufacturingInventoryRealizationConflictCodes =
    [
        .. ManufacturingConflictCodes,
        "manufacturing_inventory_realization_incomplete",
    ];

    private static readonly string[] ManufacturingUnprocessableCodes =
    [
        "manufacturing_duplicate_roll_in_plan", "manufacturing_input_already_issued",
        "manufacturing_kilogram_precision_exceeded", "manufacturing_kilogram_profile_company_mismatch",
        "manufacturing_kilogram_profile_invalid", "manufacturing_kilogram_profile_not_in_effect",
        "manufacturing_mass_negative", "manufacturing_mass_scale_exceeded", "manufacturing_order_closed",
        "manufacturing_output_already_declared", "manufacturing_plan_not_approved",
        "manufacturing_plan_not_draft", "manufacturing_plan_without_inputs",
        "manufacturing_return_already_recorded", "manufacturing_return_exceeds_issued",
        "manufacturing_roll_not_in_order", "manufacturing_roll_not_issued",
        "manufacturing_stored_record_invalid", "manufacturing_unexplained_difference",
        "manufacturing_value_does_not_fit",
        "manufacturing_wip_already_measured", "manufacturing_wip_measurement_not_latest",
        "manufacturing_work_center_not_found", "settings_unit_profile_not_found",
        InventoryOpeningCodesV1.ProductIneligible,
        InventoryOpeningCodesV1.LocationNotFound,
        InventoryOpeningCodesV1.LocationInactive,
    ];

    private static readonly IReadOnlyDictionary<string, string[]> AdditionalProblemCodes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // INV-OPEN-1 part 2: the owner's closed sets (InventoryOpeningCodesV1), read here, never retyped.
            ["RecordInventoryOpeningCountV1:400"] = [.. InventoryOpeningCodesV1.Status400Codes.Order(StringComparer.Ordinal)],
            ["RecordInventoryOpeningCountV1:409"] = [.. InventoryOpeningCodesV1.Status409Codes.Order(StringComparer.Ordinal)],
            ["RecordInventoryOpeningCountV1:422"] = [.. InventoryOpeningCodesV1.Status422Codes.Order(StringComparer.Ordinal)],
            ["CreateInventoryPhysicalAllocationV1:400"] = [.. InventoryPhysicalAllocationHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)],
            ["CreateInventoryPhysicalAllocationV1:409"] = [.. InventoryPhysicalAllocationHttpCodesV1.CreateStatus409Codes.Order(StringComparer.Ordinal)],
            ["CreateInventoryPhysicalAllocationV1:422"] = [.. InventoryPhysicalAllocationHttpCodesV1.CreateStatus422Codes.Order(StringComparer.Ordinal)],
            ["ReleaseInventoryPhysicalAllocationV1:400"] = [.. InventoryPhysicalAllocationHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)],
            ["ReleaseInventoryPhysicalAllocationV1:409"] = [.. InventoryPhysicalAllocationHttpCodesV1.ReleaseStatus409Codes.Order(StringComparer.Ordinal)],
            ["ReleaseInventoryPhysicalAllocationV1:422"] = [.. InventoryPhysicalAllocationHttpCodesV1.ReleaseStatus422Codes.Order(StringComparer.Ordinal)],
            // ORG master edit: the one stable 422 code, the owner's constant.
            ["CreateOrganizationSiteV1:422"] = [OrganizationProblemCodesV1.CodeInUse],
            ["UpdateOrganizationSiteV1:422"] = [OrganizationProblemCodesV1.CodeInUse],
            ["CreateOrganizationLocationV1:422"] = [OrganizationProblemCodesV1.CodeInUse],
            ["UpdateOrganizationLocationV1:422"] = [OrganizationProblemCodesV1.CodeInUse],
            ["CreateOrganizationWorkCenterV1:422"] = [OrganizationProblemCodesV1.CodeInUse],
            ["UpdateOrganizationWorkCenterV1:422"] = [OrganizationProblemCodesV1.CodeInUse],
            // R19: the owner's closed sets (InventoryLabelTransformationHttpCodesV1), read here, never retyped.
            ["RecordInventoryLabelTransformationV1:400"] = [.. InventoryLabelTransformationHttpCodesV1.Status400Codes.Order(StringComparer.Ordinal)],
            ["RecordInventoryLabelTransformationV1:404"] = [.. InventoryLabelTransformationHttpCodesV1.Status404Codes.Order(StringComparer.Ordinal)],
            ["RecordInventoryLabelTransformationV1:409"] = [.. InventoryLabelTransformationHttpCodesV1.Status409Codes.Order(StringComparer.Ordinal)],
            ["RecordInventoryLabelTransformationV1:503"] = [.. InventoryLabelTransformationHttpCodesV1.Status503Codes.Order(StringComparer.Ordinal)],
            ["GetInventoryParentRollCandidatesV1:400"] = [InventoryParentRollCodesV1.InvalidQuery],
            ["GetInventoryParentRollCandidatesV1:409"] = [InventoryParentRollCodesV1.ReadConflict],
            ["GetInventoryStockPositionsV1:400"] = [InventoryStockPositionCodesV1.InvalidQuery],
            ["GetInventoryStockPositionsV1:409"] = [InventoryStockPositionCodesV1.ReadConflict],
            ["GetInventoryPhysicalAllocationsV1:400"] = [InventoryPhysicalAllocationReadCodesV1.InvalidQuery],
            ["GetInventoryPhysicalAllocationsV1:409"] = [InventoryPhysicalAllocationReadCodesV1.ReadConflict],
            ["GetInventoryLabelDetailV1:409"] = [InventoryLabelDetailCodesV1.ReadConflict],
            ["GetInventoryLabelDetailV1:503"] = [InventoryLabelDetailCodesV1.Unavailable],
            ["GetInventoryProductCandidatesV1:400"] = [InventoryReferenceCodesV1.ProductInvalidQuery],
            ["GetInventoryProductCandidatesV1:409"] = [InventoryReferenceCodesV1.ProductReadConflict],
            ["GetInventoryLocationCandidatesV1:400"] = [InventoryReferenceCodesV1.LocationInvalidQuery],
            ["GetInventoryLocationCandidatesV1:409"] = [InventoryReferenceCodesV1.LocationReadConflict],
            ["GetCounterpartiesV1:400"] = ["invalid_cursor", "validation_failed"],
            ["GetCounterpartiesV1:409"] = ["client_contract_required", "client_contract_unsupported"],
            ["GetCounterpartyV1:409"] = ["client_contract_required", "client_contract_unsupported"],
            ["GetSalesAccountReferencesV1:400"] = ["invalid_cursor", "validation_failed"],
            ["GetSalesCurrencyReferenceV1:400"] = ["validation_failed"],
            ["GetSalesCurrencyReferenceV1:422"] = ["unsupported_currency"],
            ["GetSalesProductReferenceV1:400"] = ["validation_failed"],
            ["GetSalesProductReferenceV1:409"] = ["product_selection_changed"],
            ["GetSalesProductReferenceV1:422"] = ["unsupported_product", "unsupported_unit"],
            ["GetSalesProductCandidatesV1:400"] = ["validation_failed"],
            ["GetManufacturingOutputProductCandidatesV1:400"] =
                [GoymenERP.Modules.Manufacturing.Application.ManufacturingOutputSelectionCodesV1.InvalidQuery],
            ["GetManufacturingOutputLocationCandidatesV1:400"] =
                [GoymenERP.Modules.Manufacturing.Application.ManufacturingOutputSelectionCodesV1.InvalidQuery],
            ["GetPurchasingProductCandidatesV1:400"] = ["validation_failed"],
            ["GetCounterpartyCompanyAccountLinkCandidatesV1:409"] =
                ["client_contract_required", "client_contract_unsupported"],
            ["GetCounterpartyCompanyAccountLinkCandidatesV1:400"] =
                ["invalid_cursor", "validation_failed"],
            ["GetCounterpartyV1:400"] = ["validation_failed"],
            ["GetCounterpartyV1:404"] = ["not_found"],
            ["CreateCounterpartyV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["CreateCounterpartyV1:409"] =
                ["client_contract_required", "client_contract_unsupported", "idempotency_conflict"],
            ["CreateCounterpartyCompanyAccountV1:400"] =
                ["command_metadata_invalid", "validation_failed"],
            ["CreateCounterpartyCompanyAccountV1:404"] = ["not_found"],
            ["CreateCounterpartyCompanyAccountV1:409"] =
                ["already_linked", "client_contract_required", "client_contract_unsupported", "idempotency_conflict"],
            ["SetCounterpartyCompanyAccountActiveStateV1:400"] =
                ["command_metadata_invalid", "validation_failed"],
            ["SetCounterpartyCompanyAccountActiveStateV1:404"] = ["not_found"],
            ["SetCounterpartyCompanyAccountActiveStateV1:409"] =
                ["client_contract_required", "client_contract_unsupported", "idempotency_conflict", "version_conflict"],
            ["GetCounterpartyContactInformationV1:400"] = ["validation_failed"],
            ["GetCounterpartyContactInformationV1:404"] = ["not_found"],
            ["GetCounterpartyContactInformationV1:409"] = ["client_contract_required", "client_contract_unsupported"],
            ["UpdateCounterpartyContactInformationV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["UpdateCounterpartyContactInformationV1:404"] = ["not_found"],
            ["UpdateCounterpartyContactInformationV1:409"] =
                ["client_contract_required", "client_contract_unsupported", "idempotency_conflict", "version_conflict"],
            ["SaveManufacturingPlanV1:400"] = ["manufacturing_request_invalid"],
            ["SaveManufacturingPlanV1:409"] =
                [.. ManufacturingConflictCodes, "inventory_label_version_conflict", "inventory_parent_roll_read_conflict"],
            ["SaveManufacturingPlanV1:422"] =
                [.. ManufacturingUnprocessableCodes, "inventory_parent_roll_context_required", "inventory_parent_roll_no_physical_position"],
            ["ApproveManufacturingPlanV1:400"] = ["manufacturing_request_invalid"],
            ["ApproveManufacturingPlanV1:409"] = ManufacturingConflictCodes,
            ["ApproveManufacturingPlanV1:422"] = ManufacturingUnprocessableCodes,
            ["CreateManufacturingOrderV1:400"] = ["manufacturing_request_invalid"],
            ["CreateManufacturingOrderV1:409"] = ManufacturingConflictCodes,
            ["CreateManufacturingOrderV1:422"] = ManufacturingUnprocessableCodes,
            ["IssueManufacturingInputV1:400"] = ManufacturingBadRequestCodes,
            ["IssueManufacturingInputV1:409"] = ManufacturingInventoryRealizationConflictCodes,
            ["IssueManufacturingInputV1:422"] = ManufacturingUnprocessableCodes,
            ["ReturnManufacturingInputV1:400"] = ManufacturingBadRequestCodes,
            ["ReturnManufacturingInputV1:409"] = ManufacturingInventoryRealizationConflictCodes,
            ["ReturnManufacturingInputV1:422"] = ManufacturingUnprocessableCodes,
            ["RecordManufacturingWorkInProgressV1:400"] = ["manufacturing_request_invalid"],
            ["RecordManufacturingWorkInProgressV1:409"] = ManufacturingConflictCodes,
            ["RecordManufacturingWorkInProgressV1:422"] = ManufacturingUnprocessableCodes,
            ["DeclareManufacturingOutputV1:400"] = ManufacturingBadRequestCodes,
            ["DeclareManufacturingOutputV1:409"] = ManufacturingInventoryRealizationConflictCodes,
            ["DeclareManufacturingOutputV1:422"] = ManufacturingUnprocessableCodes,
            ["GetSalesOrderDraftsV1:400"] = ["invalid_cursor", "validation_failed"],
            ["CreateSalesOrderDraftV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["CreateSalesOrderDraftV1:409"] = ["account_state", "concurrent_change", "same_key_payload_mismatch", "version_conflict"],
            ["CreateSalesOrderDraftV1:422"] = ["commercial_not_available", "owner_reference_unavailable", "unsupported_currency", "unsupported_product", "unsupported_unit", "validation_failed"],
            ["SubmitSalesOrderDraftV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["SubmitSalesOrderDraftV1:409"] = ["concurrent_change", "sales_state_conflict", "same_key_payload_mismatch", "version_conflict"],
            ["ApproveSalesOrderDraftV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["ApproveSalesOrderDraftV1:409"] = ["concurrent_change", "sales_state_conflict", "same_key_payload_mismatch", "version_conflict"],
            ["RejectSalesOrderDraftV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["RejectSalesOrderDraftV1:409"] = ["concurrent_change", "sales_state_conflict", "same_key_payload_mismatch", "version_conflict"],
            ["WithdrawSalesOrderDraftV1:400"] = ["command_metadata_invalid", "company_context_invalid", "correlation_metadata_invalid", "validation_failed"],
            ["WithdrawSalesOrderDraftV1:409"] = ["concurrent_change", "sales_state_conflict", "same_key_payload_mismatch", "version_conflict"],
            ["SubmitSalesOrderDraftV1:422"] = ["validation_failed"],
            ["RejectSalesOrderDraftV1:422"] = ["sales_approval_reason_too_long"],
            ["WithdrawSalesOrderDraftV1:422"] = ["sales_approval_reason_too_long"],
            ["ReviseSalesOrderDraftCommercialV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["ReviseSalesOrderDraftCommercialV1:409"] = ["concurrent_change", "sales_state_conflict", "same_key_payload_mismatch", "version_conflict"],
            ["ReviseSalesOrderDraftCommercialV1:422"] = ["validation_failed"],
            ["RescheduleSalesOrderDraftDeliveryV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["RescheduleSalesOrderDraftDeliveryV1:409"] = ["concurrent_change", "sales_state_conflict", "same_key_payload_mismatch", "version_conflict"],
            ["RescheduleSalesOrderDraftDeliveryV1:422"] = ["validation_failed"],
            ["GetShellCapabilitiesV1:400"] = ["company_context_invalid"],
            ["GetLoadingInstructionsV1:400"] = ["invalid_cursor", "validation_failed"],
            ["CreateLoadingInstructionV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["CreateLoadingInstructionV1:409"] = ["account_state", "concurrent_change", "order_line_state", "same_key_payload_mismatch"],
            ["CreateLoadingInstructionV1:422"] = ["unsupported_unit", "validation_failed"],
            ["IssueLoadingInstructionV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["IssueLoadingInstructionV1:409"] = ["concurrent_change", "logistics_instruction_state", "same_key_payload_mismatch", "version_conflict"],
            ["IssueLoadingInstructionV1:422"] = ["validation_failed"],
            ["RecordLoadingInstructionLoadV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["RecordLoadingInstructionLoadV1:409"] = ["concurrent_change", "logistics_duplicate_share_id", "logistics_instruction_state", "logistics_line_blocked", "logistics_load_exceeds_instructed", "same_key_payload_mismatch", "version_conflict"],
            ["RecordLoadingInstructionLoadV1:422"] = ["unsupported_unit", "validation_failed"],
            ["RecordLoadingInstructionShipmentV1:400"] =
            [
                "command_metadata_invalid", "inventory_definite_shipment_invalid_request",
                "inventory_definite_shipment_quantity_invalid", "inventory_definite_shipment_version_invalid",
                "validation_failed",
            ],
            ["RecordLoadingInstructionShipmentV1:409"] =
            [
                "concurrent_change", "inventory_allocation_insufficient", "inventory_allocation_label_mismatch",
                "inventory_allocation_state_conflict", "inventory_allocation_version_conflict",
                "inventory_definite_shipment_concurrent_change", "inventory_definite_shipment_idempotency_conflict",
                "inventory_definite_shipment_owner_not_company",
                "inventory_label_not_releasable", "inventory_label_version_conflict",
                "inventory_physical_stock_insufficient", "logistics_allocation_source_mismatch",
                "logistics_duplicate_share_id", "logistics_instruction_state", "logistics_line_blocked",
                "logistics_shipment_exceeds_loaded", "same_key_payload_mismatch", "version_conflict",
            ],
            ["RecordLoadingInstructionShipmentV1:422"] = ["unsupported_unit", "validation_failed"],
            ["RecordLoadingInstructionDeliveryV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["RecordLoadingInstructionDeliveryV1:409"] =
            [
                "concurrent_change", "logistics_delivery_exceeds_shipment", "logistics_duplicate_share_id",
                "logistics_instruction_state", "logistics_shipment_source_invalid", "same_key_payload_mismatch",
                "version_conflict",
            ],
            ["RecordLoadingInstructionDeliveryV1:422"] = ["unsupported_unit", "validation_failed"],
            ["GetPurchaseRequestsV1:400"] = ["invalid_cursor", "validation_failed"],
            ["CreatePurchaseRequestV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["CreatePurchaseRequestV1:409"] = ["concurrent_change", "same_key_payload_mismatch", "version_conflict"],
            ["CreatePurchaseRequestV1:422"] = ["unsupported_product", "unsupported_unit", "validation_failed"],
            ["SubmitPurchaseRequestV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["SubmitPurchaseRequestV1:409"] = ["concurrent_change", "purchasing_request_not_draft", "same_key_payload_mismatch", "version_conflict"],
            ["SubmitPurchaseRequestV1:422"] = ["validation_failed"],
            ["GetPurchaseOrdersV1:400"] = ["invalid_cursor", "validation_failed"],
            ["CreatePurchaseOrderV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["CreatePurchaseOrderV1:409"] = ["account_state", "concurrent_change", "purchasing_request_state", "same_key_payload_mismatch", "version_conflict"],
            ["CreatePurchaseOrderV1:422"] = ["unsupported_product", "unsupported_unit", "validation_failed"],
            ["ApprovePurchaseOrderV1:400"] = ["command_metadata_invalid", "validation_failed"],
            ["ApprovePurchaseOrderV1:409"] = ["concurrent_change", "purchasing_order_state", "purchasing_request_line_over_sourced", "same_key_payload_mismatch", "version_conflict"],
            ["ApprovePurchaseOrderV1:422"] = ["validation_failed"],
            ["GetPurchaseGoodsReceiptsV1:400"] = ["validation_failed"],
            ["RecordPurchaseGoodsReceiptV1:400"] =
                ["command_metadata_invalid", "inventory_goods_receipt_invalid_request", "inventory_goods_receipt_quantity_invalid", "inventory_goods_receipt_version_invalid", "validation_failed"],
            ["RecordPurchaseGoodsReceiptV1:409"] =
                ["inventory_goods_receipt_concurrent_change", "inventory_goods_receipt_idempotency_conflict", "inventory_goods_receipt_label_already_registered", "inventory_goods_receipt_lot_or_label_conflict", "purchasing_acceptance_concurrent_change", "purchasing_acceptance_idempotency_conflict", "purchasing_acceptance_order_state_conflict", "purchasing_acceptance_order_version_conflict", "purchasing_acceptance_product_version_conflict", "purchasing_acceptance_quantity_exceeds_remaining"],
            ["GetAccountingPeriodsV1:400"] = ["accounting_request_invalid"],
            ["ProposeAccountingPeriodV1:400"] = ["accounting_request_invalid"],
            ["ProposeAccountingPeriodV1:409"] = ["accounting_concurrent_change", "accounting_period_conflict", "accounting_period_overlap", "accounting_period_preparer_conflict", "accounting_version_conflict", "idempotency_conflict"],
            ["ProposeAccountingPeriodV1:422"] = ["accounting_invalid_reference", "accounting_period_already_closed", "accounting_period_invalid", "accounting_period_not_open", "accounting_period_not_proposed", "accounting_value_does_not_fit"],
            ["GetAccountingPeriodV1:400"] = ["accounting_request_invalid"],
            ["ApproveAccountingPeriodV1:400"] = ["accounting_request_invalid"],
            ["ApproveAccountingPeriodV1:409"] = ["accounting_concurrent_change", "accounting_period_conflict", "accounting_period_overlap", "accounting_period_preparer_conflict", "accounting_version_conflict", "idempotency_conflict"],
            ["ApproveAccountingPeriodV1:422"] = ["accounting_invalid_reference", "accounting_period_already_closed", "accounting_period_invalid", "accounting_period_not_open", "accounting_period_not_proposed", "accounting_value_does_not_fit"],
            ["CloseAccountingPeriodV1:400"] = ["accounting_request_invalid"],
            ["CloseAccountingPeriodV1:409"] = ["accounting_concurrent_change", "accounting_period_conflict", "accounting_period_overlap", "accounting_period_preparer_conflict", "accounting_version_conflict", "idempotency_conflict"],
            ["CloseAccountingPeriodV1:422"] = ["accounting_invalid_reference", "accounting_period_already_closed", "accounting_period_invalid", "accounting_period_not_open", "accounting_period_not_proposed", "accounting_value_does_not_fit"],
            ["GetAccountingJournalsV1:400"] = ["accounting_request_invalid"],
            ["GetAccountingJournalV1:400"] = ["accounting_request_invalid"],
            ["GetAccountingTrialBalanceV1:400"] = ["accounting_request_invalid"],
            ["GetAccountingPostingQueuesV1:400"] = ["accounting_request_invalid"],
            ["CreateAccountingNormalJournalDraftV1:400"] = ["accounting_request_invalid"],
            ["CreateAccountingNormalJournalDraftV1:409"] = ["accounting_concurrent_change", "accounting_normal_journal_draft_content_conflict", "accounting_normal_journal_draft_not_pending", "accounting_normal_journal_draft_posting_conflict", "accounting_period_conflict", "accounting_period_overlap", "accounting_period_preparer_conflict", "accounting_version_conflict", "idempotency_conflict"],
            ["CreateAccountingNormalJournalDraftV1:422"] = ["accounting_company_mismatch", "accounting_invalid_reference", "accounting_journal_line_invalid", "accounting_journal_too_few_lines", "accounting_journal_unbalanced", "accounting_period_already_closed", "accounting_period_closed", "accounting_period_does_not_cover_date", "accounting_period_invalid", "accounting_period_not_open", "accounting_period_not_proposed", "accounting_value_does_not_fit"],
            ["GetAccountingNormalJournalDraftV1:400"] = ["accounting_request_invalid"],
            ["ApproveAccountingNormalJournalDraftV1:400"] = ["accounting_request_invalid"],
            ["ApproveAccountingNormalJournalDraftV1:409"] = ["accounting_concurrent_change", "accounting_normal_journal_draft_content_conflict", "accounting_normal_journal_draft_not_pending", "accounting_normal_journal_draft_posting_conflict", "accounting_period_conflict", "accounting_period_overlap", "accounting_period_preparer_conflict", "accounting_version_conflict", "idempotency_conflict"],
            ["ApproveAccountingNormalJournalDraftV1:422"] = ["accounting_company_mismatch", "accounting_invalid_reference", "accounting_journal_line_invalid", "accounting_journal_too_few_lines", "accounting_journal_unbalanced", "accounting_period_already_closed", "accounting_period_closed", "accounting_period_does_not_cover_date", "accounting_period_invalid", "accounting_period_not_open", "accounting_period_not_proposed", "accounting_value_does_not_fit"],
            ["ProposeAccountingJournalReversalV1:400"] = ["accounting_request_invalid"],
            ["ProposeAccountingJournalReversalV1:409"] = ["accounting_concurrent_change", "accounting_journal_reversal_not_pending", "accounting_journal_reversal_posting_conflict", "accounting_journal_reversal_proposal_content_conflict", "accounting_journal_reversal_version_conflict", "accounting_period_conflict", "accounting_period_overlap", "accounting_period_preparer_conflict", "accounting_version_conflict", "idempotency_conflict"],
            ["ProposeAccountingJournalReversalV1:422"] = ["accounting_journal_reversal_period_not_found", "accounting_journal_reversal_source_journal_not_found"],
            ["ApproveAccountingJournalReversalV1:400"] = ["accounting_request_invalid"],
            ["ApproveAccountingJournalReversalV1:409"] = ["accounting_concurrent_change", "accounting_journal_reversal_not_pending", "accounting_journal_reversal_posting_conflict", "accounting_journal_reversal_proposal_content_conflict", "accounting_journal_reversal_version_conflict", "accounting_period_conflict", "accounting_period_overlap", "accounting_period_preparer_conflict", "accounting_version_conflict", "idempotency_conflict"],
            ["ApproveAccountingJournalReversalV1:422"] = ["accounting_invalid_reference", "accounting_period_closed", "accounting_period_does_not_cover_date", "accounting_period_not_found", "accounting_period_not_open", "accounting_reversal_of_reversal"],
            // MFG-ORD: the list refuses a bad page size, cursor or status on the field; the close is a
            // write like the others, and adds manufacturing_unexplained_difference to the shared 422 list.
            ["ListManufacturingOrdersV1:400"] = ["manufacturing_request_invalid"],
            ["ListManufacturingPlansV1:400"] = ["manufacturing_request_invalid"],
            ["CloseManufacturingOrderV1:400"] = ["manufacturing_request_invalid"],
            ["CloseManufacturingOrderV1:409"] = ManufacturingConflictCodes,
            ["CloseManufacturingOrderV1:422"] = ManufacturingUnprocessableCodes,
            ["GetLogisticsApprovedOrderLinesV1:400"] = ["invalid_cursor", "validation_failed"],
            ["GetSalesContactInformationReferencesV1:400"] = ["validation_failed"],
            ["GetSalesCurrencyReferencesV1:400"] = ["validation_failed"],
        };

    private static readonly IReadOnlyDictionary<string, ComponentSchemaExpectation>
        ExpectedSuccessComponentSchemas =
            new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
            {
                ["SalesAddressOptionV1"] = ObjectSchema(
                    ["id", "version", "purpose", "text", "countryCode", "line1", "line2", "city", "district", "postalCode"],
                    ("id", "type:string;format:uuid"),
                    ("version", "type:string"),
                    ("purpose", "type:string"),
                    ("text", "type:string"),
                    ("countryCode", "type:string"),
                    ("line1", "type:string"),
                    ("line2", "type:null|string"),
                    ("city", "type:null|string"),
                    ("district", "type:null|string"),
                    ("postalCode", "type:null|string")),
                ["SalesContactInformationOptionsV1"] = ObjectSchema(
                    ["companyId", "partyId", "companyAccountId", "accountVersion", "addresses", "contacts"],
                    ("companyId", "type:string;format:uuid"),
                    ("partyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("accountVersion", "type:string"),
                    ("addresses", "type:array;items:ref:SalesAddressOptionV1"),
                    ("contacts", "type:array;items:ref:SalesContactOptionV1")),
                ["SalesContactOptionV1"] = ObjectSchema(
                    ["id", "version", "displayName", "jobTitle", "purpose"],
                    ("id", "type:string;format:uuid"),
                    ("version", "type:string"),
                    ("displayName", "type:string"),
                    ("jobTitle", "type:null|string"),
                    ("purpose", "type:string")),
                ["SalesCurrencyOptionsV1"] = ObjectSchema(
                    ["companyId", "effectiveOn", "currencies"],
                    ("companyId", "type:string;format:uuid"),
                    ("effectiveOn", "type:string;format:date"),
                    ("currencies", "type:array;items:ref:SalesCurrencyReferenceV1")),
                ["OrderListViewV1"] = ObjectSchema(
                    ["orders", "nextCursor"],
                    ("orders", "type:array;items:ref:OrderSummaryViewV1"),
                    ("nextCursor", "type:null|string")),
                ["OrderSummaryViewV1"] = ObjectSchema(
                    ["orderId", "planId", "workCenterId", "process", "status", "version", "plannedRollCount", "plannedInputKg", "target", "createdAtUtc", "closedAtUtc"],
                    ("orderId", "type:string;format:uuid"),
                    ("planId", "type:string;format:uuid"),
                    ("workCenterId", "type:string;format:uuid"),
                    ("process", "type:string"),
                    ("status", "type:string"),
                    ("version", "type:integer|string;format:int64"),
                    ("plannedRollCount", "type:integer|string;format:int32"),
                    ("plannedInputKg", "type:string"),
                    ("target", "oneOf:ref:TargetViewV1|type:null"),
                    ("createdAtUtc", "type:string;format:date-time"),
                    ("closedAtUtc", "type:null|string;format:date-time")),
                // MFG-PLAN-LIST: the order list's page shape over stored plans; approvedAtUtc is null until approval.
                ["PlanListViewV1"] = ObjectSchema(
                    ["plans", "nextCursor"],
                    ("plans", "type:array;items:ref:PlanSummaryViewV1"),
                    ("nextCursor", "type:null|string")),
                ["PlanSummaryViewV1"] = ObjectSchema(
                    ["planId", "workCenterId", "process", "status", "version", "plannedRollCount", "plannedInputKg", "target", "createdAtUtc", "approvedAtUtc"],
                    ("planId", "type:string;format:uuid"),
                    ("workCenterId", "type:string;format:uuid"),
                    ("process", "type:string"),
                    ("status", "type:string"),
                    ("version", "type:integer|string;format:int64"),
                    ("plannedRollCount", "type:integer|string;format:int32"),
                    ("plannedInputKg", "type:string"),
                    ("target", "oneOf:ref:TargetViewV1|type:null"),
                    ("createdAtUtc", "type:string;format:date-time"),
                    ("approvedAtUtc", "type:null|string;format:date-time")),
                ["LogisticsApprovedOrderLinePage"] = ObjectSchema(
                    ["items", "nextCursor"],
                    ("items", "type:array;items:ref:LogisticsApprovedOrderLineView"),
                    ("nextCursor", "type:null|string")),
                ["LogisticsApprovedOrderLineView"] = ObjectSchema(
                    ["orderId", "documentNo", "lineId", "lineNo", "customerPartyId", "companyAccountId", "customerName", "productId", "productVersion", "productCode", "productName", "unitId", "unitCode", "quantity", "requestedDeliveryDate", "revision"],
                    ("orderId", "type:string;format:uuid"),
                    ("documentNo", "type:string"),
                    ("lineId", "type:string;format:uuid"),
                    ("lineNo", "type:integer|string;format:int32"),
                    ("customerPartyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("customerName", "type:string"),
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"),
                    ("productCode", "type:string"),
                    ("productName", "type:string"),
                    ("unitId", "type:string;format:uuid"),
                    ("unitCode", "type:string"),
                    ("quantity", "type:string"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("revision", "type:integer|string;format:int32")),
                ["EngineAnswerViewV1"] = ObjectSchema(
                    ["outcome", "value", "missingParameters", "refusalCode", "refusedParameter"],
                    ("outcome", "type:string"),
                    ("value", "type:null|string"),
                    ("missingParameters", "type:array;items:type:string"),
                    ("refusalCode", "type:null|string"),
                    ("refusedParameter", "type:null|string")),
                ["MassBalanceViewV1"] = ObjectSchema(
                    ["orderId", "version", "formulaVersion", "rolls"],
                    ("orderId", "type:string;format:uuid"),
                    ("version", "type:integer|string;format:int64"),
                    ("formulaVersion", "type:string"),
                    ("rolls", "type:array;items:ref:RollMassBalanceViewV1")),
                ["MassDifferenceViewV1"] = ObjectSchema(
                    ["actualInputKg", "accountedKg", "differenceKg", "isFullyAccounted"],
                    ("actualInputKg", "type:string"),
                    ("accountedKg", "type:string"),
                    ("differenceKg", "type:string"),
                    ("isFullyAccounted", "type:boolean")),
                ["RollMassBalanceViewV1"] = ObjectSchema(
                    ["parentRollId", "difference", "scrapPercent", "actualUnitKg"],
                    ("parentRollId", "type:string;format:uuid"),
                    ("difference", "ref:MassDifferenceViewV1"),
                    ("scrapPercent", "ref:EngineAnswerViewV1"),
                    ("actualUnitKg", "ref:EngineAnswerViewV1")),
                ["JournalDetailV1"] = ObjectSchema(
                    ["journal", "lines"],
                    ("journal", "ref:JournalSummaryV1"),
                    ("lines", "type:array;items:ref:JournalLineViewV1")),
                ["JournalLineViewV1"] = ObjectSchema(
                    ["lineNumber", "accountId", "debit", "credit"],
                    ("lineNumber", "type:integer|string;format:int32"),
                    ("accountId", "type:string;format:uuid"),
                    ("debit", "type:string"),
                    ("credit", "type:string")),
                ["JournalPageV1"] = ObjectSchema(
                    ["items", "nextCursor"],
                    ("items", "type:array;items:ref:JournalSummaryV1"),
                    ("nextCursor", "type:null|string")),
                ["AccountingJournalReversalProposalViewV1"] = ObjectSchema(
                    ["proposalId", "companyId", "sourceJournalId", "reversalJournalId", "periodId", "transactionDate", "accountingDate", "valueDate", "reason", "proposedBy", "proposedAtUtc", "status", "decidedBy", "decidedAtUtc", "decisionReason", "version"],
                    ("proposalId", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("sourceJournalId", "type:string;format:uuid"),
                    ("reversalJournalId", "type:null|string;format:uuid"),
                    ("periodId", "type:string;format:uuid"),
                    ("transactionDate", "type:string;format:date"),
                    ("accountingDate", "type:string;format:date"),
                    ("valueDate", "type:null|string;format:date"),
                    ("reason", "type:string"),
                    ("proposedBy", "type:string;format:uuid"),
                    ("proposedAtUtc", "type:string;format:date-time"),
                    ("status", "type:string"),
                    ("decidedBy", "type:null|string;format:uuid"),
                    ("decidedAtUtc", "type:null|string;format:date-time"),
                    ("decisionReason", "type:null|string"),
                    ("version", "type:integer|string;format:int32")),
                ["TrialBalanceCurrencyTotalV1"] = ObjectSchema(
                    ["currencyId", "debit", "credit", "isBalanced"],
                    ("currencyId", "type:string;format:uuid"),
                    ("debit", "type:string"),
                    ("credit", "type:string"),
                    ("isBalanced", "type:boolean")),
                ["TrialBalanceLineV1"] = ObjectSchema(
                    ["accountId", "currencyId", "debit", "credit", "balanceSide", "balance"],
                    ("accountId", "type:string;format:uuid"),
                    ("currencyId", "type:string;format:uuid"),
                    ("debit", "type:string"),
                    ("credit", "type:string"),
                    ("balanceSide", "type:string"),
                    ("balance", "type:string")),
                ["TrialBalanceReportV1"] = ObjectSchema(
                    ["fromAccountingDate", "throughAccountingDate", "currencyId", "lines", "currencyTotals"],
                    ("fromAccountingDate", "type:string;format:date"),
                    ("throughAccountingDate", "type:string;format:date"),
                    ("currencyId", "type:null|string;format:uuid"),
                    ("lines", "type:array;items:ref:TrialBalanceLineV1"),
                    ("currencyTotals", "type:array;items:ref:TrialBalanceCurrencyTotalV1")),
                ["JournalSummaryV1"] = ObjectSchema(
                    ["journalId", "kind", "sourceId", "postingPurpose", "postingRuleVersion", "currencyId", "transactionDate", "accountingDate", "valueDate", "periodId", "linkedJournalId", "totalDebit", "totalCredit", "lineCount", "storedAtUtc"],
                    ("journalId", "type:string;format:uuid"),
                    ("kind", "type:string"),
                    ("sourceId", "type:string;format:uuid"),
                    ("postingPurpose", "type:string"),
                    ("postingRuleVersion", "type:string"),
                    ("currencyId", "type:string;format:uuid"),
                    ("transactionDate", "type:string;format:date"),
                    ("accountingDate", "type:string;format:date"),
                    ("valueDate", "type:null|string;format:date"),
                    ("periodId", "type:string;format:uuid"),
                    ("linkedJournalId", "type:null|string;format:uuid"),
                    ("totalDebit", "type:string"),
                    ("totalCredit", "type:string"),
                    ("lineCount", "type:integer|string;format:int32"),
                    ("storedAtUtc", "type:string;format:date-time")),
                ["NormalJournalDraftLineViewV1"] = ObjectSchema(
                    ["lineNumber", "mappingKey", "side", "accountId", "amount"],
                    ("lineNumber", "type:integer|string;format:int32"),
                    ("mappingKey", "type:string"),
                    ("side", "type:string"),
                    ("accountId", "type:string;format:uuid"),
                    ("amount", "type:string")),
                ["NormalJournalDraftViewV1"] = ObjectSchema(
                    ["draftId", "companyId", "version", "status", "preparedByUserId", "preparedAtUtc", "approvedByUserId", "approvedAtUtc", "periodId", "postedJournalId", "sourceId", "postingPurpose", "postingRuleVersion", "accountPlanSourceVersion", "accountPlanSourceSha256", "currencyId", "transactionDate", "accountingDate", "valueDate", "lines"],
                    ("draftId", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("version", "type:integer|string;format:int64"),
                    ("status", "type:string"),
                    ("preparedByUserId", "type:string;format:uuid"),
                    ("preparedAtUtc", "type:string;format:date-time"),
                    ("approvedByUserId", "type:null|string;format:uuid"),
                    ("approvedAtUtc", "type:null|string;format:date-time"),
                    ("periodId", "type:null|string;format:uuid"),
                    ("postedJournalId", "type:null|string;format:uuid"),
                    ("sourceId", "type:string;format:uuid"),
                    ("postingPurpose", "type:string"),
                    ("postingRuleVersion", "type:string"),
                    ("accountPlanSourceVersion", "type:string"),
                    ("accountPlanSourceSha256", "type:string"),
                    ("currencyId", "type:string;format:uuid"),
                    ("transactionDate", "type:string;format:date"),
                    ("accountingDate", "type:string;format:date"),
                    ("valueDate", "type:null|string;format:date"),
                    ("lines", "type:array;items:ref:NormalJournalDraftLineViewV1")),
                ["PendingValuationViewV1"] = ObjectSchema(
                    ["sourceId", "postingPurpose", "postingRuleVersion", "linesAwaitingAmount", "firstMessageId"],
                    ("sourceId", "type:string;format:uuid"),
                    ("postingPurpose", "type:string"),
                    ("postingRuleVersion", "type:string"),
                    ("linesAwaitingAmount", "type:integer|string;format:int32"),
                    ("firstMessageId", "type:string")),
                ["PeriodPageV1"] = ObjectSchema(
                    ["items", "nextCursor"],
                    ("items", "type:array;items:ref:PeriodViewV1"),
                    ("nextCursor", "type:null|string")),
                ["PeriodViewV1"] = ObjectSchema(
                    ["periodId", "companyId", "startsOn", "endsOn", "status", "version", "preparedByUserId", "preparedAtUtc", "approvedByUserId", "approvedAtUtc", "closedByUserId", "closedAtUtc", "isOwnProposal"],
                    ("periodId", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("startsOn", "type:string;format:date"),
                    ("endsOn", "type:string;format:date"),
                    ("status", "type:string"),
                    ("version", "type:integer|string;format:int64"),
                    ("preparedByUserId", "type:string;format:uuid"),
                    ("preparedAtUtc", "type:string;format:date-time"),
                    ("approvedByUserId", "type:null|string;format:uuid"),
                    ("approvedAtUtc", "type:null|string;format:date-time"),
                    ("closedByUserId", "type:null|string;format:uuid"),
                    ("closedAtUtc", "type:null|string;format:date-time"),
                    ("isOwnProposal", "type:boolean")),
                ["PostingExceptionViewV1"] = ObjectSchema(
                    ["sourceId", "postingPurpose", "postingRuleVersion", "missingMappingKeys", "firstMessageId"],
                    ("sourceId", "type:string;format:uuid"),
                    ("postingPurpose", "type:string"),
                    ("postingRuleVersion", "type:string"),
                    ("missingMappingKeys", "type:array;items:type:string"),
                    ("firstMessageId", "type:string")),
                ["PostingQueuesV1"] = ObjectSchema(
                    ["exceptions", "pending"],
                    ("exceptions", "type:array;items:ref:PostingExceptionViewV1"),
                    ("pending", "type:array;items:ref:PendingValuationViewV1")),
                ["PurchaseOrderDetailView"] = ObjectSchema(
                    ["order", "lines"],
                    ("order", "ref:PurchaseOrderSummaryView"),
                    ("lines", "type:array;items:ref:PurchaseOrderLineView")),
                ["PurchaseGoodsReceiptOptionsV1"] = ObjectSchema(
                    ["source", "locations", "searchWindowFull"],
                    ("source", "ref:PurchaseReceiptSourceV1"),
                    ("locations", "type:array;items:ref:PurchaseReceiptLocationV1"),
                    ("searchWindowFull", "type:boolean")),
                ["PurchaseGoodsReceiptPageV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:PurchaseGoodsReceiptViewV1"),
                    ("searchWindowFull", "type:boolean")),
                ["PurchaseGoodsReceiptResponseV1"] = ObjectSchema(
                    ["receipt", "isReplay", "originalCorrelationId"],
                    ("receipt", "ref:PurchaseGoodsReceiptViewV1"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string")),
                ["PurchaseGoodsReceiptViewV1"] = ObjectSchema(
                    ["companyId", "acceptanceId", "purchaseOrderId", "purchaseOrderLineId", "purchaseOrderVersion", "productId", "productVersion", "unitId", "acceptedQuantity", "remainingQuantity", "lotId", "labelId", "labelCode", "labelVersion", "movementId", "locationId", "measuredQuantityKg", "initialBlock", "occurredAtUtc"],
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
                    ("occurredAtUtc", "type:string;format:date-time")),
                ["PurchaseReceiptLocationV1"] = ObjectSchema(
                    ["locationId", "locationCode", "locationName", "siteName"],
                    ("locationId", "type:string;format:uuid"),
                    ("locationCode", "type:string"),
                    ("locationName", "type:string"),
                    ("siteName", "type:string")),
                ["PurchaseReceiptSourceV1"] = ObjectSchema(
                    ["companyId", "purchaseOrderId", "purchaseOrderLineId", "purchaseOrderVersion", "productId", "productVersion", "unitId", "effectiveOn"],
                    ("companyId", "type:string;format:uuid"),
                    ("purchaseOrderId", "type:string;format:uuid"),
                    ("purchaseOrderLineId", "type:string;format:uuid"),
                    ("purchaseOrderVersion", "type:string"),
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"),
                    ("unitId", "type:string;format:uuid"),
                    ("effectiveOn", "type:string;format:date")),
                ["PurchaseOrderLineView"] = ObjectSchema(
                    ["lineId", "lineNo", "kind", "parentLineId", "sourceRequestLineId", "productId", "productVersion", "unitId", "ordered", "technical", "committed", "accepted", "remaining"],
                    ("lineId", "type:string;format:uuid"),
                    ("lineNo", "type:integer|string;format:int32"),
                    ("kind", "type:string"),
                    ("parentLineId", "type:null|string;format:uuid"),
                    ("sourceRequestLineId", "type:null|string;format:uuid"),
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"),
                    ("unitId", "type:string;format:uuid"),
                    ("ordered", "type:string"),
                    ("technical", "type:string"),
                    ("committed", "type:string"),
                    ("accepted", "type:string"),
                    ("remaining", "type:string")),
                ["PurchaseOrderListResponse"] = ObjectSchema(
                    ["items", "nextCursor"],
                    ("items", "type:array;items:ref:PurchaseOrderSummaryView"),
                    ("nextCursor", "type:null|string")),
                ["PurchaseOrderResponse"] = ObjectSchema(
                    ["detail", "isReplay", "originalCorrelationId"],
                    ("detail", "ref:PurchaseOrderDetailView"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string")),
                ["PurchaseOrderSummaryView"] = ObjectSchema(
                    ["id", "companyId", "status", "version", "supplierPartyId", "companyAccountId", "sourceRequestId", "effectiveOn", "createdAtUtc", "updatedAtUtc", "lineCount"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("status", "type:string"),
                    ("version", "type:string"),
                    ("supplierPartyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("sourceRequestId", "type:null|string;format:uuid"),
                    ("effectiveOn", "type:string;format:date"),
                    ("createdAtUtc", "type:string;format:date-time"),
                    ("updatedAtUtc", "type:string;format:date-time"),
                    ("lineCount", "type:integer|string;format:int32")),
                ["PurchaseRequestDetailView"] = ObjectSchema(
                    ["request", "lines"],
                    ("request", "ref:PurchaseRequestSummaryView"),
                    ("lines", "type:array;items:ref:PurchaseRequestLineView")),
                ["PurchaseRequestLineView"] = ObjectSchema(
                    ["lineId", "lineNo", "productId", "productVersion", "unitId", "quantity"],
                    ("lineId", "type:string;format:uuid"),
                    ("lineNo", "type:integer|string;format:int32"),
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"),
                    ("unitId", "type:string;format:uuid"),
                    ("quantity", "type:string")),
                ["PurchaseRequestListResponse"] = ObjectSchema(
                    ["items", "nextCursor"],
                    ("items", "type:array;items:ref:PurchaseRequestSummaryView"),
                    ("nextCursor", "type:null|string")),
                ["PurchaseRequestResponse"] = ObjectSchema(
                    ["detail", "isReplay", "originalCorrelationId"],
                    ("detail", "ref:PurchaseRequestDetailView"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string")),
                ["PurchaseRequestSummaryView"] = ObjectSchema(
                    ["id", "companyId", "status", "version", "effectiveOn", "createdAtUtc", "updatedAtUtc", "lineCount"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("status", "type:string"),
                    ("version", "type:string"),
                    ("effectiveOn", "type:string;format:date"),
                    ("createdAtUtc", "type:string;format:date-time"),
                    ("updatedAtUtc", "type:string;format:date-time"),
                    ("lineCount", "type:integer|string;format:int32")),
                ["LoadingInstructionDetailView"] = ObjectSchema(
                    ["instruction", "lines", "shares"],
                    ("instruction", "ref:LoadingInstructionSummaryView"),
                    ("lines", "type:array;items:ref:LoadingInstructionLineView"),
                    ("shares", "type:array;items:ref:LoadingShareView")),
                ["LoadingInstructionLineView"] = ObjectSchema(
                    ["lineId", "lineNo", "sourceOrderId", "sourceOrderLineId", "sourceOrderRevision", "productId", "unitId", "instructed", "loaded", "shipped", "delivered", "remainingToLoad", "loadedNotShipped", "shippedNotDelivered", "isBlocked"],
                    ("lineId", "type:string;format:uuid"),
                    ("lineNo", "type:integer|string;format:int32"),
                    ("sourceOrderId", "type:string;format:uuid"),
                    ("sourceOrderLineId", "type:string;format:uuid"),
                    ("sourceOrderRevision", "type:integer|string;format:int32"),
                    ("productId", "type:string;format:uuid"),
                    ("unitId", "type:string;format:uuid"),
                    ("instructed", "type:string"),
                    ("loaded", "type:string"),
                    ("shipped", "type:string"),
                    ("delivered", "type:string"),
                    ("remainingToLoad", "type:string"),
                    ("loadedNotShipped", "type:string"),
                    ("shippedNotDelivered", "type:string"),
                    ("isBlocked", "type:boolean")),
                ["LoadingInstructionListResponse"] = ObjectSchema(
                    ["items", "nextCursor"],
                    ("items", "type:array;items:ref:LoadingInstructionSummaryView"),
                    ("nextCursor", "type:null|string")),
                ["LoadingInstructionResponse"] = ObjectSchema(
                    ["detail", "isReplay", "originalCorrelationId"],
                    ("detail", "ref:LoadingInstructionDetailView"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string")),
                ["LoadingInstructionSummaryView"] = ObjectSchema(
                    ["id", "companyId", "status", "version", "customerPartyId", "companyAccountId", "effectiveOn", "createdAtUtc", "updatedAtUtc", "lineCount"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("status", "type:string"),
                    ("version", "type:string"),
                    ("customerPartyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("effectiveOn", "type:string;format:date"),
                    ("createdAtUtc", "type:string;format:date-time"),
                    ("updatedAtUtc", "type:string;format:date-time"),
                    ("lineCount", "type:integer|string;format:int32")),
                ["LoadingShareView"] = ObjectSchema(
                    ["shareId", "lineId", "kind", "quantity", "measurement", "sourceShipmentId", "recordedAtUtc"],
                    ("shareId", "type:string;format:uuid"),
                    ("lineId", "type:string;format:uuid"),
                    ("kind", "type:string"),
                    ("quantity", "type:string"),
                    ("measurement", "type:null|string"),
                    ("sourceShipmentId", "type:null|string;format:uuid"),
                    ("recordedAtUtc", "type:string;format:date-time")),
                ["BankAccountCommandResult"] = ObjectSchema(
                    ["outcome", "resource", "isReplay", "originalCorrelationId", "issues"],
                    ("outcome", "ref:CommandOutcome"),
                    ("resource", "oneOf:ref:BankAccountView|type:null"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string"),
                    ("issues", "type:array;items:ref:ValidationIssue")),
                ["BankAccountCurrencyCandidate"] = ObjectSchema(
                    ["currencyId", "code", "displayLabel"],
                    ("currencyId", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("displayLabel", "type:string")),
                ["BankAccountCurrencyCandidates"] = ObjectSchema(
                    ["companyId", "items"],
                    ("companyId", "type:string;format:uuid"),
                    ("items", "type:array;items:ref:BankAccountCurrencyCandidate")),
                // K2: the card travels on the MASTER item and nowhere else. BankAccountView — the flat
                // read the receipt path uses — does not carry it, and that is enforced by the type
                // system rather than by a filter: FİNANS's first attempt to read the card through the
                // flat view failed to compile (CS1061). A field that travels unnecessarily is a field
                // that leaks, so its absence there is the design, not an omission.
                ["BankAccountMasterItem"] = ObjectSchema(
                    ["account", "isOwnProposal", "card", "isOwnPendingIbanProposal"],
                    ("account", "ref:BankAccountView"),
                    ("isOwnProposal", "type:boolean"),
                    ("card", "ref:BankAccountCard"),
                    ("isOwnPendingIbanProposal", "type:boolean")),
                // All eight are REQUIRED on the wire while the card values that can be absent stay nullable:
                // required means the property is always present, nullable means its value may be absent.
                // A pre-K2 account has no branch, IBAN or account number, and the schema says so in
                // types instead of leaving the client to infer it from a missing key.
                ["BankAccountCard"] = ObjectSchema(
                    ["bankName", "accountType", "branchName", "branchCode", "iban", "accountNumber", "moneyEligibility", "pendingIbanChange"],
                    ("bankName", "type:string"),
                    ("accountType", "type:string"),
                    ("branchName", "type:null|string"),
                    ("branchCode", "type:null|string"),
                    ("iban", "type:null|string"),
                    ("accountNumber", "type:null|string"),
                    ("moneyEligibility", "ref:BankAccountMoneyEligibility"),
                    ("pendingIbanChange", "oneOf:ref:BankAccountPendingIbanChange|type:null")),
                ["BankAccountMoneyEligibility"] = ScalarSchema("enum:Eligible|NotEligible|Undeclared"),
                ["BankAccountPendingIbanChange"] = ObjectSchema(
                    ["iban", "proposedBy", "proposedAtUtc", "reason"],
                    ("iban", "type:string"),
                    ("proposedBy", "type:string"),
                    ("proposedAtUtc", "type:string;format:date-time"),
                    ("reason", "type:string")),
                ["BankAccountMasterPage"] = ObjectSchema(
                    ["companyId", "items"],
                    ("companyId", "type:string;format:uuid"),
                    ("items", "type:array;items:ref:BankAccountMasterItem")),
                ["SalesDraftDeliverySliceV1"] = ObjectSchema(
                    ["sliceId", "orderUnitId", "quantity", "requestedDeliveryDate"],
                    ("sliceId", "type:string;format:uuid"),
                    ("orderUnitId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("requestedDeliveryDate", "type:string;format:date")),
                ["SalesDraftOwnerReferenceV1"] = ObjectSchema(
                    ["id", "expectedVersion"],
                    ("id", "type:string;format:uuid"),
                    ("expectedVersion", "type:string")),
                ["SalesDraftApprovalV1"] = ObjectSchema(
                    ["submittedBy", "submittedAtUtc", "decidedBy", "decidedAtUtc", "rejectionReason"],
                    ("submittedBy", "type:null|string"),
                    ("submittedAtUtc", "type:null|string;format:date-time"),
                    ("decidedBy", "type:null|string"),
                    ("decidedAtUtc", "type:null|string;format:date-time"),
                    ("rejectionReason", "type:null|string")),
                ["SalesDraftRevisionLineV1"] = ObjectSchema(
                    ["lineId", "lineNo", "productId", "productVersion", "orderUnitId", "quantity", "unitPrice", "priceUnitId", "discountPercent", "priceDisclosure"],
                    ("lineId", "type:string;format:uuid"),
                    ("lineNo", "type:integer|string;format:int32"),
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"),
                    ("orderUnitId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("unitPrice", "type:null|string"),
                    ("priceUnitId", "type:null|string;format:uuid"),
                    ("discountPercent", "type:null|string"),
                    ("priceDisclosure", "ref:SalesPriceDisclosure")),
                ["SalesDraftRevisionV1"] = ObjectSchema(
                    ["revision", "submittedBy", "submittedAtUtc", "approvedBy", "approvedAtUtc", "archivedAtUtc", "archivedBy", "moneyProfileId", "moneyProfileVersion", "moneyScale", "lines"],
                    ("revision", "type:string"),
                    ("submittedBy", "type:null|string"),
                    ("submittedAtUtc", "type:null|string;format:date-time"),
                    ("approvedBy", "type:null|string"),
                    ("approvedAtUtc", "type:null|string;format:date-time"),
                    ("archivedAtUtc", "type:string;format:date-time"),
                    ("archivedBy", "type:string"),
                    ("moneyProfileId", "type:string;format:uuid"),
                    ("moneyProfileVersion", "type:string"),
                    ("moneyScale", "type:integer|string;format:int32"),
                    ("lines", "type:array;items:ref:SalesDraftRevisionLineV1")),
                ["SalesOrderDraftRevisionsResponse"] = ObjectSchema(
                    ["items"],
                    ("items", "type:array;items:ref:SalesDraftRevisionV1")),
                ["SalesOrderDraftDeliverySchedulesResponse"] = ObjectSchema(
                    ["items"],
                    ("items", "type:array;items:ref:SalesDraftDeliveryScheduleV1")),
                ["SalesDraftDeliveryScheduleV1"] = ObjectSchema(
                    ["scheduleNo", "changedBy", "changedAtUtc", "requestedDeliveryDate", "lines"],
                    ("scheduleNo", "type:integer|string;format:int32"),
                    ("changedBy", "type:null|string"),
                    ("changedAtUtc", "type:null|string;format:date-time"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("lines", "type:array;items:ref:SalesDraftLineScheduleViewV1")),
                ["SalesDraftLineScheduleViewV1"] = ObjectSchema(
                    ["lineId", "lineNo", "requestedDeliveryDate", "slices"],
                    ("lineId", "type:string;format:uuid"),
                    ("lineNo", "type:integer|string;format:int32"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("slices", "type:array;items:ref:SalesDraftSliceScheduleViewV1")),
                ["SalesDraftSliceScheduleViewV1"] = ObjectSchema(
                    ["sliceId", "quantity", "requestedDeliveryDate"],
                    ("sliceId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("requestedDeliveryDate", "type:string;format:date")),
                ["SalesOrderDraftDeliveryProgressResponse"] = ObjectSchema(
                    ["draftVersion", "lines"],
                    ("draftVersion", "type:string"),
                    ("lines", "type:array;items:ref:SalesDraftLineDeliveryProgressV1")),
                ["SalesDraftLineDeliveryProgressV1"] = ObjectSchema(
                    ["lineId", "lineNo", "orderUnitId", "ordered", "shipped", "delivered", "remaining", "excess"],
                    ("lineId", "type:string;format:uuid"),
                    ("lineNo", "type:integer|string;format:int32"),
                    ("orderUnitId", "type:string;format:uuid"),
                    ("ordered", "type:string"),
                    ("shipped", "type:string"),
                    ("delivered", "type:string"),
                    ("remaining", "type:string"),
                    ("excess", "type:string")),
                ["SalesOrderDraftDetailView"] = ObjectSchema(
                    ["draft", "requestedDeliveryDate", "note", "lines", "deliveryAddress", "contact", "responsible", "paymentTerms", "sourceStrategy", "package", "approval", "isOwnDraft", "isOwnDecisionRestricted"],
                    ("draft", "ref:SalesOrderDraftSummaryView"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("note", "type:null|string"),
                    ("lines", "type:array;items:ref:SalesOrderDraftLineDetail"),
                    ("deliveryAddress", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("contact", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("responsible", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("paymentTerms", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("sourceStrategy", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("package", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("approval", "ref:SalesDraftApprovalV1"),
                    ("isOwnDraft", "type:boolean"),
                    ("isOwnDecisionRestricted", "type:boolean")),
                ["SalesOrderDraftLineDetail"] = ObjectSchema(
                    ["lineId", "lineNo", "productId", "productVersion", "productCode", "productName", "orderUnitId", "quantity", "unitPrice", "priceUnitId", "requestedDeliveryDate", "note", "requestedDeliverySlices", "discountPercent", "priceDisclosure"],
                    ("lineId", "type:string;format:uuid"),
                    ("lineNo", "type:integer|string;format:int32"),
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"),
                    ("productCode", "type:string"),
                    ("productName", "type:string"),
                    ("orderUnitId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("unitPrice", "type:null|string"),
                    ("priceUnitId", "type:null|string;format:uuid"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("note", "type:null|string"),
                    ("requestedDeliverySlices", "type:array|null;items:ref:SalesDraftDeliverySliceV1"),
                    ("discountPercent", "type:null|string"),
                    // SALES-P2: REQUIRED and not nullable, and the pairing is the point. The three commercial
                    // texts above stay nullable and come back null for a caller without sales.orders.price.read;
                    // this marker is what says WHY, so "withheld" stays distinguishable from "unpriced". A
                    // nullable marker would have reintroduced the very ambiguity it exists to remove.
                    ("priceDisclosure", "ref:SalesPriceDisclosure")),
                ["SalesOrderDraftListResponse"] = ObjectSchema(
                    ["items", "nextCursor"],
                    ("items", "type:array;items:ref:SalesOrderDraftSummaryView"),
                    ("nextCursor", "type:null|string")),
                ["SalesOrderDraftResponse"] = ObjectSchema(
                    ["detail", "isReplay", "originalCorrelationId"],
                    ("detail", "ref:SalesOrderDraftDetailView"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string")),
                ["SalesOrderDraftSummaryView"] = ObjectSchema(
                    ["id", "companyId", "documentNo", "kind", "state", "version", "revision", "partyId", "companyAccountId", "currencyId", "businessDate", "createdAtUtc", "lineCount", "approvalState"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("documentNo", "type:string"),
                    ("kind", "type:string"),
                    ("state", "type:string"),
                    ("version", "type:string"),
                    ("revision", "type:string"),
                    ("partyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("currencyId", "type:string;format:uuid"),
                    ("businessDate", "type:string;format:date"),
                    ("createdAtUtc", "type:string;format:date-time"),
                    ("lineCount", "type:integer|string;format:int32"),
                    ("approvalState", "type:string")),
                // SALES-P2: the disclosure marker's own component. Two members only — there is deliberately no
                // "Unavailable" third value as AmountDisclosure and CommandOutcome carry, because this answer is
                // computed at the moment of answering and can never be "not asked": the module throws 503 if the
                // name is unregistered rather than emitting a placeholder.
                ["SalesPriceDisclosure"] = ScalarSchema("enum:Visible|Withheld"),
                ["AllocationKnowledge"] = ScalarSchema("enum:Unavailable|Known"),
                ["AmountDisclosure"] = ScalarSchema("enum:Unavailable|Redacted|Visible"),
                ["AmountView"] = ObjectSchema(
                    ["disclosure", "money"],
                    ("disclosure", "ref:AmountDisclosure"),
                    ("money", "oneOf:ref:MoneyText|type:null")),
                ["BankAccountPage"] = ObjectSchema(
                    ["companyId", "items"],
                    ("companyId", "type:string;format:uuid"),
                    ("items", "type:array;items:ref:BankAccountView")),
                ["CommandOutcome"] = ScalarSchema("enum:Unavailable|Denied|ValidationFailed|VersionConflict|IdempotencyConflict|Succeeded"),
                ["CommandResult"] = ObjectSchema(
                    ["outcome", "resource", "isReplay", "originalCorrelationId", "issues"],
                    ("outcome", "ref:CommandOutcome"),
                    ("resource", "oneOf:ref:SourceDetail|type:null"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string"),
                    ("issues", "type:array;items:ref:ValidationIssue")),
                ["ManualBankPaymentCommandResult"] = ObjectSchema(
                    ["outcome", "resource", "isReplay", "originalCorrelationId", "issues"],
                    ("outcome", "ref:CommandOutcome"),
                    ("resource", "oneOf:ref:ManualBankPaymentView|type:null"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string"),
                    ("issues", "type:array;items:ref:ValidationIssue")),
                ["FinanceAllocationCommandResult"] = ObjectSchema(
                    ["outcome", "resource", "isReplay", "originalCorrelationId", "issues"],
                    ("outcome", "ref:CommandOutcome"),
                    ("resource", "oneOf:ref:FinanceAllocationView|type:null"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string"),
                    ("issues", "type:array;items:ref:ValidationIssue")),
                ["FinanceAllocationStatus"] = ScalarSchema("enum:Active|Released"),
                ["FinanceAllocationView"] = ObjectSchema(
                    ["id", "companyId", "sourceId", "openItemId", "currencyId", "amount", "status", "version", "reason", "createdBy", "createdAtUtc", "releasedBy", "releasedAtUtc", "releaseReason"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("sourceId", "type:string;format:uuid"),
                    ("openItemId", "type:string;format:uuid"),
                    ("currencyId", "type:string;format:uuid"),
                    ("amount", "type:string"),
                    ("status", "ref:FinanceAllocationStatus"),
                    ("version", "type:string"),
                    ("reason", "type:string"),
                    ("createdBy", "type:string"),
                    ("createdAtUtc", "type:string;format:date-time"),
                    ("releasedBy", "type:null|string"),
                    ("releasedAtUtc", "type:null|string;format:date-time"),
                    ("releaseReason", "type:null|string")),
                ["ManualBankPaymentLinkedDocument"] = ObjectSchema(
                    ["documentId", "version"],
                    ("documentId", "type:string;format:uuid"),
                    ("version", "type:string")),
                ["ManualBankPaymentPage"] = ObjectSchema(
                    ["companyId", "items", "nextCursor"],
                    ("companyId", "type:string;format:uuid"),
                    ("items", "type:array;items:ref:ManualBankPaymentView"),
                    ("nextCursor", "type:null|string")),
                ["ManualBankPaymentPreparation"] = ObjectSchema(
                    ["partyId", "companyAccountId", "treasuryAccountId", "money", "profile", "occurredOn", "valueOn", "bankReference", "description"],
                    ("partyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("treasuryAccountId", "type:string;format:uuid"),
                    ("money", "ref:MoneyText"),
                    ("profile", "ref:ProfileReference"),
                    ("occurredOn", "type:string;format:date"),
                    ("valueOn", "type:null|string;format:date"),
                    ("bankReference", "type:null|string"),
                    ("description", "type:null|string")),
                ["ManualBankPaymentStatus"] = ScalarSchema("enum:Draft|Approved|Recorded"),
                ["ManualBankPaymentView"] = ObjectSchema(
                    ["id", "companyId", "status", "preparation", "linkedDocument", "preparedBy", "preparedAtUtc", "approvedBy", "approvedAtUtc", "recordedBy", "recordedAtUtc", "bankResultConfirmed", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("status", "ref:ManualBankPaymentStatus"),
                    ("preparation", "ref:ManualBankPaymentPreparation"),
                    ("linkedDocument", "oneOf:ref:ManualBankPaymentLinkedDocument|type:null"),
                    ("preparedBy", "type:string"),
                    ("preparedAtUtc", "type:string;format:date-time"),
                    ("approvedBy", "type:null|string"),
                    ("approvedAtUtc", "type:null|string;format:date-time"),
                    ("recordedBy", "type:null|string"),
                    ("recordedAtUtc", "type:null|string;format:date-time"),
                    ("bankResultConfirmed", "type:boolean"),
                    ("version", "type:string")),
                ["ReversalProposalStatus"] = ScalarSchema("enum:Pending|Approved|Rejected"),
                ["ReversalProposalView"] = ObjectSchema(
                    ["id", "originalSourceId", "occurredOn", "reason", "status", "proposedBy", "proposedAtUtc",
                        "decidedBy", "decidedAtUtc", "decisionReason", "reversalSourceId", "version"],
                    ("id", "type:string;format:uuid"),
                    ("originalSourceId", "type:string;format:uuid"),
                    ("occurredOn", "type:string;format:date"),
                    ("reason", "type:string"),
                    ("status", "ref:ReversalProposalStatus"),
                    ("proposedBy", "type:string"),
                    ("proposedAtUtc", "type:string;format:date-time"),
                    ("decidedBy", "type:null|string"),
                    ("decidedAtUtc", "type:null|string;format:date-time"),
                    ("decisionReason", "type:null|string"),
                    ("reversalSourceId", "type:null|string;format:uuid"),
                    ("version", "type:string")),
                ["ReversalProposalPage"] = ObjectSchema(
                    ["items"],
                    ("items", "type:array;items:ref:ReversalProposalView")),
                ["ReversalProposalCommandResult"] = ObjectSchema(
                    ["outcome", "resource", "isReplay", "originalCorrelationId", "issues"],
                    ("outcome", "ref:CommandOutcome"),
                    ("resource", "oneOf:ref:ReversalProposalView|type:null"),
                    ("isReplay", "type:boolean"),
                    ("originalCorrelationId", "type:string"),
                    ("issues", "type:array;items:ref:ValidationIssue")),
                ["MoneyText"] = ObjectSchema(
                    ["amount", "currencyId"],
                    ("amount", "type:string"),
                    ("currencyId", "type:string;format:uuid")),
                ["SourceDetail"] = ObjectSchema(
                    ["source", "profile", "bankReference", "description", "recordedAtUtc", "reversalSourceId", "reversesSourceId", "unallocated"],
                    ("source", "ref:SourceSummary"),
                    ("profile", "ref:ProfileReference"),
                    ("bankReference", "type:null|string"),
                    ("description", "type:null|string"),
                    ("recordedAtUtc", "type:null|string;format:date-time"),
                    ("reversalSourceId", "type:null|string;format:uuid"),
                    ("reversesSourceId", "type:null|string;format:uuid"),
                    ("unallocated", "ref:UnallocatedView")),
                ["SourcePage"] = ObjectSchema(
                    ["companyId", "items", "nextCursor"],
                    ("companyId", "type:string;format:uuid"),
                    ("items", "type:array;items:ref:SourceSummary"),
                    ("nextCursor", "type:null|string")),
                ["SourceStage"] = ScalarSchema("enum:Draft|Recorded|Reversed"),
                ["SourceSummary"] = ObjectSchema(
                    ["id", "companyId", "sourceNumber", "version", "stage", "partyId", "companyAccountId", "treasuryAccountId", "currencyId", "occurredOn", "valueOn", "amount"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("sourceNumber", "type:string"),
                    ("version", "type:string"),
                    ("stage", "ref:SourceStage"),
                    ("partyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("treasuryAccountId", "type:string;format:uuid"),
                    ("currencyId", "type:string;format:uuid"),
                    ("occurredOn", "type:string;format:date"),
                    ("valueOn", "type:null|string;format:date"),
                    ("amount", "ref:AmountView")),
                ["UnallocatedView"] = ObjectSchema(
                    ["knowledge", "amount"],
                    ("knowledge", "ref:AllocationKnowledge"),
                    ("amount", "ref:AmountView")),
                ["ValidationIssue"] = ObjectSchema(
                    ["field", "code"],
                    ("field", "type:string"),
                    ("code", "type:string")),
                ["ManufacturingOutputProductCandidatesV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:ManufacturingOutputProductCandidateV1"),
                    ("searchWindowFull", "type:boolean")),
                ["ManufacturingOutputProductCandidateV1"] = ObjectSchema(
                    ["productId", "productVersion", "code", "name", "stockUnitCode"],
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("stockUnitCode", "type:string")),
                ["ManufacturingOutputLocationCandidatesV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:ManufacturingOutputLocationCandidateV1"),
                    ("searchWindowFull", "type:boolean")),
                ["ManufacturingOutputLocationCandidateV1"] = ObjectSchema(
                    ["locationId", "locationCode", "locationName", "siteId", "siteCode", "siteName"],
                    ("locationId", "type:string;format:uuid"),
                    ("locationCode", "type:string"),
                    ("locationName", "type:string"),
                    ("siteId", "type:string;format:uuid"),
                    ("siteCode", "type:string"),
                    ("siteName", "type:string")),
                ["OrderViewV1"] = ObjectSchema(
                    ["orderId", "companyId", "planId", "workCenterId", "process", "status", "version", "target", "remainingTarget", "rolls", "costStatus", "closedByUserId", "closedAtUtc"],
                    ("orderId", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("planId", "type:string;format:uuid"),
                    ("workCenterId", "type:string;format:uuid"),
                    ("process", "type:string"),
                    ("status", "type:string"),
                    ("version", "type:integer|string;format:int64"),
                    ("target", "oneOf:ref:TargetViewV1|type:null"),
                    ("remainingTarget", "ref:RemainingTargetViewV1"),
                    ("rolls", "type:array;items:ref:RollBalanceViewV1"),
                    ("costStatus", "type:string"),
                    ("closedByUserId", "type:null|string;format:uuid"),
                    ("closedAtUtc", "type:null|string;format:date-time")),
                ["PlannedInputViewV1"] = ObjectSchema(
                    ["parentRollId", "plannedKg"],
                    ("parentRollId", "type:string;format:uuid"),
                    ("plannedKg", "type:string")),
                ["PlanViewV1"] = ObjectSchema(
                    ["planId", "companyId", "workCenterId", "process", "status", "version", "inputs", "target", "approvedByUserId", "approvedAtUtc"],
                    ("planId", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("workCenterId", "type:string;format:uuid"),
                    ("process", "type:string"),
                    ("status", "type:string"),
                    ("version", "type:integer|string;format:int64"),
                    ("inputs", "type:array;items:ref:PlannedInputViewV1"),
                    ("target", "oneOf:ref:TargetViewV1|type:null"),
                    ("approvedByUserId", "type:null|string;format:uuid"),
                    ("approvedAtUtc", "type:null|string;format:date-time")),
                ["RemainingTargetViewV1"] = ObjectSchema(
                    ["basis", "targetKg", "achievedKg", "remainingKg"],
                    ("basis", "type:null|string"),
                    ("targetKg", "type:null|string"),
                    ("achievedKg", "type:null|string"),
                    ("remainingKg", "type:null|string")),
                ["RollBalanceViewV1"] = ObjectSchema(
                    ["parentRollId", "plannedKg", "issuedKg", "returnedKg", "netIssuedKg", "outputsKg", "workInProgressKg", "unexplainedKg", "isFullyAccounted"],
                    ("parentRollId", "type:string;format:uuid"),
                    ("plannedKg", "type:string"),
                    ("issuedKg", "type:string"),
                    ("returnedKg", "type:string"),
                    ("netIssuedKg", "type:string"),
                    ("outputsKg", "type:object"),
                    ("workInProgressKg", "type:string"),
                    ("unexplainedKg", "type:string"),
                    ("isFullyAccounted", "type:boolean")),
                ["TargetViewV1"] = ObjectSchema(
                    ["basis", "kg"],
                    ("basis", "type:string"),
                    ("kg", "type:string")),
                ["BankAccountView"] = ObjectSchema(
                    ["id", "companyId", "currencyId", "displayName", "isActive", "isUsableForReceipt", "version"],
                    ("id", "type:string;format:uuid"), ("companyId", "type:string;format:uuid"),
                    ("currencyId", "type:string;format:uuid"), ("displayName", "type:string"),
                    ("isActive", "type:boolean"), ("isUsableForReceipt", "type:boolean"),
                    ("version", "type:string")),
                ["DraftCurrencyContext"] = ObjectSchema(
                    ["currencyId", "displayCode", "profile"],
                    ("currencyId", "type:string;format:uuid"), ("displayCode", "type:string"),
                    ("profile", "ref:DraftEffectiveProfile")),
                ["DraftEffectiveProfile"] = ObjectSchema(
                    ["reference", "scale", "effectiveFrom", "effectiveThrough"],
                    ("reference", "ref:ProfileReference"), ("scale", "type:integer|string;format:int32"),
                    ("effectiveFrom", "type:string;format:date"),
                    ("effectiveThrough", "type:null|string;format:date")),
                ["DraftPartyLabel"] = ObjectSchema(
                    ["partyId", "companyAccountId", "code", "legalName", "tradeName", "partyVersion", "companyAccountVersion"],
                    ("partyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"), ("code", "type:string"),
                    ("legalName", "type:string"), ("tradeName", "type:null|string"),
                    ("partyVersion", "type:string"), ("companyAccountVersion", "type:string")),
                ["DraftReadContextView"] = ObjectSchema(
                    ["companyId", "occurredOn", "bankAccount", "currency", "party"],
                    ("companyId", "type:string;format:uuid"), ("occurredOn", "type:string;format:date"),
                    ("bankAccount", "ref:BankAccountView"), ("currency", "ref:DraftCurrencyContext"),
                    ("party", "ref:DraftPartyLabel")),
                ["ProfileReference"] = ObjectSchema(
                    ["profileId", "version"], ("profileId", "type:string"), ("version", "type:string")),
                ["SalesAccountOptionV1"] = ObjectSchema(
                    ["partyId", "companyAccountId", "label", "partyVersion", "accountVersion"],
                    ("partyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("label", "type:string"), ("partyVersion", "type:string"),
                    ("accountVersion", "type:string")),
                ["SalesAccountOptionsV1"] = ObjectSchema(
                    ["companyId", "accounts", "nextCursor"],
                    ("companyId", "type:string;format:uuid"),
                    ("accounts", "type:array;items:ref:SalesAccountOptionV1"),
                    ("nextCursor", "type:null|string")),
                ["SalesCurrencyReferenceV1"] = ObjectSchema(
                    ["companyId", "effectiveOn", "currencyId", "code", "label", "profileId", "profileVersion"],
                    ("companyId", "type:string;format:uuid"),
                    ("effectiveOn", "type:string;format:date"),
                    ("currencyId", "type:string;format:uuid"),
                    ("code", "type:string"), ("label", "type:string"),
                    ("profileId", "type:string;format:uuid"), ("profileVersion", "type:string")),
                ["SalesProductReferenceV1"] = ObjectSchema(
                    ["companyId", "effectiveOn", "productId", "productVersion", "label", "orderUnits"],
                    ("companyId", "type:string;format:uuid"),
                    ("effectiveOn", "type:string;format:date"),
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"), ("label", "type:string"),
                    ("orderUnits", "type:array;items:ref:SalesUnitReferenceV1")),
                ["SalesUnitReferenceV1"] = ObjectSchema(
                    ["unitId", "label", "profileId", "profileVersion"],
                    ("unitId", "type:string;format:uuid"), ("label", "type:string"),
                    ("profileId", "type:string;format:uuid"), ("profileVersion", "type:string")),
                ["ProductSalesCandidatesV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:ProductSalesCandidateV1"),
                    ("searchWindowFull", "type:boolean")),
                ["ProductSalesCandidateV1"] = ObjectSchema(
                    ["productId", "productVersion", "code", "name", "orderUnits"],
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"), ("code", "type:string"), ("name", "type:string"),
                    ("orderUnits", "type:array;items:ref:ProductSalesCandidateUnitV1")),
                ["ProductSalesCandidateUnitV1"] = ObjectSchema(
                    ["unitId", "code", "profileId", "profileVersion"],
                    ("unitId", "type:string;format:uuid"), ("code", "type:string"),
                    ("profileId", "type:string;format:uuid"), ("profileVersion", "type:string")),
                ["ProductPurchaseCandidatesV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:ProductPurchaseCandidateV1"),
                    ("searchWindowFull", "type:boolean")),
                ["ProductPurchaseCandidateV1"] = ObjectSchema(
                    ["productId", "productVersion", "code", "name", "unitId", "unitCode", "profileId", "profileVersion"],
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"), ("code", "type:string"), ("name", "type:string"),
                    ("unitId", "type:string;format:uuid"), ("unitCode", "type:string"),
                    ("profileId", "type:string;format:uuid"), ("profileVersion", "type:string")),
                ["ProductStockCandidatesV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:ProductStockCandidateV1"),
                    ("searchWindowFull", "type:boolean")),
                ["ProductStockCandidateV1"] = ObjectSchema(
                    ["productId", "productVersion", "code", "name", "stockUnitId", "stockUnitCode", "profileId", "profileVersion"],
                    ("productId", "type:string;format:uuid"),
                    ("productVersion", "type:string"), ("code", "type:string"), ("name", "type:string"),
                    ("stockUnitId", "type:string;format:uuid"), ("stockUnitCode", "type:string"),
                    ("profileId", "type:string;format:uuid"), ("profileVersion", "type:string")),
                ["OrganizationLocationCandidatesV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:OrganizationLocationCandidateV1"),
                    ("searchWindowFull", "type:boolean")),
                ["OrganizationLocationCandidateV1"] = ObjectSchema(
                    ["locationId", "locationCode", "locationName", "siteId", "siteCode", "siteName"],
                    ("locationId", "type:string;format:uuid"), ("locationCode", "type:string"),
                    ("locationName", "type:string"), ("siteId", "type:string;format:uuid"),
                    ("siteCode", "type:string"), ("siteName", "type:string")),
                ["CounterpartyContactInformationView"] = ObjectSchema(
                    ["partyId", "accountId", "companyId", "version", "isActive", "information"],
                    ("partyId", "type:string;format:uuid"),
                    ("accountId", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("version", "type:string"),
                    ("isActive", "type:boolean"),
                    ("information", "ref:CounterpartyContactInformation")),
                ["CounterpartyContactInformation"] = ObjectSchema(
                    ["phone", "phoneExtension", "email", "website", "addresses", "contacts"],
                    ("phone", "type:null|string"),
                    ("phoneExtension", "type:null|string"),
                    ("email", "type:null|string"),
                    ("website", "type:null|string"),
                    ("addresses", "type:array;items:ref:CounterpartyOperationalAddress"),
                    ("contacts", "type:array;items:ref:CounterpartyOperationalContact")),
                ["CounterpartyOperationalAddress"] = ObjectSchema(
                    ["id", "version", "isActive", "purpose", "countryCode", "line1", "line2", "city", "district", "postalCode"],
                    ("id", "type:string;format:uuid"),
                    ("version", "type:string"),
                    ("isActive", "type:boolean"),
                    ("purpose", "type:string"),
                    ("countryCode", "type:string"),
                    ("line1", "type:string"),
                    ("line2", "type:null|string"),
                    ("city", "type:null|string"),
                    ("district", "type:null|string"),
                    ("postalCode", "type:null|string")),
                ["CounterpartyOperationalContact"] = ObjectSchema(
                    ["id", "version", "isActive", "displayName", "jobTitle", "purpose", "channels"],
                    ("id", "type:string;format:uuid"),
                    ("version", "type:string"),
                    ("isActive", "type:boolean"),
                    ("displayName", "type:string"),
                    ("jobTitle", "type:null|string"),
                    ("purpose", "type:string"),
                    ("channels", "type:array;items:ref:CounterpartyContactChannel")),
                ["CounterpartyContactChannel"] = ObjectSchema(
                    ["id", "version", "isActive", "kind", "value", "extension"],
                    ("id", "type:string;format:uuid"),
                    ("version", "type:string"),
                    ("isActive", "type:boolean"),
                    ("kind", "type:string"),
                    ("value", "type:string"),
                    ("extension", "type:null|string")),
                ["AuditEventItem"] = ObjectSchema(
                [
                    "id", "companyId", "occurredAtUtc", "eventType", "entityType",
                    "entityId", "operation", "entityVersionBefore", "entityVersionAfter",
                    "actorType", "actorId", "permissionCode", "roleCodes", "beforeJson",
                    "afterJson", "reason", "approvalContextJson", "aiInvolvement",
                    "aiContextJson", "sourceChannel", "clientIpAddress", "userAgent",
                    "correlationId", "causationId", "outcome", "schemaVersion",
                    "integrityHash",
                ],
                ("id", "type:string;format:uuid"),
                ("companyId", "type:string;format:uuid"),
                ("occurredAtUtc", "type:string;format:date-time"),
                ("eventType", "type:string"),
                ("entityType", "type:string"),
                ("entityId", "type:string"),
                ("operation", "type:string"),
                ("entityVersionBefore", "type:integer|null;format:int64"),
                ("entityVersionAfter", "type:integer|null;format:int64"),
                ("actorType", "type:string"),
                ("actorId", "type:string"),
                ("permissionCode", "type:string"),
                ("roleCodes", "type:array;items:type:string"),
                ("beforeJson", "type:string"),
                ("afterJson", "type:string"),
                ("reason", "type:string"),
                ("approvalContextJson", "type:null|string"),
                ("aiInvolvement", "type:string"),
                ("aiContextJson", "type:null|string"),
                ("sourceChannel", "type:string"),
                ("clientIpAddress", "type:null|string"),
                ("userAgent", "type:null|string"),
                ("correlationId", "type:string"),
                ("causationId", "type:null|string"),
                ("outcome", "type:string"),
                ("schemaVersion", "type:integer;format:int32"),
                ("integrityHash", "type:string")),
                ["AuditEventPage"] = ObjectSchema(
                    ["items", "pageSize", "nextCursor"],
                    ("items", "type:array;items:ref:AuditEventItem"),
                    ("pageSize", "type:integer;format:int32"),
                    ("nextCursor", "type:null|string")),
                ["CompanyContextResponse"] = ObjectSchema(
                    ["companyId"],
                    ("companyId", "type:string;format:uuid")),
                ["CounterpartyDetailAccountView"] = ObjectSchema(
                    ["id", "companyId", "roles", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("roles", "ref:PartyRole"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string")),
                ["CounterpartyDetailView"] = ObjectSchema(
                    ["id", "code", "kind", "legalName", "tradeName", "isActive", "version", "companyAccounts"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string"),
                    ("companyAccounts", "type:array;items:ref:CounterpartyDetailAccountView")),
                ["CounterpartyListAccount"] = ObjectSchema(
                    ["id", "companyId", "roles", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("roles", "ref:PartyRole"),
                    ("isActive", "type:boolean"),
                    ("version", "type:integer;format:int64")),
                ["CounterpartyListItem"] = ObjectSchema(
                    ["id", "code", "kind", "legalName", "tradeName", "isActive", "version", "companyAccounts"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("isActive", "type:boolean"),
                    ("version", "type:integer;format:int64"),
                    ("companyAccounts", "type:array;items:ref:CounterpartyListAccount")),
                ["CounterpartyLinkCandidateSourceAccount"] = ObjectSchema(
                    ["id", "companyId", "roles", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("roles", "ref:PartyRole"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string")),
                ["CounterpartyLinkCandidateItem"] = ObjectSchema(
                    ["id", "code", "kind", "legalName", "tradeName", "isActive", "visibleSourceAccounts"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("isActive", "type:boolean"),
                    ("visibleSourceAccounts", "type:array;items:ref:CounterpartyLinkCandidateSourceAccount")),
                ["CounterpartyLinkCandidatePage"] = ObjectSchema(
                    ["items", "nextCursor", "hasMore"],
                    ("items", "type:array;items:ref:CounterpartyLinkCandidateItem"),
                    ("nextCursor", "type:null|string"),
                    ("hasMore", "type:boolean")),
                ["CounterpartyMutationAccountView"] = ObjectSchema(
                    ["id", "companyId", "roles", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("companyId", "type:string;format:uuid"),
                    ("roles", "ref:PartyRole"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string")),
                ["CounterpartyMutationView"] = ObjectSchema(
                    ["id", "code", "kind", "legalName", "tradeName", "isActive", "version", "companyAccounts"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("isActive", "type:boolean"),
                    ("version", "type:string"),
                    ("companyAccounts", "type:array;items:ref:CounterpartyMutationAccountView")),
                ["CounterpartyPage"] = ObjectSchema(
                    ["items", "nextCursor", "hasMore", "totalCount", "countScope"],
                    ("items", "type:array;items:ref:CounterpartyListItem"),
                    ("nextCursor", "type:null|string"),
                    ("hasMore", "type:boolean"),
                    ("totalCount", "type:integer|null|string;format:int64"),
                    ("countScope", "type:null|string")),
                ["CompanySummary"] = ObjectSchema(
                    ["id", "code", "legalName", "isPlaceholder"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("legalName", "type:string"),
                    ("isPlaceholder", "type:boolean")),
                ["IdentityProfile"] = ObjectSchema(
                    ["userId", "displayName", "email", "allowedCompanyIds"],
                    ("userId", "type:string;format:uuid"),
                    ("displayName", "type:string"),
                    ("email", "type:null|string"),
                    ("allowedCompanyIds", "type:array;items:type:string;format:uuid")),
                ["InventoryLabelViewV1"] = ObjectSchema(
                    ["labelId", "companyId", "code", "productId", "lotId", "heatNumber", "coilNumber",
                        "documentStatus", "requiredDocuments", "documents", "prints", "lot"],
                    ("labelId", "type:string;format:uuid"), ("companyId", "type:string;format:uuid"),
                    ("code", "type:string"), ("productId", "type:string;format:uuid"),
                    ("lotId", "type:string;format:uuid"), ("heatNumber", "type:null|string"),
                    ("coilNumber", "type:null|string"), ("documentStatus", "type:string"),
                    ("requiredDocuments", "type:array;items:type:string"),
                    ("documents", "type:array;items:ref:InventoryLabelDocumentV1"),
                    ("prints", "type:array;items:ref:InventoryLabelPrintV1"),
                    ("lot", "ref:InventoryLotV1")),
                // T07 lot record (#324): the label's lot, one per (company, lot).
                ["InventoryLotV1"] = ObjectSchema(
                    ["id", "heatNumber", "coilNumber"],
                    ("id", "type:string;format:uuid"), ("heatNumber", "type:null|string"),
                    ("coilNumber", "type:null|string")),
                ["InventoryLabelDocumentV1"] = ObjectSchema(
                    ["kind", "attachedAtUtc"],
                    ("kind", "type:string"), ("attachedAtUtc", "type:string;format:date-time")),
                ["InventoryLabelPrintV1"] = ObjectSchema(
                    ["sequence", "printedAtUtc", "reason"],
                    ("sequence", "type:integer|string;format:int32"),
                    ("printedAtUtc", "type:string;format:date-time"), ("reason", "type:null|string")),
                ["InventoryLabelBalancesV1"] = ObjectSchema(
                    ["labelId", "physicalKg", "freeKg", "blockedKg", "positions"],
                    ("labelId", "type:string;format:uuid"), ("physicalKg", "type:string"),
                    ("freeKg", "type:string"), ("blockedKg", "type:string"),
                    ("positions", "type:array;items:ref:InventoryPositionV1")),
                ["InventoryLabelDetailV1"] = ObjectSchema(
                    ["labelVersion", "identity", "balances", "genealogy", "movementTimeline"],
                    ("labelVersion", "type:string"), ("identity", "ref:InventoryLabelViewV1"),
                    ("balances", "ref:InventoryLabelBalancesV1"),
                    ("genealogy", "ref:InventoryLabelGenealogyV1"),
                    ("movementTimeline", "ref:InventoryLabelMovementTimelineV1")),
                ["InventoryLabelMovementTimelineV1"] = ObjectSchema(
                    ["labelId", "entries", "windowFull"],
                    ("labelId", "type:string;format:uuid"),
                    ("entries", "type:array;items:ref:InventoryLabelMovementV1"),
                    ("windowFull", "type:boolean")),
                ["InventoryLabelMovementV1"] = ObjectSchema(
                    ["movementId", "kind", "occurredAtUtc", "sourceKind", "sourceId", "sourceLineId",
                        "sourceGrain", "lines"],
                    ("movementId", "type:string;format:uuid"), ("kind", "type:string"),
                    ("occurredAtUtc", "type:string;format:date-time"), ("sourceKind", "type:string"),
                    ("sourceId", "type:string;format:uuid"),
                    ("sourceLineId", "type:null|string;format:uuid"), ("sourceGrain", "type:string"),
                    ("lines", "type:array;items:ref:InventoryLabelMovementLineV1")),
                ["InventoryLabelMovementLineV1"] = ObjectSchema(
                    ["sequence", "direction", "ownerKind", "ownerId", "custodianKind", "custodianId",
                        "locationId", "productId", "block", "quantityKg"],
                    ("sequence", "type:integer|string;format:int32"), ("direction", "type:string"),
                    ("ownerKind", "type:string"), ("ownerId", "type:string;format:uuid"),
                    ("custodianKind", "type:string"), ("custodianId", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"), ("productId", "type:string;format:uuid"),
                    ("block", "type:string"), ("quantityKg", "type:string")),
                ["InventoryLabelGenealogyV1"] = ObjectSchema(
                    ["labelId", "labelCode", "edges", "windowFull"],
                    ("labelId", "type:string;format:uuid"), ("labelCode", "type:string"),
                    ("edges", "type:array;items:ref:InventoryLabelGenealogyEdgeV1"),
                    ("windowFull", "type:boolean")),
                ["InventoryLabelGenealogyEdgeV1"] = ObjectSchema(
                    ["transformationId", "kind", "occurredAtUtc", "sourceLabelId", "sourceCode",
                        "sourceLabelVersion", "sourceQuantityKg", "outputLabelId", "outputCode",
                        "outputLabelVersion", "outputQuantityKg"],
                    ("transformationId", "type:string;format:uuid"), ("kind", "type:string"),
                    ("occurredAtUtc", "type:string;format:date-time"),
                    ("sourceLabelId", "type:string;format:uuid"), ("sourceCode", "type:string"),
                    ("sourceLabelVersion", "type:string"), ("sourceQuantityKg", "type:string"),
                    ("outputLabelId", "type:string;format:uuid"), ("outputCode", "type:string"),
                    ("outputLabelVersion", "type:string"), ("outputQuantityKg", "type:string")),
                ["InventoryParentRollCandidatesV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:InventoryParentRollReferenceV1"),
                    ("searchWindowFull", "type:boolean")),
                ["InventoryStockPositionsV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:InventoryStockPositionV1"),
                    ("searchWindowFull", "type:boolean")),
                ["InventoryStockPositionV1"] = ObjectSchema(
                    ["companyId", "labelId", "labelVersion", "labelCode", "productId", "lot",
                        "heatNumber", "coilNumber", "documentStatus", "ownerKind", "ownerId",
                        "custodianKind", "custodianId", "locationId", "physicalKg", "freePhysicalKg",
                        "blockedPhysicalKg", "activeAllocationKg", "availableKg"],
                    ("companyId", "type:string;format:uuid"),
                    ("labelId", "type:string;format:uuid"),
                    ("labelVersion", "type:string"),
                    ("labelCode", "type:string"),
                    ("productId", "type:string;format:uuid"),
                    ("lot", "ref:InventoryLotV1"),
                    ("heatNumber", "type:null|string"),
                    ("coilNumber", "type:null|string"),
                    ("documentStatus", "type:string"),
                    ("ownerKind", "type:string"),
                    ("ownerId", "type:string;format:uuid"),
                    ("custodianKind", "type:string"),
                    ("custodianId", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"),
                    ("physicalKg", "type:string"),
                    ("freePhysicalKg", "type:string"),
                    ("blockedPhysicalKg", "type:string"),
                    ("activeAllocationKg", "type:string"),
                    ("availableKg", "type:string")),
                ["InventoryParentRollReferenceV1"] = ObjectSchema(
                    ["companyId", "labelId", "labelVersion", "labelCode", "productId", "lot",
                        "heatNumber", "coilNumber", "documentStatus", "requiredDocuments", "attachedDocuments",
                        "physicalKg", "unblockedPhysicalKg", "blockedPhysicalKg", "positions", "draftSelectable",
                        "draftRefusalCode", "productionUseRequiresRevalidation"],
                    ("companyId", "type:string;format:uuid"),
                    ("labelId", "type:string;format:uuid"),
                    ("labelVersion", "type:string"),
                    ("labelCode", "type:string"),
                    ("productId", "type:string;format:uuid"),
                    ("lot", "ref:InventoryLotV1"),
                    ("heatNumber", "type:null|string"),
                    ("coilNumber", "type:null|string"),
                    ("documentStatus", "type:string"),
                    ("requiredDocuments", "type:array;items:type:string"),
                    ("attachedDocuments", "type:array;items:type:string"),
                    ("physicalKg", "type:string"),
                    ("unblockedPhysicalKg", "type:string"),
                    ("blockedPhysicalKg", "type:string"),
                    ("positions", "type:array;items:ref:InventoryPositionV1"),
                    ("draftSelectable", "type:boolean"),
                    ("draftRefusalCode", "type:null|string"),
                    ("productionUseRequiresRevalidation", "type:boolean")),
                ["InventoryPhysicalAllocationViewV1"] = ObjectSchema(
                    ["allocationId", "companyId", "version", "status", "labelId", "productId", "demandKind", "demandId", "demandLineId", "demandProductVersion", "allocatedKg", "remainingKg", "reason", "reservedUntilUtc", "createdAtUtc", "updatedAtUtc"],
                    ("allocationId", "type:string;format:uuid"), ("companyId", "type:string;format:uuid"),
                    ("version", "type:string"), ("status", "type:string"),
                    ("labelId", "type:string;format:uuid"), ("productId", "type:string;format:uuid"),
                    ("demandKind", "type:string"), ("demandId", "type:string;format:uuid"),
                    ("demandLineId", "type:string;format:uuid"), ("demandProductVersion", "type:string"),
                    ("allocatedKg", "type:string"), ("remainingKg", "type:string"), ("reason", "type:string"),
                    ("reservedUntilUtc", "type:null|string;format:date-time"),
                    ("createdAtUtc", "type:string;format:date-time"), ("updatedAtUtc", "type:string;format:date-time")),
                ["InventoryPhysicalAllocationReadsV1"] = ObjectSchema(
                    ["items", "searchWindowFull"],
                    ("items", "type:array;items:ref:InventoryPhysicalAllocationReadItemV1"),
                    ("searchWindowFull", "type:boolean")),
                ["InventoryPhysicalAllocationReadItemV1"] = ObjectSchema(
                    ["allocation", "labelVersion"],
                    ("allocation", "ref:InventoryPhysicalAllocationViewV1"),
                    ("labelVersion", "type:string")),
                ["InventoryLabelTransformationViewV1"] = ObjectSchema(
                    ["transformationId", "companyId", "kind", "occurredAtUtc", "reason", "sourceLabelIds", "outputLabelIds"],
                    ("transformationId", "type:string;format:uuid"), ("companyId", "type:string;format:uuid"),
                    ("kind", "type:string"), ("occurredAtUtc", "type:string;format:date-time"),
                    ("reason", "type:string"),
                    ("sourceLabelIds", "type:array;items:type:string;format:uuid"),
                    ("outputLabelIds", "type:array;items:type:string;format:uuid")),
                ["OpeningCountViewV1"] = ObjectSchema(
                    ["countId", "companyId", "locationId", "countedOn", "sheetReference", "lines"],
                    ("countId", "type:string;format:uuid"), ("companyId", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"), ("countedOn", "type:string;format:date"),
                    ("sheetReference", "type:string"), ("lines", "type:array;items:ref:OpeningCountLineViewV1")),
                ["OpeningCountLineViewV1"] = ObjectSchema(
                    ["lineId", "labelId", "labelCode", "lotId", "heatNumber", "coilNumber", "productId", "quantityKg"],
                    ("lineId", "type:string;format:uuid"), ("labelId", "type:string;format:uuid"),
                    ("labelCode", "type:string"), ("lotId", "type:string;format:uuid"),
                    ("heatNumber", "type:null|string"), ("coilNumber", "type:null|string"),
                    ("productId", "type:string;format:uuid"), ("quantityKg", "type:string")),
                ["InventoryPositionV1"] = ObjectSchema(
                    ["ownerKind", "ownerId", "custodianKind", "custodianId", "locationId", "productId", "block", "quantityKg"],
                    ("ownerKind", "type:string"), ("ownerId", "type:string;format:uuid"),
                    ("custodianKind", "type:string"), ("custodianId", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"), ("productId", "type:string;format:uuid"),
                    ("block", "type:string"), ("quantityKg", "type:string")),
                ["LocationSummary"] = ObjectSchema(
                    ["id", "siteId", "code", "name", "isExternal", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("siteId", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("isExternal", "type:boolean"),
                    ("isActive", "type:boolean"),
                    ("version", "type:integer;format:int64")),
                ["PageResultOfLocationSummary"] = ObjectSchema(
                    ["items", "page", "pageSize", "totalCount"],
                    ("items", "type:array;items:ref:LocationSummary"),
                    ("page", "type:integer;format:int32"),
                    ("pageSize", "type:integer;format:int32"),
                    ("totalCount", "type:integer;format:int32")),
                ["PartyKind"] = ScalarSchema("type:integer"),
                ["PartyRole"] = ScalarSchema("type:integer"),
                ["ShellCapabilitySnapshot"] = ObjectSchema(
                    ["contractVersion", "generation", "capabilities"],
                    ("contractVersion", "type:string"),
                    ("generation", "type:string"),
                    ("capabilities", "type:array;items:type:string")),
                // ORG master edit: the site row carries its integer row version, which an edit sends back.
                ["SiteSummary"] = ObjectSchema(
                    ["id", "code", "name", "type", "isActive", "version"],
                    ("id", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("type", "type:string"),
                    ("isActive", "type:boolean"),
                    ("version", "type:integer;format:int64")),
                ["OrganizationCreatedRecord"] = ObjectSchema(
                    ["id", "version"],
                    ("id", "type:string;format:uuid"),
                    ("version", "type:integer;format:int64")),
                // ORG-03: canOpenLocationDetail is REQUIRED and boolean on purpose — the client must not
                // have to treat "absent" as either answer, because the field is the server's statement
                // about authorization and a default would be the client deciding one.
                ["WorkCenterSummary"] = ObjectSchema(
                    ["id", "locationId", "code", "name", "kind", "isActive", "canOpenLocationDetail"],
                    ("id", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("kind", "type:string"),
                    ("isActive", "type:boolean"),
                    ("canOpenLocationDetail", "type:boolean")),
                // ORG-04: the card adds the row version to the list row's fields.
                ["WorkCenterDetail"] = ObjectSchema(
                    ["id", "locationId", "code", "name", "kind", "isActive", "version", "canOpenLocationDetail"],
                    ("id", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("kind", "type:string"),
                    ("isActive", "type:boolean"),
                    ("version", "type:integer;format:int64"),
                    ("canOpenLocationDetail", "type:boolean")),
            };

    private static readonly IReadOnlyDictionary<string, ComponentSchemaExpectation>
        ExpectedRequestComponentSchemas =
            new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
            {
                ["ApproveAccountingJournalReversalRequestV1"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:integer|string;format:int32")),
                ["ApproveNormalJournalDraftRequestV1"] = ObjectSchema(
                    ["expectedVersion", "periodId"],
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("periodId", "type:string;format:uuid")),
                ["CreateNormalJournalDraftLineRequestV1"] = ObjectSchema(
                    ["accountId", "side", "amount"],
                    ("accountId", "type:string;format:uuid"),
                    ("side", "type:string"),
                    ("amount", "type:string")),
                ["CreateNormalJournalDraftRequestV1"] = ObjectSchema(
                    ["draftId", "sourceId", "transactionDate", "accountingDate", "valueDate", "lines"],
                    ("draftId", "type:string;format:uuid"),
                    ("sourceId", "type:string;format:uuid"),
                    ("transactionDate", "type:string;format:date"),
                    ("accountingDate", "type:string;format:date"),
                    ("valueDate", "type:null|string;format:date"),
                    ("lines", "type:array;items:ref:CreateNormalJournalDraftLineRequestV1")),
                ["ProposeAccountingJournalReversalRequestV1"] = ObjectSchema(
                    ["proposalId", "sourceJournalId", "periodId", "transactionDate", "accountingDate", "valueDate", "reason"],
                    ("proposalId", "type:string;format:uuid"),
                    ("sourceJournalId", "type:string;format:uuid"),
                    ("periodId", "type:string;format:uuid"),
                    ("transactionDate", "type:string"),
                    ("accountingDate", "type:string"),
                    ("valueDate", "type:null|string"),
                    ("reason", "type:string")),
                ["CloseOrderRequestV1"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:integer|string;format:int64")),
// shared request/success: []
                ["PeriodCommandRequestV1"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:integer|string;format:int64")),
                ["ProposePeriodRequestV1"] = ObjectSchema(
                    ["periodId", "startsOn", "endsOn"],
                    ("periodId", "type:string;format:uuid"),
                    ("startsOn", "type:string;format:date"),
                    ("endsOn", "type:string;format:date")),
                ["ApprovePurchaseOrderRequest"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:string")),
                ["RecordPurchaseGoodsReceiptV1"] = ObjectSchema(
                    ["expectedPurchaseOrderVersion", "expectedProductVersion", "acceptanceId", "acceptedQuantity", "measuredQuantityKg", "locationId", "lotId", "labelId", "labelCode", "heatNumber", "coilNumber", "movementId", "occurredAtUtc"],
                    ("expectedPurchaseOrderVersion", "type:string"),
                    ("expectedProductVersion", "type:string"),
                    ("acceptanceId", "type:string;format:uuid"),
                    ("acceptedQuantity", "type:string"),
                    ("measuredQuantityKg", "type:string"),
                    ("locationId", "type:string;format:uuid"),
                    ("lotId", "type:string;format:uuid"),
                    ("labelId", "type:string;format:uuid"),
                    ("labelCode", "type:string"),
                    ("heatNumber", "type:null|string"),
                    ("coilNumber", "type:null|string"),
                    ("movementId", "type:string;format:uuid"),
                    ("occurredAtUtc", "type:string;format:date-time")),
                ["CreatePurchaseOrderRequest"] = ObjectSchema(
                    ["supplierPartyId", "companyAccountId", "sourceRequestId", "effectiveOn", "lines"],
                    ("supplierPartyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("sourceRequestId", "type:null|string;format:uuid"),
                    ("effectiveOn", "type:string;format:date"),
                    ("lines", "type:array;items:ref:PurchaseOrderLineRequest")),
                ["CreatePurchaseRequestRequest"] = ObjectSchema(
                    ["effectiveOn", "lines"],
                    ("effectiveOn", "type:string;format:date"),
                    ("lines", "type:array;items:ref:PurchaseRequestLineRequest")),
                ["PurchaseOrderLineRequest"] = ObjectSchema(
                    ["lineId", "productId", "expectedProductVersion", "unitId", "quantity", "sourceRequestLineId"],
                    ("lineId", "type:string;format:uuid"),
                    ("productId", "type:string;format:uuid"),
                    ("expectedProductVersion", "type:string"),
                    ("unitId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("sourceRequestLineId", "type:null|string;format:uuid")),
                ["PurchaseRequestLineRequest"] = ObjectSchema(
                    ["lineId", "productId", "expectedProductVersion", "unitId", "quantity"],
                    ("lineId", "type:string;format:uuid"),
                    ("productId", "type:string;format:uuid"),
                    ("expectedProductVersion", "type:string"),
                    ("unitId", "type:string;format:uuid"),
                    ("quantity", "type:string")),
                ["SubmitPurchaseRequestRequest"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:string")),
                ["CreateLoadingInstructionRequest"] = ObjectSchema(
                    ["customerPartyId", "companyAccountId", "effectiveOn", "lines"],
                    ("customerPartyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("effectiveOn", "type:string;format:date"),
                    ("lines", "type:array;items:ref:LoadingInstructionLineRequest")),
                ["IssueLoadingInstructionRequest"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:string")),
                ["LoadingInstructionLineRequest"] = ObjectSchema(
                    ["lineId", "sourceOrderId", "sourceOrderLineId", "unitId", "quantity"],
                    ("lineId", "type:string;format:uuid"),
                    ("sourceOrderId", "type:string;format:uuid"),
                    ("sourceOrderLineId", "type:string;format:uuid"),
                    ("unitId", "type:string;format:uuid"),
                    ("quantity", "type:string")),
                ["RecordLoadRequest"] = ObjectSchema(
                    ["expectedVersion", "loadId", "quantity", "measurement"],
                    ("expectedVersion", "type:string"),
                    ("loadId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("measurement", "type:string")),
                ["RecordShipmentRequest"] = ObjectSchema(
                    ["expectedVersion", "shipmentId", "allocationId", "expectedAllocationVersion",
                        "expectedLabelVersion", "movementId", "quantity", "occurredAtUtc"],
                    ("expectedVersion", "type:string"),
                    ("shipmentId", "type:string;format:uuid"),
                    ("allocationId", "type:string;format:uuid"),
                    ("expectedAllocationVersion", "type:string"),
                    ("expectedLabelVersion", "type:string"),
                    ("movementId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("occurredAtUtc", "type:string;format:date-time")),
                ["RecordDeliveryRequest"] = ObjectSchema(
                    ["expectedVersion", "deliveryId", "sourceShipmentId", "quantity", "occurredAtUtc"],
                    ("expectedVersion", "type:string"),
                    ("deliveryId", "type:string;format:uuid"),
                    ("sourceShipmentId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("occurredAtUtc", "type:string;format:date-time")),
                ["OpeningCountRequestV1"] = ObjectSchema(
                    ["countId", "locationId", "countedOn", "sheetReference", "lines"],
                    ("countId", "type:string;format:uuid"), ("locationId", "type:string;format:uuid"),
                    ("countedOn", "type:string;format:date"), ("sheetReference", "type:string"),
                    ("lines", "type:array;items:ref:OpeningCountLineRequestV1")),
                ["CreateInventoryPhysicalAllocationRequestV1"] = ObjectSchema(
                    ["allocationId", "salesOrderId", "salesOrderLineId", "labelId", "expectedLabelVersion", "quantityKg", "reason", "reservedUntilUtc"],
                    ("allocationId", "type:string;format:uuid"), ("salesOrderId", "type:string;format:uuid"),
                    ("salesOrderLineId", "type:string;format:uuid"), ("labelId", "type:string;format:uuid"),
                    ("expectedLabelVersion", "type:string"), ("quantityKg", "type:string"),
                    ("reason", "type:string"), ("reservedUntilUtc", "type:null|string;format:date-time")),
                ["ReleasePhysicalAllocationV1"] = ObjectSchema(
                    ["expectedAllocationVersion", "reason"],
                    ("expectedAllocationVersion", "type:string"), ("reason", "type:string")),
                ["TransformInventoryLabelsV1"] = ObjectSchema(
                    ["transformationId", "kind", "sources", "outputs", "occurredAtUtc", "reason"],
                    ("transformationId", "type:string;format:uuid"),
                    ("kind", "ref:InventoryLabelTransformationKindV1"),
                    ("sources", "type:array;items:ref:InventoryLabelTransformationSourceV1"),
                    ("outputs", "type:array;items:ref:InventoryLabelTransformationOutputV1"),
                    ("occurredAtUtc", "type:string;format:date-time"), ("reason", "type:string")),
                ["InventoryLabelTransformationSourceV1"] = ObjectSchema(
                    ["labelId", "expectedLabelVersion"],
                    ("labelId", "type:string;format:uuid"), ("expectedLabelVersion", "type:string")),
                ["InventoryLabelTransformationOutputV1"] = ObjectSchema(
                    ["labelId", "code", "quantityKg"],
                    ("labelId", "type:string;format:uuid"), ("code", "type:string"), ("quantityKg", "type:string")),
                ["OpeningCountLineRequestV1"] = ObjectSchema(
                    ["lineId", "labelId", "labelCode", "lotId", "heatNumber", "coilNumber", "productId", "productVersion", "quantityKg"],
                    ("lineId", "type:string;format:uuid"), ("labelId", "type:string;format:uuid"),
                    ("labelCode", "type:string"), ("lotId", "type:string;format:uuid"),
                    ("heatNumber", "type:null|string"), ("coilNumber", "type:null|string"),
                    ("productId", "type:string;format:uuid"), ("productVersion", "type:string"),
                    ("quantityKg", "type:string")),
                ["BankAccountApprovalBody"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:string")),
                ["BankAccountIbanChangeBody"] = ObjectSchema(
                    ["expectedVersion", "iban", "reason"],
                    ("expectedVersion", "type:string"),
                    ("iban", "type:string"),
                    ("reason", "type:string")),
                // K2 requires bankName and accountType; B3 additionally requires the explicit money
                // movement declaration. Legacy database NULL remains Undeclared and fail-closed, but a
                // new proposal cannot omit or null this wire-level boolean.
                ["BankAccountProposeRequest"] = ObjectSchema(
                    ["displayName", "currencyId", "bankName", "accountType", "moneyMovementEligible"],
                    ("displayName", "type:string"),
                    ("currencyId", "type:string;format:uuid"),
                    ("bankName", "type:string"),
                    ("accountType", "type:string"),
                    ("moneyMovementEligible", "type:boolean"),
                    ("branchName", "type:null|string"),
                    ("branchCode", "type:null|string"),
                    ("iban", "type:null|string"),
                    ("accountNumber", "type:null|string")),
                ["CreateSalesOrderDraftRequest"] = ObjectSchema(
                    ["kind", "partyId", "companyAccountId", "businessDate", "currencyId", "lines"],
                    ("kind", "type:string"),
                    ("partyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("businessDate", "type:string;format:date"),
                    ("currencyId", "type:string;format:uuid"),
                    ("lines", "type:array;items:ref:SalesOrderDraftLineRequest"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("note", "type:null|string"),
                    ("deliveryAddress", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("contact", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("responsible", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("paymentTerms", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("sourceStrategy", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null"),
                    ("package", "oneOf:ref:SalesDraftOwnerReferenceV1|type:null")),
                ["SalesDraftStateCommandRequest"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:string"),
                    ("reason", "type:null|string")),
                ["SalesDraftCommercialRevisionRequest"] = ObjectSchema(
                    ["expectedVersion", "lines"],
                    ("expectedVersion", "type:string"),
                    ("lines", "type:array;items:ref:SalesDraftLineCommercialV1")),
                ["SalesDraftLineCommercialV1"] = ObjectSchema(
                    ["lineId", "unitPrice", "priceUnitId", "discountPercent"],
                    ("lineId", "type:string;format:uuid"),
                    ("unitPrice", "type:null|string"),
                    ("priceUnitId", "type:null|string;format:uuid"),
                    ("discountPercent", "type:null|string")),
                ["SalesDraftDeliveryScheduleRequest"] = ObjectSchema(
                    ["expectedVersion", "requestedDeliveryDate", "lines"],
                    ("expectedVersion", "type:string"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("lines", "type:array;items:ref:SalesDraftLineScheduleV1")),
                ["SalesDraftLineScheduleV1"] = ObjectSchema(
                    ["lineId", "requestedDeliveryDate", "slices"],
                    ("lineId", "type:string;format:uuid"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("slices", "type:array;items:ref:SalesDraftSliceScheduleV1")),
                ["SalesDraftSliceScheduleV1"] = ObjectSchema(
                    ["sliceId", "requestedDeliveryDate"],
                    ("sliceId", "type:string;format:uuid"),
                    ("requestedDeliveryDate", "type:string;format:date")),
                ["SalesOrderDraftLineRequest"] = ObjectSchema(
                    ["lineId", "productId", "expectedProductVersion", "orderUnitId", "quantity"],
                    ("lineId", "type:string;format:uuid"),
                    ("productId", "type:string;format:uuid"),
                    ("expectedProductVersion", "type:string"),
                    ("orderUnitId", "type:string;format:uuid"),
                    ("quantity", "type:string"),
                    ("unitPrice", "type:null|string"),
                    ("priceUnitId", "type:null|string;format:uuid"),
                    ("requestedDeliveryDate", "type:null|string;format:date"),
                    ("note", "type:null|string"),
                    ("requestedDeliverySlices", "type:array|null;items:ref:SalesDraftDeliverySliceV1"),
                    ("discountPercent", "type:null|string")),
                ["ApproveManualBankPaymentRequest"] = ObjectSchema(
                    ["paymentId", "expectedVersion"],
                    ("paymentId", "type:string;format:uuid"),
                    ("expectedVersion", "type:string")),
                ["ManualBankPaymentDraftSaveRequest"] = ObjectSchema(
                    ["paymentId", "expectedVersion", "preparation", "linkedDocument"],
                    ("paymentId", "type:null|string;format:uuid"),
                    ("expectedVersion", "type:null|string"),
                    ("preparation", "ref:ManualBankPaymentPreparation"),
                    ("linkedDocument", "oneOf:ref:ManualBankPaymentLinkedDocumentRequest|type:null")),
                ["ManualBankPaymentLinkedDocumentRequest"] = ObjectSchema(
                    ["documentId", "expectedVersion"],
                    ("documentId", "type:string;format:uuid"),
                    ("expectedVersion", "type:string")),
                ["RecordManualBankPaymentRequest"] = ObjectSchema(
                    ["paymentId", "expectedVersion", "bankResultConfirmed"],
                    ("paymentId", "type:string;format:uuid"),
                    ("expectedVersion", "type:string"),
                    ("bankResultConfirmed", "type:boolean")),
                ["CreateFinanceAllocationRequest"] = ObjectSchema(
                    ["allocationId", "sourceId", "expectedSourceVersion", "openItemId", "expectedOpenItemVersion", "amount", "reason"],
                    ("allocationId", "type:string;format:uuid"),
                    ("sourceId", "type:string;format:uuid"),
                    ("expectedSourceVersion", "type:string"),
                    ("openItemId", "type:string;format:uuid"),
                    ("expectedOpenItemVersion", "type:string"),
                    ("amount", "type:string"),
                    ("reason", "type:string")),
                ["ReleaseFinanceAllocationRequest"] = ObjectSchema(
                    ["allocationId", "expectedAllocationVersion", "expectedOpenItemVersion", "reason"],
                    ("allocationId", "type:string;format:uuid"),
                    ("expectedAllocationVersion", "type:string"),
                    ("expectedOpenItemVersion", "type:string"),
                    ("reason", "type:string")),
                ["DraftSaveRequest"] = ObjectSchema(
                    ["draftId", "expectedVersion", "preparation"],
                    ("draftId", "type:null|string;format:uuid"),
                    ("expectedVersion", "type:null|string"),
                    ("preparation", "ref:ReceiptPreparation")),
                ["ReceiptPreparation"] = ObjectSchema(
                    ["partyId", "companyAccountId", "treasuryAccountId", "money", "profile", "occurredOn", "valueOn", "bankReference", "description"],
                    ("partyId", "type:string;format:uuid"),
                    ("companyAccountId", "type:string;format:uuid"),
                    ("treasuryAccountId", "type:string;format:uuid"),
                    ("money", "ref:MoneyText"),
                    ("profile", "ref:ProfileReference"),
                    ("occurredOn", "type:string;format:date"),
                    ("valueOn", "type:null|string;format:date"),
                    ("bankReference", "type:null|string"),
                    ("description", "type:null|string")),
                // FIN-T05: Record names the draft it makes definitive and the version it expects.
                // Neither property is nullable, unlike DraftSaveRequest's — a save may create, a
                // record may only act on something that already exists at a known version.
                ["RecordRequest"] = ObjectSchema(
                    ["draftId", "expectedVersion"],
                    ("draftId", "type:string;format:uuid"),
                    ("expectedVersion", "type:string")),
                ["ProposeReversalRequest"] = ObjectSchema(
                    ["sourceId", "expectedVersion", "occurredOn", "reason"],
                    ("sourceId", "type:string;format:uuid"),
                    ("expectedVersion", "type:string"),
                    ("occurredOn", "type:string;format:date"),
                    ("reason", "type:string")),
                ["ApproveReversalRequest"] = ObjectSchema(
                    ["proposalId", "expectedVersion"],
                    ("proposalId", "type:string;format:uuid"),
                    ("expectedVersion", "type:string")),
                ["RejectReversalRequest"] = ObjectSchema(
                    ["proposalId", "expectedVersion", "reason"],
                    ("proposalId", "type:string;format:uuid"),
                    ("expectedVersion", "type:string"),
                    ("reason", "type:string")),
                ["ApprovePlanRequestV1"] = ObjectSchema(
                    ["expectedVersion"],
                    ("expectedVersion", "type:integer|string;format:int64")),
                ["CreateOrderRequestV1"] = ObjectSchema(
                    ["orderId", "planId"],
                    ("orderId", "type:string;format:uuid"),
                    ("planId", "type:string;format:uuid")),
                ["IssueInventoryInputRequestV1"] = ObjectSchema(
                    ["issueId", "expectedVersion", "parentRollId", "movementId", "kg", "occurredAtUtc"],
                    ("issueId", "type:string;format:uuid"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("parentRollId", "type:string;format:uuid"),
                    ("movementId", "type:string;format:uuid"),
                    ("kg", "type:null|string"),
                    ("occurredAtUtc", "type:null|string;format:date-time")),
                ["ReturnInventoryInputRequestV1"] = ObjectSchema(
                    ["returnId", "expectedVersion", "parentRollId", "expectedParentLabelVersion", "movementId",
                        "kg", "occurredAtUtc"],
                    ("returnId", "type:string;format:uuid"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("parentRollId", "type:string;format:uuid"),
                    ("expectedParentLabelVersion", "type:null|string"),
                    ("movementId", "type:string;format:uuid"),
                    ("kg", "type:null|string"),
                    ("occurredAtUtc", "type:null|string;format:date-time")),
                ["DeclareInventoryOutputRequestV1"] = ObjectSchema(
                    ["outputId", "expectedVersion", "parentRollId", "expectedParentLabelVersion", "outputClass",
                        "productId", "expectedProductVersion", "lotId", "labelId", "labelCode", "heatNumber",
                        "coilNumber", "requiredDocuments", "ownerKind", "ownerId", "custodianKind", "custodianId",
                        "locationId", "initialBlock", "movementId", "kg", "occurredAtUtc"],
                    ("outputId", "type:string;format:uuid"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("parentRollId", "type:string;format:uuid"),
                    ("expectedParentLabelVersion", "type:null|string"),
                    ("outputClass", "type:null|string"),
                    ("productId", "type:string;format:uuid"),
                    ("expectedProductVersion", "type:null|string"),
                    ("lotId", "type:string;format:uuid"),
                    ("labelId", "type:string;format:uuid"),
                    ("labelCode", "type:null|string"),
                    ("heatNumber", "type:null|string"),
                    ("coilNumber", "type:null|string"),
                    ("requiredDocuments", "type:array|null;items:type:string"),
                    ("ownerKind", "type:null|string"),
                    ("ownerId", "type:string;format:uuid"),
                    ("custodianKind", "type:null|string"),
                    ("custodianId", "type:string;format:uuid"),
                    ("locationId", "type:string;format:uuid"),
                    ("initialBlock", "type:null|string"),
                    ("movementId", "type:string;format:uuid"),
                    ("kg", "type:null|string"),
                    ("occurredAtUtc", "type:null|string;format:date-time")),
                ["PlannedInputRequestV1"] = ObjectSchema(
                    ["parentRollId", "plannedKg", "expectedLabelVersion"],
                    ("parentRollId", "type:string;format:uuid"),
                    ("plannedKg", "type:null|string"),
                    ("expectedLabelVersion", "type:string")),
                ["RecordFactRequestV1"] = ObjectSchema(
                    ["factId", "expectedVersion", "parentRollId", "kg", "occurredAtUtc"],
                    ("factId", "type:string;format:uuid"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("parentRollId", "type:string;format:uuid"),
                    ("kg", "type:null|string"),
                    ("occurredAtUtc", "type:null|string;format:date-time")),
                ["SavePlanRequestV1"] = ObjectSchema(
                    ["planId", "workCenterId", "inputs", "target"],
                    ("planId", "type:string;format:uuid"),
                    ("workCenterId", "type:string;format:uuid"),
                    ("inputs", "type:array|null;items:ref:PlannedInputRequestV1"),
                    ("target", "oneOf:ref:TargetRequestV1|type:null")),
                ["TargetRequestV1"] = ObjectSchema(
                    ["basis", "kg"],
                    ("basis", "type:null|string"),
                    ("kg", "type:null|string")),
                ["UpdateCounterpartyContactInformation"] = ObjectSchema(
                    ["expectedVersion", "changes"],
                    ("expectedVersion", "type:string"),
                    ("changes", "ref:CounterpartyContactInformationChanges")),
                ["CounterpartyContactInformationChanges"] = ObjectSchema(
                    [],
                    ("phone", "type:null|string"),
                    ("phoneExtension", "type:null|string"),
                    ("email", "type:null|string"),
                    ("website", "type:null|string"),
                    ("addresses", "type:array;items:ref:CounterpartyAddressChange"),
                    ("contacts", "type:array;items:ref:CounterpartyContactChange")),
                ["CounterpartyAddressChange"] = ObjectSchema(
                    ["id", "operation"],
                    ("id", "type:string;format:uuid;non-empty-uuid:true"),
                    ("operation", "type:string"),
                    ("purpose", "type:string"),
                    ("countryCode", "type:string"),
                    ("line1", "type:string"),
                    ("line2", "type:null|string"),
                    ("city", "type:null|string"),
                    ("district", "type:null|string"),
                    ("postalCode", "type:null|string")),
                ["CounterpartyContactChange"] = ObjectSchema(
                    ["id", "operation"],
                    ("id", "type:string;format:uuid;non-empty-uuid:true"),
                    ("operation", "type:string"),
                    ("displayName", "type:string"),
                    ("jobTitle", "type:null|string"),
                    ("purpose", "type:string"),
                    ("channels", "type:array;items:ref:CounterpartyContactChannelChange")),
                ["CounterpartyContactChannelChange"] = ObjectSchema(
                    ["id", "operation"],
                    ("id", "type:string;format:uuid;non-empty-uuid:true"),
                    ("operation", "type:string"),
                    ("kind", "type:string"),
                    ("value", "type:string"),
                    ("extension", "type:null|string")),
                ["CreateCounterparty"] = ObjectSchema(
                    ["kind", "legalName"],
                    ("kind", "ref:PartyKind"),
                    ("legalName", "type:string"),
                    ("tradeName", "type:null|string"),
                    ("roles", "ref:PartyRole")),
                ["CreateCompanyPartyAccount"] = ObjectSchema(
                    [],
                    ("roles", "ref:PartyRole")),
                ["SetCompanyPartyAccountActiveState"] = ObjectSchema(
                    ["isActive", "expectedVersion", "reason"],
                    ("isActive", "type:boolean"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("reason", "type:string")),
                ["SetLocationActiveState"] = ObjectSchema(
                    ["isActive", "expectedVersion", "reason"],
                    ("isActive", "type:boolean"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("reason", "type:string")),
                // ORG master edit: code and name only; a site's type, a location's site and a work centre's location
                // and kind are fixed at creation, so an edit does not carry them.
                ["CreateSite"] = ObjectSchema(
                    ["code", "name", "type", "reason"],
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("type", "type:string"),
                    ("reason", "type:string")),
                ["UpdateSite"] = ObjectSchema(
                    ["code", "name", "expectedVersion", "reason"],
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("reason", "type:string")),
                ["CreateLocation"] = ObjectSchema(
                    ["siteId", "code", "name", "reason"],
                    ("siteId", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("reason", "type:string")),
                ["UpdateLocation"] = ObjectSchema(
                    ["code", "name", "expectedVersion", "reason"],
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("reason", "type:string")),
                ["CreateWorkCenter"] = ObjectSchema(
                    ["locationId", "code", "name", "kind", "reason"],
                    ("locationId", "type:string;format:uuid"),
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("kind", "type:string"),
                    ("reason", "type:string")),
                ["UpdateWorkCenter"] = ObjectSchema(
                    ["code", "name", "expectedVersion", "reason"],
                    ("code", "type:string"),
                    ("name", "type:string"),
                    ("expectedVersion", "type:integer|string;format:int64"),
                    ("reason", "type:string")),
            };

    private static readonly IReadOnlyDictionary<string, ComponentSchemaExpectation>
        ExpectedProblemComponentSchemas =
            new Dictionary<string, ComponentSchemaExpectation>(StringComparer.Ordinal)
            {
                ["ErpProblemDetailsContract"] = ObjectSchema(
                    ["type", "title", "status", "detail", "code"],
                    ("type", "type:string"),
                    ("title", "type:string"),
                    ("status", "type:integer;format:int32"),
                    ("detail", "type:string"),
                    ("code", "type:string"),
                    ("correlationId", "type:null|string"),
                    ("conflict", "oneOf:ref:ErpVersionConflictContract|type:null")),
                ["ErpCodeLessProblemDetailsContract"] = ObjectSchema(
                    ["type", "title", "status", "detail"],
                    ("type", "type:string"),
                    ("title", "type:string"),
                    ("status", "type:integer;format:int32"),
                    ("detail", "type:string"),
                    ("conflict", "oneOf:ref:ErpVersionConflictContract|type:null")),
                ["ErpVersionConflictContract"] = ObjectSchema(
                    ["expectedVersion", "currentVersion"],
                    ("expectedVersion", "type:integer;format:int64"),
                    ("currentVersion", "type:integer;format:int64")),
            };

    [Fact]
    public async Task BuildTimeContract_IsDeterministicSecretFreeAndDoesNotContactConfiguredDependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"goymen-erp-openapi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var first = await GenerateAsync(
                repositoryRoot,
                temporaryRoot,
                "Production",
                "first");
            var second = await GenerateAsync(
                repositoryRoot,
                temporaryRoot,
                "Production",
                "second");
            var development = await GenerateAsync(
                repositoryRoot,
                temporaryRoot,
                "Development",
                "development");

            Assert.Equal(first.Bytes, second.Bytes);
            Assert.Equal(first.Bytes, development.Bytes);
            Assert.Equal(0, first.AcceptedConnectionCount);
            Assert.Equal(0, second.AcceptedConnectionCount);
            Assert.Equal(0, development.AcceptedConnectionCount);

            var canonicalArtifactPath = Path.Combine(
                repositoryRoot,
                "artifacts",
                CanonicalFileName);
            Assert.True(
                File.Exists(canonicalArtifactPath),
                $"MSBuild canonical artifact was not emitted at {canonicalArtifactPath}.");
            Assert.Equal(
                first.Bytes,
                await File.ReadAllBytesAsync(canonicalArtifactPath));

            var contractText = Encoding.UTF8.GetString(first.Bytes);
            foreach (var forbiddenValue in new[]
                     {
                         first.SecretMarker,
                         second.SecretMarker,
                         development.SecretMarker,
                         repositoryRoot,
                         repositoryRoot.Replace('\\', '/'),
                         temporaryRoot,
                         temporaryRoot.Replace('\\', '/'),
                         Environment.MachineName,
                         Environment.UserName,
                         "openapi.invalid",
                         "127.0.0.1",
                         "DB_CONNECTION_STRING",
                         "Password=",
                         "SELECT ",
                     })
            {
                Assert.DoesNotContain(
                    forbiddenValue,
                    contractText,
                    StringComparison.OrdinalIgnoreCase);
            }

            AssertContractShape(first.Bytes);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EndpointDataSource_MatchesTheGeneratedContractExactly()
    {
        await using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        var routeEndpoints = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        Assert.NotEmpty(routeEndpoints);
        var runtimeOperations = routeEndpoints
            .SelectMany(endpoint => SnapshotRuntimeEndpoint(endpoint))
            .OrderBy(operation => operation.Path, StringComparer.Ordinal)
            .ThenBy(operation => operation.Method, StringComparer.Ordinal)
            .ToArray();
        var expected = ExpectedOperations
            .Select(operation => new EndpointSnapshot(
                operation.Method,
                operation.Path,
                operation.OperationId))
            .OrderBy(operation => operation.Path, StringComparer.Ordinal)
            .ThenBy(operation => operation.Method, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, runtimeOperations);
    }

    [Fact]
    public async Task JsonCommands_RejectUnsupportedAndMalformedBodiesAtTheAdvertisedBoundary()
    {
        using var factory = new GoymenErpApiFactory();
        using var client = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        var partyId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var locationId = Guid.Parse("20000000-0000-0000-0000-000000000101");
        var commands = new[]
        {
            (HttpMethod.Post, "/api/v1/counterparties"),
            (HttpMethod.Post, $"/api/v1/counterparties/{partyId}/company-account"),
            (HttpMethod.Put, $"/api/v1/counterparties/{partyId}/company-account/active-state"),
            (HttpMethod.Put, $"/api/v1/organization/locations/{locationId}/active-state"),
        };

        foreach (var (method, path) in commands)
        {
            using var unsupportedRequest = new HttpRequestMessage(method, path)
            {
                Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
            };
            using var unsupportedResponse = await client.SendAsync(unsupportedRequest);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupportedResponse.StatusCode);
            Assert.Null(unsupportedResponse.Content.Headers.ContentType);
            Assert.Empty(await unsupportedResponse.Content.ReadAsByteArrayAsync());

            using var malformedRequest = new HttpRequestMessage(method, path)
            {
                Content = new StringContent("{", Encoding.UTF8, "application/json"),
            };
            using var malformedResponse = await client.SendAsync(malformedRequest);
            Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
            Assert.Null(malformedResponse.Content.Headers.ContentType);
            Assert.Empty(await malformedResponse.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task PrimitiveQueryBindingFailures_UseTheAdvertisedEmptyBadRequestVariant()
    {
        using var factory = new GoymenErpApiFactory();
        using var scopedAdminClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        var probes = new[]
        {
            (Client: scopedAdminClient, Path: "/api/v1/audit/events?pageSize=not-an-integer"),
            (Client: scopedAdminClient, Path: "/api/v1/organization/locations?page=not-an-integer"),
            (Client: scopedAdminClient, Path: "/api/v1/organization/locations?pageSize=not-an-integer"),
        };

        foreach (var (client, path) in probes)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Content.Headers.ContentType);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task RequiredStateFieldsAndNonEmptyCompanyBoundaries_FailClosedBeforeDurableExecution()
    {
        using var factory = new GoymenErpApiFactory();
        using var organizationClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var counterpartyClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        var location = await factory.GetLocationIgnoringFiltersAsync(
            TestCompanyIds.CompanyA,
            "OSM-H6");
        var locationId = location.Id;
        var locationVersion = location.Version;

        var invalidLocationCommands = new[]
        {
            $"{{\"expectedVersion\":{locationVersion},\"reason\":\"isActive omission probe\"}}",
            "{\"isActive\":true,\"reason\":\"expectedVersion omission probe\"}",
            "{\"isActive\":true,\"expectedVersion\":0,\"reason\":\"zero version probe\"}",
            "{\"isActive\":true,\"expectedVersion\":\"-1\",\"reason\":\"quoted negative version probe\"}",
        };
        foreach (var json in invalidLocationCommands)
        {
            var key = $"openapi-invalid-org-{Guid.NewGuid():N}";
            using var response = await SendJsonCommandAsync(
                organizationClient,
                HttpMethod.Put,
                $"/api/v1/organization/locations/{locationId}/active-state",
                json,
                key);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await factory.GetCommandExecutionsAsync(key));
        }

        using var partyResponse = await SendJsonCommandAsync(
            counterpartyClient,
            HttpMethod.Post,
            "/api/v1/counterparties",
            "{\"kind\":1,\"legalName\":\"OpenAPI omission probe\",\"roles\":1}");
        Assert.Equal(HttpStatusCode.Created, partyResponse.StatusCode);
        using var partyDocument = JsonDocument.Parse(
            await partyResponse.Content.ReadAsStringAsync());
        var partyId = partyDocument.RootElement.GetProperty("id").GetGuid();
        var companyAccount = Assert.Single(
            partyDocument.RootElement.GetProperty("companyAccounts")
                .EnumerateArray(),
            account => account.GetProperty("companyId").GetGuid()
                == TestCompanyIds.CompanyA);
        var accountVersion = long.Parse(
            companyAccount.GetProperty("version").GetString()!,
            CultureInfo.InvariantCulture);

        var invalidCounterpartyCommands = new[]
        {
            $"{{\"expectedVersion\":{accountVersion},\"reason\":\"isActive omission probe\"}}",
            "{\"isActive\":true,\"reason\":\"expectedVersion omission probe\"}",
            "{\"isActive\":true,\"expectedVersion\":0,\"reason\":\"zero version probe\"}",
            "{\"isActive\":true,\"expectedVersion\":\"-1\",\"reason\":\"quoted negative version probe\"}",
        };
        foreach (var json in invalidCounterpartyCommands)
        {
            var key = $"openapi-invalid-cp-{Guid.NewGuid():N}";
            using var response = await SendJsonCommandAsync(
                counterpartyClient,
                HttpMethod.Put,
                $"/api/v1/counterparties/{partyId}/company-account/active-state",
                json,
                key);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await factory.GetCommandExecutionsAsync(key));
        }

        using var nilLink = await SendJsonCommandAsync(
            counterpartyClient,
            HttpMethod.Post,
            $"/api/v1/counterparties/{Guid.Empty}/company-account",
            "{\"roles\":1}");
        Assert.Equal(HttpStatusCode.BadRequest, nilLink.StatusCode);

        using var nilStatus = await SendJsonCommandAsync(
            counterpartyClient,
            HttpMethod.Put,
            $"/api/v1/counterparties/{Guid.Empty}/company-account/active-state",
            "{\"isActive\":true,\"expectedVersion\":1,\"reason\":\"nil guid probe\"}");
        Assert.Equal(HttpStatusCode.BadRequest, nilStatus.StatusCode);

        using var requiredEmptyCompanyClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            Guid.Empty);
        using var requiredEmptyCompany = await requiredEmptyCompanyClient.GetAsync(
            "/api/v1/organization/context");
        Assert.Equal(HttpStatusCode.BadRequest, requiredEmptyCompany.StatusCode);

        using var optionalEmptyCompanyClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            Guid.Empty);
        using var optionalEmptyCompany = await optionalEmptyCompanyClient.GetAsync(
            "/api/v1/counterparties");
        Assert.Equal(HttpStatusCode.BadRequest, optionalEmptyCompany.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendJsonCommandAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string json,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(
            "Idempotency-Key",
            idempotencyKey ?? $"openapi-probe-{Guid.NewGuid():N}");
        return await client.SendAsync(request);
    }

    [Fact]
    public void RuntimeProcess_CannotEnableBuildTimeModeThroughConfiguration()
    {
        Assert.False(OpenApiDocumentGenerationMode.IsActive);
        Assert.Equal("GetDocument.Insider", OpenApiDocumentGenerationMode.EntryAssemblyName);
    }

    [Fact]
    public void RestoredGraph_UsesTheExactPatchedOpenApiDependency()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assetsPath = ApiAssetsPath(repositoryRoot);
        using var assets = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var libraries = assets.RootElement.GetProperty("libraries");
        var openApiVersions = libraries.EnumerateObject()
            .Where(property => property.Name.StartsWith(
                "Microsoft.OpenApi/",
                StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["Microsoft.OpenApi/2.7.5"], openApiVersions);
        Assert.True(libraries.TryGetProperty(
            "Microsoft.AspNetCore.OpenApi/10.0.8",
            out _));
        Assert.True(libraries.TryGetProperty(
            "Microsoft.Extensions.ApiDescription.Server/10.0.11",
            out _));

        var packageRoot = assets.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(property => property.Name)
            .Single(path => Directory.Exists(Path.Combine(
                path,
                "microsoft.extensions.apidescription.server",
                "10.0.11")));
        var embeddedOpenApiAssembly = Path.Combine(
            packageRoot,
            "microsoft.extensions.apidescription.server",
            "10.0.11",
            "tools",
            "net10.0",
            "Microsoft.OpenApi.dll");
        Assert.True(
            File.Exists(embeddedOpenApiAssembly),
            $"Build tool OpenAPI assembly was not found: {embeddedOpenApiAssembly}");
        Assert.Equal(
            new Version(2, 7, 5, 0),
            AssemblyName.GetAssemblyName(embeddedOpenApiAssembly).Version);
    }

    private static async Task<GenerationResult> GenerateAsync(
        string repositoryRoot,
        string temporaryRoot,
        string environmentName,
        string runName)
    {
        var outputDirectory = Path.Combine(temporaryRoot, runName);
        Directory.CreateDirectory(outputDirectory);
        var secretMarker = $"openapi-secret-{runName}-{Guid.NewGuid():N}";

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var acceptedConnectionCount = 0;
        using var listenerCancellation = new CancellationTokenSource();
        var acceptLoop = Task.Run(async () =>
        {
            while (!listenerCancellation.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(
                        listenerCancellation.Token);
                    Interlocked.Increment(ref acceptedConnectionCount);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException) when (listenerCancellation.IsCancellationRequested)
                {
                    return;
                }
            }
        });

        var assetsPath = ApiAssetsPath(repositoryRoot);
        using var assets = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var packageRoot = assets.RootElement.GetProperty("packageFolders")
            .EnumerateObject()
            .Select(property => property.Name)
            .Single(path => Directory.Exists(Path.Combine(
                path,
                "microsoft.extensions.apidescription.server",
                "10.0.11")));
        var toolPath = Path.Combine(
            packageRoot,
            "microsoft.extensions.apidescription.server",
            "10.0.11",
            "tools",
            "dotnet-getdocument.dll");
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var apiAssembly = Path.Combine(
            repositoryRoot,
            "src",
            "backend",
            "GoymenERP.Api",
            "bin",
            configuration,
            "net10.0",
            "GoymenERP.Api.dll");
        Assert.True(File.Exists(toolPath), $"OpenAPI build tool was not found: {toolPath}");
        Assert.True(File.Exists(apiAssembly), $"API assembly was not found: {apiAssembly}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.Combine(
                repositoryRoot,
                "src",
                "backend",
                "GoymenERP.Api"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     toolPath,
                     "--assembly", apiAssembly,
                     "--file-list", Path.Combine(outputDirectory, "documents.cache"),
                     "--framework", ".NETCoreApp,Version=v10.0",
                     "--output", outputDirectory,
                     "--project", "GoymenERP.Api",
                     "--assets-file", assetsPath,
                     "--platform", "AnyCPU",
                     "--document-name", DocumentName,
                     "--file-name", Path.GetFileNameWithoutExtension(CanonicalFileName),
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = environmentName;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = environmentName;
        startInfo.Environment["DB_CONNECTION_STRING"] =
            $"Server=127.0.0.1,{endpoint.Port};Database={secretMarker};"
            + $"User Id={secretMarker};Password={secretMarker};Connect Timeout=1;";
        startInfo.Environment["ConnectionStrings__GoymenErp"] =
            startInfo.Environment["DB_CONNECTION_STRING"];
        startInfo.Environment["Authentication__Mode"] = "OidcJwtBearer";
        startInfo.Environment["Authentication__Oidc__Authority"] =
            $"https://127.0.0.1:{endpoint.Port}/{secretMarker}";
        startInfo.Environment["Authentication__Oidc__Audience"] = secretMarker;
        startInfo.Environment["OpenApi__DocumentGenerationMode"] = "false";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("OpenAPI generator process could not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var processTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(processTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("OpenAPI generation exceeded five minutes.");
        }
        finally
        {
            await Task.Delay(100);
            listenerCancellation.Cancel();
            listener.Stop();
            await acceptLoop;
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.True(
            process.ExitCode == 0,
            $"OpenAPI generation failed ({process.ExitCode}).\nOUT:\n{output}\nERR:\n{error}");
        var documentPath = Path.Combine(outputDirectory, CanonicalFileName);
        Assert.True(
            File.Exists(documentPath),
            $"Canonical OpenAPI artifact was not emitted at {documentPath}.");
        Assert.Equal(
            [documentPath],
            Directory.GetFiles(outputDirectory, "*.json", SearchOption.TopDirectoryOnly));
        return new GenerationResult(
            await File.ReadAllBytesAsync(documentPath),
            secretMarker,
            acceptedConnectionCount);
    }

    private static void AssertContractShape(byte[] contractBytes)
    {
        using var contract = JsonDocument.Parse(contractBytes);
        var root = contract.RootElement;
        Assert.Equal("3.1.1", root.GetProperty("openapi").GetString());
        Assert.Equal("Göymen ERP API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());
        Assert.False(root.TryGetProperty("servers", out var servers)
            && servers.GetArrayLength() > 0);
        var securitySchemes = root.GetProperty("components")
            .GetProperty("securitySchemes");
        var bearer = Assert.Single(securitySchemes.EnumerateObject());
        Assert.Equal("Bearer", bearer.Name);
        Assert.Equal("http", bearer.Value.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.Value.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.Value.GetProperty("bearerFormat").GetString());

        var operations = ReadOperations(root.GetProperty("paths"));
        Assert.Equal(139, operations.Count);
        Assert.Equal(
            139,
            operations.Select(operation => operation.OperationId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        var operationsById = operations.ToDictionary(
            operation => operation.OperationId,
            StringComparer.Ordinal);
        Assert.Equal(
            ExpectedOperations.Select(operation => operation.OperationId).Order(StringComparer.Ordinal),
            operationsById.Keys.Order(StringComparer.Ordinal));
        foreach (var expected in ExpectedOperations)
        {
            var actual = operationsById[expected.OperationId];
            if (IsProductOperation(expected.OperationId))
            {
                AssertProductOperation(expected, actual);
                continue;
            }
            Assert.Equal(expected.Method, actual.Method);
            Assert.Equal(expected.Path, actual.Path);
            var expectedResponses = expected.Responses.Order(StringComparer.Ordinal).ToArray();
            var actualResponses = SnapshotResponses(actual.Value).Order(StringComparer.Ordinal).ToArray();
            Assert.True(
                expectedResponses.SequenceEqual(actualResponses, StringComparer.Ordinal),
                $"{expected.OperationId} response drift. Expected: {string.Join(", ", expectedResponses)}; Actual: {string.Join(", ", actualResponses)}");
            Assert.Equal(
                ExpectedSuccessSchemas[expected.OperationId],
                SnapshotSuccessSchema(actual.Value));
            foreach (var response in actual.Value.GetProperty("responses").EnumerateObject())
            {
                if (response.Value.TryGetProperty("content", out var responseContent)
                    && responseContent.TryGetProperty(
                        "application/problem+json",
                        out var problemContent))
                {
                    AssertProblemResponseSchema(
                        expected.OperationId,
                        int.Parse(response.Name, CultureInfo.InvariantCulture),
                        problemContent.GetProperty("schema"));
                }
            }

            if (expected.OperationId == "CreateCounterpartyCompanyAccountV1")
            {
                Assert.Equal(
                    "#/components/schemas/CounterpartyMutationView",
                    actual.Value.GetProperty("responses")
                        .GetProperty("201")
                        .GetProperty("content")
                        .GetProperty("application/json")
                        .GetProperty("schema")
                        .GetProperty("$ref")
                        .GetString());
            }

            if (expected.RequestSchemaReference is null)
            {
                Assert.False(actual.Value.TryGetProperty("requestBody", out _),
                    $"{expected.OperationId} unexpectedly has a request body.");
            }
            else
            {
                var requestBody = actual.Value.GetProperty("requestBody");
                Assert.True(requestBody.GetProperty("required").GetBoolean());
                var requestContent = requestBody.GetProperty("content");
                Assert.Equal(["application/json"], requestContent.EnumerateObject()
                    .Select(content => content.Name));
                Assert.Equal(
                    expected.RequestSchemaReference,
                    requestContent.GetProperty("application/json")
                        .GetProperty("schema")
                        .GetProperty("$ref")
                        .GetString());
            }

            var nonHeaderParameters = ReadParameters(actual.Value)
                .Where(parameter => parameter.Location != "header")
                .Select(parameter => $"{parameter.Location}:{parameter.Name}:"
                    + parameter.Required.ToString().ToLowerInvariant()
                    + $":{parameter.Schema}")
                .ToArray();
            Assert.Equal(expected.Parameters, nonHeaderParameters);
            if (expected.OperationId == "GetCounterpartiesV1")
            {
                AssertCounterpartyPageQueryContract(actual.Value);
            }

            if (expected.OperationId == "GetCounterpartyCompanyAccountLinkCandidatesV1")
            {
                AssertCounterpartyLinkCandidateQueryContract(actual.Value);
            }

            if (expected.OperationId == "GetSalesAccountReferencesV1")
            {
                AssertSalesAccountQueryContract(actual.Value);
            }

            if (expected.OperationId == "GetSalesProductReferenceV1")
            {
                AssertSalesProductQueryContract(actual.Value);
            }

            if (expected.OperationId is "GetInventoryParentRollCandidatesV1" or "GetInventoryStockPositionsV1")
            {
                AssertInventoryParentRollQueryContract(actual.Value);
            }

            if (expected.OperationId == "GetInventoryPhysicalAllocationsV1")
            {
                AssertInventoryPhysicalAllocationReadQueryContract(actual.Value);
            }
        }

        var anonymousOperations = operations.Where(operation =>
                operation.OperationId is "GetLivenessV1" or "GetReadinessV1")
            .ToArray();
        Assert.Equal(2, anonymousOperations.Length);
        Assert.All(anonymousOperations, operation =>
            Assert.False(operation.Value.TryGetProperty("security", out _)));

        foreach (var operation in operations.Except(anonymousOperations))
        {
            var security = operation.Value.GetProperty("security");
            var requirement = Assert.Single(security.EnumerateArray());
            var bearerRequirement = Assert.Single(requirement.EnumerateObject());
            Assert.Equal("Bearer", bearerRequirement.Name);
            Assert.Empty(bearerRequirement.Value.EnumerateArray());
            Assert.Contains("401", ResponseCodes(operation.Value));
            Assert.Contains("403", ResponseCodes(operation.Value));
        }

        foreach (var operation in operations)
        {
            // The Product-specific matrix above checks every request/response/header
            // using its actual conditional problem shape, not the company V1 shape.
            if (IsProductOperation(operation.OperationId)) continue;
            var parameters = operation.Value.TryGetProperty("parameters", out var value)
                ? value.EnumerateArray().ToArray()
                : [];
            var expectedHeaderNames = new List<string>
            {
                "X-Correlation-Id",
                "X-Causation-Id",
            };
            var isCounterparty = operation.Path.StartsWith("/api/v1/counterparties", StringComparison.Ordinal);
            if (isCounterparty)
            {
                expectedHeaderNames.Add(CounterpartyClientContract.HeaderName);
                AssertHeaderSchema(parameters, CounterpartyClientContract.HeaderName,
                    required: operation.Method != "get", type: "string",
                    pattern: CounterpartyClientContract.VersionPattern);
            }
            if (RequiredCompanyOperations.Contains(operation.OperationId)
                || OptionalCompanyOperations.Contains(operation.OperationId))
            {
                expectedHeaderNames.Add("X-Company-Id");
            }

            if (IdempotentOperations.Contains(operation.OperationId))
            {
                expectedHeaderNames.Add("Idempotency-Key");
            }

            Assert.Equal(
                expectedHeaderNames.Order(StringComparer.Ordinal),
                parameters.Where(parameter =>
                        parameter.GetProperty("in").GetString() == "header")
                    .Select(parameter => parameter.GetProperty("name").GetString())
                    .Order(StringComparer.Ordinal));
            Assert.Contains(parameters, parameter =>
                parameter.GetProperty("in").GetString() == "header"
                && parameter.GetProperty("name").GetString() == "X-Correlation-Id");
            AssertHeaderSchema(
                parameters,
                "X-Correlation-Id",
                required: false,
                type: "string",
                minimumLength: 1,
                maximumLength: 128,
                pattern: "^[A-Za-z0-9._:-]+$");
            AssertHeaderSchema(
                parameters,
                "X-Causation-Id",
                required: false,
                type: "string",
                minimumLength: 1,
                maximumLength: 128,
                pattern: "^[A-Za-z0-9._:-]+$");

            var companyHeader = parameters.SingleOrDefault(parameter =>
                parameter.GetProperty("in").GetString() == "header"
                && parameter.GetProperty("name").GetString() == "X-Company-Id");
            if (RequiredCompanyOperations.Contains(operation.OperationId))
            {
                Assert.Equal(JsonValueKind.Object, companyHeader.ValueKind);
                AssertHeaderSchema(
                    parameters,
                    "X-Company-Id",
                    required: true,
                    type: "string",
                    format: "uuid",
                    nonEmptyUuid: true);
            }
            else if (OptionalCompanyOperations.Contains(operation.OperationId))
            {
                Assert.Equal(JsonValueKind.Object, companyHeader.ValueKind);
                AssertHeaderSchema(
                    parameters,
                    "X-Company-Id",
                    required: false,
                    type: "string",
                    format: "uuid",
                    nonEmptyUuid: true);
            }
            else
            {
                Assert.Equal(JsonValueKind.Undefined, companyHeader.ValueKind);
            }

            var idempotencyHeader = parameters.SingleOrDefault(parameter =>
                parameter.GetProperty("in").GetString() == "header"
                && parameter.GetProperty("name").GetString() == "Idempotency-Key");
            var expectsIdempotency = IdempotentOperations.Contains(operation.OperationId);
            var hasIdempotency = idempotencyHeader.ValueKind == JsonValueKind.Object;
            Assert.True(
                expectsIdempotency == hasIdempotency,
                $"Operation '{operation.OperationId}' idempotency header mismatch. Expected {expectsIdempotency}, actual {hasIdempotency}.");
            if (idempotencyHeader.ValueKind == JsonValueKind.Object)
            {
                AssertHeaderSchema(
                    parameters,
                    "Idempotency-Key",
                    required: true,
                    type: "string",
                    minimumLength: 1,
                    maximumLength: null,
                    pattern: "^(?!.*[,])(?=.*\\S).+$",
                    canonicalMaximumLength: 128);
            }

            foreach (var response in operation.Value.GetProperty("responses").EnumerateObject())
            {
                var responseHeaders = response.Value.GetProperty("headers");
                AssertResponseHeaderSchema(
                    responseHeaders,
                    "X-Correlation-Id",
                    "string",
                    minimumLength: 1,
                    maximumLength: 128,
                    pattern: "^[A-Za-z0-9._:-]+$");
                var numericStatusCode = int.Parse(
                    response.Name,
                    CultureInfo.InvariantCulture);
                var acknowledgmentExpected = isCounterparty;
                var hasAcknowledgment = responseHeaders.TryGetProperty(
                    CounterpartyClientContract.HeaderName, out _);
                Assert.True(
                    acknowledgmentExpected == hasAcknowledgment,
                    $"Operation '{operation.OperationId}' response '{response.Name}' counterparty acknowledgment mismatch. Expected {acknowledgmentExpected}, actual {hasAcknowledgment}.");
                if (acknowledgmentExpected)
                {
                    AssertResponseHeaderSchema(responseHeaders,
                        CounterpartyClientContract.HeaderName, "string",
                        pattern: CounterpartyClientContract.VersionPattern);
                    var acknowledgment = responseHeaders.GetProperty(CounterpartyClientContract.HeaderName);
                    Assert.False(acknowledgment.TryGetProperty("required", out var required)
                        && required.GetBoolean());
                    var description = acknowledgment.GetProperty("description").GetString();
                    Assert.Contains("handler-generated errors", description, StringComparison.Ordinal);
                    Assert.Contains("errors before the filter", description, StringComparison.Ordinal);
                    Assert.Contains("may omit it", description, StringComparison.Ordinal);
                    Assert.Contains("not command success or client readiness", description, StringComparison.Ordinal);
                    Assert.Contains("fresh, successful, validated GET", description, StringComparison.Ordinal);
                    Assert.Contains("Absence on an error is not proof of an old server", description,
                        StringComparison.Ordinal);
                }
                var replayHeadersExpected = ReplayableResponseStatuses
                    .GetValueOrDefault(operation.OperationId, [])
                    .Contains(numericStatusCode);
                var hasReplayHeader = responseHeaders.TryGetProperty(
                    "Idempotency-Replayed",
                    out _);
                var hasOriginalCorrelationHeader = responseHeaders.TryGetProperty(
                    "Idempotency-Original-Correlation-Id",
                    out _);
                Assert.True(
                    replayHeadersExpected == hasReplayHeader,
                    $"Operation '{operation.OperationId}' response '{response.Name}' replay header mismatch. Expected {replayHeadersExpected}, actual {hasReplayHeader}.");
                Assert.True(
                    replayHeadersExpected == hasOriginalCorrelationHeader,
                    $"Operation '{operation.OperationId}' response '{response.Name}' original-correlation header mismatch. Expected {replayHeadersExpected}, actual {hasOriginalCorrelationHeader}.");
                if (replayHeadersExpected)
                {
                    AssertResponseHeaderSchema(
                        responseHeaders,
                        "Idempotency-Replayed",
                        "boolean");
                    AssertResponseHeaderSchema(
                        responseHeaders,
                        "Idempotency-Original-Correlation-Id",
                        "string",
                        minimumLength: 1,
                        maximumLength: 128,
                        pattern: "^[A-Za-z0-9._:-]+$");
                }

                if (numericStatusCode >= StatusCodes.Status400BadRequest)
                {
                    var expectedProblemCodes = ExpectedProblemCodes(
                        operation.OperationId,
                        numericStatusCode);
                    var hasProblemCodes = response.Value.TryGetProperty(
                        "x-erp-problem-codes",
                        out var stableCodes);
                    Assert.Equal(expectedProblemCodes.Count > 0, hasProblemCodes);
                    if (hasProblemCodes)
                    {
                        Assert.Equal(
                            expectedProblemCodes,
                            stableCodes.EnumerateArray()
                                .Select(code => code.GetString())
                                .Order(StringComparer.Ordinal));
                    }
                }

                if (numericStatusCode == StatusCodes.Status400BadRequest)
                {
                    var expectedEmptyBodyVariant =
                        EmptyBodyPossible400Operations.Contains(operation.OperationId);
                    var hasEmptyBodyVariant = response.Value.TryGetProperty(
                            ErpOpenApiContract.EmptyBodyPossibleExtensionName,
                            out var emptyBodyPossible)
                        && emptyBodyPossible.ValueKind == JsonValueKind.True;
                    Assert.True(
                        expectedEmptyBodyVariant == hasEmptyBodyVariant,
                        $"Operation '{operation.OperationId}' response '{response.Name}' empty-body variant mismatch. Expected {expectedEmptyBodyVariant}, actual {hasEmptyBodyVariant}.");
                }
            }
        }

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertSchemaSemantics(schemas);
        AssertSuccessComponentSchemas(schemas);
        AssertProductSchemas(schemas);
        var mutationAccounts = schemas
            .GetProperty("CounterpartyMutationView")
            .GetProperty("properties")
            .GetProperty("companyAccounts");
        Assert.Equal(1, mutationAccounts.GetProperty("minItems").GetInt32());
        Assert.Equal(1, mutationAccounts.GetProperty("maxItems").GetInt32());
    }

    private static IReadOnlyList<OpenApiOperationSnapshot> ReadOperations(JsonElement paths)
    {
        var operations = new List<OpenApiOperationSnapshot>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject().Where(property =>
                         property.Name is "get" or "post" or "put" or "patch" or "delete"))
            {
                operations.Add(new OpenApiOperationSnapshot(
                    path.Name,
                    method.Name,
                    method.Value.GetProperty("operationId").GetString()
                        ?? throw new InvalidOperationException("OperationId is missing."),
                    method.Value));
            }
        }

        return operations;
    }

    private static IReadOnlyList<EndpointSnapshot> SnapshotRuntimeEndpoint(
        RouteEndpoint endpoint)
    {
        var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        Assert.False(
            string.IsNullOrWhiteSpace(name),
            $"HTTP RouteEndpoint '{endpoint.DisplayName}' has no stable endpoint name.");
        var methodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        Assert.NotNull(methodMetadata);
        Assert.NotEmpty(methodMetadata.HttpMethods);
        var path = NormalizeRoute(endpoint.RoutePattern.RawText);
        Assert.False(
            string.IsNullOrWhiteSpace(path),
            $"HTTP RouteEndpoint '{endpoint.DisplayName}' has no canonical route path.");
        return methodMetadata.HttpMethods
            .Select(method => new EndpointSnapshot(
                method.ToLowerInvariant(),
                path!,
                name!))
            .ToArray();
    }

    private static IReadOnlyList<ParameterSnapshot> ReadParameters(JsonElement operation)
    {
        if (!operation.TryGetProperty("parameters", out var parameters))
        {
            return [];
        }

        return parameters.EnumerateArray()
            .Select(parameter => new ParameterSnapshot(
                parameter.GetProperty("in").GetString()
                    ?? throw new InvalidOperationException("Parameter location is missing."),
                parameter.GetProperty("name").GetString()
                    ?? throw new InvalidOperationException("Parameter name is missing."),
                parameter.TryGetProperty("required", out var required)
                && required.GetBoolean(),
                DescribeParameterSchema(parameter.GetProperty("schema"))))
            .ToArray();
    }

    private static IReadOnlyList<string> SnapshotResponses(JsonElement operation)
    {
        return operation.GetProperty("responses")
            .EnumerateObject()
            .Select(response =>
            {
                IEnumerable<string> mediaTypes = response.Value.TryGetProperty(
                    "content",
                    out var content)
                    ? content.EnumerateObject()
                        .Select(mediaType => mediaType.Name)
                        .Order(StringComparer.Ordinal)
                    : [];
                return $"{response.Name}:{string.Join(',', mediaTypes)}";
            })
            .ToArray();
    }

    private static string SnapshotSuccessSchema(JsonElement operation)
    {
        // An operation may declare more than one 2xx (INV-OPEN-1 part 2: 201 on the record, 200 on a
        // replay); every one of them must then carry the same body, so one snapshot names them all.
        var snapshots = operation.GetProperty("responses").EnumerateObject()
            .Where(response => response.Name.StartsWith('2'))
            .Select(response => SnapshotSuccessBody(response.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(snapshots);
        return Assert.Single(snapshots);
    }

    private static string SnapshotSuccessBody(JsonElement success)
    {
        if (!success.TryGetProperty("content", out var content))
        {
            return "none";
        }

        var mediaType = Assert.Single(content.EnumerateObject());
        var schema = mediaType.Value.GetProperty("schema");
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return $"ref:{reference.GetString()?.Split('/').Last()}";
        }

        if (schema.TryGetProperty("items", out var items)
            && items.TryGetProperty("$ref", out var itemReference))
        {
            return $"array:{itemReference.GetString()?.Split('/').Last()}";
        }

        return $"type:{schema.GetProperty("type").GetString()}";
    }

    private static void AssertHeaderSchema(
        JsonElement[] parameters,
        string name,
        bool required,
        string type,
        int? minimumLength = null,
        int? maximumLength = null,
        string? pattern = null,
        string? format = null,
        int? canonicalMaximumLength = null,
        bool nonEmptyUuid = false)
    {
        var parameter = Assert.Single(parameters, candidate =>
            candidate.GetProperty("in").GetString() == "header"
            && candidate.GetProperty("name").GetString() == name);
        var actualRequired = parameter.TryGetProperty("required", out var requiredValue)
            && requiredValue.GetBoolean();
        Assert.True(
            required == actualRequired,
            $"Header '{name}' required mismatch. Expected {required}, actual {actualRequired}.");
        AssertStringSchema(
            parameter.GetProperty("schema"),
            type,
            minimumLength,
            maximumLength,
            pattern,
            format,
            canonicalMaximumLength);
        var actualNonEmptyUuid = parameter.GetProperty("schema").TryGetProperty(
                ErpOpenApiContract.NonEmptyUuidExtensionName,
                out var nonEmptyValue)
            && nonEmptyValue.ValueKind == JsonValueKind.True;
        Assert.True(
            nonEmptyUuid == actualNonEmptyUuid,
            $"Header '{name}' non-empty UUID mismatch. Expected {nonEmptyUuid}, actual {actualNonEmptyUuid}.");
    }

    private static void AssertResponseHeaderSchema(
        JsonElement headers,
        string name,
        string type,
        int? minimumLength = null,
        int? maximumLength = null,
        string? pattern = null)
    {
        Assert.True(headers.TryGetProperty(name, out var header));
        AssertStringSchema(
            header.GetProperty("schema"),
            type,
            minimumLength,
            maximumLength,
            pattern,
            format: null,
            canonicalMaximumLength: null);
    }

    private static void AssertCounterpartyPageQueryContract(JsonElement operation)
    {
        var parameters = operation.GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .ToDictionary(
                parameter => parameter.GetProperty("name").GetString()!,
                parameter => parameter.GetProperty("schema"),
                StringComparer.Ordinal);

        Assert.Equal(11, parameters.Count);
        var search = parameters["q"];
        Assert.Equal(200, search.GetProperty("maxLength").GetInt32());
        Assert.Equal(
            "unicode-trim+nfc",
            search.GetProperty(ErpOpenApiContract.CanonicalizationExtensionName).GetString());

        var role = parameters["role"];
        Assert.Equal("array", role.GetProperty("type").GetString());
        Assert.Equal(
            "^(Customer|Supplier|Tolling|Service)$",
            role.GetProperty("items").GetProperty("pattern").GetString());

        var pageSize = parameters["pageSize"];
        Assert.Equal(1, pageSize.GetProperty("minimum").GetInt32());
        Assert.Equal(200, pageSize.GetProperty("maximum").GetInt32());
        Assert.Equal(1, parameters["cursor"].GetProperty("minLength").GetInt32());
        Assert.Equal("boolean", parameters["includeCount"].GetProperty("type").GetString());
        Assert.DoesNotContain("companyId", parameters.Keys);
    }

    private static void AssertCounterpartyLinkCandidateQueryContract(JsonElement operation)
    {
        var parameters = operation.GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .ToDictionary(
                parameter => parameter.GetProperty("name").GetString()!,
                parameter => parameter.GetProperty("schema"),
                StringComparer.Ordinal);

        Assert.Equal(3, parameters.Count);
        var search = parameters["q"];
        Assert.Equal(200, search.GetProperty("maxLength").GetInt32());
        Assert.Equal(
            "unicode-trim+nfc",
            search.GetProperty(ErpOpenApiContract.CanonicalizationExtensionName).GetString());

        var pageSize = parameters["pageSize"];
        Assert.Equal(1, pageSize.GetProperty("minimum").GetInt32());
        Assert.Equal(100, pageSize.GetProperty("maximum").GetInt32());
        Assert.Equal(1, parameters["cursor"].GetProperty("minLength").GetInt32());
        Assert.DoesNotContain("companyId", parameters.Keys);
    }

    private static void AssertSalesAccountQueryContract(JsonElement operation)
    {
        var parameters = ReadQuerySchemas(operation);
        Assert.Equal(3, parameters.Count);
        Assert.Equal(200, parameters["q"].GetProperty("maxLength").GetInt32());
        Assert.Equal(1, parameters["cursor"].GetProperty("minLength").GetInt32());
        Assert.Equal(8192, parameters["cursor"].GetProperty("maxLength").GetInt32());
        Assert.Equal(1, parameters["pageSize"]
            .GetProperty(ErpOpenApiContract.NormalizedMinimumExtensionName).GetInt32());
        Assert.Equal(100, parameters["pageSize"]
            .GetProperty(ErpOpenApiContract.NormalizedMaximumExtensionName).GetInt32());
        Assert.DoesNotContain("companyId", parameters.Keys);
    }

    private static void AssertSalesProductQueryContract(JsonElement operation)
    {
        var version = ReadQuerySchemas(operation)["expectedProductVersion"];
        Assert.Equal(1, version.GetProperty("minLength").GetInt32());
        Assert.Equal(128, version.GetProperty("maxLength").GetInt32());
    }

    private static void AssertInventoryParentRollQueryContract(JsonElement operation)
    {
        var parameters = ReadQuerySchemas(operation);
        Assert.Equal(2, parameters.Count);
        var q = parameters["q"];
        Assert.Equal("trim", q.GetProperty(ErpOpenApiContract.CanonicalizationExtensionName).GetString());
        Assert.Equal(64, q.GetProperty(ErpOpenApiContract.CanonicalMaximumLengthExtensionName).GetInt32());
        var limit = parameters["limit"];
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(100, limit.GetProperty("maximum").GetInt32());
    }

    private static void AssertInventoryPhysicalAllocationReadQueryContract(JsonElement operation)
    {
        var parameters = ReadQuerySchemas(operation);
        Assert.Equal(3, parameters.Count);
        Assert.Equal("uuid", parameters["salesOrderId"].GetProperty("format").GetString());
        Assert.Equal("uuid", parameters["salesOrderLineId"].GetProperty("format").GetString());
        var limit = parameters["limit"];
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(100, limit.GetProperty("maximum").GetInt32());
    }

    private static Dictionary<string, JsonElement> ReadQuerySchemas(JsonElement operation) =>
        operation.GetProperty("parameters").EnumerateArray()
            .Where(parameter => parameter.GetProperty("in").GetString() == "query")
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!,
                parameter => parameter.GetProperty("schema"), StringComparer.Ordinal);

    private static void AssertStringSchema(
        JsonElement schema,
        string type,
        int? minimumLength,
        int? maximumLength,
        string? pattern,
        string? format,
        int? canonicalMaximumLength)
    {
        Assert.Equal(type, schema.GetProperty("type").GetString());
        Assert.Equal(
            minimumLength,
            schema.TryGetProperty("minLength", out var minLength)
                ? minLength.GetInt32()
                : null);
        Assert.Equal(
            maximumLength,
            schema.TryGetProperty("maxLength", out var maxLength)
                ? maxLength.GetInt32()
                : null);
        Assert.Equal(
            pattern,
            schema.TryGetProperty("pattern", out var patternValue)
                ? patternValue.GetString()
                : null);
        Assert.Equal(
            format,
            schema.TryGetProperty("format", out var formatValue)
                ? formatValue.GetString()
                : null);
        Assert.Equal(
            canonicalMaximumLength,
            schema.TryGetProperty(
                ErpOpenApiContract.CanonicalMaximumLengthExtensionName,
                out var canonicalMaximum)
                ? canonicalMaximum.GetInt32()
                : null);
        Assert.Equal(
            canonicalMaximumLength is not null,
            schema.TryGetProperty(
                ErpOpenApiContract.CanonicalizationExtensionName,
                out var canonicalization)
            && canonicalization.GetString() == "trim");
    }

    private static string DescribeSchema(JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return $"ref:{reference.GetString()?.Split('/').Last()}";
        }

        // A string enum emitted without "type" (first used by Finance, #320): its values in declared
        // order, so a new, removed or reordered value is a visible contract change.
        if (!schema.TryGetProperty("type", out _) && schema.TryGetProperty("enum", out var enumValues))
        {
            return "enum:" + string.Join('|', enumValues.EnumerateArray().Select(value => value.GetString()));
        }

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            return "oneOf:"
                + string.Join('|', oneOf.EnumerateArray()
                    .Select(DescribeSchema)
                    .Order(StringComparer.Ordinal));
        }

        var type = schema.GetProperty("type");
        IEnumerable<string?> types = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray()
                .Select(value => value.GetString())
                .Order(StringComparer.Ordinal)
            : new[] { type.GetString() };
        var descriptor = $"type:{string.Join('|', types)}";
        if (schema.TryGetProperty("format", out var format))
        {
            descriptor += $";format:{format.GetString()}";
        }

        if (schema.TryGetProperty("items", out var items))
        {
            descriptor += $";items:{DescribeSchema(items)}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.NonEmptyUuidExtensionName,
                out var nonEmptyUuid))
        {
            descriptor += $";non-empty-uuid:{nonEmptyUuid.GetBoolean().ToString().ToLowerInvariant()}";
        }

        return descriptor;
    }

    private static string DescribeParameterSchema(JsonElement schema)
    {
        var descriptor = DescribeSchema(schema);
        if (schema.TryGetProperty("pattern", out var pattern))
        {
            descriptor += $";pattern:{pattern.GetString()}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.CanonicalizationExtensionName,
                out var canonicalization))
        {
            descriptor += $";canonicalization:{canonicalization.GetString()}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.CanonicalMaximumLengthExtensionName,
                out var canonicalMaximum))
        {
            descriptor += $";canonical-max-length:{canonicalMaximum.GetInt32()}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.NormalizedMinimumExtensionName,
                out var normalizedMinimum))
        {
            descriptor += $";normalized-minimum:{normalizedMinimum.GetInt32()}";
        }

        if (schema.TryGetProperty(
                ErpOpenApiContract.NormalizedMaximumExtensionName,
                out var normalizedMaximum))
        {
            descriptor += $";normalized-maximum:{normalizedMaximum.GetInt32()}";
        }

        return descriptor;
    }

    private static void AssertSchemaSemantics(JsonElement schemas)
    {
        AssertComponentSchemaMatrix(schemas, ExpectedRequestComponentSchemas);
        AssertComponentSchemaMatrix(schemas, ExpectedProblemComponentSchemas);
        AssertPositiveIntegerStringVersion(schemas, "CounterpartyMutationAccountView");
        AssertPositiveIntegerStringVersion(schemas, "CounterpartyMutationView");
        AssertPositiveIntegerStringVersion(schemas, "CounterpartyDetailAccountView");
        AssertPositiveIntegerStringVersion(schemas, "CounterpartyDetailView");
        AssertPositiveIntegerStringVersion(
            schemas,
            "CounterpartyLinkCandidateSourceAccount");

        foreach (var name in new[] { "CounterpartyContactInformationView", "CounterpartyOperationalAddress",
                     "CounterpartyOperationalContact", "CounterpartyContactChannel" })
        {
            AssertPositiveIntegerStringVersion(schemas, name);
        }

        var contactCommand = schemas.GetProperty("UpdateCounterpartyContactInformation");
        Assert.False(contactCommand.GetProperty("additionalProperties").GetBoolean());
        var contactVersion = contactCommand.GetProperty("properties").GetProperty("expectedVersion");
        Assert.Equal("string", contactVersion.GetProperty("type").GetString());
        Assert.Equal("^[1-9][0-9]*$", contactVersion.GetProperty("pattern").GetString());
        Assert.Equal(19, contactVersion.GetProperty("maxLength").GetInt32());
        foreach (var name in new[] { "addresses", "contacts" })
        {
            Assert.Equal(100, schemas.GetProperty("CounterpartyContactInformationChanges")
                .GetProperty("properties").GetProperty(name).GetProperty("maxItems").GetInt32());
        }

        var problem = schemas.GetProperty("ErpProblemDetailsContract");
        Assert.Equal(
            ["code", "detail", "status", "title", "type"],
            problem.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal));
        var properties = problem.GetProperty("properties");
        Assert.Equal(
            ["code", "conflict", "correlationId", "detail", "status", "title", "type"],
            properties.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.False(IsNullable(properties.GetProperty("code")));
        Assert.Equal("string", properties.GetProperty("code").GetProperty("type").GetString());
        Assert.True(IsNullable(properties.GetProperty("correlationId")));
        Assert.True(IsNullable(properties.GetProperty("conflict")));
        Assert.Equal(
            ErpOpenApiContract.KnownProblemCodePattern,
            properties.GetProperty("code").GetProperty("pattern").GetString());
        Assert.Contains(
            "#/components/schemas/ErpVersionConflictContract",
            properties.GetProperty("conflict").GetRawText(),
            StringComparison.Ordinal);

        var codeLessProblem = schemas.GetProperty("ErpCodeLessProblemDetailsContract");
        Assert.Equal(
            ["detail", "status", "title", "type"],
            codeLessProblem.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal));
        var codeLessProperties = codeLessProblem.GetProperty("properties");
        Assert.Equal(
            ["conflict", "detail", "status", "title", "type"],
            codeLessProperties.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.False(codeLessProperties.TryGetProperty("code", out _));
        Assert.False(codeLessProperties.TryGetProperty("correlationId", out _));
        Assert.True(IsNullable(codeLessProperties.GetProperty("conflict")));
        Assert.Equal(
            ["code"],
            codeLessProblem.GetProperty("not")
                .GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString()));

        var versionConflict = schemas.GetProperty("ErpVersionConflictContract");
        Assert.Equal(
            ["currentVersion", "expectedVersion"],
            versionConflict.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Order(StringComparer.Ordinal));

        AssertRequestSchema(
            schemas.GetProperty("CreateCounterparty"),
            ["kind", "legalName"],
            ["tradeName"],
            ["kind", "legalName", "tradeName", "roles"]);
        AssertRequestSchema(
            schemas.GetProperty("CreateCompanyPartyAccount"),
            [],
            [],
            ["roles"]);
        AssertRequestSchema(
            schemas.GetProperty("SetCompanyPartyAccountActiveState"),
            ["isActive", "expectedVersion", "reason"],
            [],
            ["isActive", "expectedVersion", "reason"]);
        AssertRequestSchema(
            schemas.GetProperty("SetLocationActiveState"),
            ["isActive", "expectedVersion", "reason"],
            [],
            ["isActive", "expectedVersion", "reason"]);

        var partyKind = schemas.GetProperty("PartyKind");
        Assert.Equal(1, partyKind.GetProperty("minimum").GetInt32());
        Assert.Equal(2, partyKind.GetProperty("maximum").GetInt32());
        Assert.Equal(
            [1, 2],
            partyKind.GetProperty(ErpOpenApiContract.AllowedValuesExtensionName)
                .EnumerateArray()
                .Select(value => value.GetInt32()));
        var partyRole = schemas.GetProperty("PartyRole");
        Assert.Equal(0, partyRole.GetProperty("minimum").GetInt32());
        Assert.Equal(15, partyRole.GetProperty("maximum").GetInt32());
        Assert.Equal(
            [1, 2, 4, 8],
            partyRole.GetProperty(ErpOpenApiContract.AllowedFlagsExtensionName)
                .EnumerateArray()
                .Select(value => value.GetInt32()));

        var createCounterparty = schemas.GetProperty("CreateCounterparty")
            .GetProperty("properties");
        AssertCanonicalStringContract(
            createCounterparty.GetProperty("legalName"),
            minimumLength: 1,
            maximumLength: 250);
        AssertCanonicalStringContract(
            createCounterparty.GetProperty("tradeName"),
            minimumLength: null,
            maximumLength: 200);
        AssertStatusRequestConstraints(
            schemas.GetProperty("SetCompanyPartyAccountActiveState"),
            expectedVersionMinimum: 1);
        AssertStatusRequestConstraints(
            schemas.GetProperty("SetLocationActiveState"),
            expectedVersionMinimum: 1);
    }

    private static void AssertPositiveIntegerStringVersion(
        JsonElement schemas,
        string schemaName)
    {
        var version = schemas
            .GetProperty(schemaName)
            .GetProperty("properties")
            .GetProperty("version");
        Assert.Equal("string", version.GetProperty("type").GetString());
        Assert.False(IsNullable(version));
        Assert.False(version.TryGetProperty("format", out _));
        Assert.False(version.TryGetProperty("minimum", out _));
        Assert.False(version.TryGetProperty("maximum", out _));
        Assert.Equal(1, version.GetProperty("minLength").GetInt32());
        Assert.Equal(19, version.GetProperty("maxLength").GetInt32());
        Assert.Equal(
            ErpOpenApiContract.PositiveIntegerStringPattern,
            version.GetProperty("pattern").GetString());
    }

    private static void AssertStatusRequestConstraints(
        JsonElement schema,
        int? expectedVersionMinimum)
    {
        var properties = schema.GetProperty("properties");
        Assert.Equal(
            expectedVersionMinimum,
            properties.GetProperty("expectedVersion")
                .TryGetProperty("minimum", out var minimum)
                ? minimum.GetInt32()
                : null);
        Assert.Equal(
            ErpOpenApiContract.PositiveIntegerStringPattern,
            properties.GetProperty("expectedVersion").GetProperty("pattern").GetString());
        AssertCanonicalStringContract(
            properties.GetProperty("reason"),
            minimumLength: 1,
            maximumLength: 1000);
    }

    private static void AssertCanonicalStringContract(
        JsonElement schema,
        int? minimumLength,
        int? maximumLength)
    {
        Assert.False(schema.TryGetProperty("minLength", out _));
        Assert.False(schema.TryGetProperty("maxLength", out _));
        Assert.Equal(
            "trim",
            schema.GetProperty(ErpOpenApiContract.CanonicalizationExtensionName)
                .GetString());
        Assert.Equal(
            minimumLength,
            schema.TryGetProperty(
                ErpOpenApiContract.CanonicalMinimumLengthExtensionName,
                out var canonicalMinimum)
                ? canonicalMinimum.GetInt32()
                : null);
        Assert.Equal(
            maximumLength,
            schema.TryGetProperty(
                ErpOpenApiContract.CanonicalMaximumLengthExtensionName,
                out var canonicalMaximum)
                ? canonicalMaximum.GetInt32()
                : null);
    }

    private static void AssertRequestSchema(
        JsonElement schema,
        IReadOnlyList<string> requiredProperties,
        IReadOnlyList<string> nullableProperties,
        IReadOnlyList<string> expectedProperties)
    {
        var actualRequiredProperties = schema.TryGetProperty("required", out var required)
            ? required.EnumerateArray().Select(item => item.GetString()!).ToArray()
            : [];
        Assert.Equal(
            requiredProperties.Order(StringComparer.Ordinal),
            actualRequiredProperties.Order(StringComparer.Ordinal));
        var properties = schema.GetProperty("properties");
        Assert.Equal(
            expectedProperties.Order(StringComparer.Ordinal),
            properties.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        foreach (var propertyName in nullableProperties)
        {
            Assert.True(IsNullable(properties.GetProperty(propertyName)));
        }
    }

    private static void AssertComponentSchemaMatrix(
        JsonElement schemas,
        IReadOnlyDictionary<string, ComponentSchemaExpectation> expectations)
    {
        foreach (var (schemaName, expected) in expectations)
        {
            var schema = schemas.GetProperty(schemaName);
            Assert.Equal(expected.RootDescriptor, DescribeSchema(schema));
            IEnumerable<string?> required = schema.TryGetProperty(
                    "required",
                    out var requiredValues)
                ? requiredValues.EnumerateArray()
                    .Select(value => value.GetString())
                    .Order(StringComparer.Ordinal)
                : Array.Empty<string?>();
            Assert.Equal(
                expected.RequiredProperties.Order(StringComparer.Ordinal),
                required);
            var properties = schema.GetProperty("properties")
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => DescribeSchema(property.Value),
                    StringComparer.Ordinal);
            Assert.Equal(
                expected.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                properties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }

    private static void AssertSuccessComponentSchemas(JsonElement schemas)
    {
        var reachableComponents = CollectReachableSuccessComponents(schemas);
        Assert.Equal(
            ExpectedSuccessComponentSchemas.Keys.Order(StringComparer.Ordinal),
            reachableComponents.Order(StringComparer.Ordinal));

        foreach (var (schemaName, expected) in ExpectedSuccessComponentSchemas)
        {
            var schema = schemas.GetProperty(schemaName);
            Assert.Equal(expected.RootDescriptor, DescribeSchema(schema));
            IEnumerable<string?> required = schema.TryGetProperty(
                    "required",
                    out var requiredValues)
                ? requiredValues.EnumerateArray()
                    .Select(value => value.GetString())
                    .Order(StringComparer.Ordinal)
                : Array.Empty<string?>();
            Assert.Equal(
                expected.RequiredProperties.Order(StringComparer.Ordinal),
                required);
            var properties = schema.TryGetProperty("properties", out var propertyValues)
                ? propertyValues.EnumerateObject()
                    .ToDictionary(
                        property => property.Name,
                        property => DescribeSchema(property.Value),
                        StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            Assert.Equal(
                expected.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                properties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }

    private static HashSet<string> CollectReachableSuccessComponents(JsonElement schemas)
    {
        var queue = new Queue<string>(ExpectedSuccessSchemas.Values
            .Where(value => value.StartsWith("ref:", StringComparison.Ordinal)
                || value.StartsWith("array:", StringComparison.Ordinal))
            .Select(value => value[(value.IndexOf(':') + 1)..]));
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        while (queue.TryDequeue(out var schemaName))
        {
            if (!reachable.Add(schemaName))
            {
                continue;
            }

            foreach (var reference in ReadSchemaReferences(schemas.GetProperty(schemaName)))
            {
                queue.Enqueue(reference);
            }
        }

        return reachable;
    }

    private static IEnumerable<string> ReadSchemaReferences(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("$ref"))
                {
                    yield return property.Value.GetString()?.Split('/').Last()
                        ?? throw new InvalidOperationException("Schema reference is empty.");
                    continue;
                }

                foreach (var reference in ReadSchemaReferences(property.Value))
                {
                    yield return reference;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var reference in ReadSchemaReferences(item))
                {
                    yield return reference;
                }
            }
        }
    }

    private static bool IsNullable(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            return type.ValueKind == JsonValueKind.Array
                && type.EnumerateArray().Any(value => value.GetString() == "null");
        }

        var alternatives = schema.TryGetProperty("anyOf", out var anyOf)
            ? anyOf
            : schema.TryGetProperty("oneOf", out var oneOf)
                ? oneOf
                : default;
        return alternatives.ValueKind == JsonValueKind.Array
            && alternatives.EnumerateArray().Any(option =>
                option.TryGetProperty("type", out var optionType)
                && optionType.GetString() == "null");
    }

    private static IReadOnlyList<string> AuthenticatedResponses(params string[] responses)
    {
        return responses
            .Concat(
            [
                "401:application/problem+json",
                "403:application/problem+json",
                "500:application/problem+json",
            ])
            .ToArray();
    }

    private static IReadOnlyList<string> ExpectedProblemCodes(
        string operationId,
        int statusCode)
    {
        var codes = statusCode switch
        {
            StatusCodes.Status400BadRequest => new List<string>
            {
                "correlation_metadata_invalid",
            },
            StatusCodes.Status401Unauthorized => ["authentication_required"],
            StatusCodes.Status403Forbidden => ["forbidden"],
            StatusCodes.Status500InternalServerError => ["internal_error"],
            StatusCodes.Status503ServiceUnavailable => ["service_unavailable"],
            _ => [],
        };
        if (statusCode == StatusCodes.Status400BadRequest
            && RequiredCompanyOperations.Contains(operationId))
        {
            codes.Add("company_context_invalid");
        }

        if (AdditionalProblemCodes.TryGetValue(
                $"{operationId}:{statusCode}",
                out var additionalCodes))
        {
            codes.AddRange(additionalCodes);
        }

        return codes.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertProblemResponseSchema(
        string operationId,
        int statusCode,
        JsonElement schema)
    {
        var responseKey = $"{operationId}:{statusCode}";
        if (MixedProblemResponses.Contains(responseKey))
        {
            Assert.False(schema.TryGetProperty("$ref", out _));
            Assert.Equal(
                [
                    "#/components/schemas/ErpCodeLessProblemDetailsContract",
                    "#/components/schemas/ErpProblemDetailsContract",
                ],
                schema.GetProperty("oneOf")
                    .EnumerateArray()
                    .Select(option => option.GetProperty("$ref").GetString())
                    .Order(StringComparer.Ordinal));
            return;
        }

        var expectedReference = CodeLessProblemResponses.Contains(responseKey)
            ? "#/components/schemas/ErpCodeLessProblemDetailsContract"
            : "#/components/schemas/ErpProblemDetailsContract";
        Assert.Equal(expectedReference, schema.GetProperty("$ref").GetString());
        Assert.False(schema.TryGetProperty("oneOf", out _));
    }

    private static string? NormalizeRoute(string? rawText)
    {
        if (rawText is null)
        {
            return null;
        }

        var normalized = rawText.Length > 1
            ? $"/{rawText.Trim('/')}"
            : rawText;
        return Regex.Replace(
            normalized,
            "\\{([^}:]+):[^}]+\\}",
            "{$1}",
            RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<string> ResponseCodes(JsonElement operation)
    {
        return operation.GetProperty("responses")
            .EnumerateObject()
            .Select(response => response.Name)
            .ToArray();
    }

    private static string ApiAssetsPath(string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot,
            "src",
            "backend",
            "GoymenERP.Api",
            "obj",
            "project.assets.json");
        Assert.True(File.Exists(path), $"API assets file was not found: {path}");
        return path;
    }

    private static ComponentSchemaExpectation ObjectSchema(
        IReadOnlyList<string> requiredProperties,
        params (string Name, string Descriptor)[] properties)
    {
        return new ComponentSchemaExpectation(
            "type:object",
            requiredProperties,
            properties.ToDictionary(
                property => property.Name,
                property => property.Descriptor,
                StringComparer.Ordinal));
    }

    private static ComponentSchemaExpectation ScalarSchema(string descriptor)
    {
        return new ComponentSchemaExpectation(
            descriptor,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("GoymenERP repository root was not found.");
    }

    private sealed record GenerationResult(
        byte[] Bytes,
        string SecretMarker,
        int AcceptedConnectionCount);

    private sealed record OpenApiOperationSnapshot(
        string Path,
        string Method,
        string OperationId,
        JsonElement Value);

    private sealed record OperationExpectation(
        string Method,
        string Path,
        string OperationId,
        IReadOnlyList<string> Responses,
        string? RequestSchemaReference = null,
        IReadOnlyList<string>? ParameterValues = null)
    {
        public IReadOnlyList<string> Parameters { get; } = ParameterValues ?? [];
    }

    private sealed record ParameterSnapshot(
        string Location,
        string Name,
        bool Required,
        string Schema);

    private sealed record ComponentSchemaExpectation(
        string RootDescriptor,
        IReadOnlyList<string> RequiredProperties,
        IReadOnlyDictionary<string, string> Properties);

    private sealed record EndpointSnapshot(
        string Method,
        string Path,
        string OperationId);
}
