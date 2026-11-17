import { API_BASE, purchasingHeaders } from './api'
import type { AdjustmentOutcome } from './receiptAdjustmentApi'
import type { ViewOutcome } from '../shell/uiState'
import { decodeImportPreview, decodeImportReservation, type ImportPreview, type ImportPreviewRequest, type ImportReservationResult } from './invoiceImportContract'

const base = `${API_BASE}/purchasing/invoice-imports`
export async function reserveInvoiceImport(company: string, key: string): Promise<AdjustmentOutcome<ImportReservationResult>> {
  try {
    const response = await fetch(`${base}/reservations`, { method: 'POST', headers: { ...purchasingHeaders(company),
      'Content-Type': 'application/json', 'Idempotency-Key': key }, body: '{}' })
    let body: unknown = null
    try { body = await response.json() } catch { /* Unknown success keeps the same idempotency key. */ }
    if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
    if (!response.ok) return { type: 'failed', status: response.status, code: null, isReplay: response.headers.get('Idempotency-Replayed') === 'true' }
    if (response.status !== 200) return { type: 'unknown', reason: 'malformed' }
    const decoded = decodeImportReservation(body, company, response.headers.get('Idempotency-Replayed') === 'true', response.headers.get('Idempotency-Original-Correlation-Id'))
    return decoded === null ? { type: 'unknown', reason: 'malformed' } : { type: 'applied', response: decoded }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
export async function previewInvoiceImport(company: string, id: string, request: ImportPreviewRequest, signal: AbortSignal): Promise<ViewOutcome<ImportPreview>> {
  try {
    const response = await fetch(`${base}/${encodeURIComponent(id)}/preview`, { method: 'POST', cache: 'no-store', signal,
      headers: { ...purchasingHeaders(company), 'Content-Type': 'application/json' }, body: JSON.stringify(request) })
    if (!response.ok) return { type: 'http', status: response.status }
    if (response.status !== 200) return { type: 'malformed' }
    let body: unknown
    try { body = await response.json() } catch { return { type: 'malformed' } }
    const decoded = decodeImportPreview(body, company, id, request)
    return decoded === null ? { type: 'malformed' } : { type: 'ok-single', data: decoded }
  } catch { return signal.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
