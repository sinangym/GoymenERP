/**
 * WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): one line's margin explained.
 *   GET /api/v1/sales/drafts/{id}/lines/{lineId}/margin-explanation → SalesDraftLineMarginExplanationV1
 * (draft read, price read and the separate `sales.orders.margin.read`; a withheld margin is 403, never an empty trace).
 *
 * The margin is the line-pricing read's very margin (the same server resolution and calculator run); the steps are that
 * run's exact intermediate values. The screen shows them as text and never recomputes. Exact-member decoders: a missing or
 * extra member, an unknown step code, an operand that is not an earlier step, or a margin of another line is not this
 * contract (fail closed).
 */

import { decodeLineMargin, type LineMargin } from './linePricing'
import { headers, readFailure, request } from './api'
import { salesDraftDetailPath } from './contract'
import type { ViewOutcome } from '../shell/uiState'

export const MARGIN_EXPLANATION_CONTRACT_VERSION = 1

export const MARGIN_STEP_CODES = ['net_sale', 'sale_rate', 'net_sale_functional', 'unit_cost', 'kilograms_per_unit',
  'cost_per_order_unit', 'quantity', 'total_cost', 'margin_exact', 'margin_amount', 'margin_percent'] as const
export type MarginStepCode = (typeof MARGIN_STEP_CODES)[number]

export const MARGIN_SOURCE_KINDS = ['sales-order-line', 'inventory-cost-observation', 'margin-cost-snapshot', 'sale-rate',
  'kilograms-per-unit'] as const
export type MarginSourceKind = (typeof MARGIN_SOURCE_KINDS)[number]

export interface MarginTraceStep {
  readonly code: MarginStepCode
  readonly operation: string
  readonly operands: readonly MarginStepCode[]
  readonly value: string
  readonly currencyId: string | null
  readonly unitId: string | null
  readonly scale: number | null
}

export interface MarginTraceSource {
  readonly kind: MarginSourceKind
  readonly recordId: string | null
  readonly revision: number | null
  readonly reference: string | null
  readonly recordedAtUtc: string | null
  readonly detail: string | null
}

export interface MarginExplanation {
  readonly draftId: string
  readonly version: string
  readonly kind: 'Preview' | 'Snapshot'
  readonly calculatorVersion: string
  readonly margin: LineMargin
  readonly steps: readonly MarginTraceStep[]
  readonly sources: readonly MarginTraceSource[]
  readonly ruleReferences: readonly string[]
}

export const salesDraftLineMarginExplanationPath = (id: string, lineId: string) =>
  `${salesDraftDetailPath(id)}/lines/${encodeURIComponent(lineId)}/margin-explanation`

type Obj = Record<string, unknown>
const OPERATIONS = ['input', 'multiply', 'subtract', 'round_half_away_from_zero', 'percent_of']
const SIGNED_DECIMAL = /^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Obj
  if (Object.keys(obj).length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const isGuidOrNull = (v: unknown) => v === null || (typeof v === 'string' && GUID.test(v))
const isTextOrNull = (v: unknown, max: number) => v === null || (typeof v === 'string' && v.length > 0 && v.length <= max)
const isStepCode = (v: unknown): v is MarginStepCode => typeof v === 'string' && (MARGIN_STEP_CODES as readonly string[]).includes(v)

function decodeSteps(value: unknown): MarginTraceStep[] | null {
  if (!Array.isArray(value)) return null
  const seen = new Set<string>()
  const steps: MarginTraceStep[] = []
  for (const raw of value) {
    const o = exact(raw, ['code', 'operation', 'operands', 'value', 'currencyId', 'unitId', 'scale'])
    if (o === null || !isStepCode(o.code) || seen.has(o.code) || typeof o.operation !== 'string'
      || !OPERATIONS.includes(o.operation) || !Array.isArray(o.operands)
      || !o.operands.every((operand) => isStepCode(operand) && seen.has(operand))
      || typeof o.value !== 'string' || o.value.length > 128 || !SIGNED_DECIMAL.test(o.value)
      || !isGuidOrNull(o.currencyId) || !isGuidOrNull(o.unitId)
      || !(o.scale === null || (typeof o.scale === 'number' && Number.isInteger(o.scale) && o.scale >= 0 && o.scale <= 28))) return null
    seen.add(o.code)
    steps.push(o as unknown as MarginTraceStep)
  }
  return steps
}

function decodeSources(value: unknown): MarginTraceSource[] | null {
  if (!Array.isArray(value) || value.length === 0) return null
  const sources: MarginTraceSource[] = []
  for (const raw of value) {
    const o = exact(raw, ['kind', 'recordId', 'revision', 'reference', 'recordedAtUtc', 'detail'])
    if (o === null || typeof o.kind !== 'string' || !(MARGIN_SOURCE_KINDS as readonly string[]).includes(o.kind)
      || !isGuidOrNull(o.recordId)
      || !(o.revision === null || (typeof o.revision === 'number' && Number.isInteger(o.revision) && o.revision >= 0))
      || !isTextOrNull(o.reference, 256) || !isTextOrNull(o.detail, 65536)
      || !(o.recordedAtUtc === null || (typeof o.recordedAtUtc === 'string' && !Number.isNaN(Date.parse(o.recordedAtUtc))))) return null
    sources.push(o as unknown as MarginTraceSource)
  }
  return sources[0].kind === 'sales-order-line' ? sources : null
}

/** The explanation for exactly the asked draft and line; anything else is not this contract. */
export function decodeMarginExplanation(value: unknown, draftId: string, lineId: string): MarginExplanation | null {
  const o = exact(value, ['contractVersion', 'draftId', 'version', 'kind', 'calculatorVersion', 'margin', 'steps', 'sources',
    'ruleReferences'])
  if (o === null || o.contractVersion !== MARGIN_EXPLANATION_CONTRACT_VERSION || o.draftId !== draftId
    || typeof o.version !== 'string' || !/^[0-9]+$/.test(o.version) || (o.kind !== 'Preview' && o.kind !== 'Snapshot')
    || typeof o.calculatorVersion !== 'string' || o.calculatorVersion.length === 0) return null
  const margin = decodeLineMargin(o.margin)
  const steps = decodeSteps(o.steps)
  const sources = decodeSources(o.sources)
  if (margin === null || margin.lineId !== lineId || steps === null || sources === null) return null
  if (!Array.isArray(o.ruleReferences) || !o.ruleReferences.every((rule) => typeof rule === 'string' && rule.length > 0)) return null
  // A computed margin's trace ends on the shown amount; an unavailable one shows no amount step.
  const amountStep = steps.find((step) => step.code === 'margin_amount')
  if (margin.state === 'Computed' ? amountStep?.value !== margin.amount : amountStep !== undefined) return null
  return { draftId: o.draftId, version: o.version, kind: o.kind, calculatorVersion: o.calculatorVersion, margin, steps, sources,
    ruleReferences: o.ruleReferences as string[] }
}

export async function fetchMarginExplanation(
  draftId: string,
  lineId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<MarginExplanation>> {
  const raw = await request(salesDraftLineMarginExplanationPath(draftId, lineId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const explanation = decodeMarginExplanation(raw.body, draftId, lineId)
  return explanation === null ? { type: 'malformed' } : { type: 'ok-single', data: explanation }
}
