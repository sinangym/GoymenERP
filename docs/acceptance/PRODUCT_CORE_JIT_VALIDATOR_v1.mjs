#!/usr/bin/env node

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const DEFAULT_ROOT = resolve(SCRIPT_DIR, "..", "..");

export const PATHS = Object.freeze({
  acceptance: "docs/acceptance/PRODUCT_CORE_U01_U02_U07_U08_U09_ACCEPTANCE_v1.md",
  authority: "docs/acceptance/PRODUCT_CORE_JIT_AUTHORITY_v1.json",
  canonicalAcceptance: "docs/acceptance/ACCEPTANCE_70.md",
  csv: "docs/formulas/PRODUCT_UNIT_CONVERSION_GOLDEN_v1.csv",
  formulaMarkdown: "docs/formulas/PRODUCT_UNIT_CONVERSION_GOLDEN_v1.md",
  index: "docs/features/PRODUCT_CORE_U01_U02_U07_U08_U09_JIT_INDEX_v1.md",
  identity: "docs/features/PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1.md",
  manifest: "docs/formulas/PRODUCT_UNIT_CONVERSION_GOLDEN_MANIFEST_v1.json",
  mutation: "docs/acceptance/PRODUCT_CORE_JIT_VALIDATOR_MUTATION_TEST_v1.mjs",
  u07: "docs/features/PRODUCT_U07_PACKAGE_STANDARDS_JIT_SPEC_v1.md",
  u08: "docs/features/PRODUCT_U08_UNITS_CONVERSIONS_JIT_SPEC_v1.md",
  u09: "docs/features/PRODUCT_U09_TECHNICAL_TEMPLATES_JIT_SPEC_v1.md",
  validator: "docs/acceptance/PRODUCT_CORE_JIT_VALIDATOR_v1.mjs",
});

const ARTIFACT_PATHS = Object.freeze([
  PATHS.mutation,
  PATHS.validator,
  PATHS.acceptance,
  PATHS.csv,
  PATHS.formulaMarkdown,
  PATHS.index,
  PATHS.identity,
  PATHS.manifest,
  PATHS.u07,
  PATHS.u08,
  PATHS.u09,
]);

const REQUIRED_FILES = Object.freeze([PATHS.authority, ...ARTIFACT_PATHS]);

const REQUIRED_PROMOTION_FIELDS = Object.freeze([
  Object.freeze({ path: "formula.version", type: "string", nullable: false, minLength: 1 }),
  Object.freeze({ path: "inputs[kind=measure].unitId", type: "string", nullable: false, minLength: 1, appliesToEveryMatch: true }),
  Object.freeze({ path: "inputs[kind=controlledReference].referenceId", type: "string", nullable: false, minLength: 1, minMatches: 0, appliesToEveryMatch: true }),
  Object.freeze({ path: "expected.unitId", type: "string", nullable: false, minLength: 1 }),
  Object.freeze({ path: "roundingPolicyVersion", type: "string", nullable: false, minLength: 1 }),
  Object.freeze({ path: "tolerancePolicyVersion", type: "string", nullable: false, minLength: 1 }),
  Object.freeze({ path: "parameterSources", type: "array<string>", nullable: false, minItems: 1, itemsMinLength: 1, forbiddenItemPrefixes: Object.freeze(["SYNTHETIC_"]) }),
  Object.freeze({ path: "packageStandardVersion", type: "string", nullable: false, minLength: 1, when: Object.freeze({ path: "packageStandardApplicable", equals: true }) }),
  Object.freeze({ path: "traceHash", type: "string", nullable: false, pattern: "^sha256:[0-9a-f]{64}$" }),
]);

const EXPECTED_CSV_HEADERS = Object.freeze([
  "TestId", "FormulaId", "FormulaVersion", "Senaryo", "GirdiJSON",
  "BeklenenCiktiJSON", "Birimler", "Yuvarlama", "Tolerans", "Kaynak",
  "Onaylayan", "Durum",
]);

