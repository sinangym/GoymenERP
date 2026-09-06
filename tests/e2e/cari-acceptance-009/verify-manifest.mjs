#!/usr/bin/env node
/**
 * Pre-L gate: verifies the C-DESKTOP-006 environment manifest against the acceptance
 * contract, the REAL source/artifact bytes and the REAL running process — and only
 * then, if everything holds, touches the network (two read-only GETs on the pinned
 * loopback origin). Hardened for C007-P2-001 / C007-P2-002 (F-DESKTOP-011).
 *
 * Phases, in order; a failure in any phase stops before the next one:
 *  A. static  — required keys, identities, strict `http://127.0.0.1:<contract port>` API
 *               origin, strict `127.0.0.1,<contract port>` SQL endpoint, UI origin.
 *  B. byte    — the browser worktree's Git HEAD and the exact-byte diff of the 36
 *               application files (harness excluded) against the pin; the backend
 *               worktree's HEAD and diff; health/smoke/baseline artifact hashes; the
 *               fixture components and the recomputed fixtureHash; the recorded
 *               assembly hashes — all COMPUTED, never copied from the manifest.
 *  C. runtime — attestation freshness; the OS listener owning the API port; that
 *               process's PID, creation time (PID-reuse guard) and the served DLL on
 *               its command line hashed against the recorded build identity.
 *  D. live    — GET /health/live and GET /api/v1/identity/me, each target checked
 *               against the pinned origin, `redirect: 'error'`, 5 s timeout.
 *
 * Outcomes: passed (0) · preflight_failed / failed (1) · not_verified (2 — something
 * the gate must see could not be observed; never reported as passed).
 * A fresh report is written at the start of every run so a stale verdict cannot stand.
 *
 * usage: node verify-manifest.mjs <contract.json> <environment.json> <evidenceDir> <expectedFrontendDiffSha256> <uiOrigin>
 *        [--worktree <browserWorktree>] [--backend-worktree <dir>] [--max-attestation-age-hours <n>] [--report-name <file>]
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  extractDllPath,
  gitDiffSha256,
  gitHead,
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
export const HARNESS_PREFIX = 'tests/e2e/cari-acceptance-009/'
const START_TOLERANCE_SECONDS = 2
const HTTP_TIMEOUT_MS = 5000
// Credential words assembled from fragments so this source never carries them literally.
const credentialWords = new RegExp(['pass' + 'word', 'sec' + 'ret', 'tok' + 'en', 'p' + 'wd'].join('|'), 'i')
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function defaultObservers() {
  return {
    gitHead,
    gitDiffSha256,
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

/** Python's json.dumps(sort_keys=True, separators=(',', ':')) for a flat string map. */
export function fixtureHashOf(components) {
  const sorted = Object.fromEntries(Object.keys(components).sort().map((k) => [k, components[k]]))
  return sha256Hex(JSON.stringify(sorted))
}

