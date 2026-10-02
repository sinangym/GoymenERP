import { isLabelClassificationRequest, type LabelClassificationRequest } from './labelClassificationContract'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * One unresolved write. Its storage identity deliberately excludes the shell authority namespace:
 * capability refresh and a new authenticated generation must still find the same uncertain write.
 * Actor, company and label remain the stable security boundary.
 */
export interface PendingLabelClassification {
  readonly startedAuthorityNamespace: string
  readonly actorId: string
  readonly companyId: string
  readonly labelId: string
  readonly idempotencyKey: string
  readonly request: LabelClassificationRequest
}

export function pendingLabelClassificationKey(
  actorId: string | null,
  companyId: string | null,
  labelId: string | undefined,
): string | null {
  return actorId === null || actorId.length === 0 || companyId === null || labelId === undefined
    ? null
    : `goymen.inventory.label-classification.v1:${encodeURIComponent(actorId)}:${companyId}:${labelId}`
}

function pending(value: unknown): PendingLabelClassification | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<PendingLabelClassification>
  return typeof candidate.startedAuthorityNamespace === 'string' && candidate.startedAuthorityNamespace.length > 0
    && typeof candidate.actorId === 'string' && candidate.actorId.length > 0
    && typeof candidate.companyId === 'string' && GUID.test(candidate.companyId)
    && typeof candidate.labelId === 'string' && GUID.test(candidate.labelId)
    && typeof candidate.idempotencyKey === 'string' && GUID.test(candidate.idempotencyKey)
    && isLabelClassificationRequest(candidate.request)
    ? candidate as PendingLabelClassification
    : null
}

export function recallPendingLabelClassification(key: string | null): PendingLabelClassification | null {
  if (key === null) return null
  try {
    const raw = sessionStorage.getItem(key)
    return raw === null ? null : pending(JSON.parse(raw))
  } catch { return null }
}

export function reservePendingLabelClassification(
  key: string | null,
  value: PendingLabelClassification,
): void {
  if (key === null) return
  try { sessionStorage.setItem(key, JSON.stringify(value)) } catch { /* component state remains fail-closed */ }
}

export function releasePendingLabelClassification(key: string | null, idempotencyKey: string): void {
  if (key === null || recallPendingLabelClassification(key)?.idempotencyKey !== idempotencyKey) return
  try { sessionStorage.removeItem(key) } catch { /* no-op */ }
}
