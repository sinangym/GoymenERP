/**
 * WP-A5 (AI-20, DALGA_PLANI_v2 A.5) AY-YZG-01 — the AI management screen's three reads and one command:
 *
 *  - `GET  /api/v1/settings/company-policies/ai-gateway` — the versioned policy in force (feature switches, the
 *    "task kind × data class" model table, the monthly ceiling, the per-request reservation), the current month's ledger
 *    total and the version history. Version "0" is "nothing recorded": every feature closed, no route, no budget.
 *  - `GET  …/ai-gateway/usage` — the current month per feature from the ledger, and the acceptance counts once a WP-C9
 *    source exists (`acceptanceMeasured: false` means "not measured yet", never zero).
 *  - `POST …/ai-gateway/versions` — one new full snapshot (K3: expected version, Idempotency-Key, reason).
 *  - `GET  /api/v1/security-events?eventType=ai.…` — the AI security/audit events, read-only.
 *
 * Every decoder is strict: exact keys, the selected company, defined vocabulary, ordered histories; anything else is
 * malformed (fail-closed null). The fixed safety rules (AI-D3/AI-D4, AI-D24) are restated here only to draw the editor;
 * the server refuses a route that breaks them and the gateway re-checks them on every call.
 */

export const AI_GATEWAY_PATH = '/settings/company-policies/ai-gateway'
export const AI_GATEWAY_VERSIONS_PATH = `${AI_GATEWAY_PATH}/versions`
export const AI_GATEWAY_USAGE_PATH = `${AI_GATEWAY_PATH}/usage`
export const SECURITY_EVENTS_PATH = '/security-events'

/** `AiFeatureV1` member names, in contract order. */
export const AI_FEATURES = ['Panel', 'Summary', 'Document', 'Query', 'Write', 'Chat', 'Scheduled', 'Memory', 'Email'] as const
export type AiFeature = (typeof AI_FEATURES)[number]
/** `AiTaskKindV1`. */
export const AI_TASK_KINDS = ['Answer', 'Summarize', 'Extract', 'Classify', 'Draft', 'Converse'] as const
export type AiTaskKind = (typeof AI_TASK_KINDS)[number]
/** `AiDataClassV1`, lowest first. */
export const AI_DATA_CLASSES = ['Public', 'Internal', 'Confidential', 'StrictlyConfidential'] as const
export type AiDataClass = (typeof AI_DATA_CLASSES)[number]
/** `AiModelPlacementV1`. */
export const AI_PLACEMENTS = ['Cloud', 'OnPremises'] as const
export type AiPlacement = (typeof AI_PLACEMENTS)[number]

/** The AI security event types the screen lists (`SecurityEventTypesV1`), read one type at a time. */
export const AI_EVENT_TYPES = ['ai.request.refused', 'ai.tool.called', 'ai.authz.denied'] as const
export type AiEventType = (typeof AI_EVENT_TYPES)[number]
export const AI_EVENT_PAGE_SIZE = 25

/** The server's input bounds (`SettingsAiGatewayLimitsV1`) — bounds against typing errors, never a budget. */
export const AI_GATEWAY_LIMITS = {
  maximumRoutes: 24,
  modelDeploymentMaximumLength: 128,
  maximumMonthlyUsageCeiling: 1_000_000_000_000,
  maximumUnitsPerRequest: 10_000_000,
  reasonMaximumLength: 4000,
} as const

/** AI-D3/AI-D4 (`AiGatewayPlacementRulesV1.CloudMaximumDataClass`): a cloud model never receives more than Internal. */
export const CLOUD_MAXIMUM_DATA_CLASS: AiDataClass = 'Internal'
export const placementAllows = (placement: AiPlacement, dataClass: AiDataClass): boolean =>
  placement === 'OnPremises' || AI_DATA_CLASSES.indexOf(dataClass) <= AI_DATA_CLASSES.indexOf(CLOUD_MAXIMUM_DATA_CLASS)
