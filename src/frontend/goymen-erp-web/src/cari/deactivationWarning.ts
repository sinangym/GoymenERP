/**
 * ALS-D99 / ALS-D100 — the Cari deactivation warning and the group-wide deactivation.
 *
 * Mirrors, never invents: the shapes are `CounterpartyDeactivationExposureView`,
 * `CounterpartyGroupDeactivationPreviewView` and `CounterpartyGroupDeactivationResultView`
 * of the backend contract (operations `GetCounterpartyDeactivationWarningV1`,
 * `GetCounterpartyGroupDeactivationPreviewV1`, `GroupDeactivateCounterpartyV1`).
 *
 *  - An open balance or open documents never BLOCK a deactivation; the screen lists them first and the actor
 *    acknowledges the list. Counts only — no amount is disclosed through the warning.
 *  - A line the owner module could not answer (`unavailable`) or the actor may not read (`denied`) is a warning too,
 *    never a silent "nothing open". `acknowledgementRequired` is true when any line is not `clear`.
 *  - The decoders are strict: an unknown key, an unknown spelling, a missing line or an inconsistent flag is `null`,
 *    never a partly trusted object. Every user-facing word lives in `cariCommandText.ts`.
 */

import { isBoundUuid, isLosslessVersion, isSafeIntish } from './wireGuards'

export const DEACTIVATION_WARNING_KIND = {
  financeOpenBalance: 'finance-open-balance',
  salesOpenDocuments: 'sales-open-documents',
  purchasingOpenDocuments: 'purchasing-open-documents',
} as const

export type DeactivationWarningKind = (typeof DEACTIVATION_WARNING_KIND)[keyof typeof DEACTIVATION_WARNING_KIND]

/** The server answers every kind, in this order; a missing or repeated kind is malformed. */
export const DEACTIVATION_WARNING_KINDS: readonly DeactivationWarningKind[] = [
  DEACTIVATION_WARNING_KIND.financeOpenBalance,
  DEACTIVATION_WARNING_KIND.salesOpenDocuments,
  DEACTIVATION_WARNING_KIND.purchasingOpenDocuments,
]

export const DEACTIVATION_WARNING_OUTCOME = {
  unavailable: 'unavailable',
  denied: 'denied',
  clear: 'clear',
  open: 'open',
} as const

export type DeactivationWarningOutcome =
  (typeof DEACTIVATION_WARNING_OUTCOME)[keyof typeof DEACTIVATION_WARNING_OUTCOME]

const KNOWN_OUTCOMES = new Set<string>(Object.values(DEACTIVATION_WARNING_OUTCOME))

export const GROUP_DEACTIVATION_OUTCOME = {
  deactivated: 'deactivated',
  alreadyPassive: 'already-passive',
  versionConflict: 'version-conflict',
  notFound: 'not-found',
  forbidden: 'forbidden',
  warningAcknowledgementRequired: 'warning-acknowledgement-required',
  idempotencyConflict: 'idempotency-conflict',
  failed: 'failed',
  notRequested: 'not-requested',
} as const

export type GroupDeactivationOutcome = (typeof GROUP_DEACTIVATION_OUTCOME)[keyof typeof GROUP_DEACTIVATION_OUTCOME]

const KNOWN_GROUP_OUTCOMES = new Set<string>(Object.values(GROUP_DEACTIVATION_OUTCOME))

export function deactivationWarningPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/company-account/deactivation-warning`
}

export function groupDeactivationPreviewPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/group-deactivation-preview`
}

