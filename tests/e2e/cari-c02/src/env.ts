/**
 * The `c02` suite's inputs, from the BROWSER-CI workflow's contract (`tests/e2e/browser-ci/README.md`).
 * Every value is required; there is no default and no fallback, so a run outside the disposable stack
 * fails rather than quietly testing something else.
 *
 * THIS SUITE WRITES, deliberately, and says so in both registries: it is absent from the validator's
 * read-only set and resolves on the workflow's writing arm. That is not an omission to be tidied later
 * — C-02 creates a counterparty through the real API, so the post-browser SQL verification (zero audit
 * and command deltas) is exactly the assertion this journey must violate. Contrast org03, whose
 * comment had to be corrected for claiming a read-only guarantee nothing checked.
 *
 * NO FIXTURE AND NO SEED ENVELOPE, for the opposite reason from org03's: this suite does not need
 * data seeded because it CREATES the record it then reopens. Its only inputs are the two companies of
 * the built-in seed (for the other-company refusal) and its own actors.
 *
 * The actor ids are CONSTANTS, not run outputs, and are the same subjects
 * `infra/browser-suites/c02.override.yml` defines in this head. Named here once so no spec types a
 * subject inline: the override and this file must agree, and one place to change is what makes that
 * checkable.
 */

/**
 * Holds `counterparty.parties.create` + `counterparty.parties.read`, company-wide in company A.
 * The journey's actor: creates the card and reopens it.
 */
export const CREATOR_ACTOR = 'c02-creator'

/**
 * Holds the SAME two names, company-wide in company B. The edge the wider
 * `SingleOrAuthorizedMultiCompanyRead` context obliges us to measure: an actor who is fully
 * authorized — and in another company — must not reach company A's record. A refusal proved with an
 * UNAUTHORIZED actor would prove the permission fence, not the company fence, and those are
 * different claims.
 */
export const OTHER_COMPANY_ACTOR = 'c02-other-company'

/**
 * AT-004 read-only access view ("Kullanıcılar ve yetkiler"). Holds `identity.assignments.read` ONLY,
 * company-wide in company A. Reads the assignments of the two actors above; writes nothing.
 */
export const ACCESS_ADMIN_ACTOR = 'c02-access-admin'

/**
 * Holds the SAME single name, company-wide in company B — the company fence, measured the way C02-03
 * measures it: a fully authorized administrator of another company must not see company A's grants.
 */
export const ACCESS_ADMIN_OTHER_COMPANY_ACTOR = 'c02-access-admin-b'

/**
 * AT-009 maker (user decision 2026-09-30). Holds `counterparty.parties.create` + `counterparty.parties.read` +
 * `counterparty.parties.tax-identity.update` AND `counterparty.parties.tax-identity.approve`, company-wide in company
 * A. It holds approve on purpose: when its own change request offers it no decision (and the server refuses one), the
 * reason is the maker-checker rule, not a missing grant.
 */
export const TAX_MAKER_ACTOR = 'c02-tax-maker'

/**
 * AT-009 checker. Holds `counterparty.parties.read` + `counterparty.parties.tax-identity.approve` only, company-wide in
 * company A: the DIFFERENT person who decides the maker's change request.
 */
export const TAX_CHECKER_ACTOR = 'c02-tax-checker'

/** Identity user ids of the suite's own actors, as `infra/browser-suites/c02.override.yml` seeds them. */
export const CREATOR_USER_ID = 'b2000000-0000-4000-8000-000000000401'
export const OTHER_COMPANY_USER_ID = 'b2000000-0000-4000-8000-000000000402'

export interface C02Environment {
  readonly apiBaseUrl: string
  readonly testedCommit: string
}

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the c02 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

export function readEnvironment(): C02Environment {
  const testedCommit = required('BROWSER_E2E_TESTED_COMMIT')
  if (!/^[0-9a-f]{40}$/.test(testedCommit)) {
    throw new Error(`BROWSER_E2E_TESTED_COMMIT must be a full 40-character commit, got "${testedCommit}"`)
  }
  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }
  return { apiBaseUrl, testedCommit }
}
