#!/usr/bin/env node

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import { loadPackage, validatePackageData } from "./SALES_S01_S04_ORDER_CORE_VALIDATOR_v1.mjs";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");

function packageCopy({ requireCanonicalSource = false } = {}) {
  const loaded = loadPackage(ROOT, { requireCanonicalSource });
  return {
    ...loaded,
    data: structuredClone(loaded.data),
    authority: structuredClone(loaded.authority),
    canonical: loaded.canonical ? structuredClone(loaded.canonical) : null,
    canonicalSources: loaded.canonicalSources ? structuredClone(loaded.canonicalSources) : null,
    externalBinarySources: loaded.externalBinarySources ? structuredClone(loaded.externalBinarySources) : null,
  };
}

function validateMutation(mutate) {
  const pkg = packageCopy();
  mutate(pkg);
  return validatePackageData(pkg, { requireCanonicalSource: false });
}

function validateCanonicalMutation(mutate) {
  const pkg = packageCopy({ requireCanonicalSource: true });
  mutate(pkg);
  return validatePackageData(pkg, { requireCanonicalSource: true });
}

function sha256Text(value) {
  return createHash("sha256").update(value, "utf8").digest("hex");
}

function sha256Bytes(value) {
  return createHash("sha256").update(value).digest("hex");
}

function syncAuthorityRaw(pkg) {
  pkg.authorityRaw = `${JSON.stringify(pkg.authority, null, 2)}\n`;
}

function syncScenarioRawAndRepin(pkg) {
  pkg.jsonRaw = `${JSON.stringify(pkg.data, null, 2)}\n`;
  pkg.authority.packageArtifactInventory.find((item) => item.path.endsWith("SCENARIOS_v1.json")).sha256 = sha256Text(pkg.jsonRaw);
  syncAuthorityRaw(pkg);
}

function repinTextArtifact(pkg, path, field) {
  pkg.authority.packageArtifactInventory.find((item) => item.path === path).sha256 = sha256Text(pkg[field]);
  syncAuthorityRaw(pkg);
}

function expectFailure(errors, fragment) {
  assert.ok(errors.some((error) => error.includes(fragment)), `expected failure containing ${JSON.stringify(fragment)}; got:\n${errors.join("\n")}`);
}

function caseById(pkg, id) {
  const item = pkg.data.cases.find((candidate) => candidate.id === id);
  assert.ok(item, `missing mutation target ${id}`);
  return item;
}

function goldenById(pkg, id) {
  const item = pkg.data.goldenFixtures.find((candidate) => candidate.id === id);
  assert.ok(item, `missing mutation target ${id}`);
  return item;
}

test("baseline Sales package passes standalone validation", () => {
  const errors = validatePackageData(packageCopy(), { requireCanonicalSource: false });
  assert.deepEqual(errors, []);
});

test("source snapshot drift fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.data.sourceSnapshot = "main@stale"; });
  expectFailure(errors, "source snapshot drift");
});

test("unknown top-level key fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.data.rogueProductionSwitch = true; });
  expectFailure(errors, "Top-level JSON: exact key/order drift");
});

test("duplicate key in scenario JSON fails closed even when JSON.parse result is unchanged", () => {
  const errors = validateMutation((pkg) => {
    pkg.jsonRaw = pkg.jsonRaw.replace('"schemaVersion": "sales-order-core-scenarios.v1"', '"schemaVersion": "sales-order-core-scenarios.v1",\n  "schemaVersion": "sales-order-core-scenarios.v1"');
  });
  expectFailure(errors, "Scenario JSON: duplicate JSON key $.schemaVersion");
});

test("duplicate key in authority JSON fails closed even when JSON.parse result is unchanged", () => {
  const errors = validateMutation((pkg) => {
    pkg.authorityRaw = pkg.authorityRaw.replace('"schemaVersion": "sales-order-core-authority.v1"', '"schemaVersion": "sales-order-core-authority.v1",\n  "schemaVersion": "sales-order-core-authority.v1"');
  });
  expectFailure(errors, "Authority JSON: duplicate JSON key $.schemaVersion");
});

