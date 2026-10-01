import type { ViewOutcome } from '../shell/uiState'
import { decodeSourcePage, type SourcePage, type SourceSummary } from '../finance/contract'
import { ACCOUNTING_BASE, devHeaders } from './api'
import {
  decodeNormalJournalDraftSourcePreparation,
  type NormalJournalDraftSourcePreparation,
} from './contract'

const FINANCE_SOURCES = '/api/v1/finance/manual-receipts'

export async function fetchNormalJournalSources(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<SourcePage>> {
  const items: SourceSummary[] = []
  const sourceIds = new Set<string>()
  const cursors = new Set<string>()
  let cursor: string | null = null
  while (true) {
    const params = new URLSearchParams()
    if (cursor !== null) params.set('cursor', cursor)
    const query = params.toString()
    let response: Response
    try {
      response = await fetch(`${FINANCE_SOURCES}${query === '' ? '' : `?${query}`}`, {
        headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' },
        signal,
      })
    } catch {
      if (signal?.aborted) return { type: 'fence-stale' }
      return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
    }
    let body: unknown = null
    try {
      body = await response.json()
    } catch {
      if (signal?.aborted) return { type: 'fence-stale' }
    }
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const decoded = decodeSourcePage(body, companyId)
    if (!decoded.ok) return { type: 'malformed' }
    for (const source of decoded.value.items) {
      if (sourceIds.has(source.id)) return { type: 'malformed' }
      sourceIds.add(source.id)
      items.push(source)
    }
    const next = decoded.value.nextCursor
    if (next === null) break
    if (cursors.has(next)) return { type: 'malformed' }
    cursors.add(next)
    cursor = next
  }
  return { type: 'ok-single', data: { companyId, items, nextCursor: null } }
}

export async function fetchNormalJournalDraftSource(
  companyId: string,
  sourceId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<NormalJournalDraftSourcePreparation>> {
  let response: Response
  try {
    response = await fetch(
      `${ACCOUNTING_BASE}/normal-journal-draft-sources/${encodeURIComponent(sourceId)}`,
      { headers: { ...devHeaders(), 'X-Company-Id': companyId }, signal },
    )
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decodeNormalJournalDraftSourcePreparation(body, companyId, sourceId)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}
