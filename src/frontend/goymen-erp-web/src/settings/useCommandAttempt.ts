/**
 * One command attempt's key and status for a form. A NEW Idempotency-Key is minted for a new attempt; after an UNKNOWN
 * outcome (no response, a 5xx, an off-contract answer) the same key is kept for the same payload, so "retry" can only
 * replay the first attempt and never apply it twice. The first execution closure is frozen with its key;
 * changing or cancelling the form cannot turn an unresolved attempt into a new command.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useShellState } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { CommandOutcome } from './discountLimitApi'
import { isUuid } from './discountLimitContract'

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
  /**
   * The outcome of the pending attempt is unknown. `edited`: the form was submitted with a DIFFERENT body while that
   * attempt is still unresolved. Nothing was sent — neither the edited body (it would need a new key and could apply a
   * second command) nor anything under the old key — and the screen must say so instead of looking idle.
   */
  | { readonly kind: 'unknown'; readonly edited?: boolean }

interface Pending<T> {
  readonly key: string
  readonly fingerprint: string
  readonly payload: unknown
  readonly execute: (idempotencyKey: string) => Promise<CommandOutcome<T>>
}

export interface SettingsAttemptBinding {
  readonly sessionBinding: string | null
  readonly actorId: string
  readonly companyId: string
}

export function settingsAttemptStoreKey(binding: SettingsAttemptBinding, operation: string): string | null {
  if (!binding.sessionBinding) return null
  return `goymen.settings.command.v1:${JSON.stringify([binding.sessionBinding, binding.actorId, binding.companyId, operation])}`
}

interface Journal<T> {
  readonly storeKey: string | null
  readonly execute: (payload: unknown, key: string) => Promise<CommandOutcome<T>>
  readonly observe?: () => (outcome: CommandOutcome<T>) => void
  readonly accepts?: (payload: unknown) => boolean
}

interface StoredAttempt {
  readonly key: string
  readonly payload: unknown
  readonly claim: string
}

function recall(storeKey: string | null): StoredAttempt | null {
  if (storeKey === null) return null
  try {
    const raw: unknown = JSON.parse(sessionStorage.getItem(storeKey) ?? 'null')
    if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return null
    const value = raw as Partial<StoredAttempt>
    if (!isUuid(value.key) || !isUuid(value.claim) || typeof value.payload !== 'object' || value.payload === null
      || Array.isArray(value.payload)) return null
    return value as StoredAttempt
  } catch {
    return null
  }
}

function restore<T>(journal: Journal<T> | undefined): Pending<T> | null {
  if (journal === undefined) return null
  const stored = recall(journal.storeKey)
  return stored === null || (journal.accepts !== undefined && !journal.accepts(stored.payload)) ? null
    : { key: stored.key, payload: stored.payload,
    fingerprint: JSON.stringify(stored.payload), execute: key => journal.execute(stored.payload, key) }
}

