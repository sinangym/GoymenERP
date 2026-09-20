/**
 * The `org03` suite's inputs, from the BROWSER-CI workflow's contract (`tests/e2e/browser-ci/README.md`).
 * Every value is required; there is no default and no fallback, so a run outside the disposable stack
 * fails rather than quietly testing something else.
 *
 * THIS SUITE HAS NO FIXTURE AND NO SEED ENVELOPE, and that is a property of what it is: it is READ-ONLY
 * (`READ_ONLY_SUITES`), it writes nothing, and everything it reads — the five seeded work centres and
 * their locations — is already in the built-in organization seed. A suite that seeded data would need
 * the fixture runner and a `BROWSER_E2E_ORG03_SEED` envelope; this one has nothing to seed.
 *
 * The two actor ids below are therefore CONSTANTS rather than run outputs, and they are the same two
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
