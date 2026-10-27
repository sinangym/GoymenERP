/**
 * SAT-D61…D70 wire contract — the paths, request bodies and strict runtime decoders of the Settings discount limit routes
 * (`SettingsEndpoints`, `SalesDiscountLimitGroupsV1.cs`; pinned to `contracts/openapi/goymen-erp-v1.json` by
 * ./openapiContract.test.ts):
 *
 *   GET  /settings/sales-discount-limits/groups                      -> SalesDiscountLimitGroupListV1
 *   POST /settings/sales-discount-limits/groups                      -> 201 SalesDiscountLimitGroupCreatedV1
 *   PUT  /settings/sales-discount-limits/groups/{groupId}/name       -> 204
 *   POST /settings/sales-discount-limits/groups/{groupId}/deactivation -> 204
 *   PUT  /settings/sales-discount-limits/groups/{groupId}/limit      -> 204
 *   GET  /settings/sales-discount-limits/people?q=&limit=            -> SalesDiscountLimitPersonListV1
 *   GET  /settings/sales-discount-limits/bindings/{userId}           -> SalesDiscountLimitBindingViewV1
 *   PUT  /settings/sales-discount-limits/bindings/{userId}           -> 204
 *
 * Every decoder fails CLOSED: one bad row invalidates the whole answer (a list one group short cannot be told apart from
 * a correct one), unknown kinds and states are rejected rather than guessed, and versions and percents stay the lossless
 * decimal TEXT the server sends — this screen never computes a limit (the backend calculator does, SAT-D62).
 */

export const DISCOUNT_LIMITS_PATH = '/settings/sales-discount-limits'
export const DISCOUNT_LIMIT_GROUPS_PATH = `${DISCOUNT_LIMITS_PATH}/groups`
export const DISCOUNT_LIMIT_PEOPLE_PATH = `${DISCOUNT_LIMITS_PATH}/people`

export function groupNamePath(groupId: string): string {
  return `${DISCOUNT_LIMIT_GROUPS_PATH}/${encodeURIComponent(groupId)}/name`
}

export function groupDeactivationPath(groupId: string): string {
  return `${DISCOUNT_LIMIT_GROUPS_PATH}/${encodeURIComponent(groupId)}/deactivation`
}

export function groupLimitPath(groupId: string): string {
  return `${DISCOUNT_LIMIT_GROUPS_PATH}/${encodeURIComponent(groupId)}/limit`
}

export function bindingPath(userId: string): string {
  return `${DISCOUNT_LIMITS_PATH}/bindings/${encodeURIComponent(userId)}`
}

/** The stable problem codes the screen distinguishes; anything else is shown as a generic refusal. */
export const LIMIT_PROBLEM = {
  requestInvalid: 'settings_discount_limit_request_invalid',
  commandMetadataInvalid: 'settings_discount_limit_command_metadata_invalid',
  reasonRequired: 'settings_discount_limit_reason_required',
  reasonTooLong: 'settings_discount_limit_reason_too_long',
  idempotencyConflict: 'settings_discount_limit_idempotency_conflict',
  versionConflict: 'settings_discount_limit_version_conflict',
  groupCodeTaken: 'settings_discount_limit_group_code_taken',
  groupNameTaken: 'settings_discount_limit_group_name_taken',
  groupInactive: 'settings_discount_limit_group_inactive',
  linkedGroupNotFound: 'settings_discount_limit_linked_group_not_found',
  linkedGroupInactive: 'settings_discount_limit_linked_group_inactive',
  linkCycle: 'settings_discount_limit_link_cycle',
  bindingCompaniesMismatch: 'settings_discount_limit_binding_companies_mismatch',
  bindingPersonNotMember: 'settings_discount_limit_binding_person_not_member',
  bindingGroupNotFound: 'settings_discount_limit_binding_group_not_found',
  bindingGroupInactive: 'settings_discount_limit_binding_group_inactive',
  providerUnavailable: 'settings_provider_unavailable',
} as const

export type LimitProblemCode = (typeof LIMIT_PROBLEM)[keyof typeof LIMIT_PROBLEM]

export const LIMIT_KIND = { fixed: 'Fixed', linked: 'Linked' } as const
export type LimitKind = (typeof LIMIT_KIND)[keyof typeof LIMIT_KIND]

