/**
 * The ONE verified identity of this run for the process that imports it (see run-ownership.ts).
 *
 * In the Playwright runner (no CARI010_RUN_CLAIM in the environment) this claims the run
 * directory and the Vite cache atomically, measures the cold-start condition once and exports
 * CARI010_RUN_ID / CARI010_VITE_CACHE_DIR / CARI010_COLD_START / CARI010_RUN_CLAIM to every
 * child process. In workers and in the Vite web server (claim present) it only verifies the
 * recorded claim and returns the recorded identity — nothing is recomputed there.
 *
 * Any refusal throws here, while the config is being loaded: before the web server, the
 * browser or any test starts. Refusals never remove, move or overwrite existing bytes.
 */
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { establishRunIdentity } from './run-ownership'
import type { RunIdentity } from './run-ownership'

const here = path.dirname(fileURLToPath(import.meta.url))
const repo = path.resolve(here, '../../..')

export const identity: RunIdentity = establishRunIdentity(process.env, {
  historicalRoots: [path.resolve(repo, '../GoymenERP-orchestration/evidence/C-DESKTOP-010')],
  forbiddenCacheParents: [repo],
})
