/**
 * Wire contract of `GET /api/v1/manufacturing/plans/{planId}/revisions` (`PlanRevisionListViewV1`, MFG-PLAN-EDIT).
 *
 * A plan's history, oldest first. Revision 0 is the plan as created (version 1, no reason, no reviser); every later
 * revision is one recorded Draft edit with the COMPLETE content it set — work center, planned inputs with the
 * Inventory label version accepted for each, target — the plan version it produced, the user's reason, the actor id
 * that recorded it and when. The last entry is the plan's current content, which is what the edit form starts from.
 *
 * Decoding fails CLOSED and is strict, as the plan list's: exact keys, lossless kilogram text, contiguous revision
 * numbers from 0 with strictly increasing versions from 1, and the reason/reviser pair bound to the revision (absent
 * on 0, present on every edit). A history with one entry that is not this contract is `null` — a half-trusted history
 * would show one edit's content under another's reason.
 */

import { decodeTechnicalSnapshot, type CapturedPlanTechnicalSnapshot } from './contract'

export interface PlanRevisionInput {
  readonly parentRollId: string
  readonly plannedKg: string
  /** Canonical positive integer text, or null for a plan stored before label versions were kept. */
  readonly expectedLabelVersion: string | null
  /** Exact immutable Counterparty provenance captured with the input; null also represents a legacy omission. */
  readonly commercialContext: CapturedPlanCommercialContext | null
  /** Product/Settings facts captured with this historical input; null represents a legacy omission. */
  readonly technicalSnapshot: CapturedPlanTechnicalSnapshot | null
}

export interface CapturedPlanCommercialContext {
  readonly partyId: string
  readonly companyPartyAccountId: string
  readonly partyVersion: string
  readonly accountVersion: string
}

export interface PlanRevision {
  readonly revision: number
  readonly planVersion: number
  readonly workCenterId: string
  readonly inputs: readonly PlanRevisionInput[]
  readonly target: { readonly basis: string; readonly kg: string } | null
  readonly reason: string | null
  readonly revisedBy: string | null
  readonly recordedAtUtc: string
}

export interface PlanRevisionList {
  readonly planId: string
  readonly revisions: readonly PlanRevision[]
}

const LIST_KEYS = ['planId', 'revisions'] as const
const REVISION_KEYS = [
  'revision',
  'planVersion',
  'workCenterId',
  'inputs',
  'target',
  'reason',
  'revisedBy',
  'recordedAtUtc',
] as const
const LEGACY_INPUT_KEYS = ['parentRollId', 'plannedKg', 'expectedLabelVersion'] as const
const COMMERCIAL_INPUT_KEYS = [...LEGACY_INPUT_KEYS, 'commercialContext'] as const
const INPUT_KEYS = [...COMMERCIAL_INPUT_KEYS, 'technicalSnapshot'] as const
const COMMERCIAL_CONTEXT_KEYS = ['partyId', 'companyPartyAccountId', 'partyVersion', 'accountVersion'] as const
const TARGET_KEYS = ['basis', 'kg'] as const

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent, no trailing fractional zero. */
const KG_OUT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const VERSION_TEXT = /^[1-9][0-9]*$/
const ENUM_NAME = /^[A-Z][A-Za-z]*$/

type Json = Record<string, unknown>

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Json, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function nonNilGuid(value: unknown): string | null {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID ? value : null
}

function positiveKg(value: unknown): string | null {
  return typeof value === 'string' && KG_OUT.test(value) && /[1-9]/.test(value) ? value : null
}

function wholeNumber(value: unknown, minimum: number): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum ? value : null
}

/** `undefined` is malformed; `null` is the current wire's explicit absence. Opaque versions stay byte-for-byte. */
function decodeCommercialContext(value: unknown): CapturedPlanCommercialContext | null | undefined {
  if (value === null) return null
  if (!isObject(value) || !hasExactKeys(value, COMMERCIAL_CONTEXT_KEYS)) return undefined
  const partyId = nonNilGuid(value.partyId)
  const companyPartyAccountId = nonNilGuid(value.companyPartyAccountId)
  if (
    partyId === null ||
    companyPartyAccountId === null ||
    typeof value.partyVersion !== 'string' || value.partyVersion.length === 0 ||
    typeof value.accountVersion !== 'string' || value.accountVersion.length === 0
  ) return undefined
  return { partyId, companyPartyAccountId, partyVersion: value.partyVersion, accountVersion: value.accountVersion }
}

