/**
 * WP1 (YOL-D17, YOL-D18) — `GET /api/v1/work/approval-rules`: the company's versioned approval rule table, one row per
 * approval kind the Work inbox knows: the owner module ("sahibi"), the owner's approver permission, the duration class and
 * escalation permission in force, the version in force ("0" = the decided YOL-D18 default) and the newest versions. Read
 * only on this screen; the strict decoder refuses the whole answer on any unexpected member or shape.
 */

export const WORK_APPROVAL_RULES_PATH = '/work/approval-rules'

export const RULE_CLASS_HOURS = 'Hours'
export const RULE_CLASS_BUSINESS_DAYS = 'BusinessDays'
export type WorkApprovalRuleClass = typeof RULE_CLASS_HOURS | typeof RULE_CLASS_BUSINESS_DAYS

export const RULE_STATE_CURRENT = 'Current'
export const RULE_STATE_APPROVER_PERMISSION_CHANGED = 'ApproverPermissionChanged'
export type WorkApprovalRuleState = typeof RULE_STATE_CURRENT | typeof RULE_STATE_APPROVER_PERMISSION_CHANGED

export interface WorkApprovalRuleVersion {
  readonly version: string
  readonly durationClass: WorkApprovalRuleClass
  readonly approverPermission: string
  readonly escalationPermission: string | null
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly reason: string
}

export interface WorkApprovalRule {
  readonly kind: string
  readonly ownerModule: string
  readonly approverPermission: string
  readonly durationClass: WorkApprovalRuleClass
  readonly escalationPermission: string | null
  readonly version: string
  readonly versionText: string
  readonly isDefault: boolean
  readonly defaultBasis: string | null
  readonly state: WorkApprovalRuleState
  readonly recordedBy: string | null
  readonly recordedAtUtc: string | null
  readonly reason: string | null
  readonly versions: readonly WorkApprovalRuleVersion[]
}

export interface WorkApprovalRuleTable {
  readonly companyId: string
  readonly rules: readonly WorkApprovalRule[]
}

type Obj = Record<string, unknown>

const ROOT_KEYS = ['companyId', 'rules'] as const
const RULE_KEYS = [
  'kind', 'ownerModule', 'approverPermission', 'durationClass', 'escalationPermission', 'version', 'versionText', 'isDefault',
  'defaultBasis', 'state', 'recordedBy', 'recordedAtUtc', 'reason', 'versions',
] as const
const VERSION_KEYS = ['version', 'durationClass', 'approverPermission', 'escalationPermission', 'recordedBy', 'recordedAtUtc', 'reason'] as const
const MAX_VERSIONS = 20
const MAX_REASON = 500

const MODULE = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/
const KIND = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*)*\.v[1-9][0-9]*$/
const PERMISSION = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*)+$/
const VERSION = /^(?:0|[1-9][0-9]{0,18})$/
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const isObject = (value: unknown): value is Obj => typeof value === 'object' && value !== null && !Array.isArray(value)
const exact = (value: Obj, keys: readonly string[]) =>
  Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key))
const isClass = (value: unknown): value is WorkApprovalRuleClass => value === RULE_CLASS_HOURS || value === RULE_CLASS_BUSINESS_DAYS
const isPermission = (value: unknown): value is string => typeof value === 'string' && value.length <= 128 && PERMISSION.test(value)
const isOptionalPermission = (value: unknown): value is string | null => value === null || isPermission(value)
const isVersion = (value: unknown): value is string =>
  typeof value === 'string' && VERSION.test(value) && BigInt(value) <= 9223372036854775807n
const isText = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= max
const isInstant = (value: unknown): value is string =>
  typeof value === 'string' && value.length <= 64 && /^\d{4}-\d\d-\d\dT.*(?:Z|[+-]\d\d:\d\d)$/.test(value) && Number.isFinite(Date.parse(value))
const isUser = (value: unknown): value is string => typeof value === 'string' && GUID.test(value)

