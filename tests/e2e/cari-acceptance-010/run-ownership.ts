/**
 * Run ownership for the 010 harness (C013-P2-001 / C013-P2-002).
 *
 * Exactly ONE process — the Playwright runner, while it loads the config — establishes the
 * identity of a run: it claims the run directory and the Vite dependency cache atomically
 * (mkdir without recursion; EEXIST is a refusal, never a cleanup), measures the cold-start
 * condition once and writes <run>/run-owner.json. Every other process of the same run
 * (workers, the Vite web server) receives CARI010_RUN_CLAIM and only JOINS: it reads the
 * record, verifies the claim and takes the identity from the record. Nothing is measured
 * again in a child, so the identity cannot drift after the warm-up creates optimizer metadata.
 *
 * Refusals happen before any Vite, Playwright or network work: a run id or output directory
 * that already exists, an existing or foreign cache location, a cache inside the evidence
 * tree or the repository, an invalid run id, a missing evidence root. Existing bytes are
 * never removed, moved or overwritten by a refusal.
 *
 * Only erasable TypeScript syntax is used here so Node can run the regression file
 * (run-ownership.test.mjs) against this module directly.
 */
import { randomBytes } from 'node:crypto'
import { existsSync, lstatSync, mkdirSync, readFileSync, realpathSync, writeFileSync } from 'node:fs'
import type { Stats } from 'node:fs'
import os from 'node:os'
import path from 'node:path'

export const OWNER_RECORD = 'run-owner.json'
export const CACHE_MARKER = 'c010-cache-owner.json'
export const RECORD_SCHEMA = 'c010-run-owner/1'
export const RUN_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/

export type Role = 'starter' | 'member'

export interface OwnerRecord {
  schema: string
  status: 'claimed' | 'rejected'
  runId: string
  evidenceRoot: string
  runDir: string
  viteCacheDir: string
  coldStart: boolean
  cacheMetadataPresentAtClaim: boolean
  claimId: string
  claimedAtUtc: string
  starterPid: number
  reason?: string
}

export interface RunIdentity {
  role: Role
  evidenceRoot: string
  runId: string
  runDir: string
  viteCacheDir: string
  coldStart: boolean
  claimId: string
  claimedAtUtc: string
  starterPid: number
  ownerRecordPath: string
}

export interface OwnershipOptions {
  /** Historical evidence folders: neither the evidence root nor a cache may lie inside them. */
  historicalRoots?: string[]
  /** Additional parents under which a cache must never live (the repository worktree). */
  forbiddenCacheParents?: string[]
  tmpdir?: string
  now?: () => Date
  pid?: number
  randomId?: () => string
}

export class RunOwnershipError extends Error {
  readonly code: string
  constructor(code: string, message: string) {
    super('C010 run ownership refused [' + code + ']: ' + message)
    this.name = 'RunOwnershipError'
    this.code = code
  }
}

type Env = Record<string, string | undefined>

/** Canonical absolute path: realpath of the deepest existing ancestor plus the remaining tail. */
export function canonicalPath(target: string): string {
  const resolved = path.resolve(target)
  let existing = resolved
  const tail: string[] = []
  while (!existsSync(existing)) {
    const parent = path.dirname(existing)
    if (parent === existing) break
    tail.unshift(path.basename(existing))
    existing = parent
  }
  let real = existing
  try { real = realpathSync.native(existing) } catch { real = existing }
  return tail.length === 0 ? real : path.join(real, ...tail)
}

/** Comparison key of a canonical path (case-insensitive on Windows). */
export function pathKey(target: string): string {
  const canonical = canonicalPath(target)
  return process.platform === 'win32' ? canonical.toLowerCase() : canonical
}

export function isWithin(child: string, parent: string): boolean {
  const c = pathKey(child)
  const p = pathKey(parent)
  return c === p || c.startsWith(p.endsWith(path.sep) ? p : p + path.sep)
}

/**
 * Starter (no CARI010_RUN_CLAIM in env): validates, claims run directory and cache, measures
 * the cold-start condition once, writes the owner record and exports the identity to `env`.
 * Member (CARI010_RUN_CLAIM present): verifies the recorded claim and returns the recorded
 * identity without measuring or creating anything.
 */
export function establishRunIdentity(env: Env, options: OwnershipOptions = {}): RunIdentity {
  const evidenceRoot = requiredEvidenceRoot(env, options)
  if (env.CARI010_RUN_CLAIM) return joinRun(env, evidenceRoot)
  return startRun(env, evidenceRoot, options)
}

export function readOwnerRecord(runDir: string): OwnerRecord | null {
  const value = readJson(path.join(runDir, OWNER_RECORD))
  return value && typeof value === 'object' ? (value as OwnerRecord) : null
}

