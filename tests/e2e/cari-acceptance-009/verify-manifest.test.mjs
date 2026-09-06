/**
 * Gate regressions for verify-manifest.mjs (C007-P2-001, C007-P2-002).
 *
 * Fully isolated: temp files, an in-process fetch spy (no socket is ever opened),
 * fake observers for Git / hashes / listener / process. Nothing is written outside
 * the temp directory.
 */
import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, test } from 'node:test'
import { assertLoopbackTarget, fixtureHashOf, parseCliArgs, runVerification } from './verify-manifest.mjs'
import { GitObservationError, gitDiffSha256, gitHead, runGit, sha256Hex } from './gate-observations.mjs'

const tmp = mkdtempSync(path.join(tmpdir(), 'verify-manifest-test-'))
after(() => rmSync(tmp, { recursive: true, force: true }))

const A = '10000000-0000-0000-0000-000000000001'
const B = '10000000-0000-0000-0000-000000000002'
const BROWSER_HEAD = '9f5b9bfdef249b9c8e89c6668f66b6aa55c34602'
const BACKEND_HEAD = '05b138f525f82a7679aa873406507aa87d461bcc'
const APP_DIFF = 'a'.repeat(64)
const BACKEND_DIFF = 'b'.repeat(64)
const HASH = (name) => sha256Hex(`content-of-${name}`)
const ASSEMBLY = HASH('served.dll')
const NOW = Date.parse('2026-09-06T02:00:00Z')

const contract = {
  id: 'CARI-AB-TEST',
  codex: { taskId: 'C-DESKTOP-006', cwd: '/fake/backend', baseHead: BACKEND_HEAD },
  claude: { taskId: 'F-DESKTOP-009', baseHead: BROWSER_HEAD, frontendDiffSha256: APP_DIFF },
  suggestedPorts: { api: 15080, sql: 15433, ui: 15173 },
  environmentManifestRequired: ['contractId', 'taskId', 'readyForBrowser', 'preparedAtUtc', 'runId', 'apiBaseUrl', 'databaseName', 'databaseEndpointLoopback', 'backendHead', 'backendDiffSha256', 'frontendDiffSha256', 'fixtureHash', 'companyAId', 'companyBId', 'syntheticActorIds', 'safeBootstrapMethodNoSecrets', 'healthEvidence', 'smokeEvidence', 'baselineEvidence', 'allowedUiOrigins', 'apiPidAndStartTime', 'runtimeEvidence'],
}

const components = { 'run.json': HASH('run.json'), 'AcceptanceFactory.cs': HASH('AcceptanceFactory.cs'), 'baseline.json': HASH('baseline.json') }

function manifestFixture(over = {}) {
  return {
    contractId: 'CARI-AB-TEST',
    taskId: 'C-DESKTOP-006',
    readyForBrowser: true,
    preparedAtUtc: '2026-09-06T01:30:00Z',
    runId: 'run-1',
    instanceId: 'inst-1',
    apiBaseUrl: 'http://127.0.0.1:15080',
    databaseName: 'Db1',
    databaseEndpointLoopback: '127.0.0.1,15433',
    backendHead: BACKEND_HEAD,
    backendDiffSha256: BACKEND_DIFF,
    backendDiffBytes: 63895,
    frontendDiffSha256: APP_DIFF,
    fixtureHash: fixtureHashOf(components),
    fixtureHashComponents: components,
    companyAId: A,
    companyBId: B,
    browserDataPrefix: 'F009-test',
    syntheticActorIds: { browser: 'group-bootstrap-admin' },
    safeBootstrapMethodNoSecrets: { mode: 'DevelopmentBootstrap', headers: { 'X-Development-User': 'group-bootstrap-admin' } },
    healthEvidence: { path: '/fake/evidence/health.json', sha256: HASH('health.json') },
    smokeEvidence: { path: '/fake/evidence/smoke.json', sha256: HASH('smoke.json'), passed: true },
    baselineEvidence: { path: '/fake/evidence/baseline.json', sha256: HASH('baseline.json') },
    allowedUiOrigins: ['http://127.0.0.1:15173'],
    apiPidAndStartTime: { ApiBaseUrl: 'http://127.0.0.1:15080', pid: 4242, startTimeUtc: '2026-09-06T01:20:00.000Z' },
    runtimeEvidence: {
      observedAtUtc: '2026-09-06T01:25:00Z',
      passed: true,
      apiListener: { LocalAddress: '127.0.0.1', LocalPort: 15080, OwningProcess: 4242 },
      assemblies: [{ path: 'tools/served/served.dll', sha256: ASSEMBLY }],
    },
    ...over,
  }
}

