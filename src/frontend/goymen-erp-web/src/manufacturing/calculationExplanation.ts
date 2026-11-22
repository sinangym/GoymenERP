/**
 * WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): the order's production cost and mass balance explained.
 *   GET /api/v1/manufacturing/orders/{orderId}/production-cost/explanation → ProductionCostExplanationViewV1 (reports grant)
 *   GET /api/v1/manufacturing/orders/{orderId}/mass-balance/explanation    → MassBalanceExplanationViewV1 (order read)
 *
 * Every value is the server calculator's own (the production cost calculator's trace from the same run, the recorded kg
 * cost, GDC-BAL/1); the screen shows the text and never sums, rounds or recomputes. Strict decoders fail closed: exact keys,
 * known step codes, operands that are earlier steps, the asked order, and the last step equal to the shown figure.
 */

import { MANUFACTURING_BASE, devHeaders } from './api'
import { decodeCurrencyLine, type OrderCostCurrencyLine } from './orderCostContract'
import type { ViewOutcome } from '../shell/uiState'

export const PRODUCTION_COST_STEP_CODES = ['net_input_cost', 'consumed_kg', 'raw_material_cost_per_kg', 'usable_remnant_kg',
  'usable_remnant_value', 'weighed_scrap_value', 'good_product_kg', 'second_quality_kg', 'produced_kg', 'cost_numerator',
  'cost_per_kg_exact', 'cost_per_kg_recorded'] as const
export const MASS_BALANCE_STEP_CODES = ['issued_kg', 'returned_kg', 'actual_input_kg', 'good_product_kg', 'second_quality_kg',
  'usable_remnant_kg', 'scrap_kg', 'process_loss_kg', 'work_in_progress_kg', 'measurement_correction_kg', 'accounted_kg',
  'difference_kg'] as const
/** Operand names that are not a step of the trace but a list beside it (the issue amounts). */
const LIST_OPERANDS = ['issue_amounts']
const OPERATIONS = ['input', 'sum', 'add', 'subtract', 'multiply', 'divide', 'round_half_away_from_zero']
const UNITS = ['kg', 'TL', 'TL/kg']

export interface ManufacturingTraceStep {
  readonly code: string
  readonly operation: string
  readonly operands: readonly string[]
  readonly value: string
  readonly unit: string
  readonly scale: number | null
}

export interface ProductionCostExplanationIssue {
  readonly issueMovementId: string
  readonly issuedKg: string
  readonly returnedKg: string
  readonly keptKg: string
  readonly tlUnitCostPerKg: string
  readonly amountTl: string
}

export interface ProductionCostExplanationScrap {
  readonly scrapOutputId: string
  readonly kilograms: string
  readonly tlPerKg: string | null
  readonly valueTl: string
}

export interface ProductionCostExplanation {
  readonly orderId: string
  readonly orderVersion: number
  readonly status: 'available' | 'pending' | 'unreadable' | 'unavailable'
  readonly reasonCode: string | null
  readonly costBasis: string | null
  readonly calculatorVersion: string
  readonly displayedCostPerKgTl: string | null
  readonly steps: readonly ManufacturingTraceStep[]
  readonly issues: readonly ProductionCostExplanationIssue[]
  readonly scrapWeighings: readonly ProductionCostExplanationScrap[]
  readonly currencyLines: readonly OrderCostCurrencyLine[]
  readonly ruleReferences: readonly string[]
}

export interface RollMassBalanceExplanation {
  readonly parentRollId: string
  readonly displayedDifferenceKg: string
  readonly steps: readonly ManufacturingTraceStep[]
  readonly issueIds: readonly string[]
  readonly returnIds: readonly string[]
  readonly outputIds: readonly string[]
}

export interface MassBalanceExplanation {
  readonly orderId: string
  readonly version: number
  readonly formulaVersion: string
  readonly rolls: readonly RollMassBalanceExplanation[]
  readonly ruleReferences: readonly string[]
}