test("fixturePolicy unknown nested production switch fails closed after coordinated repin", () => {
  const errors = validateMutation((pkg) => {
    pkg.data.fixturePolicy.productionActivation = "GO";
    syncScenarioRawAndRepin(pkg);
  });
  expectFailure(errors, "Fixture policy: exact key/order drift");
  expectFailure(errors, "Scenario metadata: exact fixture/alias/parity semantic contract drift");
});

test("fixture policy note cannot claim production policy after coordinated repin", () => {
  const errors = validateMutation((pkg) => {
    pkg.data.fixturePolicy.note = "Approved production seed and business policy.";
    syncScenarioRawAndRepin(pkg);
  });
  expectFailure(errors, "Scenario metadata: exact fixture/alias/parity semantic contract drift");
});

test("aliases cannot be replaced by a production activation object", () => {
  const errors = validateMutation((pkg) => {
    pkg.data.aliases = { productionActivation: "GO" };
    syncScenarioRawAndRepin(pkg);
  });
  expectFailure(errors, "Aliases: exact key/order drift");
});

test("fixture alias cannot become an exact production role", () => {
  const errors = validateMutation((pkg) => {
    pkg.data.aliases.actors[0] = "SYSTEM_ADMINISTRATOR";
    syncScenarioRawAndRepin(pkg);
  });
  expectFailure(errors, "Aliases.actors: exact fixture catalog drift");
});

test("undefined fixture alias reference fails closed", () => {
  const errors = validateMutation((pkg) => {
    caseById(pkg, "CASE-SAL-S03-003").precondition = "CUST-UNDECLARED global active ve CMP-A account active.";
    syncScenarioRawAndRepin(pkg);
  });
  expectFailure(errors, "Aliases: referenced fixture CUST-UNDECLARED is undefined");
});

test("parity nested permissive switch fails closed after coordinated repin", () => {
  const errors = validateMutation((pkg) => {
    pkg.data.parity.unknownNestedKeysAllowed = true;
    syncScenarioRawAndRepin(pkg);
  });
  expectFailure(errors, "Parity contract: exact key/order drift");
});

test("raw scenario JSON and parsed data cannot diverge", () => {
  const errors = validateMutation((pkg) => {
    pkg.jsonRaw = pkg.jsonRaw.replace("Talep ve Teklif aynı Sales record aşamalarıdır", "Raw-only semantic mutation");
  });
  expectFailure(errors, "Scenario raw/parsed deep-equality drift");
});

test("parsed scenario mutation cannot hide behind unchanged raw JSON", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S01-001").title = "Parsed-only semantic mutation"; });
  expectFailure(errors, "Scenario raw/parsed deep-equality drift");
  expectFailure(errors, "Cases: exact parsed semantic contract drift");
});

test("duplicate or gapful CASE-SAL ID series fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.data.cases[1].id = pkg.data.cases[0].id; });
  expectFailure(errors, "ID series/order must be unique and gapless");
});

test("unknown case field fails closed", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S03-001").endpoint = "/invented"; });
  expectFailure(errors, "Case CASE-SAL-S03-001: exact key/order drift");
});

test("Markdown and JSON title parity drift fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.acceptance = pkg.acceptance.replace("| Seçili şirket izolasyonu |", "| Drift edilmiş başlık |"); });
  expectFailure(errors, "Markdown parity CASE-SAL-X-001: title drift");
});

test("idempotency cannot be mislabeled as canonical concurrency AT-005", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-X-005").atRefs = ["AT-005"]; });
  expectFailure(errors, "Case CASE-SAL-X-005: canonical AT semantic mapping drift");
});

test("accessibility cannot be mislabeled as failed-save AT-006", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-X-010").atRefs = ["AT-006"]; });
  expectFailure(errors, "Case CASE-SAL-X-010: canonical AT semantic mapping drift");
});

test("wrong Sales/Purchase AT-025 mapping fails closed", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S01-001").atRefs = ["AT-025"]; });
  expectFailure(errors, "Case CASE-SAL-S01-001: canonical AT semantic mapping drift");
});

