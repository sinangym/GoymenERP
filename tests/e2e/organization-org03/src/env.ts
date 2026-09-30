/**
 * The `org03` suite's inputs, from the BROWSER-CI workflow's contract (`tests/e2e/browser-ci/README.md`).
 * Every value is required; there is no default and no fallback, so a run outside the disposable stack
 * fails rather than quietly testing something else.
 *
 * THIS SUITE HAS NO FIXTURE AND NO SEED ENVELOPE: everything it reads — the five seeded work centres and
 * their locations — is already in the built-in organization seed, and what the ORG master-edit cases write
 * they create themselves through the real screens. A suite that seeded data would need the fixture runner and a
 * `BROWSER_E2E_ORG03_SEED` envelope; this one has nothing to seed.
 *
 * THE SUITE WRITES since the ORG master edit, and it is registered as writing: the validator's read-only set
 * does not contain it and the browser workflow resolves it on the writing arm. Only org03-editor holds a
 * write; the reader cases never issue a command.
 *
 * The actor ids below are CONSTANTS rather than run outputs, and they are the same
 * subjects `infra/browser-suites/org03.override.yml` defines in the same head. They are named here once
 * so a spec never types a subject inline: the override and this file must say the same thing, and one
 * place to change is what makes that checkable.
 */

/**
 * Holds `organization.work-centers.read` ALONE. The actor the slice exists for: ORG-03 must be reachable
 * from the sidebar for them, and ORG-01 must not open at all.
 */
export const WORK_CENTERS_ONLY_ACTOR = 'org03-work-centers-only'

/** Holds `organization.work-centers.read` + `organization.locations.read`: the row link resolves. */
export const READER_ACTOR = 'org03-reader'

/**
 * ORG master edit (user decision 2026-09-27): the only holder of `organization.sites.write`,
 * `organization.locations.write` and `organization.work-centers.write`, beside the three reads its screens need.
 * Its cases WRITE (sites, locations, work centres) in the run's disposable database, so they run LAST in the spec:
 * the read cases above count the five seeded work centres exactly.
 */
export const EDITOR_ACTOR = 'org03-editor'

/** GDC, the only company every org03 actor holds an assignment in (infra/browser-suites/org03.override.yml). */
export const GDC_COMPANY_ID = '10000000-0000-0000-0000-000000000001'

export interface Org03Environment {
  readonly apiBaseUrl: string
  readonly testedCommit: string
}

export function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the org03 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

export function readEnvironment(): Org03Environment {
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
