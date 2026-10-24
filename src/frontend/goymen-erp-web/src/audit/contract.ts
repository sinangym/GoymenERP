/**
 * Audit event list wire contract — a hand-written runtime decoder for `GET /api/v1/audit/events`.
 *
 * Read off the backend (`AuditEndpoints`, `AuditModels.cs` `AuditEventPage` / `AuditEventItem`, OpenAPI
 * `GetAuditEventsV1`): `{ items, pageSize, nextCursor }`, newest first, keyset-paged by an opaque cursor.
 *
 * FAILS CLOSED. The page and every row must carry exactly the published keys, each of the published type; one bad
 * row invalidates the page (a page one row short cannot be told apart from a correct one). Every row must belong to
 * the company the request named — a foreign-company row is a fence breach, never something to filter out quietly.
 * Ids are unique on a page and the page never holds more rows than its own `pageSize`.
 *
 * DATA MINIMISATION (Sinan, #66/6383408216). The list shows only: when, what, which record, which operation, who,
 * which permission, which channel, AI involvement, outcome and correlation. The before/after payloads, reason, client
 * IP address, user agent and role codes are shown ONLY behind the per-row "Ayrıntılar" button, ONLY to the holder of the
 * details capability, and ONLY from their own endpoint (`decodeAuditEventDetails`). If the list answer still carries
 * those fields (the published v1 shape does), the list decoder type-checks them and DROPS them, so no list component
 * can render them; the shape without them (proposed v2, detail fields moved out) is accepted too. Detail keys are
 * all-or-none: a list row carrying some of them is malformed. Approval/AI context, causation and the integrity hash
 * are never shown.

 * Entity versions are int64 on the wire; they are kept as decimal strings (like every version the UI sends back),
 * and a value JSON could not carry exactly is malformed rather than rounded.
 */

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/

/** The server's page-size bounds (`AuditQueryService.NormalizePageSize`, OpenAPI `x-erp-normalized-*`). */
const PAGE_SIZE_MIN = 1
const PAGE_SIZE_MAX = 100
/** The server's cursor bound (`AuditEndpoints.IsTooLong(cursor, 128)`). */
const CURSOR_MAX_LENGTH = 128

const PAGE_KEYS = ['items', 'pageSize', 'nextCursor'] as const

/** The fields that belong to the details endpoint; on a list row they are all present (v1) or all absent (v2). */
const LIST_DETAIL_KEYS = ['roleCodes', 'beforeJson', 'afterJson', 'reason', 'clientIpAddress', 'userAgent'] as const

const ITEM_KEYS = [
  'id',
  'companyId',
  'occurredAtUtc',
  'eventType',
  'entityType',
  'entityId',
  'operation',
  'entityVersionBefore',
  'entityVersionAfter',
  'actorType',
  'actorId',
  'permissionCode',
  'roleCodes',
  'beforeJson',
  'afterJson',
  'reason',
  'approvalContextJson',
  'aiInvolvement',
  'aiContextJson',
  'sourceChannel',
  'clientIpAddress',
  'userAgent',
  'correlationId',
  'causationId',
  'outcome',
  'schemaVersion',
  'integrityHash',
] as const

export interface AuditEventRow {
  readonly id: string
  readonly occurredAtUtc: string
  readonly eventType: string
  readonly entityType: string
  readonly entityId: string
  readonly operation: string
  readonly entityVersionBefore: string | null
  readonly entityVersionAfter: string | null
  /** Raw server token (`User`, `System`, `Integration`, `Ai`, …); the text file labels the known ones. */
  readonly actorType: string
  readonly actorId: string
  readonly permissionCode: string
  readonly sourceChannel: string
  /** Raw server token (`None`, `Assisted`, `Proposed`, `Executed`, …). */
  readonly aiInvolvement: string
  /** Raw server token (`Succeeded`, …). */
  readonly outcome: string
  readonly correlationId: string
}

