/**
 * W1-A Work inbox — `GET /api/v1/work/inbox?view=awaiting-my-decision` (AI-D31, AI-D32, YOL-D18): the approvals waiting
 * for the signed-in actor's decision in the committed company, oldest first, each with the permission it waits on, its
 * duration and the owner's modelless explanation card (rule results and the records it rests on).
 *
 * The server already lists only kinds the actor may approve and never the actor's own records (maker ≠ checker). The
 * answer is judged whole: exact members, real GUIDs, UTC instants, owner-scoped kinds and rule codes, relative links,
 * invariant decimal text, a duration whose numbers exist only when it is `Defined`, and every item belonging to a source
 * listed as `Ready` with the same module, permission and duration class. Anything else is not this contract.
 *
 * `POST /api/v1/work/inbox/events` is Work's own measurement event (card opened / owner screen opened).
 */

import { isQueueKind, isQueueLink, isQueueModule } from './workQueueContract'

export const WORK_INBOX_PATH = '/work/inbox'
export const WORK_INBOX_EVENTS_PATH = '/work/inbox/events'
export const WORK_INBOX_VIEW_AWAITING_MY_DECISION = 'awaiting-my-decision'

export const INBOX_SOURCE_READY = 'Ready'
export const INBOX_SOURCE_UNAVAILABLE = 'Unavailable'
export type WorkInboxSourceState = typeof INBOX_SOURCE_READY | typeof INBOX_SOURCE_UNAVAILABLE

export const DURATION_CLASS_HOURS = 'Hours'
export const DURATION_CLASS_BUSINESS_DAYS = 'BusinessDays'
export type WorkInboxDurationClass = typeof DURATION_CLASS_HOURS | typeof DURATION_CLASS_BUSINESS_DAYS

export const DURATION_DEFINED = 'Defined'
export const DURATION_UNDEFINED = 'Undefined'
export const DURATION_UNAVAILABLE = 'Unavailable'

export const INBOX_EVENT_OPENED = 'Opened'
export const INBOX_EVENT_OWNER_SCREEN_OPENED = 'OwnerScreenOpened'
export type WorkInboxEventType = typeof INBOX_EVENT_OPENED | typeof INBOX_EVENT_OWNER_SCREEN_OPENED

/** The server's own bounds (`WorkInboxRulesV1`, `NativeApprovalQueueRulesV1`). */
export const INBOX_MAX_DOCUMENT_LABEL_LENGTH = 200
export const INBOX_MAX_RULE_CODE_LENGTH = 120
export const INBOX_MAX_RULES_PER_ITEM = 8
export const INBOX_MAX_REFERENCES_PER_ITEM = 12
export const INBOX_MAX_ROLE_LENGTH = 64
export const INBOX_MAX_REFERENCE_LABEL_LENGTH = 200
export const INBOX_MAX_DECIMAL_TEXT_LENGTH = 40
export const INBOX_MAX_PERMISSION_LENGTH = 128
export const INBOX_MAX_ITEMS_PER_SOURCE = 50
const INBOX_MAX_POLICY_VERSION_LENGTH = 200
const INT32_MAX = 2147483647

export type WorkInboxDuration =
  | {
    readonly class: WorkInboxDurationClass
    readonly state: typeof DURATION_DEFINED
    readonly limit: number
    readonly dueAtUtc: string
    readonly isOverdue: boolean
    readonly policyVersion: string
  }
  | {
    readonly class: WorkInboxDurationClass
    readonly state: typeof DURATION_UNDEFINED | typeof DURATION_UNAVAILABLE
    readonly limit: null
    readonly dueAtUtc: null
    readonly isOverdue: null
    readonly policyVersion: null
  }

/** One owner rule result; numbers are the owner's invariant decimal text, never computed here. */
export interface WorkInboxRule {
  readonly ruleCode: string
  readonly threshold: string | null
  readonly actual: string | null
  readonly deviation: string | null
  readonly unit: string | null
}

export interface WorkInboxReference {
  readonly role: string
  readonly label: string
  readonly recordId: string | null
  readonly link: string | null
}

export interface WorkInboxItem {
  readonly module: string
  readonly kind: string
  readonly itemId: string
  readonly documentLabel: string
  /** The owner's own screen, where the decision is given: a relative application path, already verified. */
  readonly link: string
  readonly waitingSinceUtc: string
  readonly awaitingPermission: string
  readonly duration: WorkInboxDuration
  readonly explanation: { readonly rules: readonly WorkInboxRule[]; readonly references: readonly WorkInboxReference[] }
}

export interface WorkInboxSource {
  readonly module: string
  readonly kind: string
  readonly awaitingPermission: string
  readonly durationClass: WorkInboxDurationClass
  readonly state: WorkInboxSourceState
  readonly hasMore: boolean
}

export interface WorkInbox {
  readonly companyId: string
  readonly view: typeof WORK_INBOX_VIEW_AWAITING_MY_DECISION
  readonly generatedAtUtc: string
  readonly items: readonly WorkInboxItem[]
  readonly sources: readonly WorkInboxSource[]
}

