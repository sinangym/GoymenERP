#!/usr/bin/env node

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const DEFAULT_ROOT = resolve(SCRIPT_DIR, "..", "..");

export const PATHS = Object.freeze({
  index: "docs/features/PRODUCT_REMAINING_U03_U04_U05_U06_U10_JIT_INDEX_v1.md",
  service: "docs/features/PRODUCT_U03_U04_SERVICE_JIT_SPEC_v1.md",
  price: "docs/features/PRODUCT_U05_U06_PRICE_LIFECYCLE_SIMULATION_JIT_SPEC_v1.md",
  importSpec: "docs/features/PRODUCT_U10_EXCEL_IMPORT_JIT_SPEC_v1.md",
  acceptance: "docs/acceptance/PRODUCT_REMAINING_U03_U04_U05_U06_U10_ACCEPTANCE_v1.md",
  scenarios: "docs/acceptance/PRODUCT_REMAINING_U03_U04_U05_U06_U10_SCENARIOS_v1.json",
  authority: "docs/acceptance/PRODUCT_REMAINING_JIT_AUTHORITY_v1.json",
  csv: "docs/formulas/PRODUCT_PRICE_SIMULATION_GOLDEN_v1.csv",
  formulaMarkdown: "docs/formulas/PRODUCT_PRICE_SIMULATION_GOLDEN_v1.md",
  manifest: "docs/formulas/PRODUCT_PRICE_SIMULATION_GOLDEN_MANIFEST_v1.json",
  validator: "docs/acceptance/PRODUCT_REMAINING_JIT_VALIDATOR_v1.mjs",
  mutation: "docs/acceptance/PRODUCT_REMAINING_JIT_VALIDATOR_MUTATION_TEST_v1.mjs",
  canonicalAcceptance: "docs/acceptance/ACCEPTANCE_70.md",
  openDecisions: "docs/decisions/ACIK_KARARLAR.md",
  decisionRegistry: "docs/decisions/KARAR_SICILI.md",
  ownerDecisions: "docs/decisions/SINAN_KARARLARI_2026-08-31.md",
  masterReport: "docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md",
  canonicalScreenStatus: "docs/ui/CANONICAL_SCREEN_STATUS.csv",
  screenCatalog: "docs/ui/SCREEN_CATALOG.csv",
  productPdf: "docs/ui/sources/product-service/product-service-design-spec-v1.0.pdf",
});

const INVENTORY_PATHS = Object.freeze([
  PATHS.index,
  PATHS.service,
  PATHS.price,
  PATHS.importSpec,
  PATHS.acceptance,
  PATHS.scenarios,
  PATHS.formulaMarkdown,
  PATHS.csv,
  PATHS.manifest,
  PATHS.validator,
  PATHS.mutation,
]);

const REQUIRED_PATHS = Object.freeze([...INVENTORY_PATHS, PATHS.authority]);

const SOURCE_SNAPSHOT = Object.freeze({
  repository: "sinangym/GoymenERP",
  baseRef: "main",
  baseSha: "80c567a63da8e059ce92aa1b23dad9f4568ad2ca",
  capturedDate: "2026-09-02",
});

const CANONICAL_AT = Object.freeze({
  "AT-001": "Kullanıcı yalnız yetkili şirket, şube ve depo kayıtlarını görür.",
  "AT-003": "Kritik değişiklik önce/sonra, neden, kullanıcı, rol ve korelasyonla audit üretir.",
  "AT-005": "Eşzamanlı değişiklikte ikinci kullanıcıya çatışma ve fark gösterilir.",
  "AT-006": "Başarısız kaydetme kullanıcının form verisini kaybettirmez.",
  "AT-008": "Şirket kapsamı değişikliği açık formu sessizce başka şirkete taşımaz.",
  "AT-018": "Fiyat sürümü para, birim, KDV, kanal ve geçerlilik bağlamını korur.",
  "AT-019": "Excel içe aktarma eşleme/önizleme/onay olmadan kayıt yazmaz.",
  "AT-020": "Aynı Excel dosyasının tekrarı mükerrer kalıcı kayıt üretmez.",
});

const SERIES = Object.freeze(["X", "U03", "U04", "U05", "U06", "U10"]);
const SERIES_COUNTS = Object.freeze({
  X: 10,
  U03: 10,
  U04: 10,
  U05: 10,
  U06: 10,
  U10: 12,
});

const ALLOWED_AT = Object.freeze({
  X: new Set(["AT-001", "AT-003", "AT-005", "AT-006", "AT-008"]),
  U03: new Set(),
  U04: new Set(),
  U05: new Set(["AT-018"]),
  U06: new Set(["AT-018"]),
  U10: new Set(["AT-019", "AT-020"]),
});

const EXPECTED_NONEMPTY_MAP = Object.freeze({
  "CASE-PRD-REM-X-001": ["AT-001"],
  "CASE-PRD-REM-X-002": ["AT-003"],
  "CASE-PRD-REM-X-003": ["AT-005"],
  "CASE-PRD-REM-X-004": ["AT-006"],
  "CASE-PRD-REM-X-005": ["AT-008"],
  "CASE-PRD-REM-U05-001": ["AT-018"],
  "CASE-PRD-REM-U06-004": ["AT-018"],
  "CASE-PRD-REM-U10-001": ["AT-019"],
  "CASE-PRD-REM-U10-005": ["AT-020"],
});

const OWNER_GATES = Object.freeze([
  "SRV-SCOPE-001",
  "PRD-PRICE-001",
  "PRD-IMPORT-001",
  "OPEN-007",
  "G-PRD-REMAIN-BASE-01",
  "G-PRD-REMAIN-PLATFORM-01",
  "G-PRD-REMAIN-CARI-01",
  "G-PRD-REMAIN-PERM-01",
  "G-PRD-REMAIN-DATA-01",
  "G-PRD-REMAIN-AUDIT-01",
  "G-PRD-REMAIN-IDEMPOTENCY-01",
  "G-PRD-REMAIN-CONCURRENCY-01",
  "G-PRD-REMAIN-CALC-01",
  "G-PRD-REMAIN-DOC-01",
  "G-PRD-REMAIN-AI-01",
  "G-PRD-REMAIN-UI-01",
  "G-PRD-REMAIN-CI-01",
]);

