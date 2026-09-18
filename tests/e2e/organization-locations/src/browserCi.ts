/**
 * The BROWSER-CI profile of the Organization manifest (`browser:org03:ci`,
 * `.github/workflows/browser-acceptance.yml`, `tests/e2e/browser-ci/README.md`).
 *
 * In that workflow the tuple is not a hand-written file: the workflow itself binds the exact
 * head, the disposable stack and its database, and the actors come from the stack's own
 * bootstrap identities. So the manifest is BUILT from the workflow's `BROWSER_E2E_*` contract,
 * with the same refusal rule as the file: every value required, no default, no fallback port.
 *
 * - One commit: the whole stack (API, web build, capability provider, shell mount, fixture and
 *   this journey) is built from the single exact head the workflow checked out, so every binding
 *   commit is that head. Recording six equal commits is the honest statement for this run.
 * - Actors: `BROWSER_E2E_ORG03_ACTORS` (JSON), bound by BROWSER-CI stage 2 together with their
 *   synthetic test-database grants. This package adds no actor, role or grant; without that
 *   binding the run refuses, and the workflow shows the refusal.
 */
import type { ActorGrant, OrganizationEnvironmentManifest } from './manifest.js'

export const BROWSER_CI_COMMIT_VAR = 'BROWSER_E2E_TESTED_COMMIT'
export const BROWSER_CI_ACTORS_VAR = 'BROWSER_E2E_ORG03_ACTORS'

function required(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) {
    throw new Error(`${name} is not set; the org03 suite runs only inside the BROWSER-CI disposable stack.`)
  }
  return value
}

function text(raw: Record<string, unknown>, field: string): string {
  const value = raw[field]
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${BROWSER_CI_ACTORS_VAR} field "${field}" is missing or empty`)
  }
  return value.trim()
}

function optional(raw: Record<string, unknown>, field: string): string | undefined {
  const value = raw[field]
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined
}

/** True when the BROWSER-CI workflow runs this suite. */
export function isBrowserCi(): boolean {
  return process.env[BROWSER_CI_COMMIT_VAR] !== undefined
}

export function readBrowserCiManifest(): OrganizationEnvironmentManifest {
  const commit = required(BROWSER_CI_COMMIT_VAR)
  if (!/^[0-9a-f]{40}$/.test(commit)) {
    throw new Error(`${BROWSER_CI_COMMIT_VAR} must be a full 40-character commit, got "${commit}"`)
  }

  const apiBaseUrl = required('BROWSER_E2E_API_BASE_URL')
  if (new URL(apiBaseUrl).hostname !== '127.0.0.1') {
    throw new Error(`BROWSER_E2E_API_BASE_URL must be the disposable loopback API, got ${apiBaseUrl}`)
  }

  const database = required('BROWSER_E2E_DATABASE')
  const runId = required('GITHUB_RUN_ID')
  const raw = JSON.parse(required(BROWSER_CI_ACTORS_VAR)) as Record<string, unknown>
  const grantsRaw = (raw.actorGrants ?? {}) as Record<string, unknown>
  const actorGrants: Record<string, ActorGrant> = {}
  for (const [actor, grant] of Object.entries(grantsRaw)) {
    const entry = (grant ?? {}) as Record<string, unknown>
    const list = (value: unknown, field: string): string[] => {
      if (!Array.isArray(value) || value.length === 0 || value.some((item) => typeof item !== 'string' || !item.trim())) {
        throw new Error(`${BROWSER_CI_ACTORS_VAR} field "actorGrants.${actor}.${field}" must be a non-empty list`)
      }
      return value.map((item) => (item as string).trim())
    }
    actorGrants[actor] = { roles: list(entry.roles, 'roles'), scopes: list(entry.scopes, 'scopes') }
  }

  const workCentersOnlyActor = optional(raw, 'workCentersOnlyActor')
  const rowScopedActor = optional(raw, 'rowScopedActor')
  for (const [field, actor] of [
    ['workCentersOnlyActor', workCentersOnlyActor],
    ['rowScopedActor', rowScopedActor],
  ] as const) {
    if (actor !== undefined && actorGrants[actor] === undefined) {
      throw new Error(`${BROWSER_CI_ACTORS_VAR} binds ${field} "${actor}" but declares no actorGrants entry for it`)
    }
  }

  return {
    envReady: true,
    apiBaseUrl,
    operatorActor: text(raw, 'operatorActor'),
    viewerActor: text(raw, 'viewerActor'),
    foreignActor: text(raw, 'foreignActor'),
    locationsOnlyActor: optional(raw, 'locationsOnlyActor'),
    multiCompanyActor: optional(raw, 'multiCompanyActor'),
    workCentersOnlyActor,
    rowScopedActor,
    actorGrants,
    companyACode: text(raw, 'companyACode'),
    companyBCode: text(raw, 'companyBCode'),
    runPrefix: `ORG03-CI-${runId}`,
    runId,
    binding: {
      backendCommit: commit,
      frontendCommit: commit,
      capabilityProviderCommit: commit,
      shellMountCommit: commit,
      fixtureCommit: commit,
      browserJourneyCommit: commit,
      apiWorktree: `github-actions:${commit}`,
      frontendWorktree: `github-actions:${commit}`,
      containerName: required('COMPOSE_PROJECT_NAME'),
      ownerLabel: 'BROWSER-CI',
      databaseName: database,
    },
  }
}