/** AI-D24/AI-D27 (`FeatureAllows`): document and e-mail free text is read only by a company-internal model. */
export const ON_PREMISES_ONLY_FEATURES: readonly AiFeature[] = ['Document', 'Email']

export interface AiModelRoute {
  readonly taskKind: AiTaskKind
  readonly dataClass: AiDataClass
  readonly placement: AiPlacement
  readonly modelDeployment: string
}

export interface AiGatewayPolicyVersion {
  readonly version: string
  readonly monthlyUsageCeiling: number | null
  readonly maxUnitsPerRequest: number | null
  readonly featureCount: number
  readonly routeCount: number
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface AiBudgetMonth {
  readonly budgetMonth: number
  readonly committedUnits: number
  readonly entryCount: number
}

export interface AiGatewayPolicy {
  readonly companyId: string
  readonly version: string
  readonly monthlyUsageCeiling: number | null
  readonly maxUnitsPerRequest: number | null
  readonly enabledFeatures: readonly AiFeature[]
  readonly routes: readonly AiModelRoute[]
  readonly currentMonth: AiBudgetMonth
  readonly versions: readonly AiGatewayPolicyVersion[]
}

export interface AiFeatureUsage {
  readonly feature: AiFeature
  readonly entryCount: number
  readonly openReservationCount: number
  readonly committedUnits: number
}

export interface AiFeatureAcceptance {
  readonly feature: AiFeature
  readonly accepted: number
  readonly corrected: number
  readonly rejected: number
  readonly reverted: number
}

export interface AiGatewayUsage {
  readonly companyId: string
  readonly budgetMonth: number
  readonly committedUnits: number
  readonly entryCount: number
  readonly features: readonly AiFeatureUsage[]
  readonly acceptanceMeasured: boolean
  readonly acceptance: readonly AiFeatureAcceptance[]
}

export interface AiSecurityEvent {
  readonly id: string
  readonly occurredAtUtc: string
  readonly eventType: AiEventType
  readonly outcome: string
  readonly reasonCode: string | null
  readonly severity: string
  readonly actorType: string
  readonly permissionCode: string | null
  readonly correlationId: string
  readonly aiToolId: string | null
  readonly aiToolVersion: string | null
  readonly aiRiskClass: string | null
  readonly aiDataClass: string | null
  readonly occurrenceCount: number
}

export interface AiSecurityEventPage {
  readonly items: readonly AiSecurityEvent[]
  readonly nextCursor: string | null
}

export interface RecordAiModelRouteBody {
  readonly taskKind: AiTaskKind
  readonly dataClass: AiDataClass
  readonly placement: AiPlacement
  readonly modelDeployment: string
}

export interface RecordAiGatewayPolicyBody {
  readonly expectedVersion: string
  readonly monthlyUsageCeiling: number | null
  readonly maxUnitsPerRequest: number | null
  readonly enabledFeatures: readonly AiFeature[]
  readonly routes: readonly RecordAiModelRouteBody[]
  readonly reason: string
}

/** YOL-D21/S5: a budget is "entered" only with a positive monthly ceiling AND a per-request reservation. */
export const budgetEntered = (ceiling: number | null, perRequest: number | null): boolean =>
  ceiling !== null && ceiling > 0 && perRequest !== null

const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, names: readonly string[]) =>
  Object.keys(raw).length === names.length && names.every((name) => Object.hasOwn(raw, name))
const nonempty = (raw: unknown, max = 4000): raw is string => typeof raw === 'string' && raw.trim().length > 0 && raw.length <= max
const optionalText = (raw: unknown, max = 256): raw is string | null => raw === null || nonempty(raw, max)
const version = (raw: unknown): raw is string =>
  typeof raw === 'string' && /^(0|[1-9]\d{0,18})$/.test(raw) && BigInt(raw) <= 9223372036854775807n