const SCREEN_MAPPINGS = Object.freeze({
  "U-03": { name: "Hizmetler liste + detay", canonicalReferenceStatus: "CANDIDATE_EXACT_REFERENCE", sourceMappings: [{ id: "PRODUCT-H-01", status: "CANDIDATE_EXACT" }] },
  "U-04": { name: "Yeni/Düzenle Hizmet", canonicalReferenceStatus: "CANDIDATE_EXACT_REFERENCE", sourceMappings: [{ id: "PRODUCT-H-02", status: "CANDIDATE_EXACT" }, { id: "PRODUCT-H-03", status: "CANDIDATE_MERGE" }] },
  "U-05": { name: "Fiyatlar", canonicalReferenceStatus: "CANDIDATE_EXACT_REFERENCE", sourceMappings: [{ id: "PRODUCT-PRICE-01", status: "CANDIDATE_EXACT" }, { id: "PRODUCT-PRICE-04", status: "CANDIDATE_MERGE" }] },
  "U-06": { name: "Fiyat Simülasyonu / Toplu Güncelleme", canonicalReferenceStatus: "CANDIDATE_EXACT_REFERENCE", sourceMappings: [{ id: "PRODUCT-PRICE-02", status: "CANDIDATE_EXACT" }] },
  "U-10": { name: "Excel İçe Aktarma", canonicalReferenceStatus: "CANDIDATE_EXACT_REFERENCE", sourceMappings: [{ id: "PRODUCT-PRICE-03", status: "CANDIDATE_EXACT" }] },
});

const COLLISION_RULES = Object.freeze([
  "PRODUCT-U-03_IS_CANONICAL_U02_NOT_U03",
  "PRODUCT-U-04_DUPLICATE_COMPARE_UNRESOLVED_U02_R04_NOT_U04",
  "PRODUCT-U-05_IS_CANONICAL_U07_NOT_U05",
  "PRODUCT-U-06_IS_EMBEDDED_S03_P04_NOT_U06",
  "PRODUCT-PRICE-03_IS_PRICE_IMPORT_ONLY_NOT_GENERAL_PRODUCT_IMPORT",
]);

const BINDING_SOURCES = Object.freeze([
  "github-issue:100",
  "github-issue:104",
  "docs/decisions/KARAR_SICILI.md",
  "docs/decisions/SINAN_KARARLARI_2026-08-31.md",
  "docs/decisions/ACIK_KARARLAR.md",
  "docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md",
  "docs/acceptance/ACCEPTANCE_70.md",
  "docs/ui/CANONICAL_SCREEN_STATUS.csv",
  "docs/ui/SCREEN_CATALOG.csv",
  "docs/ui/sources/product-service/product-service-design-spec-v1.0.pdf",
]);

const SOURCE_PINS = Object.freeze([
  { path: "docs/decisions/KARAR_SICILI.md", gitBlobSha: "e46bd3146272cb48d3d4a897e4c4c8751391b852", sha256: "sha256:90f8c75c7c3bab0203b0c51f8602e413158d90be7a2fed2ebbcc708172f7040b" },
  { path: "docs/decisions/SINAN_KARARLARI_2026-08-31.md", gitBlobSha: "f39277202c2aedbce87d6c3ef1b77ffd3564efd0", sha256: "sha256:4cff6ef670f34df408cfdc410fd4a29c59a1da9cdbda5d15d2ff03935af4f8f8" },
  { path: "docs/decisions/ACIK_KARARLAR.md", gitBlobSha: "0135e01d1ae57e4075c9285d0f0de7422ed6191d", sha256: "sha256:f1a1160dea86146728dcc7b55014adfd6a6289207a97d2199f33e9745c1c9eeb" },
  { path: "docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md", gitBlobSha: "54059a5805f415e1c015540f9ef0c9e474c53e98", sha256: "sha256:b636993012e7041802c5a160e48e17427c2ff6e8e5ad5418f41104fdfed0ba05" },
  { path: "docs/acceptance/ACCEPTANCE_70.md", gitBlobSha: "6119f84d2a59b3e0d1fdd2d8646619c0ad29e1bd", sha256: "sha256:50ff92bfc2a9c75b3837f3cbd7741bd2da586bbe02b3bbfbb1b1d24fe8de8e6b" },
  { path: "docs/ui/CANONICAL_SCREEN_STATUS.csv", gitBlobSha: "662cdbc405b49e469c6d578580bef4766d6a4635", sha256: "sha256:7b4a6710f792eea3a1f9d2d4b64fe812686e5555f7b409e2c7c9e512e3369f2c" },
  { path: "docs/ui/SCREEN_CATALOG.csv", gitBlobSha: "4ad764683e7c98a0cfb3077d812f6d8676e55064", sha256: "sha256:6138faa0143678f42c328e25b7697977d24e33139f80c32853b7bf7bdc39eca8" },
  { path: "docs/ui/sources/product-service/product-service-design-spec-v1.0.pdf", gitBlobSha: "d199c68449caa910f3744645300289da6b94bc34", sha256: "sha256:a80be4c47b4ec136105b655628feac847712c7db54e2d1c429d8f2ff671cd1b9" },
]);

const ISSUE_SOURCE_PINS = Object.freeze([
  {
    source: "github-issue:100",
    url: "https://github.com/sinangym/GoymenERP/issues/100",
    title: "EPIC: PRD-CORE-001 — Product & Service master end-to-end delivery",
    updatedAt: "2026-08-30T17:29:55Z",
    bodySha256: "sha256:5427d36c7fe1b1bb67c75157a1e8880d9993d6ade747ed6ff41dd9afe9c7ace1",
  },
  {
    source: "github-issue:104",
    url: "https://github.com/sinangym/GoymenERP/issues/104",
    title: "PLATFORM CONTRACT: group-shared Product master boundary for PRD-SCOPE-001=A",
    updatedAt: "2026-08-30T17:25:10Z",
    bodySha256: "sha256:7aa969ac9aa2e4ade731510523b483da6307f717982ee905612d99d62b8076c5",
  },
]);

const INDEX_MAPPING_ROWS = Object.freeze([
  "| U-03 | Hizmetler liste + detay | `PRODUCT-H-01` | `CANDIDATE_EXACT` | Stoksuz ticari hizmet |",
  "| U-04 | Yeni/Düzenle Hizmet | `PRODUCT-H-02` + `PRODUCT-H-03` | H-02=`CANDIDATE_EXACT`; H-03=`CANDIDATE_MERGE` | Aktivasyon `SRV-SCOPE-001` kapısında |",
  "| U-05 | Fiyatlar | `PRODUCT-PRICE-01` + `PRODUCT-PRICE-04` | PRICE-01=`CANDIDATE_EXACT`; PRICE-04=`CANDIDATE_MERGE` | Liste, zaman çizelgesi, sürüm ve geçerlilik |",
  "| U-06 | Fiyat Simülasyonu / Toplu Güncelleme | `PRODUCT-PRICE-02` | `CANDIDATE_EXACT` | Preview → diff → approval → yeni sürüm |",
  "| U-10 | Excel İçe Aktarma | `PRODUCT-PRICE-03` | `CANDIDATE_EXACT`, kapsam borçlu | Görsel fiyat importuna özeldir; genel Product importu sayılmaz |",
]);

