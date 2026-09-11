/**
 * Gate regressions for verify-manifest.mjs, against the C006 SOURCE SCHEMA (e35ad38).
 *
 * Fully isolated: temp files, an in-process fetch spy (no socket is ever opened), fake
 * observers for Git / hashes / listener / process. Nothing is written outside the temp
 * directory this file creates and deletes.
 *
 * SYNTHETIC, and never anything else: the envelopes below are hand-built test inputs.
 * They are marked `synthetic: true`, they claim `browserAcceptance: 'NOT_RUN'`,
 * `erpAccepted: false` and `productionActivated: false`, they live only in an OS temp
 * directory — never in a real C006 run directory — and no run here reaches a real
 * server, so nothing produced here is a live pass or acceptance evidence of any kind.
 * They do set `readyForBrowser: true`, because the gate's behaviour on a ready envelope
 * is precisely what most of these cases measure; the case that sets it false asserts
 * that the gate then refuses.
 *
 * What these cases are for: the old flat envelope (`backendHead`, `apiPidAndStartTime`,
 * `allowedUiOrigins`, `fixtureHashComponents`, `runtimeEvidence.assemblies`) does not
 * exist in what C006 publishes. A gate reading those keys compared `undefined` to
 * `undefined` and passed itself, so there is a case here for each nested field the real
 * envelope carries, and one that feeds the whole old flat shape in and requires it to be
 * refused.
 */
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, test } from 'node:test'
import { assertLoopbackTarget, canonicalJson, fixtureCompositeOf, parseCliArgs, runVerification } from './verify-manifest.mjs'
import { GitObservationError, gitDiffSha256, gitHead, runGit, sha256Hex } from './gate-observations.mjs'

const tmp = mkdtempSync(path.join(tmpdir(), 'verify-manifest-test-'))
after(() => rmSync(tmp, { recursive: true, force: true }))

const A = '10000000-0000-0000-0000-000000000001'
const B = '10000000-0000-0000-0000-000000000002'
const FE_HEAD = '9f5b9bfdef249b9c8e89c6668f66b6aa55c34602'
const FE_BASE = '060da63f2644cb271d78d9eab8b7a6985d544ad1'
/** The approved C006 checkout and the locked API dependency: two different commits. */
const ACCEPTANCE_HEAD = 'e35ad3815a8aaf5b1f5b1fb5591bc48427600031'
const BACKEND_DEPENDENCY = '880cae838e5d2f01a988f1e4a89afbe67858f48c'
const BACKEND_TREE = '4b825dc642cb6eb9a060e54bf8d69288fbee4904'
const FE_DIFF = 'a'.repeat(64)
const HASH = (name) => sha256Hex(`content-of-${name}`)
const OPENAPI = HASH('goymen-erp-v1.json')
const MIGRATIONS = HASH('migration-inventory')
const SERVED = HASH('GoymenERP.CariAcceptance006.dll')
const VERIFY_SUMMARY = HASH('backend-verify.json')
const NOW = Date.parse('2026-09-06T02:00:00Z')

const FIXTURE_COMPONENTS = {
  'tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs': HASH('AcceptanceFactory.cs'),
  'tools/GoymenERP.CariAcceptance006/AcceptanceProfile.cs': HASH('AcceptanceProfile.cs'),
  'tools/cari-acceptance-006/run.json': HASH('run.json'),
}
const FIXTURE = fixtureCompositeOf(FIXTURE_COMPONENTS)
const BACKEND_DIR = '/fake/backend'
const SERVED_REL = 'tools/served/GoymenERP.CariAcceptance006.dll'
/** Published outside the checkout on purpose: the gate must use it exactly as given. */
const VERIFY_SUMMARY_PATH = '/fake/verify-out/backend-verify.json'

/** The E top-level keys the C006 publisher writes (source-schema handoff, e35ad38). */
const REQUIRED_KEYS = [
  'contractId', 'taskId', 'readyForBrowser', 'preparedAtUtc', 'runId', 'instanceId', 'apiBaseUrl', 'databaseName',
  'worktree', 'sources', 'resources', 'host', 'fixtureHash', 'baselineSha256', 'smokeSha256', 'backendTests',
  'freshGetObservations', 'companyAId', 'companyBId', 'actor', 'browserPrefix', 'clientContractHeader',
  'clientContractVersion', 'commands', 'browserAcceptance', 'erpAccepted', 'productionActivated', 'topology',
]

const contract = {
  id: 'CARI-AB-TEST',
  codex: {
    taskId: 'C-DESKTOP-006',
    cwd: BACKEND_DIR,
    /** The approved C006 pin. Distinct from `baseHead`; a build from any other commit must not satisfy it. */
    acceptanceHead: ACCEPTANCE_HEAD,
    /** The locked API dependency (`sources.BackendDependency`), not the C006 checkout. */
    baseHead: BACKEND_DEPENDENCY,
  },
  claude: { taskId: 'F-DESKTOP-009', baseHead: FE_HEAD, frontendDiffSha256: FE_DIFF, frontendDiffBase: FE_BASE },
  suggestedPorts: { api: 15080, sql: 15433, ui: 15173 },
  environmentManifestRequired: REQUIRED_KEYS,
}

const LABELS = {
  'com.goymen.acceptance.task': 'C-DESKTOP-006',
  'com.goymen.acceptance.run': 'run-1',
  'com.goymen.acceptance.instance': 'inst-1',
  'com.goymen.acceptance.contract': 'CARI-AB-TEST',
}

function sourcesFixture(over = {}) {
  return {
    AcceptanceHead: ACCEPTANCE_HEAD,
    CompiledHead: ACCEPTANCE_HEAD,
    BackendDependency: BACKEND_DEPENDENCY,
    BackendTree: BACKEND_TREE,
    OpenApiSha256: OPENAPI,
    MigrationSourcesSha256: MIGRATIONS,
    FrontendHead: FE_HEAD,
    FrontendDiffSha256: FE_DIFF,
    Worktree: BACKEND_DIR,
    FrontendWorktree: '/fake/browser',
    FixtureSha256: FIXTURE,
    RunId: 'run-1',
    InstanceId: 'inst-1',
    DatabaseName: 'Db1',
    ApiBaseUrl: 'http://127.0.0.1:15080',
    SqlEndpoint: '127.0.0.1,15433',
    SourcesClean: true,
    DiskArtifacts: { 'GoymenERP.CariAcceptance006.dll': SERVED },
    ...over,
  }
}

