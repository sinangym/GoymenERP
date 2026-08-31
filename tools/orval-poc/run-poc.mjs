import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  lstat,
  mkdtemp,
  mkdir,
  readFile,
  readdir,
  rm,
  writeFile,
} from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const TOOL_ROOT = path.dirname(fileURLToPath(import.meta.url));
const REPOSITORY_ROOT = path.resolve(TOOL_ROOT, "..", "..");
const MANIFEST_PATH = path.join(TOOL_ROOT, "poc-manifest.json");
const PACKAGE_PATH = path.join(TOOL_ROOT, "package.json");
const PACKAGE_LOCK_PATH = path.join(TOOL_ROOT, "package-lock.json");
const NPM_CONFIG_PATH = path.join(TOOL_ROOT, ".npmrc");
const GENERATOR_TSCONFIG_PATH = path.join(TOOL_ROOT, "tsconfig.generator.json");
const EXPECTED_PACKAGE_LOCK_SHA256 =
  "e8d1197ae8e8be6056a212a93774e814fe4727d99900edc2267bcf44604a2d42";
const EXPECTED_ESBUILD = {
  version: "0.28.2",
  integrity:
    "sha512-HKVLS8dvII+xoKW9kmqxbRKrnWEXfJJr/FZhhJmiqIB0e053QNYFqOBouTMO/k5sID4MvCiUCvv8b9M4h32wIA==",
};
const EXPECTED_DEPENDENCIES = {
  orval: "8.27.0",
  prettier: "3.6.2",
  typescript: "6.0.2",
};
const EXPECTED_ORVAL_INTEGRITY =
  "sha512-USIGxWy6LuNj3wf217j6EwU14oWWQFVS6ltm45caGkKRV42zrR9rwWxeny3ClOzPBhz2t5+Z/zsVzIP/aZnSVA==";
export const REQUIRED_ADOPTION_BLOCKERS = Object.freeze([
  "INT64_WIRE_PRECISION_POLICY_REQUIRED",
  "RUNTIME_RESPONSE_DECODER_REQUIRED",
  "FULL_OPENAPI_SEMANTIC_VALIDATION_REQUIRED",
  "PACKAGE_LICENSE_PROVENANCE_REVIEW_REQUIRED",
  "NETWORK_ISOLATED_CI_REQUIRED",
  "EXACT_NODE_PATCH_CI_REQUIRED",
  "OS_CONTAINER_SANDBOX_REQUIRED",
  "READ_ONLY_REPOSITORY_SANDBOX_REQUIRED",
  "SECRETLESS_ENVIRONMENT_SANDBOX_REQUIRED",
  "OS_CHILD_PROCESS_SANDBOX_REQUIRED",
  "CROSS_OS_DETERMINISM_REQUIRED",
  "REACT19_VITE8_BROWSER_SMOKE_REQUIRED",
  "MANUAL_DIFF_AND_MUTATION_CI_REQUIRED",
]);
const PROTECTED_HEADER_GUARD_MARKER =
  "Protected request headers must use generated typed parameters.";
const TYPED_HEADER_GUARD_MARKER =
  "Generated typed request headers do not match the OpenAPI contract.";
const TYPED_HEADER_GUARD_FUNCTION = "goymenValidateTypedRequestHeaders";
const REQUEST_OPTIONS_GUARD_MARKER =
  "Generic request options cannot set the generated method or body.";
const REQUEST_OPTIONS_GUARD_FUNCTION = "goymenValidateRequestOptions";
const GUARDED_REQUEST_HEADERS_HELPER_SIGNATURE =
  "const getHeaders = (h?: NonNullable<RequestInit['headers']>): Record<string, string> => {";
const MAX_FILE_BYTES = 16 * 1024 * 1024;
const MAX_TREE_BYTES = 64 * 1024 * 1024;
const HTTP_METHODS = new Set([
  "delete",
  "get",
  "head",
  "options",
  "patch",
  "post",
  "put",
  "trace",
]);
const JSON_SCHEMA_TYPES = new Set([
  "array",
  "boolean",
  "integer",
  "null",
  "number",
  "object",
  "string",
]);

export class PocError extends Error {}

const sha256 = (content) => createHash("sha256").update(content).digest("hex");

const allowlistedSpawnSync = (...arguments_) => {
  const broker = globalThis.__GOYMEN_ORVAL_POC_SPAWN_SYNC__;
  if (typeof broker !== "function") {
    throw new PocError("The child-process allowlist preload is required.");
  }
  return broker(...arguments_);
};

const childEnvironment = () => ({ ...process.env });

const isObject = (value) =>
  value !== null && typeof value === "object" && !Array.isArray(value);

const isWithin = (root, candidate) => {
  const relative = path.relative(root, candidate);
  return (
    relative === "" ||
    (!relative.startsWith(`..${path.sep}`) &&
      relative !== ".." &&
      !path.isAbsolute(relative))
  );
};

const assertExactBlockerInventory = (adoption) => {
  if (
    adoption?.status !== "NO_GO" ||
    !Array.isArray(adoption.blockers) ||
    adoption.blockers.length !== REQUIRED_ADOPTION_BLOCKERS.length ||
    adoption.blockers.some(
      (code, index) => code !== REQUIRED_ADOPTION_BLOCKERS[index],
    ) ||
    !isObject(adoption.blockerDetails) ||
    Object.keys(adoption.blockerDetails).length !==
      REQUIRED_ADOPTION_BLOCKERS.length ||
    REQUIRED_ADOPTION_BLOCKERS.some(
      (code) =>
        typeof adoption.blockerDetails[code] !== "string" ||
        adoption.blockerDetails[code].trim() === "",
    )
  ) {
    throw new PocError("PoC adoption blocker inventory failed closed.");
  }
};