const CASE_SEMANTIC_SHA256 = "sha256:36cb2024c514ce1910e69922f98ca29f30842272ece2d2f5eb44b67bea12184c";

// These pins are compiled into the executable validator as an independent
// trust anchor. Updating artifactInventory alone therefore cannot launder a
// semantic rewrite of the human-readable contract.
const MARKDOWN_SEMANTIC_PINS = Object.freeze({
  [PATHS.index]: "sha256:cc7340c2aae6858be24cd38c0e1cd77aade96d4bb4c64105d74082f15c957f33",
  [PATHS.service]: "sha256:ceadb35ab77d12cfbede50f72395957a6b5b5183a57aa13b065ecf79a194866c",
  [PATHS.price]: "sha256:53f797d857821b5cf6746feb021348644415177fcf565df72617957a56c45f04",
  [PATHS.importSpec]: "sha256:cf0e472ba51364a81a83af5ff2df6314095fe52449f3575f784a3bbe9aeba6e3",
  [PATHS.acceptance]: "sha256:901a362c424872e03a07aad445eb5ac7994a404ab3a8f8e810bc291402310442",
  [PATHS.formulaMarkdown]: "sha256:654f91bc68d30721d5d8754f3c2d90d3ee951edacf1e88ab53c43553d25d3fc6",
});

const PILOT_ROLE_POLICY = Object.freeze({
  source: "github-issue:100#PRD-PERM-001=A",
  productServiceMasterReadInitialWriteRole: "SYSTEM_ADMINISTRATOR",
  u10PilotApplyRole: "SYSTEM_ADMINISTRATOR",
  allOtherRoles: "DEFAULT_DENY",
  priceReadManageSimulateApprove: "OUT_OF_SCOPE_DEFAULT_DENY",
  exactPermissionStrings: null,
  permissionGate: "G-PRD-REMAIN-PERM-01",
  permissionGateStatus: "OPEN",
  supersedes: "DEC-PRD-010_STOCK_PLANNING_IMPORT_APPROVER_NOTE_FOR_PILOT",
});

const OPEN_DECISION_PINS = Object.freeze({
  "OPEN-007": "Formül sahibi, kesin yoğunluk/hız/kerf/kaplama/yuvarlama/tolerans değerleri",
});

const PROMOTION_DESCRIPTOR = Object.freeze({
  descriptorVersion: "product-price-simulation-promotion/v1",
  status: "BLOCKED",
  promotionReady: false,
  scope: "SYNTHETIC_PREVIEW_DELTA_PERCENT_ONLY",
  requiredPins: {
    priceCalculationPolicyVersion: null,
    roundingPolicyVersion: null,
    taxPolicyVersion: null,
    fxPolicyVersion: null,
    permissionManifestVersion: null,
  },
  blockers: [
    "PRD-PRICE-001",
    "OPEN-007",
    "G-PRD-REMAIN-CALC-01",
    "G-PRD-REMAIN-PERM-01",
    "G-PRD-REMAIN-CI-01",
  ],
  forbiddenClaims: [
    "PRODUCTION_PRICE",
    "PRODUCTION_ROUNDING",
    "TAX_RESULT",
    "FX_RESULT",
    "PERMISSION_APPROVAL",
  ],
});

const EXPECTED_CSV_HEADERS = Object.freeze([
  "FixtureId",
  "Operation",
  "OldAmount",
  "NewAmount",
  "ExpectedDelta",
  "ExpectedPercentExact",
  "CurrencyContext",
  "UnitContext",
  "RoundingPolicyVersion",
  "TaxPolicyVersion",
  "FxPolicyVersion",
  "PermissionManifestVersion",
  "PromotionStatus",
]);

const EXPECTED_FIXTURES = Object.freeze([
  ["PRD-PRICE-SIM-001", "100", "110", "10", "10"],
  ["PRD-PRICE-SIM-002", "250", "237.5", "-12.5", "-5"],
  ["PRD-PRICE-SIM-003", "0", "10", "10", "UNDEFINED"],
  ["PRD-PRICE-SIM-004", "99.99", "99.99", "0", "0"],
  ["PRD-PRICE-SIM-005", "1200", "1260", "60", "5"],
  ["PRD-PRICE-SIM-006", "0.125", "0.150", "0.025", "20"],
  ["PRD-PRICE-SIM-007", "1000000", "999999.99", "-0.01", "-0.000001"],
  ["PRD-PRICE-SIM-008", "7.5", "8.25", "0.75", "10"],
]);