function fakeObservers(over = {}) {
  const files = {
    '/fake/evidence/health.json': HASH('health.json'),
    '/fake/evidence/smoke.json': HASH('smoke.json'),
    '/fake/evidence/baseline.json': HASH('baseline.json'),
    [path.join('/fake/backend', 'tools/cari-acceptance-006/run.json')]: HASH('run.json'),
    [path.join('/fake/backend', 'tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs')]: HASH('AcceptanceFactory.cs'),
    [path.join('/fake/backend', 'tools/served/served.dll')]: ASSEMBLY,
    '/fake/backend': 'dir',
  }
  return {
    gitHead: (dir) => (dir === '/fake/backend' ? BACKEND_HEAD : BROWSER_HEAD),
    gitDiffSha256: (dir) => (dir === '/fake/backend' ? { sha256: BACKEND_DIFF, bytes: 63895, trackedModified: 0, trackedModifiedInExcluded: 0, untracked: 14 } : { sha256: APP_DIFF, bytes: 304448, trackedModified: 6, trackedModifiedInExcluded: 0, untracked: 30 }),
    sha256File: (file) => files[file] ?? files[file.replace(/\\/g, '/')] ?? sha256Hex(`unknown:${file}`),
    fileExists: (file) => file in files || file.replace(/\\/g, '/') in files,
    observeListener: () => ({ pid: 4242, localAddress: '127.0.0.1', listeners: 1 }),
    observeProcess: () => ({ pid: 4242, name: 'dotnet.exe', createdUtc: '2026-09-06T01:20:00.400Z', executablePath: 'C:/Program Files/dotnet/dotnet.exe', commandLine: `"C:/Program Files/dotnet/dotnet.exe" ${path.join('/fake/backend', 'tools/served/served.dll')} serve` }),
    ...over,
  }
}

function fetchSpy(responses = {}) {
  const calls = []
  const impl = async (url, init) => {
    calls.push({ url, init })
    const target = new URL(url)
    const hit = responses[target.pathname]
    if (!hit) throw new Error(`unexpected fetch ${url}`)
    return {
      status: hit.status,
      text: async () => hit.text ?? '',
      json: async () => hit.json ?? {},
    }
  }
  return { impl, calls }
}

let counter = 0
function setup(manifestOver = {}, contractOver = contract) {
  counter += 1
  const dir = path.join(tmp, `case-${counter}`)
  mkdirSync(dir, { recursive: true })
  const contractFile = path.join(dir, 'contract.json')
  const manifestFile = path.join(dir, 'environment.json')
  writeFileSync(contractFile, JSON.stringify(contractOver))
  writeFileSync(manifestFile, JSON.stringify(manifestFixture(manifestOver)))
  const evidenceDir = path.join(dir, 'evidence')
  // A stale "passed" verdict that must be replaced by the fresh run.
  mkdirSync(path.join(evidenceDir, 'L'), { recursive: true })
  writeFileSync(path.join(evidenceDir, 'L', 'manifest-verification.json'), JSON.stringify({ outcome: 'passed', allPassed: true, stale: true }))
  return { dir, contractFile, manifestFile, evidenceDir }
}

const healthy = { '/health/live': { status: 200, text: 'Healthy' }, '/api/v1/identity/me': { status: 200, json: { userId: 'u', displayName: 'Admin', allowedCompanyIds: [A, B, 'x'] } } }

