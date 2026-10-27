/**
 * MOD-07 — the sales order PDF transport.
 *
 *   GET /api/v1/sales/drafts/{id}/pdf → 200 application/pdf attachment (the order's current revision)
 *
 * The server decides everything: the draft read (403 / 404 for another company), whether prices are printed (only under
 * `sales.orders.price.read`; otherwise the price-less variant — this module never asks for a variant and cannot), the file
 * name (DEC-DOC-004) and the content. A 409 means the order changed between the server's two reads; asking again is safe
 * (a read, nothing is stored). `X-Company-Id` is the committed shell company — request context, never a choice here.
 */

import { salesDraftDetailPath } from './contract'

export const salesDraftPdfPath = (id: string) => `${salesDraftDetailPath(id)}/pdf`

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'
const PDF_MEDIA_TYPE = 'application/pdf'

export type OrderPdfOutcome =
  | { readonly type: 'ok'; readonly blob: Blob; readonly fileName: string | null }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null }
  /** No answer (offline, unreachable) or an answer that is not the PDF it claims to be. */
  | { readonly type: 'failed' }
  | { readonly type: 'aborted' }

export async function downloadOrderPdf(draftId: string, companyId: string, signal?: AbortSignal): Promise<OrderPdfOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${salesDraftPdfPath(draftId)}`, {
      headers: {
        ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
        'X-Company-Id': companyId,
        Accept: `${PDF_MEDIA_TYPE}, application/problem+json`,
      },
      signal,
    })
  } catch {
    return signal?.aborted ? { type: 'aborted' } : { type: 'failed' }
  }
  if (response.status !== 200) {
    return { type: 'refused', status: response.status, code: await problemCode(response) }
  }
  const mediaType = (response.headers.get('Content-Type') ?? '').split(';')[0].trim().toLowerCase()
  if (mediaType !== PDF_MEDIA_TYPE) return { type: 'failed' }
  try {
    const blob = await response.blob()
    return blob.size === 0 ? { type: 'failed' } : { type: 'ok', blob, fileName: attachmentFileName(response.headers.get('Content-Disposition')) }
  } catch {
    return signal?.aborted ? { type: 'aborted' } : { type: 'failed' }
  }
}

async function problemCode(response: Response): Promise<string | null> {
  try {
    const body: unknown = await response.json()
    if (typeof body === 'object' && body !== null && 'code' in body) {
      const code = (body as { readonly code: unknown }).code
      return typeof code === 'string' ? code : null
    }
  } catch {
    // A code-less problem (404, 403, 503) or an empty body.
  }
  return null
}

/**
 * The server's DEC-DOC-004 name: RFC 6266 `filename*=UTF-8''…` first (Turkish letters), the plain `filename` otherwise.
 * Only the last path segment is kept; the browser sanitises the rest when saving.
 */
export function attachmentFileName(disposition: string | null): string | null {
  if (disposition === null) return null
  const extended = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(disposition)
  let name: string | null = null
  if (extended !== null) {
    try {
      name = decodeURIComponent(extended[1].trim())
    } catch {
      name = null
    }
  }
  if (name === null) {
    const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(disposition)
    name = plain === null ? null : plain[1].trim()
  }
  if (name === null) return null
  const segment = name.split(/[\\/]/).pop()?.trim() ?? ''
  return segment.length === 0 ? null : segment
}

/** Saved as a file under its own name; never opened as a page of this application. */
export function saveOrderPdf(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.rel = 'noopener'
  document.body.append(anchor)
  anchor.click()
  anchor.remove()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}
