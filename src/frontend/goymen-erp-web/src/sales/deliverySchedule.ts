/**
 * DEC-SAL-003 — the planner's delivery-date update, as form rows and as the strict request. Dates only: every line of
 * the draft is sent exactly once with every slice it already carries (same ids, same quantities); the form never adds,
 * removes or re-splits a slice. An empty header or line date means "none" (null on the wire); a slice always has a date.
 */
import type { DraftDeliveryScheduleRequest, DraftDetail } from './contract'
import { isDateOnly } from './wirePrimitives'

export interface DeliveryScheduleSliceRow {
  readonly sliceId: string
  readonly quantity: string
  readonly date: string
}

export interface DeliveryScheduleLineRow {
  readonly lineId: string
  readonly lineNo: number
  readonly productName: string
  readonly date: string
  readonly slices: readonly DeliveryScheduleSliceRow[]
}

export interface DeliveryScheduleRows {
  readonly headerDate: string
  readonly lines: readonly DeliveryScheduleLineRow[]
}

export type DeliveryScheduleBuild =
  | { readonly ok: true; readonly payload: DraftDeliveryScheduleRequest }
  | { readonly ok: false; readonly reason: 'invalid-header-date' }
  | { readonly ok: false; readonly reason: 'invalid-line-date' | 'slice-date-required'; readonly lineNo: number }
  | { readonly ok: false; readonly reason: 'no-change' }

export function deliveryScheduleRowsFromDetail(detail: DraftDetail): DeliveryScheduleRows {
  return {
    headerDate: detail.requestedDeliveryDate ?? '',
    lines: detail.lines.map((line) => ({
      lineId: line.lineId,
      lineNo: line.lineNo,
      productName: line.productName,
      date: line.requestedDeliveryDate ?? '',
      slices: (line.requestedDeliverySlices ?? []).map((slice) => ({
        sliceId: slice.sliceId,
        quantity: slice.quantity,
        date: slice.requestedDeliveryDate,
      })),
    })),
  }
}

const optionalDate = (value: string): string | null | undefined => {
  const trimmed = value.trim()
  if (trimmed === '') return null
  return isDateOnly(trimmed) ? trimmed : undefined
}

/**
 * The request for the draft at `expectedVersion`. A form that moves no date is refused locally (the server would refuse
 * it too: an empty audited change is never recorded); the server still decides every rule.
 */
export function buildDeliveryScheduleRequest(
  expectedVersion: string,
  rows: DeliveryScheduleRows,
  current: DeliveryScheduleRows,
): DeliveryScheduleBuild {
  const header = optionalDate(rows.headerDate)
  if (header === undefined) return { ok: false, reason: 'invalid-header-date' }
  const lines: DraftDeliveryScheduleRequest['lines'][number][] = []
  for (const line of rows.lines) {
    const date = optionalDate(line.date)
    if (date === undefined) return { ok: false, reason: 'invalid-line-date', lineNo: line.lineNo }
    const slices: DraftDeliveryScheduleRequest['lines'][number]['slices'][number][] = []
    for (const slice of line.slices) {
      const sliceDate = slice.date.trim()
      if (!isDateOnly(sliceDate)) return { ok: false, reason: 'slice-date-required', lineNo: line.lineNo }
      slices.push({ sliceId: slice.sliceId, requestedDeliveryDate: sliceDate })
    }
    lines.push({ lineId: line.lineId, requestedDeliveryDate: date, slices })
  }
  const payload: DraftDeliveryScheduleRequest = { expectedVersion, requestedDeliveryDate: header, lines }
  if (sameDates(payload, current)) return { ok: false, reason: 'no-change' }
  return { ok: true, payload }
}

function sameDates(payload: DraftDeliveryScheduleRequest, current: DeliveryScheduleRows): boolean {
  if ((payload.requestedDeliveryDate ?? '') !== current.headerDate) return false
  return payload.lines.every((line) => {
    const known = current.lines.find((candidate) => candidate.lineId === line.lineId)
    if (known === undefined || (line.requestedDeliveryDate ?? '') !== known.date) return false
    return line.slices.every((slice) =>
      known.slices.some((candidate) => candidate.sliceId === slice.sliceId && candidate.date === slice.requestedDeliveryDate))
  })
}