test("cancel-history AT-007 cannot be attached to preview-only evidence", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S04-009").atRefs = ["AT-007"]; });
  expectFailure(errors, "Case CASE-SAL-S04-009: canonical AT semantic mapping drift");
});

test("critical header change cannot be mislabeled as canonical line-change AT-023", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S04-003").atRefs = ["AT-023"]; });
  expectFailure(errors, "Case CASE-SAL-S04-003: canonical AT semantic mapping drift");
});

test("unresolved gate reference fails closed", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S03-001").gateIds.push("G-SAL-UNKNOWN-01"); });
  expectFailure(errors, "unknown value G-SAL-UNKNOWN-01");
});

test("unknown source reference fails closed", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S03-001").sourceRefs.push("INVENTED-SOURCE"); });
  expectFailure(errors, "unknown value INVENTED-SOURCE");
});

test("allowed but wrong per-case source reference fails exact contract", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S03-001").sourceRefs.push("ADR-009"); });
  expectFailure(errors, "Cases: exact per-case sourceRefs contract drift");
});

test("authority exact case-to-source map cannot be repointed", () => {
  const errors = validateMutation((pkg) => { pkg.authority.exactCaseToSourceRefs["CASE-SAL-S03-001"] = ["ADR-009"]; });
  expectFailure(errors, "Authority CASE-SAL-S03-001: exact case-to-source mapping drift");
});

test("source resolver catalog cannot repoint a Product decision", () => {
  const errors = validateMutation((pkg) => {
    pkg.authority.sourceReferenceCatalog["PRD-PERM-001"] = "issue:https://github.com/sinangym/GoymenERP/issues/100#PRD-PERM-001=B;state=OPEN";
    pkg.authority.sourceReferenceCatalogSha256 = sha256Text(JSON.stringify(pkg.authority.sourceReferenceCatalog));
    syncAuthorityRaw(pkg);
  });
  expectFailure(errors, "source reference catalog descriptor digest drift");
  expectFailure(errors, "source reference catalog semantic drift");
});

test("pinned Product issue descriptor fields cannot drift after coordinated repin", () => {
  const errors = validateMutation((pkg) => {
    pkg.authority.sourceReferenceCatalog["PRD-SCOPE-001"] = pkg.authority.sourceReferenceCatalog["PRD-SCOPE-001"]
      .replace("updatedAt=2026-08-30T16:40:06Z", "updatedAt=2026-09-02T00:00:00Z")
      .replace(
        "bodySha256=sha256:738b8e742a1ede01e6e7382c9ca701586ec83102aa445c8b9b6d1abcb80bc983",
        `bodySha256=sha256:${"0".repeat(64)}`,
      );
    pkg.authority.sourceReferenceCatalogSha256 = sha256Text(JSON.stringify(pkg.authority.sourceReferenceCatalog));
    syncAuthorityRaw(pkg);
  });
  expectFailure(errors, "source reference catalog descriptor digest drift");
  expectFailure(errors, "source reference catalog semantic drift");
});

test("S-01 same-record identity cannot lose the locked DOCX source", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S01-001");
    item.sourceRefs = item.sourceRefs.filter((source) => source !== "SALES-LOCK-DOCX");
  });
  expectFailure(errors, "S-01 single-record identity cases must resolve to the locked Sales DOCX");
});

test("production activation cannot be promoted by document mutation", () => {
  const errors = validateMutation((pkg) => { pkg.data.productionActivation = "GO"; });
  expectFailure(errors, "production activation must remain NO_GO");
});

test("exact API enum or permission declaration cannot be enabled", () => {
  const errors = validateMutation((pkg) => { pkg.data.exactApiEnumsPermissionsDeclared = true; });
  expectFailure(errors, "exact API/enum/permission declaration is forbidden");
});

test("invented exact HTTP route fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.spec += "\nPOST /api/sales/orders\n"; });
  expectFailure(errors, "exact HTTP route detected");
});

test("coordinated spec repin cannot introduce Product implementation GO", () => {
  const errors = validateMutation((pkg) => {
    pkg.spec += "\nProduct schema/API implementation GO.\n";
    repinTextArtifact(pkg, "docs/features/SALES_S01_S04_ORDER_CORE_JIT_SPEC_v1.md", "spec");
  });
  expectFailure(errors, "executable trust-anchor digest drift");
});

