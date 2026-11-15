/**
 * ORG-05 (KIM-S23, KIM-D28, DEC-ORG-005) — the active company's VKN: wire contract and strict decoders.
 *
 *   GET  /api/v1/organization/company/tax-id                                     -> CompanyTaxIdView | 403
 *   POST /api/v1/organization/company/tax-id/change-requests  {expectedVersion, proposedVkn, reason}  -> 201
 *   POST /api/v1/organization/company/tax-id/change-requests/{id}/decision {expectedVersion, decision, note} -> 204
 *
 * (`OrganizationCompanyTaxIdContracts.cs`.) What the shapes mean:
 *
 *  - `version` is the COMPANY record's version, the `expectedVersion` of the next request or decision. A request
 *    keeps the version and VKN it was prepared against; an approval never applies over a change it did not see.
 *  - `vkn` is ten ASCII digits, or null only on a placeholder company that has no VKN yet.
 *  - `pending` is the one open change request (at most one per company), `lastDecided` the most recent closed one.
 *  - `viewer` is the server's own statement of what THIS caller may do. The screen hides what it says would be
 *    refused and never derives it; the server still decides every command (hazırlayan ≠ onaylayan is enforced there).
 *
 * Every decoder fails closed: one unreadable field makes the whole answer malformed, never a partly shown screen.
 */

import type { VersionConflict } from './contract'

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

/** The server's VKN shape (`Company.VknLength` ASCII digits). The server stays the authority. */
export const VKN_PATTERN = /^[0-9]{10}$/

/** The server's request limits, repeated only so the form can say so before it sends. */
export const COMPANY_TAX_ID_LIMITS = {
  reason: 1000,
  note: 1000,
} as const

export const TAX_ID_CHANGE_STATUS = {
  pending: 'pending',
  approved: 'approved',
  rejected: 'rejected',
} as const

export type TaxIdChangeStatus = (typeof TAX_ID_CHANGE_STATUS)[keyof typeof TAX_ID_CHANGE_STATUS]

export const TAX_ID_DECISION = { approve: 'approve', reject: 'reject' } as const

export type TaxIdDecision = (typeof TAX_ID_DECISION)[keyof typeof TAX_ID_DECISION]

/** The stable problem codes of the two commands (`OrganizationProblemCodesV1.CompanyTaxId*`). */
export const COMPANY_TAX_ID_PROBLEM = {
  inUse: 'organization_company_tax_id_in_use',
  changePending: 'organization_company_tax_id_change_pending',
  changeNotPending: 'organization_company_tax_id_change_not_pending',
  changeStale: 'organization_company_tax_id_change_stale',
  selfDecision: 'organization_company_tax_id_self_decision',
  unchanged: 'organization_company_tax_id_unchanged',
  versionConflict: 'organization_company_tax_id_version_conflict',
  idempotencyConflict: 'organization_company_tax_id_idempotency_conflict',
} as const

export type CompanyTaxIdProblemCode = (typeof COMPANY_TAX_ID_PROBLEM)[keyof typeof COMPANY_TAX_ID_PROBLEM]

export interface TaxIdChange {
  readonly id: string
  readonly currentVkn: string | null
  readonly proposedVkn: string
  readonly reason: string
  readonly status: TaxIdChangeStatus
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionNote: string | null
}

export interface TaxIdViewer {
  readonly canRequest: boolean
  readonly canApprove: boolean
  readonly isRequesterOfPending: boolean
}

export interface CompanyTaxId {
  readonly companyId: string
  readonly code: string
  readonly legalName: string
  readonly vkn: string | null
  readonly isPlaceholder: boolean
  readonly version: number
  readonly pending: TaxIdChange | null
  readonly lastDecided: TaxIdChange | null
  readonly viewer: TaxIdViewer
}

const CHANGE_KEYS = [
  'id',
  'currentVkn',
  'proposedVkn',
  'reason',
  'status',
  'requestedBy',
  'requestedAtUtc',
  'decidedBy',
  'decidedAtUtc',
  'decisionNote',
] as const

const VIEWER_KEYS = ['canRequest', 'canApprove', 'isRequesterOfPending'] as const

const VIEW_KEYS = [
  'companyId',
  'code',
  'legalName',
  'vkn',
  'isPlaceholder',
  'version',
  'pending',
  'lastDecided',
  'viewer',
] as const

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

