/**
 * API-98/B-14 wire contract (ALS-D79, BS-10; KIM-D49, KIM-D51) — the paths, request bodies and strict runtime decoder of
 * the company's reference list routes (`SettingsReferenceListEndpoints`, `SettingsReferenceListsV1.cs`):
 *
 *   GET  /settings/reference-lists/{list}                             -> SettingsReferenceListViewV1
 *   POST /settings/reference-lists/{list}/entries                     -> 204
 *   POST /settings/reference-lists/{list}/entries/{entryId}/versions  -> 204
 *
 * Only the expense type, service type, paint color and scrap kind lists are open; the operation type list has no route.
 * Every list starts empty: nothing is seeded and the screen suggests no entry. The decoder fails CLOSED: keys must match the
 * contract exactly, the list kind must be the requested one, one bad entry or a repeated id or code invalidates the answer.
 */

import { isUuid } from './discountLimitContract'

export const REFERENCE_LISTS_PATH = '/settings/reference-lists'

/** The open lists: the server's stored kind (`SettingsReferenceListKindsV1.*Code`) and its route slug. */
export const REFERENCE_LIST = {
  expenseType: { kind: 'ExpenseType', slug: 'expense-types' },
  serviceType: { kind: 'ServiceType', slug: 'service-types' },
  paintColor: { kind: 'PaintColor', slug: 'paint-colors' },
  scrapKind: { kind: 'ScrapKind', slug: 'scrap-kinds' },
} as const
export type ReferenceListId = keyof typeof REFERENCE_LIST
export type ReferenceListKind = (typeof REFERENCE_LIST)[ReferenceListId]['kind']

export function referenceListPath(list: ReferenceListId): string {
  return `${REFERENCE_LISTS_PATH}/${REFERENCE_LIST[list].slug}`
}

export function referenceListEntriesPath(list: ReferenceListId): string {
  return `${referenceListPath(list)}/entries`
}

export function referenceListEntryVersionsPath(list: ReferenceListId, entryId: string): string {
  return `${referenceListEntriesPath(list)}/${encodeURIComponent(entryId)}/versions`
}

/** The stable problem codes the screen distinguishes (`SettingsReferenceListProblemCodesV1`); others are a generic refusal. */
export const REFERENCE_LIST_PROBLEM = {
  requestInvalid: 'settings_reference_list_request_invalid',
  commandMetadataInvalid: 'settings_reference_list_command_metadata_invalid',
  reasonRequired: 'settings_reference_list_reason_required',
  reasonTooLong: 'settings_reference_list_reason_too_long',
  codeConflict: 'settings_reference_list_code_conflict',
  entryNotFound: 'settings_reference_list_entry_not_found',
  idempotencyConflict: 'settings_reference_list_idempotency_conflict',
  versionConflict: 'settings_reference_list_version_conflict',
  providerUnavailable: 'settings_provider_unavailable',
  profileInvalid: 'settings_profile_invalid',
  accessDenied: 'settings_access_denied',
} as const

/** Refusals after which the screen's copy is stale: the form closes and the list is read again. */
export const REFERENCE_LIST_STALE_CODES: readonly string[] = [
  REFERENCE_LIST_PROBLEM.versionConflict,
  REFERENCE_LIST_PROBLEM.entryNotFound,
]

/** Technical bounds mirrored from `SettingsReferenceListInputLimitsV1` (storage shape, not business policy). */
export const REFERENCE_LIST_INPUT = {
  reasonMaximumLength: 4000,
  codeMaximumLength: 32,
  nameMaximumLength: 200,
} as const

export interface ReferenceListEntry {
  readonly id: string
  readonly code: string
  readonly name: string
  readonly active: boolean
  /** The latest stored version: the next version's expected version. */
  readonly version: string
  readonly recordedBy: string
  readonly recordedAtUtc: string
}

export interface ReferenceListView {
  readonly kind: ReferenceListKind
  readonly entries: readonly ReferenceListEntry[]
}

export interface CreateReferenceListEntryBody {
  readonly code: string
  readonly name: string
  readonly reason: string
}

export interface RecordReferenceListEntryBody {
  readonly expectedVersion: string
  readonly name: string
  readonly active: boolean
  readonly reason: string
}

const CODE = new RegExp(`^[A-Z0-9][A-Z0-9._-]{0,${REFERENCE_LIST_INPUT.codeMaximumLength - 1}}$`)
const POSITIVE_VERSION = /^[1-9][0-9]*$/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64
// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/

const ENTRY_FIELDS = ['id', 'code', 'name', 'active', 'version', 'recordedBy', 'recordedAtUtc'] as const
const VIEW_FIELDS = ['kind', 'entries'] as const

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, fields: readonly string[]): boolean {
  const keys = Object.keys(value)
  return keys.length === fields.length && fields.every((field) => Object.prototype.hasOwnProperty.call(value, field))
}

function isGuid(value: unknown): value is string {
  return isUuid(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value)
}

function isInstant(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length <= INSTANT_MAXIMUM_LENGTH &&
    INSTANT.test(value) &&
    !Number.isNaN(Date.parse(value))
  )
}

/** A code exactly as the server accepts it (`ReferenceListRulesV1.IsCode`): no trimming, no case folding. */
export function isReferenceListCode(value: unknown): value is string {
  return typeof value === 'string' && CODE.test(value)
}

/** A display name as the server stores it: trimmed, 1…200 characters, no control characters. */
export function normalizeReferenceListName(text: string): string | null {
  const trimmed = text.trim()
  if (trimmed.length === 0 || trimmed.length > REFERENCE_LIST_INPUT.nameMaximumLength || CONTROL.test(trimmed)) {
    return null
  }
  return trimmed
}

function decodeEntry(raw: unknown): ReferenceListEntry | null {
  if (!isRecord(raw) || !hasExactKeys(raw, ENTRY_FIELDS)) return null
  const { id, code, name, active, version, recordedBy, recordedAtUtc } = raw
  if (!isGuid(id) || !isReferenceListCode(code)) return null
  if (typeof name !== 'string' || normalizeReferenceListName(name) !== name) return null
  if (typeof active !== 'boolean' || typeof version !== 'string' || !POSITIVE_VERSION.test(version)) return null
  if (typeof recordedBy !== 'string' || recordedBy.trim().length === 0 || !isInstant(recordedAtUtc)) return null
  return { id: id.toLowerCase(), code, name, active, version, recordedBy, recordedAtUtc }
}

/** One list's view. Another list's kind, a repeated id or code, or one bad entry fails the whole answer. */
export function decodeReferenceListView(list: ReferenceListId, raw: unknown): ReferenceListView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEW_FIELDS)) return null
  const { kind, entries } = raw
  if (kind !== REFERENCE_LIST[list].kind || !Array.isArray(entries)) return null
  const decoded: ReferenceListEntry[] = []
  const ids = new Set<string>()
  const codes = new Set<string>()
  for (const item of entries) {
    const entry = decodeEntry(item)
    if (entry === null || ids.has(entry.id) || codes.has(entry.code)) return null
    ids.add(entry.id)
    codes.add(entry.code)
    decoded.push(entry)
  }
  return { kind: REFERENCE_LIST[list].kind, entries: decoded }
}