function stableJson(value) {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(stableJson).join(",")}]`;
  return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`).join(",")}}`;
}

function assertExactKeys(value, expectedKeys, label) {
  assert.ok(value && typeof value === "object" && !Array.isArray(value), `${label}: expected object`);
  assert.deepEqual(Object.keys(value).sort(), [...expectedKeys].sort(), `${label}: key schema drift`);
}

function validateSnapshot(value, label) {
  assertExactKeys(value, ["repository", "baseRef", "baseSha", "capturedDate"], label);
  assert.equal(stableJson(value), stableJson(SOURCE_SNAPSHOT), `${label}: source snapshot mismatch`);
}

function validatePromotionDescriptor(value, label) {
  assertExactKeys(value, ["descriptorVersion", "status", "promotionReady", "scope", "requiredPins", "blockers", "forbiddenClaims"], label);
  assertExactKeys(value.requiredPins, ["priceCalculationPolicyVersion", "roundingPolicyVersion", "taxPolicyVersion", "fxPolicyVersion", "permissionManifestVersion"], `${label}.requiredPins`);
  assert.equal(stableJson(value), stableJson(PROMOTION_DESCRIPTOR), `${label}: descriptor changed`);
}

function assertNoDuplicateJsonKeys(text, label) {
  let index = 0;
  const fail = (message) => {
    throw new Error(`${label}: ${message} at byte ${index}`);
  };
  const whitespace = () => {
    while (/\s/u.test(text[index] ?? "")) index += 1;
  };
  const parseString = () => {
    whitespace();
    if (text[index] !== '"') fail("expected JSON string");
    const start = index;
    index += 1;
    while (index < text.length) {
      if (text[index] === "\\") {
        index += 2;
        continue;
      }
      if (text[index] === '"') {
        index += 1;
        return JSON.parse(text.slice(start, index));
      }
      index += 1;
    }
    fail("unterminated JSON string");
  };
  const parseValue = (path) => {
    whitespace();
    if (text[index] === "{") {
      index += 1;
      whitespace();
      const keys = new Set();
      if (text[index] === "}") {
        index += 1;
        return;
      }
      while (index < text.length) {
        const key = parseString();
        if (keys.has(key)) fail(`duplicate key ${path}.${key}`);
        keys.add(key);
        whitespace();
        if (text[index] !== ":") fail("expected colon");
        index += 1;
        parseValue(`${path}.${key}`);
        whitespace();
        if (text[index] === "}") {
          index += 1;
          return;
        }
        if (text[index] !== ",") fail("expected object delimiter");
        index += 1;
      }
      fail("unterminated object");
    }
    if (text[index] === "[") {
      index += 1;
      whitespace();
      let itemIndex = 0;
      if (text[index] === "]") {
        index += 1;
        return;
      }
      while (index < text.length) {
        parseValue(`${path}[${itemIndex}]`);
        itemIndex += 1;
        whitespace();
        if (text[index] === "]") {
          index += 1;
          return;
        }
        if (text[index] !== ",") fail("expected array delimiter");
        index += 1;
      }
      fail("unterminated array");
    }
    if (text[index] === '"') {
      parseString();
      return;
    }
    const primitive = text.slice(index).match(/^(?:-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null)/u);
    if (!primitive) fail("invalid JSON value");
    index += primitive[0].length;
  };
  parseValue("$");
  whitespace();
  if (index !== text.length) fail("trailing JSON content");
}

function sha256(text) {
  return `sha256:${createHash("sha256").update(text, "utf8").digest("hex")}`;
}

function gitBlobSha(buffer) {
  const header = Buffer.from(`blob ${buffer.length}\0`, "utf8");
  return createHash("sha1").update(header).update(buffer).digest("hex");
}

function file(root, relativePath) {
  const absolutePath = resolve(root, relativePath);
  assert.ok(existsSync(absolutePath), `missing required file: ${relativePath}`);
  return readFileSync(absolutePath, "utf8");
}

export function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = "";
  let quoted = false;
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (quoted) {
      if (char === '"' && text[index + 1] === '"') {
        field += '"';
        index += 1;
      } else if (char === '"') {
        quoted = false;
      } else {
        field += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === ",") {
      row.push(field);
      field = "";
    } else if (char === "\n") {
      row.push(field.replace(/\r$/, ""));
      if (row.some((cell) => cell !== "")) rows.push(row);
      row = [];
      field = "";
    } else {
      field += char;
    }
  }
  assert.equal(quoted, false, "CSV contains unterminated quote");
  if (field !== "" || row.length > 0) {
    row.push(field.replace(/\r$/, ""));
    if (row.some((cell) => cell !== "")) rows.push(row);
  }
  assert.ok(rows.length > 1, "CSV must contain headers and records");
  const headers = rows[0];
  const records = rows.slice(1).map((cells, index) => {
    assert.equal(cells.length, headers.length, `CSV row ${index + 2} width mismatch`);
    return Object.fromEntries(headers.map((header, cellIndex) => [header, cells[cellIndex]]));
  });
  return { headers, records };
}

function gcd(a, b) {
  a = a < 0n ? -a : a;
  b = b < 0n ? -b : b;
  while (b !== 0n) [a, b] = [b, a % b];
  return a;
}

class Fraction {
  constructor(n, d = 1n) {
    assert.notEqual(d, 0n, "zero denominator");
    if (d < 0n) { n = -n; d = -d; }
    const divisor = gcd(n, d);
    this.n = n / divisor;
    this.d = d / divisor;
  }

  static parse(raw) {
    const text = String(raw);
    assert.match(text, /^-?\d+(?:\.\d+)?$/, `invalid base-10 decimal: ${text}`);
    const negative = text.startsWith("-");
    const unsigned = negative ? text.slice(1) : text;
    const [whole, decimals = ""] = unsigned.split(".");
    const denominator = 10n ** BigInt(decimals.length);
    const numerator = BigInt(`${whole}${decimals}`) * (negative ? -1n : 1n);
    return new Fraction(numerator, denominator);
  }

  sub(other) { return new Fraction(this.n * other.d - other.n * this.d, this.d * other.d); }
  mul(other) { return new Fraction(this.n * other.n, this.d * other.d); }
  div(other) { return new Fraction(this.n * other.d, this.d * other.n); }
  equals(other) { return this.n === other.n && this.d === other.d; }

  decimalString() {
    let scaled = this.n;
    let denominator = this.d;
    let twos = 0;
    let fives = 0;
    while (denominator % 2n === 0n) { denominator /= 2n; twos += 1; }
    while (denominator % 5n === 0n) { denominator /= 5n; fives += 1; }
    assert.equal(denominator, 1n, "fraction has non-terminating decimal");
    const places = Math.max(twos, fives);
    scaled *= 2n ** BigInt(places - fives);
    scaled *= 5n ** BigInt(places - twos);
    const negative = scaled < 0n;
    let digits = (negative ? -scaled : scaled).toString().padStart(places + 1, "0");
    const whole = places === 0 ? digits : digits.slice(0, -places);
    let decimals = places === 0 ? "" : digits.slice(-places).replace(/0+$/, "");
    return `${negative ? "-" : ""}${whole}${decimals ? `.${decimals}` : ""}`;
  }
}

function exactCaseMap() {
  const mapping = {};
  for (const series of SERIES) {
    for (let ordinal = 1; ordinal <= SERIES_COUNTS[series]; ordinal += 1) {
      const id = `CASE-PRD-REM-${series}-${String(ordinal).padStart(3, "0")}`;
      mapping[id] = EXPECTED_NONEMPTY_MAP[id] ?? [];
    }
  }
  return mapping;
}

function assertNoImplementationInvention(markdownBundle, scenarios) {
  const scanned = `${markdownBundle}\n${JSON.stringify(scenarios)}`;
  const forbiddenPatterns = [
    { pattern: /\b(?:GET|POST|PUT|PATCH|DELETE)\s+[`"']?\/(?:api(?:\/|\b)|v\d+\/)[A-Za-z0-9_{}?&=./:-]*/iu, label: "exact API route" },
    { pattern: /\bendpoint\s*:\s*[`"']?\/(?:api(?:\/|\b)|v\d+\/)[A-Za-z0-9_{}?&=./:-]*/iu, label: "exact endpoint route" },
    { pattern: /\bCREATE\s+TABLE\b|\bALTER\s+TABLE\b/iu, label: "database DDL" },
    { pattern: /\bPermission\.[A-Za-z0-9_.-]+\b|\b(?:Product|Service|Price|Import)\.[A-Z][A-Za-z0-9_.-]*\b/u, label: "exact permission identifier" },
    { pattern: /\bpermission(?:\s+(?:key|id|string))?\s*[:=]\s*[`"']?[a-z][a-z0-9_.:-]*\b/iu, label: "exact permission key" },
    { pattern: /\bROLE_[A-Z0-9_]+\b/u, label: "invented role identifier" },
    { pattern: /\b(?:HALF_UP|HALF_DOWN|HALF_EVEN|TIES_TO_EVEN|AWAY_FROM_ZERO|BANKERS|CEILING|ROUND_DOWN)\b/u, label: "rounding mode invention" },
    { pattern: /\b(?:production\s+)?(?:rounding|yuvarlama)(?:\s+(?:mode|modu))?\s*[:=]\s*(?!UNRESOLVED\b|null\b)[A-Za-z][A-Za-z0-9_-]*\b/iu, label: "rounding policy invention" },
    { pattern: /\b(?:KDV|tax|vergi)\s*(?:oran[ıi]?\s*)?[:=]?\s*%?\s*\d+(?:[.,]\d+)?\b/iu, label: "tax rate invention" },
    { pattern: /\b(?:TCMB|ECB|Refinitiv|Reuters)\b/iu, label: "FX source invention" },
    { pattern: /\b(?:production\s+)?FX\s+(?:source|kayna[ğg][ıi]?)\s*[:=]\s*(?!UNRESOLVED\b|null\b)[A-Za-z][A-Za-z0-9_.-]*\b/iu, label: "FX source policy invention" },
    { pattern: /\b(?:persistence|database|veritaban[ıi])\s+(?:table|tablosu?)\s*[:=]\s*[A-Za-z][A-Za-z0-9_]*\b/iu, label: "persistence table invention" },
    { pattern: /\bSCHEMA_GO\b|\bAPI_GO\b|\bUI_GO\b/u, label: "implementation GO claim" },
  ];
  for (const { pattern, label } of forbiddenPatterns) {
    assert.doesNotMatch(scanned, pattern, `forbidden ${label}`);
  }
  for (const item of scenarios.cases) {
    assert.equal("permissionId" in item, false, `${item.id}: exact permission invention`);
    assert.equal("permissionKey" in item, false, `${item.id}: exact permission invention`);
    assert.equal("apiPath" in item, false, `${item.id}: exact API invention`);
    assert.equal("endpoint" in item, false, `${item.id}: endpoint invention`);
    assert.equal("dtoName" in item, false, `${item.id}: DTO invention`);
    assert.equal("tableName" in item, false, `${item.id}: table invention`);
    assert.equal("roundingMode" in item, false, `${item.id}: rounding invention`);
    assert.equal("taxRate" in item, false, `${item.id}: tax invention`);
  }
}

