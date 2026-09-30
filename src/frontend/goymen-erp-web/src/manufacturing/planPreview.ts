/**
 * AT-061 / AT-062 — `POST /api/v1/manufacturing/plans/preview` (`PlanPreviewViewV1`, DEC-PRDCT-005).
 *
 * The server answers a plan content with its ONE plan calculator (`GDC-PLAN-CALC/1`), the same function that answers
 * for the plan the save stores from that content. This file only carries and decodes the answer: no formula, density,
 * speed, kerf, tolerance or rounding lives in the screen (AGENTS: formula / UI / report use the same backend
 * calculator). A missing parameter is shown as the server names it; nothing is guessed.
 *
 * The preview writes nothing (no Idempotency-Key, no command record). Decoding fails CLOSED: an answer that is not
 * this contract is `malformed`, never half-shown.
 */

import { MANUFACTURING_BASE, devHeaders } from './api'
import type { PlanTargetBasis } from './planCommandApi'

export type PreviewOutcomeName = 'Computed' | 'Unavailable' | 'Refused'

export interface PlanCalculatedValue {
  readonly name: string
  readonly value: string
}

export interface PlanFormulaAnswer {
  readonly formula: string
  readonly formulaVersion: string
  readonly outcome: PreviewOutcomeName
  readonly values: readonly PlanCalculatedValue[]
  readonly missingParameters: readonly string[]
  readonly refusalCode: string | null
  readonly refusedParameter: string | null
}

export interface PlanRollPreview {
  readonly parentRollId: string
  readonly plannedKg: string
  readonly answers: readonly PlanFormulaAnswer[]
}

export interface PlanTargetPreview {
  readonly basis: string
  readonly kg: string
  readonly outcome: PreviewOutcomeName
  readonly targetMinusPlannedInputKg: string | null
  readonly missingParameters: readonly string[]
}

export interface PlanPreview {
  readonly calculatorVersion: string
  readonly workCenterId: string
  readonly process: string
  readonly plannedRollCount: number
  readonly plannedInputKg: string
  readonly target: PlanTargetPreview | null
  readonly answers: readonly PlanFormulaAnswer[]
  readonly rolls: readonly PlanRollPreview[]
  readonly missingParameters: readonly string[]
  readonly hasRefusal: boolean
}

/** The content to preview: the plan save's body without a plan id (nothing is created). */
export interface PlanPreviewRequest {
  readonly workCenterId: string
  readonly inputs: readonly {
    readonly parentRollId: string
    readonly plannedKg: string
    readonly expectedLabelVersion: string
  }[]
  readonly target: { readonly basis: PlanTargetBasis; readonly kg: string } | null
}

