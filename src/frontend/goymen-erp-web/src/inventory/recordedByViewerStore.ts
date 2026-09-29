import { isGuid } from './labelTransformationContract'

/**
 * The stock-count / stock-scrap ids the viewer recorded, remembered per actor, company and label for this browser
 * session. The count and scrap views carry no recorder, so this is how a panel knows "you recorded this" and hides
 * Approve/Reject on it (maker≠checker). It is a convenience only: the server's 409 self-approval refusal stays
 * authoritative for everything this does not remember (another tab or browser, a cleared session).
 */
export type RecordedByViewerKind = 'stock-count' | 'stock-scrap'

/** The newest ids kept per key; older ones fall back to the server's refusal. */
export const RECORDED_BY_VIEWER_LIMIT = 100

export function recordedByViewerKey(
  kind: RecordedByViewerKind,
  actorId: string | null,
  companyId: string,
  labelId: string,
): string | null {
  return actorId === null || actorId.length === 0
    ? null
    : `goymen.inventory.recorded-by-viewer.v1:${kind}:${actorId}:${companyId}:${labelId}`
}

export function recallRecordedByViewer(key: string | null): ReadonlySet<string> {
  if (key === null) return new Set()
  try {
    const raw = sessionStorage.getItem(key)
    const parsed: unknown = raw === null ? [] : JSON.parse(raw)
    return new Set(Array.isArray(parsed) ? parsed.filter(isGuid) : [])
  } catch { return new Set() }
}

/** Adds one id and answers the remembered set (the stored one, or the in-memory one when storage is unavailable). */
export function rememberRecordedByViewer(key: string | null, id: string, current: ReadonlySet<string>): ReadonlySet<string> {
  if (key === null || !isGuid(id) || current.has(id)) return current
  const next = [...current, id].slice(-RECORDED_BY_VIEWER_LIMIT)
  try { sessionStorage.setItem(key, JSON.stringify(next)) } catch { /* the in-memory set still hides the buttons */ }
  return new Set(next)
}
