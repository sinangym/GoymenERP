import { API_BASE, inventoryHeaders } from './api'
import { brandingGuid, brandingVersion } from '../documents/brandingContract'
import type { ViewOutcome } from '../shell/uiState'

export interface LabelPdfTemplateSelection { readonly publicationId: string | null; readonly publicationVersion: string | null }
export async function fetchLabelPdfTemplate(labelId: string, companyId: string): Promise<ViewOutcome<LabelPdfTemplateSelection>> {
  try {
    const response = await fetch(`${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/pdf-template`, { headers: inventoryHeaders(companyId) })
    if (response.status !== 200) return { type: 'http', status: response.status }
    const raw: unknown = await response.json()
    if (raw === null || typeof raw !== 'object' || Array.isArray(raw) || Object.keys(raw).length !== 4
      || !('companyId' in raw) || typeof raw.companyId !== 'string' || raw.companyId.toLowerCase() !== companyId.toLowerCase()
      || !('labelId' in raw) || typeof raw.labelId !== 'string' || raw.labelId.toLowerCase() !== labelId.toLowerCase()
      || !('publicationId' in raw) || !('publicationVersion' in raw)
      || !(raw.publicationId === null && raw.publicationVersion === null || brandingGuid(raw.publicationId) && brandingVersion(raw.publicationVersion)))
      return { type: 'malformed' }
    return { type: 'ok-single', data: { publicationId: raw.publicationId as string | null, publicationVersion: raw.publicationVersion as string | null } }
  } catch { return { type: navigator.onLine ? 'network-error' : 'offline' } }
}

export type LabelPrintOutcome =
  | { readonly type: 'applied'; readonly blob: Blob; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }

export async function generateLabelPdf(labelId: string, requestedAtUtc: string,
  companyId: string, idempotencyKey: string, template?: LabelPdfTemplateSelection): Promise<LabelPrintOutcome> {
  try {
    const response = await fetch(`${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/pdf`, {
      method: 'POST',
      headers: { ...inventoryHeaders(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({ requestedAtUtc, ...(template?.publicationId == null ? {} : template) }),
    })
    if (response.status === 200) {
      const replayHeader = response.headers.get('Idempotency-Replayed')
      const original = response.headers.get('Idempotency-Original-Correlation-Id')
      const replayed = replayHeader === 'true'
      const documentId = response.headers.get('X-Document-Id')
      if (!response.headers.get('Content-Type')?.startsWith('application/pdf')
        || (replayed ? original === null || original.trim() === '' : replayHeader !== null || original !== null)
        || documentId === null || !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(documentId))
        return { type: 'unknown', reason: 'malformed-response' }
      const blob = await response.blob()
      if (await blob.slice(0, 5).text() !== '%PDF-') return { type: 'unknown', reason: 'malformed-response' }
      return { type: 'applied', blob, replayed }
    }
    if ([400, 401, 403, 404, 409, 415, 422].includes(response.status)) {
      let body: unknown = null
      try { body = await response.json() } catch { /* code-less owner refusal */ }
      const code = typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null
      return { type: 'failed', status: response.status, code }
    }
    return { type: 'unknown', reason: response.status >= 500 ? 'service-unavailable' : 'malformed-response' }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
