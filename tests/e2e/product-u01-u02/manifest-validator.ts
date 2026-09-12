/**
 * The L consumer's validator for the Product acceptance environment — the exact
 * contract published by the Product Backend in
 * `tools/product-u01-u02/ENVIRONMENT_CONTRACT.md` (7c5d64f), consumed field for field.
 * No second schema: what is checked here is what that document requires of the
 * consumer, and nothing it assigns to the publisher (OS listener, container labels,
 * SQL observation) is re-decided here — those facts arrive as the publisher's
 * `source-binding.json` and are checked for consistency, never for existence.
 *
 * Consumer validation before any side effect (contract §"Consumer validation"):
 *  1. Read `environment-ready.json` and its two FIXED siblings, `accepted-contract.json`
 *     and `source-binding.json`, once, as bytes. Validate exact types, required fields,
 *     hashes, the accepted-contract hash from the Orchestrator's handoff
 *     (`URUN_ACCEPTED_CONTRACT_SHA256` — a manifest never approves itself), every
 *     repeated value, the reserved endpoints, run/root/DB/resource names and the full
 *     expected-versus-observed `source` object. Both final-status flags must be the JSON
 *     boolean `false`. Anything invalid, missing or mismatched fails BEFORE the C010
 *     run/cache claim, Vite, a browser or any request. `readyForBrowser: true` alone
 *     never passes.
 *  2. Freeze the validated snapshot: its three hashes travel to Vite and the workers,
 *     which re-hash the bytes before use. The proxy target and the development user
 *     come from that snapshot only; ambient overrides cannot redirect it.
 *
 * What this validator also observes for itself: the frontend this browser server will
 * serve IS the approved one — this worktree is `source.frontendWorktree`, its HEAD is
 * `source.frontendHead`, and the served trees are clean. Everything else about source
 * and runtime is the publisher's verified observation, bound by hash.
 *
 * Only erasable TypeScript syntax is used here so Node can run the regression file
 * (`manifest-validator.test.mjs`) against this module directly.
 */
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import path from 'node:path'

export const BINDING_ENV = 'URUN_LIVE_BINDING'
/** The fixed acceptance root of the contract; `<runId>` is appended. */
export const ACCEPTANCE_ROOT_BASE = 'C:\\Users\\goyme\\.codex\\worktrees\\dd1f\\GoymenERP-codex\\artifacts\\product-u01-u02\\acceptance'
export const CONTRACT_ID = 'PRODUCT-U0102-L-v1'
export const TASK_ID = 'U-01-U-02'
export const ENVIRONMENT_NAME = 'Testing'
export const API_BASE_URL = 'http://127.0.0.1:26080'
export const SQL_ENDPOINT = '127.0.0.1,26433'
export const UI_ORIGIN = 'http://127.0.0.1:26173'
export const READY_FILE = 'environment-ready.json'
export const ACCEPTED_FILE = 'accepted-contract.json'
export const SOURCE_BINDING_FILE = 'source-binding.json'
/** The served trees that must be clean and match the approved frontend commit. */
export const FRONTEND_DIFF_PATHS = ['src/frontend/goymen-erp-web', 'tests/e2e']

const RUN_ID_RE = /^product-u0102-[0-9a-f]{12}$/
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const NIL_UUID = '00000000-0000-0000-0000-000000000000'
const SHA256_RE = /^[0-9a-f]{64}$/
const OID_RE = /^[0-9a-f]{40}$/
const HEX_ID_RE = /^[0-9a-f]{12,64}$/
const DLL_RE = /^GoymenERP[A-Za-z0-9.]*\.dll$/

export class ManifestValidationError extends Error {
  readonly code: string
  constructor(code: string, message: string) {
    super('U-01/U-02 environment refused [' + code + ']: ' + message)
    this.code = code
  }
}
const refuse = (code: string, message: string): never => { throw new ManifestValidationError(code, message) }

type Env = Record<string, string | undefined>
type Json = Record<string, unknown>

export interface FrontendObservation {
  worktree: string
  head: string
  dirtyPaths: string[]
}
export interface ValidatorOptions {
  readFile?: (file: string) => Buffer
  /** The fixed acceptance root base; tests point it at a temp directory. */
  acceptanceRootBase?: string
  /** This browser server's frontend worktree, as observed; defaults to Git. */
  observeFrontend?: (worktree: string) => FrontendObservation
}

