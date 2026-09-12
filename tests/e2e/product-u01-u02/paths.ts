import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { establishRunIdentity } from '../cari-acceptance-010/run-ownership'
import type { RunIdentity } from '../cari-acceptance-010/run-ownership'
import { BINDING_ENV, establishLiveBinding, joinLiveBinding, type LiveBinding } from './manifest-validator'

export const here = path.dirname(fileURLToPath(import.meta.url))
export const repo = path.resolve(here, '../../..')
export const frontend = path.join(repo, 'src/frontend/goymen-erp-web')

/**
 * Two lanes, selected by `URUN_LANE`:
 *  - `T` (default): controlled responses answered in the browser; no API is reached.
 *  - `L`: the live Product API of a GATE-VERIFIED environment through the same-origin
 *    Vite proxy; nothing is fulfilled by the test.
 */
export const lane: 'T' | 'L' = (process.env.URUN_LANE ?? 'T').toUpperCase() === 'L' ? 'L' : 'T'

/**
 * UI ports. The T lane serves controlled responses on a loopback port of its own,
 * 15175, so it can run beside a real environment without touching it. The L lane binds
 * the RESERVED Ürün UI port 26173 — the origin the gate verified — and refuses an
 * ambient override that disagrees.
 */
export const RESERVED_UI_PORT = 26173
export const uiPort = resolveUiPort()
function resolveUiPort(): number {
  const requested = process.env.URUN_UI_PORT
  if (lane !== 'L') return Number(requested ?? 15175)
  if (requested !== undefined && Number(requested) !== RESERVED_UI_PORT) {
    throw new Error(`the L lane is pinned to the reserved Ürün UI port ${RESERVED_UI_PORT}; URUN_UI_PORT=${requested} refused`)
  }
  return RESERVED_UI_PORT
}
export const origin = `http://127.0.0.1:${uiPort}`

/**
 * The L lane's binding to ONE validated environment snapshot — see
 * `manifest-validator.ts` (the consumer side of `tools/product-u01-u02/ENVIRONMENT_CONTRACT.md`).
 * Established by the runner from `environment-ready.json`, its two fixed siblings and the
 * Orchestrator-recorded accepted-contract hash, BEFORE the C010 run identity is claimed,
 * so a wrong, stale, edited or missing tuple consumes no run id, directory, cache, web
 * server or browser. Members (the Vite web server, workers) only join and re-hash.
 */
/**
 * The role is the C010 one and nothing else: a process WITHOUT `CARI010_RUN_CLAIM` is the
 * starter and ALWAYS establishes from the accepted files and the Orchestrator hash — an
 * ambient `URUN_LIVE_BINDING` never stands in for that validation. A process WITH the
 * claim is a member: it joins, re-hashes, re-derives and must match what it was handed.
 */
export const liveBinding: LiveBinding | null =
  lane === 'L'
    ? process.env.CARI010_RUN_CLAIM
      ? joinLiveBinding(process.env, repo, origin)
      : establishLiveBinding(process.env, repo, origin)
    : null
if (lane === 'L' && !process.env.CARI010_RUN_CLAIM && process.env[BINDING_ENV] !== undefined) {
  // The starter's environment already carried a binding it did not establish; it was
  // ignored above and is replaced by the one just derived, so nothing downstream reads it.
  process.env[BINDING_ENV] = JSON.stringify(liveBinding)
}

/**
 * Run identity — the C010 ownership module, REUSED rather than re-written: one
 * claimed run directory, one claimed Vite cache outside the repository, nothing
 * cleaned or overwritten across runs. The variable names are therefore the shared
 * ones (`CARI010_EVIDENCE_DIR`, `CARI010_RUN_ID`, `CARI010_VITE_CACHE_DIR`; the
 * claim/cold-start pair is internal). A second copy of that contract with its own
 * names would be exactly the "second harness" this lane must not build.
 */
export const identity: RunIdentity = establishRunIdentity(process.env, {
  forbiddenCacheParents: [repo],
})
export const { evidenceRoot, runId, runDir, viteCacheDir, coldStart, role, claimId, claimedAtUtc } = identity

/**
 * The 009 live writers (`LiveRecorder.finish`, `writeLJson`, `lShot`) are reused as they
 * are and address their evidence root through `CARI009_EVIDENCE_DIR`. In the L lane it
 * is pointed INTO this run's claimed directory, so live evidence lands beside the run
 * record and never outside an owned run.
 */
if (lane === 'L') process.env.CARI009_EVIDENCE_DIR = runDir
