/**
 * F-029 S4b wire contract — the paths, request bodies and strict runtime decoders of the Settings sales policy routes
 * (`SettingsSalesPolicyEndpoints`, `SalesPoliciesV1.cs`):
 *
 *   GET  /settings/sales-policies/delivery-tolerance                              -> SalesDeliveryToleranceViewV1
 *   POST /settings/sales-policies/delivery-tolerance/baseline-transfer            -> 204 (once per company)
 *   POST /settings/sales-policies/delivery-tolerance/versions                     -> 204
 *   GET  /settings/sales-policies/approval-thresholds                             -> SalesApprovalThresholdsViewV1
 *   POST /settings/sales-policies/approval-thresholds/baseline-transfer           -> 204 (once per company)
 *   POST /settings/sales-policies/approval-thresholds/{currencyId}/versions       -> 204
 *
 * SAT-D5 (delivery tolerance: warn / approval / block percents) and SAT-D6 (approval amount threshold per currency) move
 * from the compiled catalog to Settings records (URN-D2/D3): one authorized manager records a version, no second approval,
 * every change audited; a version never starts before today (URN-D4). Before the one-time baseline transfer the view shows
 * the compiled catalog's entries (no recorder) and `version` is "0".
 *
 * Every decoder fails CLOSED: one bad entry invalidates the whole answer, unknown states and source kinds are rejected
 * rather than guessed, keys must match the contract exactly, and percents and amounts stay the lossless decimal TEXT the
 * server sends — this screen never compares, rounds or computes a tolerance or a threshold (the backend does).
 */

import { isDateText, isPercentText, isUuid } from './discountLimitContract'

export const SALES_POLICIES_PATH = '/settings/sales-policies'
export const DELIVERY_TOLERANCE_PATH = `${SALES_POLICIES_PATH}/delivery-tolerance`
export const DELIVERY_TOLERANCE_BASELINE_PATH = `${DELIVERY_TOLERANCE_PATH}/baseline-transfer`
export const DELIVERY_TOLERANCE_VERSIONS_PATH = `${DELIVERY_TOLERANCE_PATH}/versions`
export const APPROVAL_THRESHOLDS_PATH = `${SALES_POLICIES_PATH}/approval-thresholds`
export const APPROVAL_THRESHOLDS_BASELINE_PATH = `${APPROVAL_THRESHOLDS_PATH}/baseline-transfer`

export function approvalThresholdVersionsPath(currencyId: string): string {
  return `${APPROVAL_THRESHOLDS_PATH}/${encodeURIComponent(currencyId)}/versions`
}

/** The stable problem codes the screens distinguish (`SalesPolicyProblemCodesV1`); anything else is a generic refusal. */
export const SALES_POLICY_PROBLEM = {
  requestInvalid: 'settings_sales_policy_request_invalid',
  commandMetadataInvalid: 'settings_sales_policy_command_metadata_invalid',
  reasonRequired: 'settings_sales_policy_reason_required',
  reasonTooLong: 'settings_sales_policy_reason_too_long',
  idempotencyConflict: 'settings_sales_policy_idempotency_conflict',
  versionConflict: 'settings_sales_policy_version_conflict',
  effectiveFromPast: 'settings_sales_policy_effective_from_past',
  baselineRequired: 'settings_sales_policy_baseline_required',
  baselineAlreadyTransferred: 'settings_sales_policy_baseline_already_transferred',
  currencyNotServed: 'settings_sales_policy_currency_not_served',
  amountScaleExceeded: 'settings_sales_policy_amount_scale_exceeded',
  providerUnavailable: 'settings_provider_unavailable',
  accessDenied: 'settings_access_denied',
} as const

/**
 * Refusals after which the screen's copy is stale: the form closes and the view is read again, so the next attempt starts
 * from the current version and baseline state.
 */
export const SALES_POLICY_STALE_CODES: readonly string[] = [
  SALES_POLICY_PROBLEM.versionConflict,
  SALES_POLICY_PROBLEM.baselineRequired,
  SALES_POLICY_PROBLEM.baselineAlreadyTransferred,
]

