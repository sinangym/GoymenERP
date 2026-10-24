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
 * DATA MINIMISATION (ASSUMPTION-AUDIT-VIEW-FIELDS, NEEDS-SINAN for the rest). The decoder CHECKS every field but
 * KEEPS only what the list shows: when, what, which record, which operation, who, which permission, which channel,
 * AI involvement, outcome and correlation. The before/after payloads, the free-text reason, approval and AI context,
 * client IP address, user agent, role codes, causation id and integrity hash are type-checked and then dropped, so
 * no component can render them by accident. Showing any of them is a retention/privacy decision, not a UI choice.
 *
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

function decodeItem(raw: unknown, companyId: string): AuditEventRow | null {
  if (!isRecord(raw) || !hasExactKeys(raw, ITEM_KEYS)) return null
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
    !decodeRoleCodes(raw.roleCodes) ||
    !isString(raw.beforeJson) ||
    !isString(raw.afterJson) ||
    !isString(raw.reason) ||
    !isNullableString(raw.approvalContextJson) ||
    !isNullableString(raw.aiContextJson) ||
    !isNullableString(raw.clientIpAddress) ||
    !isNullableString(raw.userAgent) ||
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