test("coordinated spec repin cannot invent an exact role or permission", () => {
  const errors = validateMutation((pkg) => {
    pkg.spec += "\nExact Sales role: SALES_SUPERUSER; exact permission: sales.orders.write.\n";
    repinTextArtifact(pkg, "docs/features/SALES_S01_S04_ORDER_CORE_JIT_SPEC_v1.md", "spec");
  });
  expectFailure(errors, "executable trust-anchor digest drift");
});

test("coordinated acceptance repin cannot claim canonical AT closure", () => {
  const errors = validateMutation((pkg) => {
    pkg.acceptance += "\nAT-021 is CLOSED/PASS for production.\n";
    repinTextArtifact(pkg, "docs/acceptance/SALES_S01_S04_ORDER_CORE_ACCEPTANCE_v1.md", "acceptance");
  });
  expectFailure(errors, "executable trust-anchor digest drift");
});

test("owner decision cannot silently close", () => {
  const errors = validateMutation((pkg) => { pkg.data.decisionGates[0].status = "CLOSED"; });
  expectFailure(errors, "Decision gate SAL-REQ-001: status must remain open NO-GO");
});

test("owner decision topic cannot be weakened", () => {
  const errors = validateMutation((pkg) => { pkg.data.decisionGates[0].topic = "Generic Sales behavior"; });
  expectFailure(errors, "Decision gates: exact owner/topic/safeDefault semantic contract drift");
});

test("owner decision safe default cannot become permissive", () => {
  const errors = validateMutation((pkg) => { pkg.data.decisionGates[0].safeDefault = "Production writes may proceed"; });
  expectFailure(errors, "Decision gates: exact owner/topic/safeDefault semantic contract drift");
});

test("implementation gate cannot silently close", () => {
  const errors = validateMutation((pkg) => { pkg.data.gates[0].status = "READY"; });
  expectFailure(errors, "Implementation gate G-SAL-CARI-01: status must remain OPEN_NO_GO");
});

test("implementation gate owner cannot be reassigned", () => {
  const errors = validateMutation((pkg) => { pkg.data.gates[0].owner = "Nobody"; });
  expectFailure(errors, "Implementation gates: exact owner/dependency/blocks semantic contract drift");
});

test("implementation gate blocked surface cannot disappear", () => {
  const errors = validateMutation((pkg) => { pkg.data.gates[0].blocks = ["nothing"]; });
  expectFailure(errors, "Implementation gates: exact owner/dependency/blocks semantic contract drift");
});

test("implementation to owner-decision dependency cannot disappear", () => {
  const errors = validateMutation((pkg) => {
    const gate = pkg.data.gates.find((item) => item.id === "G-SAL-PRICE-01");
    gate.dependsOnDecisionGateIds = gate.dependsOnDecisionGateIds.filter((id) => id !== "SAL-MARGIN-001");
  });
  expectFailure(errors, "Decision gate SAL-MARGIN-001: no machine-readable case/source/implementation dependency");
});

test("spec and JSON implementation dependency parity cannot drift", () => {
  const errors = validateMutation((pkg) => {
    pkg.spec = pkg.spec.replace("`SAL-PRICE-001`, `SAL-MARGIN-001` | Price/discount", "`SAL-PRICE-001` | Price/discount");
  });
  expectFailure(errors, "Spec parity G-SAL-PRICE-01: dependsOnDecisionGateIds drift");
});

test("SIM-001 price arithmetic mutation fails closed", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-PRICE-001").expected.discountedNet = "39000.00"; });
  expectFailure(errors, "SIM-001 arithmetic drift");
});

test("golden formulaRef cannot be reassigned", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-PRICE-001").formulaRef = "INV-005"; });
  expectFailure(errors, "Golden GOLD-SAL-PRICE-001: formulaRef drift");
});

test("golden input shape cannot be renamed", () => {
  const errors = validateMutation((pkg) => {
    const fixture = goldenById(pkg, "GOLD-SAL-RISK-001");
    fixture.inputs.currentRisk = fixture.inputs.currentGrossRisk;
    delete fixture.inputs.currentGrossRisk;
  });
  expectFailure(errors, "Golden GOLD-SAL-RISK-001: exact input key/order drift");
});