function validateMarkdown(markdowns) {
  const bundle = Object.values(markdowns).join("\n");
  const requiredTokens = [
    "PREPARATION_READY_IMPLEMENTATION_NO_GO",
    "SRV-SCOPE-001",
    "PRD-PRICE-001",
    "PRD-IMPORT-001",
    "OPEN-007",
    "G-PRD-REMAIN-CI-01",
    "PRODUCT-H-01",
    "PRODUCT-H-02",
    "PRODUCT-H-03",
    "PRODUCT-PRICE-01",
    "PRODUCT-PRICE-02",
    "PRODUCT-PRICE-03",
    "PRODUCT-PRICE-04",
    "CANDIDATE_MERGE",
    "SYSTEM_ADMINISTRATOR",
    "DEFAULT_DENY",
    "implementation GO",
    "nonzero",
  ];
  for (const token of requiredTokens) {
    assert.ok(bundle.includes(token), `markdown contract missing token: ${token}`);
  }
  for (const row of INDEX_MAPPING_ROWS) {
    assert.ok(markdowns.index.includes(row), `index mapping row drift: ${row.split("|")[1].trim()}`);
  }
  assert.match(markdowns.service, /stoksuz ticari kayıt/iu);
  assert.match(markdowns.service, /lot, etiket, paket, depo, rezervasyon/iu);
  assert.match(markdowns.price, /Draft → Approved → Scheduled → Active → Expired/u);
  assert.match(markdowns.price, /preview eski-yeni|eski-yeni.{0,40}diff/iu);
  assert.match(markdowns.importSpec, /mapping → preview → validation\/error → duplicate → approval → write/iu);
  assert.match(markdowns.importSpec, /business kayıt yazımı değildir/iu);
  assert.match(markdowns.importSpec, /spreadsheet[- ]formula injection/iu);
  assert.match(markdowns.index, /62 gapless, makine-okur vaka/iu);
  assert.match(markdowns.price, /Price read\/manage\/simulate\/approve.{0,160}default-deny/isu);
  assert.match(markdowns.index, /PRD-PERM-001=A.{0,240}supersede/isu);
  assert.match(markdowns.index, /PRODUCT-U-03.*canonical U-02/iu);
  assert.match(markdowns.index, /PRODUCT-U-04.*U-02\/R-04/iu);
  assert.match(markdowns.index, /PRODUCT-U-05.*canonical U-07/iu);
  assert.match(markdowns.index, /PRODUCT-U-06.*S-03\/P-04/iu);
  assert.match(markdowns.index, /PRODUCT-PRICE-03.*genel Product importu/iu);
  for (const [atId, exactText] of Object.entries(CANONICAL_AT)) {
    assert.ok(markdowns.acceptance.includes(`${atId} | ${exactText}`), `acceptance missing exact ${atId} text`);
  }
}

