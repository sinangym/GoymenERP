#!/usr/bin/env node
/**
 * Pre-L gate: verifies the C-DESKTOP-006 `environment.json` against the acceptance
 * contract, the REAL source/artifact bytes and the REAL running process — and only
 * then, if everything holds, touches the network (two read-only GETs on the pinned
 * loopback origin).
 *
 * Adapted to the C006 SOURCE SCHEMA published for schema pin `e35ad38` (#233 comment
 * 5637562052). The envelope this gate used to read was a flat manifest of our own
 * naming — `backendHead`, `apiPidAndStartTime`, `allowedUiOrigins`,
 * `fixtureHashComponents`, `runtimeEvidence.assemblies` — and the publisher emits none
 * of those keys. Every one arrived as `undefined`, and `undefined === undefined` is how
 * a gate silently passes itself. Renaming them was not enough either: the frontend-diff
 * and fixture hashes are computed by different recipes than the ones this file used, so
 * the old checks would have compared correctly-named values that could never match.
 *
 * Phases, in order; a failure in any phase stops before the next one:
 *  A. static  — required keys; contract/task/run/instance/database identity across
 *               `sources`, `host` and `resources`; the distinct source pins; strict
 *               `http://127.0.0.1:<contract port>` API origin; strict
 *               `127.0.0.1,<contract port>` SQL endpoint; the source-pinned UI port;
 *               the loopback-only Docker port binding; no credential material.
 *  B. byte    — the browser worktree's Git HEAD; the COMMITTED frontend+harness diff
 *               recomputed over base..head; worktree cleanliness (separate from that
 *               diff); the backend HEAD, `HEAD:src/backend` tree OID, OpenAPI hash and
 *               committed migration inventory; the smoke/baseline artifacts; the
 *               fixture composite; the startup disk artifacts and the local backend
 *               test summary — all COMPUTED, never copied from the envelope.
 *  C. runtime — attestation freshness; the OS listener owning the API port; that
 *               process's PID, creation time (PID-reuse guard) and the served DLL,
 *               hashed against the artifacts observed on disk.
 *  D. live    — GET /health/live and GET /api/v1/identity/me, each target checked
 *               against the pinned origin, `redirect: 'error'`, 5 s timeout.
 *
 * Outcomes: passed (0) · preflight_failed / failed (1) · not_verified (2 — something
 * the gate must see could not be observed; never reported as passed).
 * A fresh report is written at the start of every run so a stale verdict cannot stand.
 *
 * usage: node verify-manifest.mjs <contract.json> <environment.json> <evidenceDir> <expectedFrontendDiffSha256> <uiOrigin>
 *        [--worktree <browserWorktree>] [--backend-worktree <dir>] [--frontend-diff-base <commit>]
 *        [--max-attestation-age-hours <n>] [--report-name <file>]
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  extractDllPath,
  gitCommittedDiffSha256,
  gitDiffSha256,
  gitHead,
  gitMigrationSourcesSha256,
  gitTreeOid,
  isoDiffSeconds,
  observeListener,
  observeProcess,
  resolveUnder,
  sha256File,
  sha256Hex,
  validateLoopbackHttpOrigin,
  validateSqlLoopbackEndpoint,
} from './gate-observations.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
export const DEFAULT_MAX_ATTESTATION_AGE_HOURS = 24
const START_TOLERANCE_SECONDS = 2
const HTTP_TIMEOUT_MS = 5000
// Credential words assembled from fragments so this source never carries them literally.
const credentialWords = new RegExp(['pass' + 'word', 'sec' + 'ret', 'tok' + 'en', 'p' + 'wd'].join('|'), 'i')
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const SHA256_RE = /^[0-9a-f]{64}$/i
const OID_RE = /^[0-9a-f]{40}$/

/** The counterparty handshake this browser speaks; the envelope must name the same one. */
export const CLIENT_CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
export const CLIENT_CONTRACT_VERSION = 'optional-trade-roles-v1'

/** The committed range the source binding's `FrontendDiffSha256` covers — harness INCLUDED. */
export const FRONTEND_DIFF_PATHS = Object.freeze(['src/frontend/goymen-erp-web', 'tests/e2e'])

/** Docker labels that bind a runtime resource to this task/run/instance/contract. */
const LABEL = Object.freeze({
  task: 'com.goymen.acceptance.task',
  run: 'com.goymen.acceptance.run',
  instance: 'com.goymen.acceptance.instance',
  contract: 'com.goymen.acceptance.contract',
})

export function defaultObservers() {
  return {
    gitHead,
    gitDiffSha256,
    gitCommittedDiffSha256,
    gitTreeOid,
    gitMigrationSourcesSha256,
    sha256File,
    fileExists: (file) => existsSync(file),
    observeListener,
    observeProcess,
  }
}

