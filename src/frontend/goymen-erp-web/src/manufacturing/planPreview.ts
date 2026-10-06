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
import type { CapturedPlanCommercialContext } from './planRevisionContract'

import { decodePlanPreview, type PlanPreview } from './planPreviewContract'
export { decodePlanPreview } from './planPreviewContract'
export type {
  PlanCalculatedValue,
  PlanFormulaAnswer,
  PlanPreview,
  PlanRollPreview,
  PlanTargetPreview,
  PreviewOutcomeName,
} from './planPreviewContract'
/** The content to preview: the plan save's body without a plan id (nothing is created). */
export interface PlanPreviewRequest {
  readonly workCenterId: string
  readonly inputs: readonly {
    readonly parentRollId: string
    readonly plannedKg: string
    readonly expectedLabelVersion: string
    /** Legacy/create previews omit it; edit previews send the plan's captured snapshot byte-for-byte. */
    readonly commercialContext?: CapturedPlanCommercialContext | null
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

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null
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