test("golden expected shape cannot gain an invented output", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-CONV-001").expected.rounded = true; });
  expectFailure(errors, "Golden GOLD-SAL-CONV-001: exact expected key/order drift");
});

test("golden owner-gate mapping is exact", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-PRICE-001").gateIds.reverse(); });
  expectFailure(errors, "Golden GOLD-SAL-PRICE-001: exact owner gate mapping drift");
});

test("golden canonical AT mapping is exact", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-PRICE-001").atRefs = []; });
  expectFailure(errors, "Golden GOLD-SAL-PRICE-001: exact AT mapping drift");
});

test("INV-005 remaining arithmetic mutation fails closed", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-REMAIN-001").expected.remainingQuantity = "600"; });
  expectFailure(errors, "INV-005 arithmetic drift");
});

test("INV-005 cannot be reassigned from fulfillment to allocation owner", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-REMAIN-001").gateIds = ["G-SAL-ALLOC-01"]; });
  expectFailure(errors, "fulfillment owner gate drift");
});

test("effective kg order denominator cannot substitute actual G for theoretical T", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-EFFECTIVE-KG-001").inputs.orderTheoreticalKgT = "500"; });
  expectFailure(errors, "order amount/theoretical kg T arithmetic drift");
});

test("effective kg fulfillment denominator cannot substitute theoretical T", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-EFFECTIVE-KG-001").inputs.actualDispatchKgG = "1000"; });
  expectFailure(errors, "fulfillment amount/actual dispatch kg G arithmetic drift");
});

test("uninvoiced fulfilled TL arithmetic mutation fails closed", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-UNINVOICED-001").expected.uninvoicedFulfilledTl = "0.00"; });
  expectFailure(errors, "TL commercial amount arithmetic drift");
});

test("uninvoiced fulfilled USD arithmetic mutation fails closed", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-UNINVOICED-001").expected.uninvoicedFulfilledUsd = "0.00"; });
  expectFailure(errors, "USD commercial amount arithmetic drift");
});

test("allocation denominator cannot include stockless Service", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-ALLOC-PCT-001").expected.physicalNeedDenominatorKg = "1505"; });
  expectFailure(errors, "physical-only denominator drift");
});

test("allocation percent arithmetic mutation fails closed", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-ALLOC-PCT-001").expected.allocationPercentExact = "100"; });
  expectFailure(errors, "allocation percentage arithmetic drift");
});

test("stockless Service exclusion flag cannot be disabled", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-ALLOC-PCT-001").expected.stocklessServiceExcluded = false; });
  expectFailure(errors, "stockless Service must remain excluded");
});

test("malformed decimal fails closed without accidental pass", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-RISK-001").inputs.currentGrossRisk = "NaN"; });
  expectFailure(errors, "parse/recompute failure");
});

test("synthetic golden provenance cannot be promoted", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-CONV-001").status = "APPROVED"; });
  expectFailure(errors, "must stay SYNTHETIC_CONTRACT_ONLY");
});

test("term structural golden cannot be forced onto AT-023", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-TERM-001").atRefs = ["AT-023"]; });
  expectFailure(errors, "term rule must remain local and reconciled");
});

test("fixed-factor conversion fixture cannot claim AT-016 rounding/tolerance closure", () => {
  const errors = validateMutation((pkg) => { goldenById(pkg, "GOLD-SAL-CONV-001").atRefs = ["AT-016"]; });
  expectFailure(errors, "fixed-factor fixture is local-only");
});

test("seven state axes cannot collapse into one status", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S02-002");
    item.expected = ["Tek status saklanır."];
    item.forbidden = ["Ayrı eksen gerekmez."];
  });
  expectFailure(errors, "expected polarity must preserve seven independent axes");
});

test("Request, Quotation and Order cannot split into separate aggregates", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S01-001");
    item.expected = ["Talep ve Teklif ayrı aggregate ID'leri alır."];
    item.forbidden = ["Tek Sales record ID korunmaz."];
  });
  expectFailure(errors, "expected polarity must preserve one Sales identity");
  expectFailure(errors, "forbidden polarity must prohibit separate pre-order aggregates");
});

