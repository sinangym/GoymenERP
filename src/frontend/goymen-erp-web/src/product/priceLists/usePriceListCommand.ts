import { useEffect, useRef, useState } from 'react'
import type { ShellState } from '../../shell/shellState'
import { sendPriceListCommand, type PriceListCommand } from './api'
import {
  applyPriceListOutcome,
  decidePriceListSubmit,
  generatePriceListKey,
  priceListBinding,
  type PriceListAttempt,
  type PriceListSnapshot,
} from './priceListAttempt'
import { PRICE_LIST_CODE_TEXT, PRICE_LIST_TEXT as T } from './priceListText'
import type { PriceListResult } from './contract'

export type SubmitResult = 'sent' | 'in-flight' | 'unresolved-other-payload' | 'no-context'

/** One command section's attempt: submit a command, retry the same snapshot, or leave an unknown one. */
export function usePriceListCommand(shell: ShellState, scope: string, onCompleted: (result: PriceListResult) => void,
  onRefusedMovedRecord?: () => void) {
  const [attempt, setAttempt] = useState<PriceListAttempt>({ phase: 'idle' })
  // Every state change goes through `attemptRef` first (the functions below), so the ref is never behind the state.
  const attemptRef = useRef(attempt)
  const binding = priceListBinding(shell, scope)
  const bindingRef = useRef(binding)
  useEffect(() => {
    bindingRef.current = binding
  }, [binding])
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null

  async function send(snapshot: PriceListSnapshot) {
    const sending: PriceListAttempt = { phase: 'sending', snapshot }
    attemptRef.current = sending
    setAttempt(sending)
    const outcome = await sendPriceListCommand(snapshot.command, snapshot.companyId, snapshot.key)
    const next = applyPriceListOutcome(attemptRef.current, snapshot, bindingRef.current, outcome)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase === 'completed') onCompleted(next.result)
    else if (next.phase === 'failed' && next.status === 409) onRefusedMovedRecord?.()
  }

  function submit(command: PriceListCommand): SubmitResult {
    const decision = decidePriceListSubmit(attemptRef.current, { binding, companyId, command }, generatePriceListKey)
    if (decision.type === 'blocked') return decision.reason
    void send(decision.snapshot)
    return 'sent'
  }

  function retry() {
    const current = attemptRef.current
    if (current.phase === 'unknown' || current.phase === 'abandoned') void send(current.snapshot)
  }

  function abandon() {
    const current = attemptRef.current
    if (current.phase !== 'unknown') return
    const next: PriceListAttempt = { phase: 'abandoned', snapshot: current.snapshot }
    attemptRef.current = next
    setAttempt(next)
  }

  function reset() {
    const next: PriceListAttempt = { phase: 'idle' }
    attemptRef.current = next
    setAttempt(next)
  }

  return { attempt, submit, retry, abandon, reset, companyId }
}

/** The one line a command section shows about its attempt, with its tone. */
export function attemptNotice(attempt: PriceListAttempt, successText: string): { text: string; tone: 'info' | 'unknown' | 'error' | 'success' } | null {
  switch (attempt.phase) {
    case 'idle':
      return null
    case 'sending':
      return { text: T.notice.sending, tone: 'info' }
    case 'unknown':
    case 'abandoned':
      return { text: T.notice.unknown, tone: 'unknown' }
    case 'completed':
      return { text: attempt.result.isReplay ? T.notice.replayed : successText, tone: 'success' }
    case 'failed': {
      const base = attempt.status === 401 ? T.notice.unauthenticated
        : attempt.status === 403 ? T.notice.forbidden
          : attempt.status === 404 ? T.notice.notFound
            : attempt.status === 409 ? T.notice.conflict
              : attempt.status === 400 || attempt.status === 422 ? T.notice.validation
                : T.notice.unexpected
      const code = attempt.problem.code === null ? undefined : PRICE_LIST_CODE_TEXT[attempt.problem.code]
      return { text: code === undefined ? base : `${base} ${code}`, tone: 'error' }
    }
  }
}

/** "lines[1].productId" + code → "2. satır: …" (only known codes; an unknown code keeps the generic notice). */
export function fieldErrorTexts(attempt: PriceListAttempt): string[] {
  if (attempt.phase !== 'failed') return []
  return attempt.problem.errors.flatMap((error) => {
    const text = PRICE_LIST_CODE_TEXT[error.code]
    if (text === undefined) return []
    const line = /^lines\[(\d+)\]/.exec(error.field)
    return [line === null ? text : `${Number(line[1]) + 1}. satır: ${text}`]
  })
}
