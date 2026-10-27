/** UI-71 — S-11 document timeline rows, kept apart from the component for fast refresh. */

import type { DocumentRow } from '../documents/contract'

/** Which record a timeline row is bound to. */
export const DOCUMENT_RELATION = { order: 'order', customer: 'customer' } as const
export type DocumentRelation = (typeof DOCUMENT_RELATION)[keyof typeof DOCUMENT_RELATION]
export type RelationFilter = 'all' | DocumentRelation

export interface TimelineRow {
  readonly relation: DocumentRelation
  readonly document: DocumentRow
}

/** Newest first; equal instants by file name, then id, so the order never depends on which answer landed first. */
export function mergeTimeline(order: readonly DocumentRow[], customer: readonly DocumentRow[]): TimelineRow[] {
  const rows: TimelineRow[] = [
    ...order.map((document) => ({ relation: DOCUMENT_RELATION.order, document })),
    ...customer.map((document) => ({ relation: DOCUMENT_RELATION.customer, document })),
  ]
  return rows.sort((a, b) => {
    const at = Date.parse(a.document.uploadedAtUtc)
    const bt = Date.parse(b.document.uploadedAtUtc)
    if (at !== bt) return bt - at
    const byName = a.document.fileName.localeCompare(b.document.fileName, 'tr')
    return byName !== 0 ? byName : a.document.id.localeCompare(b.document.id)
  })
}
