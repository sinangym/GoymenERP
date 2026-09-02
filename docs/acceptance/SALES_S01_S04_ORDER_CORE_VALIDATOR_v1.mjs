#!/usr/bin/env node

import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const DEFAULT_ROOT = resolve(HERE, "..", "..");

const PATHS = Object.freeze({
  scenarios: "docs/acceptance/SALES_S01_S04_ORDER_CORE_SCENARIOS_v1.json",
  authority: "docs/acceptance/SALES_S01_S04_ORDER_CORE_AUTHORITY_v1.json",
  acceptance: "docs/acceptance/SALES_S01_S04_ORDER_CORE_ACCEPTANCE_v1.md",
  spec: "docs/features/SALES_S01_S04_ORDER_CORE_JIT_SPEC_v1.md",
  validator: "docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_v1.mjs",
  mutation: "docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_MUTATION_TEST_v1.mjs",
  canonicalAcceptance: "docs/acceptance/ACCEPTANCE_70.md",
  formulaCatalog: "docs/formulas/FORMUL_KATALOGU.csv",
  decisionRegister: "docs/decisions/KARAR_SICILI.md",
  masterReport: "docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md",
  stateMachines: "docs/domain/DURUM_MAKINELERI.md",
  commonMetadata: "docs/domain/ORTAK_KAYIT_METADATA.md",
  auditAdr: "docs/adr/ADR-009-audit-and-correlation.md",
  concurrencyAdr: "docs/adr/ADR-011-idempotency-and-concurrency.md",
  canonicalScreenStatus: "docs/ui/CANONICAL_SCREEN_STATUS.csv",
  screenCatalog: "docs/ui/SCREEN_CATALOG.csv",
  sourceReconciliation: "docs/ui/SOURCE_RECONCILIATION.md",
  uiDesignSystem: "docs/ui/UI_DESIGN_SYSTEM.md",
  lockedSalesDecision: "docs/ui/sources/sales/sales-order-detail-locked-decision-v1.0.docx",
});

const SOURCE_SNAPSHOT = "main@80c567a63da8e059ce92aa1b23dad9f4568ad2ca";

const AUTHORITY_KEYS = Object.freeze([
  "schemaVersion", "status", "version", "date", "sourceSnapshot", "productionActivation",
  "sharedCiGate", "canonicalScreenSourceMapping", "externalBinarySourcePins",
  "canonicalSourceInventory", "sourceReferenceCatalog", "sourceReferenceCatalogSha256",
  "packageArtifactInventory", "exactCaseToCanonicalAt",
  "exactCaseToSourceRefs", "scenarioMetadataContractSha256", "caseSemanticContractSha256", "goldenFixtureContractSha256",
  "ownerDecisionGateContractSha256", "implementationGateContractSha256", "requiredGoldenPromotionDescriptor",
  "ownerDecisionGateIds", "implementationGateIds", "nonClaims",
]);
const AUTHORITY_CI_KEYS = Object.freeze([
  "id", "status", "validatorCommand", "mutationCommand", "visibleRunnerStepRequired",
  "nonzeroFailurePropagationRequired", "generalCiClosesGate", "workflowWiringEvidence",
]);
const CANONICAL_SOURCE_PINS = Object.freeze([
  { path: "AGENTS.md", gitBlobSha: "173285d3b7f3c200fc3185f71ce65aa5e5656546" },
  { path: "docs/acceptance/ACCEPTANCE_70.md", gitBlobSha: "6119f84d2a59b3e0d1fdd2d8646619c0ad29e1bd" },
  { path: "docs/formulas/FORMUL_KATALOGU.csv", gitBlobSha: "ed69cedecd33ba7d3947675f954309a3913ffca7" },
  { path: "docs/decisions/KARAR_SICILI.md", gitBlobSha: "e46bd3146272cb48d3d4a897e4c4c8751391b852" },
  { path: "docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md", gitBlobSha: "54059a5805f415e1c015540f9ef0c9e474c53e98" },
  { path: "docs/domain/DURUM_MAKINELERI.md", gitBlobSha: "e91e2e500501f99327657c31a7432daa62715f20" },
  { path: "docs/domain/ORTAK_KAYIT_METADATA.md", gitBlobSha: "566f41285111327df492c506b69b7d705d795a7a" },
  { path: "docs/features/PLATFORM_HTTP_ERROR_CONTRACT_v1.md", gitBlobSha: "0c92e1cfd41b1d54416c74c1160f5ae7522e73dd" },
  { path: "docs/adr/ADR-009-audit-and-correlation.md", gitBlobSha: "1c2b29a742b25ba20b443f14c77c20806f72f02c" },
  { path: "docs/adr/ADR-011-idempotency-and-concurrency.md", gitBlobSha: "9f82d6bab81640fc43a2b1c44a148017ae98dc07" },
  { path: "docs/ui/CANONICAL_SCREEN_STATUS.csv", gitBlobSha: "662cdbc405b49e469c6d578580bef4766d6a4635" },
  { path: "docs/ui/SCREEN_CATALOG.csv", gitBlobSha: "4ad764683e7c98a0cfb3077d812f6d8676e55064" },
  { path: "docs/ui/SOURCE_RECONCILIATION.md", gitBlobSha: "ae697b117e33c3408b521e6760ae5d8b4b135823" },
  { path: "docs/ui/UI_DESIGN_SYSTEM.md", gitBlobSha: "671ef0b387f1f51febfd8ae6bd291ccb933bffea" },
]);
const EXTERNAL_BINARY_SOURCE_PINS = Object.freeze([
  {
    path: "docs/ui/sources/sales/sales-order-detail-locked-decision-v1.0.docx",
    gitBlobSha: "f6c22804becf84206aa4b3bea81745cfbae771ba",
    sha256: "50ff7bc5286bbe353508bfd9161560b1f8ad4eae833de676223d098ed620b65f",
    mediaType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  },
]);
const SCREEN_SOURCE_PINS = Object.freeze([
  { canonicalScreenId: "S-01", sourceIds: ["MASTER3-S-01"], relation: "CONFIRMED_SPLIT_MERGE_REFERENCE" },
  { canonicalScreenId: "S-02", sourceIds: ["MASTER3-S-01"], relation: "CONFIRMED_SPLIT_MERGE_REFERENCE" },
  { canonicalScreenId: "S-03", sourceIds: ["MASTER3-S-03", "MASTER3-S-04", "MASTER3-S-05", "PRODUCT-U-06"], relation: "CONFIRMED_SPLIT_MERGE_REFERENCE" },
  { canonicalScreenId: "S-04", sourceIds: ["MASTER3-S-02", "MASTER3-S-03", "MASTER3-S-04", "MASTER3-S-07", "MASTER3-S-09", "MASTER3-S-12"], relation: "CONFIRMED_SPLIT_MERGE_REFERENCE" },
]);
const AUTHORITY_NON_CLAIMS = Object.freeze([
  "NO_SCHEMA_API_UI_IMPLEMENTATION_GO", "NO_PERMISSION_ROLE_CAPABILITY_DECLARATION",
  "NO_PRODUCTION_GOLDEN_OR_SEED", "NO_SHARED_CI_WIRING", "NO_CANONICAL_AT_CLOSURE",
]);

const TOP_LEVEL_KEYS = Object.freeze([
  "schemaVersion", "status", "version", "date", "sourceSnapshot", "specPath",
  "acceptancePath", "canonicalScreens", "dependencyScreens", "candidateUiRecords",
  "canonicalAcceptance", "exactApiEnumsPermissionsDeclared", "productionActivation",
  "fixturePolicy", "aliases", "goldenFixtures", "requiredGoldenPromotionFields",
  "decisionGates", "gates", "caseCount", "cases", "parity",
]);

const FIXTURE_POLICY_KEYS = Object.freeze([
  "classification", "productionSeed", "businessPolicy", "requiresOwnerPromotion", "note",
]);
const ALIAS_KEYS = Object.freeze([
  "companies", "actors", "customers", "products", "orders", "packages", "registry",
]);
const PARITY_KEYS = Object.freeze([
  "markdownCaseCount", "jsonCaseCount", "idPattern", "order", "requiredCaseFields",
  "requiredCanonicalAcceptanceCoverage", "duplicateIdsAllowed", "unknownTopLevelKeysAllowed",
]);
const ALIAS_VALUES = Object.freeze({
  companies: ["CMP-A", "CMP-B"],
  actors: ["ACT-A", "ACT-AB", "ACT-NONE", "ACT-CREATOR", "ACT-APPROVER"],
  customers: ["CUST-A-ACTIVE", "CUST-A-INACTIVE", "CUST-B-FOREIGN"],
  products: ["PRD-ACTIVE-PROFILE", "PRD-DRAFT", "PRD-INACTIVE"],
  orders: ["ORDER-A-REV0", "ORDER-A-REV1"],
  packages: ["PACK-SYNTH-V1", "PACK-SYNTH-V2"],
  registry: ["TEST-TRY", "TEST-TON", "TEST-BOY", "TEST-KG", "TEST-UNIT-NON_PHYSICAL"],
});

const CASE_KEYS = Object.freeze([
  "id", "group", "title", "screenIds", "sourceRefs", "atRefs", "gateIds",
  "evidence", "precondition", "action", "expected", "forbidden",
]);
const GOLDEN_KEYS = Object.freeze(["id", "status", "formulaRef", "inputs", "expected", "gateIds", "atRefs"]);
const DECISION_GATE_KEYS = Object.freeze(["id", "status", "topic", "safeDefault"]);
const IMPLEMENTATION_GATE_KEYS = Object.freeze(["id", "status", "owner", "dependsOnDecisionGateIds", "blocks"]);

const GROUPS = Object.freeze([
  ["X", 10], ["S01", 5], ["S02", 7], ["S03", 16], ["S04", 13], ["LOCK", 12], ["DEP", 4], ["SEL", 5],
]);

