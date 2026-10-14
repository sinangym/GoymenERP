/**
 * AT-061 / AT-062 (DEC-PRDCT-005, locked): the button-less live preview of a plan form.
 *
 * - The form's content is sent 400 ms after the user stops changing it (the decision's 300–500 ms window); every change
 *   restarts the wait, and a request already in flight for an older content is ABORTED, so an older answer can never
 *   land after a newer one.
 * - The last answered preview is kept together with the exact input snapshot it answered. It is `current` only while
 *   that snapshot is the form's content; the moment the form changes it is marked not current (never shown as the
 *   answer to the new content), and a refused or failed preview says why instead of leaving the old one as current.
 * - A content the form itself can already see is incomplete (no work center, no roll, a planned kg that is not a
 *   positive decimal, a half-given target) is not sent: the reasons are listed.
 * - The preview writes nothing and never blocks the save: the save sends its own content and the server decides.
 * - M01.2: a plan's stored cut lines are part of the content — in the request body and so in its key — exactly as
 *   given (order, ids, signed sequences, canonical lengths, counts). Without lines the member is not sent at all.
 */

import { useEffect, useRef, useState } from 'react'
import type { SecuritySnapshot } from '../shell/authBoundary'
import type { AuthBoundary } from '../shell/useAuthBoundary'
import { isKilogramText } from './contract'
import { previewPlan, type PlanPreview, type PlanPreviewRequest } from './planPreview'
import type { CutToLengthSpecRequest, PlanTargetBasis, SlittingSpecRequest } from './planCommandApi'
import type { CapturedPlanCommercialContext } from './planRevisionContract'

/** Inside DEC-PRDCT-005's locked 300–500 ms window. */
export const PLAN_PREVIEW_DEBOUNCE_MS = 400

export interface PlanPreviewFormRow {
  readonly parentRollId: string
  readonly label: string
  readonly labelVersion: string | null
  /** Omitted by legacy/create callers; edit callers preserve the captured owner snapshot, including explicit null. */
  readonly commercialContext?: CapturedPlanCommercialContext | null
  readonly plannedKg: string
}

export interface SlittingSpecFormInput {
  readonly enabled: boolean
  readonly lines: readonly { readonly label: string; readonly bandWidthMm: string; readonly count: string }[]
  readonly windingTargetKg: string
}

export type PlanPreviewInput =
  | { readonly type: 'incomplete'; readonly reasons: readonly string[] }
  | { readonly type: 'ready'; readonly request: PlanPreviewRequest }

export interface AnsweredPreview {
  readonly key: string
  readonly request: PlanPreviewRequest
  readonly preview: PlanPreview
}

export type PlanPreviewStatus =
  | { readonly type: 'idle' }
  | { readonly type: 'incomplete'; readonly reasons: readonly string[] }
  | { readonly type: 'waiting' }
  | { readonly type: 'loading' }
  | { readonly type: 'answered' }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly field: string | null }
  | { readonly type: 'failed'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed' }

export interface PlanPreviewState {
  readonly status: PlanPreviewStatus
  /** The last preview the server answered, with the content it answered. */
  readonly last: AnsweredPreview | null
  /** True only when {@link last} answered exactly the form's present content. */
  readonly current: boolean
}

function positiveKilograms(value: string): boolean {
  return isKilogramText(value) && /[1-9]/.test(value)
}

const POSITIVE_MILLIMETRES = /^(?=.*[1-9])(?:0|[1-9][0-9]*)(?:\.[0-9]{1,3})?$/
const POSITIVE_COUNT = /^[1-9][0-9]*$/
const INT32_MAX = 2_147_483_647

function positiveInt32(value: string): boolean {
  if (!POSITIVE_COUNT.test(value)) return false
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) && parsed <= INT32_MAX
}

function slittingSpecRequest(input: SlittingSpecFormInput | undefined, reasons: string[]): SlittingSpecRequest | null {
  if (input === undefined || !input.enabled) return null
  if (input.lines.length === 0) reasons.push('Dilme ayrıntısı için en az bir bant satırı eklenmelidir.')
  for (const line of input.lines) {
    if (!POSITIVE_MILLIMETRES.test(line.bandWidthMm)) {
      reasons.push(`${line.label}: bant genişliği sıfırdan büyük ve en fazla üç ondalıklı mm olmalıdır.`)
    }
    if (!positiveInt32(line.count)) {
      reasons.push(`${line.label}: bant adedi 1 ile ${INT32_MAX} arasında tam sayı olmalıdır.`)
    }
  }
  if (input.windingTargetKg !== '' && !positiveKilograms(input.windingTargetKg)) {
    reasons.push('Sarım hedefi kg sıfırdan büyük bir sayı olmalıdır.')
  }
  if (reasons.length > 0) return null
  return {
    // `positiveInt32` above proves this exact conversion is lossless and inside the backend's `int` contract.
    lines: input.lines.map((line) => ({ bandWidthMm: line.bandWidthMm, count: Number(line.count) })),
    windingTargetKg: input.windingTargetKg === '' ? null : input.windingTargetKg,
  }
}