/** The frozen snapshot every process of the run works against. */
export interface LiveBinding {
  manifestPath: string
  manifestSha256: string
  acceptedContractPath: string
  acceptedContractSha256: string
  sourceBindingPath: string
  sourceBindingSha256: string
  verifiedAtUtc: string
  contractId: string
  taskId: string
  environmentName: string
  /** The environment's RESOURCE run id — distinct from the C010 browser run id. */
  resourceRunId: string
  resourceInstanceId: string
  apiBaseUrl: string
  uiOrigin: string
  actor: string
  actorId: string
  unauthorizedActor: string
  unauthorizedActorId: string
  companyId: string
  codeGroupId: string
  browserPrefix: string
  databaseName: string
  frontendHead: string
}

function sha256(bytes: Buffer): string { return createHash('sha256').update(bytes).digest('hex') }
function isObject(v: unknown): v is Json { return typeof v === 'object' && v !== null && !Array.isArray(v) }
function str(o: Json, name: string, where: string): string {
  const v = o[name]
  if (typeof v !== 'string' || v.length === 0) refuse('FIELD', `${where}.${name} must be a non-empty string`)
  return v as string
}
function exact(o: Json, name: string, expected: unknown, where: string): void {
  if (o[name] !== expected) refuse('EXACT', `${where}.${name} must be exactly ${JSON.stringify(expected)}, got ${JSON.stringify(o[name])}`)
}
function matches(value: string, re: RegExp, name: string): void {
  if (!re.test(value)) refuse('FORMAT', `${name} has an unexpected format: ${value}`)
}
function nonNilUuid(value: string, name: string): void {
  matches(value, UUID_RE, name)
  if (value === NIL_UUID) refuse('FORMAT', `${name} must not be the nil UUID`)
}
function exactOrigin(value: string, expected: string, name: string): void {
  let url: URL
  try { url = new URL(value) } catch { return refuse('ENDPOINT', `${name} is not a URL: ${value}`) }
  if (value !== expected || url.username || url.password || url.search || url.hash || (url.pathname !== '' && url.pathname !== '/')) {
    refuse('ENDPOINT', `${name} must be exactly ${expected}; got ${value}`)
  }
}
function isSorted(items: readonly string[]): boolean {
  return items.every((item, i) => i === 0 || items[i - 1] < item)
}
function canonical(value: unknown): string {
  if (Array.isArray(value)) return '[' + value.map(canonical).join(',') + ']'
  if (isObject(value)) return '{' + Object.keys(value).sort().map((k) => JSON.stringify(k) + ':' + canonical(value[k])).join(',') + '}'
  return JSON.stringify(value === undefined ? null : value)
}

/** The `source` object, expected shape — used for the accepted contract and the observed binding alike. */
export function validateSource(source: unknown, where: string): Json {
  if (!isObject(source)) return refuse('FIELD', where + ' must be an object')
  const s = source
  const worktree = str(s, 'worktree', where)
  if (!path.isAbsolute(worktree)) refuse('FORMAT', where + '.worktree must be absolute')
  for (const name of ['acceptanceHead', 'compiledHead', 'backendDependency', 'productDependency', 'backendTree', 'frontendHead', 'frontendDiffBase']) matches(str(s, name, where), OID_RE, where + '.' + name)
  if (s.compiledHead !== s.acceptanceHead) refuse('EXACT', where + '.compiledHead must equal acceptanceHead')
  for (const name of ['openApiSha256', 'migrationSourcesSha256', 'frontendDiffSha256', 'fixtureSha256']) matches(str(s, name, where), SHA256_RE, where + '.' + name)
  const frontendWorktree = str(s, 'frontendWorktree', where)
  if (!path.isAbsolute(frontendWorktree)) refuse('FORMAT', where + '.frontendWorktree must be absolute')
  const fixturePaths = s.fixturePaths
  if (!Array.isArray(fixturePaths) || fixturePaths.length === 0 || !fixturePaths.every((p) => typeof p === 'string' && p.length > 0 && !path.isAbsolute(p) && !p.startsWith('..'))) refuse('FIELD', where + '.fixturePaths must be a non-empty list of repository-relative paths')
  if (!isSorted(fixturePaths as string[])) refuse('FORMAT', where + '.fixturePaths must be sorted')
  exact(s, 'sourcesClean', true, where)
  const artifacts = s.diskArtifacts
  if (!isObject(artifacts) || Object.keys(artifacts).length === 0) return refuse('FIELD', where + '.diskArtifacts must be a non-empty map')
  const names = Object.keys(artifacts)
  if (!isSorted(names)) refuse('FORMAT', where + '.diskArtifacts must be sorted by basename')
  for (const name of names) {
    if (!DLL_RE.test(name)) refuse('FORMAT', where + '.diskArtifacts key is not a GoymenERP*.dll basename: ' + name)
    if (typeof artifacts[name] !== 'string' || !SHA256_RE.test(artifacts[name] as string)) refuse('FORMAT', where + '.diskArtifacts.' + name + ' must be a SHA-256')
  }
  return s
}