const CANONICAL_AT = Object.freeze({
  "AT-001": "Kullanıcı yalnız yetkili şirket, şube ve depo kayıtlarını görür.",
  "AT-002": "Global arama yetkisiz kaydı sonuç, öneri, toplam veya sayı olarak sızdırmaz.",
  "AT-003": "Kritik değişiklik önce/sonra, neden, kullanıcı, rol ve korelasyonla audit üretir.",
  "AT-005": "Eşzamanlı değişiklikte ikinci kullanıcıya çatışma ve fark gösterilir.",
  "AT-006": "Başarısız kaydetme kullanıcının form verisini kaybettirmez.",
  "AT-008": "Şirket kapsamı değişikliği açık formu sessizce başka şirkete taşımaz.",
  "AT-015": "Ürün sınıfı ve ticari rol bağımsızdır; çelişki kontrol edilir.",
  "AT-016": "Birim dönüşümü yuvarlama/tolerans golden örneğini geçer.",
  "AT-017": "Paket standardının yeni sürümü geçmiş siparişi değiştirmez.",
  "AT-045": "Boru/profil adet–boy–kg dönüşümü golden örneğini geçer.",
  "AT-049": "Yeni reçete/formül/tolerans sürümü geçmiş gerçekleşmeyi değiştirmez.",
});

const FORBIDDEN_PRODUCT_AT = new Set(["AT-013"]);

const AT_ALLOWED_SERIES = Object.freeze({
  "AT-001": new Set(["X", "U01", "U07"]),
  "AT-002": new Set(["X", "U01", "U07"]),
  "AT-003": new Set(["X", "U02", "U07", "U09"]),
  "AT-005": new Set(["X", "U02", "U07", "U09"]),
  "AT-006": new Set(["X", "U02"]),
  "AT-008": new Set(["X"]),
  "AT-015": new Set(["U01", "U02", "U09"]),
  "AT-016": new Set(["X", "U02", "U07", "U08", "U09"]),
  "AT-017": new Set(["U07"]),
  "AT-045": new Set(["U02", "U07", "U08", "U09"]),
  "AT-049": new Set(["U08", "U09"]),
});

const AT_SEMANTIC_HINT = Object.freeze({
  "AT-001": /authorized|yetkili|company.{0,30}scope|CMP-[AB].{0,80}(context|sız)/iu,
  "AT-002": /global arama|non-disclos|sızdır|sızmaz|sızınt|yetkisiz.{0,40}(sonuç|öneri|toplam|sayı|kayıt)/iu,
  "AT-003": /audit|önce\/sonra|before\/after|korelasyon|correlation/iu,
  "AT-005": /eşzaman|concurr|stale|rowversion|yarış|çatışma|conflict/iu,
  "AT-006": /başarısız.{0,40}(kaydet|save)|form.{0,40}(korun|kaybet)|girdi.{0,40}(korun|kaybet)|değerler korunur|save failure/iu,
  "AT-008": /şirket.{0,40}(değiş|switch)|context.{0,40}(değiş|switch)|dirty form|açık form/iu,
  "AT-045": /boru|profil|geometry|geometri|ağırlık|FormulaId|adet.{0,30}(boy|kg)|kg.{0,30}(adet|boy)/iu,
  "AT-049": /reçete|formula|formül|tolerans|snapshot.{0,60}(geçmiş|histor|değişmez|version)|geçmiş.{0,40}(gerçekleş|formula|formül)/iu,
});

const EXPECTED_SERIES = Object.freeze({
  X: 18,
  U01: 17,
  U02: 23,
  U07: 19,
  U08: 23,
  U09: 28,
});

function sha256(text) {
  return `sha256:${createHash("sha256").update(text, "utf8").digest("hex")}`;
}

