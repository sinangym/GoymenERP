import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { identity } from './run-identity'
export const here = path.dirname(fileURLToPath(import.meta.url))
export const repo = path.resolve(here, '../../..')
export const frontend = path.join(repo, 'src/frontend/goymen-erp-web')
export const origin = 'http://127.0.0.1:15174'

/**
 * Run identity, established once by the runner and joined (never recomputed) by every other
 * process — see run-identity.ts / run-ownership.ts. One run = one claimed directory
 * `<evidence>/runs/<id>` and one claimed cache outside the evidence tree; nothing is cleaned
 * across runs and the historical C-DESKTOP-010 folder is never written.
 */
export const { evidenceRoot, runId, runDir, viteCacheDir, coldStart, role, claimId, claimedAtUtc, ownerRecordPath } = identity