/** The accepted contract, every field exact (contract §"Accepted contract"). */
export function validateAcceptedContract(accepted: unknown, acceptanceRootBase: string): Json {
  if (!isObject(accepted)) return refuse('FIELD', 'accepted contract must be an object')
  const a = accepted
  const w = 'accepted'
  exact(a, 'contractId', CONTRACT_ID, w)
  exact(a, 'taskId', TASK_ID, w)
  exact(a, 'environmentName', ENVIRONMENT_NAME, w)
  const runId = str(a, 'runId', w)
  matches(runId, RUN_ID_RE, w + '.runId')
  const suffix = runId.slice('product-u0102-'.length)
  nonNilUuid(str(a, 'instanceId', w), w + '.instanceId')
  exact(a, 'acceptanceRoot', path.join(acceptanceRootBase, runId), w)
  exactOrigin(str(a, 'apiBaseUrl', w), API_BASE_URL, w + '.apiBaseUrl')
  exact(a, 'sqlEndpoint', SQL_ENDPOINT, w)
  exactOrigin(str(a, 'uiOrigin', w), UI_ORIGIN, w + '.uiOrigin')
  exact(a, 'databaseName', 'GoymenERP_Product_U0102_' + suffix, w)
  exact(a, 'composeProject', runId, w)
  exact(a, 'containerName', runId + '-sql', w)
  exact(a, 'volumeName', runId + '-data', w)
  exact(a, 'networkName', runId + '-network', w)
  const actor = str(a, 'actor', w)
  const unauthorizedActor = str(a, 'unauthorizedActor', w)
  if (actor === unauthorizedActor) refuse('EXACT', w + '.actor and unauthorizedActor must differ')
  const actorId = str(a, 'actorId', w)
  const unauthorizedActorId = str(a, 'unauthorizedActorId', w)
  nonNilUuid(actorId, w + '.actorId')
  nonNilUuid(unauthorizedActorId, w + '.unauthorizedActorId')
  if (actorId === unauthorizedActorId) refuse('EXACT', w + '.actorId and unauthorizedActorId must differ')
  nonNilUuid(str(a, 'companyId', w), w + '.companyId')
  nonNilUuid(str(a, 'codeGroupId', w), w + '.codeGroupId')
  exact(a, 'browserPrefix', 'PRODUCT-U0102-' + suffix, w)
  validateSource(a.source, w + '.source')
  exact(a, 'erpAccepted', false, w)
  exact(a, 'productionActivated', false, w)
  return a
}

/** The publisher's observed binding: same shape, same values, plus the ownership record. */
export function validateSourceBinding(binding: unknown, accepted: Json, acceptedContractSha256: string): Json {
  if (!isObject(binding)) return refuse('FIELD', 'source binding must be an object')
  const b = binding
  const w = 'sourceBinding'
  exact(b, 'contractId', accepted.contractId, w)
  exact(b, 'runId', accepted.runId, w)
  exact(b, 'instanceId', accepted.instanceId, w)
  exact(b, 'acceptedContractSha256', acceptedContractSha256, w)
  const observed = validateSource(b.source, w + '.source')
  if (canonical(observed) !== canonical(accepted.source)) refuse('SOURCE_MISMATCH', 'the observed source object differs from the accepted contract (expected-versus-observed)')
  if (!Number.isFinite(Date.parse(str(b, 'verifiedAtUtc', w)))) refuse('FORMAT', w + '.verifiedAtUtc is not a timestamp')
  const ownership = b.ownership
  if (!isObject(ownership)) return refuse('FIELD', w + '.ownership must be an object')
  const o = ownership
  const ow = w + '.ownership'
  exact(o, 'databaseName', accepted.databaseName, ow)
  exact(o, 'sqlEndpoint', accepted.sqlEndpoint, ow)
  exact(o, 'apiBaseUrl', accepted.apiBaseUrl, ow)
  exact(o, 'composeProject', accepted.composeProject, ow)
  exact(o, 'containerName', accepted.containerName, ow)
  exact(o, 'volumeName', accepted.volumeName, ow)
  exact(o, 'networkName', accepted.networkName, ow)
  exact(o, 'resourceRunId', accepted.runId, ow)
  exact(o, 'resourceInstanceId', accepted.instanceId, ow)
  matches(str(o, 'containerId', ow), HEX_ID_RE, ow + '.containerId')
  matches(str(o, 'networkId', ow), HEX_ID_RE, ow + '.networkId')
  if (!Number.isInteger(o.hostPid) || (o.hostPid as number) <= 0) refuse('FIELD', ow + '.hostPid must be a positive integer')
  if (!Number.isFinite(Date.parse(str(o, 'hostStartTimeUtc', ow)))) refuse('FORMAT', ow + '.hostStartTimeUtc is not a timestamp')
  return b
}