export const validateSupplyChainState = ({
  lock,
  lockBytes,
  manifest,
  npmConfig,
  packageJson,
}) => {
  if (
    sha256(lockBytes) !== EXPECTED_PACKAGE_LOCK_SHA256 ||
    manifest?.toolchain?.packageLockSha256 !== EXPECTED_PACKAGE_LOCK_SHA256 ||
    npmConfig.replaceAll("\r\n", "\n") !== "ignore-scripts=true\n" ||
    lock?.lockfileVersion !== 3 ||
    !isObject(lock?.packages) ||
    Object.keys(lock.packages).filter(Boolean).length !== 135 ||
    JSON.stringify(packageJson?.devDependencies) !==
      JSON.stringify(EXPECTED_DEPENDENCIES) ||
    JSON.stringify(lock.packages[""]?.devDependencies) !==
      JSON.stringify(EXPECTED_DEPENDENCIES)
  ) {
    throw new PocError("PoC supply-chain preflight failed closed.");
  }

  const installedEntries = Object.entries(lock.packages).filter(
    ([name]) => name !== "",
  );
  for (const [name, metadata] of installedEntries) {
    if (
      !isObject(metadata) ||
      typeof metadata.version !== "string" ||
      metadata.version === "" ||
      typeof metadata.resolved !== "string" ||
      !metadata.resolved.startsWith("https://registry.npmjs.org/") ||
      typeof metadata.integrity !== "string" ||
      !/^sha512-[A-Za-z0-9+/]+={0,2}$/u.test(metadata.integrity) ||
      metadata.link === true
    ) {
      throw new PocError(`Unpinned package-lock entry: ${name}.`);
    }
  }

  const installScriptEntries = installedEntries
    .filter(([, metadata]) => metadata.hasInstallScript === true)
    .map(([name]) => name);
  const esbuild = lock.packages["node_modules/esbuild"];
  const orval = lock.packages["node_modules/orval"];
  if (
    installScriptEntries.length !== 1 ||
    installScriptEntries[0] !== "node_modules/esbuild" ||
    esbuild?.version !== EXPECTED_ESBUILD.version ||
    esbuild?.integrity !== EXPECTED_ESBUILD.integrity ||
    orval?.version !== EXPECTED_DEPENDENCIES.orval ||
    orval?.integrity !== EXPECTED_ORVAL_INTEGRITY ||
    lock.packages["node_modules/prettier"]?.version !==
      EXPECTED_DEPENDENCIES.prettier ||
    lock.packages["node_modules/typescript"]?.version !==
      EXPECTED_DEPENDENCIES.typescript
  ) {
    throw new PocError("PoC exact package allowlist failed closed.");
  }
};

const walkDocument = (value, visitor, jsonPath = "$") => {
  visitor(value, jsonPath);
  if (Array.isArray(value)) {
    value.forEach((item, index) =>
      walkDocument(item, visitor, `${jsonPath}[${index}]`),
    );
    return;
  }
  if (!isObject(value)) {
    return;
  }
  for (const [key, child] of Object.entries(value)) {
    walkDocument(child, visitor, `${jsonPath}.${key}`);
  }
};

const assertRegularFile = async (filePath, root, label) => {
  const absolute = path.resolve(filePath);
  if (!isWithin(root, absolute)) {
    throw new PocError(`${label} must remain inside the repository.`);
  }
  const relative = path.relative(root, absolute);
  if (relative.split(path.sep).includes("..")) {
    throw new PocError(`${label} path traversal is forbidden.`);
  }
  let current = path.parse(absolute).root;
  for (const component of absolute.slice(current.length).split(path.sep)) {
    if (!component) continue;
    current = path.join(current, component);
    const metadata = await lstat(current);
    if (metadata.isSymbolicLink()) {
      throw new PocError(`${label} must not traverse a symbolic link.`);
    }
  }
  const metadata = await lstat(absolute);
  if (
    !metadata.isFile() ||
    metadata.size === 0 ||
    metadata.size > MAX_FILE_BYTES
  ) {
    throw new PocError(`${label} must be a non-empty bounded regular file.`);
  }
  return absolute;
};

const resolveLocalReference = (document, reference) => {
  if (!reference.startsWith("#/")) {
    throw new PocError("External OpenAPI references are forbidden.");
  }
  let current = document;
  for (const token of reference.slice(2).split("/")) {
    const decoded = token.replaceAll("~1", "/").replaceAll("~0", "~");
    if (!isObject(current) && !Array.isArray(current)) {
      throw new PocError("OpenAPI local reference cannot be resolved.");
    }
    current = current[decoded];
    if (current === undefined) {
      throw new PocError("OpenAPI local reference cannot be resolved.");
    }
  }
  return current;
};

const operationEntries = (document) => {
  const entries = [];
  for (const [route, pathItem] of Object.entries(document.paths ?? {})) {
    if (!isObject(pathItem)) continue;
    for (const [method, operation] of Object.entries(pathItem)) {
      if (HTTP_METHODS.has(method.toLowerCase()) && isObject(operation)) {
        entries.push({
          route,
          method: method.toLowerCase(),
          operation,
          pathItem,
        });
      }
    }
  }
  return entries;
};

const mergedParameters = (document, pathItem, operation) => {
  const parameters = new Map();
  for (const candidate of [
    ...(Array.isArray(pathItem.parameters) ? pathItem.parameters : []),
    ...(Array.isArray(operation.parameters) ? operation.parameters : []),
  ]) {
    const parameter = candidate?.$ref
      ? resolveLocalReference(document, candidate.$ref)
      : candidate;
    if (!isObject(parameter)) continue;
    parameters.set(
      `${parameter.in}:${String(parameter.name).toLowerCase()}`,
      parameter,
    );
  }
  return [...parameters.values()];
};