async function run(env, { observers = {}, responses = healthy, now = NOW, extra = {} } = {}) {
  const spy = fetchSpy(responses)
  const result = await runVerification({
    contractFile: env.contractFile,
    manifestFile: env.manifestFile,
    evidenceDir: env.evidenceDir,
    expectedFrontendDiff: APP_DIFF,
    uiOrigin: 'http://127.0.0.1:15173',
    worktreeDir: '/fake/browser',
    fetchImpl: spy.impl,
    observers: fakeObservers(observers),
    now: () => now,
    ...extra,
  })
  const written = JSON.parse(readFileSync(result.reportPath, 'utf8'))
  return { ...result, spy, written }
}

test('positive path: every phase ok, exactly two loopback GETs with redirect error and a timeout', async () => {
  const env = setup()
  const r = await run(env)
  assert.equal(r.verdict.outcome, 'passed')
  assert.equal(r.exitCode, 0)
  assert.equal(r.verdict.httpAttempted, true)
  assert.equal(r.spy.calls.length, 2)
  for (const call of r.spy.calls) {
    assert.ok(call.url.startsWith('http://127.0.0.1:15080/'))
    assert.equal(call.init.redirect, 'error')
    assert.equal(call.init.headers.Connection, 'close')
    assert.ok(call.init.signal instanceof AbortSignal)
  }
  assert.equal(r.spy.calls[1].init.headers['X-Development-User'], 'group-bootstrap-admin')
  assert.equal(r.written.stale, undefined, 'stale verdict replaced')
  assert.equal(r.written.observed.applicationDiffSha256, APP_DIFF)
  assert.equal(r.written.observed.servedDllSha256, ASSEMBLY)
  assert.ok(r.written.checks.filter((c) => c.kind === 'byte').every((c) => c.status === 'ok'))
  assert.ok(r.written.checks.filter((c) => c.kind === 'runtime').every((c) => c.status === 'ok'))
})

test('P2-001: non-loopback apiBaseUrl fails preflight with ZERO HTTP requests and a fresh failed report', async () => {
  const env = setup({ apiBaseUrl: 'http://203.0.113.10:15080', apiPidAndStartTime: { ApiBaseUrl: 'http://203.0.113.10:15080', pid: 4242, startTimeUtc: '2026-09-06T01:20:00.000Z' } })
  const r = await run(env)
  assert.equal(r.verdict.outcome, 'preflight_failed')
  assert.equal(r.exitCode, 1)
  assert.equal(r.verdict.httpAttempted, false)
  assert.equal(r.spy.calls.length, 0, 'no request may leave the gate on a failed preflight')
  assert.equal(r.written.outcome, 'preflight_failed')
  assert.equal(r.written.allPassed, false)
})

test('P2-001: wrong API port, localhost host, path or credentials in the URL all fail preflight without HTTP', async () => {
  for (const bad of ['http://127.0.0.1:15081', 'http://localhost:15080', 'http://127.0.0.1:15080/api', 'http://user:pw@127.0.0.1:15080', 'https://127.0.0.1:15080']) {
    const env = setup({ apiBaseUrl: bad, apiPidAndStartTime: { ApiBaseUrl: bad, pid: 4242, startTimeUtc: '2026-09-06T01:20:00.000Z' } })
    const r = await run(env)
    assert.equal(r.verdict.outcome, 'preflight_failed', bad)
    assert.equal(r.spy.calls.length, 0, bad)
  }
})

test('P2-001: SQL endpoint format or port outside the contract fails preflight without HTTP', async () => {
  for (const bad of ['127.0.0.1,15434', 'localhost,15433', '127.0.0.1:15433', '10.0.0.5,15433']) {
    const env = setup({ databaseEndpointLoopback: bad })
    const r = await run(env)
    assert.equal(r.verdict.outcome, 'preflight_failed', bad)
    assert.equal(r.spy.calls.length, 0, bad)
  }
})