function validateScenarios(scenarios, authority) {
  assertExactKeys(scenarios, ["schemaVersion", "sourceSnapshot", "decision", "caseCount", "seriesCounts", "cases"], "scenarios");
  assert.equal(scenarios.schemaVersion, "product-remaining-scenarios/v1");
  validateSnapshot(scenarios.sourceSnapshot, "scenarios.sourceSnapshot");
  assert.equal(scenarios.decision, "PREPARATION_READY_IMPLEMENTATION_NO_GO");
  assert.equal(scenarios.caseCount, 62);
  assertExactKeys(scenarios.seriesCounts, SERIES, "scenarios.seriesCounts");
  assert.equal(stableJson(scenarios.seriesCounts), stableJson(SERIES_COUNTS), "series count descriptor mismatch");
  assert.equal(scenarios.cases.length, 62);

  const seen = new Set();
  const actualMap = {};
  for (const series of SERIES) {
    const cases = scenarios.cases.filter((item) => item.screen === series);
    assert.equal(cases.length, SERIES_COUNTS[series], `${series}: case count mismatch`);
    for (let index = 0; index < cases.length; index += 1) {
      const item = cases[index];
      assertExactKeys(item, ["id", "screen", "kind", "title", "expected", "canonicalAT", "ownerGate", "implementationDecision"], item.id ?? `${series}[${index}]`);
      const expectedId = `CASE-PRD-REM-${series}-${String(index + 1).padStart(3, "0")}`;
      assert.equal(item.id, expectedId, `${series}: IDs must be gapless and ordered`);
      assert.equal(seen.has(item.id), false, `duplicate case: ${item.id}`);
      seen.add(item.id);
      assert.ok(["positive-or-boundary", "negative-or-resilience"].includes(item.kind), `${item.id}: invalid kind`);
      assert.ok(typeof item.title === "string" && item.title.length >= 20, `${item.id}: title too short`);
      assert.ok(typeof item.expected === "string" && item.expected.length >= 30, `${item.id}: expected too short`);
      assert.ok(OWNER_GATES.includes(item.ownerGate), `${item.id}: unknown owner gate`);
      assert.equal(item.implementationDecision, "NO_GO", `${item.id}: implementation decision must be NO_GO`);
      assert.ok(Array.isArray(item.canonicalAT), `${item.id}: canonicalAT must be array`);
      assert.equal(new Set(item.canonicalAT).size, item.canonicalAT.length, `${item.id}: duplicate AT`);
      for (const atId of item.canonicalAT) {
        assert.ok(ALLOWED_AT[series].has(atId), `${item.id}: semantically invalid ${atId}`);
      }
      actualMap[item.id] = item.canonicalAT;
    }
  }

  const expectedMap = exactCaseMap();
  assert.equal(stableJson(actualMap), stableJson(expectedMap), "exact case→canonical AT mapping changed");
  assert.equal(authority.scenarioContract.caseCount, 62);
  assert.equal(stableJson(authority.scenarioContract.seriesCounts), stableJson(SERIES_COUNTS), "authority series counts mismatch");
  assert.equal(stableJson(authority.scenarioContract.caseToCanonicalAT), stableJson(expectedMap), "authority case→AT pin mismatch");
  const semanticDigest = sha256(stableJson(scenarios.cases));
  assert.equal(semanticDigest, CASE_SEMANTIC_SHA256, "scenario semantic digest changed");
  assert.equal(authority.scenarioContract.caseSemanticSha256, CASE_SEMANTIC_SHA256, "authority scenario semantic pin mismatch");
}

function validateGolden(csvText, manifest, authority) {
  const parsed = parseCsv(csvText);
  assert.deepEqual(parsed.headers, EXPECTED_CSV_HEADERS, "CSV headers changed");
  assert.equal(parsed.records.length, EXPECTED_FIXTURES.length);
  assertExactKeys(manifest, ["schemaVersion", "sourceSnapshot", "decision", "csvPath", "fixtureCount", "operation", "arithmetic", "exclusions", "promotionDescriptor", "fixtures"], "manifest");
  assert.equal(manifest.schemaVersion, "product-price-simulation-golden/v1");
  validateSnapshot(manifest.sourceSnapshot, "manifest.sourceSnapshot");
  assert.equal(manifest.decision, "PREPARATION_READY_IMPLEMENTATION_NO_GO");
  assert.equal(manifest.csvPath, PATHS.csv, "manifest CSV path drift");
  assert.equal(manifest.fixtureCount, EXPECTED_FIXTURES.length);
  assert.equal(manifest.operation, "PREVIEW_DELTA_PERCENT_ONLY");
  assert.equal(manifest.arithmetic, "exact-base10-rational");
  assert.deepEqual(manifest.exclusions, ["tax", "fx-conversion", "rounding", "discount-composition", "tier-selection", "permission-decision", "persistence"]);
  validatePromotionDescriptor(manifest.promotionDescriptor, "manifest.promotionDescriptor");
  validatePromotionDescriptor(authority.promotionDescriptor, "authority.promotionDescriptor");
  assert.equal(manifest.fixtures.length, EXPECTED_FIXTURES.length);

  for (let index = 0; index < EXPECTED_FIXTURES.length; index += 1) {
    const [id, oldAmount, newAmount, expectedDelta, expectedPercent] = EXPECTED_FIXTURES[index];
    const row = parsed.records[index];
    const fixture = manifest.fixtures[index];
    assertExactKeys(fixture, ["fixtureId", "operation", "oldAmount", "newAmount", "expectedDelta", "expectedPercentExact", "currencyContext", "unitContext", "roundingPolicyVersion", "taxPolicyVersion", "fxPolicyVersion", "permissionManifestVersion", "promotionStatus"], `manifest.fixtures[${index}]`);
    assert.equal(row.FixtureId, id);
    assert.equal(row.OldAmount, oldAmount);
    assert.equal(row.NewAmount, newAmount);
    assert.equal(row.ExpectedDelta, expectedDelta);
    assert.equal(row.ExpectedPercentExact, expectedPercent);
    assert.equal(row.Operation, "PREVIEW_DELTA_PERCENT_ONLY");
    assert.equal(row.CurrencyContext, "SYNTHETIC-CURRENCY");
    assert.equal(row.UnitContext, "SYNTHETIC-PRICE-UNIT");
    for (const policyField of ["RoundingPolicyVersion", "TaxPolicyVersion", "FxPolicyVersion", "PermissionManifestVersion"]) {
      assert.equal(row[policyField], "UNRESOLVED", `${id}: ${policyField} must remain unresolved`);
    }
    assert.equal(row.PromotionStatus, "BLOCKED");

    const rowFixture = {
      fixtureId: row.FixtureId,
      operation: row.Operation,
      oldAmount: row.OldAmount,
      newAmount: row.NewAmount,
      expectedDelta: row.ExpectedDelta,
      expectedPercentExact: row.ExpectedPercentExact,
      currencyContext: row.CurrencyContext,
      unitContext: row.UnitContext,
      roundingPolicyVersion: row.RoundingPolicyVersion,
      taxPolicyVersion: row.TaxPolicyVersion,
      fxPolicyVersion: row.FxPolicyVersion,
      permissionManifestVersion: row.PermissionManifestVersion,
      promotionStatus: row.PromotionStatus,
    };
    assert.equal(stableJson(fixture), stableJson(rowFixture), `${id}: CSV/manifest parity mismatch`);

    const oldValue = Fraction.parse(oldAmount);
    const newValue = Fraction.parse(newAmount);
    const delta = newValue.sub(oldValue);
    assert.ok(delta.equals(Fraction.parse(expectedDelta)), `${id}: delta arithmetic mismatch`);
    assert.equal(delta.decimalString(), Fraction.parse(expectedDelta).decimalString(), `${id}: noncanonical delta`);
    if (oldValue.n === 0n) {
      assert.equal(expectedPercent, "UNDEFINED", `${id}: zero denominator must be UNDEFINED`);
    } else {
      const percent = delta.div(oldValue).mul(Fraction.parse("100"));
      assert.ok(percent.equals(Fraction.parse(expectedPercent)), `${id}: percent arithmetic mismatch`);
    }
  }
}