/** Technical bounds mirrored from `SalesDiscountLimitInputLimitsV1` (transport shape, not business policy). */
export const LIMIT_INPUT = {
  codeMaximumLength: 32,
  nameMaximumLength: 100,
  reasonMaximumLength: 4000,
  percentMaximumScale: 4,
  peopleSearchLimit: 25,
  peopleQueryMaximumLength: 100,
} as const

export interface LimitDefinition {
  readonly limitVersion: string
  readonly kind: LimitKind
  readonly percent: string | null
  readonly linkedGroupId: string | null
  readonly extraPoints: string | null
  readonly effectiveFrom: string
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface EffectiveLimit {
  readonly state: 'Defined' | 'None'
  readonly percent: string | null
}

export interface LimitGroup {
  readonly id: string
  readonly code: string
  readonly name: string
  readonly isActive: boolean
  readonly version: string
  readonly limit: LimitDefinition | null
  readonly effectiveLimit: EffectiveLimit
}

export interface LimitGroupList {
  readonly companyId: string
  readonly businessDate: string
  readonly groups: readonly LimitGroup[]
}

export interface LimitGroupCreated {
  readonly id: string
  readonly version: string
}

export interface Person {
  readonly userId: string
  readonly displayName: string
}

export interface PersonList {
  readonly items: readonly Person[]
  readonly hasMore: boolean
}

export interface BoundGroup {
  readonly id: string
  readonly code: string
  readonly name: string
  readonly isActive: boolean
}

export interface BindingCompany {
  readonly companyId: string
  readonly version: string
  readonly groups: readonly BoundGroup[]
}

export interface Binding {
  readonly userId: string
  readonly companies: readonly BindingCompany[]
}

export interface CreateGroupBody {
  readonly code: string
  readonly name: string
  readonly reason: string
}

export interface RenameGroupBody {
  readonly expectedVersion: string
  readonly name: string
  readonly reason: string
}

export interface DeactivateGroupBody {
  readonly expectedVersion: string
  readonly reason: string
}

export interface SetLimitBody {
  readonly expectedVersion: string
  readonly kind: LimitKind
  readonly percent: string | null
  readonly linkedGroupId: string | null
  readonly extraPoints: string | null
  readonly reason: string
}

export interface SetBindingBody {
  readonly groupCodes: readonly string[]
  readonly companies: readonly { readonly companyId: string; readonly expectedVersion: string }[]
  readonly reason: string
}

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const VERSION = /^(0|[1-9][0-9]*)$/
const POSITIVE_VERSION = /^[1-9][0-9]*$/
const PERCENT_TEXT = /^[0-9]{1,3}(\.[0-9]{1,4})?$/
const CODE = /^[A-Z0-9][A-Z0-9_-]{0,31}$/
const DATE = /^\d{4}-\d{2}-\d{2}$/

export function isUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID.test(value)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isInstant(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/.test(value) &&
    !Number.isNaN(Date.parse(value))
  )
}

/** A percent as the server writes it: 0…100, at most four decimals, no sign, no exponent. */
export function isPercentText(value: unknown): value is string {
  return typeof value === 'string' && PERCENT_TEXT.test(value) && Number(value) >= 0 && Number(value) <= 100
}

/** The canonical code the server stores: trimmed and upper-cased (the server normalizes the same way). */
export function normalizeCode(value: string): string {
  return value.trim().toLocaleUpperCase('en-US')
}

export function isValidCode(value: string): boolean {
  return CODE.test(value)
}

function decodeDefinition(raw: unknown): LimitDefinition | null {
  if (!isRecord(raw)) return null
  const { limitVersion, kind, percent, linkedGroupId, extraPoints, effectiveFrom, recordedBy, recordedAtUtc, reason } = raw
  if (typeof limitVersion !== 'string' || !POSITIVE_VERSION.test(limitVersion)) return null
  if (typeof effectiveFrom !== 'string' || !DATE.test(effectiveFrom)) return null
  if (!isNonEmpty(recordedBy) || !isInstant(recordedAtUtc) || !isNonEmpty(reason)) return null
  if (kind === LIMIT_KIND.fixed) {
    if (!isPercentText(percent) || linkedGroupId !== null || extraPoints !== null) return null
    return { limitVersion, kind, percent, linkedGroupId: null, extraPoints: null, effectiveFrom, recordedBy, recordedAtUtc, reason }
  }
  if (kind === LIMIT_KIND.linked) {
    if (percent !== null || !isUuid(linkedGroupId) || !isPercentText(extraPoints)) return null
    return { limitVersion, kind, percent: null, linkedGroupId, extraPoints, effectiveFrom, recordedBy, recordedAtUtc, reason }
  }
  return null
}