function requiredEvidenceRoot(env: Env, options: OwnershipOptions): string {
  const value = env.CARI010_EVIDENCE_DIR
  if (!value || !path.isAbsolute(value)) {
    throw new RunOwnershipError('EVIDENCE_ROOT_REQUIRED', 'CARI010_EVIDENCE_DIR must be the absolute evidence folder of the CURRENT task (for example …/evidence/desktop-F-017); it is never defaulted.')
  }
  const resolved = canonicalPath(value)
  let stat: Stats | null = null
  try { stat = lstatSync(resolved) } catch { stat = null }
  if (!stat || !stat.isDirectory()) {
    throw new RunOwnershipError('EVIDENCE_ROOT_MISSING', 'CARI010_EVIDENCE_DIR must be an existing directory: ' + resolved)
  }
  for (const historical of options.historicalRoots ?? []) {
    if (isWithin(resolved, historical)) {
      throw new RunOwnershipError('EVIDENCE_ROOT_HISTORICAL', 'CARI010_EVIDENCE_DIR points into a historical evidence folder; refusing to write there: ' + resolved)
    }
  }
  return resolved
}

function resolveRunId(env: Env, now: () => Date): string {
  const explicit = env.CARI010_RUN_ID
  if (explicit === undefined || explicit === '') {
    return 'run-' + now().toISOString().replace(/\.\d+Z$/, 'Z').replaceAll(':', '-')
  }
  if (!RUN_ID_PATTERN.test(explicit)) {
    throw new RunOwnershipError('RUN_ID_INVALID', 'CARI010_RUN_ID must match ' + RUN_ID_PATTERN.source + '; it is never rewritten silently.')
  }
  return explicit
}

function resolveCacheDir(env: Env, evidenceRoot: string, runId: string, options: OwnershipOptions): string {
  const explicit = env.CARI010_VITE_CACHE_DIR
  let candidate: string
  if (explicit === undefined || explicit === '') {
    candidate = path.join(options.tmpdir ?? os.tmpdir(), 'goymen-cari-010-vite-cache', runId)
  } else {
    if (!path.isAbsolute(explicit)) {
      throw new RunOwnershipError('CACHE_NOT_ABSOLUTE', 'CARI010_VITE_CACHE_DIR must be an absolute path: ' + explicit)
    }
    candidate = explicit
  }
  const canonical = canonicalPath(candidate)
  const forbidden: { name: string; root: string }[] = [{ name: 'the evidence root', root: evidenceRoot }]
  const evidenceTree = path.dirname(evidenceRoot)
  if (path.basename(evidenceTree).toLowerCase() === 'evidence') forbidden.push({ name: 'the evidence tree', root: evidenceTree })
  for (const historical of options.historicalRoots ?? []) forbidden.push({ name: 'a historical evidence folder', root: historical })
  for (const parent of options.forbiddenCacheParents ?? []) forbidden.push({ name: 'a forbidden parent (repository worktree)', root: parent })
  for (const place of forbidden) {
    if (isWithin(canonical, place.root)) {
      throw new RunOwnershipError('CACHE_LOCATION_FORBIDDEN', 'CARI010_VITE_CACHE_DIR lies inside ' + place.name + ': ' + canonical)
    }
  }
  if (pathExists(canonical)) {
    throw new RunOwnershipError('CACHE_EXISTS', 'CARI010_VITE_CACHE_DIR already exists; an existing or foreign cache is never reused, cleaned or moved: ' + canonical)
  }
  return canonical
}

function startRun(env: Env, evidenceRoot: string, options: OwnershipOptions): RunIdentity {
  const now = options.now ?? (() => new Date())
  const runId = resolveRunId(env, now)
  const runsDir = path.join(evidenceRoot, 'runs')
  const runDir = path.join(runsDir, runId)
  // 0. Static refusals first (nothing is created when they fire): a run id whose directory
  //    already exists, then an unusable cache location.
  if (pathExists(runDir)) {
    throw new RunOwnershipError('RUN_EXISTS', 'run "' + runId + '" already has an output directory; a run id is never reused and old output is never cleaned: ' + runDir)
  }
  const viteCacheDir = resolveCacheDir(env, evidenceRoot, runId, options)
  // 1. Atomic claim of the run directory. EEXIST here means a concurrent starter won the
  //    race (or the directory appeared since the check): refuse without touching anything.
  mkdirSync(runsDir, { recursive: true })
  try {
    mkdirSync(runDir)
  } catch (error) {
    if (isErrno(error, 'EEXIST')) {
      throw new RunOwnershipError('RUN_EXISTS', 'run "' + runId + '" already has an output directory; a run id is never reused and old output is never cleaned: ' + runDir)
    }
    throw error
  }
  const claimedAtUtc = now().toISOString()
  const starterPid = options.pid ?? process.pid
  const claimId = (options.randomId ?? (() => randomBytes(16).toString('hex')))()
  const ownerRecordPath = path.join(runDir, OWNER_RECORD)
  // 2. Atomic claim of the cache directory: created by this run, outside the evidence tree.
  try {
    mkdirSync(path.dirname(viteCacheDir), { recursive: true })
    mkdirSync(viteCacheDir)
  } catch (error) {
    const reason = isErrno(error, 'EEXIST') ? 'CACHE_EXISTS' : 'CACHE_UNAVAILABLE'
    writeJsonOnce(ownerRecordPath, {
      schema: RECORD_SCHEMA, status: 'rejected', runId, evidenceRoot, runDir, viteCacheDir, coldStart: false,
      cacheMetadataPresentAtClaim: false, claimId, claimedAtUtc, starterPid,
      reason: reason + ': ' + String(error instanceof Error ? error.message : error).slice(0, 300),
    } satisfies OwnerRecord)
    throw new RunOwnershipError(reason, 'the cache directory could not be claimed by this run: ' + viteCacheDir)
  }
  writeJsonOnce(path.join(viteCacheDir, CACHE_MARKER), { schema: RECORD_SCHEMA, runId, evidenceRoot, runDir, claimedAtUtc, starterPid })
  // 3. Cold-start condition measured ONCE, here, before any Vite process exists.
  const cacheMetadataPresentAtClaim = existsSync(path.join(viteCacheDir, 'deps', '_metadata.json'))
  const coldStart = !cacheMetadataPresentAtClaim
  const record: OwnerRecord = {
    schema: RECORD_SCHEMA, status: 'claimed', runId, evidenceRoot, runDir, viteCacheDir, coldStart,
    cacheMetadataPresentAtClaim, claimId, claimedAtUtc, starterPid,
  }
  writeJsonOnce(ownerRecordPath, record)
  // 4. Children inherit the verified identity and join; they never claim or measure.
  env.CARI010_EVIDENCE_DIR = evidenceRoot
  env.CARI010_RUN_ID = runId
  env.CARI010_VITE_CACHE_DIR = viteCacheDir
  env.CARI010_COLD_START = coldStart ? '1' : '0'
  env.CARI010_RUN_CLAIM = claimId
  return { role: 'starter', evidenceRoot, runId, runDir, viteCacheDir, coldStart, claimId, claimedAtUtc, starterPid, ownerRecordPath }
}