function stableJson(value) {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(stableJson).join(",")}]`;
  return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`).join(",")}}`;
}

function gcd(a, b) {
  a = a < 0n ? -a : a;
  b = b < 0n ? -b : b;
  while (b !== 0n) [a, b] = [b, a % b];
  return a;
}

class Fraction {
  constructor(numerator, denominator = 1n) {
    assert.notEqual(denominator, 0n, "zero denominator");
    if (denominator < 0n) {
      numerator = -numerator;
      denominator = -denominator;
    }
    const divisor = gcd(numerator, denominator);
    this.n = numerator / divisor;
    this.d = denominator / divisor;
  }

  static parse(raw) {
    const text = String(raw);
    assert.match(text, /^-?\d+(?:\.\d+)?$/, `not a base-10 decimal: ${text}`);
    const negative = text.startsWith("-");
    const unsigned = negative ? text.slice(1) : text;
    const [whole, decimal = ""] = unsigned.split(".");
    const denominator = 10n ** BigInt(decimal.length);
    const numerator = BigInt(`${whole}${decimal}`) * (negative ? -1n : 1n);
    return new Fraction(numerator, denominator);
  }

  add(other) { return new Fraction(this.n * other.d + other.n * this.d, this.d * other.d); }
  sub(other) { return new Fraction(this.n * other.d - other.n * this.d, this.d * other.d); }
  mul(other) { return new Fraction(this.n * other.n, this.d * other.d); }
  div(other) { return new Fraction(this.n * other.d, this.d * other.n); }
  equals(other) { return this.n === other.n && this.d === other.d; }
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
      } else if (char === '"') quoted = false;
      else field += char;
    } else if (char === '"') quoted = true;
    else if (char === ",") {
      row.push(field);
      field = "";
    } else if (char === "\n") {
      row.push(field.replace(/\r$/, ""));
      if (row.some((cell) => cell !== "")) rows.push(row);
      row = [];
      field = "";
    } else field += char;
  }
  if (quoted) throw new Error("CSV contains an unterminated quoted field");
  if (field !== "" || row.length > 0) {
    row.push(field.replace(/\r$/, ""));
    if (row.some((cell) => cell !== "")) rows.push(row);
  }
  if (rows.length === 0) throw new Error("CSV is empty");
  const headers = rows[0];
  return {
    headers,
    records: rows.slice(1).map((cells, rowIndex) => {
      if (cells.length !== headers.length) throw new Error(`CSV row ${rowIndex + 2} has ${cells.length} cells; expected ${headers.length}`);
      return Object.fromEntries(headers.map((header, index) => [header, cells[index]]));
    }),
  };
}

function input(caseItem, name) {
  const match = caseItem.inputs.find((item) => item.name === name);
  assert.ok(match, `${caseItem.testId}: missing input ${name}`);
  return Fraction.parse(match.value);
}

function recompute(caseItem) {
  switch (caseItem.formula.id) {
    case "MAT-002":
      return input(caseItem, "Kalinlik").mul(input(caseItem, "En")).mul(input(caseItem, "Boy")).mul(input(caseItem, "Yogunluk")).div(Fraction.parse("1000000"));
    case "MAT-003":
      return input(caseItem, "BirimKg").mul(input(caseItem, "Adet"));
    case "MAT-004":
      return input(caseItem, "GirilenKg").div(input(caseItem, "BirimKg"));
    case "TUB-001":
      return input(caseItem, "PaketSayisi").mul(input(caseItem, "PaketIciAdet"));
    case "TUB-002":
      return input(caseItem, "ToplamAdet").mul(input(caseItem, "Boy")).div(Fraction.parse("1000"));
    case "TUB-004":
      return input(caseItem, "BirimKg").mul(input(caseItem, "ToplamAdet"));
    case "BAL-006":
      return input(caseItem, "GercekKg").div(input(caseItem, "GercekAdet"));
    case "MAT-009": {
      const shape = caseItem.inputs.find((item) => item.name === "Sekil");
      assert.equal(shape?.kind, "controlledReference", `${caseItem.testId}: Sekil must be a controlledReference`);
      assert.equal(shape?.value, "KARE", `${caseItem.testId}: this fixture only proves KARE`);
      const four = Fraction.parse("4");
      const perimeter = four.mul(input(caseItem, "A"));
      const effective = perimeter.sub(four.mul(input(caseItem, "EtKalinligi")));
      return effective.mul(input(caseItem, "EtKalinligi")).mul(input(caseItem, "Boy")).mul(input(caseItem, "Yogunluk")).div(Fraction.parse("1000000"));
    }
    default:
      throw new Error(`${caseItem.testId}: unsupported FormulaId ${caseItem.formula.id}`);
  }
}

function parseAcceptanceCases(markdown) {
  const cases = [];
  for (const line of markdown.split(/\r?\n/)) {
    const match = line.match(/^\|\s*`(CASE-PRD-(X|U\d{2})-(\d{3}))`\s*\|/u);
    if (!match) continue;
    const columns = line.split("|").slice(1, -1).map((cell) => cell.trim());
    cases.push({
      id: match[1],
      series: match[2],
      ordinal: Number(match[3]),
      line,
      atIds: [...new Set((columns.at(-1)?.match(/AT-\d{3}/g) ?? []))],
    });
  }
  return cases;
}

function getPathMatches(root, pathExpression) {
  if (pathExpression === "formula.version") return [root.formula?.version];
  if (pathExpression === "inputs[kind=measure].unitId") return root.inputs.filter((item) => item.kind === "measure").map((item) => item.unitId);
  if (pathExpression === "inputs[kind=controlledReference].referenceId") return root.inputs.filter((item) => item.kind === "controlledReference").map((item) => item.referenceId);
  if (pathExpression === "expected.unitId") return [root.expected?.unitId];
  if (pathExpression === "roundingPolicyVersion") return [root.roundingPolicyVersion];
  if (pathExpression === "tolerancePolicyVersion") return [root.tolerancePolicyVersion];
  if (pathExpression === "parameterSources") return [root.parameterSources];
  if (pathExpression === "packageStandardApplicable") return [root.packageStandardApplicable];
  if (pathExpression === "packageStandardVersion") return [root.packageStandardVersion];
  if (pathExpression === "traceHash") return [root.traceHash];
  throw new Error(`unknown promotion path: ${pathExpression}`);
}

function validatePromotionField(caseItem, field) {
  if (field.when) {
    const conditional = getPathMatches(caseItem, field.when.path);
    if (conditional.length !== 1 || conditional[0] !== field.when.equals) return;
  }
  const values = getPathMatches(caseItem, field.path);
  const minMatches = field.minMatches ?? 1;
  if (values.length < minMatches) throw new Error(`${caseItem.testId}: ${field.path} resolved ${values.length} matches; minimum is ${minMatches}`);
  for (const value of values) {
    if (value === null || value === undefined) throw new Error(`${caseItem.testId}: ${field.path} is null`);
    if (field.type === "string") {
      if (typeof value !== "string") throw new Error(`${caseItem.testId}: ${field.path} must be string`);
      if (field.minLength && value.length < field.minLength) throw new Error(`${caseItem.testId}: ${field.path} is empty`);
      if (field.pattern && !(new RegExp(field.pattern).test(value))) throw new Error(`${caseItem.testId}: ${field.path} format mismatch`);
    } else if (field.type === "array<string>") {
      if (!Array.isArray(value) || value.length < (field.minItems ?? 0)) throw new Error(`${caseItem.testId}: ${field.path} must be a non-empty array`);
      for (const item of value) {
        if (typeof item !== "string" || item.length < (field.itemsMinLength ?? 0)) throw new Error(`${caseItem.testId}: ${field.path} contains an invalid item`);
        if ((field.forbiddenItemPrefixes ?? []).some((prefix) => item.startsWith(prefix))) throw new Error(`${caseItem.testId}: ${field.path} contains forbidden provenance ${item}`);
      }
    } else throw new Error(`unknown promotion field type: ${field.type}`);
  }
}

export function computeTraceHash(caseItem) {
  const copy = structuredClone(caseItem);
  delete copy.traceHash;
  return sha256(stableJson(copy));
}

export function validateManifest(manifest) {
  const errors = [];
  const capture = (operation) => {
    try { operation(); } catch (error) { errors.push(error.message); }
  };

  capture(() => assert.equal(manifest.schemaVersion, 2, "manifest schemaVersion must be 2"));
  capture(() => assert.match(manifest.sourceSnapshot, /^main@[0-9a-f]{40}$/u, "sourceSnapshot must pin an exact main SHA"));
  capture(() => assert.deepEqual(manifest.inputKindVocabulary, ["measure", "controlledReference"], "inputKindVocabulary drift"));
  capture(() => assert.equal(manifest.promotionSchema?.validationMode, "FAIL_CLOSED", "promotionSchema must be FAIL_CLOSED"));
  capture(() => assert.equal(manifest.promotionSchema?.version, "1.0.0", "promotionSchema version drift"));
  capture(() => assert.deepEqual(manifest.promotionSchema?.fields, REQUIRED_PROMOTION_FIELDS, "promotionSchema descriptor set/order/type drift"));
  capture(() => assert.match(manifest.traceHashPolicy?.format ?? "", /^sha256:/u, "traceHashPolicy format missing"));

  const cases = Array.isArray(manifest.cases) ? manifest.cases : [];
  capture(() => assert.equal(cases.length, 9, "manifest must contain exactly nine arithmetic cases"));
  capture(() => assert.equal(new Set(cases.map((item) => item.testId)).size, cases.length, "manifest TestId values must be unique"));

  const mapping = manifest.measurementKindMapping;
  capture(() => {
    const entries = [...mapping.entries].sort((left, right) => left.csvNitelik.localeCompare(right.csvNitelik, "en"));
    const canonical = `${entries.map((entry) => `${entry.csvNitelik}=${entry.measurementKind}`).join("\n")}\n`;
    assert.equal(mapping.digest, sha256(canonical), "measurementKindMapping digest mismatch");
    assert.equal(new Set(entries.map((entry) => entry.csvNitelik)).size, entries.length, "duplicate csvNitelik mapping");
    assert.equal(new Set(entries.map((entry) => entry.measurementKind)).size, entries.length, "ambiguous measurementKind mapping");
  });

  for (const caseItem of cases) {
    capture(() => assert.match(caseItem.testId, /^GT-PRD-[A-Z0-9-]+$/u, `${caseItem.testId}: invalid TestId`));
    capture(() => assert.ok(caseItem.inputs.length > 0, `${caseItem.testId}: inputs missing`));
    capture(() => assert.ok(caseItem.inputs.every((item) => manifest.inputKindVocabulary.includes(item.kind)), `${caseItem.testId}: unknown input kind`));
    capture(() => assert.ok(recompute(caseItem).equals(Fraction.parse(caseItem.expected.raw)), `${caseItem.testId}: arithmetic result mismatch`));
    capture(() => assert.ok(mapping.entries.some((entry) => entry.measurementKind === caseItem.expected.measurementKind), `${caseItem.testId}: unmapped measurementKind`));
  }

  if (manifest.status === "APPROVED_EXECUTABLE") {
    for (const caseItem of cases) {
      for (const field of manifest.promotionSchema.fields) capture(() => validatePromotionField(caseItem, field));
      capture(() => assert.equal(caseItem.traceHash, computeTraceHash(caseItem), `${caseItem.testId}: traceHash content mismatch`));
    }
  } else capture(() => assert.equal(manifest.status, "DRAFT_NON_EXECUTABLE", "unknown manifest status"));

  return errors;
}

function validateSpecContent(label, markdown, requiredPatterns) {
  assert.ok(typeof markdown === "string" && markdown.trim().length >= 1000, `${label}: spec is empty or truncated`);
  for (const [name, pattern] of requiredPatterns) {
    assert.match(markdown, pattern, `${label}: required semantic marker missing: ${name}`);
  }
}

export function validateAuthority(authority, artifacts, acceptanceCases) {
  const errors = [];
  const capture = (operation) => {
    try { operation(); } catch (error) { errors.push(error.message); }
  };

  capture(() => assert.equal(authority.schemaVersion, 1, "authority schemaVersion must be 1"));
  capture(() => assert.equal(authority.authorityId, "PRODUCT-CORE-JIT-AUTHORITY-V1", "authorityId drift"));
  capture(() => assert.deepEqual(authority.promotionDescriptor?.fields, REQUIRED_PROMOTION_FIELDS, "authority promotion descriptor drift"));
  capture(() => assert.equal(authority.promotionDescriptor?.version, "1.0.0", "authority promotion descriptor version drift"));
  capture(() => assert.equal(authority.promotionDescriptor?.digest, sha256(stableJson(REQUIRED_PROMOTION_FIELDS)), "authority promotion descriptor digest mismatch"));

  const inventory = Array.isArray(authority.artifactInventory) ? authority.artifactInventory : [];
  capture(() => assert.deepEqual(inventory.map((item) => item.path), ARTIFACT_PATHS, "authority artifact inventory path/order drift"));
  capture(() => assert.equal(new Set(inventory.map((item) => item.path)).size, ARTIFACT_PATHS.length, "authority artifact inventory contains duplicates"));
  for (const item of inventory) {
    capture(() => assert.ok(Object.hasOwn(artifacts, item.path), `${item.path}: authority artifact content missing`));
    capture(() => assert.equal(item.sha256, sha256(artifacts[item.path]), `${item.path}: authority artifact digest mismatch`));
  }

  const exactMap = Object.fromEntries(acceptanceCases.map((item) => [item.id, item.atIds]));
  capture(() => assert.deepEqual(authority.caseToCanonicalAt, exactMap, "exact case-to-AT mapping drift"));
  capture(() => assert.deepEqual(authority.caseToCanonicalAt?.["CASE-PRD-U02-023"], ["AT-003", "AT-005"], "reactivation composite AT-003/AT-005 mapping drift"));
  return errors;
}

export function validatePackageData(data, options = {}) {
  const errors = [];
  const capture = (operation) => {
    try { operation(); } catch (error) { errors.push(error.message); }
  };
  const manifest = typeof data.manifest === "string" ? JSON.parse(data.manifest) : data.manifest;
  errors.push(...validateManifest(manifest));

  const csv = parseCsv(data.csv);
  capture(() => assert.deepEqual(csv.headers, EXPECTED_CSV_HEADERS, "CSV header drift"));
  capture(() => assert.equal(csv.records.length, 9, "CSV must contain exactly nine data rows"));
  capture(() => assert.equal(new Set(csv.records.map((row) => row.TestId)).size, csv.records.length, "CSV TestId values must be unique"));

  const byId = new Map(manifest.cases.map((item) => [item.testId, item]));
  const mappingByCsv = new Map(manifest.measurementKindMapping.entries.map((entry) => [entry.csvNitelik, entry.measurementKind]));
  for (const row of csv.records) {
    capture(() => {
      const caseItem = byId.get(row.TestId);
      assert.ok(caseItem, `${row.TestId}: CSV row missing from manifest`);
      assert.equal(row.FormulaId, caseItem.formula.id, `${row.TestId}: FormulaId parity mismatch`);
      const expectedJson = JSON.parse(row.BeklenenCiktiJSON);
      const rawEntries = Object.entries(expectedJson).filter(([key]) => key !== "Nitelik");
      assert.equal(rawEntries.length, 1, `${row.TestId}: expected JSON must contain one raw result plus Nitelik`);
      assert.ok(Fraction.parse(rawEntries[0][1]).equals(Fraction.parse(caseItem.expected.raw)), `${row.TestId}: CSV/manifest raw parity mismatch`);
      assert.equal(mappingByCsv.get(expectedJson.Nitelik), caseItem.expected.measurementKind, `${row.TestId}: Nitelik/measurementKind parity mismatch`);
      assert.equal(row.FormulaVersion, "TBD", `${row.TestId}: draft CSV FormulaVersion must remain TBD`);
      assert.equal(row.Durum, "Taslak", `${row.TestId}: draft CSV row must remain Taslak`);
    });
  }
  capture(() => assert.deepEqual([...new Set(csv.records.map((row) => row.TestId))].sort(), [...byId.keys()].sort(), "CSV/manifest TestId set mismatch"));

  for (const testId of byId.keys()) capture(() => assert.ok(data.formulaMarkdown.includes(`\`${testId}\``), `${testId}: formula Markdown row missing`));
  capture(() => assert.match(data.formulaMarkdown, /NOT_PRODUCTION|NON_EXECUTABLE|production[^\n]{0,80}(değil|yok)/iu, "formula Markdown must state non-production status"));

  const acceptanceCases = parseAcceptanceCases(data.acceptance);
  capture(() => assert.equal(new Set(acceptanceCases.map((item) => item.id)).size, acceptanceCases.length, "acceptance case IDs must be unique"));
  for (const [series, expectedCount] of Object.entries(EXPECTED_SERIES)) {
    capture(() => {
      const actual = acceptanceCases.filter((item) => item.series === series).sort((left, right) => left.ordinal - right.ordinal);
      assert.equal(actual.length, expectedCount, `${series}: expected ${expectedCount} cases`);
      assert.deepEqual(actual.map((item) => item.ordinal), Array.from({ length: expectedCount }, (_, index) => index + 1), `${series}: case ordinals must be gapless`);
    });
  }
  for (const caseItem of acceptanceCases) {
    for (const atId of caseItem.atIds) {
      capture(() => assert.ok(CANONICAL_AT[atId], `${caseItem.id}: unknown/unapproved canonical mapping ${atId}`));
      capture(() => assert.ok(!FORBIDDEN_PRODUCT_AT.has(atId), `${caseItem.id}: ${atId} is Cari-specific and forbidden in this Product package`));
      const allowedSeries = AT_ALLOWED_SERIES[atId];
      if (allowedSeries) capture(() => assert.ok(allowedSeries.has(caseItem.series), `${caseItem.id}: ${atId} is not valid for ${caseItem.series}`));
      const semanticHint = AT_SEMANTIC_HINT[atId];
      if (semanticHint) capture(() => assert.match(caseItem.line, semanticHint, `${caseItem.id}: ${atId} semantic mismatch`));
    }
  }

  errors.push(...validateAuthority(data.authority, data.artifacts, acceptanceCases));

  capture(() => validateSpecContent("U-07", data.u07, [
    ["canonical screen", /Kanonik ekran:\s*`U-07`/u],
    ["snapshot immutability", /geçmiş[^\n]{0,100}snapshot[^\n]{0,100}(değişmez|değiştirmez)|snapshot[^\n]{0,100}(geçmiş[^\n]{0,100})?(değişmez|değiştirmez)/iu],
    ["canonical AT-017", /`AT-017`/u],
    ["package implementation gate", /`G-PRD-PACK-01`/u],
    ["scope decision", /`PRD-PACK-SCOPE-001`/u],
    ["site decision", /`PRD-PACK-SITE-001`/u],
    ["local acceptance cross-reference", /`CASE-PRD-U07-\*`/u],
  ]));
  capture(() => validateSpecContent("U-08", data.u08, [
    ["canonical screen", /Kanonik ekran:\s*`U-08`/u],
    ["deterministic conversion", /deterministik|deterministic/iu],
    ["movement snapshot", /movement snapshot|hareket snapshot/iu],
    ["canonical AT-016", /`AT-016`/u],
    ["canonical AT-045", /`AT-045`/u],
    ["canonical AT-049", /`AT-049`/u],
    ["unit implementation gate", /`G-PRD-UNIT-01`/u],
    ["local acceptance cross-reference", /`CASE-PRD-U08-\*`/u],
  ]));
  capture(() => validateSpecContent("U-09", data.u09, [
    ["canonical screen", /Kanonik ekran:\s*`U-09`/u],
    ["immutable FieldId", /immutable FieldId|değişmez[^\n]{0,30}FieldId/iu],
    ["unknown field fail-closed", /unknown field[^\n]{0,60}fail-closed|tanımlı olmayan field[^\n]{0,60}fail-closed/iu],
    ["canonical AT-015", /`AT-015`/u],
    ["canonical AT-016", /`AT-016`/u],
    ["template implementation gate", /`G-PRD-TEMPLATE-01`/u],
    ["tracking implementation gate", /`G-PRD-TRACK-01`/u],
    ["local acceptance cross-reference", /`CASE-PRD-U09-\*`/u],
  ]));
  capture(() => assert.match(data.acceptance, /\|\s*`AT-005`\s*\|\s*Concurrent create\/edit\/approve\/reactivate conflict\s*\|/u, "AT-005 closure matrix semantic drift"));

  capture(() => assert.match(data.acceptance, /CASE-PRD-U02-\d{3}[^\n]*(reactivat|yeniden.{0,20}aktif)/iu, "reactivation must have a task-local acceptance case"));
  capture(() => assert.match(data.acceptance, /PROPOSED_JIT[^\n]{0,500}(idempoten|G-PRD-PERSIST-01)|(idempoten|G-PRD-PERSIST-01)[^\n]{0,500}PROPOSED_JIT/isu, "global Product idempotency cases must remain PROPOSED_JIT and persistence-gated"));
  for (const id of ["CASE-PRD-X-004", "CASE-PRD-X-005", "CASE-PRD-X-017", "CASE-PRD-X-018"]) {
    capture(() => {
      const caseItem = acceptanceCases.find((item) => item.id === id);
      assert.ok(caseItem, `${id}: missing global-idempotency draft case`);
      assert.equal(caseItem.atIds.length, 0, `${id}: no canonical AT mapping exists for the proposed global idempotency model`);
    });
  }

  capture(() => assert.match(data.index, /reactivat|yeniden.{0,20}aktif/iu, "index must preserve locked reactivation action existence"));
  capture(() => assert.match(data.identity, /G-PRD-PERSIST-01/iu, "identity spec must gate global idempotency on G-PRD-PERSIST-01"));
  capture(() => assert.match(data.identity, /PROPOSED_JIT/iu, "identity spec must label global idempotency as PROPOSED_JIT"));

  if (options.requireCanonicalSource) {
    capture(() => assert.ok(data.canonicalAcceptance, "canonical ACCEPTANCE_70.md is required"));
    for (const [atId, meaning] of Object.entries(CANONICAL_AT)) {
      capture(() => assert.ok(data.canonicalAcceptance.includes(`**${atId}:** ${meaning}`), `${atId}: canonical meaning drift`));
    }
  }

  return errors;
}