const IMPLEMENTATION_GATES = Object.freeze([
  "G-SAL-CARI-01", "G-SAL-PRD-01", "G-SAL-PRD-ACCESS-01", "G-SAL-PERSIST-01",
  "G-SAL-PRICE-01", "G-SAL-RISK-01", "G-SAL-APP-01", "G-SAL-SERVICE-01",
  "G-SAL-ALLOC-01", "G-SAL-FULFILL-01", "G-SAL-INVOICE-01", "G-SAL-DOC-01",
  "G-SAL-AI-01", "G-SAL-EVENT-01", "G-SAL-PURCHASE-READ-01",
  "G-SAL-PRODUCTION-READ-01", "G-SAL-FINANCE-READ-01", "G-SAL-FX-01",
]);

const DECISION_GATES = Object.freeze([
  "SAL-REQ-001", "SAL-STATE-001", "SAL-NUM-001", "SAL-CARI-SEL-001",
  "SAL-PRD-ACCESS-001", "SAL-SERVICE-001", "SAL-PRICE-001", "SAL-MARGIN-001",
  "SAL-RISK-001", "SAL-APP-001", "SAL-DUE-001", "SAL-TERM-001",
  "SAL-CANCEL-001", "SAL-CLOSE-001", "SAL-ERR-001", "SAL-PERM-001",
]);

const PROMOTION_FIELDS = Object.freeze([
  "immutableUnitIds", "formulaVersion", "priceVersion", "policyVersion", "decimalScale",
  "roundingMode", "tolerance", "companyScope", "businessDateRule", "currencyRule",
  "ownerEvidence", "fixtureProvenance", "datasetContentHash", "realSqlRoundTrip",
  "businessOwnerAcceptance",
]);

const GOLDEN_IDS = Object.freeze([
  "GOLD-SAL-PRICE-001", "GOLD-SAL-RISK-001", "GOLD-SAL-CONV-001",
  "GOLD-SAL-TERM-001", "GOLD-SAL-REMAIN-001", "GOLD-SAL-REV-001",
  "GOLD-SAL-EFFECTIVE-KG-001", "GOLD-SAL-UNINVOICED-001", "GOLD-SAL-ALLOC-PCT-001",
]);

// These digests pin the parsed semantic payload, not merely IDs or field presence.
// Any intentional scenario change requires fresh exact-head review and an explicit
// update to both this executable contract and the detached authority descriptor.
const CASE_SEMANTIC_CONTRACT_SHA256 = "330ed8378b3305f3e100c90637cec24013f607817344c5d32a77b73aa096c924";
const EXPECTED_OUTCOME_CONTRACT_SHA256 = "d1ae144858fa78328f3df5f8e114ed0552adaa36f154cf21e55de4075fc8c5eb";
const FORBIDDEN_OUTCOME_CONTRACT_SHA256 = "44d7dfedf89ae3ec71b0d7ceb1dfc8f6783dffc3d029d2b56fc483c251bef4ca";
const CASE_SOURCE_REFS_CONTRACT_SHA256 = "cfa410a624e598991596c8344e30321ccbd170f9326b686c9cb86230ed639801";
const GOLDEN_FIXTURE_CONTRACT_SHA256 = "47aa79f5582562de848adb9d2deb44a6e186d2218cd772d7bc922bb9bb068bf7";
const OWNER_DECISION_GATE_CONTRACT_SHA256 = "c1d775c7e916660fee46f1f2785f47a8e82e22cdaa2bbb7a55431a9900dea4d9";
const IMPLEMENTATION_GATE_CONTRACT_SHA256 = "5f18bb1df23c6a458a66f4dbc129f3e6a668758622290e6ffa7d1a0d5a49e31b";
const SCENARIO_METADATA_CONTRACT_SHA256 = "7a6e179741042851031442b0aa1e54ce7b42c917c6e8f91bae8329e669207e77";
const SOURCE_REFERENCE_CATALOG_SHA256 = "67787e605740b2dd8712650708ee67aeb88fd4f04ae05b138c93f16ae020b807";
const TRUSTED_ARTIFACT_SHA256 = Object.freeze({
  [PATHS.spec]: "4ffcb65eeb5af59891d4b370dfc126711673835a744147d0a1f0d348b0d0cdf6",
  [PATHS.acceptance]: "c9cd9913b0c34bdabf731e19eaf639ba6d9f3cc0e24a263c283505fdd65caf5f",
  [PATHS.scenarios]: "70cca7d05e960f555200b0428e481f78f419085718d724e1d6a3d6eba049d350",
  [PATHS.mutation]: "186acdee42e6624139d86c10dc7f532ca44c87bac0bac2cc286c0b374daa5535",
});

const GOLDEN_SHAPE_PINS = Object.freeze({
  "GOLD-SAL-PRICE-001": {
    formulaRef: "SIM-001",
    inputKeys: ["listPrice", "currency", "priceUnit", "discountPercent", "syntheticFloor"],
    expectedKeys: ["discountedNet", "comparison", "businessSemantic"],
    gateIds: ["G-SAL-PRICE-01", "G-SAL-APP-01"], atRefs: ["AT-021"],
  },
  "GOLD-SAL-RISK-001": {
    formulaRef: "MASTER-RISK-GROSS",
    inputKeys: ["currentGrossRisk", "candidateApprovedUndispatchedDelta", "syntheticLimit", "currency"],
    expectedKeys: ["afterGrossRisk", "comparison", "businessSemantic"],
    gateIds: ["G-SAL-RISK-01", "G-SAL-APP-01"], atRefs: ["AT-022"],
  },
  "GOLD-SAL-CONV-001": {
    formulaRef: "SYNTHETIC_FIXED_FACTOR",
    inputKeys: ["enteredQuantity", "enteredUnit", "factor", "factorUnit"],
    expectedKeys: ["primaryQuantity", "primaryUnit", "snapshotFields"],
    gateIds: ["G-SAL-PRD-01"], atRefs: [],
  },
  "GOLD-SAL-TERM-001": {
    formulaRef: "STRUCTURAL_SUM",
    inputKeys: ["lineQuantity", "unit", "sliceQuantities"],
    expectedKeys: ["sliceTotal", "reconciled"],
    gateIds: ["G-SAL-PERSIST-01"], atRefs: [],
  },
  "GOLD-SAL-REMAIN-001": {
    formulaRef: "INV-005",
    inputKeys: ["netActiveOrderQuantity", "realizedQuantity", "cancelledQuantity", "unit"],
    expectedKeys: ["remainingQuantity", "unit"],
    gateIds: ["G-SAL-FULFILL-01"], atRefs: ["AT-024"],
  },
  "GOLD-SAL-REV-001": {
    formulaRef: null,
    inputKeys: ["baseRevision", "baseApproval", "criticalField", "changeApplied"],
    expectedKeys: ["baseRevisionImmutable", "newRevision", "approvalInherited", "reapprovalRequired"],
    gateIds: ["G-SAL-PERSIST-01", "G-SAL-APP-01"], atRefs: ["AT-023"],
  },
  "GOLD-SAL-EFFECTIVE-KG-001": {
    formulaRef: "LOCKED_DOCX_EFFECTIVE_KG_T_G",
    inputKeys: ["orderAmount", "orderTheoreticalKgT", "fulfillmentAmount", "actualDispatchKgG", "invoiceAmount", "invoiceLinkedActualKgG", "currency", "massUnit"],
    expectedKeys: ["orderEffectiveKgPrice", "fulfillmentEffectiveKgPrice", "invoiceEffectiveKgPrice", "roundingApplied"],
    gateIds: ["G-SAL-PRICE-01", "G-SAL-FULFILL-01", "G-SAL-INVOICE-01", "G-SAL-FX-01"], atRefs: [],
  },
  "GOLD-SAL-UNINVOICED-001": {
    formulaRef: "LOCKED_DOCX_UNINVOICED_COMMERCIAL_AMOUNT",
    inputKeys: ["operationFulfilledTl", "invoiceFulfilledTl", "operationFulfilledUsd", "invoiceFulfilledUsd", "commercialRevision", "fxBasis"],
    expectedKeys: ["uninvoicedFulfilledTl", "uninvoicedFulfilledUsd", "invoiceLinkedQuantityRemainsSeparate"],
    gateIds: ["G-SAL-FULFILL-01", "G-SAL-INVOICE-01", "G-SAL-FX-01"], atRefs: [],
  },
  "GOLD-SAL-ALLOC-PCT-001": {
    formulaRef: "LOCKED_DOCX_PHYSICAL_ONLY_ALLOCATION_PERCENT",
    inputKeys: ["physicalNeedKg", "physicalAllocatedKg", "stocklessServiceCommercialQuantity", "serviceUnit"],
    expectedKeys: ["physicalNeedDenominatorKg", "physicalAllocatedNumeratorKg", "allocationPercentExact", "stocklessServiceExcluded"],
    gateIds: ["G-SAL-SERVICE-01", "G-SAL-ALLOC-01"], atRefs: [],
  },
});

const CANONICAL_AT = Object.freeze({
  "AT-001": "Kullanıcı yalnız yetkili şirket, şube ve depo kayıtlarını görür.",
  "AT-004": "Oluşturan kişi yüksek riskli kendi işlemini görev ayrılığına aykırı onaylayamaz.",
  "AT-005": "Eşzamanlı değişiklikte ikinci kullanıcıya çatışma ve fark gösterilir.",
  "AT-006": "Başarısız kaydetme kullanıcının form verisini kaybettirmez.",
  "AT-007": "İptal ve ters işlem geçmişi silmez.",
  "AT-008": "Şirket kapsamı değişikliği açık formu sessizce başka şirkete taşımaz.",
  "AT-013": "Pasif cari yeni işleme seçilemez; geçmiş kayıtta görünür kalır.",
  "AT-016": "Birim dönüşümü yuvarlama/tolerans golden örneğini geçer.",
  "AT-017": "Paket standardının yeni sürümü geçmiş siparişi değiştirmez.",
  "AT-021": "Satış siparişi fiyat alt sınırını aşarsa doğru onaya gider.",
  "AT-022": "Risk limiti aşımı siparişi sessizce serbest bırakmaz.",
  "AT-023": "Onaylı siparişte kritik satır değişikliği eski onayı geçersizleştirir.",
  "AT-024": "Kısmi teslimat sipariş kalanını doğru tutar.",
  "AT-025": "Satış ve satın alma belgeleri operasyon listelerinde karışmaz.",
});