test('P2-001: missing required key, wrong contract id, not-ready, or UI origin not allowed fail preflight without HTTP', async () => {
  const cases = [
    { fixtureHash: undefined },
    { contractId: 'OTHER' },
    { readyForBrowser: false },
    { allowedUiOrigins: ['http://localhost:15173'] },
  ]
  for (const over of cases) {
    const env = setup(over)
    const r = await run(env)
    assert.equal(r.verdict.outcome, 'preflight_failed', JSON.stringify(over))
    assert.equal(r.spy.calls.length, 0)
  }
})

test('P2-001: a probe outside the pinned origin is refused by the target guard', () => {
  assert.throws(() => assertLoopbackTarget('http://127.0.0.1:15081/health/live', 'http://127.0.0.1:15080'), /outside the pinned loopback origin/)
  assert.throws(() => assertLoopbackTarget('http://203.0.113.10:15080/health/live', 'http://127.0.0.1:15080'), /outside/)
  assert.equal(assertLoopbackTarget('http://127.0.0.1:15080/health/live', 'http://127.0.0.1:15080').pathname, '/health/live')
})

test('P2-002: declared hashes identical but the recomputed application diff differs → failed, no HTTP', async () => {
  const env = setup()
  const r = await run(env, { observers: { gitDiffSha256: (dir) => (dir === '/fake/backend' ? { sha256: BACKEND_DIFF, bytes: 63895, trackedModified: 0, trackedModifiedInExcluded: 0, untracked: 14 } : { sha256: 'c'.repeat(64), bytes: 1, trackedModified: 6, trackedModifiedInExcluded: 0, untracked: 30 }) } })
  assert.equal(r.verdict.outcome, 'failed')
  assert.equal(r.exitCode, 1)
  assert.equal(r.spy.calls.length, 0)
  const check = r.written.checks.find((c) => c.name.startsWith('application diff'))
  assert.equal(check.status, 'fail')
})

test('P2-002: browser HEAD or backend HEAD drift → failed', async () => {
  const env = setup()
  const r1 = await run(env, { observers: { gitHead: (dir) => (dir === '/fake/backend' ? BACKEND_HEAD : 'd'.repeat(40)) } })
  assert.equal(r1.verdict.outcome, 'failed')
  const r2 = await run(env, { observers: { gitHead: (dir) => (dir === '/fake/backend' ? 'e'.repeat(40) : BROWSER_HEAD) } })
  assert.equal(r2.verdict.outcome, 'failed')
  assert.equal(r1.spy.calls.length + r2.spy.calls.length, 0)
})

test('P2-002: an artifact or fixture component whose bytes changed → failed; fixtureHash is recomputed, not copied', async () => {
  const env = setup()
  const r = await run(env, { observers: { sha256File: (file) => (String(file).replace(/\\/g, '/').endsWith('baseline.json') ? 'f'.repeat(64) : fakeObservers().sha256File(file)) } })
  assert.equal(r.verdict.outcome, 'failed')
  const names = r.written.checks.filter((c) => c.status === 'fail').map((c) => c.name)
  assert.ok(names.some((n) => n.includes('baselineEvidence')))
  assert.ok(names.some((n) => n.includes('fixture component baseline.json')))
  assert.ok(names.some((n) => n.startsWith('fixtureHash recomputed')))
  assert.equal(r.spy.calls.length, 0)
})

test('P2-002: listener owned by another PID, PID reuse (start time skew) or unrecorded served DLL → failed, no HTTP', async () => {
  const env = setup()
  const other = await run(env, { observers: { observeListener: () => ({ pid: 999, localAddress: '127.0.0.1', listeners: 1 }) } })
  assert.equal(other.verdict.outcome, 'failed')
  const reused = await run(env, { observers: { observeProcess: () => ({ pid: 4242, name: 'dotnet.exe', createdUtc: '2026-09-06T01:29:00.000Z', commandLine: `x ${path.join('/fake/backend', 'tools/served/served.dll')} serve` }) } })
  assert.equal(reused.verdict.outcome, 'failed')
  assert.ok(reused.written.checks.some((c) => c.name.includes('PID-reuse') && c.status === 'fail'))
  const unknownBuild = await run(env, { observers: { sha256File: (file) => (String(file).endsWith('served.dll') ? '9'.repeat(64) : fakeObservers().sha256File(file)) } })
  assert.equal(unknownBuild.verdict.outcome, 'failed')
  assert.equal(other.spy.calls.length + reused.spy.calls.length + unknownBuild.spy.calls.length, 0)
})