function manifestFixture(over = {}, sourcesOver = {}, hostOver = {}) {
  const sources = sourcesFixture(sourcesOver)
  const host = {
    RunId: 'run-1',
    InstanceId: 'inst-1',
    ApiBaseUrl: 'http://127.0.0.1:15080',
    pid: 4242,
    startTimeUtc: '2026-09-06T01:20:00.000Z',
    observedAtUtc: '2026-09-06T01:25:00Z',
    server: 'Kestrel',
    capabilityRevision: 7,
    executionRunId: 'exec-1',
    startupDiskArtifacts: [{ path: SERVED_REL, sha256: SERVED }],
    sourceBinding: sources,
    artifactEvidenceMeaning: 'disk hashes captured before Kestrel startup; not a memory-image attestation',
    ...hostOver,
  }
  return {
    synthetic: true,
    contractId: 'CARI-AB-TEST',
    taskId: 'C-DESKTOP-006',
    readyForBrowser: true,
    preparedAtUtc: '2026-09-06T01:30:00Z',
    runId: 'run-1',
    instanceId: 'inst-1',
    apiBaseUrl: 'http://127.0.0.1:15080',
    databaseName: 'Db1',
    worktree: BACKEND_DIR,
    sources,
    resources: {
      recordedAtUtc: '2026-09-06T01:15:00Z',
      runId: 'run-1',
      instanceId: 'inst-1',
      composeProject: 'goymen-c006-run-1',
      databaseName: 'Db1',
      container: {
        id: '1'.repeat(64),
        created: '2026-09-06T01:10:00Z',
        image: `sha256:${'2'.repeat(64)}`,
        labels: LABELS,
        ports: { '1433/tcp': [{ HostIp: '127.0.0.1', HostPort: '15433' }] },
      },
      volume: { name: 'goymen-c006-run-1_sql', labels: LABELS },
      network: { id: '3'.repeat(64), name: 'goymen-c006-run-1_default', labels: LABELS },
    },
    host,
    fixtureHash: FIXTURE,
    baselineSha256: HASH('baseline.json'),
    smokeSha256: HASH('smoke.json'),
    backendTests: { path: VERIFY_SUMMARY_PATH, sha256: VERIFY_SUMMARY, executed: 812, publisherExecuted: 41 },
    frontendReviewReceipt: { pr: 233, head: FE_HEAD },
    reviewReceiptMeaning: 'review receipt only; not an acceptance',
    freshGetObservations: [
      { companyId: A, status: 200, ack: 'optional-trade-roles-v1', observedAtUtc: '2026-09-06T01:28:00Z' },
      { companyId: B, status: 200, ack: 'optional-trade-roles-v1', observedAtUtc: '2026-09-06T01:28:30Z' },
    ],
    companyAId: A,
    companyBId: B,
    actor: 'group-bootstrap-admin',
    browserPrefix: 'F009-test',
    clientContractHeader: 'X-ERP-Counterparty-Contract',
    clientContractVersion: 'optional-trade-roles-v1',
    commands: { control: 'control.ps1', start: 'start.ps1', verify: 'verify.ps1', stop: 'stop.ps1', releaseCheck: 'release-check.ps1', teardown: 'teardown.ps1' },
    browserAcceptance: 'NOT_RUN',
    erpAccepted: false,
    productionActivated: false,
    topology: 'single-node',
    ...over,
  }
}

function fakeObservers(over = {}, dir = tmp) {
  const files = {
    [path.join(dir, 'smoke.json')]: HASH('smoke.json'),
    [path.join(dir, 'baseline.json')]: HASH('baseline.json'),
    [path.join(BACKEND_DIR, 'contracts/openapi/goymen-erp-v1.json')]: OPENAPI,
    [path.join(BACKEND_DIR, SERVED_REL)]: SERVED,
    [VERIFY_SUMMARY_PATH]: VERIFY_SUMMARY,
    [BACKEND_DIR]: 'dir',
  }
  for (const [component, hash] of Object.entries(FIXTURE_COMPONENTS)) files[path.join(BACKEND_DIR, component)] = hash
  const key = (file) => (file in files ? file : String(file).replace(/\\/g, '/'))
  return {
    gitHead: (d) => (d === BACKEND_DIR ? ACCEPTANCE_HEAD : FE_HEAD),
    gitDiffSha256: () => ({ sha256: sha256Hex(''), bytes: 0, trackedModified: 0, trackedModifiedInExcluded: 0, untracked: 0, untrackedPaths: [], contributions: [] }),
    gitCommittedDiffSha256: () => ({ sha256: FE_DIFF, bytes: 304448 }),
    gitTreeOid: () => BACKEND_TREE,
    gitMigrationSourcesSha256: () => ({ sha256: MIGRATIONS, lines: 12 }),
    sha256File: (file) => files[key(file)] ?? sha256Hex(`unknown:${file}`),
    fileExists: (file) => key(file) in files,
    observeListener: () => ({ pid: 4242, localAddress: '127.0.0.1', listeners: 1 }),
    observeProcess: () => ({ pid: 4242, name: 'dotnet.exe', createdUtc: '2026-09-06T01:20:00.400Z', executablePath: 'C:/Program Files/dotnet/dotnet.exe', commandLine: `"C:/Program Files/dotnet/dotnet.exe" ${path.join(BACKEND_DIR, SERVED_REL)} serve` }),
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
    return { status: hit.status, text: async () => hit.text ?? '', json: async () => hit.json ?? {} }
  }
  return { impl, calls }
}

let counter = 0
function setup(manifestOver = {}, { contractOver = contract, sourcesOver = {}, hostOver = {}, manifest } = {}) {
  counter += 1
  const dir = path.join(tmp, `case-${counter}`)
  mkdirSync(dir, { recursive: true })
  const contractFile = path.join(dir, 'contract.json')
  const manifestFile = path.join(dir, 'environment.json')
  writeFileSync(contractFile, JSON.stringify(contractOver))
  writeFileSync(manifestFile, JSON.stringify(manifest ?? manifestFixture(manifestOver, sourcesOver, hostOver)))
  // The real smoke/baseline artifacts sit beside environment.json; the gate hashes them.
  writeFileSync(path.join(dir, 'smoke.json'), '{}')
  writeFileSync(path.join(dir, 'baseline.json'), '{}')
  const evidenceDir = path.join(dir, 'evidence')
  // A stale "passed" verdict that must be replaced by the fresh run.
  mkdirSync(path.join(evidenceDir, 'L'), { recursive: true })
  writeFileSync(path.join(evidenceDir, 'L', 'manifest-verification.json'), JSON.stringify({ outcome: 'passed', allPassed: true, stale: true }))
  return { dir, contractFile, manifestFile, evidenceDir }
}

const healthy = {
  '/health/live': { status: 200, text: 'Healthy' },
  '/api/v1/identity/me': { status: 200, json: { userId: 'u', displayName: 'Admin', allowedCompanyIds: [A, B, 'x'] } },
}

