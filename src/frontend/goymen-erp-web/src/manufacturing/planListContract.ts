/**
 * Wire contract of `GET /api/v1/manufacturing/plans` (`PlanListViewV1`, MFG-PLAN-LIST).
 *
 * One page of the company's stored plans, newest first: the plan's own row and the count and planned mass of its
 * planned inputs. No engine answer, no slitting spec, no roll label and no order the plan may have opened — one
 * approved plan may open more than one order (#66 5728934213 is open), so this contract has no "converted" state and
 * the screen never infers one.
 *
 * Decoding fails CLOSED and is strict: every summary must carry exactly the contract's fields, every kilogram is the
 * server's lossless invariant text, and the approval time is bound to the status (a Draft has none, an Approved plan
 * has one). A page with one row that is not this contract is `null`, and the screen says the answer could not be
 * read — a half-trusted list would put one plan's figures beside another's.
 *
 * Kept in its own file beside `./contract.ts` on purpose: the list's decoder is additive and shares no mutable state
 * with the order and plan decoders there.
 */

import type { PlanStatus } from './contract'

export const PLAN_STATUSES: readonly PlanStatus[] = ['Draft', 'Approved']

export interface PlanSummary {
  readonly planId: string
  readonly workCenterId: string
  readonly process: string
  readonly status: PlanStatus
  /** The version the plan detail shows; approval names the version the detail read, never this one. */
  readonly version: number
  readonly plannedRollCount: number
  readonly plannedInputKg: string
  readonly target: { readonly basis: string; readonly kg: string } | null
  readonly createdAtUtc: string
  readonly approvedAtUtc: string | null
}

/** `nextCursor === null` is the end of the list; the cursor is opaque and is sent back unchanged. */
export interface PlanListView {
  readonly plans: readonly PlanSummary[]
  readonly nextCursor: string | null
}

const SUMMARY_KEYS = [
  'planId',
  'workCenterId',
  'process',
  'status',
  'version',
  'plannedRollCount',
  'plannedInputKg',
  'target',
  'createdAtUtc',
  'approvedAtUtc',
] as const

const PAGE_KEYS = ['plans', 'nextCursor'] as const
const TARGET_KEYS = ['basis', 'kg'] as const

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent, no trailing fractional zero. */
const KG_OUT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
/** A process or basis name as the server writes an enum: letters only, never a number. */
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

function kg(value: unknown): string | null {
  return typeof value === 'string' && KG_OUT.test(value) ? value : null
}

/** A positive safe integer: shown and compared, never computed. */
function positive(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null
}

function instant(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 && !Number.isNaN(Date.parse(value)) ? value : null
}

/** `undefined` = not a contract value; `null` = the contract's own "no target". */
function target(value: unknown): PlanSummary['target'] | undefined {
  if (value === null) return null
  if (!isObject(value) || !hasExactKeys(value, TARGET_KEYS)) return undefined
  const mass = kg(value.kg)
  return typeof value.basis === 'string' && ENUM_NAME.test(value.basis) && mass !== null
    ? { basis: value.basis, kg: mass }
    : undefined
}

function decodePlanSummary(value: unknown): PlanSummary | null {
  if (!isObject(value) || !hasExactKeys(value, SUMMARY_KEYS)) return null
  const planId = nonNilGuid(value.planId)
  const workCenterId = nonNilGuid(value.workCenterId)
  const version = positive(value.version)
  // A stored plan always has at least one planned input.
  const plannedRollCount = positive(value.plannedRollCount)
  const plannedInputKg = kg(value.plannedInputKg)
  const planTarget = target(value.target)
  const createdAtUtc = instant(value.createdAtUtc)
  const approvedAtUtc = value.approvedAtUtc === null ? null : instant(value.approvedAtUtc)
  if (
    planId === null ||
    workCenterId === null ||
    version === null ||
    plannedRollCount === null ||
    plannedInputKg === null ||
    planTarget === undefined ||
    createdAtUtc === null ||
    typeof value.process !== 'string' ||
    !ENUM_NAME.test(value.process) ||
    (value.status !== 'Draft' && value.status !== 'Approved')
  ) {
    return null
  }
  // A non-null approval time that is not an instant is not the contract, and neither is a half-told status:
  // a Draft has no approval time and an Approved plan has one. The screen never infers one from the other.
  if (value.approvedAtUtc !== null && approvedAtUtc === null) return null
  if ((value.status === 'Approved') !== (approvedAtUtc !== null)) return null
  return {
    planId,
    workCenterId,
    process: value.process,
    status: value.status,
    version,
    plannedRollCount,
    plannedInputKg,
    target: planTarget,
    createdAtUtc,
    approvedAtUtc,
  }
}

export function decodePlanList(body: unknown): PlanListView | null {
  if (!isObject(body) || !hasExactKeys(body, PAGE_KEYS) || !Array.isArray(body.plans)) return null
  const nextCursor =
    body.nextCursor === null
      ? null
      : typeof body.nextCursor === 'string' && body.nextCursor.length > 0
        ? body.nextCursor
        : undefined
  if (nextCursor === undefined) return null
  const plans: PlanSummary[] = []
  const seen = new Set<string>()
  for (const raw of body.plans) {
    const summary = decodePlanSummary(raw)
    // One page names each plan once; a repeated id is not this contract.
    if (summary === null || seen.has(summary.planId.toLowerCase())) return null
    seen.add(summary.planId.toLowerCase())
    plans.push(summary)
  }
  return { plans, nextCursor }
}