function decodeVersion(raw: unknown): WorkApprovalRuleVersion | null {
  if (!isObject(raw) || !exact(raw, VERSION_KEYS)) return null
  const { version, durationClass, approverPermission, escalationPermission, recordedBy, recordedAtUtc, reason } = raw
  if (!isVersion(version) || version === '0' || !isClass(durationClass) || !isPermission(approverPermission)) return null
  if (!isOptionalPermission(escalationPermission) || !isUser(recordedBy) || !isInstant(recordedAtUtc) || !isText(reason, MAX_REASON)) return null
  return { version, durationClass, approverPermission, escalationPermission, recordedBy: recordedBy.toLowerCase(), recordedAtUtc, reason }
}

function decodeRule(raw: unknown): WorkApprovalRule | null {
  if (!isObject(raw) || !exact(raw, RULE_KEYS)) return null
  const {
    kind, ownerModule, approverPermission, durationClass, escalationPermission, version, versionText, isDefault, defaultBasis, state,
    recordedBy, recordedAtUtc, reason, versions,
  } = raw
  if (typeof ownerModule !== 'string' || !MODULE.test(ownerModule)) return null
  if (typeof kind !== 'string' || kind.length > 160 || !KIND.test(kind) || !kind.startsWith(`${ownerModule}.`)) return null
  if (!isPermission(approverPermission) || !isClass(durationClass) || !isOptionalPermission(escalationPermission)) return null
  if (!isVersion(version) || versionText !== `work.approval-rule:${kind}:${version}` || typeof isDefault !== 'boolean') return null
  if (state !== RULE_STATE_CURRENT && state !== RULE_STATE_APPROVER_PERMISSION_CHANGED) return null
  if (!Array.isArray(versions) || versions.length > MAX_VERSIONS) return null
  const history: WorkApprovalRuleVersion[] = []
  for (const item of versions) {
    const decoded = decodeVersion(item)
    if (decoded === null) return null
    if (history.length > 0 && BigInt(history[history.length - 1].version) <= BigInt(decoded.version)) return null
    history.push(decoded)
  }
  if (isDefault) {
    // The decided default: version 0, its basis named, nothing recorded, no escalation, always current.
    if (version !== '0' || !isText(defaultBasis, 32) || recordedBy !== null || recordedAtUtc !== null || reason !== null
      || escalationPermission !== null || state !== RULE_STATE_CURRENT || history.length !== 0) return null
    return {
      kind, ownerModule, approverPermission, durationClass, escalationPermission, version, versionText, isDefault, defaultBasis, state,
      recordedBy, recordedAtUtc, reason, versions: history,
    }
  }
  if (version === '0' || defaultBasis !== null || !isUser(recordedBy) || !isInstant(recordedAtUtc) || !isText(reason, MAX_REASON)) return null
  const newest = history[0]
  // The version in force is the newest recorded one, word for word.
  if (newest === undefined || newest.version !== version || newest.durationClass !== durationClass
    || newest.escalationPermission !== escalationPermission || newest.recordedBy !== recordedBy.toLowerCase()
    || newest.recordedAtUtc !== recordedAtUtc || newest.reason !== reason) return null
  // "Current" means the newest version names the owner's permission today; otherwise the state must say it does not.
  if ((newest.approverPermission === approverPermission) !== (state === RULE_STATE_CURRENT)) return null
  return {
    kind, ownerModule, approverPermission, durationClass, escalationPermission, version, versionText, isDefault, defaultBasis, state,
    recordedBy: recordedBy.toLowerCase(), recordedAtUtc, reason, versions: history,
  }
}

/** The whole rule table of `companyId`, or null. */
export function decodeWorkApprovalRuleTable(raw: unknown, companyId: string): WorkApprovalRuleTable | null {
  if (!isObject(raw) || !exact(raw, ROOT_KEYS)) return null
  if (typeof raw.companyId !== 'string' || raw.companyId.toLowerCase() !== companyId.toLowerCase()) return null
  if (!Array.isArray(raw.rules)) return null
  const rules: WorkApprovalRule[] = []
  const seen = new Set<string>()
  for (const item of raw.rules) {
    const rule = decodeRule(item)
    if (rule === null || seen.has(rule.kind)) return null
    seen.add(rule.kind)
    rules.push(rule)
  }
  return { companyId: companyId.toLowerCase(), rules }
}