test('P2-002: no listener on the port → failed; stale attestation → failed', async () => {
  const env = setup()
  const none = await run(env, { observers: { observeListener: () => null } })
  assert.equal(none.verdict.outcome, 'failed')
  const stale = await run(env, { now: NOW + 30 * 3_600_000 })
  assert.equal(stale.verdict.outcome, 'failed')
  assert.ok(stale.written.checks.some((c) => c.name.startsWith('attestation not older') && c.status === 'fail'))
  assert.equal(none.spy.calls.length + stale.spy.calls.length, 0)
})

test('P2-002: runtime cannot be observed → not_verified (exit 2), never passed, no HTTP', async () => {
  const env = setup()
  const r = await run(env, { observers: { observeProcess: () => { throw new Error('access denied') } } })
  assert.equal(r.verdict.outcome, 'not_verified')
  assert.equal(r.exitCode, 2)
  assert.equal(r.verdict.allPassed, false)
  assert.equal(r.spy.calls.length, 0)
  const noDll = await run(env, { observers: { observeProcess: () => ({ pid: 4242, name: 'dotnet.exe', createdUtc: '2026-09-06T01:20:00.000Z', commandLine: null }) } })
  assert.equal(noDll.verdict.outcome, 'not_verified')
})

test('P2-002: declared passed flags alone cannot pass — a false declaration fails, a true one is labelled declared', async () => {
  const env = setup({ smokeEvidence: { path: '/fake/evidence/smoke.json', sha256: HASH('smoke.json'), passed: false } })
  const r = await run(env)
  assert.equal(r.verdict.outcome, 'preflight_failed')
  const env2 = setup()
  const r2 = await run(env2)
  assert.ok(r2.written.checks.filter((c) => c.kind === 'declared').every((c) => c.detail.includes('declaration only')))
  assert.ok(r2.written.summary.declaredOnly >= 2)
})

test('live probe failure after a clean gate → failed with the requests recorded', async () => {
  const env = setup()
  const r = await run(env, { responses: { '/health/live': { status: 503, text: 'Unhealthy' }, '/api/v1/identity/me': healthy['/api/v1/identity/me'] } })
  assert.equal(r.verdict.outcome, 'failed')
  assert.equal(r.verdict.httpRequests, 2)
})

test('CLI argument parsing keeps the positional contract and optional flags', () => {
  const args = parseCliArgs(['c.json', 'm.json', 'ev', 'f'.repeat(64), 'http://127.0.0.1:15173', '--worktree', 'W', '--max-attestation-age-hours', '12'])
  assert.equal(args.contractFile, 'c.json')
  assert.equal(args.uiOrigin, 'http://127.0.0.1:15173')
  assert.equal(args.worktreeDir, 'W')
  assert.equal(args.maxAttestationAgeHours, 12)
})

// ---- C008-P2-001: Git observation failures at the verifier boundary ---------------------
// The helper now throws GitObservationError for undocumented Git outcomes, spawn/truncation
// failures and unreadable listed files. These tests measure what the verifier does with
// that: a fresh not_verified report (exit 2), never passed, and no HTTP request at all.

