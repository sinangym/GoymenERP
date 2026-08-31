import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { lookup } from "node:dns";
import { mkdir, readFile, mkdtemp, rm } from "node:fs/promises";
import { get as httpGet } from "node:http";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { ModuleKind, ScriptTarget, transpileModule } from "typescript";

import {
  PocError,
  REQUIRED_ADOPTION_BLOCKERS,
  applyProtectedHeaderGuard,
  assertProductionAdoptionAllowed,
  buildOrvalOptions,
  buildRiskReport,
  collectTree,
  compareTrees,
  compileGeneratedOutput,
  generateOnce,
  inspectGeneratedTransport,
  parseCliArguments,
  scanGeneratedOutput,
  validateSupplyChainState,
  validateContractDocument,
} from "../run-poc.mjs";

const TOOL_ROOT = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
);
const REPOSITORY_ROOT = path.resolve(TOOL_ROOT, "..", "..");
const CONTRACT_PATH = path.join(
  REPOSITORY_ROOT,
  "contracts",
  "openapi",
  "goymen-erp-v1.json",
);
const clone = (value) => structuredClone(value);

const loadContract = async () =>
  JSON.parse(await readFile(CONTRACT_PATH, "utf8"));

const importGeneratedSource = async (source) => {
  const javaScript = transpileModule(source, {
    compilerOptions: {
      module: ModuleKind.ESNext,
      target: ScriptTarget.ES2023,
    },
  }).outputText;
  const digest = createHash("sha256").update(javaScript).digest("hex");
  return import(
    `data:text/javascript;base64,${Buffer.from(javaScript).toString("base64")}#${digest}`
  );
};

const typeBlock = (source, name) => {
  const match = new RegExp(
    `export type ${name} = \\{([\\s\\S]*?)\\n\\};`,
    "u",
  ).exec(source);
  assert.ok(match, `Missing generated type ${name}`);
  return match[1];
};

test("lockfile has the reviewed exact toolchain and one install-script allowlist entry", async () => {
  const packageJson = JSON.parse(
    await readFile(path.join(TOOL_ROOT, "package.json"), "utf8"),
  );
  const lock = JSON.parse(
    await readFile(path.join(TOOL_ROOT, "package-lock.json"), "utf8"),
  );
  const lockBytes = await readFile(path.join(TOOL_ROOT, "package-lock.json"));
  const npmConfig = await readFile(path.join(TOOL_ROOT, ".npmrc"), "utf8");

  assert.equal(lock.lockfileVersion, 3);
  assert.deepEqual(packageJson.devDependencies, {
    orval: "8.27.0",
    prettier: "3.6.2",
    typescript: "6.0.2",
  });
  assert.deepEqual(packageJson.scripts, {
    diagnose: "node --import ./deny-network.mjs ./run-poc.mjs",
    poc: "node --import ./deny-network.mjs ./run-poc.mjs --require-adoption",
    test: "node --import ./deny-network.mjs --test ./test/orval-poc.test.mjs",
  });
  assert.equal(npmConfig.replaceAll("\r\n", "\n"), "ignore-scripts=true\n");
  assert.deepEqual(
    lock.packages[""].devDependencies,
    packageJson.devDependencies,
  );
  assert.equal(Object.keys(lock.packages).filter(Boolean).length, 135);
  assert.equal(
    createHash("sha256").update(lockBytes).digest("hex"),
    "e8d1197ae8e8be6056a212a93774e814fe4727d99900edc2267bcf44604a2d42",
  );
  assert.equal(lock.packages["node_modules/orval"].version, "8.27.0");
  assert.equal(
    lock.packages["node_modules/orval"].integrity,
    "sha512-USIGxWy6LuNj3wf217j6EwU14oWWQFVS6ltm45caGkKRV42zrR9rwWxeny3ClOzPBhz2t5+Z/zsVzIP/aZnSVA==",
  );
  assert.equal(lock.packages["node_modules/prettier"].version, "3.6.2");
  assert.equal(lock.packages["node_modules/typescript"].version, "6.0.2");
  assert.equal(lock.packages["node_modules/esbuild"].version, "0.28.2");
  assert.equal(
    lock.packages["node_modules/esbuild"].integrity,
    "sha512-HKVLS8dvII+xoKW9kmqxbRKrnWEXfJJr/FZhhJmiqIB0e053QNYFqOBouTMO/k5sID4MvCiUCvv8b9M4h32wIA==",
  );
  assert.deepEqual(
    Object.entries(lock.packages)
      .filter(([, metadata]) => metadata.hasInstallScript === true)
      .map(([name]) => name),
    ["node_modules/esbuild"],
  );
  for (const [name, metadata] of Object.entries(lock.packages)) {
    if (metadata.resolved !== undefined) {
      assert.match(
        metadata.resolved,
        /^https:\/\/registry\.npmjs\.org\//u,
        `Unapproved package source for ${name}`,
      );
    }
    if (name !== "") {
      assert.match(
        metadata.integrity,
        /^sha512-[A-Za-z0-9+/]+={0,2}$/u,
        `Missing exact integrity for ${name}`,
      );
    }
    assert.notEqual(
      metadata.link,
      true,
      `Linked package is forbidden: ${name}`,
    );
  }

  const manifest = JSON.parse(
    await readFile(path.join(TOOL_ROOT, "poc-manifest.json"), "utf8"),
  );
  assert.equal(
    manifest.toolchain.packageLockSha256,
    "e8d1197ae8e8be6056a212a93774e814fe4727d99900edc2267bcf44604a2d42",
  );
  assert.equal(manifest.adoption.status, "NO_GO");
  assert.deepEqual(manifest.adoption.blockers, REQUIRED_ADOPTION_BLOCKERS);
  for (const blocker of REQUIRED_ADOPTION_BLOCKERS) {
    assert.match(
      manifest.adoption.blockerDetails[blocker],
      /\S/u,
      `Missing blocker detail: ${blocker}`,
    );
  }
  assert.deepEqual(
    Object.keys(manifest.adoption.blockerDetails).sort(),
    [...manifest.adoption.blockers].sort(),
  );
  validateSupplyChainState({
    lock,
    lockBytes,
    manifest,
    npmConfig,
    packageJson,
  });

  const missingIntegrity = structuredClone(lock);
  delete missingIntegrity.packages["node_modules/@orval/fetch"].integrity;
  assert.throws(
    () =>
      validateSupplyChainState({
        lock: missingIntegrity,
        lockBytes,
        manifest,
        npmConfig,
        packageJson,
      }),
    /Unpinned package-lock entry/u,
  );

  const substitutedPostinstall = structuredClone(lock);
  substitutedPostinstall.packages["node_modules/esbuild"].version = "0.0.0";
  assert.throws(
    () =>
      validateSupplyChainState({
        lock: substitutedPostinstall,
        lockBytes,
        manifest,
        npmConfig,
        packageJson,
      }),
    /exact package allowlist/u,
  );
});

