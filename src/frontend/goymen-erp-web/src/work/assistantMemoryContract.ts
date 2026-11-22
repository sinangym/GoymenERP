/**
 * WP-C6 (AI-D30) — strict decoders for the owner's own personal assistant memory. Every answer is `unknown` until it
 * decodes with EXACTLY the server keys (`WorkAssistantMemory*V1` records); anything else is `null` (fail closed). The
 * answer's company must be the active company and, for own memory, its owner must be the signed-in actor. The overseer
 * surface (`/work/assistant-memory/oversight/**`) has no screen here.
 */

export const ASSISTANT_MEMORY_ROUTE = '/asistan-hafizam'
export const ASSISTANT_MEMORY_STATUS_PATH = '/work/assistant-memory/mine/status'
export const ASSISTANT_MEMORY_ENTRIES_PATH = '/work/assistant-memory/mine/entries'
export const ASSISTANT_MEMORY_VIEWS_PATH = '/work/assistant-memory/mine/oversight-views'
export const assistantMemoryRevisionPath = (id: string) => `${ASSISTANT_MEMORY_ENTRIES_PATH}/${encodeURIComponent(id)}/revision`
export const assistantMemoryDeletionPath = (id: string) => `${ASSISTANT_MEMORY_ENTRIES_PATH}/${encodeURIComponent(id)}/deletion`

export const ASSISTANT_MEMORY_LIMITS = { body: 500, page: 100 } as const
export const ASSISTANT_MEMORY_KINDS = ['preference', 'note'] as const
export type AssistantMemoryKind = (typeof ASSISTANT_MEMORY_KINDS)[number]
export type AssistantMemoryState = 'active' | 'expired' | 'closed'
export const ASSISTANT_MEMORY_DISABLED_REASONS = ['pilot_off', 'retention_undecided', 'entry_limit_undecided', 'information_notice_pending', 'closed_for_user'] as const
export type AssistantMemoryDisabledReason = (typeof ASSISTANT_MEMORY_DISABLED_REASONS)[number]

export interface AssistantMemoryEntry {
  readonly id: string; readonly companyId: string; readonly ownerUserId: string; readonly kind: AssistantMemoryKind; readonly body: string
  readonly state: AssistantMemoryState; readonly version: string; readonly createdAtUtc: string; readonly updatedAtUtc: string
  readonly expiresAtUtc: string; readonly closedAtUtc: string | null; readonly closeReason: string | null
}
export interface AssistantMemoryEntryPage { readonly companyId: string; readonly ownerUserId: string; readonly items: readonly AssistantMemoryEntry[]; readonly hasMore: boolean }
export interface AssistantMemoryStatus {
  readonly companyId: string; readonly userId: string; readonly writable: boolean; readonly disabledReasons: readonly AssistantMemoryDisabledReason[]
  readonly pilotEnabled: boolean; readonly retentionDays: number | null; readonly maxEntriesPerUser: number | null
  readonly informationNoticeVersion: string | null; readonly activeEntryCount: number
}
export interface AssistantMemoryDeletion { readonly id: string; readonly version: string; readonly deletedAtUtc: string }
export interface AssistantMemoryView { readonly id: string; readonly viewerUserId: string; readonly viewedAtUtc: string; readonly entryCount: number }
export interface AssistantMemoryViewPage { readonly companyId: string; readonly subjectUserId: string; readonly items: readonly AssistantMemoryView[]; readonly hasMore: boolean }

export const ENTRY_FIELDS = ['id', 'companyId', 'ownerUserId', 'kind', 'body', 'state', 'version', 'createdAtUtc', 'updatedAtUtc', 'expiresAtUtc', 'closedAtUtc', 'closeReason'] as const
export const ENTRY_PAGE_FIELDS = ['companyId', 'ownerUserId', 'items', 'hasMore'] as const
export const STATUS_FIELDS = ['companyId', 'userId', 'writable', 'disabledReasons', 'pilotEnabled', 'retentionDays', 'maxEntriesPerUser', 'informationNoticeVersion', 'activeEntryCount'] as const
export const DELETION_FIELDS = ['id', 'version', 'deletedAtUtc'] as const
export const VIEW_FIELDS = ['id', 'viewerUserId', 'viewedAtUtc', 'entryCount'] as const
export const VIEW_PAGE_FIELDS = ['companyId', 'subjectUserId', 'items', 'hasMore'] as const