test('C008-P2-001: a Git observation failure in the diff or HEAD helper → not_verified (exit 2), fresh report, ZERO HTTP', async () => {
  const env = setup()
  const backendOnly = (impl) => (dir, opts) => (dir === '/fake/backend' ? fakeObservers().gitDiffSha256(dir, opts) : impl(dir, opts))
  const diffFails = await run(env, { observers: { gitDiffSha256: backendOnly(() => { throw new GitObservationError('git diff could not run: ENOBUFS (output truncated)') }) } })
  assert.equal(diffFails.verdict.outcome, 'not_verified')
  assert.equal(diffFails.exitCode, 2)
  assert.equal(diffFails.verdict.allPassed, false)
  assert.equal(diffFails.verdict.httpAttempted, false)
  assert.equal(diffFails.spy.calls.length, 0, 'no request may leave the gate when the source could not be observed')
  assert.equal(diffFails.written.stale, undefined, 'stale verdict replaced')
  assert.equal(diffFails.written.outcome, 'not_verified')
  const check = diffFails.written.checks.find((c) => c.name.startsWith('application diff'))
  assert.equal(check.status, 'not_verified')
  assert.match(check.detail, /ENOBUFS/)
  assert.equal(diffFails.written.observed.applicationDiffSha256, undefined, 'no hash is recorded for an unobserved diff')

  const unreadable = await run(env, { observers: { gitDiffSha256: backendOnly(() => { throw new GitObservationError('untracked file "yeni dosya ş.txt" listed by Git cannot be read: EACCES') }) } })
  assert.equal(unreadable.verdict.outcome, 'not_verified')
  assert.equal(unreadable.exitCode, 2)
  assert.equal(unreadable.spy.calls.length, 0)
  assert.match(unreadable.written.checks.find((c) => c.name.startsWith('application diff')).detail, /cannot be read: EACCES/)

  const headFails = await run(env, { observers: { gitHead: (dir) => { if (dir === '/fake/backend') return BACKEND_HEAD; throw new GitObservationError('git rev-parse exited 128: fatal: not a git repository') } } })
  assert.equal(headFails.verdict.outcome, 'not_verified')
  assert.equal(headFails.exitCode, 2)
  assert.equal(headFails.spy.calls.length, 0)
  assert.equal(headFails.written.checks.find((c) => c.name.startsWith('browser worktree Git HEAD')).status, 'not_verified')

  const backendFails = await run(env, { observers: { gitDiffSha256: (dir, opts) => { if (dir === '/fake/backend') throw new GitObservationError('git ls-files was terminated by SIGKILL'); return fakeObservers().gitDiffSha256(dir, opts) } } })
  assert.equal(backendFails.verdict.outcome, 'not_verified')
  assert.equal(backendFails.spy.calls.length, 0)
  assert.equal(backendFails.written.checks.find((c) => c.name.startsWith('backend worktree Git HEAD and diff')).status, 'not_verified')
})

test('C008-P2-001: an observation failure next to a real pin mismatch is still recorded as not_verified, the verdict is failed, and no HTTP is sent', async () => {
  const env = setup()
  const r = await run(env, { observers: { gitDiffSha256: (dir, opts) => { if (dir === '/fake/backend') throw new GitObservationError('git diff exited 128: fatal: bad object HEAD'); return { ...fakeObservers().gitDiffSha256(dir, opts), sha256: 'c'.repeat(64) } } } })
  // Existing verifier precedence (unchanged in F-012): a concrete failed check decides the verdict
  // (exit 1) before the not_verified rule; the unobserved backend is still not_verified in the report,
  // never ok, and neither outcome reaches the network phase.
  assert.equal(r.verdict.outcome, 'failed')
  assert.equal(r.exitCode, 1)
  assert.equal(r.verdict.allPassed, false)
  assert.equal(r.verdict.httpAttempted, false)
  assert.equal(r.spy.calls.length, 0)
  assert.equal(r.written.summary.fail, 1)
  assert.equal(r.written.summary.notVerified, 1)
  assert.equal(r.written.checks.find((c) => c.name.startsWith('application diff')).status, 'fail')
  assert.equal(r.written.checks.find((c) => c.name.startsWith('backend worktree Git HEAD and diff')).status, 'not_verified')
})