test("network-deny preload is active", () => {
  assert.equal(globalThis.__GOYMEN_ORVAL_POC_NETWORK_DENIED__, true);
  assert.throws(() => fetch("https://openapi.invalid/contract.json"), {
    message: "ORVAL_POC_NETWORK_DENIED",
  });
  assert.throws(() => httpGet("http://openapi.invalid/contract.json"), {
    message: "ORVAL_POC_NETWORK_DENIED",
  });
  assert.throws(() => lookup("openapi.invalid", () => {}), {
    message: "ORVAL_POC_NETWORK_DENIED",
  });
  assert.equal(globalThis.__GOYMEN_ORVAL_POC_CHILD_PROCESS_DENIED__, true);
  assert.throws(() => spawnSync(process.execPath, ["-e", "process.exit(0)"]), {
    message: "ORVAL_POC_CHILD_PROCESS_DENIED",
  });
  assert.throws(
    () =>
      globalThis.__GOYMEN_ORVAL_POC_SPAWN_SYNC__(
        process.execPath,
        ["-e", "process.exit(0)"],
        { env: { ...process.env } },
      ),
    { message: "ORVAL_POC_CHILD_PROCESS_DENIED" },
  );
  const spoofRoot = path.join(tmpdir(), "goymen-orval-spoof");
  const compiler = path.join(
    TOOL_ROOT,
    "node_modules",
    "typescript",
    "bin",
    "tsc",
  );
  assert.throws(
    () =>
      globalThis.__GOYMEN_ORVAL_POC_SPAWN_SYNC__(
        path.join(spoofRoot, path.basename(process.execPath)),
        [
          compiler,
          "-p",
          path.join(spoofRoot, "tsconfig.json"),
          "--pretty",
          "false",
        ],
        { cwd: spoofRoot, env: { ...process.env } },
      ),
    { message: "ORVAL_POC_CHILD_PROCESS_DENIED" },
  );
  assert.throws(
    () =>
      globalThis.__GOYMEN_ORVAL_POC_SPAWN_SYNC__("python", [], {
        cwd: REPOSITORY_ROOT,
        env: { ...process.env },
      }),
    { message: "ORVAL_POC_CHILD_PROCESS_DENIED" },
  );
  const allowedEnvironment = new Set([
    "SYSTEMROOT",
    "TEMP",
    "TMP",
    "TMPDIR",
    "WINDIR",
  ]);
  for (const name of Object.keys(process.env)) {
    assert.ok(allowedEnvironment.has(name.toUpperCase()), name);
  }
});

