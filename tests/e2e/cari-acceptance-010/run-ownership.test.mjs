// Narrow regressions for run-ownership.ts (C013-P2-001 / C013-P2-002), on temporary fixtures only.
// Run: node --test tests/e2e/cari-acceptance-010/run-ownership.test.mjs
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { spawn, spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, realpathSync, statSync, writeFileSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import {
  CACHE_MARKER, OWNER_RECORD, RUN_ID_PATTERN, RunOwnershipError, establishRunIdentity, readOwnerRecord,
} from './run-ownership.ts'

const here = path.dirname(fileURLToPath(import.meta.url))
const ownershipUrl = pathToFileURL(path.join(here, 'run-ownership.ts')).href

/**
 * A fresh fixture tree: <root>/evidence/desktop-FIXTURE (evidence root), <root>/tmp, <root>/repo,
 * historical folder.
 *
 * C020-P2-001: os.tmpdir() may hand back a temp root in a spelling that is not the one the file
 * system reports for that directory — on a Windows hosted runner it is the 8.3 alias
 * (its user component is RUNNER~1) of the long name (whose user component is runneradmin).
 * The identity helper stores the canonical spelling, so a fixture that keeps the alias compares
 * two spellings of one directory and fails on a runner while passing locally. The root is
 * therefore canonicalized here by the file system itself, with realpathSync.native on the
 * directory that was just created, and every expected run and cache path is derived from that
 * canonical root. This is an independent resolution: the module under test is never used to
 * produce an expectation, no alias text is rewritten, no pattern is matched, and the comparisons
 * below stay strict equality between two spellings of the SAME directory: two different
 * directories are still never treated as equal.
 */
function fixture() {
  const created = mkdtempSync(path.join(os.tmpdir(), 'c010-ownership-'))
  const root = realpathSync.native(created)
  const evidenceRoot = path.join(root, 'evidence', 'desktop-FIXTURE')
  const historical = path.join(root, 'evidence', 'C-DESKTOP-010')
  const repo = path.join(root, 'repo')
  const tmp = path.join(root, 'tmp')
  for (const dir of [evidenceRoot, historical, repo, tmp]) mkdirSync(dir, { recursive: true })
  // The derived expectations must already be canonical: resolving them again changes nothing.
  for (const dir of [evidenceRoot, historical, repo, tmp]) assert.equal(realpathSync.native(dir), dir)
  writeFileSync(path.join(historical, 'report.json'), '{"historical":true}\n')
  writeFileSync(path.join(root, 'package.json'), '{"type":"module"}\n')
  const options = { historicalRoots: [historical], forbiddenCacheParents: [repo], tmpdir: tmp }
  const child = path.join(root, 'child.mjs')
  writeFileSync(child, [
    `import { establishRunIdentity, RunOwnershipError } from ${JSON.stringify(ownershipUrl)}`,
    'const options = JSON.parse(process.argv[2])',
    'try {',
    '  const id = establishRunIdentity(process.env, options)',
    '  console.log(JSON.stringify({ ok: true, ...id, env: { runId: process.env.CARI010_RUN_ID, claim: process.env.CARI010_RUN_CLAIM, cold: process.env.CARI010_COLD_START, cache: process.env.CARI010_VITE_CACHE_DIR } }))',
    '} catch (error) {',
    '  console.log(JSON.stringify({ ok: false, code: error instanceof RunOwnershipError ? error.code : "OTHER", message: String(error && error.message).slice(0, 300) }))',
    '  process.exitCode = 1',
    '}',
    '',
  ].join('\n'))
  return { root, evidenceRoot, historical, repo, tmp, options, child }
}

function cleanEnv(extra) {
  const env = {}
  for (const [key, value] of Object.entries(process.env)) if (!key.startsWith('CARI010_')) env[key] = value
  return { ...env, ...extra }
}

function snapshot(dir) {
  const out = new Map()
  const walk = (d) => {
    for (const entry of readdirSync(d, { withFileTypes: true })) {
      const full = path.join(d, entry.name)
      if (entry.isDirectory()) walk(full)
      else out.set(path.relative(dir, full), createHash('sha256').update(readFileSync(full)).digest('hex'))
    }
  }
  if (existsSync(dir)) walk(dir)
  return out
}

function sameSnapshot(a, b) {
  assert.deepEqual([...a.entries()].sort(), [...b.entries()].sort())
}

function runChild(fx, env, options = fx.options) {
  const result = spawnSync(process.execPath, [fx.child, JSON.stringify(options)], { env: cleanEnv(env), encoding: 'utf8', cwd: fx.root })
  assert.equal(result.error, undefined)
  const line = result.stdout.trim().split(/\r?\n/).pop()
  return { ...JSON.parse(line), exitCode: result.status, stderr: result.stderr }
}

function refused(fn, code) {
  let caught = null
  try { fn() } catch (error) { caught = error }
  assert.ok(caught instanceof RunOwnershipError, 'expected RunOwnershipError, got ' + String(caught && caught.message))
  assert.equal(caught.code, code)
  return caught
}

test('starter claims run directory and cache atomically; identity, record, marker and env are consistent', () => {
  const fx = fixture()
  const env = { CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'fixture-run-1' }
  const id = establishRunIdentity(env, fx.options)
  assert.equal(id.role, 'starter')
  assert.equal(id.runId, 'fixture-run-1')
  assert.equal(id.runDir, path.join(fx.evidenceRoot, 'runs', 'fixture-run-1'))
  assert.equal(id.viteCacheDir, path.join(fx.tmp, 'goymen-cari-010-vite-cache', 'fixture-run-1'))
  assert.equal(id.coldStart, true)
  assert.ok(statSync(id.runDir).isDirectory())
  assert.ok(statSync(id.viteCacheDir).isDirectory())
  const record = readOwnerRecord(id.runDir)
  assert.equal(record.status, 'claimed')
  assert.equal(record.claimId, id.claimId)
  assert.equal(record.cacheMetadataPresentAtClaim, false)
  assert.equal(JSON.parse(readFileSync(path.join(id.viteCacheDir, CACHE_MARKER), 'utf8')).runId, 'fixture-run-1')
  assert.equal(env.CARI010_RUN_CLAIM, id.claimId)
  assert.equal(env.CARI010_COLD_START, '1')
  assert.equal(env.CARI010_VITE_CACHE_DIR, id.viteCacheDir)
  assert.match(id.claimId, /^[0-9a-f]{32}$/)
})

test('reusing a run id is refused before any work and the earlier run bytes stay unchanged', () => {
  const fx = fixture()
  const first = establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'reuse-me' }, fx.options)
  // Pretend the first run produced output that a rerun must never touch.
  mkdirSync(path.join(first.runDir, 'diagnostics'), { recursive: true })
  writeFileSync(path.join(first.runDir, 'diagnostics', 'old-test.json'), '{"status":"failed","marker":"old"}\n')
  writeFileSync(path.join(first.runDir, 'tests.json'), '{"passed":16,"failed":1,"marker":"old"}\n')
  writeFileSync(path.join(first.viteCacheDir, 'marker.txt'), 'old cache bytes\n')
  const runBefore = snapshot(first.runDir)
  const cacheBefore = snapshot(first.viteCacheDir)
  // Sequential second start with the same id (fresh env, no claim): refused with RUN_EXISTS.
  const error = refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'reuse-me' }, fx.options), 'RUN_EXISTS')
  assert.match(error.message, /never reused/)
  sameSnapshot(snapshot(first.runDir), runBefore)
  sameSnapshot(snapshot(first.viteCacheDir), cacheBefore)
  // Same refusal from a separate process (the way a second `playwright test` would start).
  const child = runChild(fx, { CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'reuse-me' })
  assert.equal(child.ok, false)
  assert.equal(child.code, 'RUN_EXISTS')
  assert.equal(child.exitCode, 1)
  sameSnapshot(snapshot(first.runDir), runBefore)
  sameSnapshot(snapshot(first.viteCacheDir), cacheBefore)
})

