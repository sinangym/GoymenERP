import type { RemainderChangeRequest, RemainderDecisionRequest } from './remainderChange'

export type PendingRemainderAction =
  | { readonly kind: 'request'; readonly key: string; readonly payload: RemainderChangeRequest }
  | { readonly kind: 'approval' | 'rejection'; readonly key: string; readonly changeNo: number; readonly payload: RemainderDecisionRequest }

const slots = new Map<string, PendingRemainderAction>()
const settled = new Set<string>()
const listeners = new Map<string, Set<(action: PendingRemainderAction | null) => void>>()

const tokenOf = (binding: string, key: string) => `${binding}|${key}`

function clone(action: PendingRemainderAction): PendingRemainderAction {
  return action.kind === 'request'
    ? { ...action, payload: { ...action.payload } }
    : { ...action, payload: { ...action.payload } }
}

/** Keeps an outcome-unknown command alive across detail refreshes and route remounts. */
function publish(binding: string): void {
  const action = recallPendingRemainderAction(binding)
  listeners.get(binding)?.forEach((listener) => listener(action))
}

export function rememberPendingRemainderAction(binding: string, action: PendingRemainderAction): PendingRemainderAction {
  const token = tokenOf(binding, action.key)
  const current = slots.get(binding)
  if (current !== undefined) return clone(current)
  if (settled.has(token)) return clone(action)
  slots.set(binding, clone(action))
  publish(binding)
  return clone(action)
}

export function recallPendingRemainderAction(binding: string | null): PendingRemainderAction | null {
  if (binding === null) return null
  const action = slots.get(binding)
  return action === undefined ? null : clone(action)
}

/** A definite success or refusal closes only that exact actor/session/company/draft attempt. */
export function settlePendingRemainderAction(binding: string, key: string): void {
  settled.add(tokenOf(binding, key))
  if (slots.get(binding)?.key === key) {
    slots.delete(binding)
    publish(binding)
  }
}

export function subscribePendingRemainderAction(
  binding: string,
  listener: (action: PendingRemainderAction | null) => void,
): () => void {
  const bindingListeners = listeners.get(binding) ?? new Set()
  bindingListeners.add(listener)
  listeners.set(binding, bindingListeners)
  listener(recallPendingRemainderAction(binding))
  return () => {
    bindingListeners.delete(listener)
    if (bindingListeners.size === 0) listeners.delete(binding)
  }
}

/** Test isolation; production slots intentionally outlive a component render. */
export function resetPendingRemainderActions(): void {
  slots.clear()
  settled.clear()
  listeners.clear()
}
