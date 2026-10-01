import type { ViewOutcome } from '../shell/uiState'
import { ACCOUNTING_BASE, devHeaders } from './api'
import {
  decodeNormalJournalDraftSourcePreparation,
  type NormalJournalDraftSourcePreparation,
} from './contract'

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