function validateAuthority(authority, texts) {
  assertExactKeys(authority, [
    "schemaVersion",
    "sourceSnapshot",
    "decision",
    "implementationDecision",
    "productCore181AssumedClosed",
    "canonicalAcceptance",
    "sourceDescriptor",
    "pilotRolePolicy",
    "ownerGates",
    "artifactInventory",
    "markdownContract",
    "scenarioContract",
    "promotionDescriptor",
    "requiredCommands",
    "sharedCiWiring",
    "forbiddenImplementationClaims",
  ], "authority");
  assert.equal(authority.schemaVersion, "product-remaining-jit-authority/v1");
  validateSnapshot(authority.sourceSnapshot, "authority.sourceSnapshot");
  assert.equal(authority.decision, "PREPARATION_READY_IMPLEMENTATION_NO_GO");
  assert.equal(authority.implementationDecision, "NO_GO");
  assert.equal(authority.productCore181AssumedClosed, false, "#181 must not be assumed closed");
  assertExactKeys(authority.canonicalAcceptance, ["path", "exactTexts"], "authority.canonicalAcceptance");
  assertExactKeys(authority.canonicalAcceptance.exactTexts, Object.keys(CANONICAL_AT), "authority.canonicalAcceptance.exactTexts");
  assert.equal(authority.canonicalAcceptance.path, PATHS.canonicalAcceptance);
  assert.equal(stableJson(authority.canonicalAcceptance.exactTexts), stableJson(CANONICAL_AT), "canonical AT descriptor mismatch");

  assertExactKeys(authority.sourceDescriptor, ["screenMappings", "collisionRules", "bindingSources", "sourcePins", "issuePins", "openDecisionPins"], "authority.sourceDescriptor");
  assertExactKeys(authority.sourceDescriptor.screenMappings, Object.keys(SCREEN_MAPPINGS), "authority.sourceDescriptor.screenMappings");
  assert.equal(stableJson(authority.sourceDescriptor.screenMappings), stableJson(SCREEN_MAPPINGS), "screen mapping descriptor mismatch");
  assert.equal(stableJson(authority.sourceDescriptor.collisionRules), stableJson(COLLISION_RULES), "source collision rules changed");
  assert.equal(stableJson(authority.sourceDescriptor.bindingSources), stableJson(BINDING_SOURCES), "binding source descriptor changed");
  assert.equal(stableJson(authority.sourceDescriptor.sourcePins), stableJson(SOURCE_PINS), "source pin descriptor changed");
  assert.equal(stableJson(authority.sourceDescriptor.issuePins), stableJson(ISSUE_SOURCE_PINS), "issue snapshot pin descriptor changed");
  const fileBindings = authority.sourceDescriptor.bindingSources.filter((source) => !source.startsWith("github-issue:"));
  const issueBindings = authority.sourceDescriptor.bindingSources.filter((source) => source.startsWith("github-issue:"));
  assert.deepEqual(fileBindings, SOURCE_PINS.map((pin) => pin.path), "file binding/source pin parity mismatch");
  assert.deepEqual(issueBindings, ISSUE_SOURCE_PINS.map((pin) => pin.source), "issue binding/snapshot pin parity mismatch");
  assert.equal(stableJson(authority.sourceDescriptor.openDecisionPins), stableJson(OPEN_DECISION_PINS), "open decision pin changed");
  for (const [index, pin] of authority.sourceDescriptor.sourcePins.entries()) {
    assertExactKeys(pin, ["path", "gitBlobSha", "sha256"], `authority.sourceDescriptor.sourcePins[${index}]`);
    assert.match(pin.gitBlobSha, /^[0-9a-f]{40}$/u);
    assert.match(pin.sha256, /^sha256:[0-9a-f]{64}$/u);
  }
  for (const [index, pin] of authority.sourceDescriptor.issuePins.entries()) {
    assertExactKeys(pin, ["source", "url", "title", "updatedAt", "bodySha256"], `authority.sourceDescriptor.issuePins[${index}]`);
    assert.match(pin.bodySha256, /^sha256:[0-9a-f]{64}$/u);
  }

  assertExactKeys(authority.pilotRolePolicy, ["source", "productServiceMasterReadInitialWriteRole", "u10PilotApplyRole", "allOtherRoles", "priceReadManageSimulateApprove", "exactPermissionStrings", "permissionGate", "permissionGateStatus", "supersedes"], "authority.pilotRolePolicy");
  assert.equal(stableJson(authority.pilotRolePolicy), stableJson(PILOT_ROLE_POLICY), "pilot role policy drift");

  assert.equal(authority.ownerGates.length, OWNER_GATES.length);
  assert.deepEqual(authority.ownerGates.map((gate) => gate.id), OWNER_GATES, "owner gate order/set changed");
  for (const gate of authority.ownerGates) {
    assertExactKeys(gate, ["id", "status", "closureEvidence"], `authority.ownerGates.${gate.id ?? "unknown"}`);
    assert.equal(gate.status, "OPEN", `${gate.id}: gate must remain OPEN`);
    assert.equal(gate.closureEvidence, null, `${gate.id}: unproved closure evidence`);
  }

  assert.equal(authority.artifactInventory.length, INVENTORY_PATHS.length);
  assert.deepEqual(authority.artifactInventory.map((item) => item.path), INVENTORY_PATHS, "artifact inventory path/order changed");
  for (const item of authority.artifactInventory) {
    assertExactKeys(item, ["path", "sha256"], `authority.artifactInventory.${item.path ?? "unknown"}`);
    assert.match(item.sha256, /^sha256:[0-9a-f]{64}$/, `${item.path}: invalid digest`);
    assert.equal(item.sha256, sha256(texts[item.path]), `${item.path}: digest mismatch`);
  }

  assertExactKeys(authority.markdownContract, ["algorithm", "pins"], "authority.markdownContract");
  assert.equal(authority.markdownContract.algorithm, "sha256/raw-utf8");
  assertExactKeys(authority.markdownContract.pins, Object.keys(MARKDOWN_SEMANTIC_PINS), "authority.markdownContract.pins");
  assert.equal(stableJson(authority.markdownContract.pins), stableJson(MARKDOWN_SEMANTIC_PINS), "authority markdown semantic descriptor changed");
  for (const [path, expectedDigest] of Object.entries(MARKDOWN_SEMANTIC_PINS)) {
    assert.equal(sha256(texts[path]), expectedDigest, `${path}: compiled markdown semantic pin mismatch`);
  }

  assertExactKeys(authority.scenarioContract, ["caseCount", "seriesCounts", "caseToCanonicalAT", "caseSemanticSha256"], "authority.scenarioContract");
  assertExactKeys(authority.scenarioContract.seriesCounts, SERIES, "authority.scenarioContract.seriesCounts");
  assertExactKeys(authority.scenarioContract.caseToCanonicalAT, Object.keys(exactCaseMap()), "authority.scenarioContract.caseToCanonicalAT");

  validatePromotionDescriptor(authority.promotionDescriptor, "authority.promotionDescriptor");

  assert.deepEqual(authority.requiredCommands, [
    "node docs/acceptance/PRODUCT_REMAINING_JIT_VALIDATOR_v1.mjs --standalone",
    "node docs/acceptance/PRODUCT_REMAINING_JIT_VALIDATOR_v1.mjs",
    "node --test docs/acceptance/PRODUCT_REMAINING_JIT_VALIDATOR_MUTATION_TEST_v1.mjs",
  ]);
  assertExactKeys(authority.sharedCiWiring, ["gate", "status", "visibleRunnerStepRequired", "nonzeroFailurePropagationRequired", "githubWorkflowEditedByThisPackage"], "authority.sharedCiWiring");
  assert.equal(authority.sharedCiWiring.gate, "G-PRD-REMAIN-CI-01");
  assert.equal(authority.sharedCiWiring.status, "OPEN");
  assert.equal(authority.sharedCiWiring.visibleRunnerStepRequired, true);
  assert.equal(authority.sharedCiWiring.nonzeroFailurePropagationRequired, true);
  assert.equal(authority.sharedCiWiring.githubWorkflowEditedByThisPackage, false);
  assert.deepEqual(authority.forbiddenImplementationClaims, [
    "SCHEMA_GO",
    "API_GO",
    "UI_GO",
    "PRODUCTION_PRICE",
    "PRODUCTION_IMPORT",
    "PRODUCT_CORE_181_PROMOTED",
  ]);
}