test("expected and forbidden outcome arrays cannot be polarity-swapped", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S01-001");
    [item.expected, item.forbidden] = [item.forbidden, item.expected];
  });
  expectFailure(errors, "Cases: exact expected-polarity contract drift");
  expectFailure(errors, "Cases: exact forbidden-polarity contract drift");
  expectFailure(errors, "expected polarity must not contain the prohibited aggregate split");
  expectFailure(errors, "forbidden polarity must not contain the required identity preservation");
});

test("critical header change cannot inherit approval", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S04-003");
    item.expected = ["Current revision yerinde güncellenir ve approval korunur."];
    item.forbidden = ["Yeni revision açılmaz."];
  });
  expectFailure(errors, "expected polarity must create a local DEC revision without approval inheritance");
});

test("term-only change cannot be changed to mandatory reapproval", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S04-006");
    item.expected = ["Her termin değişikliği yeni approval açar."];
    item.forbidden = ["Mevcut approval korunamaz."];
  });
  expectFailure(errors, "expected polarity must keep approval on a term-only change");
});

test("AI cannot become required authority for manual draft", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S03-016");
    item.expected = ["AI order line'ı doğrudan uygular."];
    item.forbidden = ["Manuel akış kapalıdır."];
  });
  expectFailure(errors, "expected polarity must preserve the AI-off manual path");
});

test("AI cannot gain final-order approval authority", () => {
  const errors = validateMutation((pkg) => {
    caseById(pkg, "CASE-SAL-S03-016").forbidden = ["AI nihai sipariş onayı verebilir."];
  });
  expectFailure(errors, "forbidden polarity must prohibit AI final order approval");
});

test("compact summary cannot lose reconciliation and quantity no-double-count semantics", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-LOCK-001");
    item.expected = ["Üç display axis ayrı ad/değerle görünür."];
  });
  expectFailure(errors, "expected polarity must preserve compact summary disclosure");
  expectFailure(errors, "expected polarity must reconcile summary with active lines and price movements");
  expectFailure(errors, "expected polarity must preserve the locked remaining-amount basis");
  expectFailure(errors, "expected polarity must prohibit quantity double counting");
  expectFailure(errors, "expected polarity must preserve the locked operation/invoice percentage bases");
});

test("compact summary remaining and percentage bases cannot be substituted after coordinated repin", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-LOCK-001");
    item.expected = item.expected.map((value) => value
      .replace("Kalan tutar Sipariş Tutarı eksi Operasyon Gerçekleşen tutarıdır", "Kalan tutar Sipariş Tutarı eksi Fatura Gerçekleşen tutarıdır")
      .replace("operasyon yüzdesi mal ve hizmet için ortak gerçekleşme oranıdır; fatura yüzdesi gerçekleşen/faturalanabilir kısım üzerinden", "operasyon yüzdesi yalnız mal oranıdır; fatura yüzdesi Sipariş Tutarı üzerinden"));
    item.forbidden = item.forbidden.filter((value) => !value.startsWith("Kalan tutar fatura veya tahsis"));
    syncScenarioRawAndRepin(pkg);
  });
  expectFailure(errors, "expected polarity must preserve the locked remaining-amount basis");
  expectFailure(errors, "expected polarity must preserve the locked operation/invoice percentage bases");
  expectFailure(errors, "forbidden polarity must prohibit a wrong remaining-amount basis");
});

test("uninvoiced fulfilled visibility cannot be promoted into the collapsed row after coordinated repin", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-LOCK-005");
    item.expected = item.expected.map((value) => value.replace("yalnız detay açıldığında", "daraltılmış tek satırda daima"));
    item.forbidden = item.forbidden.filter((value) => !value.includes("daraltılmış tek satırda zorunlu"));
    syncScenarioRawAndRepin(pkg);
  });
  expectFailure(errors, "expected polarity must preserve detail-only uninvoiced visibility");
  expectFailure(errors, "forbidden polarity must preserve locked-source visibility adjudication");
});