export interface AuditEventPage {
  readonly items: readonly AuditEventRow[]
  readonly pageSize: number
  readonly nextCursor: string | null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID.test(value)
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isString(value: unknown): value is string {
  return typeof value === 'string'
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function isInstant(value: unknown): value is string {
  return typeof value === 'string' && INSTANT.test(value) && !Number.isNaN(Date.parse(value))
}

/** `undefined` = malformed; `null` = absent on the wire; otherwise the exact decimal string. */
function decodeVersion(value: unknown): string | null | undefined {
  if (value === null) return null
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0) return undefined
  return String(value)
}

function decodeRoleCodes(value: unknown): boolean {
  return Array.isArray(value) && value.every(isNonEmptyString)
}

const ITEM_KEYS_WITHOUT_DETAILS: readonly string[] = ITEM_KEYS.filter(
  (key) => !(LIST_DETAIL_KEYS as readonly string[]).includes(key),
)

function decodeItem(raw: unknown, companyId: string): AuditEventRow | null {
  if (!isRecord(raw)) return null
  const carriesDetails = hasExactKeys(raw, ITEM_KEYS)
  if (!carriesDetails && !hasExactKeys(raw, ITEM_KEYS_WITHOUT_DETAILS)) return null
  const entityVersionBefore = decodeVersion(raw.entityVersionBefore)
  const entityVersionAfter = decodeVersion(raw.entityVersionAfter)
  const {
    id,
    occurredAtUtc,
    eventType,
    entityType,
    entityId,
    operation,
    actorType,
    actorId,
    permissionCode,
    sourceChannel,
    aiInvolvement,
    outcome,
    correlationId,
  } = raw
  if (
    !isUuid(id) ||
    !isUuid(raw.companyId) ||
    raw.companyId.toLowerCase() !== companyId.toLowerCase() ||
    !isInstant(occurredAtUtc) ||
    !isNonEmptyString(eventType) ||
    !isNonEmptyString(entityType) ||
    !isNonEmptyString(entityId) ||
    !isNonEmptyString(operation) ||
    entityVersionBefore === undefined ||
    entityVersionAfter === undefined ||
    !isNonEmptyString(actorType) ||
    !isNonEmptyString(actorId) ||
    !isNonEmptyString(permissionCode) ||
    !isNonEmptyString(sourceChannel) ||
    !isNonEmptyString(aiInvolvement) ||
    !isNonEmptyString(outcome) ||
    !isNonEmptyString(correlationId) ||
    // Checked, then dropped (see the file header).
    (carriesDetails &&
      (!decodeRoleCodes(raw.roleCodes) ||
        !isString(raw.beforeJson) ||
        !isString(raw.afterJson) ||
        !isString(raw.reason) ||
        !isNullableString(raw.clientIpAddress) ||
        !isNullableString(raw.userAgent))) ||
    !isNullableString(raw.approvalContextJson) ||
    !isNullableString(raw.aiContextJson) ||
    !isNullableString(raw.causationId) ||
    typeof raw.schemaVersion !== 'number' ||
    !Number.isSafeInteger(raw.schemaVersion) ||
    raw.schemaVersion < 1 ||
    !isNonEmptyString(raw.integrityHash)
  ) {
    return null
  }
  return {
    id,
    occurredAtUtc,
    eventType,
    entityType,
    entityId,
    operation,
    entityVersionBefore,
    entityVersionAfter,
    actorType,
    actorId,
    permissionCode,
    sourceChannel,
    aiInvolvement,
    outcome,
    correlationId,
  }
}

/** One page for `companyId`, the company the request named in `X-Company-Id`; `null` when anything is off. */
export function decodeAuditEventPage(raw: unknown, companyId: string): AuditEventPage | null {
  if (!isRecord(raw) || !hasExactKeys(raw, PAGE_KEYS) || !Array.isArray(raw.items)) return null
  const { pageSize, nextCursor } = raw
  if (
    typeof pageSize !== 'number' ||
    !Number.isSafeInteger(pageSize) ||
    pageSize < PAGE_SIZE_MIN ||
    pageSize > PAGE_SIZE_MAX ||
    raw.items.length > pageSize
  ) {
    return null
  }
  if (nextCursor !== null && (!isNonEmptyString(nextCursor) || nextCursor.length > CURSOR_MAX_LENGTH)) return null
  const items: AuditEventRow[] = []
  const seen = new Set<string>()
  for (const entry of raw.items) {
    const item = decodeItem(entry, companyId)
    if (item === null) return null
    const key = item.id.toLowerCase()
    if (seen.has(key)) return null
    seen.add(key)
    items.push(item)
  }
  // A next page with nothing on this one is not a shape the server produces (it only hands a cursor on overflow).
  if (items.length === 0 && nextCursor !== null) return null
  return { items, pageSize, nextCursor }
}

export interface AuditEventDetails {
  readonly id: string
  readonly beforeJson: string
  readonly afterJson: string
  readonly reason: string
  readonly clientIpAddress: string | null
  readonly userAgent: string | null
  readonly roleCodes: readonly string[]
}

const DETAIL_KEYS = ['id', 'beforeJson', 'afterJson', 'reason', 'clientIpAddress', 'userAgent', 'roleCodes'] as const

/**
 * PROPOSED `GET /api/v1/audit/events/{id}/details` (#66/6383408216; Codex owns the final shape). Must be the event the
 * button asked for; exact keys; nothing passed through.
 */
export function decodeAuditEventDetails(raw: unknown, requestedId: string): AuditEventDetails | null {
  if (!isRecord(raw) || !hasExactKeys(raw, DETAIL_KEYS)) return null
  const { id, beforeJson, afterJson, reason, clientIpAddress, userAgent, roleCodes } = raw
  if (
    !isUuid(id) ||
    id.toLowerCase() !== requestedId.toLowerCase() ||
    !isString(beforeJson) ||
    !isString(afterJson) ||
    !isString(reason) ||
    !isNullableString(clientIpAddress) ||
    !isNullableString(userAgent) ||
    !Array.isArray(roleCodes) ||
    !roleCodes.every(isNonEmptyString)
  ) {
    return null
  }
  return { id, beforeJson, afterJson, reason, clientIpAddress, userAgent, roleCodes: [...roleCodes] }
}
