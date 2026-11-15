/**
 * "Denetim kaydı" outcome filter and refused-attempt payload (F-023, F023-5b).
 *
 * Read off the owner: `GET /api/v1/audit/events?outcome=Succeeded|Refused` (F023-5a, exact ordinal match; any other
 * value is a 400) and the refused row the Audit module writes through `IRefusedAttemptRecorderV1`
 * (`RefusedAttemptAuditDraftMapper`): `outcome = "Refused"`, `beforeJson = "{}"` and an `afterJson` of exactly
 * `{ httpStatus, problemCode, refusalClass, fieldErrors: [{ field, code }] }`. The refusal classes are the closed list
 * `RefusedAttemptClassesV1` (KIM-D7). Authorization, identity and company-boundary refusals are never audit rows: they
 * live in the security event channel only (KIM-D10).
 *
 * The aggregated summary row of F023-4 (`{ count, windowStartUtc, windowEndUtc, refusalClass, problemCode }`, spec
 * "Hacim kontrolü") is decoded with the same strictness; aggregation stays off until its thresholds are decided, so
 * the server does not write such a row today.
 *
 * Every decoder here fails CLOSED: an unknown outcome, refusal class or payload shape returns `null`, and the page
 * decoder then refuses the whole page. Nothing is guessed or partially shown.
 */

/** The two stored outcomes (`AuditOutcomes`), exactly as the server writes and filters them. */
export const AUDIT_OUTCOMES = ['Succeeded', 'Refused'] as const

export type AuditOutcome = (typeof AUDIT_OUTCOMES)[number]

/**
 * The outcome filter of this screen. `''` is "Tümü" (nothing sent); `'invalid'` is a URL value this screen does not
 * know: it is never sent, the view shows the invalid-filter state instead.
 */
export type AuditOutcomeFilter = '' | AuditOutcome | 'invalid'

/** The URL parameter of the outcome filter and its Turkish slugs, so a shared link reads like the other filters. */
export const OUTCOME_PARAM = 'sonuc'

export const OUTCOME_SLUG: Record<AuditOutcome, string> = {
  Succeeded: 'basarili',
  Refused: 'reddedilen',
}

export function isAuditOutcome(value: unknown): value is AuditOutcome {
  return typeof value === 'string' && (AUDIT_OUTCOMES as readonly string[]).includes(value)
}

/** Reads the outcome filter from its URL value, trimmed. Absent or blank is "Tümü"; an unknown slug is `'invalid'`. */
export function outcomeFromParam(value: string | null): AuditOutcomeFilter {
  const trimmed = (value ?? '').trim()
  if (trimmed === '') return ''
  for (const outcome of AUDIT_OUTCOMES) {
    if (OUTCOME_SLUG[outcome] === trimmed) return outcome
  }
  return 'invalid'
}

/** The URL value of an outcome filter, or `null` when nothing is written ("Tümü" or an unknown value). */
export function outcomeToParam(outcome: AuditOutcomeFilter): string | null {
  return outcome === '' || outcome === 'invalid' ? null : OUTCOME_SLUG[outcome]
}

/** The closed refusal classes (`RefusedAttemptClassesV1`). */
export const REFUSAL_CLASSES = ['Validation', 'BusinessRule', 'Concurrency', 'Idempotency', 'RequestShape'] as const

export type RefusalClass = (typeof REFUSAL_CLASSES)[number]

export function isRefusalClass(value: unknown): value is RefusalClass {
  return typeof value === 'string' && (REFUSAL_CLASSES as readonly string[]).includes(value)
}

export interface RefusedFieldError {
  readonly field: string
  readonly code: string
}

/** One refused business attempt. */
export interface RefusalDetail {
  readonly kind: 'detail'
  readonly httpStatus: number
  readonly problemCode: string
  readonly refusalClass: RefusalClass
  readonly fieldErrors: readonly RefusedFieldError[]
}

/** The F023-4 summary of refusals counted past the threshold inside one window. */
export interface RefusalSummary {
  readonly kind: 'summary'
  readonly count: number
  readonly windowStartUtc: string
  readonly windowEndUtc: string
  readonly refusalClass: RefusalClass
  readonly problemCode: string
}

export type AuditRefusal = RefusalDetail | RefusalSummary