async function run(env, { observers = {}, responses = healthy, now = NOW, extra = {} } = {}) {
  const spy = fetchSpy(responses)
  const result = await runVerification({
    contractFile: env.contractFile,
    manifestFile: env.manifestFile,
    evidenceDir: env.evidenceDir,
    expectedFrontendDiff: FE_DIFF,
    uiOrigin: 'http://127.0.0.1:15173',
    worktreeDir: '/fake/browser',
    fetchImpl: spy.impl,
    observers: fakeObservers(observers, env.dir),
    now: () => now,
    ...extra,
  })
  const written = JSON.parse(readFileSync(result.reportPath, 'utf8'))
  return { ...result, spy, written }
}

const failedNames = (r) => r.written.checks.filter((c) => c.status === 'fail').map((c) => c.name)
const checkNamed = (r, prefix) => r.written.checks.find((c) => c.name.startsWith(prefix))

// ---- the positive path ------------------------------------------------------------------

test('positive path: every phase ok, exactly two loopback GETs with redirect error and a timeout', async () => {
  const env = setup()
  const r = await run(env)
  assert.equal(r.verdict.outcome, 'passed', JSON.stringify(failedNames(r)))
  assert.equal(r.exitCode, 0)
  assert.equal(r.verdict.httpAttempted, true)
  assert.equal(r.spy.calls.length, 2)
  for (const call of r.spy.calls) {
    assert.ok(call.url.startsWith('http://127.0.0.1:15080/'))
    assert.equal(call.init.redirect, 'error')
    assert.equal(call.init.headers.Connection, 'close')
    assert.ok(call.init.signal instanceof AbortSignal)
  }
  // The actor comes from `actor`; there is no `syntheticActorIds` object any more.
  assert.equal(r.spy.calls[1].init.headers['X-Development-User'], 'group-bootstrap-admin')
  assert.equal(r.written.stale, undefined, 'stale verdict replaced')
  assert.equal(r.written.observed.frontendDiffSha256, FE_DIFF)
  assert.equal(r.written.observed.backendTree, BACKEND_TREE)
  assert.equal(r.written.observed.migrationSourcesSha256, MIGRATIONS)
  assert.equal(r.written.observed.fixtureHashRecomputed, FIXTURE)
  assert.equal(r.written.observed.servedDllSha256, SERVED)
  assert.equal(r.written.pins.contractAcceptanceHead, ACCEPTANCE_HEAD)
  assert.equal(r.written.pins.contractBackendDependency, BACKEND_DEPENDENCY)
  assert.ok(r.written.checks.filter((c) => c.kind === 'byte').every((c) => c.status === 'ok'))
  assert.ok(r.written.checks.filter((c) => c.kind === 'runtime').every((c) => c.status === 'ok'))
  // No invented health artifact: health is observed live, in phase D, or not at all.
  assert.equal(r.written.checks.some((c) => c.name.toLowerCase().includes('health artifact')), false)
})

// ---- the old flat envelope must not pass -------------------------------------------------

test('the pre-e35 flat envelope is refused outright, with ZERO HTTP', async () => {
  /**
   * This is the shape the gate used to read. Every key it names is absent from what C006
   * publishes, so a gate still reading them would find `undefined` on both sides of each
   * comparison and pass. Feeding the old shape in must now fail at preflight.
   */
  const legacy = {
    contractId: 'CARI-AB-TEST',
    taskId: 'C-DESKTOP-006',
    readyForBrowser: true,
    preparedAtUtc: '2026-09-06T01:30:00Z',
    runId: 'run-1',
    instanceId: 'inst-1',
    apiBaseUrl: 'http://127.0.0.1:15080',
    databaseName: 'Db1',
    databaseEndpointLoopback: '127.0.0.1,15433',
    backendHead: BACKEND_DEPENDENCY,
    backendDiffSha256: 'b'.repeat(64),
    frontendDiffSha256: FE_DIFF,
    fixtureHash: FIXTURE,
    fixtureHashComponents: FIXTURE_COMPONENTS,
    companyAId: A,
    companyBId: B,
    browserDataPrefix: 'F009-test',
    syntheticActorIds: { browser: 'group-bootstrap-admin' },
    safeBootstrapMethodNoSecrets: { mode: 'DevelopmentBootstrap' },
    healthEvidence: { path: '/fake/evidence/health.json', sha256: HASH('health.json') },
    smokeEvidence: { path: '/fake/evidence/smoke.json', sha256: HASH('smoke.json'), passed: true },
    baselineEvidence: { path: '/fake/evidence/baseline.json', sha256: HASH('baseline.json') },
    allowedUiOrigins: ['http://127.0.0.1:15173'],
    apiPidAndStartTime: { ApiBaseUrl: 'http://127.0.0.1:15080', pid: 4242, startTimeUtc: '2026-09-06T01:20:00.000Z' },
    runtimeEvidence: { observedAtUtc: '2026-09-06T01:25:00Z', passed: true, apiListener: { OwningProcess: 4242 }, assemblies: [{ path: SERVED_REL, sha256: SERVED }] },
  }
  const env = setup({}, { manifest: legacy })
  const r = await run(env)
  assert.equal(r.verdict.outcome, 'preflight_failed')
  assert.equal(r.spy.calls.length, 0)
  const missing = checkNamed(r, 'required keys present')
  assert.equal(missing.status, 'fail')
  for (const key of ['sources', 'resources', 'host', 'actor', 'browserPrefix', 'clientContractVersion', 'smokeSha256']) {
    assert.ok(missing.detail.includes(key), `${key} must be reported missing`)
  }
  assert.ok(failedNames(r).some((n) => n.startsWith('sources.AcceptanceHead')))
})

// ---- the distinct source pins ------------------------------------------------------------

