/**
 * Port injection and the two hooks every Finance screen uses: a context-fenced READ and
 * a context-bound COMMAND.
 *
 * Generic shell pieces are used as they are — `useShellState` / `contextKey` for the
 * company, actor, session and fence namespace; `RemoteViewState` for read states — and
 * nothing of Cari's ACK hook or Product's group-shared model is imported.
 *
 * No provider = no adapter = fail closed as `service-unavailable`. Until the Backend
 * publishes the HTTP mapping, production renders exactly that.
 */

/* eslint-disable react-refresh/only-export-components -- context + hooks live together on purpose */

import { createContext, useCallback, useContext, useEffect, useReducer, useRef, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import type { RemoteViewState } from '../shell/uiState'
import { decodeReadResult, type Decoded } from './contract'
import type { FinanceReceiptPort, FinanceScope, PortReply } from './port'
import {
  IDLE,
  attemptIdentity,
  attemptReducer,
  attemptStoreKey,
  attemptTarget,
  classifyReply,
  generateAttemptKey,
  isFrozen,
  keyForSubmit,
  recallAttempt,
  releaseAttempt,
  rememberAttempt,
  type AttemptBinding,
  type AttemptSnapshot,
  type AttemptState,
  type CommandRequest,
  type Settlement,
} from './receiptCommand'

const FinancePortContext = createContext<FinanceReceiptPort | null>(null)

export function FinancePortProvider({ port, children }: { readonly port: FinanceReceiptPort; readonly children: ReactNode }) {
  return <FinancePortContext.Provider value={port}>{children}</FinancePortContext.Provider>
}

/** The verified, bound single-company context — or null (switching, group, unbound). */
export function useFinanceBinding(): { readonly binding: AttemptBinding; readonly namespace: string } | null {
  const shell = useShellState()
  const key = contextKey(shell)
  const ctx = shell.activeContext
  if (key === null || ctx === null || ctx.scopeMode !== 'single' || ctx.companyId === null) return null
  return {
    binding: { sessionEpoch: shell.sessionEpoch, actorId: ctx.userId, companyId: ctx.companyId },
    namespace: contextKeyString(key),
  }
}

// ---------------------------------------------------------------------------------
// Reads
// ---------------------------------------------------------------------------------

type ReadTag<T> =
  | { readonly type: 'found'; readonly value: T }
  | { readonly type: 'Denied' | 'NotFound' | 'Unavailable' }
  | { readonly type: 'malformed' }
  | { readonly type: 'no-reply' }

export interface FinanceRead<T> {
  readonly state: RemoteViewState<T>
  readonly refetch: () => void
  readonly isFetching: boolean
}

/**
 * One authorized read. The query key starts with the shell context namespace, so a
 * response from another company/actor/generation lands under a key nobody observes —
 * the same fence every other lane's reads rely on.
 */
export function useFinanceRead<T>(
  name: string,
  args: readonly unknown[],
  call: (port: FinanceReceiptPort, scope: FinanceScope, signal: AbortSignal) => Promise<PortReply>,
  decode: (resource: unknown, companyId: string) => Decoded<T>,
): FinanceRead<T> {
  const port = useContext(FinancePortContext)
  const bound = useFinanceBinding()
  const companyId = bound?.binding.companyId ?? null
  const query = useQuery({
    queryKey: [bound?.namespace ?? 'unbound', 'finance', name, ...args],
    enabled: port !== null && bound !== null,
    queryFn: async ({ signal }): Promise<ReadTag<T>> => {
      if (port === null || companyId === null) return { type: 'no-reply' }
      let reply: PortReply
      try {
        reply = await call(port, { companyId }, signal)
      } catch {
        return { type: 'no-reply' }
      }
      if (reply.kind === 'no-reply') return { type: 'no-reply' }
      const decoded = decodeReadResult(reply.body, (resource) => decode(resource, companyId))
      if (!decoded.ok) return { type: 'malformed' }
      const result = decoded.value
      return result.outcome === 'Found' ? { type: 'found', value: result.resource } : { type: result.outcome }
    },
  })

  const refetch = useCallback(() => void query.refetch(), [query])
  if (port === null) return { state: { kind: 'service-unavailable' }, refetch, isFetching: false }
  if (bound === null || query.data === undefined) return { state: { kind: 'initial-loading' }, refetch, isFetching: query.isFetching }
  const tag = query.data
  const state: RemoteViewState<T> =
    tag.type === 'found'
      ? { kind: 'ready', data: tag.value }
      : tag.type === 'Denied'
        ? { kind: 'forbidden' }
        : tag.type === 'NotFound'
          ? { kind: 'not-found' }
          : tag.type === 'Unavailable'
            ? { kind: 'service-unavailable' }
            : tag.type === 'malformed'
              ? { kind: 'malformed-response' }
              : { kind: 'network-error' }
  return { state, refetch, isFetching: query.isFetching }
}

// ---------------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------------

function send(port: FinanceReceiptPort, snapshot: AttemptSnapshot): Promise<PortReply> {
  const scope = { companyId: snapshot.binding.companyId }
  const { request, key } = snapshot
  switch (request.operation) {
    case 'draft-save':
      return port.draftSave(scope, key, request.body)
    case 'record':
      return port.record(scope, key, request.body)
    case 'reverse':
      return port.reverse(scope, key, request.body)
  }
}

/** Mirror a settlement into the survival store — for this key only. */
function settleStore(storeKey: string, snapshot: AttemptSnapshot, settlement: Settlement): void {
  const stored = recallAttempt(storeKey)
  if (stored === null) return
  if (stored.phase === 'abandoned') {
    if (stored.snapshot.key !== snapshot.key) return
    // A definitive late answer settles the abandoned attempt; it never re-opens the UI.
    if (settlement.type === 'succeeded' || settlement.type === 'refused') releaseAttempt(storeKey, snapshot.key)
    return
  }
  if (stored.state.snapshot.key !== snapshot.key) return
  const next = attemptReducer(stored.state, { type: 'SETTLE', key: snapshot.key, settlement })
  if (isFrozen(next) && next.phase !== 'idle') rememberAttempt(storeKey, { phase: 'outstanding', state: next as AttemptState & { snapshot: AttemptSnapshot } })
  else releaseAttempt(storeKey, snapshot.key)
}

export type SubmitResult = 'started' | 'blocked'

export interface ReceiptCommand {
  readonly state: AttemptState
  readonly available: boolean
  submit(request: CommandRequest): SubmitResult
  resend(): SubmitResult
  abandon(): void
}

/**
 * A command bound to ONE (operation, target) in the CURRENT context.
 *
 * `target` must be the same value `attemptTarget(request)` would yield for the requests
 * this screen sends; a request for another target is refused.
 */
export function useReceiptCommand(operation: CommandRequest['operation'], target: string): ReceiptCommand {
  const port = useContext(FinancePortContext)
  const bound = useFinanceBinding()
  const storeKey = bound === null ? null : attemptStoreKey(bound.binding, operation, target)
  const [state, dispatch] = useReducer(attemptReducer, IDLE)

  // Which context this instance currently shows. A settle captured under another value
  // is a late answer from a superseded context and never becomes UI state.
  const shownStoreKey = useRef<string | null>(storeKey)
  const shownNamespace = useRef<string | null>(bound?.namespace ?? null)
  useEffect(() => {
    shownStoreKey.current = storeKey
    shownNamespace.current = bound?.namespace ?? null
  })

  // Load this slot's own attempt whenever the slot changes (mount, other company/actor/
  // session). Another slot's attempt is never shown here; its key stays in its own slot.
  useEffect(() => {
    const stored = storeKey === null ? null : recallAttempt(storeKey)
    if (storeKey === null || stored?.phase !== 'outstanding') {
      dispatch({ type: 'LOAD', state: IDLE })
      return
    }
    if (stored.state.phase !== 'submitting') {
      dispatch({ type: 'LOAD', state: stored.state })
      return
    }
    // The request was sent by an instance that no longer exists; its answer can no longer
    // reach this screen. Show it as unknown AND record it as unknown, so the same-key
    // resend this screen offers is not refused as "still in flight".
    const unknown = { phase: 'unknown' as const, snapshot: stored.state.snapshot, reason: stored.state.unresolvedSince ?? ('no-reply' as const) }
    rememberAttempt(storeKey, { phase: 'outstanding', state: unknown })
    dispatch({ type: 'LOAD', state: unknown })
  }, [storeKey])

  const run = useCallback(
    (snapshot: AttemptSnapshot, slot: string, namespace: string) => {
      if (port === null) return
      let pending: Promise<PortReply>
      try {
        pending = send(port, snapshot)
      } catch {
        pending = Promise.resolve({ kind: 'no-reply' })
      }
      void pending
        .catch((): PortReply => ({ kind: 'no-reply' }))
        .then((reply) => {
          const settlement = classifyReply(reply, snapshot.binding.companyId)
          settleStore(slot, snapshot, settlement)
          if (shownStoreKey.current !== slot || shownNamespace.current !== namespace) return
          dispatch({ type: 'SETTLE', key: snapshot.key, settlement })
        })
    },
    [port],
  )

  const submit = (request: CommandRequest): SubmitResult => {
    if (port === null || bound === null || storeKey === null) return 'blocked'
    if (request.operation !== operation || attemptTarget(request) !== target) return 'blocked'
    if (isFrozen(state)) return 'blocked'
    const stored = recallAttempt(storeKey)
    // Checked against the STORE, which is written synchronously: a second click in the
    // same tick sees the reservation even though React has not re-rendered yet.
    if (stored?.phase === 'outstanding') return 'blocked'
    const identity = attemptIdentity(bound.binding, request)
    const key = keyForSubmit(stored, identity, generateAttemptKey)
    const snapshot: AttemptSnapshot = { key, binding: bound.binding, request, identity }
    rememberAttempt(storeKey, { phase: 'outstanding', state: { phase: 'submitting', snapshot } })
    dispatch({ type: 'SUBMIT', snapshot })
    run(snapshot, storeKey, bound.namespace)
    return 'started'
  }

  const resend = (): SubmitResult => {
    if (port === null || bound === null || storeKey === null || state.phase !== 'unknown') return 'blocked'
    const stored = recallAttempt(storeKey)
    if (stored?.phase !== 'outstanding' || stored.state.phase === 'submitting') return 'blocked'
    if (stored.state.snapshot.key !== state.snapshot.key) return 'blocked'
    const snapshot = state.snapshot
    rememberAttempt(storeKey, { phase: 'outstanding', state: { phase: 'submitting', snapshot, unresolvedSince: state.reason } })
    dispatch({ type: 'RESEND' })
    run(snapshot, storeKey, bound.namespace)
    return 'started'
  }

  const abandon = () => {
    if (storeKey === null || (state.phase !== 'unknown' && state.phase !== 'conflicted')) return
    rememberAttempt(storeKey, { phase: 'abandoned', snapshot: state.snapshot })
    dispatch({ type: 'ABANDON' })
  }

  return { state, available: port !== null && bound !== null, submit, resend, abandon }
}
