import childProcess from "node:child_process";
import dns from "node:dns";
import http from "node:http";
import https from "node:https";
import net from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import tls from "node:tls";
import { fileURLToPath } from "node:url";
import { syncBuiltinESMExports } from "node:module";

const TOOL_ROOT = path.dirname(fileURLToPath(import.meta.url));
const REPOSITORY_ROOT = path.resolve(TOOL_ROOT, "..", "..");
const TEMPORARY_ROOT = path.resolve(tmpdir());
const ORIGINAL_SPAWN_SYNC = childProcess.spawnSync.bind(childProcess);
const SAFE_ENVIRONMENT_NAMES = new Set([
  "SYSTEMROOT",
  "TEMP",
  "TMP",
  "TMPDIR",
  "WINDIR",
]);

for (const name of Object.keys(process.env)) {
  if (!SAFE_ENVIRONMENT_NAMES.has(name.toUpperCase())) {
    delete process.env[name];
  }
}

const denyNetwork = () => {
  throw new Error("ORVAL_POC_NETWORK_DENIED");
};

const denyChildProcess = () => {
  throw new Error("ORVAL_POC_CHILD_PROCESS_DENIED");
};

const normalizeForComparison = (value) => {
  const normalized = path.resolve(value);
  return process.platform === "win32" ? normalized.toLowerCase() : normalized;
};

const isSamePath = (left, right) =>
  normalizeForComparison(left) === normalizeForComparison(right);

const isWithin = (root, candidate) => {
  const relative = path.relative(root, candidate);
  return (
    relative === "" ||
    (!relative.startsWith(`..${path.sep}`) &&
      relative !== ".." &&
      !path.isAbsolute(relative))
  );
};

const hasSanitizedEnvironment = (environment) =>
  environment !== undefined &&
  Object.keys(environment).every((name) =>
    SAFE_ENVIRONMENT_NAMES.has(name.toUpperCase()),
  );

const isAllowedTypeScriptInvocation = (command, args, options) => {
  const compiler = path.join(
    TOOL_ROOT,
    "node_modules",
    "typescript",
    "bin",
    "tsc",
  );
  if (
    !isSamePath(command, process.execPath) ||
    !Array.isArray(args) ||
    args.length !== 5
  ) {
    return false;
  }
  const [compilerArgument, projectFlag, configPath, prettyFlag, prettyValue] =
    args;
  const absoluteConfig = path.resolve(String(configPath));
  const absoluteCwd = path.resolve(String(options?.cwd));
  return (
    isSamePath(compilerArgument, compiler) &&
    projectFlag === "-p" &&
    prettyFlag === "--pretty" &&
    prettyValue === "false" &&
    path.basename(absoluteConfig) === "tsconfig.json" &&
    isWithin(TEMPORARY_ROOT, absoluteConfig) &&
    !isWithin(REPOSITORY_ROOT, absoluteConfig) &&
    isSamePath(path.dirname(absoluteConfig), absoluteCwd) &&
    options?.shell !== true &&
    hasSanitizedEnvironment(options?.env)
  );
};

const allowlistedSpawnSync = (command, args, options = {}) => {
  if (!isAllowedTypeScriptInvocation(command, args, options)) {
    throw new Error("ORVAL_POC_CHILD_PROCESS_DENIED");
  }
  return ORIGINAL_SPAWN_SYNC(command, args, options);
};

globalThis.fetch = denyNetwork;

http.request = denyNetwork;
http.get = denyNetwork;
https.request = denyNetwork;
https.get = denyNetwork;
net.connect = denyNetwork;
net.createConnection = denyNetwork;
tls.connect = denyNetwork;

for (const name of [
  "lookup",
  "resolve",
  "resolve4",
  "resolve6",
  "resolveAny",
  "resolveCaa",
  "resolveCname",
  "resolveMx",
  "resolveNaptr",
  "resolveNs",
  "resolvePtr",
  "resolveSoa",
  "resolveSrv",
  "resolveTxt",
  "reverse",
]) {
  if (typeof dns[name] === "function") {
    dns[name] = denyNetwork;
  }
  if (typeof dns.promises?.[name] === "function") {
    dns.promises[name] = denyNetwork;
  }
}

for (const name of [
  "exec",
  "execFile",
  "execFileSync",
  "execSync",
  "fork",
  "spawn",
  "spawnSync",
]) {
  childProcess[name] = denyChildProcess;
}

syncBuiltinESMExports();
globalThis.__GOYMEN_ORVAL_POC_NETWORK_DENIED__ = true;
globalThis.__GOYMEN_ORVAL_POC_CHILD_PROCESS_DENIED__ = true;
globalThis.__GOYMEN_ORVAL_POC_SPAWN_SYNC__ = allowlistedSpawnSync;
