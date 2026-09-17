/**
 * The environment tuple this suite refuses to run without.
 *
 * WHY A GATE AND NOT A DEFAULT. A browser acceptance run is only evidence about the
 * thing it actually drove. If the manifest is missing or half-filled, the honest
 * outcome is to refuse — not to fall back to an ambient port and produce a green report
 * about some other process. So every field below is required, every one is checked, and
 * the errors name what is missing rather than saying "not ready".
 *
 * The tuple mirrors `tests/e2e-fixtures/organization-locations/README.md`
 * (`runtimeBinding.requiredBeforeStart`): the run is bound to concrete backend,
 * frontend, capability-provider, shell-mount, fixture and journey commits, to the
 * worktrees they came from, and to the owned container/database/ports. Recording the
 * provider and mount commits even when they are already ancestors is deliberate — "it
 * was in main" is not the same statement as "this run served it".
 *
 * WHO WRITES IT: the Organization backend owner, as part of declaring ENV_READY. This
 * package never writes it and never fills a blank in.
 */
import { readFileSync } from 'node:fs'

/** Ports reserved for the Organization lane. Cari (25xxx) and Ürün (26xxx) are not ours. */
export const ORG_UI_PORT = 27173
export const ORG_API_PORT = 27080
export const ORG_SQL_PORT = 27433

export interface OrganizationEnvironmentManifest {
  /** Must be literally true. A filled-in status string alone authorizes nothing. */
  readonly envReady: boolean
  /** e.g. `http://127.0.0.1:27080`. */
  readonly apiBaseUrl: string
  /** Development bootstrap subject with read + status.update in company A. */
  readonly operatorActor: string
  /** Site-scoped read-only subject (the viewer). */
  readonly viewerActor: string
  /** Subject in the OTHER company, for the isolation checks. */
  readonly foreignActor: string
  /**
   * Subject holding locations.read but NOT sites.read.
   *
   * OPTIONAL on purpose: the fixture package marks this variant `NOT_PREPARED`
   * (PR #244 `a2a3cfc8`). When it is absent the no-sites case SKIPS and says so; it is
   * never reported as covered. Inventing an actor here would be exactly the
   * fabrication the fixture forbids.
   */
  readonly locationsOnlyActor?: string
  /**
   * ORG-03 (#66 5698360588 item 5 and the empty half of item 6): a subject authorized in
   * BOTH companies with `organization.work-centers.read`. The normal Development seed
   * already carries one (`group-bootstrap-admin`, SYSTEM_ADMINISTRATOR in GDC, GSP and
   * GIN); this package still takes it from the manifest rather than naming it.
   */
  readonly multiCompanyActor?: string
  /**
   * ORG-03 item 3: `organization.work-centers.read` WITHOUT `organization.locations.read`.
   * The normal seed has NO such subject. Optional, and absent means the case SKIPS and
   * `coverage.json` says so — this package adds no actor, role or grant.
   */
  readonly workCentersOnlyActor?: string
  /**
   * ORG-03 item 4 as changed by #66 5700319744: `work-centers.read` on two locations and
   * `locations.read` on only one of them. The normal seed has NO such subject either;
   * same rule as above.
   */
  readonly rowScopedActor?: string
  /**
   * What each bound ORG-03 test actor was GIVEN, copied verbatim into `coverage.json`
   * (Business #66 5705730345: actor, role, scope and run identity are bound to the
   * evidence). Declared by the environment owner from the run's seed override; this
   * package does not check it against the server — the capability snapshot and the rows
   * each case observes are what is measured. REQUIRED for `workCentersOnlyActor` and
   * `rowScopedActor`: an item-3/4 run whose grant is undeclared is refused, not recorded.
   */
  readonly actorGrants: Readonly<Record<string, ActorGrant>>
  readonly companyACode: string
  readonly companyBCode: string
  /** Prefix stamped into every reason this run writes, so rows are attributable. */
  readonly runPrefix: string
  readonly runId: string
  readonly binding: OrganizationRunBinding
}

export interface OrganizationRunBinding {
  readonly backendCommit: string
  readonly frontendCommit: string
  readonly capabilityProviderCommit: string
  readonly shellMountCommit: string
  readonly fixtureCommit: string
  readonly browserJourneyCommit: string
  readonly apiWorktree: string
  readonly frontendWorktree: string
  readonly containerName: string
  readonly ownerLabel: string
  readonly databaseName: string
}

/** One actor's declared grant: role codes, and `Type:id` scopes per assignment. */
export interface ActorGrant {
  readonly roles: readonly string[]
  readonly scopes: readonly string[]
}