/** The form content as a preview request, or what the form can already see is missing. No formula runs here. */
export function planPreviewInput(
  workCenterId: string,
  rows: readonly PlanPreviewFormRow[],
  targetBasis: '' | PlanTargetBasis,
  targetKg: string,
  slitting?: SlittingSpecFormInput,
  cutToLengthSpec?: CutToLengthSpecRequest | null,
): PlanPreviewInput {
  const reasons: string[] = []
  if (workCenterId === '') reasons.push('İş merkezi seçilmedi.')
  if (rows.length === 0) reasons.push('En az bir ana rulo eklenmedi.')
  for (const row of rows) {
    if (!positiveKilograms(row.plannedKg)) reasons.push(`${row.label}: planlanan kg sıfırdan büyük bir sayı değil.`)
    if (row.labelVersion === null) reasons.push(`${row.label}: etiket sürümü bilinmiyor; ana ruloyu yeniden seçin.`)
  }
  if ((targetBasis === '') !== (targetKg === '') || (targetKg !== '' && !positiveKilograms(targetKg))) {
    reasons.push('Hedef kullanılacaksa türü ve sıfırdan büyük kg değeri birlikte girilmelidir.')
  }
  const slittingSpec = slittingSpecRequest(slitting, reasons)
  if (reasons.length > 0) return { type: 'incomplete', reasons }
  return {
    type: 'ready',
    request: {
      workCenterId,
      inputs: rows.map((row) => ({
        parentRollId: row.parentRollId,
        plannedKg: row.plannedKg,
        expectedLabelVersion: row.labelVersion as string,
        ...(row.commercialContext === undefined ? {} : { commercialContext: row.commercialContext }),
      })),
      target: targetBasis === '' ? null : { basis: targetBasis, kg: targetKg },
      ...(slitting === undefined ? {} : { slittingSpec }),
      // Carried, never validated, sorted or renumbered here: these are the stored lines the server already accepted.
      ...(cutToLengthSpec === undefined || cutToLengthSpec === null
        ? {}
        : {
            cutToLengthSpec: {
              lines: cutToLengthSpec.lines.map((line) => ({
                lineId: line.lineId,
                sequence: line.sequence,
                lengthMm: line.lengthMm,
                count: line.count,
              })),
            },
          }),
    },
  }
}

export function usePlanPreview(
  companyId: string | null,
  input: PlanPreviewInput,
  authBoundary: AuthBoundary,
): PlanPreviewState {
  // What the server said, and for which content (its key). Everything else is derived from the form's present input,
  // so a changed content is "waiting" and the older answer is "not current" in the very render that changed it.
  const [settled, setSettled] = useState<{ readonly key: string; readonly status: PlanPreviewStatus } | null>(null)
  const [last, setLast] = useState<AnsweredPreview | null>(null)
  const sequence = useRef(0)
  const security = authBoundary.capture()
  const reportSecurity = authBoundary.report
  // The immutable request-start snapshot is part of the preview key, so an old actor/company/capability answer cannot
  // become the current answer after the shell context changes. It is parsed back with the request inside the effect;
  // no response-time shell value can replace it.
  const key = companyId !== null && input.type === 'ready'
    ? JSON.stringify([companyId, input.request, security])
    : null

  // The effect restarts on a CHANGED content (its key text), never on a new object of the same content; the content
  // is read back from that key, so the request sent is exactly the one the key names.
  useEffect(() => {
    const generation = ++sequence.current
    if (key === null) return
    const [company, request, requestSecurity] = JSON.parse(key) as [string, PlanPreviewRequest, SecuritySnapshot]
    const controller = new AbortController()
    const timer = setTimeout(() => {
      if (generation !== sequence.current) return
      setSettled({ key, status: { type: 'loading' } })
      void previewPlan(company, request, controller.signal).then((outcome) => {
        // A superseded or cancelled request never answers: only the newest content may set the state.
        if (generation !== sequence.current || controller.signal.aborted || outcome.type === 'aborted') return
        if (outcome.type === 'answered') {
          setLast({ key, request, preview: outcome.preview })
          setSettled({ key, status: { type: 'answered' } })
        } else if (outcome.type === 'refused') {
          if (outcome.status === 401 || outcome.status === 403) {
            reportSecurity(outcome.status, requestSecurity)
          }
          setSettled({
            key,
            status: { type: 'refused', status: outcome.status, code: outcome.code, field: outcome.field },
          })
        } else {
          setSettled({ key, status: { type: 'failed', reason: outcome.reason } })
        }
      })
    }, PLAN_PREVIEW_DEBOUNCE_MS)
    return () => {
      clearTimeout(timer)
      controller.abort()
    }
  }, [key, reportSecurity])

  const status: PlanPreviewStatus = companyId === null
    ? { type: 'idle' }
    : input.type === 'incomplete'
      ? { type: 'incomplete', reasons: input.reasons }
      : settled !== null && settled.key === key
        ? settled.status
        : { type: 'waiting' }
  return { status, last, current: status.type === 'answered' && last !== null && last.key === key }
}