test('the three source commits stay distinct: a conflated, drifted or unapproved pin fails preflight with ZERO HTTP', async () => {
  const cases = [
    // The C006 checkout is not the API dependency: accepting either for the other lets a
    // build from any commit satisfy the pin.
    { over: { AcceptanceHead: BACKEND_DEPENDENCY, CompiledHead: BACKEND_DEPENDENCY }, expect: 'sources.AcceptanceHead' },
    { over: { BackendDependency: ACCEPTANCE_HEAD }, expect: 'sources.BackendDependency' },
    // The assembly was built from a different revision than the approved checkout.
    { over: { CompiledHead: 'd'.repeat(40) }, expect: 'sources.CompiledHead' },
    { over: { AcceptanceHead: 'd'.repeat(40) }, expect: 'sources.AcceptanceHead' },
    { over: { FrontendHead: 'd'.repeat(40) }, expect: 'sources.FrontendHead' },
    { over: { FrontendDiffSha256: 'f'.repeat(64) }, expect: 'sources.FrontendDiffSha256' },
    { over: { BackendTree: 'not-an-oid' }, expect: 'source binding carries the tree' },
    { over: { OpenApiSha256: 'nope' }, expect: 'source binding carries the tree' },
    { over: { MigrationSourcesSha256: undefined }, expect: 'source binding carries the tree' },
    { over: { FixtureSha256: 'f'.repeat(64) }, expect: 'sources.FixtureSha256' },
  ]
  for (const { over, expect } of cases) {
    const env = setup({}, { sourcesOver: over })
    const r = await run(env)
    assert.equal(r.verdict.outcome, 'preflight_failed', JSON.stringify(over))
    assert.ok(failedNames(r).some((n) => n.startsWith(expect)), `${expect} for ${JSON.stringify(over)}`)
    assert.equal(r.spy.calls.length, 0)
  }
})

test('the host receipt must carry the same source record it was started from', async () => {
  const drifted = sourcesFixture({ AcceptanceHead: ACCEPTANCE_HEAD, CompiledHead: ACCEPTANCE_HEAD, OpenApiSha256: HASH('other') })
  const env = setup({}, { hostOver: { sourceBinding: drifted } })
  const r = await run(env)
  assert.equal(r.verdict.outcome, 'preflight_failed')
  assert.ok(failedNames(r).some((n) => n.startsWith('host.sourceBinding')))
  assert.equal(r.spy.calls.length, 0)

  const absent = await run(setup({}, { hostOver: { sourceBinding: undefined } }))
  assert.equal(absent.verdict.outcome, 'preflight_failed')
  assert.equal(checkNamed(absent, 'host.sourceBinding').detail, 'not present')
  // Ordering is not drift: the same record written in another key order still matches.
  assert.equal(canonicalJson({ b: 1, a: [2, 3] }), canonicalJson({ a: [2, 3], b: 1 }))
})

test('run, instance, database and API identity must agree across the envelope, sources, host and resources', async () => {
  const cases = [
    [{}, { RunId: 'run-2' }, {}, 'host and sources agree'],
    [{}, { DatabaseName: 'Other' }, {}, 'host and sources agree'],
    [{}, { ApiBaseUrl: 'http://127.0.0.1:15081' }, {}, 'host and sources agree'],
    [{}, {}, { InstanceId: 'inst-2' }, 'host and sources agree'],
    [{}, {}, { ApiBaseUrl: 'http://127.0.0.1:15081' }, 'host.ApiBaseUrl'],
  ]
  for (const [over, sourcesOver, hostOver, expect] of cases) {
    const env = setup(over, { sourcesOver, hostOver })
    const r = await run(env)
    assert.equal(r.verdict.outcome, 'preflight_failed', expect)
    assert.ok(failedNames(r).some((n) => n.startsWith(expect)), `${expect}: ${JSON.stringify(failedNames(r))}`)
    assert.equal(r.spy.calls.length, 0)
  }
})

// ---- network targets -------------------------------------------------------------------

test('a non-loopback, wrong-port, named-host, path-bearing or credentialled API URL fails preflight with ZERO HTTP', async () => {
  for (const bad of ['http://203.0.113.10:15080', 'http://127.0.0.1:15081', 'http://localhost:15080', 'http://127.0.0.1:15080/api', 'https://127.0.0.1:15080']) {
    const env = setup({ apiBaseUrl: bad }, { sourcesOver: { ApiBaseUrl: bad }, hostOver: { ApiBaseUrl: bad } })
    const r = await run(env)
    assert.equal(r.verdict.outcome, 'preflight_failed', bad)
    assert.equal(r.verdict.httpAttempted, false, bad)
    assert.equal(r.spy.calls.length, 0, bad)
  }
})

test('sources.SqlEndpoint must be exactly 127.0.0.1,<contract sql port>', async () => {
  for (const bad of ['127.0.0.1,15434', 'localhost,15433', '127.0.0.1:15433', '10.0.0.5,15433', undefined]) {
    const env = setup({}, { sourcesOver: { SqlEndpoint: bad } })
    const r = await run(env)
    assert.equal(r.verdict.outcome, 'preflight_failed', String(bad))
    assert.ok(failedNames(r).some((n) => n.startsWith('sources.SqlEndpoint')))
    assert.equal(r.spy.calls.length, 0)
  }
})

test('the UI port is bound to the source profile: 25173-equivalent only, and a moved port is refused', async () => {
  /**
   * The publisher emits no UI field at all — no `allowedUiOrigins`, `uiPort` or
   * `uiBaseUrl` — so the only thing that can be bound is the port pinned in
   * AcceptanceFactory's CORS policy. A UI that slid to the next port because this one
   * was occupied must fail rather than pass quietly.
   */
  for (const bad of ['http://127.0.0.1:15174', 'http://localhost:15173', 'http://203.0.113.10:15173', 'https://127.0.0.1:15173']) {
    const env = setup()
    const r = await run(env, { extra: { uiOrigin: bad } })
    assert.equal(r.verdict.outcome, 'preflight_failed', bad)
    assert.ok(failedNames(r).some((n) => n.startsWith('UI origin')), bad)
    assert.equal(r.spy.calls.length, 0, bad)
  }
  const good = await run(setup(), { extra: { uiOrigin: 'http://127.0.0.1:15173' } })
  assert.equal(good.verdict.outcome, 'passed')
})

// ---- runtime resources ------------------------------------------------------------------