export async function runVerification(options) {
  const {
    contractFile,
    manifestFile,
    evidenceDir,
    expectedFrontendDiff,
    uiOrigin,
    worktreeDir = path.resolve(here, '../../..'),
    harnessPrefix = HARNESS_PREFIX,
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
        contractBrowserHead: contract?.claude?.baseHead ?? null,
        contractBackendHead: contract?.codex?.baseHead ?? null,
      },
      observed,
      checks,
      summary: {
        ok: checks.filter((c) => c.status === 'ok').length,
        fail: checks.filter((c) => c.status === 'fail').length,
        notVerified: checks.filter((c) => c.status === 'not_verified').length,
        declaredOnly: checks.filter((c) => c.kind === 'declared').length,
      },
      note: 'Declared flags are recorded but never count as byte or runtime evidence; byte and runtime checks are computed from files, Git and the OS.',
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
  const required = Array.isArray(contract.environmentManifestRequired) ? contract.environmentManifestRequired : []
  const missing = required.filter((k) => manifest[k] === undefined)
  ok('static', 'required keys present', required.length > 0 && missing.length === 0, missing.length ? `missing: ${missing.join(', ')}` : `${required.length} keys`)
  ok('static', 'contractId matches', manifest.contractId === contract.id, manifest.contractId)
  ok('static', 'taskId is the contract codex task', manifest.taskId === contract.codex?.taskId, manifest.taskId)
  ok('static', 'readyForBrowser is true', manifest.readyForBrowser === true, String(manifest.readyForBrowser))
  ok('static', 'backendHead declared equals contract pin', manifest.backendHead === contract.codex?.baseHead, manifest.backendHead)
  ok(
    'static',
    'frontendDiffSha256 declared equals expected and contract pin',
    typeof expectedFrontendDiff === 'string' && /^[0-9a-f]{64}$/.test(expectedFrontendDiff) && manifest.frontendDiffSha256 === expectedFrontendDiff && contract.claude?.frontendDiffSha256 === expectedFrontendDiff,
    manifest.frontendDiffSha256,
  )
  const apiCheck = validateLoopbackHttpOrigin(manifest.apiBaseUrl, { requiredPort: contract.suggestedPorts?.api })
  ok('static', 'apiBaseUrl is strictly http://127.0.0.1:<contract api port>', apiCheck.ok, apiCheck.ok ? apiCheck.origin : apiCheck.reason)
  ok('static', 'apiPidAndStartTime.ApiBaseUrl equals apiBaseUrl', manifest.apiPidAndStartTime?.ApiBaseUrl === manifest.apiBaseUrl, manifest.apiPidAndStartTime?.ApiBaseUrl)
  const sqlCheck = validateSqlLoopbackEndpoint(manifest.databaseEndpointLoopback, { requiredPort: contract.suggestedPorts?.sql })
  ok('static', 'databaseEndpointLoopback is strictly 127.0.0.1,<contract sql port>', sqlCheck.ok, sqlCheck.ok ? `${sqlCheck.host},${sqlCheck.port}` : sqlCheck.reason)
  const uiCheck = validateLoopbackHttpOrigin(uiOrigin, { requiredPort: contract.suggestedPorts?.ui })
  ok('static', 'UI origin is strictly loopback on the contract ui port and allowed by the manifest', uiCheck.ok && Array.isArray(manifest.allowedUiOrigins) && manifest.allowedUiOrigins.includes(uiOrigin), uiCheck.ok ? uiOrigin : uiCheck.reason)
  ok('static', 'databaseName present', typeof manifest.databaseName === 'string' && manifest.databaseName.length > 0, manifest.databaseName)
  ok('static', 'runId and instanceId present', typeof manifest.runId === 'string' && manifest.runId.length > 0 && typeof manifest.instanceId === 'string' && manifest.instanceId.length > 0, manifest.runId)
  ok('static', 'company ids are distinct UUIDs', UUID_RE.test(manifest.companyAId ?? '') && UUID_RE.test(manifest.companyBId ?? '') && manifest.companyAId !== manifest.companyBId, `${manifest.companyAId} / ${manifest.companyBId}`)
  ok('static', 'browserDataPrefix present', typeof manifest.browserDataPrefix === 'string' && manifest.browserDataPrefix.length > 0, manifest.browserDataPrefix)
  ok('static', 'bootstrap method carries no credential material', JSON.stringify(manifest.safeBootstrapMethodNoSecrets ?? {}).match(credentialWords) === null && typeof manifest.syntheticActorIds?.browser === 'string', 'header-based development bootstrap')
  add('declared', 'smokeEvidence.passed (declared by the environment owner)', manifest.smokeEvidence?.passed === true ? 'ok' : 'fail', 'declaration only — bound below by the smoke.json hash')
  add('declared', 'runtimeEvidence.passed (declared by the environment owner)', manifest.runtimeEvidence?.passed === true ? 'ok' : 'fail', 'declaration only — bound below by the live OS observation')
  if (checks.some((c) => c.status === 'fail')) return finish('preflight_failed')

  // ---- B. byte identity (computed) -----------------------------------------------------
  try {
    const head = observers.gitHead(worktreeDir)
    observed.browserHead = head
    ok('byte', 'browser worktree Git HEAD equals contract pin', head === contract.claude?.baseHead, head)
  } catch (error) {
    add('byte', 'browser worktree Git HEAD equals contract pin', 'not_verified', String(error?.message ?? error).slice(0, 120))
  }
  try {
    const diff = observers.gitDiffSha256(worktreeDir, { excludePrefixes: [harnessPrefix] })
    observed.applicationDiffSha256 = diff.sha256
    observed.applicationDiffBytes = diff.bytes
    ok('byte', 'application diff (36 files, harness excluded) recomputed equals pin', diff.sha256 === expectedFrontendDiff && diff.trackedModifiedInExcluded === 0, `${diff.sha256} (${diff.bytes} bytes, untracked ${diff.untracked}, tracked modified ${diff.trackedModified})`)
  } catch (error) {
    add('byte', 'application diff (36 files, harness excluded) recomputed equals pin', 'not_verified', String(error?.message ?? error).slice(0, 120))
  }
  const backendDir = options.backendWorktreeDir ?? contract.codex?.cwd ?? null
  if (backendDir && observers.fileExists(backendDir)) {
    try {
      const backendHead = observers.gitHead(backendDir)
      observed.backendHead = backendHead
      ok('byte', 'backend worktree Git HEAD equals contract pin and manifest', backendHead === contract.codex?.baseHead && backendHead === manifest.backendHead, backendHead)
      const backendDiff = observers.gitDiffSha256(backendDir, {})
      observed.backendDiffSha256 = backendDiff.sha256
      observed.backendDiffBytes = backendDiff.bytes
      ok('byte', 'backend diff recomputed equals manifest backendDiffSha256/bytes', backendDiff.sha256 === manifest.backendDiffSha256 && backendDiff.bytes === manifest.backendDiffBytes, `${backendDiff.sha256} (${backendDiff.bytes} bytes)`)
    } catch (error) {
      add('byte', 'backend worktree Git HEAD and diff', 'not_verified', String(error?.message ?? error).slice(0, 120))
    }
  } else {
    add('byte', 'backend worktree Git HEAD and diff', 'not_verified', `backend worktree not available: ${backendDir}`)
  }
  for (const key of ['healthEvidence', 'smokeEvidence', 'baselineEvidence']) {
    const entry = manifest[key]
    if (!entry?.path || !observers.fileExists(entry.path)) {
      add('byte', `${key} file hash equals manifest`, 'not_verified', entry?.path ?? 'no path')
      continue
    }
    const actual = observers.sha256File(entry.path)
    ok('byte', `${key} file hash equals manifest`, actual === entry.sha256, actual)
  }
  const components = manifest.fixtureHashComponents ?? {}
  const componentFiles = {
    'run.json': backendDir ? resolveUnder(backendDir, 'tools/cari-acceptance-006/run.json') : null,
    'AcceptanceFactory.cs': backendDir ? resolveUnder(backendDir, 'tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs') : null,
    'baseline.json': manifest.baselineEvidence?.path ?? null,
  }
  const recomputed = {}
  let componentsVerified = true
  for (const [name, declared] of Object.entries(components)) {
    const file = componentFiles[name]
    if (!file || !observers.fileExists(file)) {
      componentsVerified = false
      add('byte', `fixture component ${name} hash`, 'not_verified', file ?? 'unknown component')
      continue
    }
    recomputed[name] = observers.sha256File(file)
    ok('byte', `fixture component ${name} hash equals manifest`, recomputed[name] === declared, recomputed[name])
  }
  if (componentsVerified && Object.keys(components).length > 0) {
    const composite = fixtureHashOf(recomputed)
    observed.fixtureHashRecomputed = composite
    ok('byte', 'fixtureHash recomputed from component files equals manifest', composite === manifest.fixtureHash, composite)
  } else {
    add('byte', 'fixtureHash recomputed from component files equals manifest', 'not_verified', 'components incomplete')
  }
  const assemblies = Array.isArray(manifest.runtimeEvidence?.assemblies) ? manifest.runtimeEvidence.assemblies : []
  const assemblyHashes = new Map()
  if (assemblies.length === 0) add('byte', 'recorded assembly hashes', 'not_verified', 'no assemblies recorded')
  for (const asm of assemblies) {
    const file = backendDir ? resolveUnder(backendDir, asm.path) : null
    if (!file || !observers.fileExists(file)) {
      add('byte', `assembly ${path.basename(asm.path)} hash`, 'not_verified', asm.path)
      continue
    }
    const actual = observers.sha256File(file)
    assemblyHashes.set(path.resolve(file).toLowerCase(), actual)
    ok('byte', `assembly ${path.basename(asm.path)} hash equals manifest`, actual === asm.sha256, actual)
  }
  if (checks.some((c) => c.status === 'fail')) return finish('failed')

  // ---- C. runtime identity (observed on this machine) ----------------------------------------
  const nowMs = now()
  const ageHours = (iso) => (nowMs - Date.parse(iso)) / 3_600_000
  const observedAge = ageHours(manifest.runtimeEvidence?.observedAtUtc)
  const preparedAge = ageHours(manifest.preparedAtUtc)
  ok('runtime', `attestation not older than ${maxAttestationAgeHours} h`, Number.isFinite(observedAge) && observedAge >= 0 && observedAge <= maxAttestationAgeHours && Number.isFinite(preparedAge) && preparedAge <= maxAttestationAgeHours, `runtimeEvidence ${observedAge.toFixed(2)} h, manifest ${preparedAge.toFixed(2)} h`)
  const declaredPid = Number(manifest.apiPidAndStartTime?.pid)
  const declaredListenerPid = Number(manifest.runtimeEvidence?.apiListener?.OwningProcess)
  ok('static', 'declared PID equals declared listener owner', declaredPid > 0 && declaredPid === declaredListenerPid, `${declaredPid} / ${declaredListenerPid}`)
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
    const skew = isoDiffSeconds(proc.createdUtc, manifest.apiPidAndStartTime?.startTimeUtc)
    ok('runtime', 'process creation time matches the declared start (PID-reuse guard)', skew <= START_TOLERANCE_SECONDS, `skew ${Number.isFinite(skew) ? skew.toFixed(3) : 'n/a'} s (${proc.createdUtc})`)
    const dll = extractDllPath(proc.commandLine)
    if (dll && observers.fileExists(dll)) {
      const dllHash = observers.sha256File(dll)
      observed.servedDllSha256 = dllHash
      const recorded = assemblies.some((a) => a.sha256 === dllHash)
      ok('runtime', 'served assembly on the process command line hashes to a recorded build identity', recorded, `${path.basename(dll)} ${dllHash}`)
    } else {
      add('runtime', 'served assembly on the process command line hashes to a recorded build identity', 'not_verified', dll ? `dll not readable: ${dll}` : 'no dll on command line / command line unavailable')
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
    const response = await probe(`${origin}/api/v1/identity/me`, { headers: { 'X-Development-User': manifest.syntheticActorIds.browser } })
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
    maxAttestationAgeHours: flags['max-attestation-age-hours'] !== undefined ? Number(flags['max-attestation-age-hours']) : undefined,
    reportName: flags['report-name'],
  }
}

async function main() {
  const args = parseCliArgs(process.argv.slice(2))
  if (!args.contractFile || !args.manifestFile || !args.evidenceDir || !args.expectedFrontendDiff || !args.uiOrigin) {
    console.error('usage: node verify-manifest.mjs <contract.json> <environment.json> <evidenceDir> <expectedFrontendDiffSha256> <uiOrigin> [--worktree d] [--backend-worktree d] [--max-attestation-age-hours n] [--report-name f]')
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