export interface WorkInboxEventBody {
  readonly clientEventId: string
  readonly kind: string
  readonly itemId: string
  readonly waitingSinceUtc: string
  readonly eventType: WorkInboxEventType
}

type Obj = Record<string, unknown>

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'
const UTC_INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|\+00:00)$/
const DOTTED_CODE = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*)+$/
const ROLE = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/
const DECIMAL = /^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/
const UNIT = /^(?:%|[A-Za-z]{1,16})$/

const ROOT_KEYS = ['companyId', 'view', 'generatedAtUtc', 'items', 'sources'] as const
const ITEM_KEYS = [
  'module', 'kind', 'itemId', 'documentLabel', 'link', 'waitingSinceUtc', 'awaitingPermission', 'duration', 'explanation',
] as const
const DURATION_KEYS = ['class', 'state', 'limit', 'dueAtUtc', 'isOverdue', 'policyVersion'] as const
const EXPLANATION_KEYS = ['rules', 'references'] as const
const RULE_KEYS = ['ruleCode', 'threshold', 'actual', 'deviation', 'unit'] as const
const REFERENCE_KEYS = ['role', 'label', 'recordId', 'link'] as const
const SOURCE_KEYS = ['module', 'kind', 'awaitingPermission', 'durationClass', 'state', 'hasMore'] as const
const EVENT_KEYS = ['clientEventId', 'replayed'] as const

const isObject = (value: unknown): value is Obj => typeof value === 'object' && value !== null && !Array.isArray(value)

function exact(value: Obj, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}

export const isInboxId = (value: unknown): value is string =>
  typeof value === 'string' && GUID.test(value) && value !== EMPTY_GUID
const isUtcInstant = (value: unknown): value is string =>
  typeof value === 'string' && UTC_INSTANT.test(value) && !Number.isNaN(Date.parse(value))
const isLabel = (value: unknown, max: number): value is string =>
  typeof value === 'string' && value.trim().length > 0 && value.length <= max
export const isInboxPermission = (value: unknown): value is string =>
  typeof value === 'string' && value.length <= INBOX_MAX_PERMISSION_LENGTH && DOTTED_CODE.test(value)
const isDecimalText = (value: unknown): value is string | null =>
  value === null || (typeof value === 'string' && value.length <= INBOX_MAX_DECIMAL_TEXT_LENGTH && DECIMAL.test(value))
const isDurationClass = (value: unknown): value is WorkInboxDurationClass =>
  value === DURATION_CLASS_HOURS || value === DURATION_CLASS_BUSINESS_DAYS

/** A rule code is `{module}.{rule}`, owned by the item's module. Its words are the screen's; an unknown code is still valid. */
const isRuleCode = (module: string, value: unknown): value is string =>
  typeof value === 'string'
  && value.length <= INBOX_MAX_RULE_CODE_LENGTH
  && DOTTED_CODE.test(value)
  && value.startsWith(`${module}.`)

function decodeDuration(value: unknown): WorkInboxDuration | null {
  if (!isObject(value) || !exact(value, DURATION_KEYS)) return null
  const { class: durationClass, state, limit, dueAtUtc, isOverdue, policyVersion } = value
  if (!isDurationClass(durationClass)) return null
  if (state === DURATION_DEFINED) {
    if (typeof limit !== 'number' || !Number.isInteger(limit) || limit < 1 || limit > INT32_MAX) return null
    if (!isUtcInstant(dueAtUtc) || typeof isOverdue !== 'boolean') return null
    if (!isLabel(policyVersion, INBOX_MAX_POLICY_VERSION_LENGTH)) return null
    return { class: durationClass, state, limit, dueAtUtc, isOverdue, policyVersion }
  }
  if (state !== DURATION_UNDEFINED && state !== DURATION_UNAVAILABLE) return null
  // Only a defined duration carries numbers: anything else claims nothing.
  if (limit !== null || dueAtUtc !== null || isOverdue !== null || policyVersion !== null) return null
  return { class: durationClass, state, limit, dueAtUtc, isOverdue, policyVersion }
}

function decodeRule(module: string, value: unknown): WorkInboxRule | null {
  if (!isObject(value) || !exact(value, RULE_KEYS)) return null
  const { ruleCode, threshold, actual, deviation, unit } = value
  if (!isRuleCode(module, ruleCode)) return null
  if (!isDecimalText(threshold) || !isDecimalText(actual) || !isDecimalText(deviation)) return null
  if (unit !== null && (typeof unit !== 'string' || !UNIT.test(unit))) return null
  // A unit belongs to a number; a rule without numbers has none.
  if (unit !== null && threshold === null && actual === null && deviation === null) return null
  return { ruleCode, threshold, actual, deviation, unit: unit as string | null }
}

function decodeReference(value: unknown): WorkInboxReference | null {
  if (!isObject(value) || !exact(value, REFERENCE_KEYS)) return null
  const { role, label, recordId, link } = value
  if (typeof role !== 'string' || role.length > INBOX_MAX_ROLE_LENGTH || !ROLE.test(role)) return null
  if (!isLabel(label, INBOX_MAX_REFERENCE_LABEL_LENGTH)) return null
  if (recordId !== null && !isInboxId(recordId)) return null
  if (link !== null && !isQueueLink(link)) return null
  return { role, label, recordId: recordId === null ? null : recordId.toLowerCase(), link }
}

