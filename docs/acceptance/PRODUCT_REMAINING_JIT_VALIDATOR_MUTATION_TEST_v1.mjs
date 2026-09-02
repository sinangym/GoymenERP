#!/usr/bin/env node

import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import test from "node:test";

import { PATHS, validateRoot } from "./PRODUCT_REMAINING_JIT_VALIDATOR_v1.mjs";

const REPO_ROOT = resolve(dirname(new URL(import.meta.url).pathname), "..", "..");
const PACKAGE_PATHS = Object.freeze([
  PATHS.index,
  PATHS.service,
  PATHS.price,
  PATHS.importSpec,
  PATHS.acceptance,
  PATHS.scenarios,
  PATHS.authority,
  PATHS.csv,
  PATHS.formulaMarkdown,
  PATHS.manifest,
  PATHS.validator,
  PATHS.mutation,
]);

const CANONICAL_PATHS = Object.freeze([
  PATHS.canonicalAcceptance,
  PATHS.decisionRegistry,
  PATHS.ownerDecisions,
  PATHS.openDecisions,
  PATHS.masterReport,
  PATHS.canonicalScreenStatus,
  PATHS.screenCatalog,
  PATHS.productPdf,
]);

function makeRoot(options = {}) {
  const root = mkdtempSync(join(tmpdir(), "product-remaining-jit-"));
  for (const relativePath of PACKAGE_PATHS) {
    const source = resolve(REPO_ROOT, relativePath);
    assert.ok(existsSync(source), `fixture source missing: ${relativePath}`);
    const destination = resolve(root, relativePath);
    mkdirSync(dirname(destination), { recursive: true });
    cpSync(source, destination);
  }
  if (options.canonical) {
    const fixtureRoot = process.env.PRODUCT_REMAINING_CANONICAL_FIXTURE_ROOT
      ? resolve(process.env.PRODUCT_REMAINING_CANONICAL_FIXTURE_ROOT)
      : REPO_ROOT;
    for (const relativePath of CANONICAL_PATHS) {
      const fixtureSource = resolve(fixtureRoot, relativePath);
      const explicitPdf = relativePath === PATHS.productPdf
        ? process.env.PRODUCT_REMAINING_PRODUCT_PDF_FIXTURE
        : null;
      const source = explicitPdf
        ? resolve(explicitPdf)
        : (existsSync(fixtureSource) ? fixtureSource : resolve(REPO_ROOT, relativePath));
      assert.ok(existsSync(source), `canonical fixture source missing: ${relativePath}`);
      const destination = resolve(root, relativePath);
      mkdirSync(dirname(destination), { recursive: true });
      cpSync(source, destination);
    }
  }
  return root;
}

function read(root, relativePath) {
  return readFileSync(resolve(root, relativePath), "utf8");
}

function write(root, relativePath, content) {
  writeFileSync(resolve(root, relativePath), content, "utf8");
}

function json(root, relativePath) {
  return JSON.parse(read(root, relativePath));
}

function writeJson(root, relativePath, value) {
  write(root, relativePath, `${JSON.stringify(value, null, 2)}\n`);
}

function replaceExact(root, relativePath, before, after) {
  const original = read(root, relativePath);
  assert.ok(original.includes(before), `mutation precondition missing: ${before}`);
  write(root, relativePath, original.replace(before, after));
}

function sha256(text) {
  return `sha256:${createHash("sha256").update(text, "utf8").digest("hex")}`;
}

function refreshDigest(root, relativePath) {
  const authority = json(root, PATHS.authority);
  const item = authority.artifactInventory.find((entry) => entry.path === relativePath);
  assert.ok(item, `inventory item missing: ${relativePath}`);
  item.sha256 = sha256(read(root, relativePath));
  writeJson(root, PATHS.authority, authority);
}

function mutateJson(root, relativePath, mutator, refresh = true) {
  const value = json(root, relativePath);
  mutator(value);
  writeJson(root, relativePath, value);
  if (refresh) refreshDigest(root, relativePath);
}