/** Technical bounds mirrored from `SalesPolicyInputLimitsV1` (transport shape, not business policy). */
export const SALES_POLICY_INPUT = {
  reasonMaximumLength: 4000,
  decisionReferenceMaximumLength: 400,
  percentMaximumScale: 4,
  amountMaximumIntegerDigits: 20,
  amountMaximumScale: 8,
} as const

export const SALES_POLICY_STATE = {
  inForce: 'InForce',
  scheduled: 'Scheduled',
  ended: 'Ended',
  superseded: 'Superseded',
} as const
export type SalesPolicyState = (typeof SALES_POLICY_STATE)[keyof typeof SALES_POLICY_STATE]

export const SALES_POLICY_SOURCE = {
  compiledBaselineV0: 'CompiledBaselineV0',
  userMaintained: 'UserMaintained',
} as const
export type SalesPolicySource = (typeof SALES_POLICY_SOURCE)[keyof typeof SALES_POLICY_SOURCE]

/** What every version entry carries besides its values. */
export interface SalesPolicyVersionMeta {
  readonly profileId: string
  readonly version: string
  readonly isActive: boolean
  readonly effectiveFrom: string
  readonly effectiveThrough: string | null
  readonly state: SalesPolicyState
  readonly sourceKind: SalesPolicySource
  readonly decisionReference: string | null
  readonly recordedBy: string | null
  readonly recordedAtUtc: string | null
  readonly reason: string | null
}

export interface DeliveryToleranceVersion extends SalesPolicyVersionMeta {
  readonly warnPercent: string
  readonly approvalPercent: string
  readonly blockPercent: string
}

export interface DeliveryToleranceView {
  readonly companyId: string
  readonly today: string
  readonly isBaselineTransferred: boolean
  /** The latest stored version ("0" when none): the next record's expected version. */
  readonly version: string
  readonly versions: readonly DeliveryToleranceVersion[]
}

export interface ApprovalThresholdVersion extends SalesPolicyVersionMeta {
  readonly isEnabled: boolean
  /** The threshold as lossless text when enabled; null when disabled (no amount-based routing). */
  readonly amount: string | null
}

export interface ApprovalThresholdCurrency {
  readonly currencyId: string
  readonly version: string
  readonly versions: readonly ApprovalThresholdVersion[]
}

export interface ApprovalThresholdsView {
  readonly companyId: string
  readonly today: string
  readonly isBaselineTransferred: boolean
  readonly currencies: readonly ApprovalThresholdCurrency[]
}

export interface TransferBaselineBody {
  readonly reason: string
}

export interface RecordDeliveryToleranceBody {
  readonly expectedVersion: string
  readonly warnPercent: string
  readonly approvalPercent: string
  readonly blockPercent: string
  readonly reason: string
  /** Null = today (Europe/Istanbul, the server's business date). */
  readonly effectiveFrom: string | null
}

export interface RecordApprovalThresholdBody {
  readonly expectedVersion: string
  readonly isEnabled: boolean
  readonly amount: string | null
  readonly reason: string
  readonly effectiveFrom: string | null
}

const VERSION = /^(0|[1-9][0-9]*)$/
const POSITIVE_VERSION = /^[1-9][0-9]*$/
const AMOUNT = new RegExp(
  `^(0|[1-9][0-9]{0,${SALES_POLICY_INPUT.amountMaximumIntegerDigits - 1}})(\\.[0-9]{1,${SALES_POLICY_INPUT.amountMaximumScale}})?$`,
)
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64