function validateCanonicalSources(root) {
  for (const pin of SOURCE_PINS) {
    const absolutePath = resolve(root, pin.path);
    assert.ok(existsSync(absolutePath), `canonical source missing: ${pin.path}`);
    const bytes = readFileSync(absolutePath);
    assert.equal(sha256(bytes), pin.sha256, `canonical SHA-256 drift: ${pin.path}`);
    assert.equal(gitBlobSha(bytes), pin.gitBlobSha, `canonical git blob drift: ${pin.path}`);
  }

  const canonicalText = file(root, PATHS.canonicalAcceptance);
  for (const [atId, exactText] of Object.entries(CANONICAL_AT)) {
    assert.ok(canonicalText.includes(`**${atId}:** ${exactText}`), `canonical source drift: ${atId}`);
  }

  const statusText = file(root, PATHS.canonicalScreenStatus);
  for (const [canonicalId, descriptor] of Object.entries(SCREEN_MAPPINGS)) {
    const line = statusText.split(/\r?\n/u).find((item) => item.startsWith(`${canonicalId},`));
    assert.ok(line, `canonical screen status missing: ${canonicalId}`);
    assert.ok(line.includes(descriptor.canonicalReferenceStatus), `canonical reference status drift: ${canonicalId}`);
    for (const source of descriptor.sourceMappings) {
      assert.ok(line.includes(source.id), `canonical screen source drift: ${canonicalId}/${source.id}`);
    }
  }

  const catalogText = file(root, PATHS.screenCatalog);
  for (const [canonicalId, descriptor] of Object.entries(SCREEN_MAPPINGS)) {
    for (const source of descriptor.sourceMappings) {
      const line = catalogText.split(/\r?\n/u).find((item) => item.startsWith(`${source.id},`));
      assert.ok(line, `screen catalog source missing: ${source.id}`);
      assert.ok(line.includes(`[""${canonicalId}""]`), `screen catalog canonical mapping drift: ${source.id}`);
      assert.ok(line.includes(`,${source.status},`), `screen catalog source status drift: ${source.id}`);
    }
  }

  const openDecisionText = file(root, PATHS.openDecisions);
  for (const [decisionId, exactText] of Object.entries(OPEN_DECISION_PINS)) {
    assert.ok(openDecisionText.includes(`| ${decisionId} | ${exactText} |`), `open decision source drift: ${decisionId}`);
  }
}

export function validateRoot(root = DEFAULT_ROOT, options = {}) {
  const standalone = options.standalone ?? false;
  for (const relativePath of REQUIRED_PATHS) {
    assert.ok(existsSync(resolve(root, relativePath)), `missing required file: ${relativePath}`);
  }

  const texts = Object.fromEntries(INVENTORY_PATHS.map((relativePath) => [relativePath, file(root, relativePath)]));
  const markdowns = {
    index: texts[PATHS.index],
    service: texts[PATHS.service],
    price: texts[PATHS.price],
    importSpec: texts[PATHS.importSpec],
    acceptance: texts[PATHS.acceptance],
    formulaMarkdown: texts[PATHS.formulaMarkdown],
  };
  const authorityRaw = file(root, PATHS.authority);
  assertNoDuplicateJsonKeys(texts[PATHS.scenarios], PATHS.scenarios);
  assertNoDuplicateJsonKeys(texts[PATHS.manifest], PATHS.manifest);
  assertNoDuplicateJsonKeys(authorityRaw, PATHS.authority);
  const scenarios = JSON.parse(texts[PATHS.scenarios]);
  const manifest = JSON.parse(texts[PATHS.manifest]);
  const authority = JSON.parse(authorityRaw);

  validateMarkdown(markdowns);
  assertNoImplementationInvention(Object.values(markdowns).join("\n"), scenarios);
  validateAuthority(authority, texts);
  validateScenarios(scenarios, authority);
  validateGolden(texts[PATHS.csv], manifest, authority);

  if (!standalone) validateCanonicalSources(root);

  return {
    mode: standalone ? "standalone" : "canonical",
    caseCount: scenarios.caseCount,
    fixtureCount: manifest.fixtureCount,
    artifactCount: authority.artifactInventory.length,
    openGateCount: authority.ownerGates.length,
    promotionStatus: authority.promotionDescriptor.status,
    implementationDecision: authority.implementationDecision,
  };
}

function main() {
  const args = process.argv.slice(2);
  assert.ok(args.length === 0 || (args.length === 1 && args[0] === "--standalone"), "usage: PRODUCT_REMAINING_JIT_VALIDATOR_v1.mjs [--standalone]");
  const result = validateRoot(DEFAULT_ROOT, { standalone: args[0] === "--standalone" });
  process.stdout.write(`PRODUCT_REMAINING_JIT_VALIDATOR PASS ${JSON.stringify(result)}\n`);
}

if (resolve(process.argv[1] ?? "") === fileURLToPath(import.meta.url)) {
  try {
    main();
  } catch (error) {
    process.stderr.write(`PRODUCT_REMAINING_JIT_VALIDATOR FAIL: ${error.message}\n`);
    process.exitCode = 1;
  }
}
