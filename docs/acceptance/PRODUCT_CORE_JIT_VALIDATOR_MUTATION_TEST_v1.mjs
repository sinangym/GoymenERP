#!/usr/bin/env node

import assert from "node:assert/strict";
import test from "node:test";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import {
  computeTraceHash,
  loadPackage,
  PATHS,
  validateManifest,
  validatePackageData,
} from "./PRODUCT_CORE_JIT_VALIDATOR_v1.mjs";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");

function packageCopy() {
  const loaded = loadPackage(ROOT, { requireCanonicalSource: false });
  return {
    ...loaded,
    manifest: structuredClone(loaded.manifest),
  };
}

function expectFailure(errors, fragment) {
  assert.ok(errors.some((error) => error.includes(fragment)), `expected failure containing ${JSON.stringify(fragment)}; got:\n${errors.join("\n")}`);
}

function replaceAtCell(markdown, caseId, newCell) {
  let replaced = false;
  const result = markdown.split(/\r?\n/).map((line) => {
    if (!line.includes(`\`${caseId}\``)) return line;
    replaced = true;
    return line.replace(/\|\s*(?:AT-[^|]*|—)\s*\|\s*$/u, `| ${newCell} |`);
  }).join("\n");
  assert.equal(replaced, true, `${caseId}: mutation target missing`);
  return result;
}

function promote(manifest) {
  const promoted = structuredClone(manifest);
  promoted.status = "APPROVED_EXECUTABLE";
  for (const caseItem of promoted.cases) {
    caseItem.formula.version = "FORMULA-V1";
    caseItem.inputs.forEach((item) => {
      if (item.kind === "measure") item.unitId = `UNIT:${item.unitAlias}`;
      else item.referenceId = `REFERENCE:${item.name}:${item.value}`;
    });
    caseItem.parameterSources = caseItem.parameterSources.map((value) => value.startsWith("SYNTHETIC_") ? `OWNER_EVIDENCE:${value.slice("SYNTHETIC_".length)}` : value);
    caseItem.expected.unitId = `UNIT:${caseItem.expected.unitAlias}`;
    caseItem.roundingPolicyVersion = "ROUNDING-V1";
    caseItem.tolerancePolicyVersion = "TOLERANCE-V1";
    if (caseItem.packageStandardApplicable) caseItem.packageStandardVersion = "PACKAGE-V1";
    caseItem.traceHash = computeTraceHash(caseItem);
  }
  return promoted;
}

test("current package passes fail-closed structural validation", () => {
  assert.deepEqual(validatePackageData(packageCopy(), { requireCanonicalSource: false }), []);
});

test("arithmetic mutation is killed", () => {
  const data = packageCopy();
  data.manifest.cases[0].expected.raw = "31.5";
  expectFailure(validatePackageData(data), "arithmetic result mismatch");
});

test("CSV-to-manifest raw drift is killed", () => {
  const data = packageCopy();
  data.csv = data.csv.replace('"{""HamBirimKg"":31.4,', '"{""HamBirimKg"":31.5,');
  expectFailure(validatePackageData(data), "CSV/manifest raw parity mismatch");
});

test("measurement vocabulary digest mutation is killed", () => {
  const data = packageCopy();
  data.manifest.measurementKindMapping.entries[0].measurementKind = "ACTUAL_MUTATED";
  expectFailure(validateManifest(data.manifest), "measurementKindMapping digest mismatch");
});

test("CSV Nitelik mapping mutation is killed", () => {
  const data = packageCopy();
  data.csv = data.csv.replace('""Nitelik"":""TEORIK_SENTETIK_FIXTURE""', '""Nitelik"":""UNKNOWN_KIND""');
  expectFailure(validatePackageData(data), "Nitelik/measurementKind parity mismatch");
});

test("duplicate manifest TestId is killed", () => {
  const data = packageCopy();
  data.manifest.cases[1].testId = data.manifest.cases[0].testId;
  expectFailure(validateManifest(data.manifest), "TestId values must be unique");
});

test("clean approved promotion envelope validates", () => {
  const data = packageCopy();
  assert.deepEqual(validateManifest(promote(data.manifest)), []);
});

test("synthetic provenance cannot be promoted", () => {
  const data = packageCopy();
  const promoted = promote(data.manifest);
  promoted.cases[0].parameterSources = ["SYNTHETIC_FIXTURE"];
  promoted.cases[0].traceHash = computeTraceHash(promoted.cases[0]);
  expectFailure(validateManifest(promoted), "forbidden provenance");
});

test("null UnitId cannot be promoted", () => {
  const data = packageCopy();
  const promoted = promote(data.manifest);
  promoted.cases[0].inputs[0].unitId = null;
  promoted.cases[0].traceHash = computeTraceHash(promoted.cases[0]);
  expectFailure(validateManifest(promoted), "unitId is null");
});