test("invoice-stage price movement cannot bypass capability, approval or append-only audit", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-LOCK-008");
    item.expected = ["Fiyat hareketi doğrudan uygulanır."];
    item.forbidden = ["Geçmiş yeniden yazılabilir."];
  });
  expectFailure(errors, "expected polarity must gate invoice-stage price movement application");
  expectFailure(errors, "expected polarity must preserve prepare-to-approval behavior");
  expectFailure(errors, "expected polarity must preserve price-movement audit history");
  expectFailure(errors, "forbidden polarity must prohibit destructive price-movement history");
});

test("AT-021 exact route/task binding cannot be weakened", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S03-013");
    item.expected = ["Any approval task is acceptable."];
    item.forbidden = ["No route binding is required."];
  });
  expectFailure(errors, "expected polarity must resolve the correct approval route");
  expectFailure(errors, "forbidden polarity must prohibit a hard-coded route");
});

test("cancellation cannot delete immutable history", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-S04-013");
    item.expected = ["Old revisions are deleted."];
    item.forbidden = ["History may be rewritten."];
  });
  expectFailure(errors, "expected polarity must preserve cancellation history");
  expectFailure(errors, "forbidden polarity must prohibit destructive cancellation");
});

test("source preference cannot become allocation authority", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-LOCK-009");
    item.expected = ["Draft preference immediately allocates stock."];
    item.forbidden = ["Allocation owner is not consulted."];
  });
  expectFailure(errors, "expected polarity must pin allocation ownership");
  expectFailure(errors, "forbidden polarity must prohibit source-preference side effects");
});

test("allocation cannot become fulfillment authority", () => {
  const errors = validateMutation((pkg) => {
    const item = caseById(pkg, "CASE-SAL-LOCK-010");
    item.expected = ["Full allocation sets realized and remaining."];
    item.forbidden = ["Fulfillment owner is not needed."];
  });
  expectFailure(errors, "expected polarity must pin fulfillment ownership");
  expectFailure(errors, "forbidden polarity must prohibit allocation-as-fulfillment");
});

test("canonical AT-021 coverage cannot disappear", () => {
  const errors = validateMutation((pkg) => {
    for (const item of pkg.data.cases) item.atRefs = item.atRefs.filter((value) => value !== "AT-021");
  });
  expectFailure(errors, "Canonical coverage: AT-021 has no exact-semantic case");
});

test("unknown screen identity fails closed", () => {
  const errors = validateMutation((pkg) => { caseById(pkg, "CASE-SAL-S03-001").screenIds = ["S-99"]; });
  expectFailure(errors, "unknown value S-99");
});

test("promotion field removal fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.data.requiredGoldenPromotionFields.pop(); });
  expectFailure(errors, "Promotion gate: exact required fields/order drift");
});

test("authority artifact digest drift fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.authority.packageArtifactInventory[0].sha256 = "0".repeat(64); });
  expectFailure(errors, "Authority artifact docs/features/SALES_S01_S04_ORDER_CORE_JIT_SPEC_v1.md: content digest drift");
});

test("authority exact case-to-AT mapping drift fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.authority.exactCaseToCanonicalAt["CASE-SAL-S04-013"] = []; });
  expectFailure(errors, "Authority CASE-SAL-S04-013: exact case-to-AT mapping drift");
});

test("authority promotion descriptor reorder fails closed", () => {
  const errors = validateMutation((pkg) => { pkg.authority.requiredGoldenPromotionDescriptor.reverse(); });
  expectFailure(errors, "Authority: independent golden promotion descriptor drift");
});

test("authority case semantic digest cannot be repinned", () => {
  const errors = validateMutation((pkg) => { pkg.authority.caseSemanticContractSha256 = "0".repeat(64); });
  expectFailure(errors, "Authority: case semantic contract digest drift");
});

test("authority golden semantic digest cannot be repinned", () => {
  const errors = validateMutation((pkg) => { pkg.authority.goldenFixtureContractSha256 = "0".repeat(64); });
  expectFailure(errors, "Authority: golden fixture contract digest drift");
});

