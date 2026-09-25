import { isGuid, isLabelTransformationRequest, type LabelTransformationRequest } from './labelTransformationContract'

/**
 * One split/merge attempt whose outcome is not known yet. It is remembered per session, actor, company and
 * the label it was started from, so a reload or a return to the label offers exactly the same key and payload
 * again instead of a second, different attempt.
 */
export interface PendingLabelTransformation {
  readonly actorId: string
  readonly companyId: string
  readonly originLabelId: string
  readonly idempotencyKey: string
  readonly payload: LabelTransformationRequest
}

export function pendingTransformationKey(
  sessionEpoch: number,
  actorId: string | null,
  companyId: string | null,
  originLabelId: string | undefined,
): string | null {
  return actorId === null || companyId === null || originLabelId === undefined
    ? null
    : `goymen.inventory.label-transformation.v1:${sessionEpoch}:${actorId}:${companyId}:${originLabelId}`
}

function pending(value: unknown): PendingLabelTransformation | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<PendingLabelTransformation>
  return typeof candidate.actorId === 'string' && candidate.actorId.length > 0 &&
    isGuid(candidate.companyId) && isGuid(candidate.originLabelId) && isGuid(candidate.idempotencyKey) &&
    isLabelTransformationRequest(candidate.payload) &&
    candidate.payload.sources.some((source) => source.labelId === candidate.originLabelId)
    ? candidate as PendingLabelTransformation
    : null
}

export function recallPendingTransformation(key: string | null): PendingLabelTransformation | null {
  if (key === null) return null
  try {
    const raw = sessionStorage.getItem(key)
    return raw === null ? null : pending(JSON.parse(raw))
  } catch { return null }
}

export function reservePendingTransformation(key: string | null, value: PendingLabelTransformation): void {
  if (key === null) return
  try { sessionStorage.setItem(key, JSON.stringify(value)) } catch { /* component state stays fail-closed */ }
}

export function releasePendingTransformation(key: string | null, idempotencyKey: string): void {
  if (key === null || recallPendingTransformation(key)?.idempotencyKey !== idempotencyKey) return
  try { sessionStorage.removeItem(key) } catch { /* no-op */ }
}