test("CLI keeps diagnostic success separate from production adoption", () => {
  assert.deepEqual(parseCliArguments([]), { requireAdoption: false });
  assert.deepEqual(parseCliArguments(["--require-adoption"]), {
    requireAdoption: true,
  });
  assert.throws(
    () => parseCliArguments(["--unknown"]),
    /Unsupported Orval PoC command-line argument/u,
  );
  assert.throws(
    () => parseCliArguments(["--require-adoption", "--require-adoption"]),
    /Unsupported Orval PoC command-line argument/u,
  );
});

test("canonical preflight is local-only and fail-closed", async () => {
  const canonicalBytes = await readFile(CONTRACT_PATH);
  assert.equal(
    createHash("sha256").update(canonicalBytes).digest("hex"),
    "94c170470c8399284aa8bd905912e343c3a5f1bc9085f1f7da30a28821f17b6a",
  );
  const canonical = await loadContract();
  assert.equal(validateContractDocument(canonical), canonical);

  const unsupported = clone(canonical);
  unsupported.openapi = "3.2.0";
  assert.throws(
    () => validateContractDocument(unsupported),
    /OpenAPI 3\.1\.1/u,
  );

  const server = clone(canonical);
  server.servers = [{ url: "https://openapi.invalid" }];
  assert.throws(
    () => validateContractDocument(server),
    /servers are forbidden/u,
  );

  const externalReference = clone(canonical);
  externalReference.components.schemas.CompanySummary = {
    $ref: "https://openapi.invalid/company.json#/CompanySummary",
  };
  assert.throws(
    () => validateContractDocument(externalReference),
    /External OpenAPI/u,
  );

  const dynamicReference = clone(canonical);
  delete dynamicReference.components.schemas.CompanySummary.properties.code
    .type;
  dynamicReference.components.schemas.CompanySummary.properties.code.$dynamicRef =
    "https://openapi.invalid/company.json#/CompanyCode";
  assert.throws(
    () => validateContractDocument(dynamicReference),
    /\$dynamicRef is unsupported/u,
  );

  const unsupportedSchemaType = clone(canonical);
  unsupportedSchemaType.components.schemas.CompanySummary.properties.code.type =
    "definitely-not-a-type";
  assert.throws(
    () => validateContractDocument(unsupportedSchemaType),
    /Unsupported OpenAPI schema type/u,
  );

  const missingRequiredProperty = clone(canonical);
  missingRequiredProperty.components.schemas.CompanySummary.required.push(
    "doesNotExist",
  );
  assert.throws(
    () => validateContractDocument(missingRequiredProperty),
    /required name is absent from properties/u,
  );

  const duplicateOperation = clone(canonical);
  duplicateOperation.paths["/api/v1/identity/me"].get.operationId =
    duplicateOperation.paths["/api/v1/organization/companies"].get.operationId;
  assert.throws(
    () => validateContractDocument(duplicateOperation),
    /Duplicate OpenAPI/u,
  );

  const missingOperationId = clone(canonical);
  delete missingOperationId.paths["/health/live"].get.operationId;
  assert.throws(
    () => validateContractDocument(missingOperationId),
    /operationId/u,
  );

  const unsafeRoute = clone(canonical);
  unsafeRoute.paths["/../escape"] = unsafeRoute.paths["/health/live"];
  delete unsafeRoute.paths["/health/live"];
  assert.throws(
    () => validateContractDocument(unsafeRoute),
    /Unsafe OpenAPI route/u,
  );

  const injectedDescription = clone(canonical);
  injectedDescription.info.description = "*/ export const injected = true; /*";
  assert.throws(
    () => validateContractDocument(injectedDescription),
    /Unsafe generated-comment/u,
  );

  const injectedEnum = clone(canonical);
  injectedEnum.components.schemas.CompanySummary.properties.code.enum = [
    "safe",
    "${globalThis.injected}",
  ];
  assert.throws(
    () => validateContractDocument(injectedEnum),
    /Unsafe OpenAPI enum/u,
  );

  const injectedDefault = clone(canonical);
  injectedDefault.components.schemas.CompanySummary.properties.code.default =
    "*/ export const injected = true; /*";
  assert.throws(
    () => validateContractDocument(injectedDefault),
    /Unsafe OpenAPI default/u,
  );

  const injectedHeader = clone(canonical);
  injectedHeader.paths["/api/v1/organization/context"].get.parameters.find(
    (parameter) => parameter.name === "X-Company-Id",
  ).name = "X-Company-Id\r\nInjected";
  assert.throws(
    () => validateContractDocument(injectedHeader),
    /Unsafe OpenAPI parameter/u,
  );

  const injectedQuery = clone(canonical);
  injectedQuery.paths["/api/v1/counterparties"].get.parameters.find(
    (parameter) => parameter.name === "q",
  ).name = "q];globalThis.injected";
  assert.throws(
    () => validateContractDocument(injectedQuery),
    /Unsafe OpenAPI parameter/u,
  );
});