function isNonEmptyText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isOptionalText(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function isTimestamp(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && !Number.isNaN(Date.parse(value))
}

function isVkn(value: unknown): value is string {
  return typeof value === 'string' && VKN_PATTERN.test(value)
}

function isPositiveVersion(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 1
}

function isStatus(value: unknown): value is TaxIdChangeStatus {
  return (
    typeof value === 'string' &&
    (Object.values(TAX_ID_CHANGE_STATUS) as readonly string[]).includes(value)
  )
}

export function decodeTaxIdChange(raw: unknown): TaxIdChange | null {
  if (!isRecord(raw) || !hasExactKeys(raw, CHANGE_KEYS)) return null
  const { id, currentVkn, proposedVkn, reason, status, requestedBy, requestedAtUtc, decidedBy, decidedAtUtc, decisionNote } =
    raw
  if (typeof id !== 'string' || !UUID.test(id)) return null
  if (currentVkn !== null && !isVkn(currentVkn)) return null
  if (!isVkn(proposedVkn) || proposedVkn === currentVkn) return null
  if (!isNonEmptyText(reason) || !isStatus(status)) return null
  if (!isNonEmptyText(requestedBy) || !isTimestamp(requestedAtUtc)) return null
  if (!isOptionalText(decisionNote)) return null
  if (status === TAX_ID_CHANGE_STATUS.pending) {
    // A pending request has no decision yet; any decision field on it is a contradiction.
    if (decidedBy !== null || decidedAtUtc !== null || decisionNote !== null) return null
  } else if (!isNonEmptyText(decidedBy) || !isTimestamp(decidedAtUtc)) {
    return null
  }
  return {
    id,
    currentVkn,
    proposedVkn,
    reason,
    status,
    requestedBy,
    requestedAtUtc,
    decidedBy: decidedBy as string | null,
    decidedAtUtc: decidedAtUtc as string | null,
    decisionNote,
  }
}

function decodeViewer(raw: unknown): TaxIdViewer | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEWER_KEYS)) return null
  const { canRequest, canApprove, isRequesterOfPending } = raw
  if (typeof canRequest !== 'boolean' || typeof canApprove !== 'boolean' || typeof isRequesterOfPending !== 'boolean') {
    return null
  }
  // The server only answers a caller holding one of the two names.
  if (!canRequest && !canApprove) return null
  return { canRequest, canApprove, isRequesterOfPending }
}

/**
 * The GET answer. It must be the ACTIVE company's (`expectedCompanyId`): an answer about another company is
 * malformed, never shown under this company's label.
 */
export function decodeCompanyTaxId(raw: unknown, expectedCompanyId: string): CompanyTaxId | null {
  if (!isRecord(raw) || !hasExactKeys(raw, VIEW_KEYS)) return null
  const { companyId, code, legalName, vkn, isPlaceholder, version, pending, lastDecided, viewer } = raw
  if (typeof companyId !== 'string' || !UUID.test(companyId)) return null
  if (companyId.toLowerCase() !== expectedCompanyId.toLowerCase()) return null
  if (!isNonEmptyText(code) || !isNonEmptyText(legalName)) return null
  if (typeof isPlaceholder !== 'boolean') return null
  // KIM-D28: only a placeholder company may be without a VKN.
  if (vkn !== null && !isVkn(vkn)) return null
  if (vkn === null && !isPlaceholder) return null
  if (!isPositiveVersion(version)) return null
  const decodedPending = pending === null ? null : decodeTaxIdChange(pending)
  if (pending !== null && (decodedPending === null || decodedPending.status !== TAX_ID_CHANGE_STATUS.pending)) {
    return null
  }
  const decodedLast = lastDecided === null ? null : decodeTaxIdChange(lastDecided)
  if (lastDecided !== null && (decodedLast === null || decodedLast.status === TAX_ID_CHANGE_STATUS.pending)) {
    return null
  }
  const decodedViewer = decodeViewer(viewer)
  if (decodedViewer === null) return null
  // "I prepared the pending request" without a pending request is a contradiction.
  if (decodedViewer.isRequesterOfPending && decodedPending === null) return null
  return {
    companyId,
    code,
    legalName,
    vkn,
    isPlaceholder,
    version,
    pending: decodedPending,
    lastDecided: decodedLast,
    viewer: decodedViewer,
  }
}

/** The stable code on a 409/422 problem body, or null when absent or not one of this surface's codes. */
export function decodeCompanyTaxIdProblemCode(raw: unknown): CompanyTaxIdProblemCode | null {
  if (!isRecord(raw)) return null
  const code = raw.code
  return typeof code === 'string' && (Object.values(COMPANY_TAX_ID_PROBLEM) as readonly string[]).includes(code)
    ? (code as CompanyTaxIdProblemCode)
    : null
}

/** The `conflict` object on a version-conflict 409. */
export function decodeTaxIdVersionConflict(raw: unknown): VersionConflict | null {
  if (!isRecord(raw) || !isRecord(raw.conflict)) return null
  const { expectedVersion, currentVersion } = raw.conflict
  if (!isPositiveVersion(expectedVersion) || !isPositiveVersion(currentVersion)) return null
  return { expectedVersion, currentVersion }
}

/** The form's own check; the server refuses the same shapes with a 400. */
export function isValidVknInput(value: string): boolean {
  return VKN_PATTERN.test(value.trim())
}