function decodeInput(value: unknown): PlanRevisionInput | null {
  if (!isObject(value)) return null
  const isCurrent = hasExactKeys(value, INPUT_KEYS)
  const hasCommercialContext = isCurrent || hasExactKeys(value, COMMERCIAL_INPUT_KEYS)
  if (!hasCommercialContext && !hasExactKeys(value, LEGACY_INPUT_KEYS)) return null
  const parentRollId = nonNilGuid(value.parentRollId)
  const plannedKg = positiveKg(value.plannedKg)
  const label = value.expectedLabelVersion
  const expectedLabelVersion =
    label === null ? null : typeof label === 'string' && VERSION_TEXT.test(label) ? label : undefined
  // Old stored history JSON omitted this additive member; current JSON always emits it, including explicit null.
  const commercialContext = hasCommercialContext ? decodeCommercialContext(value.commercialContext) : null
  const technicalSnapshot = isCurrent
    ? value.technicalSnapshot === null ? null : decodeTechnicalSnapshot(value.technicalSnapshot)
    : null
  if (
    parentRollId === null || plannedKg === null || expectedLabelVersion === undefined ||
    commercialContext === undefined || technicalSnapshot === undefined
  ) return null
  return { parentRollId, plannedKg, expectedLabelVersion, commercialContext, technicalSnapshot }
}

function decodeTarget(value: unknown): PlanRevision['target'] | undefined {
  if (value === null) return null
  if (!isObject(value) || !hasExactKeys(value, TARGET_KEYS)) return undefined
  const kg = positiveKg(value.kg)
  return typeof value.basis === 'string' && ENUM_NAME.test(value.basis) && kg !== null
    ? { basis: value.basis, kg }
    : undefined
}

function decodeRevision(value: unknown, expectedRevision: number): PlanRevision | null {
  if (!isObject(value) || !hasExactKeys(value, REVISION_KEYS)) return null
  const revision = wholeNumber(value.revision, 0)
  const planVersion = wholeNumber(value.planVersion, 1)
  const workCenterId = nonNilGuid(value.workCenterId)
  const target = decodeTarget(value.target)
  const recordedAtUtc =
    typeof value.recordedAtUtc === 'string' && !Number.isNaN(Date.parse(value.recordedAtUtc))
      ? value.recordedAtUtc
      : null
  if (
    revision !== expectedRevision ||
    planVersion === null ||
    workCenterId === null ||
    target === undefined ||
    recordedAtUtc === null ||
    !Array.isArray(value.inputs) ||
    value.inputs.length === 0
  ) {
    return null
  }
  // Revision 0 is the plan as created: no reason and no reviser. Every edit carries both.
  const reason = value.reason
  const revisedBy = value.revisedBy
  if (revision === 0) {
    if (reason !== null || revisedBy !== null || planVersion !== 1) return null
  } else if (
    typeof reason !== 'string' || reason.trim().length === 0 ||
    typeof revisedBy !== 'string' || revisedBy.trim().length === 0
  ) {
    return null
  }
  const inputs: PlanRevisionInput[] = []
  const rolls = new Set<string>()
  for (const raw of value.inputs) {
    const input = decodeInput(raw)
    if (input === null || rolls.has(input.parentRollId.toLowerCase())) return null
    rolls.add(input.parentRollId.toLowerCase())
    inputs.push(input)
  }
  return {
    revision,
    planVersion,
    workCenterId,
    inputs,
    target,
    reason: revision === 0 ? null : (reason as string),
    revisedBy: revision === 0 ? null : (revisedBy as string),
    recordedAtUtc,
  }
}

/** Fails closed: `null` unless every entry is this contract and the entries form one ordered history. */
export function decodePlanRevisions(body: unknown): PlanRevisionList | null {
  if (!isObject(body) || !hasExactKeys(body, LIST_KEYS)) return null
  const planId = nonNilGuid(body.planId)
  if (planId === null || !Array.isArray(body.revisions) || body.revisions.length === 0) return null
  const revisions: PlanRevision[] = []
  for (const [index, raw] of body.revisions.entries()) {
    const revision = decodeRevision(raw, index)
    if (revision === null) return null
    const previous = revisions.at(-1)
    if (previous !== undefined && revision.planVersion <= previous.planVersion) return null
    revisions.push(revision)
  }
  return { planId, revisions }
}

/** The plan's current content: the last history entry. */
export function currentRevision(history: PlanRevisionList): PlanRevision {
  return history.revisions[history.revisions.length - 1]
}
