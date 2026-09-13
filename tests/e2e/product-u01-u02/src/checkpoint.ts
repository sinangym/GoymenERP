/**
 * Verifiable STAGE HANDOFF between the browser journey and the Backend's snapshot.
 *
 * The Product owner's assertions want the real `after-create` state (v1) and the real
 * `final` state (v2), and v1 cannot be derived from v2 — so the journey must stop at a
 * quiet point after the create is complete and before the edit begins, and let the
 * same Backend take a real quiescent checkpoint with its own
 * `control.ps1 -Product -Action snapshot`. The browser never opens SQL, never runs the
 * snapshot and never estimates how long it takes: it hands the stage over with a
 * request file and continues only once an acknowledgement that names THAT request has
 * been placed beside it. Waiting for a named artifact is not a timed sleep.
 *
 * Three stages, all inside the ONE bound browser run:
 *   baseline      — before the first mutation leaves the browser;
 *   after-create  — the create is complete (real replay accepted), the edit not begun;
 *   final         — the edit is complete and the card was reopened.
 *
 * Request  <run>/L/checkpoints/<stage>.request.json  — written by the spec after the
 *          browser is quiescent (C009 `LiveRecorder.quiesce`); carries the stage, the
 *          bound identity, the record's identity as read from REAL responses, and the
 *          real HTTP exchange metadata so far.
 * Ack      <run>/L/checkpoints/<stage>.ack.json      — written by the Backend after its
 *          snapshot; must name this stage, this resource run and the SHA-256 of the
 *          exact request bytes, and carry the snapshot's SHA-256. If it names a
 *          snapshot file that is readable here, its bytes must hash to that value
 *          (read-only); nothing else is opened.
 *
 * Only erasable TypeScript syntax is used so the regression file can run it with Node.
 */
import { createHash } from 'node:crypto'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'

export type CheckpointStage = 'baseline' | 'after-create' | 'final'
export const CHECKPOINT_STAGES: readonly CheckpointStage[] = ['baseline', 'after-create', 'final']
/** How long the browser is prepared to hold a quiet stage for the Backend's snapshot. Overridable per run, never a guess of the snapshot's duration. */
export const DEFAULT_ACK_TIMEOUT_MS = 15 * 60_000
const SHA256_RE = /^[0-9a-f]{64}$/

export class CheckpointError extends Error {
  readonly code: string
  constructor(code: string, message: string) {
    super('U-01/U-02 checkpoint refused [' + code + ']: ' + message)
    this.code = code
  }
}
const refuse = (code: string, message: string): never => { throw new CheckpointError(code, message) }

export interface CheckpointRequest {
  stage: CheckpointStage
  requestedAtUtc: string
  browserRunId: string
  resourceRunId: string
  resourceInstanceId: string
  manifestSha256: string
  /** What the snapshot is about: identity read from REAL responses, never invented. */
  subject: { productId: string | null; code: string | null; version: string | null }
  /** The browser's quiescence statement at the moment of the request (C009 barrier). */
  quiescence: unknown
  /** Real HTTP exchange metadata so far (method, path, status, key, origin — no bodies). */
  exchanges: readonly unknown[]
  note: string
}

export interface CheckpointAck {
  stage: CheckpointStage
  resourceRunId: string
  /** SHA-256 of the exact request file bytes this ack answers. */
  requestSha256: string
  /** SHA-256 of the snapshot the Backend took. */
  snapshotSha256: string
  takenAtUtc: string
  /** Optional: where the Backend put the snapshot; hash-checked if readable, never parsed. */
  snapshotPath?: string
  tool?: string
}

export interface CheckpointReceipt {
  stage: CheckpointStage
  requestPath: string
  requestSha256: string
  ackPath: string
  ackSha256: string
  ack: CheckpointAck
  /** 'verified' when the named snapshot file was readable and hashed to `snapshotSha256`; 'not-readable' otherwise. */
  snapshotBytes: 'verified' | 'not-readable'
  waitedMs: number
}

const sha256 = (bytes: Buffer) => createHash('sha256').update(bytes).digest('hex')

export function checkpointDir(runDir: string): string {
  return path.join(runDir, 'L', 'checkpoints')
}

/** Writes the request file for a stage and returns its path and hash. */
export function writeCheckpointRequest(runDir: string, request: CheckpointRequest): { path: string; sha256: string } {
  const dir = checkpointDir(runDir)
  mkdirSync(dir, { recursive: true })
  const file = path.join(dir, request.stage + '.request.json')
  if (existsSync(file)) refuse('DUPLICATE', 'a request for stage ' + request.stage + ' already exists in this run: ' + file)
  const bytes = Buffer.from(JSON.stringify(request, null, 2) + '\n', 'utf8')
  writeFileSync(file, bytes)
  return { path: file, sha256: sha256(bytes) }
}