const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, names: readonly string[]) => Object.keys(value).length === names.length && names.every((name) => Object.hasOwn(value, name))
const guid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()
const instant = (value: unknown): value is string => typeof value === 'string' && value.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(value) && Number.isFinite(Date.parse(value))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= max
const version = (value: unknown): value is string => typeof value === 'string' && /^[1-9]\d{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n
const count = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0
const positiveOrNull = (value: unknown) => value === null || (typeof value === 'number' && Number.isInteger(value) && value > 0)
const kind = (value: unknown): value is AssistantMemoryKind => typeof value === 'string' && (ASSISTANT_MEMORY_KINDS as readonly string[]).includes(value)
const state = (value: unknown): value is AssistantMemoryState => value === 'active' || value === 'expired' || value === 'closed'
const reason = (value: unknown): value is AssistantMemoryDisabledReason => typeof value === 'string' && (ASSISTANT_MEMORY_DISABLED_REASONS as readonly string[]).includes(value)

export function decodeAssistantMemoryEntry(raw: unknown, companyId: string, ownerUserId: string, entryId?: string): AssistantMemoryEntry | null {
  if (!object(raw) || !exact(raw, ENTRY_FIELDS) || !guid(raw.id) || raw.companyId !== companyId || !guid(raw.ownerUserId)
    || !sameId(raw.ownerUserId, ownerUserId) || !kind(raw.kind) || !text(raw.body, ASSISTANT_MEMORY_LIMITS.body) || !state(raw.state)
    || !version(raw.version) || !instant(raw.createdAtUtc) || !instant(raw.updatedAtUtc) || !instant(raw.expiresAtUtc)) return null
  // A closed entry carries its closure time and reason; an active or expired one carries neither.
  const closed = raw.state === 'closed'
  if (closed ? !instant(raw.closedAtUtc) || !text(raw.closeReason, 500) : raw.closedAtUtc !== null || raw.closeReason !== null) return null
  if (entryId !== undefined && !sameId(raw.id, entryId)) return null
  return raw as unknown as AssistantMemoryEntry
}

export function decodeAssistantMemoryEntryPage(raw: unknown, companyId: string, ownerUserId: string): AssistantMemoryEntryPage | null {
  if (!object(raw) || !exact(raw, ENTRY_PAGE_FIELDS) || raw.companyId !== companyId || !guid(raw.ownerUserId) || !sameId(raw.ownerUserId, ownerUserId)
    || !Array.isArray(raw.items) || raw.items.length > ASSISTANT_MEMORY_LIMITS.page || typeof raw.hasMore !== 'boolean') return null
  const items = raw.items.map((item) => decodeAssistantMemoryEntry(item, companyId, ownerUserId))
  if (items.some((item) => item === null)) return null
  const decoded = items as AssistantMemoryEntry[]
  if (new Set(decoded.map((item) => item.id)).size !== decoded.length) return null
  return { companyId, ownerUserId: raw.ownerUserId, items: decoded, hasMore: raw.hasMore }
}

export function decodeAssistantMemoryStatus(raw: unknown, companyId: string, userId: string): AssistantMemoryStatus | null {
  if (!object(raw) || !exact(raw, STATUS_FIELDS) || raw.companyId !== companyId || !guid(raw.userId) || !sameId(raw.userId, userId)
    || typeof raw.writable !== 'boolean' || !Array.isArray(raw.disabledReasons) || !raw.disabledReasons.every(reason)
    || new Set(raw.disabledReasons).size !== raw.disabledReasons.length || typeof raw.pilotEnabled !== 'boolean'
    || !positiveOrNull(raw.retentionDays) || !positiveOrNull(raw.maxEntriesPerUser)
    || !(raw.informationNoticeVersion === null || text(raw.informationNoticeVersion, 200)) || !count(raw.activeEntryCount)) return null
  // Writable exactly when no reason closes it.
  if (raw.writable !== (raw.disabledReasons.length === 0)) return null
  return raw as unknown as AssistantMemoryStatus
}

export function decodeAssistantMemoryDeletion(raw: unknown, entryId: string): AssistantMemoryDeletion | null {
  if (!object(raw) || !exact(raw, DELETION_FIELDS) || !guid(raw.id) || !sameId(raw.id, entryId) || !version(raw.version) || !instant(raw.deletedAtUtc)) return null
  return raw as unknown as AssistantMemoryDeletion
}

export function decodeAssistantMemoryViewPage(raw: unknown, companyId: string, userId: string): AssistantMemoryViewPage | null {
  if (!object(raw) || !exact(raw, VIEW_PAGE_FIELDS) || raw.companyId !== companyId || !guid(raw.subjectUserId) || !sameId(raw.subjectUserId, userId)
    || !Array.isArray(raw.items) || raw.items.length > ASSISTANT_MEMORY_LIMITS.page || typeof raw.hasMore !== 'boolean') return null
  const valid = raw.items.every((item) => object(item) && exact(item, VIEW_FIELDS) && guid(item.id) && guid(item.viewerUserId)
    && instant(item.viewedAtUtc) && count(item.entryCount))
  if (!valid) return null
  return raw as unknown as AssistantMemoryViewPage
}
