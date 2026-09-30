import type { RemainderChangeRequest, RemainderDecisionRequest } from './remainderChange'

export type PendingRemainderAction =
  | { readonly kind: 'request'; readonly key: string; readonly payload: RemainderChangeRequest }
  | { readonly kind: 'approval' | 'rejection'; readonly key: string; readonly changeNo: number; readonly payload: RemainderDecisionRequest }

const slots = new Map<string, PendingRemainderAction>()
const settled = new Set<string>()

const tokenOf = (binding: string, key: string) => `${binding}|${key}`

function clone(action: PendingRemainderAction): PendingRemainderAction {
  return action.kind === 'request'
    ? { ...action, payload: { ...action.payload } }
    : { ...action, payload: { ...action.payload } }
}

/** Keeps an outcome-unknown command alive across detail refreshes and route remounts. */
export function rememberPendingRemainderAction(binding: string, action: PendingRemainderAction): void {
  const token = tokenOf(binding, action.key)
  if (settled.has(token)) return
  const current = slots.get(binding)
  if (current !== undefined && current.key !== action.key) settled.add(tokenOf(binding, current.key))
  slots.set(binding, clone(action))
}

export function recallPendingRemainderAction(binding: string | null): PendingRemainderAction | null {
  if (binding === null) return null
  const action = slots.get(binding)
  return action === undefined ? null : clone(action)
}

/** A definite success or refusal closes only that exact actor/session/company/draft attempt. */
export function settlePendingRemainderAction(binding: string, key: string): void {
  settled.add(tokenOf(binding, key))
  if (slots.get(binding)?.key === key) slots.delete(binding)
}

/** Test isolation; production slots intentionally outlive a component render. */
export function resetPendingRemainderActions(): void {
  slots.clear()
  settled.clear()
}