const META_FIELDS = [
  'profileId',
  'version',
  'isActive',
  'effectiveFrom',
  'effectiveThrough',
  'state',
  'sourceKind',
  'decisionReference',
  'recordedBy',
  'recordedAtUtc',
  'reason',
] as const
const TOLERANCE_VERSION_FIELDS = [...META_FIELDS, 'warnPercent', 'approvalPercent', 'blockPercent'] as const
const TOLERANCE_VIEW_FIELDS = ['companyId', 'today', 'isBaselineTransferred', 'version', 'versions'] as const
const THRESHOLD_VERSION_FIELDS = [...META_FIELDS, 'isEnabled', 'amount'] as const
const THRESHOLD_CURRENCY_FIELDS = ['currencyId', 'version', 'versions'] as const
const THRESHOLDS_VIEW_FIELDS = ['companyId', 'today', 'isBaselineTransferred', 'currencies'] as const

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** The object's key set equals the contract's exactly: an extra or a missing key is another contract. */
function hasExactKeys(value: Record<string, unknown>, fields: readonly string[]): boolean {
  const keys = Object.keys(value)
  return keys.length === fields.length && fields.every((field) => Object.prototype.hasOwnProperty.call(value, field))
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isInstant(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length <= INSTANT_MAXIMUM_LENGTH &&
    INSTANT.test(value) &&
    !Number.isNaN(Date.parse(value))
  )
}

function isGuid(value: unknown): value is string {
  return isUuid(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value)
}

/** An enabled threshold as the server writes it: no sign, no exponent, no leading zero, greater than zero. */
export function isAmountText(value: unknown): value is string {
  return typeof value === 'string' && AMOUNT.test(value) && /[1-9]/.test(value)
}

function isState(value: unknown): value is SalesPolicyState {
  return Object.values(SALES_POLICY_STATE).includes(value as SalesPolicyState)
}

function isSource(value: unknown): value is SalesPolicySource {
  return Object.values(SALES_POLICY_SOURCE).includes(value as SalesPolicySource)
}

/**
 * The fields every version entry shares. `transferred` is the view's flag: before the transfer an entry is the compiled
 * catalog's (no recorder, no reason); afterwards it is a stored record and names who recorded it, when and why.
 */
function decodeMeta(raw: Record<string, unknown>, transferred: boolean): SalesPolicyVersionMeta | null {
  const { profileId, version, isActive, effectiveFrom, effectiveThrough, state, sourceKind } = raw
  const { decisionReference, recordedBy, recordedAtUtc, reason } = raw
  if (!isGuid(profileId) || typeof version !== 'string' || !POSITIVE_VERSION.test(version)) return null
  if (typeof isActive !== 'boolean' || !isDateText(effectiveFrom)) return null
  if (effectiveThrough !== null && (!isDateText(effectiveThrough) || effectiveThrough < effectiveFrom)) return null
  if (!isState(state) || !isSource(sourceKind)) return null
  if (
    decisionReference !== null &&
    (!isNonEmpty(decisionReference) || decisionReference.length > SALES_POLICY_INPUT.decisionReferenceMaximumLength)
  ) {
    return null
  }
  const through = effectiveThrough as string | null
  const reference = decisionReference as string | null
  if (!transferred) {
    if (sourceKind !== SALES_POLICY_SOURCE.compiledBaselineV0) return null
    if (recordedBy !== null || recordedAtUtc !== null || reason !== null) return null
    return {
      profileId, version, isActive, effectiveFrom, effectiveThrough: through, state, sourceKind,
      decisionReference: reference, recordedBy: null, recordedAtUtc: null, reason: null,
    }
  }
  if (!isNonEmpty(recordedBy) || !isInstant(recordedAtUtc) || !isNonEmpty(reason)) return null
  if (reason.length > SALES_POLICY_INPUT.reasonMaximumLength) return null
  return {
    profileId, version, isActive, effectiveFrom, effectiveThrough: through, state, sourceKind,
    decisionReference: reference, recordedBy, recordedAtUtc, reason,
  }
}

function decodeToleranceVersion(raw: unknown, transferred: boolean): DeliveryToleranceVersion | null {
  if (!isRecord(raw) || !hasExactKeys(raw, TOLERANCE_VERSION_FIELDS)) return null
  const meta = decodeMeta(raw, transferred)
  const { warnPercent, approvalPercent, blockPercent } = raw
  if (meta === null || !isPercentText(warnPercent) || !isPercentText(approvalPercent) || !isPercentText(blockPercent)) {
    return null
  }
  return { ...meta, warnPercent, approvalPercent, blockPercent }
}