test('an existing output directory without a record is refused and its bytes stay unchanged', () => {
  const fx = fixture()
  const stale = path.join(fx.evidenceRoot, 'runs', 'stale-output')
  mkdirSync(path.join(stale, 'playwright-output'), { recursive: true })
  writeFileSync(path.join(stale, 'playwright-output', 'failure.png'), 'not really a png\n')
  writeFileSync(path.join(stale, 'tests.json'), '{"marker":"stale"}\n')
  const before = snapshot(stale)
  refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'stale-output' }, fx.options), 'RUN_EXISTS')
  sameSnapshot(snapshot(stale), before)
  assert.equal(readOwnerRecord(stale), null, 'no record is written into a directory this run does not own')
  assert.equal(existsSync(path.join(fx.tmp, 'goymen-cari-010-vite-cache', 'stale-output')), false, 'no cache is created for a refused run')
})

test('concurrent starters with the same run id: exactly one claim succeeds', async () => {
  const fx = fixture()
  const env = { CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'race' }
  const runOne = () => new Promise((resolve, reject) => {
    const proc = spawn(process.execPath, [fx.child, JSON.stringify(fx.options)], { env: cleanEnv(env), cwd: fx.root })
    let stdout = ''
    proc.stdout.on('data', (chunk) => { stdout += chunk })
    proc.on('error', reject)
    proc.on('close', (code) => resolve({ ...JSON.parse(stdout.trim().split(/\r?\n/).pop()), exitCode: code }))
  })
  const results = await Promise.all(Array.from({ length: 6 }, runOne))
  const winners = results.filter((r) => r.ok)
  const losers = results.filter((r) => !r.ok)
  assert.equal(winners.length, 1)
  assert.equal(losers.length, 5)
  // A loser is refused either by the static run-directory check / the atomic mkdir
  // (RUN_EXISTS) or, when it checked the run directory just before the winner created it,
  // by the winner's freshly created cache (CACHE_EXISTS). Both refusals happen before any
  // work and neither writes into the winner's directories. Which gate stops which loser is
  // scheduling, not contract: the requirement is one winner, five losers, and a valid refusal
  // with a nonzero exit for every one of them, so no distribution over the two codes is asserted.
  assert.ok(losers.every((r) => (r.code === 'RUN_EXISTS' || r.code === 'CACHE_EXISTS') && r.exitCode === 1), JSON.stringify(losers))
  const record = readOwnerRecord(path.join(fx.evidenceRoot, 'runs', 'race'))
  assert.equal(record.status, 'claimed')
  assert.equal(record.claimId, winners[0].claimId)
  assert.equal(readdirSync(path.join(fx.evidenceRoot, 'runs')).length, 1)
})