test("controlled-reference identifier is mandatory on promotion", () => {
  const data = packageCopy();
  const promoted = promote(data.manifest);
  const geometry = promoted.cases.find((item) => item.formula.id === "MAT-009");
  geometry.inputs.find((item) => item.kind === "controlledReference").referenceId = null;
  geometry.traceHash = computeTraceHash(geometry);
  expectFailure(validateManifest(promoted), "referenceId is null");
});

test("applicable package version is mandatory on promotion", () => {
  const data = packageCopy();
  const promoted = promote(data.manifest);
  const packageCase = promoted.cases.find((item) => item.packageStandardApplicable);
  packageCase.packageStandardVersion = null;
  packageCase.traceHash = computeTraceHash(packageCase);
  expectFailure(validateManifest(promoted), "packageStandardVersion is null");
});

test("traceHash content mutation is killed", () => {
  const data = packageCopy();
  const promoted = promote(data.manifest);
  promoted.cases[0].inputs[0].value = "2.1";
  expectFailure(validateManifest(promoted), "traceHash content mismatch");
});

test("acceptance case deletion creates a gap and is killed", () => {
  const data = packageCopy();
  data.acceptance = data.acceptance.split(/\r?\n/).filter((line) => !line.includes("`CASE-PRD-U08-010`")).join("\n");
  expectFailure(validatePackageData(data), "U08: expected 23 cases");
});

test("Cari-specific AT mapping cannot leak into Product package", () => {
  const data = packageCopy();
  data.acceptance = replaceAtCell(data.acceptance, "CASE-PRD-U01-001", "AT-013");
  expectFailure(validatePackageData(data), "Cari-specific");
});

test("canonical AT semantic mismatch is killed", () => {
  const data = packageCopy();
  data.acceptance = replaceAtCell(data.acceptance, "CASE-PRD-U02-001", "AT-003");
  expectFailure(validatePackageData(data), "AT-003 semantic mismatch");
});

test("reactivation acceptance deletion is killed", () => {
  const data = packageCopy();
  data.acceptance = data.acceptance.replace(/reactivat/giu, "action-removed").replace(/yeniden.{0,20}aktif/giu, "action-removed");
  expectFailure(validatePackageData(data), "reactivation must have");
});

test("global idempotency draft cannot claim an unrelated canonical AT", () => {
  const data = packageCopy();
  data.acceptance = replaceAtCell(data.acceptance, "CASE-PRD-X-004", "AT-006");
  expectFailure(validatePackageData(data), "no canonical AT mapping exists");
});

for (const [label, key, marker, expected] of [
  ["U-07", "u07", "`AT-017`", "U-07: spec is empty or truncated"],
  ["U-08", "u08", "`AT-049`", "U-08: spec is empty or truncated"],
  ["U-09", "u09", "`G-PRD-TRACK-01`", "U-09: spec is empty or truncated"],
]) {
  test(`${label} empty-content mutation is killed`, () => {
    const data = packageCopy();
    data[key] = "";
    expectFailure(validatePackageData(data), expected);
  });

  test(`${label} semantic-marker mutation is killed`, () => {
    const data = packageCopy();
    data[key] = data[key].replaceAll(marker, "`REMOVED-MARKER`");
    expectFailure(validatePackageData(data), `${label}: required semantic marker missing`);
  });
}

test("independent artifact digest detects source drift", () => {
  const data = packageCopy();
  data.artifacts[PATHS.u07] += "\nmutated\n";
  expectFailure(validatePackageData(data), `${PATHS.u07}: authority artifact digest mismatch`);
});

test("exact case-to-AT authority rejects a plausible but wrong mapping", () => {
  const data = packageCopy();
  data.acceptance = replaceAtCell(data.acceptance, "CASE-PRD-U01-001", "AT-015");
  expectFailure(validatePackageData(data), "exact case-to-AT mapping drift");
});

test("reactivation composite AT-003/AT-005 mapping is exact", () => {
  const data = packageCopy();
  data.acceptance = replaceAtCell(data.acceptance, "CASE-PRD-U02-023", "AT-005");
  expectFailure(validatePackageData(data), "exact case-to-AT mapping drift");
});

test("authority case-to-AT inventory cannot self-expand", () => {
  const data = packageCopy();
  data.authority.caseToCanonicalAt["CASE-PRD-U01-001"] = ["AT-015"];
  expectFailure(validatePackageData(data), "exact case-to-AT mapping drift");
});

for (const [label, mutate] of [
  ["empty", (fields) => fields.splice(0)],
  ["missing", (fields) => fields.splice(0, 1)],
  ["reordered", (fields) => fields.splice(0, 2, fields[1], fields[0])],
  ["renamed", (fields) => { fields[0].path = "formulaVersion"; }],
  ["type-weakened", (fields) => { fields[0].nullable = true; }],
]) {
  test(`promotion descriptor ${label} mutation is killed`, () => {
    const data = packageCopy();
    const promoted = promote(data.manifest);
    mutate(promoted.promotionSchema.fields);
    expectFailure(validateManifest(promoted), "promotionSchema descriptor set/order/type drift");
  });
}