export const validateContractDocument = (document) => {
  if (!isObject(document)) {
    throw new PocError("OpenAPI root must be an object.");
  }
  if (document.openapi !== "3.1.1") {
    throw new PocError("Only the reviewed OpenAPI 3.1.1 input is allowed.");
  }
  if (Object.hasOwn(document, "servers")) {
    throw new PocError("OpenAPI servers are forbidden in this local-only PoC.");
  }
  if (!isObject(document.paths) || Object.keys(document.paths).length === 0) {
    throw new PocError("OpenAPI paths must be a non-empty object.");
  }
  walkDocument(document, (value, jsonPath) => {
    if (!isObject(value)) return;
    if (Object.hasOwn(value, "servers")) {
      throw new PocError(
        `Nested OpenAPI servers are forbidden at ${jsonPath}.`,
      );
    }
    if (typeof value.$ref === "string") {
      resolveLocalReference(document, value.$ref);
    }
    if (Object.hasOwn(value, "$dynamicRef")) {
      throw new PocError(`OpenAPI $dynamicRef is unsupported at ${jsonPath}.`);
    }
    for (const key of ["description", "summary", "title"]) {
      const text = value[key];
      if (
        typeof text === "string" &&
        (/\*\//u.test(text) ||
          /sourceMappingURL/iu.test(text) ||
          /[\u0000\u2028\u2029]/u.test(text))
      ) {
        throw new PocError(
          `Unsafe generated-comment input at ${jsonPath}.${key}.`,
        );
      }
    }
    if (
      typeof value.in === "string" &&
      ["cookie", "header", "path", "query"].includes(value.in) &&
      (typeof value.name !== "string" ||
        !/^[A-Za-z][A-Za-z0-9._-]{0,127}$/u.test(value.name))
    ) {
      throw new PocError(`Unsafe OpenAPI parameter name at ${jsonPath}.`);
    }
    const assertSafeLiteral = (literal, label) => {
      if (typeof literal !== "string") return;
      if (
        literal.length > 1024 ||
        /\*\/|sourceMappingURL|<\/script|[`$]\{|[\u0000-\u001f\u2028\u2029]/iu.test(
          literal,
        )
      ) {
        throw new PocError(`Unsafe OpenAPI ${label} literal at ${jsonPath}.`);
      }
    };
    if (Array.isArray(value.enum)) {
      value.enum.forEach((literal) => assertSafeLiteral(literal, "enum"));
    }
    if (Object.hasOwn(value, "default")) {
      if (
        value.default !== null &&
        !["boolean", "number", "string"].includes(typeof value.default)
      ) {
        throw new PocError(
          `Complex OpenAPI default is forbidden at ${jsonPath}.`,
        );
      }
      assertSafeLiteral(value.default, "default");
    }
  });

  const visitedSchemas = new Set();
  const validateGeneratorSchema = (schema, jsonPath) => {
    if (typeof schema === "boolean") return;
    if (!isObject(schema)) {
      throw new PocError(`OpenAPI schema must be an object at ${jsonPath}.`);
    }
    if (visitedSchemas.has(schema)) return;
    visitedSchemas.add(schema);

    if (Object.hasOwn(schema, "type")) {
      const schemaTypes = Array.isArray(schema.type)
        ? schema.type
        : [schema.type];
      if (
        schemaTypes.length === 0 ||
        schemaTypes.some(
          (type) => typeof type !== "string" || !JSON_SCHEMA_TYPES.has(type),
        ) ||
        new Set(schemaTypes).size !== schemaTypes.length
      ) {
        throw new PocError(`Unsupported OpenAPI schema type at ${jsonPath}.`);
      }
    }

    const objectSchema =
      schema.type === "object" ||
      (Array.isArray(schema.type) && schema.type.includes("object"));
    if (
      Array.isArray(schema.required) &&
      (Object.hasOwn(schema, "properties") || objectSchema)
    ) {
      if (!isObject(schema.properties)) {
        throw new PocError(
          `OpenAPI object schema with required names must declare properties at ${jsonPath}.`,
        );
      }
      const missingRequiredName = schema.required.find(
        (name) =>
          typeof name !== "string" ||
          name.length === 0 ||
          !Object.hasOwn(schema.properties, name),
      );
      if (missingRequiredName !== undefined) {
        throw new PocError(
          `OpenAPI required name is absent from properties at ${jsonPath}.`,
        );
      }
    }

    for (const key of [
      "$defs",
      "definitions",
      "dependentSchemas",
      "patternProperties",
      "properties",
    ]) {
      if (schema[key] === undefined) continue;
      if (!isObject(schema[key])) {
        throw new PocError(
          `OpenAPI schema map is invalid at ${jsonPath}.${key}.`,
        );
      }
      for (const [name, child] of Object.entries(schema[key])) {
        validateGeneratorSchema(child, `${jsonPath}.${key}.${name}`);
      }
    }
    for (const key of ["allOf", "anyOf", "oneOf", "prefixItems"]) {
      if (schema[key] === undefined) continue;
      if (!Array.isArray(schema[key])) {
        throw new PocError(
          `OpenAPI schema list is invalid at ${jsonPath}.${key}.`,
        );
      }
      schema[key].forEach((child, index) =>
        validateGeneratorSchema(child, `${jsonPath}.${key}[${index}]`),
      );
    }
    for (const key of [
      "additionalProperties",
      "contains",
      "else",
      "if",
      "items",
      "not",
      "propertyNames",
      "then",
      "unevaluatedItems",
      "unevaluatedProperties",
    ]) {
      if (schema[key] !== undefined) {
        validateGeneratorSchema(schema[key], `${jsonPath}.${key}`);
      }
    }
  };

  if (isObject(document.components?.schemas)) {
    for (const [name, schema] of Object.entries(document.components.schemas)) {
      validateGeneratorSchema(schema, `$.components.schemas.${name}`);
    }
  }
  walkDocument(document, (value, jsonPath) => {
    if (isObject(value) && Object.hasOwn(value, "schema")) {
      validateGeneratorSchema(value.schema, `${jsonPath}.schema`);
    }
  });

  const operationIds = new Set();
  for (const { route, operation } of operationEntries(document)) {
    if (
      !route.startsWith("/") ||
      route.includes("\\") ||
      route.includes("://") ||
      /(?:^|\/)\.\.(?:\/|$)/u.test(route) ||
      /%(?:2e|2f|5c)/iu.test(route) ||
      (route.match(/\{/gu)?.length ?? 0) !== (route.match(/\}/gu)?.length ?? 0)
    ) {
      throw new PocError(`Unsafe OpenAPI route: ${route}`);
    }
    if (
      typeof operation.operationId !== "string" ||
      operation.operationId.trim() === ""
    ) {
      throw new PocError(
        "Every OpenAPI operation must have a non-empty operationId.",
      );
    }
    if (operationIds.has(operation.operationId)) {
      throw new PocError(
        `Duplicate OpenAPI operationId: ${operation.operationId}`,
      );
    }
    operationIds.add(operation.operationId);
  }
  return document;
};

export const buildRiskReport = (document) => {
  const int64Fields = [];
  walkDocument(document, (value, jsonPath) => {
    if (isObject(value) && value.format === "int64") {
      int64Fields.push(jsonPath);
    }
  });

  const operations = operationEntries(document).map(
    ({ route, method, operation, pathItem }) => {
      const requestHeaders = mergedParameters(document, pathItem, operation)
        .filter((parameter) => parameter.in === "header")
        .map((parameter) => ({
          name: parameter.name,
          required: parameter.required === true,
        }))
        .sort((left, right) => left.name.localeCompare(right.name));
      const responseHeaders = [];
      for (const [status, responseCandidate] of Object.entries(
        operation.responses ?? {},
      )) {
        const response = responseCandidate?.$ref
          ? resolveLocalReference(document, responseCandidate.$ref)
          : responseCandidate;
        for (const name of Object.keys(response?.headers ?? {})) {
          responseHeaders.push({ status, name });
        }
      }
      responseHeaders.sort((left, right) =>
        `${left.status}:${left.name}`.localeCompare(
          `${right.status}:${right.name}`,
        ),
      );
      return {
        operationId: operation.operationId,
        method: method.toUpperCase(),
        route,
        requestHeaders,
        responseHeaders,
      };
    },
  );

  return {
    adoption: "NO_GO",
    generatorProfile: {
      client: "fetch",
      headers: true,
      includeHttpResponseReturnType: true,
      pathParametersUrlEncoded: true,
      protectedRequestOptionHeaders: "reject",
      requestInitOverrides: true,
      runtimeValidation: false,
      useBigInt: false,
    },
    blockers: [
      {
        code: "INT64_WIRE_PRECISION_POLICY_REQUIRED",
        count: int64Fields.length,
        fields: int64Fields,
      },
      {
        code: "RUNTIME_RESPONSE_DECODER_REQUIRED",
        detail:
          "Generated fetch code uses compile-time types and JSON.parse without ERP schema/version validation.",
      },
      {
        code: "FULL_OPENAPI_SEMANTIC_VALIDATION_REQUIRED",
        detail:
          "The PoC preflight enforces a generator-specific schema subset; complete OpenAPI 3.1 and JSON Schema semantic validation remains required before adoption.",
      },
    ],
    operations,
  };
};

export const buildOrvalOptions = (document, target) => ({
  input: {
    target: document,
    unsafeDisableValidation: false,
    parserOptions: { externalRefs: { allow: [] } },
  },
  output: {
    target,
    schemas: false,
    mode: "single",
    client: "fetch",
    httpClient: "fetch",
    clean: false,
    docs: false,
    mock: false,
    headers: true,
    indexFiles: false,
    urlEncodeParameters: true,
    tsconfig: GENERATOR_TSCONFIG_PATH,
    packageJson: PACKAGE_PATH,
    override: {
      header: false,
      useBigInt: false,
      fetch: {
        includeHttpResponseReturnType: true,
        forceSuccessResponse: false,
        serializeResponseHeaders: false,
        runtimeValidation: false,
        useRuntimeFetcher: false,
      },
    },
  },
});

const PROTECTED_HEADERS = [
  "X-Company-Id",
  "Idempotency-Key",
  "X-Correlation-Id",
  "X-Causation-Id",
];

const RAW_REQUEST_HEADERS_HELPER = [
  "const getHeaders = (h?: NonNullable<RequestInit['headers']>): Record<string, string | readonly string[]> => {",
  "    if (!h) return {};",
  "    if (h instanceof Headers) return Object.fromEntries(h.entries());",
  "    if (Array.isArray(h)) return Object.fromEntries(h);",
  "    return h;",
  "  };",
].join("\n");

const protectedRequestOptionHeaderNames = (document) => {
  const names = new Set(["accept", "content-type"]);
  for (const { operation, pathItem } of operationEntries(document)) {
    for (const parameter of mergedParameters(document, pathItem, operation)) {
      if (parameter.in === "header") {
        names.add(parameter.name.toLowerCase());
      }
    }
  }
  return [...names].sort((left, right) => left.localeCompare(right));
};

const operationFunctionName = (operationId) =>
  `${operationId[0].toLowerCase()}${operationId.slice(1)}`;

const typedHeaderRuntimeGuard = [
  `const ${TYPED_HEADER_GUARD_FUNCTION} = (`,
  "  value: NonNullable<RequestInit['headers']> | undefined,",
  "  allowed: readonly string[],",
  "  required: readonly string[],",
  "): Record<string, string> => {",
  "  if (value instanceof Headers || Array.isArray(value)) {",
  `    throw new TypeError("${TYPED_HEADER_GUARD_MARKER}");`,
  "  }",
  "  const entries = Object.entries(value ?? {});",
  "  const canonicalByLowerName = new Map(",
  "    allowed.map((name) => [name.toLowerCase(), name]),",
  "  );",
  "  const seen = new Set<string>();",
  "  const normalized: Record<string, string> = {};",
  "  for (const [name, headerValue] of entries) {",
  "    const lowerName = name.toLowerCase();",
  "    const canonicalName = canonicalByLowerName.get(lowerName);",
  "    if (",
  "      canonicalName === undefined ||",
  "      canonicalName !== name ||",
  "      seen.has(lowerName) ||",
  '      typeof headerValue !== "string"',
  "    ) {",
  `      throw new TypeError("${TYPED_HEADER_GUARD_MARKER}");`,
  "    }",
  "    seen.add(lowerName);",
  "    normalized[canonicalName] = headerValue;",
  "  }",
  "  if (required.some((name) => !seen.has(name.toLowerCase()))) {",
  `    throw new TypeError("${TYPED_HEADER_GUARD_MARKER}");`,
  "  }",
  "  return normalized;",
  "};",
  "",
].join("\n");

const requestOptionsRuntimeGuard = [
  `const ${REQUEST_OPTIONS_GUARD_FUNCTION} = (`,
  "  value: RequestInit | undefined,",
  "): RequestInit => {",
  "  if (value === undefined) return {};",
  "  const entries = Object.entries(value);",
  "  if (",
  "    entries.some(",
  '      ([name]) => name === "method" || name === "body",',
  "    )",
  "  ) {",
  `    throw new TypeError("${REQUEST_OPTIONS_GUARD_MARKER}");`,
  "  }",
  "  return Object.fromEntries(",
  '    entries.filter(([name]) => name !== "headers"),',
  "  ) as RequestInit;",
  "};",
  "",
].join("\n");

export const applyProtectedHeaderGuard = (source, document) => {
  const contractOperations = operationEntries(document);
  const expectedOperations = new Map(
    contractOperations.map(({ method, operation, pathItem }) => {
      const declaredHeaders = mergedParameters(document, pathItem, operation)
        .filter((parameter) => parameter.in === "header")
        .map((parameter) => ({
          name: parameter.name,
          required: parameter.required === true,
        }))
        .sort((left, right) => left.name.localeCompare(right.name));
      return [
        operationFunctionName(operation.operationId),
        {
          allowed: declaredHeaders.map((header) => header.name),
          hasRequestBody: operation.requestBody !== undefined,
          method: method.toUpperCase(),
          required: declaredHeaders
            .filter((header) => header.required)
            .map((header) => header.name),
        },
      ];
    }),
  );
  const sourceFile = ts.createSourceFile(
    "client.ts",
    source,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TS,
  );
  const parseDiagnostics = sourceFile.parseDiagnostics ?? [];
  const expectedHelperCount = expectedOperations.size;
  if (
    expectedHelperCount === 0 ||
    expectedOperations.size !== contractOperations.length ||
    parseDiagnostics.length > 0 ||
    source.includes(PROTECTED_HEADER_GUARD_MARKER) ||
    source.includes(TYPED_HEADER_GUARD_MARKER) ||
    source.includes(TYPED_HEADER_GUARD_FUNCTION) ||
    source.includes(REQUEST_OPTIONS_GUARD_MARKER) ||
    source.includes(REQUEST_OPTIONS_GUARD_FUNCTION)
  ) {
    throw new PocError(
      "Generated protected-header guard precondition failed closed.",
    );
  }

  const protectedNames = JSON.stringify(
    protectedRequestOptionHeaderNames(document),
  );
  const guardedHelper = [
    GUARDED_REQUEST_HEADERS_HELPER_SIGNATURE,
    "    if (!h) return {};",
    "    const normalized: Record<string, string> =",
    "      h instanceof Headers",
    "        ? Object.fromEntries(h.entries())",
    "        : Array.isArray(h)",
    "          ? Object.fromEntries(h)",
    "          : Object.fromEntries(Object.entries(h));",
    '    if (Object.values(normalized).some((value) => typeof value !== "string")) {',
    `      throw new TypeError("${PROTECTED_HEADER_GUARD_MARKER}");`,
    "    }",
    "    for (const name of Object.keys(normalized)) {",
    `      if (${protectedNames}.includes(name.toLowerCase())) {`,
    `        throw new TypeError("${PROTECTED_HEADER_GUARD_MARKER}");`,
    "      }",
    "    }",
    "    return normalized;",
    "  };",
  ].join("\n");
  const replacements = [];
  const foundOperations = new Set();
  for (const statement of sourceFile.statements) {
    if (!ts.isVariableStatement(statement)) continue;
    for (const declaration of statement.declarationList.declarations) {
      if (!ts.isIdentifier(declaration.name)) continue;
      const metadata = expectedOperations.get(declaration.name.text);
      if (metadata === undefined) continue;
      if (
        foundOperations.has(declaration.name.text) ||
        !declaration.initializer ||
        !ts.isArrowFunction(declaration.initializer) ||
        !ts.isBlock(declaration.initializer.body)
      ) {
        throw new PocError(
          "Generated operation shape failed the protected-header guard.",
        );
      }

      const helperStatements = declaration.initializer.body.statements.filter(
        (candidate) =>
          ts.isVariableStatement(candidate) &&
          candidate.declarationList.declarations.some(
            (helperDeclaration) =>
              ts.isIdentifier(helperDeclaration.name) &&
              helperDeclaration.name.text === "getHeaders",
          ),
      );
      if (
        helperStatements.length !== 1 ||
        helperStatements[0].getText(sourceFile) !== RAW_REQUEST_HEADERS_HELPER
      ) {
        throw new PocError(
          "Generated request-header helper drifted from the reviewed shape.",
        );
      }

      const fetchCalls = [];
      const headerObjectInitializers = [];
      const methodInitializers = [];
      const bodyInitializers = [];
      const optionsSpreads = [];
      const visit = (node) => {
        if (
          ts.isCallExpression(node) &&
          ts.isIdentifier(node.expression) &&
          node.expression.text === "fetch"
        ) {
          fetchCalls.push(node);
        }
        if (
          ts.isPropertyAssignment(node) &&
          ts.isIdentifier(node.name) &&
          node.name.text === "headers" &&
          ts.isObjectLiteralExpression(node.initializer)
        ) {
          headerObjectInitializers.push(node);
        }
        if (
          ts.isPropertyAssignment(node) &&
          ts.isIdentifier(node.name) &&
          node.name.text === "method"
        ) {
          methodInitializers.push(node);
        }
        if (
          ts.isPropertyAssignment(node) &&
          ts.isIdentifier(node.name) &&
          node.name.text === "body"
        ) {
          bodyInitializers.push(node);
        }
        if (
          ts.isSpreadAssignment(node) &&
          ts.isIdentifier(node.expression) &&
          node.expression.text === "options"
        ) {
          optionsSpreads.push(node);
        }
        ts.forEachChild(node, visit);
      };
      visit(declaration.initializer.body);
      const fetchCall = fetchCalls[0];
      if (
        fetchCalls.length !== 1 ||
        fetchCall.arguments.length !== 2 ||
        !ts.isObjectLiteralExpression(fetchCall.arguments[1])
      ) {
        throw new PocError(
          "Generated fetch initializer drifted from the reviewed shape.",
        );
      }

      const fetchInitializer = fetchCall.arguments[1];
      const directHeaderInitializers = fetchInitializer.properties.filter(
        (property) =>
          ts.isPropertyAssignment(property) &&
          ts.isIdentifier(property.name) &&
          property.name.text === "headers" &&
          ts.isObjectLiteralExpression(property.initializer),
      );
      const directMethodInitializers = fetchInitializer.properties.filter(
        (property) =>
          ts.isPropertyAssignment(property) &&
          ts.isIdentifier(property.name) &&
          property.name.text === "method",
      );
      const directBodyInitializers = fetchInitializer.properties.filter(
        (property) =>
          ts.isPropertyAssignment(property) &&
          ts.isIdentifier(property.name) &&
          property.name.text === "body",
      );
      const directOptionsSpreads = fetchInitializer.properties.filter(
        (property) =>
          ts.isSpreadAssignment(property) &&
          ts.isIdentifier(property.expression) &&
          property.expression.text === "options",
      );
      if (
        headerObjectInitializers.length !== 1 ||
        directHeaderInitializers.length !== 1 ||
        headerObjectInitializers[0] !== directHeaderInitializers[0] ||
        optionsSpreads.length !== 1 ||
        directOptionsSpreads.length !== 1 ||
        optionsSpreads[0] !== directOptionsSpreads[0] ||
        methodInitializers.length !== 1 ||
        directMethodInitializers.length !== 1 ||
        methodInitializers[0] !== directMethodInitializers[0] ||
        !ts.isStringLiteral(directMethodInitializers[0].initializer) ||
        directMethodInitializers[0].initializer.text !== metadata.method ||
        bodyInitializers.length !== (metadata.hasRequestBody ? 1 : 0) ||
        directBodyInitializers.length !== (metadata.hasRequestBody ? 1 : 0) ||
        (metadata.hasRequestBody &&
          bodyInitializers[0] !== directBodyInitializers[0])
      ) {
        throw new PocError(
          "Generated fetch contract initializer drifted from the reviewed shape.",
        );
      }

      const headerInitializer = directHeaderInitializers[0].initializer;
      const typedSpreads = headerInitializer.properties.filter(
        (property) =>
          ts.isSpreadAssignment(property) &&
          ts.isIdentifier(property.expression) &&
          property.expression.text === "headers",
      );
      const genericSpreads = headerInitializer.properties.filter(
        (property) =>
          ts.isSpreadAssignment(property) &&
          property.getText(sourceFile) === "...getHeaders(options?.headers)",
      );
      if (typedSpreads.length !== 1 || genericSpreads.length !== 1) {
        throw new PocError(
          "Generated fetch-header initializer drifted from the reviewed shape.",
        );
      }

      const typedSpread = typedSpreads[0];
      replacements.push({
        start: helperStatements[0].getStart(sourceFile),
        end: helperStatements[0].getEnd(),
        text: guardedHelper,
      });
      replacements.push({
        start: typedSpread.getStart(sourceFile),
        end: typedSpread.getEnd(),
        text: `...${TYPED_HEADER_GUARD_FUNCTION}(headers, ${JSON.stringify(metadata.allowed)}, ${JSON.stringify(metadata.required)})`,
      });
      replacements.push({
        start: directOptionsSpreads[0].getStart(sourceFile),
        end: directOptionsSpreads[0].getEnd(),
        text: `...${REQUEST_OPTIONS_GUARD_FUNCTION}(options)`,
      });
      foundOperations.add(declaration.name.text);
    }
  }
  if (
    foundOperations.size !== expectedOperations.size ||
    [...expectedOperations.keys()].some((name) => !foundOperations.has(name))
  ) {
    throw new PocError(
      "Generated operation coverage failed the protected-header guard.",
    );
  }

  replacements.sort((left, right) => right.start - left.start);
  let guardedSource = source;
  for (const replacement of replacements) {
    guardedSource = `${guardedSource.slice(0, replacement.start)}${replacement.text}${guardedSource.slice(replacement.end)}`;
  }
  guardedSource = `${requestOptionsRuntimeGuard}${typedHeaderRuntimeGuard}${guardedSource}`;
  const guardCount =
    guardedSource.split(GUARDED_REQUEST_HEADERS_HELPER_SIGNATURE).length - 1;
  const typedGuardCallCount =
    guardedSource.split(`...${TYPED_HEADER_GUARD_FUNCTION}(`).length - 1;
  const requestOptionsGuardCallCount =
    guardedSource.split(`...${REQUEST_OPTIONS_GUARD_FUNCTION}(options)`)
      .length - 1;
  if (
    guardedSource.includes(RAW_REQUEST_HEADERS_HELPER) ||
    guardCount !== expectedHelperCount ||
    typedGuardCallCount !== expectedHelperCount ||
    requestOptionsGuardCallCount !== expectedHelperCount
  ) {
    throw new PocError("Generated protected-header guard failed closed.");
  }
  return guardedSource;
};

export const inspectGeneratedTransport = (source, document) => {
  const encodedPathParameterCount = (
    source.match(/encodeURIComponent\(String\([A-Za-z_$][\w$]*\)\)/gu) ?? []
  ).length;
  const operationPathParameterNames = document
    ? [
        ...new Set(
          operationEntries(document).flatMap(({ route }) =>
            [...route.matchAll(/\{([^{}]+)\}/gu)].map((match) => match[1]),
          ),
        ),
      ]
    : [];
  const expectedEncodedPathParameterCount = document
    ? operationEntries(document).reduce(
        (count, { route }) =>
          count + (route.match(/\{[^{}]+\}/gu)?.length ?? 0),
        0,
      )
    : undefined;
  const rawPathParameterInterpolations = operationPathParameterNames.filter(
    (name) => source.includes(`\${${name}}`),
  );
  const protectedHeadersPresent = PROTECTED_HEADERS.filter((name) =>
    source.includes(`'${name}'`),
  );
  const requestInitHeaderMergeCount =
    source.split("...getHeaders(options?.headers)").length - 1;
  const protectedHeaderGuardCount =
    source.split(GUARDED_REQUEST_HEADERS_HELPER_SIGNATURE).length - 1;
  const typedHeaderGuardCount =
    source.split(`...${TYPED_HEADER_GUARD_FUNCTION}(`).length - 1;
  const requestOptionsGuardCount =
    source.split(`...${REQUEST_OPTIONS_GUARD_FUNCTION}(options)`).length - 1;
  const requestInitProtectedHeadersRejected =
    requestInitHeaderMergeCount > 0 &&
    protectedHeaderGuardCount === requestInitHeaderMergeCount &&
    typedHeaderGuardCount === requestInitHeaderMergeCount;
  const requestInitHeadersOverrideTypedHeaders =
    requestInitHeaderMergeCount > 0 && !requestInitProtectedHeadersRejected;

  return {
    encodedPathParameterCount,
    expectedEncodedPathParameterCount,
    allPathParametersUrlEncoded:
      expectedEncodedPathParameterCount === undefined
        ? undefined
        : encodedPathParameterCount >= expectedEncodedPathParameterCount &&
          rawPathParameterInterpolations.length === 0,
    protectedHeadersPresent,
    protectedHeaderGuardCount,
    queryParametersUseUrlSearchParams: /new URLSearchParams\(\)/u.test(source),
    rawPathParameterInterpolations,
    requestInitHeaderMergeCount,
    requestInitHeadersOverrideTypedHeaders,
    requestInitProtectedHeadersRejected,
    requestInitMethodBodyRejected:
      requestOptionsGuardCount > 0 &&
      requestOptionsGuardCount === requestInitHeaderMergeCount,
    requestOptionsGuardCount,
    typedHeaderGuardCount,
  };
};

export const assertProductionAdoptionAllowed = ({
  manifestAdoption,
  riskReport,
  transportInspection,
}) => {
  if (
    transportInspection.requestInitHeadersOverrideTypedHeaders ||
    transportInspection.requestInitProtectedHeadersRejected !== true
  ) {
    throw new PocError(
      "Generated client is not adoptable: protected RequestInit headers are not rejected.",
    );
  }
  if (transportInspection.requestInitMethodBodyRejected !== true) {
    throw new PocError(
      "Generated client is not adoptable: generic RequestInit method or body is not rejected.",
    );
  }
  if (
    manifestAdoption?.status !== "GO" ||
    !Array.isArray(manifestAdoption?.blockers) ||
    manifestAdoption.blockers.length > 0
  ) {
    throw new PocError(
      "Generated client is not adoptable: the fail-closed profile has unresolved blockers.",
    );
  }
  if (riskReport.adoption !== "GO" || riskReport.blockers.length > 0) {
    throw new PocError(
      "Generated client is not approved for production adoption.",
    );
  }
};

const assertNetworkDenied = () => {
  if (globalThis.__GOYMEN_ORVAL_POC_NETWORK_DENIED__ !== true) {
    throw new PocError(
      "Generation must run with deny-network.mjs loaded through node --import.",
    );
  }
};

let generatorPromise;
const loadOrvalGenerator = async () => {
  await loadManifest();
  generatorPromise ??= import("orval").then((module) => {
    if (typeof module.generate !== "function") {
      throw new PocError("Reviewed Orval generator export is unavailable.");
    }
    return module.generate;
  });
  return generatorPromise;
};

export const generateOnce = async (document, outputRoot) => {
  assertNetworkDenied();
  validateContractDocument(document);
  const generate = await loadOrvalGenerator();
  const generatedRoot = path.join(outputRoot, "generated");
  await mkdir(generatedRoot, { recursive: false });
  const target = path.join(generatedRoot, "client.ts");
  try {
    await generate(buildOrvalOptions(document, target), TOOL_ROOT, {
      throwOnError: true,
      failOnWarnings: true,
    });
  } catch {
    throw new PocError("Orval generation failed closed.");
  }
  const topLevelEntries = await readdir(outputRoot, { withFileTypes: true });
  if (
    topLevelEntries.length !== 1 ||
    topLevelEntries[0].name !== "generated" ||
    !topLevelEntries[0].isDirectory()
  ) {
    throw new PocError("Orval wrote outside the expected generated directory.");
  }
  const generatedSource = await readFile(target, "utf8");
  const guardedSource = applyProtectedHeaderGuard(generatedSource, document);
  if (Buffer.byteLength(guardedSource, "utf8") > MAX_FILE_BYTES) {
    throw new PocError("Guarded generated output exceeds the file limit.");
  }
  await writeFile(target, guardedSource, { encoding: "utf8", flag: "w" });
  return generatedRoot;
};

export const collectTree = async (root) => {
  const files = new Map();
  let totalBytes = 0;
  const visit = async (directory) => {
    const entries = await readdir(directory, { withFileTypes: true });
    entries.sort((left, right) => left.name.localeCompare(right.name));
    for (const entry of entries) {
      const absolute = path.join(directory, entry.name);
      const metadata = await lstat(absolute);
      if (metadata.isSymbolicLink()) {
        throw new PocError("Generated output must not contain symbolic links.");
      }
      if (entry.isDirectory()) {
        await visit(absolute);
        continue;
      }
      if (
        !entry.isFile() ||
        metadata.size === 0 ||
        metadata.size > MAX_FILE_BYTES
      ) {
        throw new PocError("Generated output contains an invalid file.");
      }
      const content = await readFile(absolute);
      totalBytes += content.length;
      if (totalBytes > MAX_TREE_BYTES) {
        throw new PocError("Generated output exceeds the bounded tree size.");
      }
      files.set(
        path.relative(root, absolute).split(path.sep).join("/"),
        content,
      );
    }
  };
  await visit(root);
  if (files.size === 0) {
    throw new PocError("Generated output tree must not be empty.");
  }
  return files;
};

export const compareTrees = (first, second) => {
  assert.deepEqual([...first.keys()], [...second.keys()]);
  for (const [relative, content] of first) {
    if (!content.equals(second.get(relative))) {
      throw new PocError(`Generated output is not byte-identical: ${relative}`);
    }
  }
};

export const scanGeneratedOutput = (tree, forbiddenRoots = []) => {
  const forbiddenText = forbiddenRoots
    .filter(Boolean)
    .flatMap((value) => [value, value.replaceAll("\\", "/")]);
  const environmentIdentity = [
    process.env.GITHUB_ACTOR,
    process.env.RUNNER_NAME,
    process.env.USERNAME,
  ].filter((value) => value && value.length >= 5);
  const patterns = [
    /[A-Za-z]:[\\/]/u,
    /\/(?:home|Users|tmp|__w)\//u,
    /\b(?:Server|Password|User Id|Initial Catalog)\s*=/iu,
    /-----BEGIN [A-Z ]*PRIVATE KEY-----/u,
    /\bBearer\s+[A-Za-z0-9._~-]{20,}/u,
    /\b(?:SELECT|INSERT|UPDATE|DELETE)\s+(?:FROM|INTO|SET|[A-Za-z_*])/iu,
    /\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z/u,
    /sourceMappingURL/iu,
  ];
  for (const [relative, content] of tree) {
    const text = content.toString("utf8");
    for (const forbidden of [...forbiddenText, ...environmentIdentity]) {
      if (text.includes(forbidden)) {
        throw new PocError(
          `Generated output leaked host identity in ${relative}.`,
        );
      }
    }
    if (patterns.some((pattern) => pattern.test(text))) {
      throw new PocError(
        `Generated output failed the sensitive-content scan: ${relative}.`,
      );
    }
  }
};

export const compileGeneratedOutput = async (runRoot) => {
  const configPath = path.join(runRoot, "tsconfig.json");
  await writeFile(
    configPath,
    `${JSON.stringify(
      {
        compilerOptions: {
          target: "ES2023",
          lib: ["ES2023", "DOM", "DOM.Iterable"],
          module: "ESNext",
          moduleResolution: "Bundler",
          strict: true,
          skipLibCheck: false,
          noEmit: true,
          exactOptionalPropertyTypes: true,
          noUncheckedIndexedAccess: true,
          useUnknownInCatchVariables: true,
          verbatimModuleSyntax: true,
          moduleDetection: "force",
          types: [],
        },
        files: ["./generated/client.ts"],
      },
      null,
      2,
    )}\n`,
    { encoding: "utf8", flag: "wx" },
  );
  const compiler = path.join(
    TOOL_ROOT,
    "node_modules",
    "typescript",
    "bin",
    "tsc",
  );
  const result = allowlistedSpawnSync(
    process.execPath,
    [compiler, "-p", configPath, "--pretty", "false"],
    {
      cwd: runRoot,
      encoding: "utf8",
      maxBuffer: 4 * 1024 * 1024,
      env: childEnvironment(),
    },
  );
  if (result.error || result.status !== 0) {
    throw new PocError(
      "Generated output failed strict TypeScript compilation.",
    );
  }
};

const loadManifest = async () => {
  const [manifestText, packageText, lockBytes, npmConfig] = await Promise.all([
    readFile(MANIFEST_PATH, "utf8"),
    readFile(PACKAGE_PATH, "utf8"),
    readFile(PACKAGE_LOCK_PATH),
    readFile(NPM_CONFIG_PATH, "utf8"),
  ]);
  const manifest = JSON.parse(manifestText);
  const packageJson = JSON.parse(packageText);
  const lock = JSON.parse(lockBytes.toString("utf8"));
  if (
    manifest?.contract?.path !== "contracts/openapi/goymen-erp-v1.json" ||
    !/^[0-9a-f]{64}$/u.test(manifest?.contract?.sha256 ?? "") ||
    manifest?.contract?.openapi !== "3.1.1" ||
    manifest?.toolchain?.nodeMajor !== 24 ||
    manifest?.toolchain?.orval !== EXPECTED_DEPENDENCIES.orval ||
    manifest?.toolchain?.prettier !== EXPECTED_DEPENDENCIES.prettier ||
    manifest?.toolchain?.typescript !== EXPECTED_DEPENDENCIES.typescript ||
    Number(process.versions.node.split(".")[0]) !== 24
  ) {
    throw new PocError("PoC manifest failed closed.");
  }
  assertExactBlockerInventory(manifest.adoption);
  validateSupplyChainState({
    lock,
    lockBytes,
    manifest,
    npmConfig,
    packageJson,
  });
  return manifest;
};

export const runPoc = async ({ canonicalPath, generatedPath } = {}) => {
  assertNetworkDenied();
  const manifest = await loadManifest();
  const canonical = await assertRegularFile(
    canonicalPath ?? path.join(REPOSITORY_ROOT, manifest.contract.path),
    REPOSITORY_ROOT,
    "Canonical contract",
  );
  const generated = await assertRegularFile(
    generatedPath ?? path.join(REPOSITORY_ROOT, "artifacts", "openapi.json"),
    REPOSITORY_ROOT,
    "Generated contract",
  );
  const [canonicalBytes, generatedBytes] = await Promise.all([
    readFile(canonical),
    readFile(generated),
  ]);
  if (!canonicalBytes.equals(generatedBytes)) {
    throw new PocError("Canonical/generated OpenAPI byte gate failed closed.");
  }
  if (sha256(canonicalBytes) !== manifest.contract.sha256) {
    throw new PocError(
      "Canonical OpenAPI SHA-256 does not match the reviewed PoC input.",
    );
  }
  const document = validateContractDocument(
    JSON.parse(canonicalBytes.toString("utf8")),
  );

  const firstRoot = await mkdtemp(path.join(tmpdir(), "goymen-orval-poc-a-"));
  const secondRoot = await mkdtemp(path.join(tmpdir(), "goymen-orval-poc-b-"));
  if (
    isWithin(REPOSITORY_ROOT, firstRoot) ||
    isWithin(REPOSITORY_ROOT, secondRoot)
  ) {
    throw new PocError("Generated output must remain outside the repository.");
  }
  try {
    const firstGenerated = await generateOnce(document, firstRoot);
    const secondGenerated = await generateOnce(document, secondRoot);
    const firstTree = await collectTree(firstGenerated);
    const secondTree = await collectTree(secondGenerated);
    compareTrees(firstTree, secondTree);
    scanGeneratedOutput(firstTree, [REPOSITORY_ROOT, firstRoot, secondRoot]);
    await compileGeneratedOutput(firstRoot);
    await compileGeneratedOutput(secondRoot);
    const generatedSource = firstTree.get("client.ts")?.toString("utf8");
    if (generatedSource === undefined) {
      throw new PocError("Expected generated client.ts is missing.");
    }
    const transportInspection = inspectGeneratedTransport(
      generatedSource,
      document,
    );
    if (transportInspection.allPathParametersUrlEncoded !== true) {
      throw new PocError("Generated path parameters are not URL-encoded.");
    }
    const riskReport = buildRiskReport(document);
    let productionAdoptionGate;
    try {
      assertProductionAdoptionAllowed({
        manifestAdoption: manifest.adoption,
        riskReport,
        transportInspection,
      });
      productionAdoptionGate = { passed: true };
    } catch (error) {
      if (!(error instanceof PocError)) throw error;
      productionAdoptionGate = { passed: false, reason: error.message };
    }
    if (productionAdoptionGate.passed) {
      throw new PocError(
        "PoC unexpectedly passed the production adoption gate.",
      );
    }
    return {
      contractSha256: manifest.contract.sha256,
      generatedFiles: [...firstTree.keys()],
      generatedTreeSha256: sha256(
        Buffer.concat(
          [...firstTree].flatMap(([name, content]) => [
            Buffer.from(`${name}\0`),
            content,
          ]),
        ),
      ),
      deterministic: true,
      strictTypeScript: true,
      networkDenyDefenseInDepth: true,
      childProcessDenyDefenseInDepth: true,
      externallyEnforcedProfile: {
        crossOsDeterminism: false,
        manualDiffAndMutationCi: false,
        osContainerSandbox: false,
        react19Vite8BrowserSmoke: false,
        readOnlyRepository: false,
        secretlessEnvironment: false,
      },
      manifestAdoption: manifest.adoption,
      transportInspection,
      productionAdoptionGate,
      riskReport,
    };
  } finally {
    await Promise.all([
      rm(firstRoot, { recursive: true, force: true }),
      rm(secondRoot, { recursive: true, force: true }),
    ]);
  }
};

export const parseCliArguments = (arguments_) => {
  if (
    !Array.isArray(arguments_) ||
    arguments_.some((argument) => argument !== "--require-adoption") ||
    arguments_.filter((argument) => argument === "--require-adoption").length >
      1
  ) {
    throw new PocError("Unsupported Orval PoC command-line argument.");
  }
  return { requireAdoption: arguments_.includes("--require-adoption") };
};

const isMain =
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isMain) {
  try {
    const { requireAdoption } = parseCliArguments(process.argv.slice(2));
    const result = await runPoc();
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    if (requireAdoption) {
      assertProductionAdoptionAllowed({
        manifestAdoption: result.manifestAdoption,
        riskReport: result.riskReport,
        transportInspection: result.transportInspection,
      });
    }
  } catch (error) {
    const message =
      error instanceof PocError ? error.message : "Orval PoC failed closed.";
    process.stderr.write(`ERROR: ${message}\n`);
    process.exitCode = 2;
  }
}