/** The pinned API origin is the ONLY host the gate may contact; every target is checked. */
export function assertLoopbackTarget(urlString, allowedOrigin) {
  const url = new URL(urlString)
  if (url.origin !== allowedOrigin) throw new Error(`target ${url.origin} is outside the pinned loopback origin ${allowedOrigin}`)
  if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1') throw new Error('target is not plain loopback http')
  return url
}

/**
 * The publisher's fixture recipe, in its exact order.
 *
 * Three `<repo-relative path>=<raw file SHA-256>` lines, each terminated by LF,
 * concatenated and hashed as UTF-8 — the final LF included. NOT sorted JSON, NOT
 * basename keys, and `baseline.json` is not one of the components. The envelope has no
 * `fixtureHashComponents` field to read them back from, so they are hashed from the
 * working-copy bytes with no line-ending normalisation: normalising would make a CRLF
 * checkout hash the same as an LF one, which is the drift this exists to catch.
 */
export const FIXTURE_COMPONENT_PATHS = Object.freeze([
  'tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs',
  'tools/GoymenERP.CariAcceptance006/AcceptanceProfile.cs',
  'tools/cari-acceptance-006/run.json',
])

export function fixtureCompositeOf(hashByPath) {
  const lines = FIXTURE_COMPONENT_PATHS.map((component) => `${component}=${hashByPath[component]}\n`)
  return sha256Hex(Buffer.from(lines.join(''), 'utf8'))
}

/** Order-independent structural equality, for `host.sourceBinding` against `sources`. */
export function canonicalJson(value) {
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`
  if (value !== null && typeof value === 'object') {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(',')}}`
  }
  return JSON.stringify(value === undefined ? null : value)
}