const AT_CASES = Object.freeze({
  "AT-001": ["CASE-SAL-X-001", "CASE-SAL-X-002", "CASE-SAL-X-003", "CASE-SAL-X-004", "CASE-SAL-S01-002", "CASE-SAL-S02-001", "CASE-SAL-S02-006", "CASE-SAL-S03-002", "CASE-SAL-S03-007", "CASE-SAL-SEL-002"],
  "AT-004": ["CASE-SAL-DEP-003"],
  "AT-005": ["CASE-SAL-X-007"],
  "AT-006": ["CASE-SAL-X-009"],
  "AT-007": ["CASE-SAL-S04-013"],
  "AT-008": ["CASE-SAL-X-004"],
  "AT-013": ["CASE-SAL-S02-005", "CASE-SAL-S03-004"],
  "AT-017": ["CASE-SAL-S03-010"],
  "AT-021": ["CASE-SAL-S03-013"],
  "AT-022": ["CASE-SAL-S03-014"],
  "AT-023": ["CASE-SAL-S04-004", "CASE-SAL-S04-005", "CASE-SAL-DEP-004"],
  "AT-024": ["CASE-SAL-S02-003", "CASE-SAL-S04-012"],
  "AT-025": ["CASE-SAL-S02-001", "CASE-SAL-S02-004"],
});

const ALLOWED_SOURCE_REFS = new Set([
  "ADR-009", "ADR-011", "AGENTS", "AT-004", "AT-006", "AT-007", "AT-013", "AT-017",
  "AT-021", "AT-022", "AT-024", "AT-025", "DEC-AI-001", "DEC-CAR-001",
  "DEC-CAR-002", "DEC-CAR-003", "DEC-PRD-006", "DEC-PRD-007", "DEC-PRD-010",
  "DEC-SAL-001", "DEC-SAL-002", "DEC-SAL-003", "DEC-SAL-004", "DEC-SAL-005",
  "DEC-SAL-006", "DEC-SAL-007", "DEC-SAL-008", "DURUM-MAKINELERI",
  "MASTER3-S-05", "Master-12", "Master-27.2", "Master-28.10", "Master-28.13",
  "Master-28.14", "Master-7.2", "Master-8.1", "Master-9.1", "Master-9.2",
  "Master-Company", "ORTAK-METADATA", "PLATFORM-HTTP", "PRD-CORE", "PRD-PERM-001",
  "PRD-SCOPE-001", "PRODUCT-U-06", "S-05", "S-06", "SAL-PRD-ACCESS-001",
  "SAL-REQ-001", "SAL-SERVICE-001", "SAL-STATE-001", "SALES-LOCK-DOCX", "UI-DESIGN", "UI-MAP-001",
  "UI-MAP-002",
]);

const VALID_SCREENS = new Set(["S-01", "S-02", "S-03", "S-04", "S-05", "S-06"]);
const VALID_EVIDENCE = new Set(["UT", "CT", "SQL", "SEC", "FE", "E2E", "A11Y", "SRC"]);

function sameArray(actual, expected) {
  return Array.isArray(actual) && actual.length === expected.length && actual.every((value, index) => value === expected[index]);
}

function addError(errors, condition, message) {
  if (!condition) errors.push(message);
}

function exactKeys(errors, label, value, expected) {
  const actual = value && typeof value === "object" && !Array.isArray(value) ? Object.keys(value) : [];
  addError(errors, sameArray(actual, expected), `${label}: exact key/order drift; expected ${expected.join(",")}; got ${actual.join(",")}`);
}

function nonEmptyString(value) {
  return typeof value === "string" && value.trim().length > 0;
}

function sha256Text(value) {
  return createHash("sha256").update(value, "utf8").digest("hex");
}

function sha256Bytes(value) {
  return createHash("sha256").update(value).digest("hex");
}

function sha256Json(value) {
  return sha256Text(JSON.stringify(value) ?? "undefined");
}