/** `environment-ready.json`: the repeated fields, the readiness flag, and the hashes of the two siblings. */
export function validateReady(ready: unknown, accepted: Json, acceptedContractSha256: string, sourceBindingSha256: string, sourceBinding: Json): Json {
  if (!isObject(ready)) return refuse('FIELD', 'environment-ready must be an object')
  const r = ready
  const w = 'ready'
  for (const name of ['contractId', 'taskId', 'environmentName', 'runId', 'instanceId', 'apiBaseUrl', 'actor', 'unauthorizedActor', 'companyId', 'codeGroupId', 'browserPrefix']) exact(r, name, accepted[name], w)
  exact(r, 'erpAccepted', false, w)
  exact(r, 'productionActivated', false, w)
  exact(r, 'readyForBrowser', true, w)
  exact(r, 'acceptedContractSha256', acceptedContractSha256, w)
  exact(r, 'sourceBindingSha256', sourceBindingSha256, w)
  exact(r, 'verifiedAtUtc', sourceBinding.verifiedAtUtc, w)
  return r
}

export function observeFrontendWithGit(worktree: string): FrontendObservation {
  const git = (args: string[]) => {
    const result = spawnSync('git', ['-C', worktree, ...args], { encoding: 'utf8' })
    if (result.status !== 0) throw new ManifestValidationError('FRONTEND_UNOBSERVED', 'git ' + args.join(' ') + ' failed: ' + String(result.stderr).slice(0, 120))
    return result.stdout
  }
  const head = git(['rev-parse', 'HEAD']).trim()
  const dirty = git(['status', '--porcelain', '--untracked-files=all', '--', ...FRONTEND_DIFF_PATHS]).split('\n').map((l) => l.trim()).filter((l) => l.length > 0)
  return { worktree, head, dirtyPaths: dirty }
}

function samePath(a: string, b: string): boolean {
  return path.resolve(a).toLowerCase() === path.resolve(b).toLowerCase()
}

/**
 * Runner role (contract steps 1–2): validate the three files and the handoff hash, then
 * freeze the snapshot. Called BEFORE the C010 run/cache claim.
 */
