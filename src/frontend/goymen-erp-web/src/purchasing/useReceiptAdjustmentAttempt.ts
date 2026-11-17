import { useLayoutEffect, useRef, useState } from 'react'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { AdjustmentOutcome } from './receiptAdjustmentApi'
import { receiptIdentity } from './goodsReceiptAttempt'

interface Snapshot { readonly key: string; readonly payload: unknown }
const pending = new Map<string, Snapshot>() // Session/actor/company/command scope, memory only; never URL, logs or storage.
export function resetAdjustmentAttemptsForTests(): void { pending.clear() }

export function useReceiptAdjustmentAttempt<TPayload, TAnswer>(scope: string, readFence: string,
  send: (payload: TPayload, key: string) => Promise<AdjustmentOutcome<TAnswer>>, onSettled: () => void) {
  const boundary = useAuthBoundary()
  const [snapshot, setSnapshot] = useState<Snapshot | null>(() => pending.get(scope) ?? null)
  const [outcome, setOutcome] = useState<AdjustmentOutcome<TAnswer> | null>(null)
  const [sending, setSending] = useState(false)
  const live = useRef(0), inFlight = useRef(false)
  useLayoutEffect(() => {
    live.current++; inFlight.current = false; setSending(false); setSnapshot(pending.get(scope) ?? null); setOutcome(null)
    return () => { live.current++ }
  }, [scope, readFence])
  async function execute(attempt: Snapshot) {
    if (inFlight.current || pending.get(scope)?.key !== attempt.key) return
    inFlight.current = true; setSending(true)
    const generation = live.current, auth = boundary.capture(), wasUnknown = snapshot !== null
    const result = await send(attempt.payload as TPayload, attempt.key)
    if (live.current !== generation) { inFlight.current = false; return }
    inFlight.current = false; setSending(false)
    if (result.type === 'failed') boundary.report(result.status, auth)
    if (result.type === 'unknown' || result.type === 'failed' && wasUnknown && result.isReplay !== true) {
      setOutcome(result.type === 'unknown' ? result : { type: 'unknown', reason: 'network-error' }); return
    }
    pending.delete(scope); setSnapshot(null); setOutcome(result)
    if (result.type === 'applied' || result.status === 409) onSettled()
  }
  return { snapshot, outcome, sending, locked: snapshot !== null || sending,
    submit(payload: TPayload) {
      if (pending.has(scope) || inFlight.current) return
      const attempt = { key: receiptIdentity(), payload: structuredClone(payload) }
      pending.set(scope, attempt); setSnapshot(attempt); void execute(attempt)
    },
    retry() { const attempt = pending.get(scope); if (attempt !== undefined) void execute(attempt) },
  }
}