test("risk report keeps request-header semantics and blocks production adoption", async () => {
  const report = buildRiskReport(await loadContract());
  assert.equal(report.adoption, "NO_GO");
  assert.deepEqual(report.generatorProfile, {
    client: "fetch",
    headers: true,
    includeHttpResponseReturnType: true,
    pathParametersUrlEncoded: true,
    protectedRequestOptionHeaders: "reject",
    requestInitOverrides: true,
    runtimeValidation: false,
    useBigInt: false,
  });
  const int64 = report.blockers.find(
    (blocker) => blocker.code === "INT64_WIRE_PRECISION_POLICY_REQUIRED",
  );
  assert.equal(int64.count, 12);
  assert.ok(
    int64.fields.some((field) => field.endsWith(".properties.version")),
  );
  assert.ok(
    report.blockers.some(
      (blocker) => blocker.code === "RUNTIME_RESPONSE_DECODER_REQUIRED",
    ),
  );
  assert.ok(
    report.blockers.some(
      (blocker) => blocker.code === "FULL_OPENAPI_SEMANTIC_VALIDATION_REQUIRED",
    ),
  );
  assert.equal(
    report.blockers.some(
      (blocker) => blocker.code === "PROTECTED_HEADER_OVERRIDE_GUARD_REQUIRED",
    ),
    false,
  );

  const context = report.operations.find(
    (operation) => operation.operationId === "GetOrganizationContextV1",
  );
  const list = report.operations.find(
    (operation) => operation.operationId === "GetCounterpartiesV1",
  );
  const create = report.operations.find(
    (operation) => operation.operationId === "CreateCounterpartyV1",
  );
  assert.equal(
    context.requestHeaders.find((header) => header.name === "X-Company-Id")
      .required,
    true,
  );
  assert.equal(
    list.requestHeaders.find((header) => header.name === "X-Company-Id")
      .required,
    false,
  );
  assert.equal(
    create.requestHeaders.find((header) => header.name === "Idempotency-Key")
      .required,
    true,
  );
  assert.ok(
    create.responseHeaders.some(
      (header) =>
        header.status === "201" && header.name === "Idempotency-Replayed",
    ),
  );
});

test("programmatic Orval profile disables discovery and executable extensions", async () => {
  const document = await loadContract();
  const options = buildOrvalOptions(document, "C:/temporary-output/client.ts");
  assert.equal(options.input.target, document);
  assert.equal(options.input.unsafeDisableValidation, false);
  assert.deepEqual(options.input.parserOptions.externalRefs.allow, []);
  assert.equal(Object.hasOwn(options, "hooks"), false);
  assert.equal(options.output.client, "fetch");
  assert.equal(options.output.httpClient, "fetch");
  assert.equal(options.output.headers, true);
  assert.equal(options.output.urlEncodeParameters, true);
  assert.equal(options.output.clean, false);
  assert.equal(options.output.mock, false);
  assert.equal(options.output.docs, false);
  assert.equal(Object.hasOwn(options.output, "formatter"), false);
  assert.equal(options.output.override.header, false);
  assert.equal(options.output.override.requestOptions, undefined);
  assert.equal(options.output.override.transformer, undefined);
  assert.equal(options.output.override.mutator, undefined);
  assert.equal(options.output.override.useBigInt, false);
  assert.deepEqual(options.output.override.fetch, {
    includeHttpResponseReturnType: true,
    forceSuccessResponse: false,
    serializeResponseHeaders: false,
    runtimeValidation: false,
    useRuntimeFetcher: false,
  });
});