const count = (raw: unknown): raw is number => typeof raw === 'number' && Number.isSafeInteger(raw) && raw >= 0
const bounded = (raw: unknown, min: number, max: number): raw is number => count(raw) && raw >= min && raw <= max
const instant = (raw: unknown): raw is string =>
  typeof raw === 'string' && raw.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(raw) && Number.isFinite(Date.parse(raw))
const guid = (raw: unknown): raw is string =>
  typeof raw === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(raw)
const sameCompany = (raw: unknown, company: string) => typeof raw === 'string' && raw.toLowerCase() === company.toLowerCase()
const member = <T extends string>(list: readonly T[], raw: unknown): raw is T => typeof raw === 'string' && (list as readonly string[]).includes(raw)
/** yyyymm with a real month. */
const budgetMonth = (raw: unknown): raw is number =>
  typeof raw === 'number' && Number.isInteger(raw) && raw >= 200001 && raw <= 999912 && raw % 100 >= 1 && raw % 100 <= 12
const ceiling = (raw: unknown): raw is number | null => raw === null || bounded(raw, 0, AI_GATEWAY_LIMITS.maximumMonthlyUsageCeiling)
const perRequest = (raw: unknown): raw is number | null => raw === null || bounded(raw, 1, AI_GATEWAY_LIMITS.maximumUnitsPerRequest)

const ROOT_KEYS = ['companyId', 'version', 'monthlyUsageCeiling', 'maxUnitsPerRequest', 'enabledFeatures', 'routes', 'currentMonth', 'versions'] as const
const ROUTE_KEYS = ['taskKind', 'dataClass', 'placement', 'modelDeployment'] as const
const MONTH_KEYS = ['budgetMonth', 'committedUnits', 'entryCount'] as const
const VERSION_KEYS = ['version', 'monthlyUsageCeiling', 'maxUnitsPerRequest', 'featureCount', 'routeCount', 'recordedBy', 'recordedAtUtc', 'reason'] as const

function decodeRoute(raw: unknown): AiModelRoute | null {
  if (!object(raw) || !exact(raw, ROUTE_KEYS) || !member(AI_TASK_KINDS, raw.taskKind) || !member(AI_DATA_CLASSES, raw.dataClass)
    || !member(AI_PLACEMENTS, raw.placement) || !nonempty(raw.modelDeployment, AI_GATEWAY_LIMITS.modelDeploymentMaximumLength)
    || !placementAllows(raw.placement, raw.dataClass)) return null
  return { taskKind: raw.taskKind, dataClass: raw.dataClass, placement: raw.placement, modelDeployment: raw.modelDeployment }
}