test('runtime resources are bound by id, label and a loopback-only port publication', async () => {
  const base = manifestFixture()
  const mutate = (fn) => {
    const copy = JSON.parse(JSON.stringify(base))
    fn(copy)
    return copy
  }
  const cases = [
    ['container.image is a registry digest, not the observed content id', mutate((m) => { m.resources.container.image = 'mcr.microsoft.com/mssql/server@sha256:' + '2'.repeat(64) }), 'container.image'],
    ['a container name is not a container id', mutate((m) => { m.resources.container.id = 'goymen-c006-run-1-sql-1' }), 'container and network are identified'],
    ['a network name is not a network id', mutate((m) => { m.resources.network.id = 'goymen-c006-run-1_default' }), 'container and network are identified'],
    ['one missing acceptance label breaks the binding', mutate((m) => { delete m.resources.volume.labels['com.goymen.acceptance.instance'] }), 'container, volume and network labels'],
    ['a label pointing at another run breaks the binding', mutate((m) => { m.resources.network.labels['com.goymen.acceptance.run'] = 'run-9' }), 'container, volume and network labels'],
    ['the SQL port published on every interface', mutate((m) => { m.resources.container.ports['1433/tcp'] = [{ HostIp: '0.0.0.0', HostPort: '15433' }] }), 'the SQL container port'],
    ['a second binding alongside the loopback one', mutate((m) => { m.resources.container.ports['1433/tcp'].push({ HostIp: '0.0.0.0', HostPort: '15433' }) }), 'the SQL container port'],
    ['the SQL port published on a port the contract does not pin', mutate((m) => { m.resources.container.ports['1433/tcp'] = [{ HostIp: '127.0.0.1', HostPort: '15434' }] }), 'the SQL container port'],
    ['resources describing another run', mutate((m) => { m.resources.runId = 'run-9' }), 'resources agree with the envelope'],
  ]
  for (const [label, manifest, expect] of cases) {
    const r = await run(setup({}, { manifest }))
    assert.equal(r.verdict.outcome, 'preflight_failed', label)
    assert.ok(failedNames(r).some((n) => n.startsWith(expect)), `${label}: ${JSON.stringify(failedNames(r))}`)
    assert.equal(r.spy.calls.length, 0, label)
  }
})

// ---- envelope claims and the protocol ----------------------------------------------------

test('a missing key, a foreign contract, an unready environment or any acceptance claim fails preflight with ZERO HTTP', async () => {
  const cases = [
    [{ fixtureHash: undefined }, 'required keys present'],
    [{ sources: undefined }, 'required keys present'],
    [{ contractId: 'OTHER' }, 'contractId matches'],
    [{ taskId: 'C-DESKTOP-999' }, 'taskId is the contract codex task'],
    [{ readyForBrowser: false }, 'readyForBrowser is true'],
    [{ browserAcceptance: 'PASSED' }, 'the envelope claims neither browser acceptance'],
    [{ erpAccepted: true }, 'the envelope claims neither browser acceptance'],
    [{ productionActivated: true }, 'the envelope claims neither browser acceptance'],
    [{ companyBId: A }, 'company ids are distinct UUIDs'],
    [{ actor: '' }, 'browserPrefix and actor present'],
    [{ browserPrefix: undefined }, 'required keys present'],
    [{ clientContractVersion: 'optional-trade-roles-v2' }, 'the envelope declares the counterparty client contract'],
    [{ clientContractHeader: 'X-Erp-Contract' }, 'the envelope declares the counterparty client contract'],
    [{ backendTests: { path: VERIFY_SUMMARY_PATH, sha256: VERIFY_SUMMARY, executed: 0, publisherExecuted: 41 } }, 'backendTests records an executed'],
    [{ backendTests: { path: VERIFY_SUMMARY_PATH, sha256: 'short', executed: 812, publisherExecuted: 41 } }, 'backendTests records an executed'],
  ]
  for (const [over, expect] of cases) {
    const r = await run(setup(over))
    assert.equal(r.verdict.outcome, 'preflight_failed', JSON.stringify(over))
    assert.ok(failedNames(r).some((n) => n.startsWith(expect)), `${expect}: ${JSON.stringify(failedNames(r))}`)
    assert.equal(r.spy.calls.length, 0)
  }
})

test('a credential word anywhere in the envelope fails preflight with ZERO HTTP', async () => {
  // The isolated Testing host authenticates by header; no credential is ever handed over,
  // and the scan covers the whole document rather than one bootstrap sub-object.
  const r = await run(setup({ commands: { control: 'control.ps1 -Pass' + 'word x', start: 's', verify: 'v', stop: 'x', releaseCheck: 'r', teardown: 't' } }))
  assert.equal(r.verdict.outcome, 'preflight_failed')
  assert.ok(failedNames(r).some((n) => n.startsWith('the envelope carries no credential material')))
  assert.equal(r.spy.calls.length, 0)
})

test('freshGetObservations are recorded as a declaration and never as readiness', async () => {
  const r = await run(setup())
  const declared = r.written.checks.filter((c) => c.kind === 'declared')
  assert.ok(declared.length >= 2)
  assert.ok(declared.every((c) => c.detail.includes('declaration only')))
  const observation = declared.find((c) => c.name.startsWith('freshGetObservations'))
  assert.match(observation.detail, /still needs its own fresh acknowledged GET/)

  // An acknowledgment on an error is not a successful observation, and a run that
  // covers only one company is not a run that covers both.
  const acked503 = await run(setup({ freshGetObservations: [
    { companyId: A, status: 503, ack: 'optional-trade-roles-v1', observedAtUtc: '2026-09-06T01:28:00Z' },
    { companyId: B, status: 200, ack: 'optional-trade-roles-v1', observedAtUtc: '2026-09-06T01:28:30Z' },
  ] }))
  assert.equal(acked503.verdict.outcome, 'preflight_failed')
  const oneCompany = await run(setup({ freshGetObservations: [{ companyId: A, status: 200, ack: 'optional-trade-roles-v1', observedAtUtc: '2026-09-06T01:28:00Z' }] }))
  assert.equal(oneCompany.verdict.outcome, 'preflight_failed')
})

// ---- byte identity ----------------------------------------------------------------------

test('the committed frontend diff is recomputed over base..head across the frontend AND the harness', async () => {
  /**
   * `FrontendDiffSha256` is a committed range, not the old harness-excluded worktree
   * diff. The observer call is asserted, because a correct-looking hash computed over
   * the wrong range or the wrong paths is exactly the failure this replaced.
   */
  const calls = []
  const env = setup()
  const r = await run(env, { observers: { gitCommittedDiffSha256: (dir, opts) => { calls.push({ dir, opts }); return { sha256: FE_DIFF, bytes: 304448 } } } })
  assert.equal(r.verdict.outcome, 'passed')
  assert.equal(calls.length, 1)
  assert.equal(calls[0].dir, '/fake/browser')
  assert.equal(calls[0].opts.base, FE_BASE)
  assert.equal(calls[0].opts.head, FE_HEAD)
  assert.deepEqual(calls[0].opts.paths, ['src/frontend/goymen-erp-web', 'tests/e2e'])
})

test('a recomputed committed diff that differs from the envelope fails, with ZERO HTTP', async () => {
  const r = await run(setup(), { observers: { gitCommittedDiffSha256: () => ({ sha256: 'c'.repeat(64), bytes: 12 }) } })
  assert.equal(r.verdict.outcome, 'failed')
  assert.equal(r.exitCode, 1)
  assert.equal(checkNamed(r, 'committed frontend+harness diff').status, 'fail')
  assert.equal(r.spy.calls.length, 0)
})