export async function runVerification(options) {
  const {
    contractFile,
    manifestFile,
    evidenceDir,
    expectedFrontendDiff,
    uiOrigin,
    worktreeDir = path.resolve(here, '../../..'),
    maxAttestationAgeHours = DEFAULT_MAX_ATTESTATION_AGE_HOURS,
    now = () => Date.now(),
    fetchImpl = globalThis.fetch,
    observers = defaultObservers(),
    reportName = 'manifest-verification.json',
  } = options
  const reportPath = path.join(evidenceDir, 'L', reportName)
  const startedAtUtc = new Date(now()).toISOString()
  const checks = []
  const add = (kind, name, status, detail) => checks.push({ kind, name, status, detail: detail === undefined ? null : detail })
  const ok = (kind, name, condition, detail) => add(kind, name, condition ? 'ok' : 'fail', detail)
  let httpRequests = 0
  let httpAttempted = false
  const observed = {}

  const writeReport = (outcome) => {
    const verdict = {
      tool: 'verify-manifest.mjs',
      schemaPin: 'C006 source schema (e35ad38)',
      startedAtUtc,
      verifiedAtUtc: new Date(now()).toISOString(),
      outcome,
      allPassed: outcome === 'passed',
      exitCode: outcome === 'passed' ? 0 : outcome === 'not_verified' ? 2 : 1,
      httpAttempted,
      httpRequests,
      manifestPath: path.resolve(manifestFile),
      manifestSha256: manifestBytes ? sha256Hex(manifestBytes) : null,
      contractId: contract?.id ?? null,
      runId: manifest?.runId ?? null,
      instanceId: manifest?.instanceId ?? null,
      pins: {
        expectedFrontendDiffSha256: expectedFrontendDiff,
        contractFrontendDiffSha256: contract?.claude?.frontendDiffSha256 ?? null,
        frontendDiffBase: options.frontendDiffBase ?? contract?.claude?.frontendDiffBase ?? null,
        contractBrowserHead: contract?.claude?.baseHead ?? null,
        contractAcceptanceHead: contract?.codex?.acceptanceHead ?? null,
        contractBackendDependency: contract?.codex?.baseHead ?? null,
      },
      observed,
      checks,
      summary: {
        ok: checks.filter((c) => c.status === 'ok').length,
        fail: checks.filter((c) => c.status === 'fail').length,
        notVerified: checks.filter((c) => c.status === 'not_verified').length,
        declaredOnly: checks.filter((c) => c.kind === 'declared').length,
      },
      note: 'Declared flags are recorded but never count as byte or runtime evidence; byte and runtime checks are computed from files, Git and the OS. Startup artifact hashes are DISK hashes, not memory-image attestation, and the migration hash is a committed source inventory, not a live database schema.',
    }
    mkdirSync(path.dirname(reportPath), { recursive: true })
    writeFileSync(reportPath, `${JSON.stringify(verdict, null, 2)}\n`, 'utf8')
    return verdict
  }

  let contract = null
  let manifest = null
  let manifestBytes = null
  writeReport('incomplete')
  try {
    contract = JSON.parse(readFileSync(contractFile, 'utf8'))
    manifestBytes = readFileSync(manifestFile)
    manifest = JSON.parse(manifestBytes.toString('utf8'))
  } catch (error) {
    add('static', 'contract and manifest readable', 'fail', String(error?.message ?? error).slice(0, 160))
    return finish('preflight_failed')
  }

  // ---- A. static preflight ------------------------------------------------------------
  /**
   * The nested records the publisher actually writes: `sources` is the runtime source
   * binding, `host` the started-host receipt, `resources` the Docker resources. Case
   * differs between them on purpose — the identity fields in `sources` and `host` are
   * Pascal case while `host.pid` and everything under `resources` is not — and a wrong
   * case reads as `undefined`, which compares equal to the next missing field.
   */
  const sources = manifest.sources ?? {}
  const host = manifest.host ?? {}
  const resources = manifest.resources ?? {}
  const container = resources.container ?? {}
  const backendTests = manifest.backendTests ?? {}

  const required = Array.isArray(contract.environmentManifestRequired) ? contract.environmentManifestRequired : []
  const missing = required.filter((k) => manifest[k] === undefined)
  ok('static', 'required keys present', required.length > 0 && missing.length === 0, missing.length ? `missing: ${missing.join(', ')}` : `${required.length} keys`)
  ok('static', 'contractId matches', manifest.contractId === contract.id, manifest.contractId)
  ok('static', 'taskId is the contract codex task', manifest.taskId === contract.codex?.taskId, manifest.taskId)
  ok('static', 'readyForBrowser is true', manifest.readyForBrowser === true, String(manifest.readyForBrowser))

  /**
   * Three DIFFERENT commits, and conflating any two of them is what this guards:
   * `AcceptanceHead` is the approved C006 checkout, `CompiledHead` the revision baked
   * into the assembly built from it, and `BackendDependency` the locked API commit the
   * environment serves. A C006 build from any other commit would satisfy a check that
   * treated them as aliases.
   */
  ok('static', 'sources.AcceptanceHead equals the approved C006 pin', OID_RE.test(String(sources.AcceptanceHead ?? '')) && sources.AcceptanceHead === contract.codex?.acceptanceHead, sources.AcceptanceHead)
  ok('static', 'sources.CompiledHead equals AcceptanceHead', typeof sources.CompiledHead === 'string' && sources.CompiledHead === sources.AcceptanceHead, sources.CompiledHead)
  ok('static', 'sources.BackendDependency equals the locked API pin and is NOT the C006 head', OID_RE.test(String(sources.BackendDependency ?? '')) && sources.BackendDependency === contract.codex?.baseHead && sources.BackendDependency !== sources.AcceptanceHead, sources.BackendDependency)
  ok('static', 'sources.FrontendHead equals the contract FE pin', OID_RE.test(String(sources.FrontendHead ?? '')) && sources.FrontendHead === contract.claude?.baseHead, sources.FrontendHead)
  ok(
    'static',
    'sources.FrontendDiffSha256 equals the expected pin and the contract',
    typeof expectedFrontendDiff === 'string' && SHA256_RE.test(expectedFrontendDiff) && sources.FrontendDiffSha256 === expectedFrontendDiff && contract.claude?.frontendDiffSha256 === expectedFrontendDiff,
    sources.FrontendDiffSha256,
  )
  ok('static', 'source binding carries the tree, OpenAPI and migration-inventory pins', OID_RE.test(String(sources.BackendTree ?? '')) && SHA256_RE.test(String(sources.OpenApiSha256 ?? '')) && SHA256_RE.test(String(sources.MigrationSourcesSha256 ?? '')), `tree ${sources.BackendTree}`)

  const apiCheck = validateLoopbackHttpOrigin(manifest.apiBaseUrl, { requiredPort: contract.suggestedPorts?.api })
  ok('static', 'apiBaseUrl is strictly http://127.0.0.1:<contract api port>', apiCheck.ok, apiCheck.ok ? apiCheck.origin : apiCheck.reason)
  ok('static', 'host.ApiBaseUrl equals apiBaseUrl', host.ApiBaseUrl === manifest.apiBaseUrl, host.ApiBaseUrl)
  ok('static', 'host and sources agree with the envelope run/instance/database identity',
    host.RunId === manifest.runId && host.InstanceId === manifest.instanceId
      && sources.RunId === manifest.runId && sources.InstanceId === manifest.instanceId
      && sources.DatabaseName === manifest.databaseName && sources.ApiBaseUrl === manifest.apiBaseUrl,
    `${host.RunId} / ${host.InstanceId}`)
  // The host receipt embeds the whole source record; a host started from a binding other
  // than the published one is exactly what this catches.
  ok('static', 'host.sourceBinding is the same source record as sources', canonicalJson(host.sourceBinding) === canonicalJson(manifest.sources), host.sourceBinding === undefined ? 'not present' : 'identical')
  ok('static', 'host declares a PID, a start time and an observation time', Number(host.pid) > 0 && Number.isFinite(Date.parse(host.startTimeUtc ?? '')) && Number.isFinite(Date.parse(host.observedAtUtc ?? '')), `pid ${host.pid} started ${host.startTimeUtc}`)
  ok('static', 'sources.FixtureSha256 equals fixtureHash', SHA256_RE.test(String(manifest.fixtureHash ?? '')) && sources.FixtureSha256 === manifest.fixtureHash, sources.FixtureSha256)

  const sqlCheck = validateSqlLoopbackEndpoint(sources.SqlEndpoint, { requiredPort: contract.suggestedPorts?.sql })
  ok('static', 'sources.SqlEndpoint is strictly 127.0.0.1,<contract sql port>', sqlCheck.ok, sqlCheck.ok ? `${sqlCheck.host},${sqlCheck.port}` : sqlCheck.reason)
  /**
   * The UI origin is a SOURCE-profile condition, not an envelope field.
   *
   * The publisher emits no `allowedUiOrigins`, `uiPort` or `uiBaseUrl`. The permitted
   * origins are pinned inside the isolated Testing host by `AcceptanceFactory.cs` —
   * exactly `http://127.0.0.1:25173` and `http://localhost:25173` — so the API itself
   * refuses a UI served anywhere else. Asserting an envelope field here would have
   * invented one; the gate binds the pinned port instead, so a UI that moved to another
   * port because this one was occupied fails rather than passing quietly.
   */
  const uiCheck = validateLoopbackHttpOrigin(uiOrigin, { requiredPort: contract.suggestedPorts?.ui })
  ok('static', 'UI origin is strictly loopback on the source-pinned UI port (the envelope has no UI field)', uiCheck.ok, uiCheck.ok ? uiOrigin : uiCheck.reason)

  ok('static', 'databaseName present', typeof manifest.databaseName === 'string' && manifest.databaseName.length > 0, manifest.databaseName)
  ok('static', 'runId and instanceId present', typeof manifest.runId === 'string' && manifest.runId.length > 0 && typeof manifest.instanceId === 'string' && manifest.instanceId.length > 0, manifest.runId)
  ok('static', 'company ids are distinct UUIDs', UUID_RE.test(manifest.companyAId ?? '') && UUID_RE.test(manifest.companyBId ?? '') && manifest.companyAId !== manifest.companyBId, `${manifest.companyAId} / ${manifest.companyBId}`)
  ok('static', 'browserPrefix and actor present', typeof manifest.browserPrefix === 'string' && manifest.browserPrefix.length > 0 && typeof manifest.actor === 'string' && manifest.actor.length > 0, `${manifest.browserPrefix} as ${manifest.actor}`)
  // The isolated Testing host authenticates by `X-Development-User` and no credential is
  // supplied, so the whole envelope is scanned rather than one bootstrap sub-object.
  ok('static', 'the envelope carries no credential material', manifestBytes.toString('utf8').match(credentialWords) === null, 'header-based development bootstrap only')
  ok('static', 'the envelope declares the counterparty client contract this browser speaks', manifest.clientContractHeader === CLIENT_CONTRACT_HEADER && manifest.clientContractVersion === CLIENT_CONTRACT_VERSION, `${manifest.clientContractHeader}: ${manifest.clientContractVersion}`)
  // Browser readiness is not ERP acceptance, and the publisher never claims it is.
  ok('static', 'the envelope claims neither browser acceptance, ERP acceptance nor production activation',
    manifest.browserAcceptance === 'NOT_RUN' && manifest.erpAccepted === false && manifest.productionActivated === false,
    `${manifest.browserAcceptance} / erpAccepted=${manifest.erpAccepted} / productionActivated=${manifest.productionActivated}`)

  /**
   * Runtime resources are identified by their runtime IDs and bound by labels: a name is
   * not an identity, Docker's container `.Image` content id is not the registry digest
   * pinned in run.json, and the SQL port must reach loopback and nothing else.
   */
  const labelsBind = (entry) => {
    const labels = entry && typeof entry.labels === 'object' && entry.labels !== null ? entry.labels : {}
    return labels[LABEL.task] === manifest.taskId && labels[LABEL.run] === manifest.runId
      && labels[LABEL.instance] === manifest.instanceId && labels[LABEL.contract] === manifest.contractId
  }
  ok('static', 'resources agree with the envelope run/instance/database identity',
    resources.runId === manifest.runId && resources.instanceId === manifest.instanceId
      && resources.databaseName === manifest.databaseName && typeof resources.composeProject === 'string' && resources.composeProject.length > 0,
    resources.composeProject)
  ok('static', 'container, volume and network labels bind this task, run, instance and contract',
    labelsBind(container) && labelsBind(resources.volume) && labelsBind(resources.network), 'four acceptance labels on all three')
  ok('static', 'container and network are identified by runtime ids, not names', /^[0-9a-f]{12,64}$/.test(String(container.id ?? '')) && /^[0-9a-f]{12,64}$/.test(String(resources.network?.id ?? '')) && typeof resources.volume?.name === 'string' && resources.volume.name.length > 0, String(container.id ?? '').slice(0, 12))
  ok('static', 'container.image is an observed content id, not a registry digest string', /^sha256:[0-9a-f]{64}$/.test(String(container.image ?? '')), container.image)
  const sqlBinding = (container.ports ?? {})['1433/tcp']
  ok('static', 'the SQL container port is published to loopback on the contract sql port only',
    Array.isArray(sqlBinding) && sqlBinding.length === 1 && sqlBinding[0]?.HostIp === '127.0.0.1' && Number(sqlBinding[0]?.HostPort) === Number(contract.suggestedPorts?.sql),
    JSON.stringify(sqlBinding ?? null))

  ok('static', 'backendTests records an executed local verify summary', typeof backendTests.path === 'string' && backendTests.path.length > 0 && SHA256_RE.test(String(backendTests.sha256 ?? '')) && Number(backendTests.executed) > 0 && Number(backendTests.publisherExecuted) > 0, `${backendTests.executed} executed / ${backendTests.publisherExecuted} publisher tests`)

  /**
   * The publisher's own pre-handover observations. They are recorded, never credited:
   * this browser context still needs its OWN fresh, successful, validated GET carrying
   * the acknowledgment before it may write, and an acknowledgment on an error is not one.
   */
  const fresh = Array.isArray(manifest.freshGetObservations) ? manifest.freshGetObservations : []
  add('declared', 'freshGetObservations recorded by the publisher for both companies',
    fresh.length >= 2 && [manifest.companyAId, manifest.companyBId].every((id) => fresh.some((o) => o.companyId === id && o.status === 200 && o.ack === manifest.clientContractVersion)) ? 'ok' : 'fail',
    'declaration only — this browser context still needs its own fresh acknowledged GET; these never grant readiness')
  add('declared', 'sources.SourcesClean (declared by the environment owner)', sources.SourcesClean === true ? 'ok' : 'fail', 'declaration only — bound below by the byte and live OS observations')
  if (checks.some((c) => c.status === 'fail')) return finish('preflight_failed')

  // ---- B. byte identity (computed) -----------------------------------------------------
  try {
    const head = observers.gitHead(worktreeDir)
    observed.browserHead = head
    ok('byte', 'browser worktree Git HEAD equals the contract FE pin and sources.FrontendHead', head === contract.claude?.baseHead && head === sources.FrontendHead, head)
  } catch (error) {
    add('byte', 'browser worktree Git HEAD equals the contract FE pin and sources.FrontendHead', 'not_verified', String(error?.message ?? error).slice(0, 120))
  }
  /**
   * `FrontendDiffSha256` is a COMMITTED range — base..head over the frontend and the
   * whole `tests/e2e` tree, harness INCLUDED. The old worktree diff (HEAD against a
   * dirty tree, harness excluded) answers a different question and cannot stand in for
   * it. The base is not serialized in the envelope, so it comes from the pinned contract
   * or the CLI, and without it this is reported unobserved rather than assumed.
   */
  const frontendDiffBase = options.frontendDiffBase ?? contract.claude?.frontendDiffBase ?? null
  if (!frontendDiffBase) {
    add('byte', 'committed frontend+harness diff recomputed equals sources.FrontendDiffSha256', 'not_verified', 'no frontend diff base supplied (contract.claude.frontendDiffBase or --frontend-diff-base)')
  } else {
    try {
      const diff = observers.gitCommittedDiffSha256(worktreeDir, { base: frontendDiffBase, head: sources.FrontendHead, paths: [...FRONTEND_DIFF_PATHS] })
      observed.frontendDiffSha256 = diff.sha256
      observed.frontendDiffBytes = diff.bytes
      ok('byte', 'committed frontend+harness diff recomputed equals sources.FrontendDiffSha256', diff.sha256 === sources.FrontendDiffSha256 && diff.sha256 === expectedFrontendDiff, `${diff.sha256} (${diff.bytes} bytes over ${String(frontendDiffBase).slice(0, 8)}..${String(sources.FrontendHead).slice(0, 8)})`)
    } catch (error) {
      add('byte', 'committed frontend+harness diff recomputed equals sources.FrontendDiffSha256', 'not_verified', String(error?.message ?? error).slice(0, 120))
    }
  }
  // Cleanliness is a SEPARATE observation from the committed diff: a clean worktree does
  // not prove the pinned range, and the pinned range does not prove nothing was edited.
  try {
    const dirty = observers.gitDiffSha256(worktreeDir, {})
    observed.browserWorktreeDirtyBytes = dirty.bytes
    ok('byte', 'browser worktree is clean at the pinned head', dirty.bytes === 0, `${dirty.bytes} bytes (tracked modified ${dirty.trackedModified}, untracked ${dirty.untracked})`)
  } catch (error) {
    add('byte', 'browser worktree is clean at the pinned head', 'not_verified', String(error?.message ?? error).slice(0, 120))
  }

  const backendDir = options.backendWorktreeDir ?? sources.Worktree ?? manifest.worktree ?? contract.codex?.cwd ?? null
  if (backendDir && observers.fileExists(backendDir)) {
    try {
      const backendHead = observers.gitHead(backendDir)
      observed.backendHead = backendHead
      ok('byte', 'backend worktree Git HEAD equals the approved C006 pin', backendHead === sources.AcceptanceHead && backendHead === contract.codex?.acceptanceHead, backendHead)
      const tree = observers.gitTreeOid(backendDir, 'HEAD:src/backend')
      observed.backendTree = tree
      ok('byte', 'HEAD:src/backend tree OID equals sources.BackendTree', tree === sources.BackendTree, tree)
      // A committed source inventory. Not a live DDL hash, and never reported as one.
      const migrations = observers.gitMigrationSourcesSha256(backendDir, {})
      observed.migrationSourcesSha256 = migrations.sha256
      ok('byte', 'committed migration inventory recomputed equals sources.MigrationSourcesSha256', migrations.sha256 === sources.MigrationSourcesSha256, `${migrations.sha256} (${migrations.lines} migration blobs)`)
      // `backendDiffSha256`/`backendDiffBytes` are not emitted by this publisher, and an
      // exact head plus a clean tree is not an alias for that old hash: what is checked
      // here is that the checkout carries no uncommitted change, with its own bytes.
      const backendDiff = observers.gitDiffSha256(backendDir, {})
      observed.backendWorktreeDirtyBytes = backendDiff.bytes
      ok('byte', 'backend worktree is clean at the approved C006 pin', backendDiff.bytes === 0, `${backendDiff.sha256} (${backendDiff.bytes} bytes)`)
    } catch (error) {
      add('byte', 'backend worktree Git HEAD, tree and inventory', 'not_verified', String(error?.message ?? error).slice(0, 120))
    }
    const openApi = resolveUnder(backendDir, 'contracts/openapi/goymen-erp-v1.json')
    if (observers.fileExists(openApi)) {
      const actual = observers.sha256File(openApi)
      observed.openApiSha256 = actual
      ok('byte', 'OpenAPI document hash equals sources.OpenApiSha256', actual === sources.OpenApiSha256, actual)
    } else {
      add('byte', 'OpenAPI document hash equals sources.OpenApiSha256', 'not_verified', openApi)
    }
  } else {
    add('byte', 'backend worktree Git HEAD, tree and inventory', 'not_verified', `backend worktree not available: ${backendDir}`)
  }

  /**
   * `smoke.json` and `baseline.json` are real files beside the real `environment.json`;
   * the envelope carries their raw hashes and no path/passed wrapper. No health artifact
   * is emitted and none is invented here — a real health observation is the live probe
   * in phase D.
   */
  const manifestDir = path.dirname(path.resolve(manifestFile))
  for (const [file, declared] of [['smoke.json', manifest.smokeSha256], ['baseline.json', manifest.baselineSha256]]) {
    const full = path.join(manifestDir, file)
    if (typeof declared !== 'string') {
      add('byte', `${file} hash equals the envelope`, 'fail', `${file} hash is not declared`)
      continue
    }
    if (!observers.fileExists(full)) {
      add('byte', `${file} hash equals the envelope`, 'not_verified', full)
      continue
    }
    const actual = observers.sha256File(full)
    ok('byte', `${file} hash equals the envelope`, actual === declared, actual)
  }

  const componentHashes = {}
  let componentsComplete = true
  for (const component of FIXTURE_COMPONENT_PATHS) {
    const file = backendDir ? resolveUnder(backendDir, component) : null
    if (!file || !observers.fileExists(file)) {
      componentsComplete = false
      add('byte', `fixture component ${component}`, 'not_verified', file ?? 'backend worktree unavailable')
      continue
    }
    componentHashes[component] = observers.sha256File(file)
  }
  if (componentsComplete) {
    const composite = fixtureCompositeOf(componentHashes)
    observed.fixtureHashRecomputed = composite
    ok('byte', 'fixtureHash recomputed by the publisher recipe equals the envelope', composite === manifest.fixtureHash, composite)
  } else {
    add('byte', 'fixtureHash recomputed by the publisher recipe equals the envelope', 'not_verified', 'fixture components incomplete')
  }

  /**
   * Startup DISK artifacts: hashes of files on disk, captured before Kestrel started.
   * They are explicitly not a memory-image attestation, and the report says so. Where the
   * basename map in `sources.DiskArtifacts` overlaps them it must agree.
   */
  const startupArtifacts = Array.isArray(host.startupDiskArtifacts) ? host.startupDiskArtifacts : []
  const diskArtifacts = sources.DiskArtifacts && typeof sources.DiskArtifacts === 'object' ? sources.DiskArtifacts : {}
  const artifactHashes = new Map()
  if (startupArtifacts.length === 0) add('byte', 'recorded startup disk artifacts', 'fail', 'host.startupDiskArtifacts is empty')
  for (const artifact of startupArtifacts) {
    const base = path.basename(String(artifact?.path ?? ''))
    const file = backendDir ? resolveUnder(backendDir, String(artifact?.path ?? '')) : null
    if (!file || !observers.fileExists(file)) {
      add('byte', `startup disk artifact ${base} hash`, 'not_verified', artifact?.path ?? 'no path')
      continue
    }
    const actual = observers.sha256File(file)
    artifactHashes.set(actual.toLowerCase(), base)
    const inSources = diskArtifacts[base]
    ok('byte', `startup disk artifact ${base} hash equals host and sources (disk bytes, not a memory image)`,
      actual === artifact.sha256 && (inSources === undefined || String(inSources).toLowerCase() === actual.toLowerCase()), actual)
  }

  // The local BackendVerify summary. Its path may legitimately be outside the checkout,
  // so it is used exactly as published — never re-rooted into the worktree to make it
  // resolve, and never assumed present.
  if (typeof backendTests.path === 'string' && backendTests.path.length > 0) {
    const summaryFile = backendDir ? resolveUnder(backendDir, backendTests.path) : backendTests.path
    if (observers.fileExists(summaryFile)) {
      const actual = observers.sha256File(summaryFile)
      observed.backendTestsSummarySha256 = actual
      ok('byte', 'backendTests summary file hash equals the envelope', actual === backendTests.sha256, actual)
    } else {
      add('byte', 'backendTests summary file hash equals the envelope', 'not_verified', summaryFile)
    }
  }
  if (checks.some((c) => c.status === 'fail')) return finish('failed')

  // ---- C. runtime identity (observed on this machine) ----------------------------------------
  const nowMs = now()
  const ageHours = (iso) => (nowMs - Date.parse(iso)) / 3_600_000
  const observedAge = ageHours(host.observedAtUtc)
  const preparedAge = ageHours(manifest.preparedAtUtc)
  ok('runtime', `attestation not older than ${maxAttestationAgeHours} h`, Number.isFinite(observedAge) && observedAge >= 0 && observedAge <= maxAttestationAgeHours && Number.isFinite(preparedAge) && preparedAge <= maxAttestationAgeHours, `host ${observedAge.toFixed(2)} h, envelope ${preparedAge.toFixed(2)} h`)
  /**
   * There is no persisted listener record in this envelope, so the declared PID is
   * checked against the OS rather than against another field the same publisher wrote at
   * the same moment.
   */
  const declaredPid = Number(host.pid)
  let listener = null
  try {
    listener = observers.observeListener(apiCheck.port)
  } catch (error) {
    add('runtime', 'loopback listener on the API port observed', 'not_verified', String(error?.message ?? error).slice(0, 120))
  }
  if (listener !== null && listener !== undefined) {
    observed.listenerPid = listener.pid
    ok('runtime', 'loopback listener on the API port observed and owned by the declared PID', listener.pid === declaredPid && listener.localAddress === '127.0.0.1', `pid ${listener.pid} on ${listener.localAddress}:${apiCheck.port}`)
  } else if (!checks.some((c) => c.name.startsWith('loopback listener'))) {
    add('runtime', 'loopback listener on the API port observed and owned by the declared PID', 'fail', `no LISTENING socket on 127.0.0.1:${apiCheck.port}`)
  }
  let proc = null
  try {
    proc = observers.observeProcess(declaredPid)
  } catch (error) {
    add('runtime', 'declared PID is alive', 'not_verified', String(error?.message ?? error).slice(0, 120))
  }
  if (proc) {
    observed.processCreatedUtc = proc.createdUtc
    observed.processName = proc.name
    const skew = isoDiffSeconds(proc.createdUtc, host.startTimeUtc)
    ok('runtime', 'process creation time matches the declared start (PID-reuse guard)', skew <= START_TOLERANCE_SECONDS, `skew ${Number.isFinite(skew) ? skew.toFixed(3) : 'n/a'} s (${proc.createdUtc})`)
    const dll = extractDllPath(proc.commandLine)
    if (dll && observers.fileExists(dll)) {
      const dllHash = observers.sha256File(dll)
      observed.servedDllSha256 = dllHash
      ok('runtime', 'served assembly on the process command line is one of the artifacts hashed from disk', artifactHashes.has(dllHash.toLowerCase()), `${path.basename(dll)} ${dllHash}`)
    } else {
      add('runtime', 'served assembly on the process command line is one of the artifacts hashed from disk', 'not_verified', dll ? `dll not readable: ${dll}` : 'no dll on command line / command line unavailable')
    }
  } else if (!checks.some((c) => c.name === 'declared PID is alive')) {
    add('runtime', 'declared PID is alive', 'fail', `pid ${declaredPid} not running`)
  }
  if (checks.some((c) => c.status === 'fail')) return finish('failed')
  if (checks.some((c) => c.status === 'not_verified')) return finish('not_verified')

  // ---- D. live probes on the pinned origin only ----------------------------------------------
  httpAttempted = true
  const origin = apiCheck.origin
  const probe = async (target, init = {}) => {
    const url = assertLoopbackTarget(target, origin)
    httpRequests += 1
    // `Connection: close`: no keep-alive socket outlives the probe, so the process can
    // drain and exit with its real code instead of aborting inside libuv.
    return fetchImpl(url.href, { ...init, headers: { ...init.headers, Connection: 'close' }, redirect: 'error', signal: AbortSignal.timeout(HTTP_TIMEOUT_MS) })
  }
  try {
    const response = await probe(`${origin}/health/live`)
    const body = (await response.text()).slice(0, 100)
    observed.health = { status: response.status, body }
    ok('live', 'anonymous health probe returns 200', response.status === 200, `${response.status} ${body}`)
  } catch (error) {
    add('live', 'anonymous health probe returns 200', 'fail', String(error?.message ?? error).slice(0, 120))
  }
  try {
    const response = await probe(`${origin}/api/v1/identity/me`, { headers: { 'X-Development-User': manifest.actor } })
    const body = await response.json()
    observed.identity = { status: response.status, userId: body?.userId ?? null, allowedCompanyIds: body?.allowedCompanyIds ?? null }
    ok('live', 'browser actor identity covers A and B', response.status === 200 && Array.isArray(body?.allowedCompanyIds) && body.allowedCompanyIds.includes(manifest.companyAId) && body.allowedCompanyIds.includes(manifest.companyBId), `${response.status} ${body?.displayName ?? ''}`)
  } catch (error) {
    add('live', 'browser actor identity covers A and B', 'fail', String(error?.message ?? error).slice(0, 120))
  }
  return finish(checks.some((c) => c.status === 'fail') ? 'failed' : 'passed')

  function finish(outcome) {
    const verdict = writeReport(outcome)
    return { verdict, exitCode: verdict.exitCode, reportPath }
  }
}