export function decodeAiGatewayPolicy(raw: unknown, company: string): AiGatewayPolicy | null {
  if (!object(raw) || !exact(raw, ROOT_KEYS) || !sameCompany(raw.companyId, company) || !version(raw.version)
    || !ceiling(raw.monthlyUsageCeiling) || !perRequest(raw.maxUnitsPerRequest)
    || !Array.isArray(raw.enabledFeatures) || !Array.isArray(raw.routes) || !Array.isArray(raw.versions)) return null
  const features: AiFeature[] = []
  for (const item of raw.enabledFeatures) {
    if (!member(AI_FEATURES, item) || features.includes(item)) return null
    features.push(item)
  }
  if (raw.routes.length > AI_GATEWAY_LIMITS.maximumRoutes) return null
  const routes: AiModelRoute[] = []
  for (const item of raw.routes) {
    const route = decodeRoute(item)
    if (route === null || routes.some((existing) => existing.taskKind === route.taskKind && existing.dataClass === route.dataClass)) return null
    routes.push(route)
  }
  const month = raw.currentMonth
  if (!object(month) || !exact(month, MONTH_KEYS) || !budgetMonth(month.budgetMonth) || !count(month.committedUnits) || !count(month.entryCount)) return null
  const history: AiGatewayPolicyVersion[] = []
  for (const item of raw.versions) {
    if (!object(item) || !exact(item, VERSION_KEYS) || !version(item.version) || item.version === '0'
      || !ceiling(item.monthlyUsageCeiling) || !perRequest(item.maxUnitsPerRequest)
      || !bounded(item.featureCount, 0, AI_FEATURES.length) || !bounded(item.routeCount, 0, AI_GATEWAY_LIMITS.maximumRoutes)
      || !nonempty(item.recordedBy, 256) || !instant(item.recordedAtUtc) || !nonempty(item.reason)
      || BigInt(item.version) > BigInt(raw.version)
      || (history.length > 0 && BigInt(history[history.length - 1].version) <= BigInt(item.version))) return null
    history.push({
      version: item.version, monthlyUsageCeiling: item.monthlyUsageCeiling, maxUnitsPerRequest: item.maxUnitsPerRequest,
      featureCount: item.featureCount, routeCount: item.routeCount, recordedBy: item.recordedBy, recordedAtUtc: item.recordedAtUtc,
      reason: item.reason,
    })
  }
  // Nothing recorded: closed, empty, no history. Recorded: the newest version is the one in force, with its counts.
  if (raw.version === '0') {
    if (features.length !== 0 || routes.length !== 0 || raw.monthlyUsageCeiling !== null || raw.maxUnitsPerRequest !== null
      || history.length !== 0) return null
  } else {
    const newest = history[0]
    if (newest?.version !== raw.version || newest.monthlyUsageCeiling !== raw.monthlyUsageCeiling
      || newest.maxUnitsPerRequest !== raw.maxUnitsPerRequest || newest.featureCount !== features.length
      || newest.routeCount !== routes.length) return null
  }
  // YOL-D21: a policy with an open feature and no budget cannot come from this server.
  if (features.length > 0 && !budgetEntered(raw.monthlyUsageCeiling, raw.maxUnitsPerRequest)) return null
  return {
    companyId: company, version: raw.version, monthlyUsageCeiling: raw.monthlyUsageCeiling, maxUnitsPerRequest: raw.maxUnitsPerRequest,
    enabledFeatures: features, routes,
    currentMonth: { budgetMonth: month.budgetMonth, committedUnits: month.committedUnits, entryCount: month.entryCount },
    versions: history,
  }
}

const USAGE_KEYS = ['companyId', 'budgetMonth', 'committedUnits', 'entryCount', 'features', 'acceptanceMeasured', 'acceptance'] as const
const FEATURE_USAGE_KEYS = ['feature', 'entryCount', 'openReservationCount', 'committedUnits'] as const
const ACCEPTANCE_KEYS = ['feature', 'accepted', 'corrected', 'rejected', 'reverted'] as const

export function decodeAiGatewayUsage(raw: unknown, company: string): AiGatewayUsage | null {
  if (!object(raw) || !exact(raw, USAGE_KEYS) || !sameCompany(raw.companyId, company) || !budgetMonth(raw.budgetMonth)
    || !count(raw.committedUnits) || !count(raw.entryCount) || typeof raw.acceptanceMeasured !== 'boolean'
    || !Array.isArray(raw.features) || !Array.isArray(raw.acceptance)) return null
  const features: AiFeatureUsage[] = []
  for (const item of raw.features) {
    if (!object(item) || !exact(item, FEATURE_USAGE_KEYS) || !member(AI_FEATURES, item.feature) || !bounded(item.entryCount, 1, Number.MAX_SAFE_INTEGER)
      || !count(item.openReservationCount) || item.openReservationCount > item.entryCount || !count(item.committedUnits)
      || features.some((existing) => existing.feature === item.feature)) return null
    features.push({ feature: item.feature, entryCount: item.entryCount, openReservationCount: item.openReservationCount, committedUnits: item.committedUnits })
  }
  // The per-feature lines are the month's ledger split; they must add up to its totals.
  if (features.reduce((sum, line) => sum + line.entryCount, 0) !== raw.entryCount
    || features.reduce((sum, line) => sum + line.committedUnits, 0) !== raw.committedUnits) return null
  if (!raw.acceptanceMeasured && raw.acceptance.length !== 0) return null
  const acceptance: AiFeatureAcceptance[] = []
  for (const item of raw.acceptance) {
    if (!object(item) || !exact(item, ACCEPTANCE_KEYS) || !member(AI_FEATURES, item.feature) || !count(item.accepted)
      || !count(item.corrected) || !count(item.rejected) || !count(item.reverted)
      || acceptance.some((existing) => existing.feature === item.feature)) return null
    acceptance.push({ feature: item.feature, accepted: item.accepted, corrected: item.corrected, rejected: item.rejected, reverted: item.reverted })
  }
  return {
    companyId: company, budgetMonth: raw.budgetMonth, committedUnits: raw.committedUnits, entryCount: raw.entryCount, features,
    acceptanceMeasured: raw.acceptanceMeasured, acceptance,
  }
}