test('without a diff base the committed diff is not_verified (exit 2) — never assumed', async () => {
  // The base is deliberately absent from this contract; the destructuring names it only
  // to drop it.
  // oxlint-disable-next-line no-unused-vars
  const { frontendDiffBase, ...claude } = contract.claude
  const r = await run(setup({}, { contractOver: { ...contract, claude } }))
  assert.equal(r.verdict.outcome, 'not_verified')
  assert.equal(r.exitCode, 2)
  assert.equal(r.verdict.allPassed, false)
  assert.equal(r.spy.calls.length, 0)
  assert.match(checkNamed(r, 'committed frontend+harness diff').detail, /no frontend diff base supplied/)

  // Supplied on the CLI instead of in the contract, the same run passes.
  const viaCli = await run(setup({}, { contractOver: { ...contract, claude } }), { extra: { frontendDiffBase: FE_BASE } })
  assert.equal(viaCli.verdict.outcome, 'passed')
})

test('worktree cleanliness is a separate observation from the committed diff', async () => {
  // A clean tree does not prove the pinned range, and the pinned range does not prove
  // that nothing was edited on top of it; both are checked, neither substitutes.
  const dirty = { sha256: 'e'.repeat(64), bytes: 4096, trackedModified: 2, trackedModifiedInExcluded: 0, untracked: 1, untrackedPaths: [], contributions: [] }
  const browserDirty = await run(setup(), { observers: { gitDiffSha256: (dir) => (dir === '/fake/browser' ? dirty : fakeObservers().gitDiffSha256(dir)) } })
  assert.equal(browserDirty.verdict.outcome, 'failed')
  assert.equal(checkNamed(browserDirty, 'browser worktree is clean').status, 'fail')
  const backendDirty = await run(setup(), { observers: { gitDiffSha256: (dir) => (dir === BACKEND_DIR ? dirty : fakeObservers().gitDiffSha256(dir)) } })
  assert.equal(backendDirty.verdict.outcome, 'failed')
  assert.equal(checkNamed(backendDirty, 'backend worktree is clean').status, 'fail')
  assert.equal(browserDirty.spy.calls.length + backendDirty.spy.calls.length, 0)
})

test('backend HEAD, the src/backend tree OID, the OpenAPI document and the migration inventory are each recomputed', async () => {
  const cases = [
    [{ gitHead: (d) => (d === BACKEND_DIR ? 'd'.repeat(40) : FE_HEAD) }, 'backend worktree Git HEAD'],
    [{ gitHead: (d) => (d === BACKEND_DIR ? ACCEPTANCE_HEAD : 'd'.repeat(40)) }, 'browser worktree Git HEAD'],
    [{ gitTreeOid: () => '9'.repeat(40) }, 'HEAD:src/backend tree OID'],
    [{ gitMigrationSourcesSha256: () => ({ sha256: 'a'.repeat(64), lines: 12 }) }, 'committed migration inventory'],
    [{ sha256File: (f) => (String(f).replace(/\\/g, '/').endsWith('goymen-erp-v1.json') ? '7'.repeat(64) : fakeObservers().sha256File(f)) }, 'OpenAPI document hash'],
  ]
  for (const [observers, expect] of cases) {
    const r = await run(setup(), { observers })
    assert.equal(r.verdict.outcome, 'failed', expect)
    assert.equal(checkNamed(r, expect).status, 'fail', expect)
    assert.equal(r.spy.calls.length, 0)
  }
})

test('the fixture hash follows the publisher recipe: three ordered path=hash lines, LF-terminated, baseline.json excluded', () => {
  const expected = createHash('sha256').update(Buffer.from(
    `tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs=${HASH('AcceptanceFactory.cs')}\n`
    + `tools/GoymenERP.CariAcceptance006/AcceptanceProfile.cs=${HASH('AcceptanceProfile.cs')}\n`
    + `tools/cari-acceptance-006/run.json=${HASH('run.json')}\n`, 'utf8')).digest('hex')
  assert.equal(fixtureCompositeOf(FIXTURE_COMPONENTS), expected)
  // The old recipe — sorted JSON of a basename map — produces a different value, so a
  // gate still using it could never match a real envelope.
  const sortedJson = sha256Hex(JSON.stringify({ 'AcceptanceFactory.cs': HASH('AcceptanceFactory.cs'), 'baseline.json': HASH('baseline.json'), 'run.json': HASH('run.json') }))
  assert.notEqual(fixtureCompositeOf(FIXTURE_COMPONENTS), sortedJson)
  // Order is part of the recipe: the same three hashes in another order is another value.
  const swapped = createHash('sha256').update(Buffer.from(
    `tools/cari-acceptance-006/run.json=${HASH('run.json')}\n`
    + `tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs=${HASH('AcceptanceFactory.cs')}\n`
    + `tools/GoymenERP.CariAcceptance006/AcceptanceProfile.cs=${HASH('AcceptanceProfile.cs')}\n`, 'utf8')).digest('hex')
  assert.notEqual(expected, swapped)
})

test('a changed fixture component fails the recomputed fixtureHash; a changed baseline.json fails its own artifact check', async () => {
  const profileChanged = await run(setup(), { observers: { sha256File: (f) => (String(f).replace(/\\/g, '/').endsWith('AcceptanceProfile.cs') ? 'f'.repeat(64) : fakeObservers().sha256File(f)) } })
  assert.equal(profileChanged.verdict.outcome, 'failed')
  assert.equal(checkNamed(profileChanged, 'fixtureHash recomputed').status, 'fail')

  const env = setup()
  const baselineChanged = await run(env, { observers: { sha256File: (f) => (String(f).replace(/\\/g, '/').endsWith('baseline.json') ? 'f'.repeat(64) : fakeObservers({}, env.dir).sha256File(f)) } })
  assert.equal(baselineChanged.verdict.outcome, 'failed')
  assert.equal(checkNamed(baselineChanged, 'baseline.json hash').status, 'fail')
  // baseline.json is NOT a fixture component: changing it must not move the fixture hash.
  assert.equal(checkNamed(baselineChanged, 'fixtureHash recomputed').status, 'ok')
  assert.equal(profileChanged.spy.calls.length + baselineChanged.spy.calls.length, 0)
})

test('smoke.json and baseline.json are hashed beside environment.json; an undeclared hash fails rather than being skipped', async () => {
  const r = await run(setup({ smokeSha256: undefined }))
  // `smokeSha256` is a required key, so this is caught at preflight before the bytes.
  assert.equal(r.verdict.outcome, 'preflight_failed')
  const wrong = await run(setup({ smokeSha256: '5'.repeat(64) }))
  assert.equal(wrong.verdict.outcome, 'failed')
  assert.equal(checkNamed(wrong, 'smoke.json hash').status, 'fail')
  assert.equal(wrong.spy.calls.length, 0)
})