function joinRun(env: Env, evidenceRoot: string): RunIdentity {
  const runId = env.CARI010_RUN_ID ?? ''
  if (!RUN_ID_PATTERN.test(runId)) {
    throw new RunOwnershipError('RUN_ID_INVALID', 'a member process needs the run id of the claimed run in CARI010_RUN_ID')
  }
  const runDir = path.join(evidenceRoot, 'runs', runId)
  const ownerRecordPath = path.join(runDir, OWNER_RECORD)
  const record = readOwnerRecord(runDir)
  if (!record) {
    throw new RunOwnershipError('NO_CLAIM', 'no ' + OWNER_RECORD + ' for run "' + runId + '"; members never claim a run themselves: ' + runDir)
  }
  if (record.schema !== RECORD_SCHEMA || record.status !== 'claimed') {
    throw new RunOwnershipError('CLAIM_NOT_ACTIVE', 'run "' + runId + '" is not a claimed run (status ' + String(record.status) + ')')
  }
  if (record.claimId !== env.CARI010_RUN_CLAIM) {
    throw new RunOwnershipError('CLAIM_MISMATCH', 'CARI010_RUN_CLAIM does not match the claim recorded for run "' + runId + '"')
  }
  if (record.runId !== runId || pathKey(record.evidenceRoot) !== pathKey(evidenceRoot)) {
    throw new RunOwnershipError('IDENTITY_DRIFT', 'the recorded run identity differs from the environment of this process')
  }
  if (env.CARI010_VITE_CACHE_DIR && pathKey(env.CARI010_VITE_CACHE_DIR) !== pathKey(record.viteCacheDir)) {
    throw new RunOwnershipError('IDENTITY_DRIFT', 'CARI010_VITE_CACHE_DIR differs from the claimed cache directory')
  }
  if (env.CARI010_COLD_START !== undefined && env.CARI010_COLD_START !== (record.coldStart ? '1' : '0')) {
    throw new RunOwnershipError('IDENTITY_DRIFT', 'CARI010_COLD_START differs from the cold-start condition measured at claim time; the identity is immutable')
  }
  const marker = readJson(path.join(record.viteCacheDir, CACHE_MARKER)) as { runId?: unknown } | null
  if (!marker || marker.runId !== runId) {
    throw new RunOwnershipError('CACHE_NOT_OWNED', 'the cache directory does not carry the marker of run "' + runId + '": ' + record.viteCacheDir)
  }
  // The identity comes from the record only — nothing is measured again in a member.
  env.CARI010_COLD_START = record.coldStart ? '1' : '0'
  return {
    role: 'member', evidenceRoot: record.evidenceRoot, runId, runDir: record.runDir, viteCacheDir: record.viteCacheDir,
    coldStart: record.coldStart, claimId: record.claimId, claimedAtUtc: record.claimedAtUtc, starterPid: record.starterPid, ownerRecordPath,
  }
}

function isErrno(error: unknown, code: string): boolean {
  return typeof error === 'object' && error !== null && (error as { code?: unknown }).code === code
}

function pathExists(target: string): boolean {
  try { lstatSync(target); return true } catch { return false }
}

function writeJsonOnce(file: string, value: unknown): void {
  writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' })
}

function readJson(file: string): unknown {
  try { return JSON.parse(readFileSync(file, 'utf8')) as unknown } catch { return null }
}