export function groupDeactivationPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/group-deactivation`
}

export interface DeactivationWarningItem {
  readonly kind: DeactivationWarningKind
  readonly outcome: DeactivationWarningOutcome
  /** Positive only for `open`; null otherwise. */
  readonly openItemCount: number | null
}

export interface DeactivationWarningView {
  readonly companyId: string
  readonly partyId: string
  /** The account version as read (lossless decimal string), informational: the command keeps its own fence. */
  readonly accountVersion: string
  readonly isActive: boolean
  readonly items: readonly DeactivationWarningItem[]
  readonly acknowledgementRequired: boolean
}

const VIEW_KEYS = ['accountVersion', 'acknowledgementRequired', 'companyId', 'isActive', 'items', 'partyId'] as const
const ITEM_KEYS = ['kind', 'openItemCount', 'outcome'] as const

const isObject = (raw: unknown): raw is Record<string, unknown> =>
  typeof raw === 'object' && raw !== null && !Array.isArray(raw)

function hasExactKeys(raw: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(raw).sort()
  return actual.length === keys.length && actual.every((key, index) => key === keys[index])
}

const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()

function decodeItem(raw: unknown, expectedKind: DeactivationWarningKind): DeactivationWarningItem | null {
  if (!isObject(raw) || !hasExactKeys(raw, ITEM_KEYS)) return null
  if (raw.kind !== expectedKind) return null
  if (typeof raw.outcome !== 'string' || !KNOWN_OUTCOMES.has(raw.outcome)) return null
  const outcome = raw.outcome as DeactivationWarningOutcome
  if (outcome === DEACTIVATION_WARNING_OUTCOME.open) {
    if (!isSafeIntish(raw.openItemCount) || raw.openItemCount < 1) return null
    return { kind: expectedKind, outcome, openItemCount: raw.openItemCount }
  }
  if (raw.openItemCount !== null) return null
  return { kind: expectedKind, outcome, openItemCount: null }
}

/** The read must answer for the party and company the request named; another record is not this one. */
export function decodeDeactivationWarningView(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): DeactivationWarningView | null {
  if (!isObject(raw) || !hasExactKeys(raw, VIEW_KEYS)) return null
  if (
    !isBoundUuid(raw.partyId) ||
    !sameId(raw.partyId, expected.partyId) ||
    !isBoundUuid(raw.companyId) ||
    !sameId(raw.companyId, expected.companyId) ||
    !isLosslessVersion(raw.accountVersion) ||
    typeof raw.isActive !== 'boolean' ||
    typeof raw.acknowledgementRequired !== 'boolean' ||
    !Array.isArray(raw.items) ||
    raw.items.length !== DEACTIVATION_WARNING_KINDS.length
  ) {
    return null
  }
  const items: DeactivationWarningItem[] = []
  for (let index = 0; index < DEACTIVATION_WARNING_KINDS.length; index += 1) {
    const item = decodeItem(raw.items[index], DEACTIVATION_WARNING_KINDS[index])
    if (item === null) return null
    items.push(item)
  }
  // The flag is derived from the lines; a server that says otherwise is not believed either way.
  const required = items.some((item) => item.outcome !== DEACTIVATION_WARNING_OUTCOME.clear)
  if (raw.acknowledgementRequired !== required) return null
  return {
    companyId: raw.companyId,
    partyId: raw.partyId,
    accountVersion: raw.accountVersion,
    isActive: raw.isActive,
    items,
    acknowledgementRequired: required,
  }
}

export interface GroupDeactivationPreviewCompany {
  readonly companyId: string
  readonly groupPermissionGranted: boolean
  readonly exposure: DeactivationWarningView
}

export interface GroupDeactivationPreview {
  readonly partyId: string
  readonly companies: readonly GroupDeactivationPreviewCompany[]
}

const PREVIEW_KEYS = ['companies', 'partyId'] as const
const PREVIEW_COMPANY_KEYS = ['companyId', 'exposure', 'groupPermissionGranted'] as const

export function decodeGroupDeactivationPreview(
  raw: unknown,
  expected: { readonly partyId: string },
): GroupDeactivationPreview | null {
  if (!isObject(raw) || !hasExactKeys(raw, PREVIEW_KEYS)) return null
  if (!isBoundUuid(raw.partyId) || !sameId(raw.partyId, expected.partyId) || !Array.isArray(raw.companies)) {
    return null
  }
  if (raw.companies.length === 0) return null
  const seen = new Set<string>()
  const companies: GroupDeactivationPreviewCompany[] = []
  for (const entry of raw.companies) {
    if (!isObject(entry) || !hasExactKeys(entry, PREVIEW_COMPANY_KEYS)) return null
    if (!isBoundUuid(entry.companyId) || typeof entry.groupPermissionGranted !== 'boolean') return null
    const key = entry.companyId.toLowerCase()
    if (seen.has(key)) return null
    seen.add(key)
    const exposure = decodeDeactivationWarningView(entry.exposure, {
      partyId: expected.partyId,
      companyId: entry.companyId,
    })
    if (exposure === null) return null
    companies.push({ companyId: entry.companyId, groupPermissionGranted: entry.groupPermissionGranted, exposure })
  }
  return { partyId: raw.partyId, companies }
}

export interface GroupDeactivationCompanyOutcome {
  readonly companyId: string
  readonly outcome: GroupDeactivationOutcome
  readonly replayed: boolean
  /** The account version on a version conflict (lossless decimal string); null otherwise. */
  readonly currentVersion: string | null
}

export interface GroupDeactivationResult {
  readonly partyId: string
  readonly companies: readonly GroupDeactivationCompanyOutcome[]
}

const RESULT_KEYS = ['companies', 'partyId'] as const
const RESULT_COMPANY_KEYS = ['companyId', 'currentVersion', 'outcome', 'replayed'] as const

export function decodeGroupDeactivationResult(
  raw: unknown,
  expected: { readonly partyId: string; readonly requestedCompanyIds: readonly string[] },
): GroupDeactivationResult | null {
  if (!isObject(raw) || !hasExactKeys(raw, RESULT_KEYS)) return null
  if (!isBoundUuid(raw.partyId) || !sameId(raw.partyId, expected.partyId) || !Array.isArray(raw.companies)) {
    return null
  }
  const requested = new Set(expected.requestedCompanyIds.map((id) => id.toLowerCase()))
  const seen = new Set<string>()
  const companies: GroupDeactivationCompanyOutcome[] = []
  for (const entry of raw.companies) {
    if (!isObject(entry) || !hasExactKeys(entry, RESULT_COMPANY_KEYS)) return null
    if (!isBoundUuid(entry.companyId) || typeof entry.replayed !== 'boolean') return null
    if (typeof entry.outcome !== 'string' || !KNOWN_GROUP_OUTCOMES.has(entry.outcome)) return null
    const outcome = entry.outcome as GroupDeactivationOutcome
    const key = entry.companyId.toLowerCase()
    if (seen.has(key)) return null
    seen.add(key)
    // A requested company is never reported as not requested, and an unrequested one only as that.
    if ((outcome === GROUP_DEACTIVATION_OUTCOME.notRequested) === requested.has(key)) return null
    let currentVersion: string | null = null
    if (entry.currentVersion !== null) {
      if (outcome !== GROUP_DEACTIVATION_OUTCOME.versionConflict || !isLosslessVersion(entry.currentVersion)) {
        return null
      }
      currentVersion = entry.currentVersion
    }
    companies.push({ companyId: entry.companyId, outcome, replayed: entry.replayed, currentVersion })
  }
  // Every requested company gets exactly one answer.
  for (const id of requested) if (!seen.has(id)) return null
  return { partyId: raw.partyId, companies }
}

export interface GroupDeactivationTarget {
  readonly companyId: string
  /** The account version the actor saw in the preview, carried verbatim as decimal text. */
  readonly expectedVersion: string
}

/** The command body, or null when it cannot be sent (no target, duplicate company, bad fence or reason). */
export function buildGroupDeactivationBody(
  targets: readonly GroupDeactivationTarget[],
  canonicalReasonText: string,
  acknowledged: boolean,
): {
  readonly targets: readonly { readonly companyId: string; readonly expectedVersion: string }[]
  readonly reason: string
  readonly exposureWarningAcknowledged: boolean
} | null {
  if (targets.length === 0 || canonicalReasonText.length === 0) return null
  const seen = new Set<string>()
  for (const target of targets) {
    if (!isBoundUuid(target.companyId) || !isLosslessVersion(target.expectedVersion)) return null
    const key = target.companyId.toLowerCase()
    if (seen.has(key)) return null
    seen.add(key)
  }
  return {
    targets: targets.map((target) => ({ companyId: target.companyId, expectedVersion: target.expectedVersion })),
    reason: canonicalReasonText,
    exposureWarningAcknowledged: acknowledged,
  }
}