export type PlanPreviewOutcome =
  | { readonly type: 'answered'; readonly preview: PlanPreview }
  /** The save's own refusal for the same content: the reason and, when the server names one, the field. */
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly field: string | null }
  | { readonly type: 'failed'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed' }
  | { readonly type: 'aborted' }

type Json = Record<string, unknown>

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
/** The server's own lossless decimal text (a target comparison may be negative). */
const DECIMAL_OUT = /^-?(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const KG_OUT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const OUTCOMES: readonly PreviewOutcomeName[] = ['Computed', 'Unavailable', 'Refused']

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null
}

function nullableText(value: unknown): string | null | undefined {
  if (value === null) return null
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function outcome(value: unknown): PreviewOutcomeName | null {
  return OUTCOMES.find((name) => name === value) ?? null
}

function names(value: unknown): string[] | null {
  if (!Array.isArray(value)) return null
  const result: string[] = []
  for (const item of value) {
    if (typeof item !== 'string' || item.length === 0) return null
    result.push(item)
  }
  return result
}

function decodeAnswer(value: unknown): PlanFormulaAnswer | null {
  if (!isObject(value)) return null
  const formula = text(value.formula)
  const formulaVersion = text(value.formulaVersion)
  const answered = outcome(value.outcome)
  const missing = names(value.missingParameters)
  const refusalCode = nullableText(value.refusalCode)
  const refusedParameter = nullableText(value.refusedParameter)
  if (!Array.isArray(value.values)) return null
  const values: PlanCalculatedValue[] = []
  for (const item of value.values) {
    if (!isObject(item)) return null
    const name = text(item.name)
    const number = text(item.value)
    if (name === null || number === null || !DECIMAL_OUT.test(number)) return null
    values.push({ name, value: number })
  }
  if (
    formula === null || formulaVersion === null || answered === null || missing === null ||
    refusalCode === undefined || refusedParameter === undefined
  ) {
    return null
  }
  // The outcome and its content travel together: a number only when computed, a code only when refused.
  if (answered === 'Computed' && (values.length === 0 || missing.length > 0 || refusalCode !== null)) return null
  if (answered === 'Unavailable' && (values.length > 0 || missing.length === 0 || refusalCode !== null)) return null
  if (answered === 'Refused' && (values.length > 0 || refusalCode === null)) return null
  return { formula, formulaVersion, outcome: answered, values, missingParameters: missing, refusalCode, refusedParameter }
}

function decodeAnswers(value: unknown): PlanFormulaAnswer[] | null {
  if (!Array.isArray(value)) return null
  const result: PlanFormulaAnswer[] = []
  for (const item of value) {
    const answer = decodeAnswer(item)
    if (answer === null) return null
    result.push(answer)
  }
  return result
}

function decodeTarget(value: unknown): PlanTargetPreview | null | undefined {
  if (value === null) return null
  if (!isObject(value)) return undefined
  const basis = text(value.basis)
  const kg = text(value.kg)
  const answered = outcome(value.outcome)
  const gap = nullableText(value.targetMinusPlannedInputKg)
  const missing = names(value.missingParameters)
  if (basis === null || kg === null || !KG_OUT.test(kg) || answered === null || gap === undefined || missing === null) {
    return undefined
  }
  if (gap !== null && !DECIMAL_OUT.test(gap)) return undefined
  if ((answered === 'Computed') !== (gap !== null)) return undefined
  return { basis, kg, outcome: answered, targetMinusPlannedInputKg: gap, missingParameters: missing }
}

export function decodePlanPreview(value: unknown): PlanPreview | null {
  if (!isObject(value)) return null
  const calculatorVersion = text(value.calculatorVersion)
  const workCenterId = text(value.workCenterId)
  const process = text(value.process)
  const plannedRollCount = value.plannedRollCount
  const plannedInputKg = text(value.plannedInputKg)
  const target = decodeTarget(value.target)
  const answers = decodeAnswers(value.answers)
  const missing = names(value.missingParameters)
  if (
    calculatorVersion === null || workCenterId === null || !GUID.test(workCenterId) || process === null ||
    typeof plannedRollCount !== 'number' || !Number.isInteger(plannedRollCount) || plannedRollCount < 1 ||
    plannedInputKg === null || !KG_OUT.test(plannedInputKg) || target === undefined || answers === null ||
    missing === null || typeof value.hasRefusal !== 'boolean' || !Array.isArray(value.rolls)
  ) {
    return null
  }
  const rolls: PlanRollPreview[] = []
  for (const item of value.rolls) {
    if (!isObject(item)) return null
    const parentRollId = text(item.parentRollId)
    const plannedKg = text(item.plannedKg)
    const rollAnswers = decodeAnswers(item.answers)
    if (parentRollId === null || !GUID.test(parentRollId) || plannedKg === null || !KG_OUT.test(plannedKg) || rollAnswers === null) {
      return null
    }
    rolls.push({ parentRollId, plannedKg, answers: rollAnswers })
  }
  if (rolls.length !== plannedRollCount) return null
  return {
    calculatorVersion,
    workCenterId,
    process,
    plannedRollCount,
    plannedInputKg,
    target,
    answers,
    rolls,
    missingParameters: missing,
    hasRefusal: value.hasRefusal,
  }
}

/** One preview request. It never retries: the next keystroke is the next request, and the hook cancels this one. */
export async function previewPlan(
  companyId: string,
  request: PlanPreviewRequest,
  signal?: AbortSignal,
): Promise<PlanPreviewOutcome> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/plans/preview`, {
      method: 'POST',
      headers: { ...devHeaders(), 'Content-Type': 'application/json', 'X-Company-Id': companyId },
      body: JSON.stringify(request),
      cache: 'no-store',
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    return { type: 'failed', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    body = null
  }
  if (signal?.aborted) return { type: 'aborted' }
  if (response.status === 200) {
    const preview = decodePlanPreview(body)
    return preview === null ? { type: 'failed', reason: 'malformed' } : { type: 'answered', preview }
  }
  if (response.status >= 500) return { type: 'failed', reason: 'service-unavailable' }
  const code = isObject(body) ? text(body.code) : null
  const field = isObject(body) ? text(body.field) : null
  return { type: 'refused', status: response.status, code, field }
}