test("AST-validated sanitizer turns the raw header override red path green", async () => {
  const canonical = await loadContract();
  const rawRoot = await mkdtemp(path.join(tmpdir(), "goymen-orval-raw-red-"));
  try {
    const generatedRoot = path.join(rawRoot, "generated");
    await mkdir(generatedRoot);
    const target = path.join(generatedRoot, "client.ts");
    const { generate } = await import("orval");
    await generate(buildOrvalOptions(canonical, target), TOOL_ROOT, {
      throwOnError: true,
      failOnWarnings: true,
    });
    const rawSource = await readFile(target, "utf8");
    const rawInspection = inspectGeneratedTransport(rawSource, canonical);
    assert.equal(rawInspection.requestInitHeadersOverrideTypedHeaders, true);
    assert.equal(rawInspection.requestInitProtectedHeadersRejected, false);

    const guardedSource = applyProtectedHeaderGuard(rawSource, canonical);
    const guardedInspection = inspectGeneratedTransport(
      guardedSource,
      canonical,
    );
    assert.equal(
      guardedInspection.requestInitHeadersOverrideTypedHeaders,
      false,
    );
    assert.equal(guardedInspection.requestInitProtectedHeadersRejected, true);
    assert.equal(guardedInspection.protectedHeaderGuardCount, 16);
    assert.equal(guardedInspection.requestInitMethodBodyRejected, true);
    assert.equal(guardedInspection.requestOptionsGuardCount, 16);
    assert.equal(guardedInspection.typedHeaderGuardCount, 16);

    const driftedSource = rawSource.replace(
      "...headers, ...getHeaders(options?.headers)",
      "...({ ...headers }), ...getHeaders(options?.headers)",
    );
    assert.notEqual(driftedSource, rawSource);
    assert.throws(
      () => applyProtectedHeaderGuard(driftedSource, canonical),
      /fetch-header property inventory drifted/u,
    );

    const livenessFetch = `const res = await fetch(getGetLivenessV1Url(),
  {
    ...options,
    method: 'GET',
    headers: { ...headers, ...getHeaders(options?.headers) }

  }
)`;
    const decoyLivenessFetch = `if (false) {
  ({ headers: { ...headers, ...getHeaders(options?.headers) } });
}
const res = await fetch(getGetLivenessV1Url(),
  {
    ...options,
    method: 'GET',

  }
)`;
    const decoySource = rawSource.replace(livenessFetch, decoyLivenessFetch);
    assert.notEqual(decoySource, rawSource);
    assert.throws(
      () => applyProtectedHeaderGuard(decoySource, canonical),
      /fetch contract initializer drifted/u,
    );

    const shadowedSource = rawSource.replace(
      livenessFetch,
      `{
  const getHeaders = (value?: NonNullable<RequestInit['headers']>) => value ?? {};
  ${livenessFetch}
}`,
    );
    assert.notEqual(shadowedSource, rawSource);
    assert.throws(
      () => applyProtectedHeaderGuard(shadowedSource, canonical),
      /fetch lexical binding drifted/u,
    );

    const appendedHeaderSource = rawSource.replace(
      "headers: { ...headers, ...getHeaders(options?.headers) }",
      "headers: { ...headers, ...getHeaders(options?.headers), ...({ 'X-Correlation-Id': 'override' }) }",
    );
    assert.notEqual(appendedHeaderSource, rawSource);
    assert.throws(
      () => applyProtectedHeaderGuard(appendedHeaderSource, canonical),
      /fetch-header property inventory drifted/u,
    );
  } finally {
    await rm(rawRoot, { recursive: true, force: true });
  }
});

test("requestOptions=false remains a compile-failing Orval regression", async () => {
  const canonical = await loadContract();
  const rawRoot = await mkdtemp(
    path.join(tmpdir(), "goymen-orval-no-request-options-"),
  );
  try {
    const generatedRoot = path.join(rawRoot, "generated");
    await mkdir(generatedRoot);
    const target = path.join(generatedRoot, "client.ts");
    const options = buildOrvalOptions(canonical, target);
    options.output.override.requestOptions = false;
    const { generate } = await import("orval");
    await generate(options, TOOL_ROOT, {
      throwOnError: true,
      failOnWarnings: true,
    });
    const source = await readFile(target, "utf8");
    assert.equal(source.includes("options?: RequestInit"), false);
    assert.equal(source.split("getHeaders(options?.headers)").length - 1, 16);
    await assert.rejects(
      compileGeneratedOutput(rawRoot),
      /strict TypeScript compilation/u,
    );
  } finally {
    await rm(rawRoot, { recursive: true, force: true });
  }
});

