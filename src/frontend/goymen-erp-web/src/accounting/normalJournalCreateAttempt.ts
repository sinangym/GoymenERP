import type { NormalJournalCommandSnapshot } from './normalJournalApi'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const DATE = /^\d{4}-\d{2}-\d{2}$/
const HASH = /^[0-9a-fA-F]{64}$/
const PREFIX = 'goymen:accounting:normal-journal:create:'

export type NormalJournalCreateSnapshot = Extract<NormalJournalCommandSnapshot, { readonly operation: 'create' }>

function storageKey(namespace: string): string {
  return `${PREFIX}${namespace}`
}

function isCreateSnapshot(value: unknown): value is NormalJournalCreateSnapshot {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false
  const snapshot = value as Record<string, unknown>
  if (snapshot.operation !== 'create' || typeof snapshot.idempotencyKey !== 'string' || snapshot.idempotencyKey.length === 0) return false
  if (typeof snapshot.request !== 'object' || snapshot.request === null || Array.isArray(snapshot.request)) return false
  const request = snapshot.request as Record<string, unknown>
  if (!GUID.test(String(request.draftId)) || !GUID.test(String(request.sourceId))) return false
  if (!DATE.test(String(request.transactionDate)) || !DATE.test(String(request.accountingDate))) return false
  if (!(request.valueDate === null || DATE.test(String(request.valueDate)))) return false
  if (!HASH.test(String(request.expectedPreparationVersion)) || !Array.isArray(request.lines) || request.lines.length < 2) return false
  return request.lines.every(raw => {
    if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return false
    const line = raw as Record<string, unknown>
    return GUID.test(String(line.accountId))
      && (line.side === 'Debit' || line.side === 'Credit')
      && typeof line.amount === 'string'
      && line.amount.length > 0
  })
}

export function recallNormalJournalCreate(namespace: string): NormalJournalCreateSnapshot | null {
  try {
    const raw = window.localStorage.getItem(storageKey(namespace))
    if (raw === null) return null
    const parsed: unknown = JSON.parse(raw)
    if (isCreateSnapshot(parsed)) return parsed
  } catch {
    // Corrupt or unavailable browser storage is never a trustworthy command snapshot.
  }
  clearNormalJournalCreate(namespace)
  return null
}

export function storeNormalJournalCreate(namespace: string, snapshot: NormalJournalCreateSnapshot): void {
  try {
    window.localStorage.setItem(storageKey(namespace), JSON.stringify(snapshot))
  } catch {
    // The caller still sends once; a later unknown outcome remains visibly unknown in this mount.
  }
}

export function clearNormalJournalCreate(namespace: string): void {
  try {
    window.localStorage.removeItem(storageKey(namespace))
  } catch {
    // Storage availability must not turn a known server answer into a second command.
  }
}
