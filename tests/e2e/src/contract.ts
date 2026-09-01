/**
 * The C-01 browser-suite contract, in one place.
 *
 * These values are NOT free choices. `tools/validate_c01_e2e_contract.py` checks
 * every one of them, and the run fails if the emitted evidence disagrees. They
 * are duplicated here rather than imported because the validator is Python and
 * runs in a separate step — so the duplication is deliberate, and the validator
 * is the authority whenever the two disagree.
 */

/** B-C01-001 … B-C01-010, in this exact order. */
export const EXPECTED_CASES = Array.from(
  { length: 10 },
  (_, index) => `B-C01-${String(index + 1).padStart(3, '0')}`,
) as readonly string[]

/** The declared reflow matrix. B-C01-009 is the case that actually walks it. */
export const EXPECTED_VIEWPORTS = ['1600x900', '768x1024', '390x844', '640x900', '320x900'] as const

/** Only these four paths may appear in the network evidence at all. */
export const REQUIRED_REAL_PATHS = [
  '/api/v1/identity/me',
  '/api/v1/organization/companies',
  '/api/v1/shell/capabilities',
  '/api/v1/counterparties',
] as const

/**
 * Every one of these must be observed at least once, and every one of them must
 * be bound to `/api/v1/counterparties` — a fault injected anywhere else is a
 * suite bug, not evidence.
 */
export const REQUIRED_FAULT_OUTCOMES = [
  'injected-delay',
  'injected-500',
  'injected-503',
  'injected-abort',
  'injected-offline',
  'retry-200',
  'real-400-invalid-cursor',
] as const

export type FaultOutcome = (typeof REQUIRED_FAULT_OUTCOMES)[number]
export type NetworkOutcome = FaultOutcome | 'real-200' | 'real-401' | 'real-403'

/** One observed request, exactly the shape the validator requires. */
export interface NetworkRecord {
  readonly method: 'GET'
  readonly path: string
  /** Assigned by the reporter across the whole run: unique, contiguous, from 1. */
  ordinal: number
  readonly outcome: NetworkOutcome
  /** > 0 only for `retry-200`; the validator rejects any other combination. */
  readonly retryOrdinal: number
}

/** The per-test payload specs attach; the reporter aggregates and re-ordinals it. */
export const NETWORK_ATTACHMENT = 'c01-network'

export interface EnvContext {
  readonly authorizedBaseUrl: string
  readonly unauthorizedBaseUrl: string
  readonly apiBaseUrl: string
  readonly artifactsDir: string
  readonly testedCommit: string
  readonly runId: string
  readonly composeProject: string
  readonly fixtureEvidencePath: string
}

function required(name: string): string {
  const value = process.env[name]
  if (!value || !value.trim()) {
    throw new Error(
      `${name} is not set. The C-01 browser suite only runs against the disposable ` +
        `compose stack that c01-real-browser-e2e.yml stands up; it has no defaults ` +
        `on purpose, so it can never silently point at something else.`,
    )
  }
  return value.trim()
}

export function readEnv(): EnvContext {
  const artifactsDir = required('C01_E2E_ARTIFACTS_DIR')
  return {
    authorizedBaseUrl: required('C01_E2E_AUTHORIZED_BASE_URL'),
    unauthorizedBaseUrl: required('C01_E2E_UNAUTHORIZED_BASE_URL'),
    apiBaseUrl: required('C01_E2E_API_BASE_URL'),
    artifactsDir,
    testedCommit: required('C01_E2E_TESTED_COMMIT'),
    runId: required('GITHUB_RUN_ID'),
    composeProject: required('C01_E2E_COMPOSE_PROJECT'),
    fixtureEvidencePath:
      process.env.GOYMEN_ERP_C01_E2E_FIXTURE_EVIDENCE?.trim() ||
      `${artifactsDir}/c01-e2e-fixture-evidence.json`,
  }
}

/**
 * Strip everything the validator forbids from a URL before it is recorded.
 *
 * The query string is removed entirely rather than filtered. A cursor is an
 * opaque token derived from row keys, and the evidence file is a build artifact
 * that outlives the disposable stack — an allowlist would be one forgotten
 * parameter away from publishing it.
 */
export function redactPath(url: string): string {
  const parsed = new URL(url)
  return parsed.pathname
}