test('startup disk artifacts are hashed from disk and must agree with the sources map', async () => {
  const hostDrift = await run(setup({}, { hostOver: { startupDiskArtifacts: [{ path: SERVED_REL, sha256: '8'.repeat(64) }] } }))
  assert.equal(hostDrift.verdict.outcome, 'failed')
  assert.ok(failedNames(hostDrift).some((n) => n.startsWith('startup disk artifact')))

  const sourcesDrift = await run(setup({}, { sourcesOver: { DiskArtifacts: { 'GoymenERP.CariAcceptance006.dll': '8'.repeat(64) } } }))
  assert.equal(sourcesDrift.verdict.outcome, 'failed')
  assert.ok(failedNames(sourcesDrift).some((n) => n.startsWith('startup disk artifact')))

  const none = await run(setup({}, { hostOver: { startupDiskArtifacts: [] } }))
  assert.equal(none.verdict.outcome, 'failed')
  assert.equal(checkNamed(none, 'recorded startup disk artifacts').status, 'fail')

  // The report must not let a disk hash be read as a memory-image attestation.
  const passed = await run(setup())
  assert.match(passed.written.note, /DISK hashes, not memory-image attestation/)
  assert.ok(passed.written.checks.some((c) => c.name.includes('disk bytes, not a memory image')))
})

test('the backendTests summary is hashed at the path published, even outside the checkout, and never re-rooted', async () => {
  const r = await run(setup({ backendTests: { path: VERIFY_SUMMARY_PATH, sha256: '6'.repeat(64), executed: 812, publisherExecuted: 41 } }))
  assert.equal(r.verdict.outcome, 'failed')
  assert.equal(checkNamed(r, 'backendTests summary file hash').status, 'fail')
  assert.equal(r.written.observed.backendTestsSummarySha256, VERIFY_SUMMARY)

  // A path that is not on this machine is unobserved, not quietly resolved to something
  // inside the worktree that happens to exist.
  const elsewhere = await run(setup({ backendTests: { path: '/fake/nowhere/backend-verify.json', sha256: VERIFY_SUMMARY, executed: 812, publisherExecuted: 41 } }))
  assert.equal(elsewhere.verdict.outcome, 'not_verified')
  assert.equal(elsewhere.exitCode, 2)
  assert.equal(checkNamed(elsewhere, 'backendTests summary file hash').status, 'not_verified')
  assert.equal(r.spy.calls.length + elsewhere.spy.calls.length, 0)
})

// ---- runtime identity --------------------------------------------------------------------

test('the OS decides the runtime: a foreign listener, a reused PID, an unrecorded DLL or a dead PID all fail with ZERO HTTP', async () => {
  const other = await run(setup(), { observers: { observeListener: () => ({ pid: 999, localAddress: '127.0.0.1', listeners: 1 }) } })
  assert.equal(other.verdict.outcome, 'failed')
  const reused = await run(setup(), { observers: { observeProcess: () => ({ pid: 4242, name: 'dotnet.exe', createdUtc: '2026-09-06T01:29:00.000Z', commandLine: `x ${path.join(BACKEND_DIR, SERVED_REL)} serve` }) } })
  assert.equal(reused.verdict.outcome, 'failed')
  assert.ok(failedNames(reused).some((n) => n.includes('PID-reuse')))
  const unknownBuild = await run(setup(), { observers: { sha256File: (f) => (String(f).endsWith('.dll') ? '9'.repeat(64) : fakeObservers().sha256File(f)) } })
  assert.equal(unknownBuild.verdict.outcome, 'failed')
  const dead = await run(setup(), { observers: { observeProcess: () => null } })
  assert.equal(dead.verdict.outcome, 'failed')
  const none = await run(setup(), { observers: { observeListener: () => null } })
  assert.equal(none.verdict.outcome, 'failed')
  assert.equal(other.spy.calls.length + reused.spy.calls.length + unknownBuild.spy.calls.length + dead.spy.calls.length + none.spy.calls.length, 0)
})

test('freshness comes from host.observedAtUtc and preparedAtUtc; a stale attestation fails', async () => {
  const stale = await run(setup(), { now: NOW + 30 * 3_600_000 })
  assert.equal(stale.verdict.outcome, 'failed')
  assert.ok(failedNames(stale).some((n) => n.startsWith('attestation not older')))
  const noObservation = await run(setup({}, { hostOver: { observedAtUtc: undefined } }))
  assert.equal(noObservation.verdict.outcome, 'preflight_failed')
  assert.equal(stale.spy.calls.length + noObservation.spy.calls.length, 0)
})

test('a runtime that cannot be observed is not_verified (exit 2), never passed, and never reaches the network', async () => {
  const r = await run(setup(), { observers: { observeProcess: () => { throw new Error('access denied') } } })
  assert.equal(r.verdict.outcome, 'not_verified')
  assert.equal(r.exitCode, 2)
  assert.equal(r.verdict.allPassed, false)
  assert.equal(r.spy.calls.length, 0)
  const noDll = await run(setup(), { observers: { observeProcess: () => ({ pid: 4242, name: 'dotnet.exe', createdUtc: '2026-09-06T01:20:00.400Z', commandLine: null }) } })
  assert.equal(noDll.verdict.outcome, 'not_verified')
})

// ---- live phase and CLI ------------------------------------------------------------------

test('live probe failure after a clean gate → failed with the requests recorded', async () => {
  const r = await run(setup(), { responses: { '/health/live': { status: 503, text: 'Unhealthy' }, '/api/v1/identity/me': healthy['/api/v1/identity/me'] } })
  assert.equal(r.verdict.outcome, 'failed')
  assert.equal(r.verdict.httpRequests, 2)
})

test('an identity that does not cover both companies fails the live phase', async () => {
  const r = await run(setup(), { responses: { '/health/live': healthy['/health/live'], '/api/v1/identity/me': { status: 200, json: { userId: 'u', allowedCompanyIds: [A] } } } })
  assert.equal(r.verdict.outcome, 'failed')
  assert.equal(checkNamed(r, 'browser actor identity').status, 'fail')
})

test('a probe outside the pinned origin is refused by the target guard', () => {
  assert.throws(() => assertLoopbackTarget('http://127.0.0.1:15081/health/live', 'http://127.0.0.1:15080'), /outside the pinned loopback origin/)
  assert.throws(() => assertLoopbackTarget('http://203.0.113.10:15080/health/live', 'http://127.0.0.1:15080'), /outside/)
  assert.equal(assertLoopbackTarget('http://127.0.0.1:15080/health/live', 'http://127.0.0.1:15080').pathname, '/health/live')
})