test("locked DOCX binary source pin cannot drift", () => {
  const errors = validateMutation((pkg) => { pkg.authority.externalBinarySourcePins[0].sha256 = "0".repeat(64); });
  expectFailure(errors, "Authority: external binary source pin drift");
});

test("locked DOCX bytes are mandatory in canonical mode", () => {
  const path = "docs/ui/sources/sales/sales-order-detail-locked-decision-v1.0.docx";
  const errors = validateCanonicalMutation((pkg) => { delete pkg.externalBinarySources[path]; });
  expectFailure(errors, `Authority external binary source ${path}: bytes were not loaded`);
});

test("locked DOCX byte mutation cannot be hidden by descriptor repin", () => {
  const path = "docs/ui/sources/sales/sales-order-detail-locked-decision-v1.0.docx";
  const errors = validateCanonicalMutation((pkg) => {
    const bytes = Buffer.from(pkg.externalBinarySources[path]);
    bytes[0] ^= 0xff;
    pkg.externalBinarySources[path] = bytes;
    pkg.authority.externalBinarySourcePins[0].sha256 = sha256Bytes(bytes);
  });
  expectFailure(errors, `Authority external binary source ${path}: true git blob recomputation drift`);
});

test("validator bytes are part of package artifact inventory", () => {
  const errors = validateMutation((pkg) => { pkg.validatorRaw += "\n// mutated validator bytes\n"; });
  expectFailure(errors, "Authority artifact docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_v1.mjs: content digest drift");
});

test("mutation-suite bytes are part of package artifact inventory", () => {
  const errors = validateMutation((pkg) => { pkg.mutationRaw += "\n// mutated mutation-suite bytes\n"; });
  expectFailure(errors, "Authority artifact docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_MUTATION_TEST_v1.mjs: content digest drift");
});

test("coordinated mutation-suite descriptor repin cannot replace the executable suite", () => {
  const errors = validateMutation((pkg) => {
    pkg.mutationRaw += "\n// coordinated replacement attempt\n";
    repinTextArtifact(pkg, "docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_MUTATION_TEST_v1.mjs", "mutationRaw");
  });
  expectFailure(errors, "executable trust-anchor digest drift");
});

test("validator or mutation artifact cannot disappear from inventory", () => {
  const errors = validateMutation((pkg) => { pkg.authority.packageArtifactInventory.pop(); });
  expectFailure(errors, "Authority: package artifact path/order drift");
});

test("canonical source mutation cannot be hidden by repinning only SHA-256", () => {
  const path = "docs/acceptance/ACCEPTANCE_70.md";
  const errors = validateCanonicalMutation((pkg) => {
    pkg.canonicalSources[path] += "\nsemantic mutation";
    pkg.canonical.acceptance = pkg.canonicalSources[path];
    pkg.authority.canonicalSourceInventory.find((item) => item.path === path).sha256 = sha256Text(pkg.canonicalSources[path]);
  });
  expectFailure(errors, `Authority canonical source ${path}: true git blob recomputation drift`);
});

test("canonical decision-register bytes cannot drift from pinned git blob", () => {
  const path = "docs/decisions/KARAR_SICILI.md";
  const errors = validateCanonicalMutation((pkg) => {
    pkg.canonicalSources[path] += "\nDEC-SAL-999 invented";
    pkg.canonical.decisions = pkg.canonicalSources[path];
    pkg.authority.canonicalSourceInventory.find((item) => item.path === path).sha256 = sha256Text(pkg.canonicalSources[path]);
  });
  expectFailure(errors, `Authority canonical source ${path}: true git blob recomputation drift`);
});

test("shared CI gate cannot be closed by generic CI or invented evidence", () => {
  const errors = validateMutation((pkg) => {
    pkg.authority.sharedCiGate.status = "CLOSED";
    pkg.authority.sharedCiGate.generalCiClosesGate = true;
    pkg.authority.sharedCiGate.workflowWiringEvidence = "generic-green";
  });
  expectFailure(errors, "Authority: G-SAL-CI-01 must remain OPEN_NO_GO");
  expectFailure(errors, "Authority: shared CI fail-closed contract drift");
});
