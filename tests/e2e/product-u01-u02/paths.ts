import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { establishRunIdentity } from '../cari-acceptance-010/run-ownership'
import type { RunIdentity } from '../cari-acceptance-010/run-ownership'
import { readProductManifest, type ProductEnvironmentManifest } from './src/live'

export const here = path.dirname(fileURLToPath(import.meta.url))
export const repo = path.resolve(here, '../../..')
export const frontend = path.join(repo, 'src/frontend/goymen-erp-web')

/**
 * Two lanes, selected by `URUN_LANE`:
 *  - `T` (default): controlled responses answered in the browser; no API is reached.
 *  - `L`: the live Product API of the Backend's ENV_READY tuple through the Vite proxy;
 *    nothing is fulfilled by the test.
 */
export const lane: 'T' | 'L' = (process.env.URUN_LANE ?? 'T').toUpperCase() === 'L' ? 'L' : 'T'

/**
 * UI ports. The T lane serves controlled responses on a loopback port of its own,
 * 15175, so it can run beside a real environment without touching it. The L lane
 * binds the RESERVED Ürün UI port 26173 — the only origin the isolated Product host is
 * expected to allow — and refuses an ambient override that disagrees rather than
 * turning it into a confusing CORS failure later.
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
 * The L lane's inputs come from the Backend's ENV_READY manifest and nowhere else. It is
 * read BEFORE the run identity is claimed, so a missing or not-ready manifest refuses
 * the run without consuming a run id, a directory or a cache location.
 */
export const liveManifest: ProductEnvironmentManifest | null = lane === 'L' ? readProductManifest() : null

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