test('CLI argument parsing keeps the positional contract and the optional flags, including the diff base', () => {
  const args = parseCliArgs(['c.json', 'm.json', 'ev', 'f'.repeat(64), 'http://127.0.0.1:15173', '--worktree', 'W', '--backend-worktree', 'BW', '--frontend-diff-base', FE_BASE, '--max-attestation-age-hours', '12'])
  assert.equal(args.contractFile, 'c.json')
  assert.equal(args.uiOrigin, 'http://127.0.0.1:15173')
  assert.equal(args.worktreeDir, 'W')
  assert.equal(args.backendWorktreeDir, 'BW')
  assert.equal(args.frontendDiffBase, FE_BASE)
  assert.equal(args.maxAttestationAgeHours, 12)
})

// ---- C008-P2-001: Git observation failures at the verifier boundary ---------------------
// The helpers throw GitObservationError for undocumented Git outcomes, spawn/truncation
// failures and unreadable listed files. These measure what the verifier does with that:
// a fresh not_verified report (exit 2), never passed, and no HTTP request at all.

test('C008-P2-001: a Git observation failure in any source observer → not_verified (exit 2), fresh report, ZERO HTTP', async () => {
  const cases = [
    [{ gitCommittedDiffSha256: () => { throw new GitObservationError('git diff could not run: ENOBUFS (output truncated)') } }, 'committed frontend+harness diff', /ENOBUFS/],
    [{ gitDiffSha256: (d) => { if (d === '/fake/browser') throw new GitObservationError('untracked file "yeni dosya ş.txt" listed by Git cannot be read: EACCES'); return fakeObservers().gitDiffSha256(d) } }, 'browser worktree is clean', /cannot be read: EACCES/],
    [{ gitHead: (d) => { if (d === BACKEND_DIR) return ACCEPTANCE_HEAD; throw new GitObservationError('git rev-parse exited 128: fatal: not a git repository') } }, 'browser worktree Git HEAD', /exited 128/],
    [{ gitTreeOid: () => { throw new GitObservationError('git rev-parse exited 128: fatal: not a tree object') } }, 'backend worktree Git HEAD, tree and inventory', /not a tree object/],
    [{ gitMigrationSourcesSha256: () => { throw new GitObservationError('git ls-tree was terminated by SIGKILL') } }, 'backend worktree Git HEAD, tree and inventory', /SIGKILL/],
  ]
  for (const [observers, expect, detail] of cases) {
    const r = await run(setup(), { observers })
    assert.equal(r.verdict.outcome, 'not_verified', expect)
    assert.equal(r.exitCode, 2, expect)
    assert.equal(r.verdict.allPassed, false)
    assert.equal(r.verdict.httpAttempted, false)
    assert.equal(r.spy.calls.length, 0, 'no request may leave the gate when the source could not be observed')
    assert.equal(r.written.stale, undefined, 'stale verdict replaced')
    const check = checkNamed(r, expect)
    assert.equal(check.status, 'not_verified', expect)
    assert.match(check.detail, detail)
  }
})

test('C008-P2-001: an observation failure beside a real mismatch is still not_verified, the verdict is failed, and no HTTP is sent', async () => {
  const r = await run(setup(), { observers: {
    gitTreeOid: () => { throw new GitObservationError('git rev-parse exited 128: fatal: bad object HEAD') },
    gitCommittedDiffSha256: () => ({ sha256: 'c'.repeat(64), bytes: 1 }),
  } })
  // Existing precedence: a concrete failed check decides the verdict (exit 1) before the
  // not_verified rule; the unobserved source stays not_verified in the report, never ok.
  assert.equal(r.verdict.outcome, 'failed')
  assert.equal(r.exitCode, 1)
  assert.equal(r.verdict.httpAttempted, false)
  assert.equal(r.spy.calls.length, 0)
  assert.equal(checkNamed(r, 'committed frontend+harness diff').status, 'fail')
  assert.equal(checkNamed(r, 'backend worktree Git HEAD, tree and inventory').status, 'not_verified')
})

test('C008-P2-001: the real Git helper on a directory that is not a repository reaches the verifier as not_verified with ZERO HTTP', async () => {
  const notARepo = path.join(tmp, 'not-a-repo')
  mkdirSync(notARepo, { recursive: true })
  const observers = {
    gitHead: (d) => (d === BACKEND_DIR ? ACCEPTANCE_HEAD : gitHead(d)),
    gitDiffSha256: (d, opts) => (d === BACKEND_DIR ? fakeObservers().gitDiffSha256(d, opts) : gitDiffSha256(d, opts)),
  }
  const r = await run(setup(), { observers, extra: { worktreeDir: notARepo } })
  assert.equal(r.verdict.outcome, 'not_verified')
  assert.equal(r.exitCode, 2)
  assert.equal(r.verdict.httpAttempted, false)
  assert.equal(r.spy.calls.length, 0)
  const head = checkNamed(r, 'browser worktree Git HEAD')
  assert.equal(head.status, 'not_verified')
  assert.match(head.detail, /exited 128/)
  const clean = checkNamed(r, 'browser worktree is clean')
  assert.equal(clean.status, 'not_verified')
  assert.match(clean.detail, /exited 12[89]: .*not a git repository/i)
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
      gitCommittedDiffSha256: (dir, opts) => {
        runGit(dir, ['diff', '--no-index', '--', '/dev/null', 'new.txt'], {
          okStatuses: [0, 1],
          spawn: (cmd, args) => {
            assert.equal(cmd, 'git')
            assert.deepEqual(args.slice(0, 4), ['-c', 'core.safecrlf=false', '-C', dir])
            return { status: null, signal: null, stdout: Buffer.alloc(0), stderr: Buffer.alloc(0), ...failure }
          },
        })
        return fakeObservers().gitCommittedDiffSha256(dir, opts)
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
    assert.equal(r.written.observed.frontendDiffSha256, undefined)
    const failureCheck = checkNamed(r, 'committed frontend+harness diff')
    assert.equal(failureCheck.status, 'not_verified')
    assert.match(failureCheck.detail, /git diff (reported:|exited|was terminated|could not run:)/, 'the helper error, not an assertion or another exception, must reach the verdict')

    const mismatch = await run(setup(), { observers: { ...observers, gitTreeOid: () => '9'.repeat(40) } })
    assert.equal(mismatch.exitCode, 1)
    assert.equal(mismatch.written.outcome, 'failed')
    assert.equal(mismatch.written.summary.fail, 1)
    assert.equal(mismatch.written.summary.notVerified, 1)
    assert.equal(mismatch.spy.calls.length, 0)
  }
})