test("canonical generation is deterministic, sensitive-free and strict-TypeScript clean", async () => {
  const canonical = await loadContract();
  const firstRoot = await mkdtemp(path.join(tmpdir(), "goymen-orval-test-a-"));
  const secondRoot = await mkdtemp(path.join(tmpdir(), "goymen-orval-test-b-"));
  try {
    const firstGenerated = await generateOnce(canonical, firstRoot);
    const secondGenerated = await generateOnce(canonical, secondRoot);
    const firstTree = await collectTree(firstGenerated);
    const secondTree = await collectTree(secondGenerated);
    compareTrees(firstTree, secondTree);
    const manuallyChangedTree = new Map(firstTree);
    manuallyChangedTree.set(
      "client.ts",
      Buffer.concat([
        manuallyChangedTree.get("client.ts"),
        Buffer.from("\n// manual diff\n"),
      ]),
    );
    assert.throws(() => compareTrees(firstTree, manuallyChangedTree));
    scanGeneratedOutput(firstTree, [REPOSITORY_ROOT, firstRoot, secondRoot]);
    await compileGeneratedOutput(firstRoot);
    await compileGeneratedOutput(secondRoot);

    assert.deepEqual([...firstTree.keys()], ["client.ts"]);
    const source = firstTree.get("client.ts").toString("utf8");
    assert.match(
      typeBlock(source, "GetOrganizationContextV1Headers"),
      /'X-Company-Id': string;/u,
    );
    assert.match(
      typeBlock(source, "GetCounterpartiesV1Headers"),
      /'X-Company-Id'\?: string;/u,
    );
    assert.match(
      typeBlock(source, "CreateCounterpartyV1Headers"),
      /'Idempotency-Key': string;/u,
    );
    assert.match(source, /headers: Headers;/u);
    assert.match(source, /JSON\.parse\(body\)/u);
    assert.match(source, /version: number;/u);
    const transportInspection = inspectGeneratedTransport(source, canonical);
    const expectedEncodedPathParameterCount = buildRiskReport(
      canonical,
    ).operations.reduce(
      (count, operation) =>
        count + (operation.route.match(/\{[^{}]+\}/gu)?.length ?? 0),
      0,
    );
    assert.ok(
      transportInspection.encodedPathParameterCount >=
        expectedEncodedPathParameterCount,
    );
    assert.equal(
      transportInspection.expectedEncodedPathParameterCount,
      expectedEncodedPathParameterCount,
    );
    assert.deepEqual(transportInspection.rawPathParameterInterpolations, []);
    assert.equal(transportInspection.allPathParametersUrlEncoded, true);
    assert.equal(transportInspection.queryParametersUseUrlSearchParams, true);
    assert.deepEqual(transportInspection.protectedHeadersPresent, [
      "X-Company-Id",
      "Idempotency-Key",
      "X-Correlation-Id",
      "X-Causation-Id",
    ]);
    assert.equal(
      transportInspection.requestInitHeadersOverrideTypedHeaders,
      false,
    );
    assert.equal(transportInspection.requestInitHeaderMergeCount, 16);
    assert.equal(transportInspection.protectedHeaderGuardCount, 16);
    assert.equal(transportInspection.requestInitMethodBodyRejected, true);
    assert.equal(transportInspection.requestOptionsGuardCount, 16);
    assert.equal(transportInspection.typedHeaderGuardCount, 16);
    assert.equal(transportInspection.requestInitProtectedHeadersRejected, true);
    assert.doesNotThrow(() =>
      assertProductionAdoptionAllowed({
        manifestAdoption: {
          status: "GO",
          blockers: [],
        },
        riskReport: { adoption: "GO", blockers: [] },
        transportInspection,
      }),
    );
    assert.throws(
      () =>
        assertProductionAdoptionAllowed({
          manifestAdoption: { status: "GO", blockers: [] },
          riskReport: { adoption: "GO", blockers: [] },
          transportInspection: {
            requestInitHeadersOverrideTypedHeaders: true,
            requestInitMethodBodyRejected: true,
            requestInitProtectedHeadersRejected: false,
          },
        }),
      /protected RequestInit headers are not rejected/u,
    );
    assert.throws(
      () =>
        assertProductionAdoptionAllowed({
          manifestAdoption: { status: "GO", blockers: [] },
          riskReport: { adoption: "GO", blockers: [] },
          transportInspection: {
            requestInitHeadersOverrideTypedHeaders: false,
            requestInitMethodBodyRejected: false,
            requestInitProtectedHeadersRejected: true,
          },
        }),
      /generic RequestInit method or body is not rejected/u,
    );
    assert.throws(
      () =>
        assertProductionAdoptionAllowed({
          manifestAdoption: {
            status: "NO_GO",
            blockers: ["CROSS_OS_DETERMINISM_REQUIRED"],
          },
          riskReport: { adoption: "GO", blockers: [] },
          transportInspection: {
            requestInitHeadersOverrideTypedHeaders: false,
            requestInitMethodBodyRejected: true,
            requestInitProtectedHeadersRejected: true,
          },
        }),
      /fail-closed profile has unresolved blockers/u,
    );
  } finally {
    await Promise.all([
      rm(firstRoot, { recursive: true, force: true }),
      rm(secondRoot, { recursive: true, force: true }),
    ]);
  }
});