function decodeItem(value: unknown): WorkInboxItem | null {
  if (!isObject(value) || !exact(value, ITEM_KEYS)) return null
  const { module, kind, itemId, documentLabel, link, waitingSinceUtc, awaitingPermission, duration, explanation } = value
  if (!isQueueKind(module, kind) || !isInboxId(itemId) || !isQueueLink(link) || !isUtcInstant(waitingSinceUtc)) return null
  if (!isLabel(documentLabel, INBOX_MAX_DOCUMENT_LABEL_LENGTH) || !isInboxPermission(awaitingPermission)) return null
  const decodedDuration = decodeDuration(duration)
  if (decodedDuration === null) return null
  if (!isObject(explanation) || !exact(explanation, EXPLANATION_KEYS)) return null
  const { rules, references } = explanation
  if (!Array.isArray(rules) || rules.length < 1 || rules.length > INBOX_MAX_RULES_PER_ITEM) return null
  if (!Array.isArray(references) || references.length > INBOX_MAX_REFERENCES_PER_ITEM) return null
  const decodedRules: WorkInboxRule[] = []
  for (const raw of rules) {
    const rule = decodeRule(module as string, raw)
    if (rule === null) return null
    decodedRules.push(rule)
  }
  const decodedReferences: WorkInboxReference[] = []
  for (const raw of references) {
    const reference = decodeReference(raw)
    if (reference === null) return null
    decodedReferences.push(reference)
  }
  return {
    module: module as string,
    kind,
    itemId: itemId.toLowerCase(),
    documentLabel,
    link,
    waitingSinceUtc,
    awaitingPermission,
    duration: decodedDuration,
    explanation: { rules: decodedRules, references: decodedReferences },
  }
}

function decodeSource(value: unknown): WorkInboxSource | null {
  if (!isObject(value) || !exact(value, SOURCE_KEYS)) return null
  const { module, kind, awaitingPermission, durationClass, state, hasMore } = value
  if (!isQueueModule(module) || !isQueueKind(module, kind) || !isInboxPermission(awaitingPermission)) return null
  if (!isDurationClass(durationClass) || typeof hasMore !== 'boolean') return null
  if (state === INBOX_SOURCE_READY) return { module, kind, awaitingPermission, durationClass, state, hasMore }
  // An unavailable source shows nothing and claims nothing more.
  if (state === INBOX_SOURCE_UNAVAILABLE && !hasMore) return { module, kind, awaitingPermission, durationClass, state, hasMore }
  return null
}

/** The whole inbox of `companyId`, or null. */
export function decodeWorkInbox(body: unknown, companyId: string): WorkInbox | null {
  if (!isObject(body) || !exact(body, ROOT_KEYS)) return null
  const { companyId: answeredCompany, view, generatedAtUtc, items, sources } = body
  if (!isInboxId(answeredCompany) || answeredCompany.toLowerCase() !== companyId.toLowerCase()) return null
  if (view !== WORK_INBOX_VIEW_AWAITING_MY_DECISION || !isUtcInstant(generatedAtUtc)) return null
  if (!Array.isArray(items) || !Array.isArray(sources)) return null

  const sourceByKind = new Map<string, WorkInboxSource>()
  for (const raw of sources) {
    const source = decodeSource(raw)
    if (source === null || sourceByKind.has(source.kind)) return null
    sourceByKind.set(source.kind, source)
  }

  const decoded: WorkInboxItem[] = []
  const seen = new Set<string>()
  const perKind = new Map<string, number>()
  for (const raw of items) {
    const item = decodeItem(raw)
    if (item === null) return null
    const source = sourceByKind.get(item.kind)
    if (source === undefined || source.state !== INBOX_SOURCE_READY || source.module !== item.module) return null
    if (source.awaitingPermission !== item.awaitingPermission || source.durationClass !== item.duration.class) return null
    const key = `${item.kind}::${item.itemId}`
    if (seen.has(key)) return null
    seen.add(key)
    const count = (perKind.get(item.kind) ?? 0) + 1
    if (count > INBOX_MAX_ITEMS_PER_SOURCE) return null
    perKind.set(item.kind, count)
    decoded.push(item)
  }

  return {
    companyId: answeredCompany.toLowerCase(),
    view,
    generatedAtUtc,
    items: decoded,
    sources: [...sourceByKind.values()],
  }
}

/** The event answer echoes the event that was sent. */
export function decodeWorkInboxEventRecorded(body: unknown, clientEventId: string): { readonly replayed: boolean } | null {
  if (!isObject(body) || !exact(body, EVENT_KEYS)) return null
  if (!isInboxId(body.clientEventId) || body.clientEventId.toLowerCase() !== clientEventId.toLowerCase()) return null
  return typeof body.replayed === 'boolean' ? { replayed: body.replayed } : null
}