export function loadPackage(root = DEFAULT_ROOT, options = {}) {
  const read = (relativePath) => readFileSync(resolve(root, relativePath), "utf8");
  const missing = REQUIRED_FILES.filter((relativePath) => !existsSync(resolve(root, relativePath)));
  if (missing.length > 0) throw new Error(`missing required package files: ${missing.join(", ")}`);
  const artifacts = Object.fromEntries(ARTIFACT_PATHS.map((relativePath) => [relativePath, read(relativePath)]));
  return {
    acceptance: read(PATHS.acceptance),
    artifacts,
    authority: JSON.parse(read(PATHS.authority)),
    canonicalAcceptance: existsSync(resolve(root, PATHS.canonicalAcceptance)) ? read(PATHS.canonicalAcceptance) : null,
    csv: read(PATHS.csv),
    formulaMarkdown: read(PATHS.formulaMarkdown),
    index: read(PATHS.index),
    identity: read(PATHS.identity),
    manifest: JSON.parse(read(PATHS.manifest)),
    u07: read(PATHS.u07),
    u08: read(PATHS.u08),
    u09: read(PATHS.u09),
    requireCanonicalSource: options.requireCanonicalSource,
  };
}

export function validatePackage(root = DEFAULT_ROOT, options = {}) {
  const data = loadPackage(root, options);
  return validatePackageData(data, options);
}

function parseArguments(argv) {
  let root = DEFAULT_ROOT;
  let standalone = false;
  let json = false;
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--root") root = resolve(argv[++index]);
    else if (argv[index] === "--standalone") standalone = true;
    else if (argv[index] === "--json") json = true;
    else throw new Error(`unknown argument: ${argv[index]}`);
  }
  return { root, standalone, json };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = parseArguments(process.argv.slice(2));
    const errors = validatePackage(args.root, { requireCanonicalSource: !args.standalone });
    if (errors.length > 0) {
      if (args.json) console.error(JSON.stringify({ ok: false, errors }, null, 2));
      else errors.forEach((error) => console.error(`ERROR: ${error}`));
      process.exitCode = 1;
    } else if (args.json) console.log(JSON.stringify({ ok: true, arithmeticCases: 9, acceptanceCases: Object.values(EXPECTED_SERIES).reduce((sum, count) => sum + count, 0) }));
    else console.log(`PASS: Product JIT package; 9 arithmetic cases; ${Object.values(EXPECTED_SERIES).reduce((sum, count) => sum + count, 0)} acceptance cases.`);
  } catch (error) {
    console.error(`ERROR: ${error.message}`);
    process.exitCode = 1;
  }
}