test('C008-P2-001: the real Git helper on a directory that is not a repository reaches the verifier as not_verified with ZERO HTTP', async () => {
  const env = setup()
  const notARepo = path.join(tmp, 'not-a-repo')
  mkdirSync(notARepo, { recursive: true })
  const observers = {
    gitHead: (dir) => (dir === '/fake/backend' ? BACKEND_HEAD : gitHead(dir)),
    gitDiffSha256: (dir, opts) => (dir === '/fake/backend' ? fakeObservers().gitDiffSha256(dir, opts) : gitDiffSha256(dir, opts)),
  }
  const r = await run(env, { observers, extra: { worktreeDir: notARepo } })
  assert.equal(r.verdict.outcome, 'not_verified')
  assert.equal(r.exitCode, 2)
  assert.equal(r.verdict.httpAttempted, false)
  assert.equal(r.spy.calls.length, 0)
  const head = r.written.checks.find((c) => c.name.startsWith('browser worktree Git HEAD'))
  assert.equal(head.status, 'not_verified')
  assert.match(head.detail, /exited 128/)
  const diff = r.written.checks.find((c) => c.name.startsWith('application diff'))
  assert.equal(diff.status, 'not_verified')
  assert.match(diff.detail, /exited 12[89]: .*not a git repository/i)
})

test('F013-P2-001: command-local safecrlf does not hide real Git errors at the verifier boundary; ZERO HTTP and fail precedence remain', async () => {
  const patch = Buffer.from('diff --git a/new.txt b/new.txt\n')
  const cases = [
    { status: 1, stdout: patch, stderr: Buffer.from('warning: unrelated diagnostic') },
    { status: 1, stderr: Buffer.from('error: Could not access file') },
    { status: 128, stderr: Buffer.from('fatal: bad revision') },
    { status: 129, stderr: Buffer.from('error: unknown option') },
    { status: null, signal: 'SIGKILL' },
    { status: null, error: Object.assign(new Error('spawn failure'), { code: 'ENOENT' }) },
    { status: null, error: Object.assign(new Error('access denied'), { code: 'EACCES' }) },
    { status: null, signal: 'SIGTERM', stdout: patch, error: Object.assign(new Error('output cut'), { code: 'ENOBUFS' }) },
  ]
  for (const failure of cases) {
    const observers = {
      gitDiffSha256: (dir, opts) => {
        if (dir !== '/fake/backend') return fakeObservers().gitDiffSha256(dir, opts)
        runGit(dir, ['diff', '--no-index', '--', '/dev/null', 'new.txt'], {
          okStatuses: [0, 1],
          spawn: (cmd, args) => {
            assert.equal(cmd, 'git')
            assert.deepEqual(args.slice(0, 4), ['-c', 'core.safecrlf=false', '-C', dir])
            return { status: null, signal: null, stdout: Buffer.alloc(0), stderr: Buffer.alloc(0), ...failure }
          },
        })
        return fakeObservers().gitDiffSha256(dir, opts)
      },
    }
    const r = await run(setup(), { observers })
    assert.equal(r.exitCode, 2)
    assert.equal(r.written.outcome, 'not_verified')
    assert.equal(r.written.allPassed, false)
    assert.equal(r.written.stale, undefined)
    assert.equal(r.written.httpAttempted, false)
    assert.equal(r.written.httpRequests, 0)
    assert.equal(r.spy.calls.length, 0)
    assert.equal(r.written.observed.backendDiffSha256, undefined)
    const failureCheck = r.written.checks.find((c) => c.name === 'backend worktree Git HEAD and diff')
    assert.equal(failureCheck.status, 'not_verified')
    assert.match(failureCheck.detail, /git diff (reported:|exited|was terminated|could not run:)/, 'the helper error, not an assertion or another exception, must reach the verdict')
    const baseDiff = observers.gitDiffSha256
    const mismatch = await run(setup(), { observers: {
      ...observers,
      gitDiffSha256: (dir, opts) => dir === '/fake/backend' ? baseDiff(dir, opts) : { ...fakeObservers().gitDiffSha256(dir, opts), sha256: 'c'.repeat(64) },
    } })
    assert.equal(mismatch.exitCode, 1)
    assert.equal(mismatch.written.outcome, 'failed')
    assert.equal(mismatch.written.summary.fail, 1)
    assert.equal(mismatch.written.summary.notVerified, 1)
    assert.equal(mismatch.spy.calls.length, 0)
  }
})
