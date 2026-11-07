/**
 * Audit event page wire contract — runtime decoder (UI-K02, read-only).
 *
 * Read off the owner's endpoint and OpenAPI (`GET /api/v1/audit/events`, `AuditEventPage` / `AuditEventItem` in
 * `contracts/openapi/goymen-erp-v1.json`). The decoder fails CLOSED: the key set of the page and of every item must
 * equal the schema exactly, one bad item invalidates the whole page, and an item of another company than the active
 * one is not a record of this view — the page is malformed. It returns the typed page or `null`, never a partial one.
 *
 * `clientIpAddress` and `userAgent` are decoded only to keep the key check exact; they are dropped from the decoded
 * item and never reach the screen (Decision No 135 privacy, docs/decisions/SINAN_KARARLARI_2026-10-21.md).
 */

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const EMPTY_UUID = '00000000-0000-0000-0000-000000000000'
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/

export const AUDIT_PAGE_FIELDS = ['items', 'pageSize', 'nextCursor'] as const

export const AUDIT_ITEM_FIELDS = [
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

/** One audit event as the screen may use it. Client IP and user agent are deliberately absent. */
export interface AuditEvent {
  readonly id: string
  readonly companyId: string
  readonly occurredAtUtc: string
  readonly eventType: string
  readonly entityType: string
  readonly entityId: string
  readonly operation: string
  readonly entityVersionBefore: number | null
  readonly entityVersionAfter: number | null
  readonly actorType: string
  readonly actorId: string
  readonly permissionCode: string
  readonly roleCodes: readonly string[]
  readonly beforeJson: string
  readonly afterJson: string
  readonly reason: string
  readonly approvalContextJson: string | null
  readonly aiInvolvement: string
  readonly aiContextJson: string | null
  readonly sourceChannel: string
  readonly correlationId: string
  readonly causationId: string | null
  readonly outcome: string
  readonly schemaVersion: number
  readonly integrityHash: string
}

export interface AuditEventPage {
  readonly items: readonly AuditEvent[]
  readonly pageSize: number
  readonly nextCursor: string | null
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, fields: readonly string[]): boolean {
  const keys = Object.keys(value)
  return keys.length === fields.length && fields.every((field) => Object.prototype.hasOwnProperty.call(value, field))
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && UUID.test(value) && value !== EMPTY_UUID
}

function isString(value: unknown): value is string {
  return typeof value === 'string'
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function isInstant(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && INSTANT.test(value) && !Number.isNaN(Date.parse(value))
}

/** An entity version is a non-negative int64 on the wire; beyond the safe integer range it is not trusted. */
function isNullableVersion(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isSafeInteger(value) && value >= 0)
}

function isInt32(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= -2147483648 && value <= 2147483647
}

function decodeItem(value: unknown, companyId: string): AuditEvent | null {
  if (!isRecord(value) || !hasExactKeys(value, AUDIT_ITEM_FIELDS)) return null
  const v = value
  if (!isGuid(v.id) || !isGuid(v.companyId) || v.companyId.toLowerCase() !== companyId.toLowerCase()) return null
  if (!isInstant(v.occurredAtUtc)) return null
  if (
    !isString(v.eventType) ||
    !isString(v.entityType) ||
    !isString(v.entityId) ||
    !isString(v.operation) ||
    !isString(v.actorType) ||
    !isString(v.actorId) ||
    !isString(v.permissionCode) ||
    !isString(v.beforeJson) ||
    !isString(v.afterJson) ||
    !isString(v.reason) ||
    !isString(v.aiInvolvement) ||
    !isString(v.sourceChannel) ||
    !isString(v.correlationId) ||
    !isString(v.outcome) ||
    !isString(v.integrityHash)
  ) {
    return null
  }
  if (!isNullableVersion(v.entityVersionBefore) || !isNullableVersion(v.entityVersionAfter)) return null
  if (!Array.isArray(v.roleCodes) || !v.roleCodes.every(isString)) return null
  if (
    !isNullableString(v.approvalContextJson) ||
    !isNullableString(v.aiContextJson) ||
    !isNullableString(v.clientIpAddress) ||
    !isNullableString(v.userAgent) ||
    !isNullableString(v.causationId)
  ) {
    return null
  }
  if (!isInt32(v.schemaVersion)) return null
  return {
    id: v.id,
    companyId: v.companyId,
    occurredAtUtc: v.occurredAtUtc,
    eventType: v.eventType,
    entityType: v.entityType,
    entityId: v.entityId,
    operation: v.operation,
    entityVersionBefore: v.entityVersionBefore,
    entityVersionAfter: v.entityVersionAfter,
    actorType: v.actorType,
    actorId: v.actorId,
    permissionCode: v.permissionCode,
    roleCodes: [...v.roleCodes],
    beforeJson: v.beforeJson,
    afterJson: v.afterJson,
    reason: v.reason,
    approvalContextJson: v.approvalContextJson,
    aiInvolvement: v.aiInvolvement,
    aiContextJson: v.aiContextJson,
    sourceChannel: v.sourceChannel,
    correlationId: v.correlationId,
    causationId: v.causationId,
    outcome: v.outcome,
    schemaVersion: v.schemaVersion,
    integrityHash: v.integrityHash,
  }
}

/**
 * Decodes one page for the active company. `null` for any deviation: a missing or extra key, a wrong type, a page
 * size outside the server's 1…100, more items than the page size, a duplicate id inside the page, an empty cursor
 * string, or an item of another company.
 */
export function decodeAuditEventPage(value: unknown, companyId: string): AuditEventPage | null {
  if (!isRecord(value) || !hasExactKeys(value, AUDIT_PAGE_FIELDS)) return null
  if (!isInt32(value.pageSize) || value.pageSize < 1 || value.pageSize > 100) return null
  if (!(value.nextCursor === null || (typeof value.nextCursor === 'string' && value.nextCursor.length > 0))) return null
  if (!Array.isArray(value.items) || value.items.length > value.pageSize) return null
  const items: AuditEvent[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    const item = decodeItem(raw, companyId)
    if (item === null) return null
    const id = item.id.toLowerCase()
    if (seen.has(id)) return null
    seen.add(id)
    items.push(item)
  }
  return { items, pageSize: value.pageSize, nextCursor: value.nextCursor }
}