const COMMIT_FIELDS = [
  'backendCommit',
  'frontendCommit',
  'capabilityProviderCommit',
  'shellMountCommit',
  'fixtureCommit',
  'browserJourneyCommit',
] as const

const TEXT_FIELDS = [
  'apiWorktree',
  'frontendWorktree',
  'containerName',
  'ownerLabel',
  'databaseName',
] as const

export const MANIFEST_ENV_VAR = 'ORG_ENVIRONMENT_MANIFEST'

function requireText(value: unknown, field: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`environment manifest field "${field}" is missing or empty`)
  }
  return value.trim()
}

/** 40 hex characters. An abbreviated commit does not identify a tree unambiguously. */
function requireCommit(value: unknown, field: string): string {
  const text = requireText(value, field)
  if (!/^[0-9a-f]{40}$/.test(text)) {
    throw new Error(`environment manifest field "${field}" must be a full 40-character commit, got "${text}"`)
  }
  return text
}

export function readOrganizationManifest(): OrganizationEnvironmentManifest {
  const file = process.env[MANIFEST_ENV_VAR]
  if (!file) {
    throw new Error(
      `${MANIFEST_ENV_VAR} is not set. This suite runs only against the Organization ` +
        'environment declared ENV_READY by its backend owner; there is no default and no fallback.',
    )
  }
  const raw = JSON.parse(readFileSync(file, 'utf8')) as Record<string, unknown>

  if (raw.envReady !== true) {
    throw new Error('environment manifest does not declare envReady === true')
  }

  const apiBaseUrl = requireText(raw.apiBaseUrl, 'apiBaseUrl')
  /**
   * The API must be the Organization loopback port. A run pointed at another lane's
   * API would be measuring someone else's environment while claiming to measure ours,
   * and would also be touching a process we do not own.
   */
  const url = new URL(apiBaseUrl)
  if (url.hostname !== '127.0.0.1' || url.port !== String(ORG_API_PORT)) {
    throw new Error(
      `apiBaseUrl must be the Organization loopback API http://127.0.0.1:${ORG_API_PORT}, got ${apiBaseUrl}`,
    )
  }

  const bindingRaw = (raw.binding ?? {}) as Record<string, unknown>
  const binding = {
    ...Object.fromEntries(COMMIT_FIELDS.map((f) => [f, requireCommit(bindingRaw[f], `binding.${f}`)])),
    ...Object.fromEntries(TEXT_FIELDS.map((f) => [f, requireText(bindingRaw[f], `binding.${f}`)])),
  } as unknown as OrganizationRunBinding

  const optionalActor = (value: unknown): string | undefined =>
    typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined
  const locationsOnlyActor = optionalActor(raw.locationsOnlyActor)
  const workCentersOnlyActor = optionalActor(raw.workCentersOnlyActor)
  const rowScopedActor = optionalActor(raw.rowScopedActor)

  const grantsRaw = (raw.actorGrants ?? {}) as Record<string, unknown>
  const textList = (value: unknown, field: string): string[] => {
    if (!Array.isArray(value) || value.length === 0) {
      throw new Error(`environment manifest field "${field}" must be a non-empty list`)
    }
    return value.map((item, index) => requireText(item, `${field}[${index}]`))
  }
  const actorGrants: Record<string, ActorGrant> = {}
  for (const [actor, grant] of Object.entries(grantsRaw)) {
    const entry = (grant ?? {}) as Record<string, unknown>
    actorGrants[actor] = {
      roles: textList(entry.roles, `actorGrants.${actor}.roles`),
      scopes: textList(entry.scopes, `actorGrants.${actor}.scopes`),
    }
  }
  for (const [field, actor] of [
    ['workCentersOnlyActor', workCentersOnlyActor],
    ['rowScopedActor', rowScopedActor],
  ] as const) {
    if (actor !== undefined && actorGrants[actor] === undefined) {
      throw new Error(`environment manifest binds ${field} "${actor}" but declares no actorGrants entry for it`)
    }
  }

  return {
    envReady: true,
    apiBaseUrl,
    operatorActor: requireText(raw.operatorActor, 'operatorActor'),
    viewerActor: requireText(raw.viewerActor, 'viewerActor'),
    foreignActor: requireText(raw.foreignActor, 'foreignActor'),
    locationsOnlyActor,
    multiCompanyActor: optionalActor(raw.multiCompanyActor),
    workCentersOnlyActor,
    rowScopedActor,
    actorGrants,
    companyACode: requireText(raw.companyACode, 'companyACode'),
    companyBCode: requireText(raw.companyBCode, 'companyBCode'),
    runPrefix: requireText(raw.runPrefix, 'runPrefix'),
    runId: requireText(raw.runId, 'runId'),
    binding,
  }
}
