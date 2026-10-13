/**
 * FI04 open-item transport: two GETs and nothing else. The list takes only the server's opaque `cursor`; the detail
 * takes no query. Neither is cached by the browser, and a reply for another company or item is refused by the decoder.
 */

import type { ViewOutcome } from '../shell/uiState'
import { isBoundUuid } from './contract'
import { API_BASE, devHeaders } from './http'
import { decodeOpenItemFor, decodeOpenItemPage, OPEN_ITEM_CURSOR_MAX_LENGTH, type OpenItem, type OpenItemPage } from './openItemReadContract'

export const OPEN_ITEMS_PATH = '/finance/open-items'

export function openItemsUrl(cursor: string | null): string {
  return cursor === null ? `${API_BASE}${OPEN_ITEMS_PATH}` : `${API_BASE}${OPEN_ITEMS_PATH}?${new URLSearchParams({ cursor }).toString()}`
}

export function openItemUrl(openItemId: string): string {
  return `${API_BASE}${OPEN_ITEMS_PATH}/${encodeURIComponent(openItemId)}`
}

async function read<T>(url: string, companyId: string, decode: (body: unknown) => T | null,
  signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(url, {
      method: 'GET',
      headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' },
      cache: 'no-store',
      signal,
    })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let body: unknown
    try {
      body = await response.json()
    } catch {
      return signal?.aborted ? { type: 'fence-stale' } : { type: 'malformed' }
    }
    const data = decode(body)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch {
    return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

/** One page, newest first. `cursor` is the previous page's `nextCursor`, sent back unchanged; `null` is the first page. */
export function fetchOpenItems(companyId: string, cursor: string | null, signal?: AbortSignal): Promise<ViewOutcome<OpenItemPage>> {
  if (!isBoundUuid(companyId) || (cursor !== null && (cursor.trim().length === 0 || cursor.length > OPEN_ITEM_CURSOR_MAX_LENGTH))) {
    return Promise.resolve({ type: 'malformed' })
  }
  return read(openItemsUrl(cursor), companyId, (body) => decodeOpenItemPage(body, companyId), signal)
}

export function fetchOpenItem(companyId: string, openItemId: string, signal?: AbortSignal): Promise<ViewOutcome<OpenItem>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(openItemId)) return Promise.resolve({ type: 'malformed' })
  return read(openItemUrl(openItemId), companyId, (body) => decodeOpenItemFor(body, companyId, openItemId), signal)
}