export function useCommandAttempt<T>(journal?: Journal<T>) {
  const [pending, setPending] = useState<Pending<T> | null>(() => restore(journal))
  const [status, setStatus] = useState<AttemptStatus<T>>(() => pending === null ? { kind: 'idle' } : { kind: 'unknown' })
  const pendingRef = useRef<Pending<T> | null>(pending)
  const inFlight = useRef(false)
  const mounted = useRef(false)
  const binding = useRef(journal?.storeKey)
  const originalBinding = useRef(journal?.storeKey)
  useEffect(() => {
    mounted.current = true
    binding.current = journal?.storeKey
    return () => { mounted.current = false }
  }, [journal?.storeKey])

  const dispatch = useCallback(
    async (attempt: Pending<T>, previouslyUnknown: boolean): Promise<CommandOutcome<T>> => {
      if (inFlight.current || !mounted.current || binding.current !== journal?.storeKey
        || originalBinding.current !== journal?.storeKey
        || (journal?.accepts !== undefined && !journal.accepts(attempt.payload))) {
        return { type: 'unknown', reason: 'unexpected' } as const
      }
      const storeKey = journal?.storeKey ?? null
      const claim = crypto.randomUUID()
      if (storeKey !== null) {
        try {
          const current = recall(storeKey)
          if ((current !== null && current.key !== attempt.key)
            || (current === null && sessionStorage.getItem(storeKey) !== null)) {
            setStatus({ kind: 'unknown' })
            return { type: 'unknown', reason: 'unexpected' }
          }
          // Reserve before sending. If durable browser storage refuses the reservation, no request leaves.
          sessionStorage.setItem(storeKey, JSON.stringify({ key: attempt.key, payload: attempt.payload, claim }))
        } catch {
          setStatus({ kind: 'unknown' })
          return { type: 'unknown', reason: 'unexpected' }
        }
      }
      // K3-REVIEW-B P3-2: an attempt becomes the pending one only once its slot is reserved. A blocked new attempt
      // never left, so it must not lock the form: reset stays possible and the slot owner keeps its own key.
      pendingRef.current = attempt
      setPending(attempt)
      inFlight.current = true
      setStatus({ kind: 'busy' })
      const observe = journal?.observe?.()
      let outcome: CommandOutcome<T>
      try {
        outcome = await attempt.execute(attempt.key)
      } catch {
        outcome = { type: 'unknown', reason: 'network-error' }
      } finally {
        inFlight.current = false
      }
      observe?.(outcome)
      // An old mount/request cannot settle the slot claimed by a newer retry, nor run its old form callbacks.
      if (!mounted.current || binding.current !== journal?.storeKey
        || (storeKey !== null && recall(storeKey)?.claim !== claim)) {
        return { type: 'unknown', reason: 'unexpected' }
      }
      if (outcome.type === 'unknown') {
        setStatus({ kind: 'unknown' })
        return outcome
      }

      // A refusal before the owner receipt was read cannot settle an earlier lost response.
      if (previouslyUnknown && outcome.type === 'refused' && [400, 401, 403, 404, 415].includes(outcome.status)) {
        setStatus({ kind: 'unknown' })
        return outcome
      }

      pendingRef.current = null
      setPending(null)
      if (storeKey !== null) {
        try {
          if (recall(storeKey)?.claim === claim) sessionStorage.removeItem(storeKey)
        } catch { /* A retained receipt can be replayed safely on the next visit. */ }
      }
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
    [journal],
  )

  const run = useCallback(async (payload: unknown, execute: (idempotencyKey: string) => Promise<CommandOutcome<T>>) => {
    const fingerprint = JSON.stringify(payload)
    const previous = pendingRef.current
    if (inFlight.current) return { type: 'unknown', reason: 'unexpected' } as const
    if (previous !== null && previous.fingerprint !== fingerprint) {
      // K3 P2-4: an edited retry is never sent and never swallowed silently; the unresolved attempt stays pending.
      setStatus({ kind: 'unknown', edited: true })
      return { type: 'unknown', reason: 'unexpected' } as const
    }
    return dispatch(previous ?? { key: crypto.randomUUID(), fingerprint, payload, execute }, previous !== null)
  }, [dispatch])

  const retry = useCallback(async (): Promise<CommandOutcome<T>> => {
    const previous = pendingRef.current
    return previous === null ? { type: 'unknown', reason: 'unexpected' } : dispatch(previous, true)
  }, [dispatch])

  const reset = useCallback(() => {
    if (pendingRef.current !== null || inFlight.current) return
    setPending(null)
    setStatus({ kind: 'idle' })
  }, [])

  return { status, run, retry, reset, hasPendingRetry: pending !== null, pendingPayload: pending?.payload ?? null }
}

export interface ScopedCommandAttempt<P, T> {
  readonly status: AttemptStatus<T>
  readonly hasPendingRetry: boolean
  readonly pendingPayload: P | null
  readonly run: (payload: P) => Promise<CommandOutcome<T>>
  readonly retry: () => Promise<CommandOutcome<T>>
}

/** Validate only a persisted command's shape, never derive business values or treat storage as authority. */
export function hasCommandFields(value: unknown,
  shape: Readonly<Record<string, 'string' | 'number' | 'boolean' | 'object' | 'nullable-string'>>): value is Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false
  const record = value as Record<string, unknown>
  return Object.keys(record).length === Object.keys(shape).length && Object.entries(shape).every(([key, type]) => {
    const field = record[key]
    if (type === 'nullable-string') return field === null || typeof field === 'string'
    return typeof field === type && (type !== 'object' || (field !== null && !Array.isArray(field)))
  })
}

/** Optional journal for the three migrated screens; other callers keep the local hook contract. */
export function useScopedCommandAttempt<P extends object, T>(operation: string,
  execute: (payload: P, key: string) => Promise<CommandOutcome<T>>,
  accepts: (payload: unknown) => payload is P): ScopedCommandAttempt<P, T> {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const actorId = shell.activeContext?.userId
  const companyId = shell.activeContext?.companyId
  const storeKey = actorId && companyId
    ? settingsAttemptStoreKey({ sessionBinding: shell.sessionBinding, actorId, companyId }, operation) : null
  const send = (payload: unknown, key: string) => execute(payload as P, key)
  const attempt = useCommandAttempt<T>({ storeKey, execute: send, accepts, observe: () => {
    // Capture for THIS dispatch, including a retry after capabilities were refreshed; never reuse the first closure's fence.
    const captured = boundary.capture()
    return outcome => {
      if (outcome.type === 'refused' && (outcome.status === 401 || outcome.status === 403))
        boundary.report(outcome.status, captured)
    }
  } })
  return { ...attempt, pendingPayload: attempt.pendingPayload as P | null,
    run: payload => attempt.run(payload, key => send(payload, key)) }
}