const DETAIL_FIELDS = ['httpStatus', 'problemCode', 'refusalClass', 'fieldErrors'] as const
const SUMMARY_FIELDS = ['count', 'windowStartUtc', 'windowEndUtc', 'refusalClass', 'problemCode'] as const
const FIELD_ERROR_FIELDS = ['field', 'code'] as const

/** The server's bounds (`RefusedAttemptAuditDraftMapper`): a code is at most 128, a field name at most 256 long. */
const CODE_MAX_LENGTH = 128
const FIELD_NAME_MAX_LENGTH = 256
const MAX_FIELD_ERRORS = 64

/** A refusal is a business 4xx; 401 and 403 are security-channel refusals and never an audit row (KIM-D10). */
const CLIENT_ERROR_MIN = 400
const CLIENT_ERROR_MAX = 499
const SECURITY_CHANNEL_STATUSES: readonly number[] = [401, 403]

const CODE = /^[a-z][a-z0-9]*(?:[_.-][a-z0-9]+)*$/
const FIELD_NAME = /^[A-Za-z][A-Za-z0-9_]*(?:\[[0-9]+\])*(?:\.[A-Za-z][A-Za-z0-9_]*(?:\[[0-9]+\])*)*$/
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, fields: readonly string[]): boolean {
  const keys = Object.keys(value)
  return keys.length === fields.length && fields.every((field) => Object.prototype.hasOwnProperty.call(value, field))
}

function isCode(value: unknown): value is string {
  return typeof value === 'string' && value.length <= CODE_MAX_LENGTH && CODE.test(value)
}

function isInstant(value: unknown): value is string {
  return typeof value === 'string' && value.length <= 64 && INSTANT.test(value) && !Number.isNaN(Date.parse(value))
}

function decodeFieldError(value: unknown): RefusedFieldError | null {
  if (!isRecord(value) || !hasExactKeys(value, FIELD_ERROR_FIELDS)) return null
  if (typeof value.field !== 'string' || value.field.length > FIELD_NAME_MAX_LENGTH || !FIELD_NAME.test(value.field)) return null
  if (!isCode(value.code)) return null
  return { field: value.field, code: value.code }
}

function decodeDetail(value: Record<string, unknown>): RefusalDetail | null {
  const status = value.httpStatus
  if (
    typeof status !== 'number' ||
    !Number.isInteger(status) ||
    status < CLIENT_ERROR_MIN ||
    status > CLIENT_ERROR_MAX ||
    SECURITY_CHANNEL_STATUSES.includes(status)
  ) {
    return null
  }
  if (!isCode(value.problemCode) || !isRefusalClass(value.refusalClass)) return null
  if (!Array.isArray(value.fieldErrors) || value.fieldErrors.length > MAX_FIELD_ERRORS) return null
  const fieldErrors: RefusedFieldError[] = []
  for (const raw of value.fieldErrors) {
    const error = decodeFieldError(raw)
    if (error === null) return null
    fieldErrors.push(error)
  }
  return { kind: 'detail', httpStatus: status, problemCode: value.problemCode, refusalClass: value.refusalClass, fieldErrors }
}

function decodeSummary(value: Record<string, unknown>): RefusalSummary | null {
  const count = value.count
  if (typeof count !== 'number' || !Number.isSafeInteger(count) || count < 1) return null
  if (!isInstant(value.windowStartUtc) || !isInstant(value.windowEndUtc)) return null
  if (Date.parse(value.windowEndUtc) < Date.parse(value.windowStartUtc)) return null
  if (!isCode(value.problemCode) || !isRefusalClass(value.refusalClass)) return null
  return {
    kind: 'summary',
    count,
    windowStartUtc: value.windowStartUtc,
    windowEndUtc: value.windowEndUtc,
    refusalClass: value.refusalClass,
    problemCode: value.problemCode,
  }
}

/**
 * Decodes the `afterJson` of a `Refused` row: a refusal detail or an F023-4 summary, by its exact key set. `null` for
 * anything else (unparsable JSON, a missing or extra key, an unknown refusal class, a 401/403 or non-4xx status).
 */
export function decodeRefusal(afterJson: string): AuditRefusal | null {
  let parsed: unknown
  try {
    parsed = JSON.parse(afterJson)
  } catch {
    return null
  }
  if (!isRecord(parsed)) return null
  if (hasExactKeys(parsed, DETAIL_FIELDS)) return decodeDetail(parsed)
  if (hasExactKeys(parsed, SUMMARY_FIELDS)) return decodeSummary(parsed)
  return null
}