export function parseCliArgs(argv) {
  const positional = []
  const flags = {}
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i]
    if (arg.startsWith('--')) {
      flags[arg.slice(2)] = argv[i + 1]
      i += 1
    } else positional.push(arg)
  }
  const [contractFile, manifestFile, evidenceDir, expectedFrontendDiff, uiOrigin] = positional
  return {
    contractFile,
    manifestFile,
    evidenceDir,
    expectedFrontendDiff,
    uiOrigin,
    worktreeDir: flags.worktree,
    backendWorktreeDir: flags['backend-worktree'],
    frontendDiffBase: flags['frontend-diff-base'],
    maxAttestationAgeHours: flags['max-attestation-age-hours'] !== undefined ? Number(flags['max-attestation-age-hours']) : undefined,
    reportName: flags['report-name'],
  }
}

async function main() {
  const args = parseCliArgs(process.argv.slice(2))
  if (!args.contractFile || !args.manifestFile || !args.evidenceDir || !args.expectedFrontendDiff || !args.uiOrigin) {
    console.error('usage: node verify-manifest.mjs <contract.json> <environment.json> <evidenceDir> <expectedFrontendDiffSha256> <uiOrigin> [--worktree d] [--backend-worktree d] [--frontend-diff-base c] [--max-attestation-age-hours n] [--report-name f]')
    process.exit(2)
  }
  const options = Object.fromEntries(Object.entries(args).filter(([, v]) => v !== undefined))
  const { verdict, exitCode } = await runVerification(options)
  for (const c of verdict.checks) console.log(`${c.status === 'ok' ? 'OK  ' : c.status === 'fail' ? 'FAIL' : 'N/V '} [${c.kind}] ${c.name}${c.detail ? ` — ${c.detail}` : ''}`)
  console.log(`outcome=${verdict.outcome} httpAttempted=${verdict.httpAttempted} httpRequests=${verdict.httpRequests} manifestSha256=${verdict.manifestSha256}`)
  // Let the event loop drain (no forced exit while a probe socket may still be closing).
  process.exitCode = exitCode
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main()
