import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { establishRunIdentity } from '../cari-acceptance-010/run-ownership'
import type { RunIdentity } from '../cari-acceptance-010/run-ownership'

export const here = path.dirname(fileURLToPath(import.meta.url))
export const repo = path.resolve(here, '../../..')
export const frontend = path.join(repo, 'src/frontend/goymen-erp-web')
/**
 * Loopback origin of the controlled-T dev server. NOT the reserved Ürün UI port
 * (26173): that one belongs to the real environment this suite does not have yet,
 * and the T lane must be able to run beside it without touching it.
 */
export const origin = 'http://127.0.0.1:15175'

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