function duplicateJsonKeyErrors(raw, label) {
  const errors = [];
  let index = 0;
  const skipWhitespace = () => {
    while (/\s/u.test(raw[index] || "")) index += 1;
  };
  const fail = (message) => {
    throw new Error(`${message} at byte ${index}`);
  };
  const parseString = () => {
    if (raw[index] !== '"') fail("expected JSON string");
    const start = index;
    index += 1;
    while (index < raw.length) {
      if (raw[index] === '"') {
        index += 1;
        return JSON.parse(raw.slice(start, index));
      }
      if (raw[index] === "\\") {
        index += 1;
        if (raw[index] === "u") {
          if (!/^[0-9a-fA-F]{4}$/u.test(raw.slice(index + 1, index + 5))) fail("invalid unicode escape");
          index += 5;
          continue;
        }
        if (!/["\\/bfnrt]/u.test(raw[index] || "")) fail("invalid string escape");
      }
      index += 1;
    }
    fail("unterminated JSON string");
  };
  const parseValue = (path) => {
    skipWhitespace();
    const token = raw[index];
    if (token === "{") {
      index += 1;
      skipWhitespace();
      const keys = new Set();
      if (raw[index] === "}") { index += 1; return; }
      while (index < raw.length) {
        skipWhitespace();
        const key = parseString();
        const keyPath = `${path}.${key}`;
        if (keys.has(key)) errors.push(`${label}: duplicate JSON key ${keyPath}`);
        keys.add(key);
        skipWhitespace();
        if (raw[index] !== ":") fail("expected colon");
        index += 1;
        parseValue(keyPath);
        skipWhitespace();
        if (raw[index] === "}") { index += 1; return; }
        if (raw[index] !== ",") fail("expected comma or object close");
        index += 1;
      }
      fail("unterminated JSON object");
    }
    if (token === "[") {
      index += 1;
      skipWhitespace();
      if (raw[index] === "]") { index += 1; return; }
      let itemIndex = 0;
      while (index < raw.length) {
        parseValue(`${path}[${itemIndex}]`);
        itemIndex += 1;
        skipWhitespace();
        if (raw[index] === "]") { index += 1; return; }
        if (raw[index] !== ",") fail("expected comma or array close");
        index += 1;
      }
      fail("unterminated JSON array");
    }
    if (token === '"') { parseString(); return; }
    for (const literal of ["true", "false", "null"]) {
      if (raw.startsWith(literal, index)) { index += literal.length; return; }
    }
    const number = raw.slice(index).match(/^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/u)?.[0];
    if (number) { index += number.length; return; }
    fail("invalid JSON value");
  };

  try {
    parseValue("$");
    skipWhitespace();
    if (index !== raw.length) fail("trailing JSON content");
  } catch (error) {
    errors.push(`${label}: JSON duplicate-key scan failure: ${error.message}`);
  }
  return errors;
}

function gitBlobSha1Text(value) {
  const bytes = Buffer.from(value, "utf8");
  return gitBlobSha1Bytes(bytes);
}

function gitBlobSha1Bytes(bytes) {
  return createHash("sha1").update(`blob ${bytes.length}\0`, "utf8").update(bytes).digest("hex");
}

function validateAuthority(errors, pkg, { requireCanonicalSource }) {
  const authority = pkg.authority;
  exactKeys(errors, "Authority", authority, AUTHORITY_KEYS);
  addError(errors, authority?.schemaVersion === "sales-order-core-authority.v1", "Authority: schemaVersion drift");
  addError(errors, authority?.status === "TEST_DESIGN_AUTHORITY_READY_EXECUTION_GATED", "Authority: status must remain execution-gated");
  addError(errors, authority?.version === 1 && authority?.date === "2026-09-02", "Authority: version/date drift");
  addError(errors, authority?.sourceSnapshot === SOURCE_SNAPSHOT, "Authority: source snapshot drift");
  addError(errors, authority?.productionActivation === "NO_GO", "Authority: production activation must remain NO_GO");

  exactKeys(errors, "Authority shared CI gate", authority?.sharedCiGate, AUTHORITY_CI_KEYS);
  const ci = authority?.sharedCiGate;
  addError(errors, ci?.id === "G-SAL-CI-01" && ci?.status === "OPEN_NO_GO", "Authority: G-SAL-CI-01 must remain OPEN_NO_GO");
  addError(errors, ci?.validatorCommand === "node docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_v1.mjs", "Authority: exact validator command drift");
  addError(errors, ci?.mutationCommand === "node --test docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_MUTATION_TEST_v1.mjs", "Authority: exact mutation command drift");
  addError(errors, ci?.visibleRunnerStepRequired === true && ci?.nonzeroFailurePropagationRequired === true && ci?.generalCiClosesGate === false && ci?.workflowWiringEvidence === null, "Authority: shared CI fail-closed contract drift");

  addError(errors, JSON.stringify(authority?.canonicalScreenSourceMapping) === JSON.stringify(SCREEN_SOURCE_PINS), "Authority: canonical screen/source mapping drift");
  addError(errors, JSON.stringify(authority?.externalBinarySourcePins) === JSON.stringify(EXTERNAL_BINARY_SOURCE_PINS), "Authority: external binary source pin drift");
  if (requireCanonicalSource) {
    for (const pin of EXTERNAL_BINARY_SOURCE_PINS) {
      const bytes = pkg.externalBinarySources?.[pin.path];
      addError(errors, bytes && bytes.byteLength > 0, `Authority external binary source ${pin.path}: bytes were not loaded`);
      if (!bytes || bytes.byteLength === 0) continue;
      addError(errors, sha256Bytes(bytes) === pin.sha256, `Authority external binary source ${pin.path}: content digest drift`);
      addError(errors, gitBlobSha1Bytes(bytes) === pin.gitBlobSha, `Authority external binary source ${pin.path}: true git blob recomputation drift`);
    }
  }

  const canonicalInventory = authority?.canonicalSourceInventory || [];
  addError(errors, sameArray(canonicalInventory.map((item) => item.path), CANONICAL_SOURCE_PINS.map((item) => item.path)), "Authority: canonical source path/order drift");
  for (let index = 0; index < CANONICAL_SOURCE_PINS.length; index += 1) {
    const actual = canonicalInventory[index];
    const expected = CANONICAL_SOURCE_PINS[index];
    exactKeys(errors, `Authority canonical source ${expected.path}`, actual, ["path", "gitBlobSha", "sha256"]);
    addError(errors, actual?.gitBlobSha === expected.gitBlobSha, `Authority canonical source ${expected.path}: git blob drift`);
    addError(errors, /^[0-9a-f]{64}$/u.test(actual?.sha256 || ""), `Authority canonical source ${expected.path}: invalid SHA-256`);
  }
  if (requireCanonicalSource && pkg.canonicalSources) {
    for (const item of canonicalInventory) {
      const content = pkg.canonicalSources[item.path];
      addError(errors, nonEmptyString(content), `Authority canonical source ${item.path}: bytes were not loaded`);
      if (!nonEmptyString(content)) continue;
      addError(errors, sha256Text(content) === item.sha256, `Authority canonical source ${item.path}: content digest drift`);
      addError(errors, gitBlobSha1Text(content) === item.gitBlobSha, `Authority canonical source ${item.path}: true git blob recomputation drift`);
    }
  }

  const sourceCatalog = authority?.sourceReferenceCatalog || {};
  addError(errors, sameArray(Object.keys(sourceCatalog), [...ALLOWED_SOURCE_REFS]), "Authority: source reference catalog key/order drift");
  addError(errors, authority?.sourceReferenceCatalogSha256 === SOURCE_REFERENCE_CATALOG_SHA256, "Authority: source reference catalog descriptor digest drift");
  addError(errors, sha256Json(sourceCatalog) === SOURCE_REFERENCE_CATALOG_SHA256, "Authority: source reference catalog semantic drift");
  const canonicalPaths = new Set(canonicalInventory.map((item) => item.path));
  const binaryPaths = new Set((authority?.externalBinarySourcePins || []).map((item) => item.path));
  const packagePaths = new Set((authority?.packageArtifactInventory || []).map((item) => item.path));
  for (const [ref, resolver] of Object.entries(sourceCatalog)) {
    addError(errors, nonEmptyString(resolver), `Authority source ${ref}: resolver missing`);
    const match = nonEmptyString(resolver) ? resolver.match(/^(local|binary|package|issue):([^#]+)#(.+)$/u) : null;
    addError(errors, Boolean(match), `Authority source ${ref}: malformed resolver`);
    if (!match) continue;
    const [, kind, target, locator] = match;
    addError(errors, nonEmptyString(locator), `Authority source ${ref}: locator missing`);
    if (kind === "local") addError(errors, canonicalPaths.has(target), `Authority source ${ref}: local resolver is not canonical-pinned`);
    if (kind === "binary") addError(errors, binaryPaths.has(target), `Authority source ${ref}: binary resolver is not pinned`);
    if (kind === "package") addError(errors, packagePaths.has(target), `Authority source ${ref}: package resolver is not artifact-pinned`);
    if (kind === "issue") addError(errors, /^https:\/\/github\.com\/sinangym\/GoymenERP\/issues\/(?:98|99|100)$/u.test(target), `Authority source ${ref}: external issue resolver drift`);
  }

  const artifactTexts = {
    [PATHS.spec]: pkg.spec,
    [PATHS.acceptance]: pkg.acceptance,
    [PATHS.scenarios]: pkg.jsonRaw,
    [PATHS.validator]: pkg.validatorRaw,
    [PATHS.mutation]: pkg.mutationRaw,
  };
  const artifactInventory = authority?.packageArtifactInventory || [];
  addError(errors, sameArray(artifactInventory.map((item) => item.path), Object.keys(artifactTexts)), "Authority: package artifact path/order drift");
  for (const item of artifactInventory) {
    exactKeys(errors, `Authority artifact ${item.path}`, item, ["path", "sha256"]);
    addError(errors, sha256Text(artifactTexts[item.path] || "") === item.sha256, `Authority artifact ${item.path}: content digest drift`);
    if (TRUSTED_ARTIFACT_SHA256[item.path]) {
      addError(errors, item.sha256 === TRUSTED_ARTIFACT_SHA256[item.path], `Authority artifact ${item.path}: executable trust-anchor digest drift`);
    }
  }

  const mapping = authority?.exactCaseToCanonicalAt || {};
  addError(errors, sameArray(Object.keys(mapping), pkg.data.cases.map((item) => item.id)), "Authority: exact case-to-AT key/order drift");
  for (const item of pkg.data.cases) addError(errors, sameArray(mapping[item.id], item.atRefs), `Authority ${item.id}: exact case-to-AT mapping drift`);

  const sourceMapping = authority?.exactCaseToSourceRefs || {};
  addError(errors, sameArray(Object.keys(sourceMapping), pkg.data.cases.map((item) => item.id)), "Authority: exact case-to-source key/order drift");
  for (const item of pkg.data.cases) addError(errors, sameArray(sourceMapping[item.id], item.sourceRefs), `Authority ${item.id}: exact case-to-source mapping drift`);

  addError(errors, authority?.caseSemanticContractSha256 === CASE_SEMANTIC_CONTRACT_SHA256, "Authority: case semantic contract digest drift");
  addError(errors, authority?.scenarioMetadataContractSha256 === SCENARIO_METADATA_CONTRACT_SHA256, "Authority: scenario metadata contract digest drift");
  addError(errors, authority?.goldenFixtureContractSha256 === GOLDEN_FIXTURE_CONTRACT_SHA256, "Authority: golden fixture contract digest drift");
  addError(errors, authority?.ownerDecisionGateContractSha256 === OWNER_DECISION_GATE_CONTRACT_SHA256, "Authority: owner-decision semantic contract digest drift");
  addError(errors, authority?.implementationGateContractSha256 === IMPLEMENTATION_GATE_CONTRACT_SHA256, "Authority: implementation-gate semantic contract digest drift");
  addError(errors, sameArray(authority?.requiredGoldenPromotionDescriptor, PROMOTION_FIELDS), "Authority: independent golden promotion descriptor drift");
  addError(errors, sameArray(authority?.ownerDecisionGateIds, DECISION_GATES), "Authority: owner decision gate inventory drift");
  addError(errors, sameArray(authority?.implementationGateIds, IMPLEMENTATION_GATES), "Authority: implementation gate inventory drift");
  addError(errors, sameArray(authority?.nonClaims, AUTHORITY_NON_CLAIMS), "Authority: non-claims drift");
}

function validateStringArray(errors, label, values, { allowEmpty = false, allowed = null } = {}) {
  addError(errors, Array.isArray(values), `${label}: must be an array`);
  if (!Array.isArray(values)) return;
  addError(errors, allowEmpty || values.length > 0, `${label}: must not be empty`);
  addError(errors, values.every(nonEmptyString), `${label}: values must be non-empty strings`);
  addError(errors, new Set(values).size === values.length, `${label}: duplicate values are forbidden`);
  if (allowed) {
    for (const value of values) addError(errors, allowed.has(value), `${label}: unknown value ${value}`);
  }
}

function expectedCaseIds() {
  return GROUPS.flatMap(([group, count]) => Array.from({ length: count }, (_, index) => `CASE-SAL-${group}-${String(index + 1).padStart(3, "0")}`));
}

function expectedAtRefsByCase() {
  const result = Object.fromEntries(expectedCaseIds().map((id) => [id, []]));
  for (const [at, ids] of Object.entries(AT_CASES)) {
    for (const id of ids) result[id].push(at);
  }
  return result;
}

function parseMarkdownCases(markdown, errors) {
  const rows = [];
  for (const line of markdown.split(/\r?\n/u)) {
    if (!/^\|\s*`CASE-SAL-/u.test(line)) continue;
    const cells = line.slice(1, line.endsWith("|") ? -1 : undefined).split("|").map((cell) => cell.trim());
    if (cells.length !== 7) {
      errors.push(`Markdown parity: expected 7 cells, got ${cells.length}: ${line.slice(0, 80)}`);
      continue;
    }
    const idMatch = cells[0].match(/^`(CASE-SAL-[^`]+)`$/u);
    if (!idMatch) {
      errors.push(`Markdown parity: malformed case ID cell ${cells[0]}`);
      continue;
    }
    const tokens = (cell) => cell === "—" ? [] : [...cell.matchAll(/`([^`]+)`/gu)].map((match) => match[1]);
    rows.push({
      id: idMatch[1], title: cells[1], context: cells[2], outcome: cells[3],
      evidence: tokens(cells[4]), atRefs: tokens(cells[5]), gateIds: tokens(cells[6]),
    });
  }
  return rows;
}

function validateMarkdownParity(errors, data, acceptance) {
  const rows = parseMarkdownCases(acceptance, errors);
  addError(errors, rows.length === data.cases.length, `Markdown parity: ${rows.length} rows != ${data.cases.length} JSON cases`);
  const jsonIds = data.cases.map((item) => item.id);
  addError(errors, sameArray(rows.map((row) => row.id), jsonIds), "Markdown parity: case ID/order drift");
  for (let index = 0; index < Math.min(rows.length, data.cases.length); index += 1) {
    const row = rows[index];
    const item = data.cases[index];
    addError(errors, row.title === item.title, `Markdown parity ${item.id}: title drift`);
    addError(errors, row.context === `${item.precondition} **Eylem:** ${item.action}`, `Markdown parity ${item.id}: precondition/action drift`);
    addError(errors, row.outcome === `**Beklenen:** ${item.expected.join(" ")} **Yasak:** ${item.forbidden.join(" ")}`, `Markdown parity ${item.id}: expected/forbidden drift`);
    addError(errors, sameArray(row.evidence, item.evidence), `Markdown parity ${item.id}: evidence drift`);
    addError(errors, sameArray(row.atRefs, item.atRefs), `Markdown parity ${item.id}: AT mapping drift`);
    addError(errors, sameArray(row.gateIds, item.gateIds), `Markdown parity ${item.id}: gate mapping drift`);
  }
}

function parseSpecTableRows(spec, prefix, expectedCellCount, errors) {
  const rows = [];
  for (const line of spec.split(/\r?\n/u)) {
    if (!line.startsWith(`| \`${prefix}`)) continue;
    const cells = line.slice(1, line.endsWith("|") ? -1 : undefined).split("|").map((cell) => cell.trim());
    if (cells.length !== expectedCellCount) {
      errors.push(`Spec parity ${prefix}: expected ${expectedCellCount} cells, got ${cells.length}`);
      continue;
    }
    const idMatch = cells[0].match(/^`([^`]+)`$/u);
    if (!idMatch) {
      errors.push(`Spec parity ${prefix}: malformed ID ${cells[0]}`);
      continue;
    }
    rows.push({ id: idMatch[1], cells });
  }
  return rows;
}

function validateSpecGateParity(errors, data, spec) {
  const decisionRows = parseSpecTableRows(spec, "SAL-", 3, errors);
  addError(errors, sameArray(decisionRows.map((row) => row.id), DECISION_GATES), "Spec parity: 16 owner decision ID/order drift");

  const implementationRows = parseSpecTableRows(spec, "G-SAL-", 5, errors);
  addError(errors, sameArray(implementationRows.map((row) => row.id), IMPLEMENTATION_GATES), "Spec parity: 18 implementation gate ID/order drift");
  const gateById = Object.fromEntries((data.gates || []).map((item) => [item.id, item]));
  for (const row of implementationRows) {
    const declared = row.cells[2] === "—" ? [] : [...row.cells[2].matchAll(/`(SAL-[^`]+)`/gu)].map((match) => match[1]);
    addError(errors, sameArray(declared, gateById[row.id]?.dependsOnDecisionGateIds || []), `Spec parity ${row.id}: dependsOnDecisionGateIds drift`);
  }
}

function decimal(value) {
  const text = String(value);
  const match = text.match(/^(-?)(\d+)(?:\.(\d+))?$/u);
  if (!match) throw new Error(`invalid decimal ${text}`);
  const scale = (match[3] || "").length;
  const denominator = 10n ** BigInt(scale);
  const numerator = BigInt(`${match[1]}${match[2]}${match[3] || ""}`);
  return { numerator, denominator };
}

function fraction(numerator, denominator) {
  if (denominator === 0n) throw new Error("zero denominator");
  return denominator < 0n ? { numerator: -numerator, denominator: -denominator } : { numerator, denominator };
}

const add = (a, b) => fraction(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
const subtract = (a, b) => fraction(a.numerator * b.denominator - b.numerator * a.denominator, a.denominator * b.denominator);
const multiply = (a, b) => fraction(a.numerator * b.numerator, a.denominator * b.denominator);
const divide = (a, b) => fraction(a.numerator * b.denominator, a.denominator * b.numerator);
const equal = (a, b) => a.numerator * b.denominator === b.numerator * a.denominator;
const lessThan = (a, b) => a.numerator * b.denominator < b.numerator * a.denominator;

function fixtureById(data, id) {
  return data.goldenFixtures.find((fixture) => fixture.id === id);
}

function validateGolden(errors, data) {
  addError(errors, sameArray(data.goldenFixtures.map((item) => item.id), GOLDEN_IDS), "Golden fixtures: exact ID/order drift");
  addError(errors, sha256Json(data.goldenFixtures) === GOLDEN_FIXTURE_CONTRACT_SHA256, "Golden fixtures: exact parsed semantic contract drift");
  for (const fixture of data.goldenFixtures) {
    exactKeys(errors, `Golden ${fixture.id}`, fixture, GOLDEN_KEYS);
    addError(errors, fixture.status === "SYNTHETIC_CONTRACT_ONLY", `Golden ${fixture.id}: promotion/provenance must stay SYNTHETIC_CONTRACT_ONLY`);
    validateStringArray(errors, `Golden ${fixture.id}.gateIds`, fixture.gateIds, { allowEmpty: false, allowed: new Set(IMPLEMENTATION_GATES) });
    validateStringArray(errors, `Golden ${fixture.id}.atRefs`, fixture.atRefs, { allowEmpty: true, allowed: new Set(Object.keys(CANONICAL_AT)) });
    const pin = GOLDEN_SHAPE_PINS[fixture.id];
    addError(errors, Boolean(pin), `Golden ${fixture.id}: missing exact shape pin`);
    if (pin) {
      addError(errors, fixture.formulaRef === pin.formulaRef, `Golden ${fixture.id}: formulaRef drift`);
      addError(errors, sameArray(Object.keys(fixture.inputs || {}), pin.inputKeys), `Golden ${fixture.id}: exact input key/order drift`);
      addError(errors, sameArray(Object.keys(fixture.expected || {}), pin.expectedKeys), `Golden ${fixture.id}: exact expected key/order drift`);
      addError(errors, sameArray(fixture.gateIds, pin.gateIds), `Golden ${fixture.id}: exact owner gate mapping drift`);
      addError(errors, sameArray(fixture.atRefs, pin.atRefs), `Golden ${fixture.id}: exact AT mapping drift`);
    }
  }

  const price = fixtureById(data, "GOLD-SAL-PRICE-001");
  if (price) {
    const result = divide(multiply(decimal(price.inputs.listPrice), subtract(decimal("100"), decimal(price.inputs.discountPercent))), decimal("100"));
    addError(errors, equal(result, decimal(price.expected.discountedNet)), "Golden GOLD-SAL-PRICE-001: SIM-001 arithmetic drift");
    addError(errors, lessThan(result, decimal(price.inputs.syntheticFloor)) && price.expected.comparison === "BELOW_SYNTHETIC_FLOOR", "Golden GOLD-SAL-PRICE-001: floor comparison drift");
    addError(errors, sameArray(price.atRefs, ["AT-021"]), "Golden GOLD-SAL-PRICE-001: AT mapping drift");
  }

  const risk = fixtureById(data, "GOLD-SAL-RISK-001");
  if (risk) {
    const result = add(decimal(risk.inputs.currentGrossRisk), decimal(risk.inputs.candidateApprovedUndispatchedDelta));
    addError(errors, equal(result, decimal(risk.expected.afterGrossRisk)), "Golden GOLD-SAL-RISK-001: risk arithmetic drift");
    addError(errors, !lessThan(result, decimal(risk.inputs.syntheticLimit)) && risk.expected.comparison === "ABOVE_SYNTHETIC_LIMIT", "Golden GOLD-SAL-RISK-001: limit comparison drift");
    addError(errors, sameArray(risk.atRefs, ["AT-022"]), "Golden GOLD-SAL-RISK-001: AT mapping drift");
  }

  const conversion = fixtureById(data, "GOLD-SAL-CONV-001");
  if (conversion) {
    const result = multiply(decimal(conversion.inputs.enteredQuantity), decimal(conversion.inputs.factor));
    addError(errors, equal(result, decimal(conversion.expected.primaryQuantity)), "Golden GOLD-SAL-CONV-001: conversion arithmetic drift");
    addError(errors, conversion.atRefs.length === 0, "Golden GOLD-SAL-CONV-001: fixed-factor fixture is local-only, not closing AT-016 rounding/tolerance");
  }

  const term = fixtureById(data, "GOLD-SAL-TERM-001");
  if (term) {
    const result = term.inputs.sliceQuantities.map(decimal).reduce(add, decimal("0"));
    addError(errors, equal(result, decimal(term.expected.sliceTotal)) && equal(result, decimal(term.inputs.lineQuantity)), "Golden GOLD-SAL-TERM-001: structural sum drift");
    addError(errors, term.expected.reconciled === true && term.atRefs.length === 0, "Golden GOLD-SAL-TERM-001: term rule must remain local and reconciled");
  }

  const remain = fixtureById(data, "GOLD-SAL-REMAIN-001");
  if (remain) {
    const result = subtract(subtract(decimal(remain.inputs.netActiveOrderQuantity), decimal(remain.inputs.realizedQuantity)), decimal(remain.inputs.cancelledQuantity));
    addError(errors, equal(result, decimal(remain.expected.remainingQuantity)), "Golden GOLD-SAL-REMAIN-001: INV-005 arithmetic drift");
    addError(errors, sameArray(remain.atRefs, ["AT-024"]), "Golden GOLD-SAL-REMAIN-001: AT mapping drift");
    addError(errors, sameArray(remain.gateIds, ["G-SAL-FULFILL-01"]), "Golden GOLD-SAL-REMAIN-001: fulfillment owner gate drift");
  }

  const revision = fixtureById(data, "GOLD-SAL-REV-001");
  if (revision) {
    addError(errors, revision.expected.baseRevisionImmutable === true && revision.expected.approvalInherited === false && revision.expected.reapprovalRequired === true, "Golden GOLD-SAL-REV-001: critical revision/reapproval drift");
    addError(errors, sameArray(revision.atRefs, ["AT-023"]), "Golden GOLD-SAL-REV-001: AT mapping drift");
  }

  const effectiveKg = fixtureById(data, "GOLD-SAL-EFFECTIVE-KG-001");
  if (effectiveKg) {
    const order = divide(decimal(effectiveKg.inputs.orderAmount), decimal(effectiveKg.inputs.orderTheoreticalKgT));
    const fulfillment = divide(decimal(effectiveKg.inputs.fulfillmentAmount), decimal(effectiveKg.inputs.actualDispatchKgG));
    const invoice = divide(decimal(effectiveKg.inputs.invoiceAmount), decimal(effectiveKg.inputs.invoiceLinkedActualKgG));
    addError(errors, equal(order, decimal(effectiveKg.expected.orderEffectiveKgPrice)), "Golden GOLD-SAL-EFFECTIVE-KG-001: order amount/theoretical kg T arithmetic drift");
    addError(errors, equal(fulfillment, decimal(effectiveKg.expected.fulfillmentEffectiveKgPrice)), "Golden GOLD-SAL-EFFECTIVE-KG-001: fulfillment amount/actual dispatch kg G arithmetic drift");
    addError(errors, equal(invoice, decimal(effectiveKg.expected.invoiceEffectiveKgPrice)), "Golden GOLD-SAL-EFFECTIVE-KG-001: invoice amount/invoice-linked actual kg G arithmetic drift");
    addError(errors, effectiveKg.expected.roundingApplied === false, "Golden GOLD-SAL-EFFECTIVE-KG-001: unapproved rounding must remain disabled");
  }

  const uninvoiced = fixtureById(data, "GOLD-SAL-UNINVOICED-001");
  if (uninvoiced) {
    const tl = subtract(decimal(uninvoiced.inputs.operationFulfilledTl), decimal(uninvoiced.inputs.invoiceFulfilledTl));
    const usd = subtract(decimal(uninvoiced.inputs.operationFulfilledUsd), decimal(uninvoiced.inputs.invoiceFulfilledUsd));
    addError(errors, equal(tl, decimal(uninvoiced.expected.uninvoicedFulfilledTl)), "Golden GOLD-SAL-UNINVOICED-001: TL commercial amount arithmetic drift");
    addError(errors, equal(usd, decimal(uninvoiced.expected.uninvoicedFulfilledUsd)), "Golden GOLD-SAL-UNINVOICED-001: USD commercial amount arithmetic drift");
    addError(errors, uninvoiced.expected.invoiceLinkedQuantityRemainsSeparate === true, "Golden GOLD-SAL-UNINVOICED-001: invoice-linked quantity separation drift");
  }

  const allocation = fixtureById(data, "GOLD-SAL-ALLOC-PCT-001");
  if (allocation) {
    const need = allocation.inputs.physicalNeedKg.map(decimal).reduce(add, decimal("0"));
    const allocated = allocation.inputs.physicalAllocatedKg.map(decimal).reduce(add, decimal("0"));
    const percentage = multiply(divide(allocated, need), decimal("100"));
    addError(errors, equal(need, decimal(allocation.expected.physicalNeedDenominatorKg)), "Golden GOLD-SAL-ALLOC-PCT-001: physical-only denominator drift");
    addError(errors, equal(allocated, decimal(allocation.expected.physicalAllocatedNumeratorKg)), "Golden GOLD-SAL-ALLOC-PCT-001: physical-only numerator drift");
    addError(errors, equal(percentage, decimal(allocation.expected.allocationPercentExact)), "Golden GOLD-SAL-ALLOC-PCT-001: allocation percentage arithmetic drift");
    addError(errors, allocation.expected.stocklessServiceExcluded === true, "Golden GOLD-SAL-ALLOC-PCT-001: stockless Service must remain excluded");
  }
}

function parseCanonicalAcceptance(markdown) {
  const result = {};
  for (const match of markdown.matchAll(/^- \*\*(AT-\d{3}):\*\* (.+)$/gmu)) result[match[1]] = match[2].trim();
  return result;
}

function validateCanonicalSources(errors, canonical) {
  if (!canonical) {
    errors.push("Canonical source: required repository files were not loaded");
    return;
  }
  const parsed = parseCanonicalAcceptance(canonical.acceptance);
  for (const [id, meaning] of Object.entries(CANONICAL_AT)) {
    addError(errors, parsed[id] === meaning, `Canonical source ${id}: meaning drift or missing entry`);
  }
  addError(errors, /(?:^|\r?\n)INV-005,Satış satırı açık ihtiyaç,`NetAktifSipariş − Gerçekleşen − İptal`(?:\r?\n|$)/u.test(canonical.formulas), "Canonical source INV-005: formula drift or missing entry");
  addError(errors, /(?:^|\r?\n)SIM-001,İskontolu TL\/birim,`Liste × \(100 − İskonto%\) \/ 100`(?:\r?\n|$)/u.test(canonical.formulas), "Canonical source SIM-001: formula drift or missing entry");
  for (const decision of ["DEC-SAL-001", "DEC-SAL-002", "DEC-SAL-003", "DEC-SAL-004", "DEC-SAL-005", "DEC-SAL-006", "DEC-SAL-007", "DEC-SAL-008"]) {
    addError(errors, canonical.decisions.includes(decision), `Canonical source ${decision}: missing from decision register`);
  }
}

function validateLockedSemantics(errors, data, spec) {
  const byId = Object.fromEntries(data.cases.map((item) => [item.id, item]));
  const expectedText = (id) => byId[id]?.expected?.join(" ") || "";
  const forbiddenText = (id) => byId[id]?.forbidden?.join(" ") || "";
  const requireExpected = (id, pattern, label) => {
    addError(errors, pattern.test(expectedText(id)), `${id}: expected polarity ${label}`);
  };
  const requireForbidden = (id, pattern, label) => {
    addError(errors, pattern.test(forbiddenText(id)), `${id}: forbidden polarity ${label}`);
  };
  const forbidExpected = (id, pattern, label) => {
    addError(errors, !pattern.test(expectedText(id)), `${id}: expected polarity must not ${label}`);
  };
  const forbidForbidden = (id, pattern, label) => {
    addError(errors, !pattern.test(forbiddenText(id)), `${id}: forbidden polarity must not ${label}`);
  };

  requireExpected("CASE-SAL-S01-001", /Tek Sales record ID korunur/u, "must preserve one Sales identity");
  requireForbidden("CASE-SAL-S01-001", /ayrı aggregate\/duplicate identity|ayrı ön sipariş/u, "must prohibit separate pre-order aggregates");
  forbidExpected("CASE-SAL-S01-001", /ayrı aggregate\/duplicate identity|ayrı ön sipariş/u, "contain the prohibited aggregate split");
  forbidForbidden("CASE-SAL-S01-001", /Tek Sales record ID korunur/u, "contain the required identity preservation");
  requireExpected("CASE-SAL-S01-004", /Aynı Sales record ID Sipariş aşamasına ilerler/u, "must preserve identity through Order transition");
  requireForbidden("CASE-SAL-S01-004", /Ayrı ön sipariş\/order aggregate yaratılmaz/u, "must prohibit a separate Order aggregate");
  addError(errors, byId["CASE-SAL-S01-001"]?.sourceRefs?.includes("SALES-LOCK-DOCX") && byId["CASE-SAL-S01-004"]?.sourceRefs?.includes("SALES-LOCK-DOCX"), "S-01 single-record identity cases must resolve to the locked Sales DOCX");

  requireExpected("CASE-SAL-S02-002", /Her axis kendi label\/value/u, "must preserve seven independent axes");
  requireForbidden("CASE-SAL-S02-002", /Tek stored renk\/status/u, "must prohibit one collapsed status");
  requireExpected("CASE-SAL-S04-003", /Her header değişikliği ayrı yeni revision\/diff üretir; Rev\.0 immutable kalır\. Eski approval yeni revision'a miras kalmaz ve reapproval zorunludur/u, "must create a local DEC revision without approval inheritance");
  requireExpected("CASE-SAL-S04-004", /Yeni revision\/diff\/snapshot oluşur[^.]*\. [^.]*approval tarihsel kalır[^.]*\. Yeni revision approved sayılmaz/u, "must create a revision without approval inheritance");
  requireExpected("CASE-SAL-S04-005", /Her satır değişikliği yeni revision ve reapproval gerektirir/u, "must create a revision and reapproval");
  requireExpected("CASE-SAL-DEP-004", /approval inheritance yapmaz/u, "must reject approval inheritance");
  requireForbidden("CASE-SAL-DEP-004", /Stale task current revision'ı approve etmez/u, "must prohibit stale-task approval");
  requireExpected("CASE-SAL-S04-006", /Approval reference geçerli kalır/u, "must keep approval on a term-only change");
  requireForbidden("CASE-SAL-S04-006", /Yeni approval sırf termin nedeniyle açılmaz/u, "must prohibit term-only reapproval");
  requireExpected("CASE-SAL-S03-013", /owner-resolved approval policy\/route reference/u, "must resolve the correct approval route");
  requireForbidden("CASE-SAL-S03-013", /UI hard-coded floor\/rol\/route/u, "must prohibit a hard-coded route");
  requireExpected("CASE-SAL-S03-016", /Manuel Draft save AI olmadan çalışır/u, "must preserve the AI-off manual path");
  requireForbidden("CASE-SAL-S03-016", /nihai sipariş onayı vermez/u, "must prohibit AI final order approval");
  requireForbidden("CASE-SAL-S03-015", /Auto-pass, auto-approve/u, "must fail closed when S-05/S-06 are missing");
  requireExpected("CASE-SAL-DEP-003", /Owner matrix\/SoD fail-closed/u, "must preserve maker-checker/SoD");
  requireExpected("CASE-SAL-S02-004", /Yalnız Sales owner\/discriminator/u, "must preserve the Sales discriminator");
  requireForbidden("CASE-SAL-S02-004", /Purchase row/u, "must prohibit Purchase rows");
  requireExpected("CASE-SAL-S02-003", /Remaining 500 kg/u, "must preserve the partial-delivery oracle");
  requireExpected("CASE-SAL-S04-013", /önceki revision, snapshot, approval ve audit kanıtı okunabilir kalır/u, "must preserve cancellation history");
  requireForbidden("CASE-SAL-S04-013", /hard-delete edilmez/u, "must prohibit destructive cancellation");

  requireExpected("CASE-SAL-LOCK-004", /Order amount\/theoretical kg T; fulfillment amount\/actual dispatch kg G; invoice amount\/invoice-linked actual kg G/u, "must pin T/G denominators");
  requireForbidden("CASE-SAL-LOCK-004", /T ve G karıştırılmaz/u, "must prohibit T/G substitution");
  requireExpected("CASE-SAL-LOCK-005", /operation fulfilled amount eksi invoice fulfilled amount/u, "must pin uninvoiced commercial amount");
  requireExpected("CASE-SAL-LOCK-005", /Faturalanmamış Gerçekleşen yalnız detay açıldığında görünür/u, "must preserve detail-only uninvoiced visibility");
  requireForbidden("CASE-SAL-LOCK-005", /allocation\/order quantity ticari tutar yerine kullanılmaz/u, "must prohibit quantity-as-amount substitution");
  requireForbidden("CASE-SAL-LOCK-005", /daraltılmış tek satırda zorunlu kılınmaz/u, "must preserve locked-source visibility adjudication");
  requireExpected("CASE-SAL-LOCK-006", /Physical-only allocation yüzde 60/u, "must pin the physical-only denominator");
  requireForbidden("CASE-SAL-LOCK-006", /Service `0 kg` satır olarak paydaya eklenmez/u, "must exclude stockless Service");
  requireExpected("CASE-SAL-LOCK-009", /yalnız Allocation owner sonucu source\/tahsis axis'ini değiştirir/u, "must pin allocation ownership");
  requireForbidden("CASE-SAL-LOCK-009", /Draft save rezerv, stok hareketi, purchase veya production kaydı yaratmaz/u, "must prohibit source-preference side effects");
  requireExpected("CASE-SAL-LOCK-010", /remaining yalnız Fulfillment owner verisiyle hesaplanır/u, "must pin fulfillment ownership");
  requireForbidden("CASE-SAL-LOCK-010", /Tam tahsis otomatik gerçekleşme/u, "must prohibit allocation-as-fulfillment");
  requireExpected("CASE-SAL-LOCK-001", /varsayılan olarak tek kompakt yatay satırdır; Detayı Göster ile açılır/u, "must preserve compact summary disclosure");
  requireExpected("CASE-SAL-LOCK-001", /aktif satırlar ve fiyat hareketleriyle birebir uzlaşır/u, "must reconcile summary with active lines and price movements");
  requireExpected("CASE-SAL-LOCK-001", /Kalan tutar Sipariş Tutarı eksi Operasyon Gerçekleşen tutarıdır/u, "must preserve the locked remaining-amount basis");
  requireExpected("CASE-SAL-LOCK-001", /adetli satırın KG karşılığı ikinci kez toplama eklenmez/u, "must prohibit quantity double counting");
  requireExpected("CASE-SAL-LOCK-001", /operasyon yüzdesi mal ve hizmet için ortak gerçekleşme oranıdır; fatura yüzdesi gerçekleşen\/faturalanabilir kısım üzerinden/u, "must preserve the locked operation/invoice percentage bases");
  requireExpected("CASE-SAL-LOCK-001", /Tahsis yüzdesi[^.]+en yakın açık termin/u, "must preserve owner-based percentages and nearest open term");
  requireForbidden("CASE-SAL-LOCK-001", /Kalan tutar fatura veya tahsis verisinden türetilmez/u, "must prohibit a wrong remaining-amount basis");
  requireExpected("CASE-SAL-LOCK-008", /yalnız owner-authorized apply capability fiyat hareketini uygular/u, "must gate invoice-stage price movement application");
  requireExpected("CASE-SAL-LOCK-008", /capability'siz accounting actor taslak hazırlayıp onaya gönderebilir/u, "must preserve prepare-to-approval behavior");
  requireExpected("CASE-SAL-LOCK-008", /append-only actor\/time\/stage\/reason\/audit/u, "must preserve price-movement audit history");
  requireForbidden("CASE-SAL-LOCK-008", /geçmiş fiyat hareketi silinmez veya sessiz yeniden yazılmaz/u, "must prohibit destructive price-movement history");

  for (const token of ["DEC-SAL-001", "DEC-SAL-002", "DEC-SAL-003", "DEC-SAL-004", "DEC-SAL-005", "DEC-SAL-006", "DEC-SAL-007", "DEC-SAL-008", "AT-007", "INV-005 = NetAktifSipariş − Gerçekleşen − İptal", "production submit/approve/activate", "production NO-GO", "G-SAL-CI-01", "Detayı Göster", "Kalan = Sipariş Tutarı − Operasyon Gerçekleşen", "append-only auditlidir", "nihai sipariş onayı vermez"]) {
    addError(errors, spec.includes(token), `Spec locked semantic missing: ${token}`);
  }
  addError(errors, /Exact endpoint, payload alan adı, enum\/string, permission\/capability[^\n]+uydurulmaz/u.test(spec), "Spec: exact API/enum/permission non-invention rule missing");
}

export function loadPackage(root = DEFAULT_ROOT, { requireCanonicalSource = true } = {}) {
  const read = (relativePath) => readFileSync(resolve(root, relativePath), "utf8");
  const readBytes = (relativePath) => readFileSync(resolve(root, relativePath));
  const jsonRaw = read(PATHS.scenarios);
  const authorityRaw = read(PATHS.authority);
  const result = {
    root,
    jsonRaw,
    authorityRaw,
    data: JSON.parse(jsonRaw),
    authority: JSON.parse(authorityRaw),
    acceptance: read(PATHS.acceptance),
    spec: read(PATHS.spec),
    validatorRaw: read(PATHS.validator),
    mutationRaw: read(PATHS.mutation),
    canonical: null,
    canonicalSources: null,
    externalBinarySources: null,
  };
  if (requireCanonicalSource) {
    const required = [...CANONICAL_SOURCE_PINS.map((item) => item.path), ...EXTERNAL_BINARY_SOURCE_PINS.map((item) => item.path)];
    const missing = required.filter((item) => !existsSync(resolve(root, item)));
    if (missing.length) throw new Error(`missing canonical source files: ${missing.join(", ")}`);
    result.canonicalSources = Object.fromEntries(CANONICAL_SOURCE_PINS.map((item) => [item.path, read(item.path)]));
    result.externalBinarySources = Object.fromEntries(EXTERNAL_BINARY_SOURCE_PINS.map((item) => [item.path, readBytes(item.path)]));
    result.canonical = {
      acceptance: result.canonicalSources[PATHS.canonicalAcceptance],
      formulas: result.canonicalSources[PATHS.formulaCatalog],
      decisions: result.canonicalSources[PATHS.decisionRegister],
    };
  }
  return result;
}

export function validatePackageData(pkg, { requireCanonicalSource = false } = {}) {
  const errors = [];
  const { data, acceptance, spec } = pkg;

  errors.push(...duplicateJsonKeyErrors(pkg.jsonRaw || "", "Scenario JSON"));
  errors.push(...duplicateJsonKeyErrors(pkg.authorityRaw || "", "Authority JSON"));

  try {
    addError(errors, JSON.stringify(JSON.parse(pkg.jsonRaw)) === JSON.stringify(data), "Scenario raw/parsed deep-equality drift");
  } catch (error) {
    errors.push(`Scenario raw JSON parse failure: ${error.message}`);
  }
  try {
    addError(errors, JSON.stringify(JSON.parse(pkg.authorityRaw)) === JSON.stringify(pkg.authority), "Authority raw/parsed deep-equality drift");
  } catch (error) {
    errors.push(`Authority raw JSON parse failure: ${error.message}`);
  }

  exactKeys(errors, "Top-level JSON", data, TOP_LEVEL_KEYS);
  addError(errors, data.schemaVersion === "sales-order-core-scenarios.v1", "Top-level JSON: schemaVersion drift");
  addError(errors, data.status === "TEST_DESIGN_READY_EXECUTION_GATED", "Top-level JSON: status must remain execution-gated");
  addError(errors, data.version === 1 && data.date === "2026-09-02", "Top-level JSON: version/date drift");
  addError(errors, data.sourceSnapshot === SOURCE_SNAPSHOT, "Top-level JSON: source snapshot drift");
  addError(errors, data.specPath === PATHS.spec && data.acceptancePath === PATHS.acceptance, "Top-level JSON: package path drift");
  addError(errors, sameArray(data.canonicalScreens, ["S-01", "S-02", "S-03", "S-04"]), "Top-level JSON: canonical screens drift");
  addError(errors, sameArray(data.dependencyScreens, ["S-05", "S-06"]), "Top-level JSON: dependency screens drift");
  addError(errors, sameArray(data.candidateUiRecords, ["PRODUCT-U-06"]), "Top-level JSON: candidate UI record drift");
  addError(errors, sameArray(data.canonicalAcceptance, ["AT-007", "AT-021", "AT-022", "AT-023", "AT-024", "AT-025"]), "Top-level JSON: canonical acceptance scope drift");
  addError(errors, data.exactApiEnumsPermissionsDeclared === false, "Top-level JSON: exact API/enum/permission declaration is forbidden");
  addError(errors, data.productionActivation === "NO_GO", "Top-level JSON: production activation must remain NO_GO");
  exactKeys(errors, "Fixture policy", data.fixturePolicy, FIXTURE_POLICY_KEYS);
  addError(errors, data.fixturePolicy?.classification === "SYNTHETIC_CONTRACT_ONLY" && data.fixturePolicy?.productionSeed === false && data.fixturePolicy?.businessPolicy === false && data.fixturePolicy?.requiresOwnerPromotion === true, "Fixture policy: synthetic/non-production invariant drift");
  exactKeys(errors, "Aliases", data.aliases, ALIAS_KEYS);
  for (const key of ALIAS_KEYS) {
    addError(errors, sameArray(data.aliases?.[key], ALIAS_VALUES[key]), `Aliases.${key}: exact fixture catalog drift`);
  }
  exactKeys(errors, "Parity contract", data.parity, PARITY_KEYS);
  addError(errors, sha256Json({ fixturePolicy: data.fixturePolicy, aliases: data.aliases, parity: data.parity }) === SCENARIO_METADATA_CONTRACT_SHA256, "Scenario metadata: exact fixture/alias/parity semantic contract drift");
  addError(errors, sameArray(data.requiredGoldenPromotionFields, PROMOTION_FIELDS), "Promotion gate: exact required fields/order drift");

  addError(errors, Array.isArray(data.decisionGates) && sameArray(data.decisionGates.map((item) => item.id), DECISION_GATES), "Decision gates: exact 16 ID/order drift");
  addError(errors, sha256Json(data.decisionGates) === OWNER_DECISION_GATE_CONTRACT_SHA256, "Decision gates: exact owner/topic/safeDefault semantic contract drift");
  for (const item of data.decisionGates || []) {
    exactKeys(errors, `Decision gate ${item.id}`, item, DECISION_GATE_KEYS);
    addError(errors, item.status === "OWNER_DECISION_OPEN_NO_GO", `Decision gate ${item.id}: status must remain open NO-GO`);
    addError(errors, nonEmptyString(item.topic) && nonEmptyString(item.safeDefault), `Decision gate ${item.id}: topic/safeDefault missing`);
  }

  addError(errors, Array.isArray(data.gates) && sameArray(data.gates.map((item) => item.id), IMPLEMENTATION_GATES), "Implementation gates: exact 18 ID/order drift");
  addError(errors, sha256Json(data.gates) === IMPLEMENTATION_GATE_CONTRACT_SHA256, "Implementation gates: exact owner/dependency/blocks semantic contract drift");
  for (const item of data.gates || []) {
    exactKeys(errors, `Implementation gate ${item.id}`, item, IMPLEMENTATION_GATE_KEYS);
    addError(errors, item.status === "OPEN_NO_GO", `Implementation gate ${item.id}: status must remain OPEN_NO_GO`);
    addError(errors, nonEmptyString(item.owner), `Implementation gate ${item.id}: owner missing`);
    validateStringArray(errors, `Implementation gate ${item.id}.dependsOnDecisionGateIds`, item.dependsOnDecisionGateIds, { allowEmpty: true, allowed: new Set(DECISION_GATES) });
    validateStringArray(errors, `Implementation gate ${item.id}.blocks`, item.blocks);
  }

  const expectedIds = expectedCaseIds();
  addError(errors, data.caseCount === 72, `Cases: declared caseCount ${data.caseCount} != 72`);
  addError(errors, Array.isArray(data.cases) && data.cases.length === 72, `Cases: actual count ${data.cases?.length} != 72`);
  addError(errors, sameArray((data.cases || []).map((item) => item.id), expectedIds), "Cases: ID series/order must be unique and gapless");
  const expectedAt = expectedAtRefsByCase();
  const resolvedGates = new Set([...IMPLEMENTATION_GATES, ...DECISION_GATES]);

  const caseSourceRefs = Object.fromEntries((data.cases || []).map((item) => [item.id, item.sourceRefs]));
  const expectedOutcomes = Object.fromEntries((data.cases || []).map((item) => [item.id, item.expected]));
  const forbiddenOutcomes = Object.fromEntries((data.cases || []).map((item) => [item.id, item.forbidden]));
  addError(errors, sha256Json(data.cases) === CASE_SEMANTIC_CONTRACT_SHA256, "Cases: exact parsed semantic contract drift");
  addError(errors, sha256Json(caseSourceRefs) === CASE_SOURCE_REFS_CONTRACT_SHA256, "Cases: exact per-case sourceRefs contract drift");
  addError(errors, sha256Json(expectedOutcomes) === EXPECTED_OUTCOME_CONTRACT_SHA256, "Cases: exact expected-polarity contract drift");
  addError(errors, sha256Json(forbiddenOutcomes) === FORBIDDEN_OUTCOME_CONTRACT_SHA256, "Cases: exact forbidden-polarity contract drift");

  for (const item of data.cases || []) {
    exactKeys(errors, `Case ${item.id}`, item, CASE_KEYS);
    addError(errors, nonEmptyString(item.title) && nonEmptyString(item.precondition) && nonEmptyString(item.action), `Case ${item.id}: required prose missing`);
    validateStringArray(errors, `Case ${item.id}.screenIds`, item.screenIds, { allowed: VALID_SCREENS });
    validateStringArray(errors, `Case ${item.id}.sourceRefs`, item.sourceRefs, { allowed: ALLOWED_SOURCE_REFS });
    validateStringArray(errors, `Case ${item.id}.atRefs`, item.atRefs, { allowEmpty: true, allowed: new Set(Object.keys(CANONICAL_AT)) });
    validateStringArray(errors, `Case ${item.id}.gateIds`, item.gateIds, { allowEmpty: true, allowed: resolvedGates });
    validateStringArray(errors, `Case ${item.id}.evidence`, item.evidence, { allowed: VALID_EVIDENCE });
    validateStringArray(errors, `Case ${item.id}.expected`, item.expected);
    validateStringArray(errors, `Case ${item.id}.forbidden`, item.forbidden);
    addError(errors, sameArray(item.atRefs, expectedAt[item.id] || []), `Case ${item.id}: canonical AT semantic mapping drift`);
    for (const source of item.sourceRefs) {
      if (source.startsWith("SAL-")) addError(errors, DECISION_GATES.includes(source), `Case ${item.id}: unresolved local decision source ${source}`);
    }
  }

  const declaredAliases = new Set(ALIAS_KEYS.flatMap((key) => data.aliases?.[key] || []));
  const aliasBearingPayload = [
    ...(data.cases || []).flatMap((item) => [item.precondition, item.action, ...(item.expected || []), ...(item.forbidden || [])]),
    JSON.stringify((data.goldenFixtures || []).map((item) => ({ inputs: item.inputs, expected: item.expected }))),
  ].join("\n");
  const referencedAliases = new Set(aliasBearingPayload.match(/\b(?:ACT|CMP|CUST|PRD|ORDER|PACK|TEST)-[A-Z0-9_-]+\b/gu) || []);
  for (const alias of referencedAliases) addError(errors, declaredAliases.has(alias), `Aliases: referenced fixture ${alias} is undefined`);
  for (const alias of declaredAliases) addError(errors, referencedAliases.has(alias), `Aliases: declared fixture ${alias} is unreferenced`);

  for (const requiredAt of data.canonicalAcceptance || []) {
    addError(errors, (data.cases || []).some((item) => item.atRefs.includes(requiredAt)), `Canonical coverage: ${requiredAt} has no exact-semantic case`);
  }

  const usedImplementationGates = new Set([
    ...(data.cases || []).flatMap((item) => item.gateIds.filter((id) => id.startsWith("G-SAL-"))),
    ...(data.goldenFixtures || []).flatMap((item) => item.gateIds),
  ]);
  for (const gateId of IMPLEMENTATION_GATES) addError(errors, usedImplementationGates.has(gateId), `Implementation gate ${gateId}: unreferenced by every case/golden`);

  const usedDecisionGates = new Set([
    ...(data.gates || []).flatMap((item) => item.dependsOnDecisionGateIds || []),
    ...(data.cases || []).flatMap((item) => item.gateIds.filter((id) => id.startsWith("SAL-"))),
    ...(data.cases || []).flatMap((item) => item.sourceRefs.filter((id) => id.startsWith("SAL-"))),
  ]);
  for (const gateId of DECISION_GATES) addError(errors, usedDecisionGates.has(gateId), `Decision gate ${gateId}: no machine-readable case/source/implementation dependency`);

  addError(errors, sameArray(data.parity?.requiredCaseFields, CASE_KEYS), "Parity contract: required case fields drift");
  addError(errors, data.parity?.markdownCaseCount === 72 && data.parity?.jsonCaseCount === 72, "Parity contract: declared counts drift");
  addError(errors, data.parity?.idPattern === "^CASE-SAL-(X|S01|S02|S03|S04|LOCK|DEP|SEL)-[0-9]{3}$", "Parity contract: ID pattern drift");
  addError(errors, data.parity?.order === "markdown-and-json-identical" && data.parity?.duplicateIdsAllowed === false && data.parity?.unknownTopLevelKeysAllowed === false, "Parity contract: fail-closed flags drift");
  addError(errors, sameArray(data.parity?.requiredCanonicalAcceptanceCoverage, data.canonicalAcceptance), "Parity contract: canonical coverage declaration drift");

  try {
    validateGolden(errors, data);
  } catch (error) {
    errors.push(`Golden fixtures: parse/recompute failure: ${error.message}`);
  }
  validateMarkdownParity(errors, data, acceptance);
  validateSpecGateParity(errors, data, spec);
  validateLockedSemantics(errors, data, spec);
  validateAuthority(errors, pkg, { requireCanonicalSource });

  const combined = `${spec}\n${acceptance}\n${pkg.jsonRaw || ""}`;
  addError(errors, !/\b(?:GET|POST|PUT|PATCH|DELETE)\s+\/[^\s`]+/u.test(combined) && !/\/api\//u.test(combined), "Non-invention gate: exact HTTP route detected");

  if (requireCanonicalSource) validateCanonicalSources(errors, pkg.canonical);
  return errors;
}

export function validatePackage(root = DEFAULT_ROOT, options = {}) {
  let pkg;
  try {
    pkg = loadPackage(root, options);
  } catch (error) {
    return [`Load failure: ${error.message}`];
  }
  return validatePackageData(pkg, options);
}

function main() {
  const standalone = process.argv.includes("--standalone");
  const errors = validatePackage(DEFAULT_ROOT, { requireCanonicalSource: !standalone });
  if (errors.length) {
    console.error(`FAIL: ${errors.length} Sales S-01/S-04 JIT validation error(s)`);
    for (const error of errors) console.error(`- ${error}`);
    process.exitCode = 1;
    return;
  }
    console.log("PASS: 9 golden fixtures, 72 gapless acceptance cases, 16 owner decisions, 18 implementation gates, strict Markdown/JSON/source authority and canonical AT semantics");
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