function decodeThresholdVersion(raw: unknown, transferred: boolean): ApprovalThresholdVersion | null {
  if (!isRecord(raw) || !hasExactKeys(raw, THRESHOLD_VERSION_FIELDS)) return null
  const meta = decodeMeta(raw, transferred)
  const { isEnabled, amount } = raw
  if (meta === null || typeof isEnabled !== 'boolean') return null
  if (isEnabled ? !isAmountText(amount) : amount !== null) return null
  return { ...meta, isEnabled, amount: isEnabled ? (amount as string) : null }
}

/** The view's `version`: "0" before the transfer (nothing stored yet), a stored version afterwards ("0" when empty). */
function isViewVersion(value: unknown, transferred: boolean): value is string {
  return typeof value === 'string' && (transferred ? VERSION.test(value) : value === '0')
}

/** One version number names one entry: two entries with the same number cannot both be this timeline. */
function hasDistinctVersions(entries: readonly SalesPolicyVersionMeta[]): boolean {
  return new Set(entries.map((entry) => entry.version)).size === entries.length
}

/** The delivery tolerance timeline of the company the request named; a view for another company is not this answer. */
export function decodeDeliveryToleranceView(raw: unknown, companyId: string): DeliveryToleranceView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, TOLERANCE_VIEW_FIELDS)) return null
  const { today, isBaselineTransferred, version, versions } = raw
  if (raw.companyId !== companyId || !isDateText(today) || typeof isBaselineTransferred !== 'boolean') return null
  if (!isViewVersion(version, isBaselineTransferred) || !Array.isArray(versions)) return null
  const decoded: DeliveryToleranceVersion[] = []
  for (const entry of versions) {
    const item = decodeToleranceVersion(entry, isBaselineTransferred)
    if (item === null) return null
    decoded.push(item)
  }
  if (!hasDistinctVersions(decoded)) return null
  return { companyId, today, isBaselineTransferred, version, versions: decoded }
}

function decodeThresholdCurrency(raw: unknown, transferred: boolean): ApprovalThresholdCurrency | null {
  if (!isRecord(raw) || !hasExactKeys(raw, THRESHOLD_CURRENCY_FIELDS)) return null
  const { currencyId, version, versions } = raw
  if (!isGuid(currencyId) || !isViewVersion(version, transferred) || !Array.isArray(versions)) return null
  const decoded: ApprovalThresholdVersion[] = []
  for (const entry of versions) {
    const item = decodeThresholdVersion(entry, transferred)
    if (item === null) return null
    decoded.push(item)
  }
  if (!hasDistinctVersions(decoded)) return null
  return { currencyId, version, versions: decoded }
}

/** The approval thresholds of the company the request named, one block per distinct currency. */
export function decodeApprovalThresholdsView(raw: unknown, companyId: string): ApprovalThresholdsView | null {
  if (!isRecord(raw) || !hasExactKeys(raw, THRESHOLDS_VIEW_FIELDS)) return null
  const { today, isBaselineTransferred, currencies } = raw
  if (raw.companyId !== companyId || !isDateText(today) || typeof isBaselineTransferred !== 'boolean') return null
  if (!Array.isArray(currencies)) return null
  const decoded: ApprovalThresholdCurrency[] = []
  for (const entry of currencies) {
    const item = decodeThresholdCurrency(entry, isBaselineTransferred)
    if (item === null) return null
    decoded.push(item)
  }
  const ids = decoded.map((currency) => currency.currencyId.toLowerCase())
  if (new Set(ids).size !== ids.length) return null
  return { companyId, today, isBaselineTransferred, currencies: decoded }
}

/** The entry in force today, as the server marked it (the screen does not work it out from the dates). */
export function inForceOf<T extends SalesPolicyVersionMeta>(versions: readonly T[]): T | null {
  return versions.find((entry) => entry.state === SALES_POLICY_STATE.inForce) ?? null
}