function mutation(name, mutator, options = {}) {
  test(name, () => {
    const standalone = options.standalone ?? true;
    const root = makeRoot({ canonical: !standalone });
    try {
      validateRoot(root, { standalone });
      mutator(root);
      assert.throws(
        () => validateRoot(root, { standalone }),
        undefined,
        `${name}: validator unexpectedly accepted mutation`,
      );
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test("baseline standalone package passes", () => {
  const root = makeRoot();
  try {
    assert.equal(validateRoot(root, { standalone: true }).caseCount, 62);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("baseline canonical package passes", () => {
  const root = makeRoot({ canonical: true });
  try {
    assert.equal(validateRoot(root, { standalone: false }).mode, "canonical");
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

mutation("raw duplicate authority key fails even when last value is valid", (root) => {
  replaceExact(
    root,
    PATHS.authority,
    '  "implementationDecision": "NO_GO",',
    '  "implementationDecision": "GO",\n  "implementationDecision": "NO_GO",',
  );
});
mutation("raw duplicate scenario key fails after digest refresh", (root) => {
  replaceExact(
    root,
    PATHS.scenarios,
    '  "decision": "PREPARATION_READY_IMPLEMENTATION_NO_GO",',
    '  "decision": "GO",\n  "decision": "PREPARATION_READY_IMPLEMENTATION_NO_GO",',
  );
  refreshDigest(root, PATHS.scenarios);
});
mutation("raw duplicate manifest key fails after digest refresh", (root) => {
  replaceExact(
    root,
    PATHS.manifest,
    '  "csvPath": "docs/formulas/PRODUCT_PRICE_SIMULATION_GOLDEN_v1.csv",',
    '  "csvPath": "docs/formulas/OTHER.csv",\n  "csvPath": "docs/formulas/PRODUCT_PRICE_SIMULATION_GOLDEN_v1.csv",',
  );
  refreshDigest(root, PATHS.manifest);
});

mutation("missing required index fails", (root) => unlinkSync(resolve(root, PATHS.index)));
mutation("unpinned index content change fails digest", (root) => {
  write(root, PATHS.index, `${read(root, PATHS.index)}\nTAMPER\n`);
});
mutation("markdown semantic drift cannot be laundered by inventory digest refresh", (root) => {
  write(root, PATHS.service, `${read(root, PATHS.service)}\nSemantically rewritten contract.\n`);
  refreshDigest(root, PATHS.service);
});
mutation("source collision disclosure cannot be removed even with refreshed digest", (root) => {
  replaceExact(root, PATHS.index, "PRODUCT-U-03` kaynak kartı Product düzenlemeyi gösterir ve canonical U-02", "PRODUCT-X-03` kaynak kartı farklıdır ve canonical U-02");
  refreshDigest(root, PATHS.index);
});
mutation("index exact source row cannot drift with token preserved", (root) => {
  replaceExact(root, PATHS.index, "| U-03 | Hizmetler liste + detay | `PRODUCT-H-01` |", "| U-03 | Hizmetler liste + detay | `PRODUCT-H-02` |");
  write(root, PATHS.index, `${read(root, PATHS.index)}\n<!-- PRODUCT-H-01 -->\n`);
  refreshDigest(root, PATHS.index);
});
mutation("index exact mapping status cannot drift", (root) => {
  replaceExact(root, PATHS.index, "H-03=`CANDIDATE_MERGE`", "H-03=`CANDIDATE_EXACT`");
  refreshDigest(root, PATHS.index);
});
mutation("index case-count claim cannot drift with refreshed digest", (root) => {
  replaceExact(root, PATHS.index, "62 gapless, makine-okur vaka", "60 gapless, makine-okur vaka");
  refreshDigest(root, PATHS.index);
});
mutation("service stockless invariant cannot be removed", (root) => {
  replaceExact(root, PATHS.service, "stoksuz ticari kayıt", "ticari kayıt");
  refreshDigest(root, PATHS.service);
});
mutation("exact API route invention fails", (root) => {
  write(root, PATHS.service, `${read(root, PATHS.service)}\nPOST /api/services\n`);
  refreshDigest(root, PATHS.service);
});
mutation("versioned exact API route invention fails", (root) => {
  write(root, PATHS.service, `${read(root, PATHS.service)}\nPOST /v1/services\n`);
  refreshDigest(root, PATHS.service);
});
mutation("lowercase exact API route invention fails", (root) => {
  write(root, PATHS.service, `${read(root, PATHS.service)}\npost /api/services\n`);
  refreshDigest(root, PATHS.service);
});
mutation("bare endpoint invention fails", (root) => {
  write(root, PATHS.service, `${read(root, PATHS.service)}\nEndpoint: /api/services\n`);
  refreshDigest(root, PATHS.service);
});
mutation("database DDL invention fails", (root) => {
  write(root, PATHS.importSpec, `${read(root, PATHS.importSpec)}\nCREATE TABLE PriceImport\n`);
  refreshDigest(root, PATHS.importSpec);
});
mutation("exact permission identifier invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nPermission.Product.Price.Apply\n`);
  refreshDigest(root, PATHS.price);
});
mutation("lowercase permission key invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\npermission key: product.price.apply\n`);
  refreshDigest(root, PATHS.price);
});
mutation("alternate production rounding mode invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nProduction rounding: TIES_TO_EVEN\n`);
  refreshDigest(root, PATHS.price);
});
mutation("HALF_DOWN production rounding invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nProduction rounding: HALF_DOWN\n`);
  refreshDigest(root, PATHS.price);
});
mutation("colon-delimited tax rate invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nKDV oranı: %20\n`);
  refreshDigest(root, PATHS.price);
});
mutation("named production FX source invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nProduction FX source: Refinitiv\n`);
  refreshDigest(root, PATHS.price);
});
mutation("persistence table name invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nPersistence table: ProductPriceVersion\n`);
  refreshDigest(root, PATHS.price);
});
mutation("price lifecycle marker removal fails", (root) => {
  replaceExact(root, PATHS.price, "Draft → Approved → Scheduled → Active → Expired", "Draft → Active");
  refreshDigest(root, PATHS.price);
});
mutation("import no-write chain removal fails", (root) => {
  replaceExact(root, PATHS.importSpec, "mapping → preview → validation/error → duplicate → approval → write", "upload → write");
  refreshDigest(root, PATHS.importSpec);
});
mutation("acceptance exact AT-018 wording drift fails", (root) => {
  replaceExact(root, PATHS.acceptance, "Fiyat sürümü para, birim, KDV, kanal ve geçerlilik bağlamını korur.", "Fiyat sürümü bağlamı korur.");
  refreshDigest(root, PATHS.acceptance);
});
mutation("scenario deletion fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => value.cases.splice(10, 1));
});
mutation("scenario duplicate ID fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[11].id = value.cases[10].id; });
});
mutation("scenario gap fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[12].id = "CASE-PRD-REM-U03-099"; });
});
mutation("scenario series reassignment fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[20].screen = "U03"; });
});
mutation("service cannot claim module-specific AT-018", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[10].canonicalAT = ["AT-018"]; });
});
mutation("price cannot claim import AT-019", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[40].canonicalAT = ["AT-019"]; });
});
mutation("exact case mapping cannot change with matching authority edit", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[30].canonicalAT = []; });
  const authority = json(root, PATHS.authority);
  authority.scenarioContract.caseToCanonicalAT["CASE-PRD-REM-U05-001"] = [];
  writeJson(root, PATHS.authority, authority);
});
mutation("case implementation decision cannot become GO", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].implementationDecision = "GO"; });
});
mutation("unknown owner gate fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].ownerGate = "G-PRD-INVENTED-01"; });
});
mutation("too-short expected evidence fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].expected = "ok"; });
});
mutation("scenario permissionId invention fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].permissionId = "invented"; });
});
mutation("reversed unsafe-file expected semantics fails after digest refresh", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => {
    const item = value.cases.find((entry) => entry.id === "CASE-PRD-REM-U10-011");
    item.expected = "Unsafe ve makrolu dosya kabul edilir; persistent business write hemen yapılır.";
  });
});
mutation("valid but wrong owner gate mapping fails after digest refresh", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => {
    const item = value.cases.find((entry) => entry.id === "CASE-PRD-REM-U10-011");
    item.ownerGate = "G-PRD-REMAIN-CI-01";
  });
});
mutation("scenario top-level extra key fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.endpoint = "invented"; });
});
mutation("scenario permissionKey invention fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].permissionKey = "Product.Price.Apply"; });
});
mutation("scenario endpoint invention fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].endpoint = "POST /api/product-price"; });
});
mutation("scenario DTO invention fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].dtoName = "ProductPriceDto"; });
});
mutation("scenario rounding mode invention fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].roundingMode = "HALF_UP"; });
});
mutation("scenario tax rate invention fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.cases[0].taxRate = "20"; });
});
mutation("scenario source snapshot drift fails", (root) => {
  mutateJson(root, PATHS.scenarios, (value) => { value.sourceSnapshot.baseSha = "0".repeat(40); });
});
mutation("authority cannot assume Product core PR closed", (root) => {
  const value = json(root, PATHS.authority);
  value.productCore181AssumedClosed = true;
  writeJson(root, PATHS.authority, value);
});
mutation("authority top-level extra key fails", (root) => {
  const value = json(root, PATHS.authority);
  value.endpoint = "invented";
  writeJson(root, PATHS.authority, value);
});
mutation("pilot role policy cannot widen Price roles", (root) => {
  const value = json(root, PATHS.authority);
  value.pilotRolePolicy.priceReadManageSimulateApprove = "SYSTEM_ADMINISTRATOR";
  writeJson(root, PATHS.authority, value);
});
mutation("pilot role policy cannot close exact permission gate", (root) => {
  const value = json(root, PATHS.authority);
  value.pilotRolePolicy.permissionGateStatus = "CLOSED";
  writeJson(root, PATHS.authority, value);
});
mutation("owner gate cannot be closed without evidence", (root) => {
  const value = json(root, PATHS.authority);
  value.ownerGates[0].status = "CLOSED";
  writeJson(root, PATHS.authority, value);
});
mutation("owner gate closure evidence cannot be asserted", (root) => {
  const value = json(root, PATHS.authority);
  value.ownerGates[0].closureEvidence = "unreviewed";
  writeJson(root, PATHS.authority, value);
});
mutation("owner gate set cannot shrink", (root) => {
  const value = json(root, PATHS.authority);
  value.ownerGates.pop();
  writeJson(root, PATHS.authority, value);
});
mutation("screen source mapping cannot drift", (root) => {
  const value = json(root, PATHS.authority);
  value.sourceDescriptor.screenMappings["U-10"].sources = ["PRODUCT-U10"];
  writeJson(root, PATHS.authority, value);
});
mutation("collision rule cannot be removed", (root) => {
  const value = json(root, PATHS.authority);
  value.sourceDescriptor.collisionRules.pop();
  writeJson(root, PATHS.authority, value);
});
mutation("OPEN-007 semantic pin cannot drift", (root) => {
  const value = json(root, PATHS.authority);
  value.sourceDescriptor.openDecisionPins["OPEN-007"] = "Fiyat önceliği";
  writeJson(root, PATHS.authority, value);
});
mutation("source blob pin cannot drift", (root) => {
  const value = json(root, PATHS.authority);
  value.sourceDescriptor.sourcePins[1].gitBlobSha = "0".repeat(40);
  writeJson(root, PATHS.authority, value);
});
mutation("issue snapshot body digest cannot drift", (root) => {
  const value = json(root, PATHS.authority);
  value.sourceDescriptor.issuePins[0].bodySha256 = `sha256:${"0".repeat(64)}`;
  writeJson(root, PATHS.authority, value);
});
mutation("binding source cannot lack a corresponding pin", (root) => {
  const value = json(root, PATHS.authority);
  value.sourceDescriptor.sourcePins.splice(0, 1);
  writeJson(root, PATHS.authority, value);
});
mutation("canonical AT descriptor cannot be shortened", (root) => {
  const value = json(root, PATHS.authority);
  value.canonicalAcceptance.exactTexts["AT-019"] = "Import writes safely.";
  writeJson(root, PATHS.authority, value);
});
mutation("shared CI gate cannot be closed", (root) => {
  const value = json(root, PATHS.authority);
  value.sharedCiWiring.status = "CLOSED";
  writeJson(root, PATHS.authority, value);
});
mutation("nonzero CI propagation requirement cannot be disabled", (root) => {
  const value = json(root, PATHS.authority);
  value.sharedCiWiring.nonzeroFailurePropagationRequired = false;
  writeJson(root, PATHS.authority, value);
});
mutation("package cannot claim workflow edit", (root) => {
  const value = json(root, PATHS.authority);
  value.sharedCiWiring.githubWorkflowEditedByThisPackage = true;
  writeJson(root, PATHS.authority, value);
});
mutation("authority implementation decision cannot become GO", (root) => {
  const value = json(root, PATHS.authority);
  value.implementationDecision = "GO";
  writeJson(root, PATHS.authority, value);
});
mutation("manifest and authority descriptor cannot self-promote", (root) => {
  mutateJson(root, PATHS.manifest, (value) => {
    value.promotionDescriptor.status = "READY";
    value.promotionDescriptor.promotionReady = true;
  });
  const authority = json(root, PATHS.authority);
  authority.promotionDescriptor.status = "READY";
  authority.promotionDescriptor.promotionReady = true;
  writeJson(root, PATHS.authority, authority);
});
mutation("descriptor policy pin cannot be invented", (root) => {
  mutateJson(root, PATHS.manifest, (value) => {
    value.promotionDescriptor.requiredPins.roundingPolicyVersion = "ROUNDING-v1";
  });
  const authority = json(root, PATHS.authority);
  authority.promotionDescriptor.requiredPins.roundingPolicyVersion = "ROUNDING-v1";
  writeJson(root, PATHS.authority, authority);
});
mutation("manifest fixture deletion fails", (root) => {
  mutateJson(root, PATHS.manifest, (value) => {
    value.fixtures.pop();
    value.fixtureCount -= 1;
  });
});
mutation("manifest CSV path cannot drift", (root) => {
  mutateJson(root, PATHS.manifest, (value) => { value.csvPath = "docs/formulas/OTHER.csv"; });
});
mutation("manifest top-level extra key fails", (root) => {
  mutateJson(root, PATHS.manifest, (value) => { value.roundingMode = "HALF_UP"; });
});
mutation("manifest fixture extra tax key fails", (root) => {
  mutateJson(root, PATHS.manifest, (value) => { value.fixtures[0].taxRate = "20"; });
});
mutation("CSV header drift fails", (root) => {
  replaceExact(root, PATHS.csv, "ExpectedPercentExact", "ExpectedPercent");
  refreshDigest(root, PATHS.csv);
});
mutation("CSV delta arithmetic mutation fails", (root) => {
  replaceExact(root, PATHS.csv, "PRD-PRICE-SIM-001,PREVIEW_DELTA_PERCENT_ONLY,100,110,10,10", "PRD-PRICE-SIM-001,PREVIEW_DELTA_PERCENT_ONLY,100,110,11,10");
  refreshDigest(root, PATHS.csv);
});
mutation("CSV production rounding pin fails", (root) => {
  replaceExact(root, PATHS.csv, "SYNTHETIC-PRICE-UNIT,UNRESOLVED", "SYNTHETIC-PRICE-UNIT,ROUND-v1");
  refreshDigest(root, PATHS.csv);
});
mutation("CSV promotion status cannot become READY", (root) => {
  replaceExact(root, PATHS.csv, ",BLOCKED\n", ",READY\n");
  refreshDigest(root, PATHS.csv);
});
mutation("CSV and manifest fixture parity mismatch fails", (root) => {
  replaceExact(root, PATHS.csv, "PRD-PRICE-SIM-008,PREVIEW_DELTA_PERCENT_ONLY,7.5,8.25,0.75,10", "PRD-PRICE-SIM-008,PREVIEW_DELTA_PERCENT_ONLY,7.5,8.50,1,13.333");
  refreshDigest(root, PATHS.csv);
});
mutation("backticked exact API route invention fails", (root) => {
  write(root, PATHS.service, `${read(root, PATHS.service)}\nPOST \`/api/services\`\n`);
  refreshDigest(root, PATHS.service);
});
mutation("dot-qualified permission invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nProduct.Price.Apply\n`);
  refreshDigest(root, PATHS.price);
});
mutation("rounding mode invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nHALF_UP\n`);
  refreshDigest(root, PATHS.price);
});
mutation("numeric tax rate invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nKDV %20\n`);
  refreshDigest(root, PATHS.price);
});
mutation("FX source invention fails", (root) => {
  write(root, PATHS.price, `${read(root, PATHS.price)}\nFX source TCMB\n`);
  refreshDigest(root, PATHS.price);
});
mutation("manifest source snapshot drift fails", (root) => {
  mutateJson(root, PATHS.manifest, (value) => { value.sourceSnapshot.baseSha = "f".repeat(40); });
});
mutation("artifact inventory cannot shrink", (root) => {
  const value = json(root, PATHS.authority);
  value.artifactInventory.pop();
  writeJson(root, PATHS.authority, value);
});
mutation("artifact digest cannot be forged", (root) => {
  const value = json(root, PATHS.authority);
  value.artifactInventory[0].sha256 = `sha256:${"0".repeat(64)}`;
  writeJson(root, PATHS.authority, value);
});
mutation("authority case count cannot drift", (root) => {
  const value = json(root, PATHS.authority);
  value.scenarioContract.caseCount = 59;
  writeJson(root, PATHS.authority, value);
});
mutation("validator command pin cannot drift", (root) => {
  const value = json(root, PATHS.authority);
  value.requiredCommands[0] = "node some-other-validator.mjs";
  writeJson(root, PATHS.authority, value);
});
mutation("forbidden implementation claim set cannot shrink", (root) => {
  const value = json(root, PATHS.authority);
  value.forbiddenImplementationClaims.pop();
  writeJson(root, PATHS.authority, value);
});
mutation("canonical source drift fails canonical mode", (root) => {
  replaceExact(root, PATHS.canonicalAcceptance, "Excel içe aktarma eşleme/önizleme/onay olmadan kayıt yazmaz.", "Excel içe aktarma kayıt yazabilir.");
}, { standalone: false });
mutation("raw canonical acceptance append fails source pin", (root) => {
  write(root, PATHS.canonicalAcceptance, `${read(root, PATHS.canonicalAcceptance)}\n`);
}, { standalone: false });
mutation("missing decision registry binding fails", (root) => {
  unlinkSync(resolve(root, PATHS.decisionRegistry));
}, { standalone: false });
mutation("missing owner decision binding fails", (root) => {
  unlinkSync(resolve(root, PATHS.ownerDecisions));
}, { standalone: false });
mutation("missing master report binding fails", (root) => {
  unlinkSync(resolve(root, PATHS.masterReport));
}, { standalone: false });
mutation("pinned open decision source byte drift fails", (root) => {
  write(root, PATHS.openDecisions, `${read(root, PATHS.openDecisions)}\n`);
}, { standalone: false });
mutation("pinned Product PDF byte drift fails", (root) => {
  const absolutePath = resolve(root, PATHS.productPdf);
  const original = readFileSync(absolutePath);
  writeFileSync(absolutePath, Buffer.concat([original, Buffer.from([0])]));
}, { standalone: false });
mutation("missing pinned Product PDF fails", (root) => {
  unlinkSync(resolve(root, PATHS.productPdf));
}, { standalone: false });
mutation("canonical screen status source drift fails", (root) => {
  replaceExact(root, PATHS.canonicalScreenStatus, "PRODUCT-H-01", "PRODUCT-H-99");
}, { standalone: false });
mutation("screen catalog mapping status drift fails", (root) => {
  replaceExact(root, PATHS.screenCatalog, ",CANDIDATE_MERGE,[],LOCKED_VISUAL", ",CANDIDATE_EXACT,[],LOCKED_VISUAL");
}, { standalone: false });
mutation("malformed scenarios JSON fails", (root) => {
  write(root, PATHS.scenarios, "{not-json");
  refreshDigest(root, PATHS.scenarios);
});
mutation("unterminated CSV quote fails", (root) => {
  write(root, PATHS.csv, `${read(root, PATHS.csv)}"unterminated`);
  refreshDigest(root, PATHS.csv);
});
