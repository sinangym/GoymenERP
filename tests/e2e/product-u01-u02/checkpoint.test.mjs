/**
 * Target regressions for the stage handoff (`src/checkpoint.ts`): the browser continues
 * a stage ONLY on an acknowledgement that names this stage, this bound run and the
 * exact request bytes, and carries a snapshot hash; a missing ack at the ceiling is a
 * refusal, never a continuation. Synthetic files in a temp directory; no browser, no
 * SQL, no snapshot tool — nothing here is acceptance evidence.
 *
 *   node --test tests/e2e/product-u01-u02/checkpoint.test.mjs
 */
import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { after, test } from 'node:test'
import { CheckpointError, awaitCheckpointAck, boundedMs, checkpointDir, validateCheckpointAck, writeCheckpointRequest } from './src/checkpoint.ts'

const tmp = mkdtempSync(path.join(tmpdir(), 'urun-checkpoint-'))
after(() => rmSync(tmp, { recursive: true, force: true }))
const sha = (text) => createHash('sha256').update(text).digest('hex')
let n = 0
const RUN = 'product-u0102-a1b2c3d4e5f6'

function request(runDir, stage = 'after-create') {
  return writeCheckpointRequest(runDir, {
    stage, requestedAtUtc: '2026-09-13T10:00:00Z', browserRunId: 'urun-browser-1', resourceRunId: RUN, resourceInstanceId: 'inst-1',
    manifestSha256: 'a'.repeat(64), subject: { productId: '11111111-1111-1111-1111-111111111111', code: 'SAC-1', version: '1' },
    quiescence: { accepted: true }, exchanges: [], note: 'synthetic',
  })
}
function ack(over = {}) {
  return { stage: 'after-create', resourceRunId: RUN, requestSha256: 'x', snapshotSha256: 'b'.repeat(64), takenAtUtc: '2026-09-13T10:01:00Z', tool: 'control.ps1 -Product -Action snapshot', ...over }
}
const refused = (fn, code) => assert.rejects(fn, (e) => e instanceof CheckpointError && e.code === code, 'expected ' + code)
const refusedSync = (fn, code) => assert.throws(fn, (e) => e instanceof CheckpointError && e.code === code, 'expected ' + code)
const runDir = () => { const d = path.join(tmp, 'run-' + (++n)); mkdirSync(d, { recursive: true }); return d }

test('an ack that names the stage, the bound run and the exact request bytes, with a snapshot hash, continues the stage', async () => {
  const dir = runDir()
  const req = request(dir)
  const snapshotFile = path.join(dir, 'after-create.snapshot.json')
  writeFileSync(snapshotFile, '{"rows":1}\n')
  writeFileSync(path.join(checkpointDir(dir), 'after-create.ack.json'), JSON.stringify(ack({ requestSha256: req.sha256, snapshotSha256: sha('{"rows":1}\n'), snapshotPath: snapshotFile })))
  const receipt = await awaitCheckpointAck(dir, { stage: 'after-create', resourceRunId: RUN, requestSha256: req.sha256, requestPath: req.path }, { timeoutMs: 5_000, pollMs: 1 })
  assert.equal(receipt.snapshotBytes, 'verified')
  assert.equal(receipt.ack.snapshotSha256, sha('{"rows":1}\n'))
  assert.equal(receipt.ackSha256.length, 64)
})

test('the ack is awaited as an artifact: it may arrive later, and the wait is bounded by the ceiling, not a guess', async () => {
  const dir = runDir()
  const req = request(dir)
  let ticks = 0
  const sleep = async () => { ticks += 1; if (ticks === 3) writeFileSync(path.join(checkpointDir(dir), 'after-create.ack.json'), JSON.stringify(ack({ requestSha256: req.sha256 }))) }
  const receipt = await awaitCheckpointAck(dir, { stage: 'after-create', resourceRunId: RUN, requestSha256: req.sha256, requestPath: req.path }, { timeoutMs: 60_000, pollMs: 1, sleep })
  assert.equal(receipt.snapshotBytes, 'not-readable')
  assert.equal(ticks, 3)
})

test('no ack at the ceiling refuses the stage — the journey does not continue on silence', async () => {
  const dir = runDir()
  const req = request(dir)
  let t = 0
  await refused(() => awaitCheckpointAck(dir, { stage: 'after-create', resourceRunId: RUN, requestSha256: req.sha256, requestPath: req.path }, { timeoutMs: 100, pollMs: 1, now: () => (t += 50), sleep: async () => {} }), 'TIMEOUT')
})