/** Validates an ack against the stage, the bound run and the exact request bytes. Pure. */
export function validateCheckpointAck(
  raw: unknown,
  expected: { stage: CheckpointStage; resourceRunId: string; requestSha256: string },
  readFile: (file: string) => Buffer = (file) => readFileSync(file),
): { ack: CheckpointAck; snapshotBytes: 'verified' | 'not-readable' } {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return refuse('SHAPE', 'ack is not an object')
  const a = raw as Record<string, unknown>
  if (a.stage !== expected.stage) refuse('STAGE', 'ack stage ' + JSON.stringify(a.stage) + ' is not ' + expected.stage)
  if (a.resourceRunId !== expected.resourceRunId) refuse('RUN', 'ack resourceRunId is not this bound run')
  if (a.requestSha256 !== expected.requestSha256) refuse('REQUEST', 'ack does not name the exact request bytes of this stage')
  if (typeof a.snapshotSha256 !== 'string' || !SHA256_RE.test(a.snapshotSha256)) refuse('SNAPSHOT', 'ack carries no snapshot SHA-256')
  if (typeof a.takenAtUtc !== 'string' || !Number.isFinite(Date.parse(a.takenAtUtc))) refuse('TIME', 'ack takenAtUtc is not a timestamp')
  let snapshotBytes: 'verified' | 'not-readable' = 'not-readable'
  if (typeof a.snapshotPath === 'string' && a.snapshotPath.length > 0) {
    let bytes: Buffer | null = null
    try { bytes = readFile(a.snapshotPath) } catch { bytes = null }
    if (bytes !== null) {
      if (sha256(bytes) !== a.snapshotSha256) refuse('SNAPSHOT', 'the named snapshot file does not hash to snapshotSha256')
      snapshotBytes = 'verified'
    }
  }
  return { ack: a as unknown as CheckpointAck, snapshotBytes }
}

/** A wait ceiling or a poll interval must be a finite, positive number of milliseconds — anything else is not a bound. */
export function boundedMs(value: unknown, name: string): number {
  const ms = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(ms) || ms <= 0) refuse('CONFIG', name + ' must be a finite positive number of milliseconds; got ' + JSON.stringify(value))
  return ms
}

/**
 * Waits for the ack of a stage — for the ARTIFACT, polled at a fixed short interval,
 * bounded by a run-configurable ceiling that is checked on EVERY turn of the loop,
 * whatever branch the turn took (absent file, unreadable file, transient read error).
 * A missing or unreadable ack at the ceiling fails the stage (`TIMEOUT`); a wrong ack
 * fails it immediately; a ceiling or interval that is not a finite positive number is
 * refused before the first wait.
 */
export async function awaitCheckpointAck(
  runDir: string,
  expected: { stage: CheckpointStage; resourceRunId: string; requestSha256: string; requestPath: string },
  options: { timeoutMs?: number; pollMs?: number; now?: () => number; sleep?: (ms: number) => Promise<void>; readFile?: (file: string) => Buffer; exists?: (file: string) => boolean } = {},
): Promise<CheckpointReceipt> {
  const timeoutMs = boundedMs(options.timeoutMs ?? process.env.URUN_CHECKPOINT_TIMEOUT_MS ?? DEFAULT_ACK_TIMEOUT_MS, 'checkpoint ack timeout')
  const pollMs = boundedMs(options.pollMs ?? 1000, 'checkpoint poll interval')
  const now = options.now ?? (() => Date.now())
  const sleep = options.sleep ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)))
  const readFile = options.readFile ?? ((file: string) => readFileSync(file))
  const exists = options.exists ?? ((file: string) => existsSync(file))
  const ackPath = path.join(checkpointDir(runDir), expected.stage + '.ack.json')
  const started = now()
  const deadline = started + timeoutMs
  let lastProblem = 'no ack file'
  for (;;) {
    if (exists(ackPath)) {
      let bytes: Buffer | null = null
      try { bytes = readFile(ackPath) } catch (error) { lastProblem = 'ack not readable: ' + String((error as Error)?.message ?? error).slice(0, 80) }
      if (bytes !== null) {
        let raw: unknown
        try { raw = JSON.parse(bytes.toString('utf8')) } catch { return refuse('SHAPE', 'ack is not JSON: ' + ackPath) }
        const { ack, snapshotBytes } = validateCheckpointAck(raw, expected, readFile)
        return { stage: expected.stage, requestPath: expected.requestPath, requestSha256: expected.requestSha256, ackPath, ackSha256: sha256(bytes), ack, snapshotBytes, waitedMs: now() - started }
      }
    } else lastProblem = 'no ack file'
    // The one deadline check, reached from every branch that did not return.
    if (now() >= deadline) refuse('TIMEOUT', 'no usable ' + expected.stage + ' ack at ' + ackPath + ' within ' + timeoutMs + ' ms (' + lastProblem + '); the stage was NOT continued')
    await sleep(pollMs)
  }
}