type Json = Record<string, unknown>
const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
/** The server's `Text(decimal)`, signed (a mass difference, a numerator) — no grouping, exponent or trailing zero. */
const SIGNED = /^-?(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const AMOUNT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const CODE = /^[a-z][a-z0-9]*([._-][a-z0-9]+)*$/

function exact(value: unknown, keys: readonly string[]): Json | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const obj = value as Json
  if (Object.keys(obj).length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const isGuid = (v: unknown): v is string => typeof v === 'string' && GUID.test(v)
const isAmount = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && AMOUNT.test(v)
const isTexts = (v: unknown): v is string[] => Array.isArray(v) && v.every((item) => typeof item === 'string' && item.length > 0)
const isGuids = (v: unknown): v is string[] => Array.isArray(v) && v.every(isGuid)

function decodeSteps(value: unknown, codes: readonly string[]): ManufacturingTraceStep[] | null {
  if (!Array.isArray(value)) return null
  const seen = new Set<string>()
  const steps: ManufacturingTraceStep[] = []
  for (const raw of value) {
    const o = exact(raw, ['code', 'operation', 'operands', 'value', 'unit', 'scale'])
    if (o === null || typeof o.code !== 'string' || !codes.includes(o.code) || seen.has(o.code)
      || typeof o.operation !== 'string' || !OPERATIONS.includes(o.operation) || !Array.isArray(o.operands)
      || !o.operands.every((operand) => typeof operand === 'string' && (seen.has(operand) || LIST_OPERANDS.includes(operand)))
      || typeof o.value !== 'string' || o.value.length > 64 || !SIGNED.test(o.value)
      || typeof o.unit !== 'string' || !UNITS.includes(o.unit)
      || !(o.scale === null || (typeof o.scale === 'number' && Number.isInteger(o.scale) && o.scale >= 0 && o.scale <= 28))) return null
    seen.add(o.code)
    steps.push(o as unknown as ManufacturingTraceStep)
  }
  return steps
}

export function decodeProductionCostExplanation(value: unknown, orderId: string): ProductionCostExplanation | null {
  const o = exact(value, ['contractVersion', 'orderId', 'orderVersion', 'status', 'reasonCode', 'costBasis', 'calculatorVersion',
    'displayedCostPerKgTl', 'steps', 'issues', 'scrapWeighings', 'currencyLines', 'ruleReferences'])
  if (o === null || o.contractVersion !== 1 || o.orderId !== orderId || typeof o.orderVersion !== 'number'
    || !Number.isInteger(o.orderVersion) || o.orderVersion < 0
    || !['available', 'pending', 'unreadable', 'unavailable'].includes(o.status as string)
    || typeof o.calculatorVersion !== 'string' || o.calculatorVersion.length === 0 || !isTexts(o.ruleReferences)) return null
  const steps = decodeSteps(o.steps, PRODUCTION_COST_STEP_CODES)
  if (steps === null || !Array.isArray(o.issues) || !Array.isArray(o.scrapWeighings) || !Array.isArray(o.currencyLines)) return null
  const issues: ProductionCostExplanationIssue[] = []
  for (const raw of o.issues) {
    const issue = exact(raw, ['issueMovementId', 'issuedKg', 'returnedKg', 'keptKg', 'tlUnitCostPerKg', 'amountTl'])
    if (issue === null || !isGuid(issue.issueMovementId) || !isAmount(issue.issuedKg) || !isAmount(issue.returnedKg)
      || !isAmount(issue.keptKg) || !isAmount(issue.tlUnitCostPerKg) || !isAmount(issue.amountTl)) return null
    issues.push(issue as unknown as ProductionCostExplanationIssue)
  }
  const scrap: ProductionCostExplanationScrap[] = []
  for (const raw of o.scrapWeighings) {
    const weighing = exact(raw, ['scrapOutputId', 'kilograms', 'tlPerKg', 'valueTl'])
    if (weighing === null || !isGuid(weighing.scrapOutputId) || !isAmount(weighing.kilograms)
      || !(weighing.tlPerKg === null || isAmount(weighing.tlPerKg)) || !isAmount(weighing.valueTl)) return null
    scrap.push(weighing as unknown as ProductionCostExplanationScrap)
  }
  const currencyLines: OrderCostCurrencyLine[] = []
  for (const raw of o.currencyLines) {
    const line = decodeCurrencyLine(raw)
    if (line === null) return null
    currencyLines.push(line)
  }
  if (o.status === 'available') {
    // Figures only with available, no reason beside them, and the last step IS the shown figure.
    if (o.reasonCode !== null || !isAmount(o.displayedCostPerKgTl) || steps.length === 0 || issues.length === 0
      || steps[steps.length - 1].code !== 'cost_per_kg_recorded' || steps[steps.length - 1].value !== o.displayedCostPerKgTl
      || !(o.costBasis === null || (typeof o.costBasis === 'string' && CODE.test(o.costBasis)))) return null
  } else if (typeof o.reasonCode !== 'string' || !CODE.test(o.reasonCode) || o.displayedCostPerKgTl !== null || o.costBasis !== null
    || steps.length > 0 || issues.length > 0 || scrap.length > 0 || currencyLines.length > 0) return null
  return {
    orderId, orderVersion: o.orderVersion, status: o.status as ProductionCostExplanation['status'],
    reasonCode: o.reasonCode as string | null, costBasis: o.costBasis as string | null, calculatorVersion: o.calculatorVersion,
    displayedCostPerKgTl: o.displayedCostPerKgTl as string | null, steps, issues, scrapWeighings: scrap, currencyLines,
    ruleReferences: o.ruleReferences,
  }
}

export function decodeMassBalanceExplanation(value: unknown, orderId: string): MassBalanceExplanation | null {
  const o = exact(value, ['contractVersion', 'orderId', 'version', 'formulaVersion', 'rolls', 'ruleReferences'])
  if (o === null || o.contractVersion !== 1 || o.orderId !== orderId || typeof o.version !== 'number'
    || !Number.isInteger(o.version) || typeof o.formulaVersion !== 'string' || o.formulaVersion.length === 0
    || !Array.isArray(o.rolls) || !isTexts(o.ruleReferences)) return null
  const rolls: RollMassBalanceExplanation[] = []
  for (const raw of o.rolls) {
    const roll = exact(raw, ['parentRollId', 'displayedDifferenceKg', 'steps', 'issueIds', 'returnIds', 'outputIds'])
    if (roll === null || !isGuid(roll.parentRollId) || typeof roll.displayedDifferenceKg !== 'string'
      || !SIGNED.test(roll.displayedDifferenceKg) || !isGuids(roll.issueIds) || !isGuids(roll.returnIds)
      || !isGuids(roll.outputIds)) return null
    const steps = decodeSteps(roll.steps, MASS_BALANCE_STEP_CODES)
    if (steps === null || steps.length === 0 || steps[steps.length - 1].code !== 'difference_kg'
      || steps[steps.length - 1].value !== roll.displayedDifferenceKg) return null
    rolls.push({ parentRollId: roll.parentRollId, displayedDifferenceKg: roll.displayedDifferenceKg, steps,
      issueIds: roll.issueIds, returnIds: roll.returnIds, outputIds: roll.outputIds })
  }
  return { orderId, version: o.version, formulaVersion: o.formulaVersion, rolls, ruleReferences: o.ruleReferences }
}

async function read<T>(path: string, companyId: string, decode: (body: unknown) => T | null,
  signal?: AbortSignal): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(path, { headers: { ...devHeaders(), 'X-Company-Id': companyId }, cache: 'no-store', signal })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decode(body)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

export function fetchProductionCostExplanation(companyId: string, orderId: string, signal?: AbortSignal) {
  return read(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/production-cost/explanation`, companyId,
    (body) => decodeProductionCostExplanation(body, orderId), signal)
}

export function fetchMassBalanceExplanation(companyId: string, orderId: string, signal?: AbortSignal) {
  return read(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(orderId)}/mass-balance/explanation`, companyId,
    (body) => decodeMassBalanceExplanation(body, orderId), signal)
}