function decodeEffective(raw: unknown): EffectiveLimit | null {
  if (!isRecord(raw)) return null
  if (raw.state === 'Defined' && isPercentText(raw.percent)) return { state: 'Defined', percent: raw.percent }
  if (raw.state === 'None' && raw.percent === null) return { state: 'None', percent: null }
  return null
}

function decodeGroup(raw: unknown): LimitGroup | null {
  if (!isRecord(raw)) return null
  const { id, code, name, isActive, version, limit, effectiveLimit } = raw
  if (!isUuid(id) || typeof code !== 'string' || !isValidCode(code) || !isNonEmpty(name)) return null
  if (typeof isActive !== 'boolean' || typeof version !== 'string' || !POSITIVE_VERSION.test(version)) return null
  const definition = limit === null ? null : decodeDefinition(limit)
  if (limit !== null && definition === null) return null
  const effective = decodeEffective(effectiveLimit)
  if (effective === null) return null
  return { id, code, name, isActive, version, limit: definition, effectiveLimit: effective }
}

/** The group list of the company the request named; a list for another company is not this answer. */
export function decodeGroupList(raw: unknown, companyId: string): LimitGroupList | null {
  if (!isRecord(raw) || raw.companyId !== companyId) return null
  if (typeof raw.businessDate !== 'string' || !DATE.test(raw.businessDate) || !Array.isArray(raw.groups)) return null
  const groups: LimitGroup[] = []
  for (const entry of raw.groups) {
    const group = decodeGroup(entry)
    if (group === null) return null
    groups.push(group)
  }
  if (new Set(groups.map((group) => group.id)).size !== groups.length) return null
  if (new Set(groups.map((group) => group.code)).size !== groups.length) return null
  return { companyId, businessDate: raw.businessDate, groups }
}

export function decodeCreated(raw: unknown): LimitGroupCreated | null {
  if (!isRecord(raw) || !isUuid(raw.id) || raw.version !== '1') return null
  return { id: raw.id, version: raw.version }
}

export function decodePeople(raw: unknown): PersonList | null {
  if (!isRecord(raw) || !Array.isArray(raw.items) || typeof raw.hasMore !== 'boolean') return null
  const items: Person[] = []
  for (const entry of raw.items) {
    if (!isRecord(entry) || !isUuid(entry.userId) || !isNonEmpty(entry.displayName)) return null
    items.push({ userId: entry.userId, displayName: entry.displayName })
  }
  return { items, hasMore: raw.hasMore }
}

/** The binding of the person the request named, in distinct companies. */
export function decodeBinding(raw: unknown, userId: string): Binding | null {
  if (!isRecord(raw) || raw.userId !== userId || !Array.isArray(raw.companies) || raw.companies.length === 0) return null
  const companies: BindingCompany[] = []
  for (const entry of raw.companies) {
    if (!isRecord(entry) || !isUuid(entry.companyId) || typeof entry.version !== 'string' || !VERSION.test(entry.version)) {
      return null
    }
    if (!Array.isArray(entry.groups)) return null
    const groups: BoundGroup[] = []
    for (const group of entry.groups) {
      if (!isRecord(group) || !isUuid(group.id) || typeof group.code !== 'string' || !isValidCode(group.code)) return null
      if (!isNonEmpty(group.name) || typeof group.isActive !== 'boolean') return null
      groups.push({ id: group.id, code: group.code, name: group.name, isActive: group.isActive })
    }
    if (entry.version === '0' && groups.length > 0) return null
    companies.push({ companyId: entry.companyId, version: entry.version, groups })
  }
  if (new Set(companies.map((company) => company.companyId)).size !== companies.length) return null
  return { userId, companies }
}

/** The stable `code` of a ProblemDetails body, when it has one. */
export function problemCodeOf(body: unknown): string | null {
  return isRecord(body) && typeof body.code === 'string' && body.code.length > 0 ? body.code : null
}

/** The company and the group code a 409/422 refusal names (SAT-D66 multi-company write). */
export function problemSubjectOf(body: unknown): { readonly companyId: string | null; readonly groupCode: string | null } {
  if (!isRecord(body)) return { companyId: null, groupCode: null }
  return {
    companyId: isUuid(body.companyId) ? body.companyId : null,
    groupCode: typeof body.groupCode === 'string' && isValidCode(body.groupCode) ? body.groupCode : null,
  }
}
