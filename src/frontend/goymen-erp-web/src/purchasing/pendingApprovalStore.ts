export interface ApprovalSnapshot {
  readonly actorId: string
  readonly companyId: string
  readonly orderId: string
  readonly expectedVersion: string
  readonly idempotencyKey: string
}

export function pendingApprovalKey(
  sessionEpoch: number,
  actorId: string | null,
  companyId: string | null,
  orderId: string,
): string | null {
  if (actorId === null || companyId === null || orderId.length === 0) return null
  return `goymen.purchasing.approval.v1:${sessionEpoch}:${actorId}:${companyId}:${orderId}`
}

export function recallPendingApproval(key: string | null): ApprovalSnapshot | null {
  if (key === null) return null
  let raw: string | null
  try { raw = sessionStorage.getItem(key) } catch { return null }
  if (raw === null) return null
  try {
    const value = JSON.parse(raw) as Partial<ApprovalSnapshot>
    return typeof value.actorId === 'string' && value.actorId.length > 0 &&
      typeof value.companyId === 'string' && value.companyId.length > 0 &&
      typeof value.orderId === 'string' && value.orderId.length > 0 &&
      typeof value.expectedVersion === 'string' && value.expectedVersion.length > 0 &&
      typeof value.idempotencyKey === 'string' && value.idempotencyKey.length > 0
      ? value as ApprovalSnapshot
      : null
  } catch { return null }
}

export function reservePendingApproval(key: string | null, snapshot: ApprovalSnapshot): void {
  if (key === null) return
  try { sessionStorage.setItem(key, JSON.stringify(snapshot)) } catch { /* fail closed in component state */ }
}

export function releasePendingApproval(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  const current = recallPendingApproval(key)
  if (current?.idempotencyKey !== idempotencyKey) return
  try { sessionStorage.removeItem(key) } catch { /* no-op */ }
}