test('invalid cache locations are refused before the run directory is claimed', () => {
  const fx = fixture()
  const cases = [
    { cache: path.join(fx.evidenceRoot, 'cache'), code: 'CACHE_LOCATION_FORBIDDEN' },
    { cache: path.join(fx.evidenceRoot, 'runs', 'x', 'cache'), code: 'CACHE_LOCATION_FORBIDDEN' },
    { cache: path.join(fx.root, 'evidence', 'desktop-OTHER', 'cache'), code: 'CACHE_LOCATION_FORBIDDEN' },
    { cache: path.join(fx.historical, 'cache'), code: 'CACHE_LOCATION_FORBIDDEN' },
    { cache: path.join(fx.repo, 'node_modules', '.vite'), code: 'CACHE_LOCATION_FORBIDDEN' },
    { cache: 'relative/cache', code: 'CACHE_NOT_ABSOLUTE' },
  ]
  for (const [index, c] of cases.entries()) {
    const runId = 'cache-case-' + index
    refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: runId, CARI010_VITE_CACHE_DIR: c.cache }, fx.options), c.code)
    assert.equal(existsSync(path.join(fx.evidenceRoot, 'runs', runId)), false, 'no run directory for ' + c.cache)
    assert.equal(existsSync(c.cache), false, 'no cache created for ' + c.cache)
  }
  assert.equal(existsSync(path.join(fx.evidenceRoot, 'runs')), false, 'no runs directory was created by refused starts')
})

test('an existing (foreign) cache is refused before the run directory is claimed and its bytes stay unchanged', () => {
  const fx = fixture()
  const foreign = path.join(fx.tmp, 'foreign-cache')
  mkdirSync(path.join(foreign, 'deps'), { recursive: true })
  writeFileSync(path.join(foreign, 'deps', '_metadata.json'), '{"hash":"foreign"}\n')
  writeFileSync(path.join(foreign, CACHE_MARKER), '{"runId":"someone-else"}\n')
  const before = snapshot(foreign)
  refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'wants-foreign-cache', CARI010_VITE_CACHE_DIR: foreign }, fx.options), 'CACHE_EXISTS')
  sameSnapshot(snapshot(foreign), before)
  assert.equal(existsSync(path.join(fx.evidenceRoot, 'runs', 'wants-foreign-cache')), false)
  // An empty existing directory is also "existing": the run must create its cache itself.
  const empty = path.join(fx.tmp, 'empty-cache')
  mkdirSync(empty)
  refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'wants-empty-cache', CARI010_VITE_CACHE_DIR: empty }, fx.options), 'CACHE_EXISTS')
  assert.equal(existsSync(path.join(fx.evidenceRoot, 'runs', 'wants-empty-cache')), false)
})

