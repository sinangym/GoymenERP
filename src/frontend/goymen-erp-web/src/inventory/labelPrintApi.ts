import { API_BASE, inventoryHeaders } from './api'

export type LabelPrintOutcome =
  | { readonly type: 'applied'; readonly blob: Blob; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly status: number; readonly code: string | null }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }

export async function generateLabelPdf(labelId: string, requestedAtUtc: string,
  companyId: string, idempotencyKey: string): Promise<LabelPrintOutcome> {
  try {
    const response = await fetch(`${API_BASE}/inventory/labels/${encodeURIComponent(labelId)}/pdf`, {
      method: 'POST',
      headers: { ...inventoryHeaders(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify({ requestedAtUtc }),
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