test("generated transport rejects contract-controlled RequestInit headers at runtime", async () => {
  const canonical = await loadContract();
  const runRoot = await mkdtemp(
    path.join(tmpdir(), "goymen-orval-runtime-guard-"),
  );
  const originalFetch = globalThis.fetch;
  try {
    const generatedRoot = await generateOnce(canonical, runRoot);
    const source = await readFile(
      path.join(generatedRoot, "client.ts"),
      "utf8",
    );
    const generated = await importGeneratedSource(source);
    const calls = [];
    globalThis.fetch = async (url, init) => {
      calls.push({ url, init });
      return new Response("{}", {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    };

    const typedHeaders = {
      "X-Company-Id": "11111111-1111-1111-1111-111111111111",
      "X-Correlation-Id": "correlation-safe",
    };
    const protectedInputs = [
      { headers: { "x-company-id": "override" } },
      { headers: new Headers({ "IDEMPOTENCY-KEY": "override" }) },
      { headers: [["x-correlation-id", "override"]] },
      { headers: { "X-Causation-Id": "override" } },
      { headers: { Accept: "text/plain" } },
      { headers: { "Content-Type": "text/plain" } },
      { headers: { "X-Client-Trace": ["not", "a", "string"] } },
    ];
    for (const options of protectedInputs) {
      await assert.rejects(
        generated.getOrganizationContextV1(typedHeaders, options),
        /Protected request headers must use generated typed parameters/u,
      );
    }
    await assert.rejects(
      generated.getOrganizationContextV1(undefined, {}),
      /Generated typed request headers do not match the OpenAPI contract/u,
    );
    await assert.rejects(
      generated.getOrganizationContextV1(
        {
          "X-Company-Id": "11111111-1111-1111-1111-111111111111",
          "x-company-id": "override",
        },
        {},
      ),
      /Generated typed request headers do not match the OpenAPI contract/u,
    );
    await assert.rejects(
      generated.getOrganizationContextV1(
        {
          "X-Company-Id": "11111111-1111-1111-1111-111111111111",
          "X-Unknown": "override",
        },
        {},
      ),
      /Generated typed request headers do not match the OpenAPI contract/u,
    );
    await assert.rejects(
      generated.getOrganizationContextV1(
        new Headers({ "X-Company-Id": "override" }),
        {},
      ),
      /Generated typed request headers do not match the OpenAPI contract/u,
    );
    await assert.rejects(
      generated.getOrganizationContextV1(typedHeaders, {
        body: "attacker-body",
      }),
      /Generic request options cannot set the generated method or body/u,
    );
    await assert.rejects(
      generated.getOrganizationContextV1(typedHeaders, {
        method: "DELETE",
      }),
      /Generic request options cannot set the generated method or body/u,
    );
    assert.equal(calls.length, 0);

    const controller = new AbortController();
    await generated.getOrganizationContextV1(typedHeaders, {
      headers: { "X-Client-Trace": "allowed" },
      signal: controller.signal,
    });
    assert.equal(calls.length, 1);
    assert.equal(calls[0].init.signal, controller.signal);
    const actualHeaders = new Headers(calls[0].init.headers);
    assert.equal(
      actualHeaders.get("X-Company-Id"),
      "11111111-1111-1111-1111-111111111111",
    );
    assert.equal(actualHeaders.get("X-Correlation-Id"), "correlation-safe");
    assert.equal(actualHeaders.get("X-Client-Trace"), "allowed");

    let proxyOwnKeysCount = 0;
    const statefulHeaders = new Proxy(
      {
        "X-Client-Trace": "proxy-safe",
        "X-Company-Id": "attacker-company",
      },
      {
        ownKeys() {
          proxyOwnKeysCount += 1;
          return proxyOwnKeysCount <= 2 ? ["X-Client-Trace"] : ["X-Company-Id"];
        },
      },
    );
    await generated.getOrganizationContextV1(typedHeaders, {
      headers: statefulHeaders,
    });
    assert.equal(proxyOwnKeysCount, 1);
    assert.equal(calls.length, 2);
    const statefulProxyHeaders = new Headers(calls[1].init.headers);
    assert.equal(
      statefulProxyHeaders.get("X-Company-Id"),
      "11111111-1111-1111-1111-111111111111",
    );
    assert.equal(statefulProxyHeaders.get("X-Client-Trace"), "proxy-safe");

    await generated.getCounterpartiesV1(undefined, undefined, {
      headers: { "X-Client-Trace": "optional-company-allowed" },
    });
    assert.equal(calls.length, 3);
    const optionalCompanyHeaders = new Headers(calls[2].init.headers);
    assert.equal(optionalCompanyHeaders.has("X-Company-Id"), false);

    await assert.rejects(
      generated.createCounterpartyV1(
        {},
        {
          "X-Company-Id": "11111111-1111-1111-1111-111111111111",
          "Idempotency-Key": "idempotency-safe",
        },
        {
          method: "DELETE",
          body: "attacker-body",
          headers: { "X-Client-Trace": "create-allowed" },
        },
      ),
      /Generic request options cannot set the generated method or body/u,
    );
    assert.equal(calls.length, 3);

    await generated.createCounterpartyV1(
      {},
      {
        "X-Company-Id": "11111111-1111-1111-1111-111111111111",
        "Idempotency-Key": "idempotency-safe",
      },
      {
        headers: { "X-Client-Trace": "create-allowed" },
      },
    );
    assert.equal(calls.length, 4);
    assert.equal(calls[3].init.method, "POST");
    assert.equal(calls[3].init.body, "{}");
    const createHeaders = new Headers(calls[3].init.headers);
    assert.equal(createHeaders.get("Content-Type"), "application/json");
    assert.equal(createHeaders.get("Idempotency-Key"), "idempotency-safe");

    assert.throws(
      () => applyProtectedHeaderGuard(source, canonical),
      /precondition failed closed/u,
    );
    assert.throws(
      () => applyProtectedHeaderGuard("generator drift", canonical),
      /precondition failed closed/u,
    );
  } finally {
    globalThis.fetch = originalFetch;
    await rm(runRoot, { recursive: true, force: true });
  }
});

test("required-header and int64 mutations change generated semantics", async () => {
  const canonical = await loadContract();
  const optionalHeader = clone(canonical);
  const contextHeader = optionalHeader.paths[
    "/api/v1/organization/context"
  ].get.parameters.find(
    (parameter) =>
      parameter.in === "header" && parameter.name === "X-Company-Id",
  );
  contextHeader.required = false;

  const stringVersion = clone(canonical);
  stringVersion.components.schemas.CompanyPartyAccountView.properties.version =
    {
      type: "string",
    };

  const baselineRoot = await mkdtemp(
    path.join(tmpdir(), "goymen-orval-mutation-base-"),
  );
  const headerRoot = await mkdtemp(
    path.join(tmpdir(), "goymen-orval-mutation-header-"),
  );
  const int64Root = await mkdtemp(
    path.join(tmpdir(), "goymen-orval-mutation-int64-"),
  );
  try {
    const baseline = await collectTree(
      await generateOnce(canonical, baselineRoot),
    );
    const headerMutation = await collectTree(
      await generateOnce(optionalHeader, headerRoot),
    );
    const int64Mutation = await collectTree(
      await generateOnce(stringVersion, int64Root),
    );
    assert.notDeepEqual(
      baseline.get("client.ts"),
      headerMutation.get("client.ts"),
    );
    assert.notDeepEqual(
      baseline.get("client.ts"),
      int64Mutation.get("client.ts"),
    );

    const headerSource = headerMutation.get("client.ts").toString("utf8");
    assert.match(
      typeBlock(headerSource, "GetOrganizationContextV1Headers"),
      /'X-Company-Id'\?: string;/u,
    );
    const versionSource = int64Mutation.get("client.ts").toString("utf8");
    const accountInterface =
      /export interface CompanyPartyAccountView \{([\s\S]*?)\n\}/u.exec(
        versionSource,
      );
    assert.ok(accountInterface);
    assert.match(accountInterface[1], /version: string;/u);
  } finally {
    await Promise.all([
      rm(baselineRoot, { recursive: true, force: true }),
      rm(headerRoot, { recursive: true, force: true }),
      rm(int64Root, { recursive: true, force: true }),
    ]);
  }
});

test("sensitive scanner rejects host paths and credentials", () => {
  const tree = new Map([
    [
      "client.ts",
      Buffer.from("export const value = 'Server=db;Password=secret';"),
    ],
  ]);
  assert.throws(
    () => scanGeneratedOutput(tree),
    (error) =>
      error instanceof PocError && /sensitive-content/u.test(error.message),
  );
});