test('members join with the recorded identity even after warm-up metadata appears (parent and worker agree)', () => {
  const fx = fixture()
  const env = { CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'cold-run' }
  const starter = establishRunIdentity(env, fx.options)
  assert.equal(starter.coldStart, true)
  // The warm-up (Vite dependency optimization) creates the optimizer metadata after the claim.
  mkdirSync(path.join(starter.viteCacheDir, 'deps'), { recursive: true })
  writeFileSync(path.join(starter.viteCacheDir, 'deps', '_metadata.json'), '{"hash":"warm"}\n')
  // In-process member (what a re-import in the runner would see) and a separate worker process.
  const inProcess = establishRunIdentity({ ...env }, fx.options)
  assert.equal(inProcess.role, 'member')
  assert.equal(inProcess.coldStart, true)
  assert.equal(inProcess.claimId, starter.claimId)
  assert.equal(inProcess.runDir, starter.runDir)
  assert.equal(inProcess.viteCacheDir, starter.viteCacheDir)
  const worker = runChild(fx, env)
  assert.equal(worker.ok, true)
  assert.equal(worker.role, 'member')
  assert.equal(worker.coldStart, true, 'worker keeps the cold-start identity measured at claim time')
  assert.equal(worker.env.cold, '1')
  assert.equal(worker.env.claim, starter.claimId)
  assert.equal(worker.runDir, starter.runDir)
  // A member that only received the claim and the evidence root (minimal env) also joins.
  const minimal = runChild(fx, { CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'cold-run', CARI010_RUN_CLAIM: starter.claimId })
  assert.equal(minimal.ok, true)
  assert.equal(minimal.coldStart, true)
  assert.equal(minimal.env.cold, '1')
  // Members never write records or create directories.
  assert.deepEqual(readdirSync(starter.runDir), [OWNER_RECORD])
})

test('members without a valid claim are refused and never claim on their own', () => {
  const fx = fixture()
  // Claim present, but no run was ever claimed: no record, no directory is created.
  refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'ghost', CARI010_RUN_CLAIM: 'deadbeef' }, fx.options), 'NO_CLAIM')
  assert.equal(existsSync(path.join(fx.evidenceRoot, 'runs', 'ghost')), false)
  const env = { CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: 'claimed' }
  const starter = establishRunIdentity(env, fx.options)
  refused(() => establishRunIdentity({ ...env, CARI010_RUN_CLAIM: 'not-the-claim' }, fx.options), 'CLAIM_MISMATCH')
  refused(() => establishRunIdentity({ ...env, CARI010_COLD_START: '0' }, fx.options), 'IDENTITY_DRIFT')
  refused(() => establishRunIdentity({ ...env, CARI010_VITE_CACHE_DIR: path.join(fx.tmp, 'other-cache') }, fx.options), 'IDENTITY_DRIFT')
  refused(() => establishRunIdentity({ ...env, CARI010_RUN_ID: 'other-run' }, fx.options), 'NO_CLAIM')
  // A rejected record (cache claim failed after the run directory was taken) is not joinable.
  const rejectedDir = path.join(fx.evidenceRoot, 'runs', 'rejected')
  mkdirSync(rejectedDir, { recursive: true })
  writeFileSync(path.join(rejectedDir, OWNER_RECORD), JSON.stringify({ ...readOwnerRecord(starter.runDir), runId: 'rejected', status: 'rejected' }))
  refused(() => establishRunIdentity({ ...env, CARI010_RUN_ID: 'rejected' }, fx.options), 'CLAIM_NOT_ACTIVE')
})

test('invalid run ids and evidence roots are refused; the default run id is a UTC stamp', () => {
  const fx = fixture()
  for (const bad of ['..', '.', 'a/b', 'a\\b', '-leading-dash', 'x'.repeat(65), 'sp ace']) {
    refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot, CARI010_RUN_ID: bad }, fx.options), 'RUN_ID_INVALID')
  }
  assert.equal(existsSync(path.join(fx.evidenceRoot, 'runs')), false)
  refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: 'relative/evidence' }, fx.options), 'EVIDENCE_ROOT_REQUIRED')
  refused(() => establishRunIdentity({}, fx.options), 'EVIDENCE_ROOT_REQUIRED')
  refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: path.join(fx.root, 'evidence', 'does-not-exist') }, fx.options), 'EVIDENCE_ROOT_MISSING')
  refused(() => establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.historical }, fx.options), 'EVIDENCE_ROOT_HISTORICAL')
  const fixedNow = () => new Date('2026-09-06T17:40:45.123Z')
  const id = establishRunIdentity({ CARI010_EVIDENCE_DIR: fx.evidenceRoot }, { ...fx.options, now: fixedNow })
  assert.equal(id.runId, 'run-2026-09-06T17-40-45Z')
  assert.match(id.runId, RUN_ID_PATTERN)
})
