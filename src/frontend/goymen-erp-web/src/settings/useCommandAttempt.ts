/**
 * One command attempt's key and status for a form. A NEW Idempotency-Key is minted for a new attempt; after an UNKNOWN
 * outcome (no response, a 5xx, an off-contract answer) the same key is kept for the same payload, so "retry" can only
 * replay the first attempt and never apply it twice. A definite answer (applied or refused) releases the key.
 */

import { useCallback, useState } from 'react'
import type { CommandOutcome } from './discountLimitApi'

export type AttemptStatus<T> =
  | { readonly kind: 'idle' }
  | { readonly kind: 'busy' }
  | { readonly kind: 'applied'; readonly data: T; readonly replayed: boolean }
  | {
      readonly kind: 'refused'
      readonly status: number
      readonly code: string | null
      readonly companyId: string | null
      readonly groupCode: string | null
    }
  | { readonly kind: 'unknown' }

interface Pending {
  readonly key: string
  readonly fingerprint: string
}

export function useCommandAttempt<T>() {
  const [status, setStatus] = useState<AttemptStatus<T>>({ kind: 'idle' })
  const [pending, setPending] = useState<Pending | null>(null)

  const run = useCallback(
    async (payload: unknown, execute: (idempotencyKey: string) => Promise<CommandOutcome<T>>) => {
      const fingerprint = JSON.stringify(payload)
      const key = pending !== null && pending.fingerprint === fingerprint ? pending.key : crypto.randomUUID()
      setStatus({ kind: 'busy' })
      const outcome = await execute(key)
      if (outcome.type === 'unknown') {
        setPending({ key, fingerprint })
        setStatus({ kind: 'unknown' })
        return outcome
      }

      setPending(null)
      setStatus(
        outcome.type === 'applied'
          ? { kind: 'applied', data: outcome.data, replayed: outcome.replayed }
          : {
              kind: 'refused',
              status: outcome.status,
              code: outcome.code,
              companyId: outcome.companyId,
              groupCode: outcome.groupCode,
            },
      )
      return outcome
    },
    [pending],
  )

  const reset = useCallback(() => {
    setPending(null)
    setStatus({ kind: 'idle' })
  }, [])

  return { status, run, reset, hasPendingRetry: pending !== null }
}