const EVENT_PAGE_KEYS = ['items', 'pageSize', 'nextCursor'] as const
const EVENT_KEYS = [
  'id', 'schemaVersion', 'occurredAtUtc', 'companyId', 'requestedCompanyId', 'actorType', 'actorId', 'eventType', 'phase',
  'outcome', 'reasonCode', 'severity', 'permissionCode', 'policyId', 'policyVersion', 'correlationId', 'causationId',
  'sourceChannel', 'aiToolId', 'aiToolVersion', 'aiRiskClass', 'aiDataClass', 'retainUntilUtc', 'occurrenceCount',
  'aggregationWindowStartUtc', 'aggregationWindowEndUtc', 'integrityHash',
] as const

export const securityEventsPath = (eventType: AiEventType, cursor: string | null) => {
  const query = new URLSearchParams({ eventType, pageSize: String(AI_EVENT_PAGE_SIZE) })
  if (cursor !== null) query.set('cursor', cursor)
  return `${SECURITY_EVENTS_PATH}?${query.toString()}`
}

/** One page of the requested AI event type in the selected company; any other type or company is malformed. */
export function decodeAiSecurityEventPage(raw: unknown, company: string, eventType: AiEventType): AiSecurityEventPage | null {
  if (!object(raw) || !exact(raw, EVENT_PAGE_KEYS) || !Array.isArray(raw.items) || !bounded(raw.pageSize, 1, 200)
    || raw.items.length > raw.pageSize || !optionalText(raw.nextCursor, 128)) return null
  const items: AiSecurityEvent[] = []
  for (const item of raw.items) {
    if (!object(item) || !exact(item, EVENT_KEYS) || !guid(item.id) || !instant(item.occurredAtUtc) || !sameCompany(item.companyId, company)
      || item.eventType !== eventType || !nonempty(item.outcome, 64) || !optionalText(item.reasonCode, 128)
      || !nonempty(item.severity, 64) || !nonempty(item.actorType, 64) || !optionalText(item.permissionCode, 128)
      || !nonempty(item.correlationId, 128) || !optionalText(item.aiToolId, 128) || !optionalText(item.aiToolVersion, 64)
      || !optionalText(item.aiRiskClass, 64) || !optionalText(item.aiDataClass, 64) || !bounded(item.occurrenceCount, 1, Number.MAX_SAFE_INTEGER)
      || items.some((existing) => existing.id === item.id)) return null
    items.push({
      id: item.id, occurredAtUtc: item.occurredAtUtc, eventType, outcome: item.outcome, reasonCode: item.reasonCode,
      severity: item.severity, actorType: item.actorType, permissionCode: item.permissionCode, correlationId: item.correlationId,
      aiToolId: item.aiToolId, aiToolVersion: item.aiToolVersion, aiRiskClass: item.aiRiskClass, aiDataClass: item.aiDataClass,
      occurrenceCount: item.occurrenceCount,
    })
  }
  return { items, nextCursor: raw.nextCursor }
}