test('an ack file that stays UNREADABLE is not a continuation: the deadline holds on that branch too (TIMEOUT)', async () => {
  const dir = runDir()
  const req = request(dir)
  writeFileSync(path.join(checkpointDir(dir), 'after-create.ack.json'), '{}')
  let t = 0
  let reads = 0
  await refused(() => awaitCheckpointAck(dir, { stage: 'after-create', resourceRunId: RUN, requestSha256: req.sha256, requestPath: req.path }, {
    timeoutMs: 100, pollMs: 1, now: () => (t += 30), sleep: async () => {},
    readFile: () => { reads += 1; throw new Error('EBUSY: locked by the writer') },
  }), 'TIMEOUT')
  assert.ok(reads >= 2 && reads <= 5, `bounded number of read attempts, got ${reads}`)
})

test('a TRANSIENT read error is retried and the valid ack that follows continues the stage', async () => {
  const dir = runDir()
  const req = request(dir)
  writeFileSync(path.join(checkpointDir(dir), 'after-create.ack.json'), JSON.stringify(ack({ requestSha256: req.sha256 })))
  let attempts = 0
  const { readFileSync } = await import('node:fs')
  const receipt = await awaitCheckpointAck(dir, { stage: 'after-create', resourceRunId: RUN, requestSha256: req.sha256, requestPath: req.path }, {
    timeoutMs: 10_000, pollMs: 1,
    readFile: (file) => { attempts += 1; if (attempts <= 2) throw new Error('EBUSY'); return readFileSync(file) },
  })
  assert.equal(attempts, 3)
  assert.equal(receipt.ack.requestSha256, req.sha256)
})

for (const [label, value] of [['NaN', Number.NaN], ['Infinity', Number.POSITIVE_INFINITY], ['zero', 0], ['negative', -5], ['a word', 'soon'], ['undefined env value coerced', 'undefined']]) {
  test(`a ceiling of ${label} is refused before any wait (CONFIG) — an unbounded wait is not a bounded one`, async () => {
    const dir = runDir()
    const req = request(dir)
    let slept = 0
    await refused(() => awaitCheckpointAck(dir, { stage: 'after-create', resourceRunId: RUN, requestSha256: req.sha256, requestPath: req.path }, { timeoutMs: value, pollMs: 1, sleep: async () => { slept += 1 } }), 'CONFIG')
    assert.equal(slept, 0)
    assert.throws(() => boundedMs(value, 'x'), (e) => e instanceof CheckpointError && e.code === 'CONFIG')
  })
}

test('a poll interval that is not finite and positive is refused the same way', async () => {
  const dir = runDir()
  const req = request(dir)
  await refused(() => awaitCheckpointAck(dir, { stage: 'after-create', resourceRunId: RUN, requestSha256: req.sha256, requestPath: req.path }, { timeoutMs: 1_000, pollMs: 0 }), 'CONFIG')
  assert.equal(boundedMs('1500', 'x'), 1500)
})

for (const [label, over, code] of [
  ['another stage', { stage: 'final' }, 'STAGE'],
  ['another resource run', { resourceRunId: 'product-u0102-000000000000' }, 'RUN'],
  ['a request hash that is not these bytes', { requestSha256: 'c'.repeat(64) }, 'REQUEST'],
  ['no snapshot hash', { snapshotSha256: undefined }, 'SNAPSHOT'],
  ['a malformed snapshot hash', { snapshotSha256: 'not-a-hash' }, 'SNAPSHOT'],
  ['no timestamp', { takenAtUtc: 'yesterday' }, 'TIME'],
]) {
  test(`an ack with ${label} is refused (${code})`, async () => {
    const dir = runDir()
    const req = request(dir)
    writeFileSync(path.join(checkpointDir(dir), 'after-create.ack.json'), JSON.stringify(ack({ requestSha256: req.sha256, ...over })))
    await refused(() => awaitCheckpointAck(dir, { stage: 'after-create', resourceRunId: RUN, requestSha256: req.sha256, requestPath: req.path }, { timeoutMs: 1_000, pollMs: 1 }), code)
  })
}

test('a named snapshot file that does not hash to snapshotSha256 is refused; an unreadable one is recorded, not trusted', () => {
  const dir = runDir()
  const snapshotFile = path.join(dir, 'snap.json')
  writeFileSync(snapshotFile, 'real bytes')
  refusedSync(() => validateCheckpointAck(ack({ requestSha256: 'r', snapshotPath: snapshotFile }), { stage: 'after-create', resourceRunId: RUN, requestSha256: 'r' }), 'SNAPSHOT')
  const ok = validateCheckpointAck(ack({ requestSha256: 'r', snapshotPath: path.join(dir, 'missing.json') }), { stage: 'after-create', resourceRunId: RUN, requestSha256: 'r' })
  assert.equal(ok.snapshotBytes, 'not-readable')
})

test('a stage request is written once per run; a second request for the same stage is refused', () => {
  const dir = runDir()
  request(dir, 'baseline')
  refusedSync(() => request(dir, 'baseline'), 'DUPLICATE')
})