export function establishLiveBinding(env: Env, frontendWorktree: string, uiOrigin: string, options: ValidatorOptions = {}): LiveBinding {
  const read = options.readFile ?? ((file: string) => readFileSync(file))
  const base = options.acceptanceRootBase ?? ACCEPTANCE_ROOT_BASE
  const observe = options.observeFrontend ?? observeFrontendWithGit

  const manifestPath = env.URUN_ENVIRONMENT_MANIFEST
  if (!manifestPath || !path.isAbsolute(manifestPath)) refuse('INPUT', 'URUN_ENVIRONMENT_MANIFEST must be the absolute path of environment-ready.json; it is never defaulted')
  if (path.basename(manifestPath as string) !== READY_FILE) refuse('INPUT', 'URUN_ENVIRONMENT_MANIFEST must point at ' + READY_FILE)
  const handoffSha = env.URUN_ACCEPTED_CONTRACT_SHA256
  if (!handoffSha || !SHA256_RE.test(handoffSha)) refuse('INPUT', 'URUN_ACCEPTED_CONTRACT_SHA256 (the Orchestrator-recorded accepted contract hash) is required; a manifest never approves itself')
  if (uiOrigin !== UI_ORIGIN) refuse('ENDPOINT', 'this run serves ' + uiOrigin + ', the contract UI origin is ' + UI_ORIGIN)

  const dir = path.dirname(manifestPath as string)
  const acceptedContractPath = path.join(dir, ACCEPTED_FILE)
  const sourceBindingPath = path.join(dir, SOURCE_BINDING_FILE)
  let readyBytes: Buffer
  let acceptedBytes: Buffer
  let bindingBytes: Buffer
  try {
    readyBytes = read(manifestPath as string)
    acceptedBytes = read(acceptedContractPath)
    bindingBytes = read(sourceBindingPath)
  } catch (error) {
    return refuse('INPUT', 'a required file could not be read: ' + String((error as Error)?.message ?? error).slice(0, 160))
  }
  const acceptedContractSha256 = sha256(acceptedBytes)
  if (acceptedContractSha256 !== handoffSha) refuse('ACCEPTED_HASH', 'accepted-contract.json bytes do not hash to the Orchestrator-recorded accepted contract hash')
  const sourceBindingSha256 = sha256(bindingBytes)
  const manifestSha256 = sha256(readyBytes)
  let ready: unknown
  let acceptedRaw: unknown
  let bindingRaw: unknown
  try {
    ready = JSON.parse(readyBytes.toString('utf8'))
    acceptedRaw = JSON.parse(acceptedBytes.toString('utf8'))
    bindingRaw = JSON.parse(bindingBytes.toString('utf8'))
  } catch (error) {
    return refuse('INPUT', 'a required file is not JSON: ' + String((error as Error)?.message ?? error).slice(0, 160))
  }

  const accepted = validateAcceptedContract(acceptedRaw, base)
  if (!samePath(dir, accepted.acceptanceRoot as string)) refuse('ROOT', 'environment-ready.json is not inside the accepted acceptanceRoot')
  const sourceBinding = validateSourceBinding(bindingRaw, accepted, acceptedContractSha256)
  validateReady(ready, accepted, acceptedContractSha256, sourceBindingSha256, sourceBinding)

  // The frontend this server will serve is the approved one, and nothing else.
  const source = accepted.source as Json
  if (!samePath(frontendWorktree, source.frontendWorktree as string)) refuse('FRONTEND', 'this browser worktree is not source.frontendWorktree')
  const fe = observe(frontendWorktree)
  if (fe.head !== source.frontendHead) refuse('FRONTEND', 'this worktree HEAD ' + fe.head + ' is not source.frontendHead ' + String(source.frontendHead))
  if (fe.dirtyPaths.length > 0) refuse('FRONTEND', 'the served trees are not clean: ' + fe.dirtyPaths.slice(0, 3).join(', '))

  return {
    manifestPath: manifestPath as string, manifestSha256, acceptedContractPath, acceptedContractSha256, sourceBindingPath, sourceBindingSha256,
    verifiedAtUtc: sourceBinding.verifiedAtUtc as string,
    contractId: accepted.contractId as string, taskId: accepted.taskId as string, environmentName: accepted.environmentName as string,
    resourceRunId: accepted.runId as string, resourceInstanceId: accepted.instanceId as string,
    apiBaseUrl: accepted.apiBaseUrl as string, uiOrigin: accepted.uiOrigin as string,
    actor: accepted.actor as string, actorId: accepted.actorId as string,
    unauthorizedActor: accepted.unauthorizedActor as string, unauthorizedActorId: accepted.unauthorizedActorId as string,
    companyId: accepted.companyId as string, codeGroupId: accepted.codeGroupId as string, browserPrefix: accepted.browserPrefix as string,
    databaseName: accepted.databaseName as string, frontendHead: source.frontendHead as string,
  }
}

/** Member role (Vite web server, workers): take the frozen snapshot as handed; re-hash all three files before use. */
export function joinLiveBinding(env: Env, options: ValidatorOptions = {}): LiveBinding {
  const raw = env[BINDING_ENV]
  if (!raw) refuse('BINDING', BINDING_ENV + ' is absent; a member process never establishes a binding itself')
  let binding: LiveBinding
  try { binding = JSON.parse(raw as string) } catch { return refuse('BINDING', BINDING_ENV + ' is not JSON') }
  const read = options.readFile ?? ((file: string) => readFileSync(file))
  const check = (file: string, expected: string, name: string) => {
    let bytes: Buffer
    try { bytes = read(file) } catch (error) { return refuse('INPUT', name + ' could not be re-read: ' + String((error as Error)?.message ?? error).slice(0, 120)) }
    if (sha256(bytes) !== expected) refuse('DRIFT', name + ' bytes changed after the run was bound')
  }
  check(binding.manifestPath, binding.manifestSha256, READY_FILE)
  check(binding.acceptedContractPath, binding.acceptedContractSha256, ACCEPTED_FILE)
  check(binding.sourceBindingPath, binding.sourceBindingSha256, SOURCE_BINDING_FILE)
  return binding
}

export function bindingEnvValue(binding: LiveBinding): string {
  return JSON.stringify(binding)
}
