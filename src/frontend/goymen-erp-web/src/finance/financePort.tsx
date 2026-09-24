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
import { useAuthBoundary } from '../shell/useAuthBoundary'
import {
  decodeReadResult,
  decodeReversalProposalCommandResult,
  type ApproveReversalRequest,
  type Decoded,
  type ProposeReversalRequest,
  type RejectReversalRequest,
  type ReversalProposalView,
  type ValidationIssue,
} from './contract'
import { decodeDraftContext, type DraftContext, type DraftContextRequest } from './draftContext'
import { decodeManualBankPaymentDraftContext, type ManualBankPaymentDraftContext,
  type ManualBankPaymentDraftContextRequest } from './manualBankPaymentDraftContext'
import type { HttpReadReply } from './http'
import type { FinanceReceiptPort, FinanceScope, PortReply } from './port'
import {
  IDLE,
  abandonOutstanding,
  attemptIdentity,
  attemptReducer,
  attemptStoreKey,
  attemptTarget,
  classifyReply,
  generateAttemptKey,
  isFrozen,
  keyForSubmit,
  recallSlot,
  setOutstanding,
  settleStore,
  type AttemptBinding,
  type AttemptSnapshot,
  type AttemptState,
  type CommandRequest,
  type OutstandingState,
  type UnknownReason,
} from './receiptCommand'
import {
  BANK_IDLE,
  abandonBankOutstanding,
  bankAttemptIdentity,
  bankAttemptReducer,
  bankAttemptTarget,
  bankKeyForSubmit,
  bankStoreKey,
  classifyBankReply,
  isBankFrozen,
  recallBankSlot,
  setBankOutstanding,
  settleBankStore,
  type BankAccountOperation,
  type BankAccountRequest,
  type BankAccountSnapshot,
  type BankAttemptState,
  type BankOutstandingState,
  type BankUnknownReason,
} from './bankAccountCommand'

const FinancePortContext = createContext<FinanceReceiptPort | null>(null)

export function FinancePortProvider({ port, children }: { readonly port: FinanceReceiptPort; readonly children: ReactNode }) {
  return <FinancePortContext.Provider value={port}>{children}</FinancePortContext.Provider>
}

export function useFinancePort(): FinanceReceiptPort | null {
  return useContext(FinancePortContext)
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
// Draft read context (published HTTP read)
// ---------------------------------------------------------------------------------

export type DraftContextState =
  /** Bank account and a valid date are not both chosen yet: nothing is requested. */
  | { readonly kind: 'needs-selection' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly context: DraftContext }
  | { readonly kind: 'invalid-request' }
  | { readonly kind: 'forbidden' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'unavailable' }
  | { readonly kind: 'unauthenticated' }
  | { readonly kind: 'malformed' }
  | { readonly kind: 'network-error' }

export interface DraftContextRead {
  readonly state: DraftContextState
  readonly refetch: () => void
}

type ContextTag =
  | { readonly type: 'ready'; readonly context: DraftContext }
  | { readonly type: 'status'; readonly status: number }
  | { readonly type: 'malformed' }
  | { readonly type: 'no-reply' }

/**
 * The effective profile/currency/Cari label for exactly (company, bank, party, account, date).
 *
 * All five are in the query key, so a context read for another date or account can never be
 * the one a save uses — changing the date is a new key with no data until its own answer
 * arrives. A 401/403 is reported to the shared auth boundary from the query itself, like every
 * other business read.
 */
export function useDraftContext(request: DraftContextRequest | null): DraftContextRead {
  const port = useContext(FinancePortContext)
  const bound = useFinanceBinding()
  const boundary = useAuthBoundary()
  const companyId = bound?.binding.companyId ?? null
  const query = useQuery({
    queryKey: [
      bound?.namespace ?? 'unbound',
      'finance',
      'draft-context',
      request?.treasuryAccountId ?? '',
      request?.partyId ?? '',
      request?.companyAccountId ?? '',
      request?.occurredOn ?? '',
    ],
    enabled: port !== null && bound !== null && request !== null,
    queryFn: async ({ signal }): Promise<ContextTag> => {
      if (port === null || companyId === null || request === null) return { type: 'no-reply' }
      const snapshot = boundary.capture()
      let reply: HttpReadReply
      try {
        reply = await port.draftContext({ companyId }, request, signal)
      } catch {
        return { type: 'no-reply' }
      }
      if (reply.kind === 'no-reply') return { type: 'no-reply' }
      if (reply.kind === 'status') {
        boundary.report(reply.status, snapshot)
        return { type: 'status', status: reply.status }
      }
      const decoded = decodeDraftContext(reply.body, companyId, request)
      return decoded.ok ? { type: 'ready', context: decoded.value } : { type: 'malformed' }
    },
  })

  const refetch = useCallback(() => void query.refetch(), [query])
  if (request === null) return { state: { kind: 'needs-selection' }, refetch }
  if (port === null) return { state: { kind: 'unavailable' }, refetch }
  const tag = query.data
  if (bound === null || tag === undefined || query.isFetching) return { state: { kind: 'loading' }, refetch }
  if (tag.type === 'ready') return { state: { kind: 'ready', context: tag.context }, refetch }
  if (tag.type === 'malformed') return { state: { kind: 'malformed' }, refetch }
  if (tag.type === 'no-reply') return { state: { kind: 'network-error' }, refetch }
  const byStatus: Readonly<Record<number, DraftContextState>> = {
    400: { kind: 'invalid-request' },
    401: { kind: 'unauthenticated' },
    403: { kind: 'forbidden' },
    404: { kind: 'not-found' },
    503: { kind: 'unavailable' },
  }
  return { state: byStatus[tag.status] ?? { kind: 'unavailable' }, refetch }
}

export type ManualBankPaymentDraftContextState =
  | { readonly kind: 'needs-selection' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly context: ManualBankPaymentDraftContext }
  | { readonly kind: 'invalid-request' }
  | { readonly kind: 'forbidden' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'unavailable' }
  | { readonly kind: 'unauthenticated' }
  | { readonly kind: 'malformed' }
  | { readonly kind: 'network-error' }

type ManualBankPaymentContextTag =
  | { readonly type: 'ready'; readonly context: ManualBankPaymentDraftContext }
  | { readonly type: 'status'; readonly status: number }
  | { readonly type: 'malformed' }
  | { readonly type: 'no-reply' }

export function useManualBankPaymentDraftContext(request: ManualBankPaymentDraftContextRequest | null) {
  const port = useContext(FinancePortContext)
  const bound = useFinanceBinding()
  const boundary = useAuthBoundary()
  const companyId = bound?.binding.companyId ?? null
  const query = useQuery({
    queryKey: [bound?.namespace ?? 'unbound', 'finance', 'manual-bank-payment-draft-context',
      request?.treasuryAccountId ?? '', request?.partyId ?? '', request?.companyAccountId ?? '',
      request?.occurredOn ?? ''],
    enabled: port !== null && bound !== null && request !== null,
    queryFn: async ({ signal }): Promise<ManualBankPaymentContextTag> => {
      if (port === null || companyId === null || request === null) return { type: 'no-reply' }
      const snapshot = boundary.capture()
      let reply: HttpReadReply
      try {
        reply = await port.manualBankPaymentDraftContext({ companyId }, request, signal)
      } catch {
        return { type: 'no-reply' }
      }
      if (reply.kind === 'no-reply') return { type: 'no-reply' }
      if (reply.kind === 'status') {
        boundary.report(reply.status, snapshot)
        return { type: 'status', status: reply.status }
      }
      const decoded = decodeManualBankPaymentDraftContext(reply.body, companyId, request)
      return decoded.ok ? { type: 'ready', context: decoded.value } : { type: 'malformed' }
    },
  })
  const refetch = useCallback(() => void query.refetch(), [query])
  if (request === null) return { state: { kind: 'needs-selection' } as const, refetch }
  if (port === null) return { state: { kind: 'unavailable' } as const, refetch }
  const tag = query.data
  if (bound === null || tag === undefined || query.isFetching)
    return { state: { kind: 'loading' } as const, refetch }
  if (tag.type === 'ready') return { state: { kind: 'ready' as const, context: tag.context }, refetch }
  if (tag.type === 'malformed') return { state: { kind: 'malformed' } as const, refetch }
  if (tag.type === 'no-reply') return { state: { kind: 'network-error' } as const, refetch }
  const byStatus: Readonly<Record<number, ManualBankPaymentDraftContextState>> = {
    400: { kind: 'invalid-request' }, 401: { kind: 'unauthenticated' }, 403: { kind: 'forbidden' },
    404: { kind: 'not-found' }, 503: { kind: 'unavailable' },
  }
  return { state: byStatus[tag.status] ?? { kind: 'unavailable' }, refetch }
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
    const outstanding = storeKey === null ? null : recallSlot(storeKey).outstanding
    if (storeKey === null || outstanding === null) {
      dispatch({ type: 'LOAD', state: IDLE })
      return
    }
    if (outstanding.phase !== 'submitting') {
      dispatch({ type: 'LOAD', state: outstanding })
      return
    }
    // The request was sent by an instance that no longer exists; its answer can no longer
    // reach this screen. Show it as unknown AND record it as unknown, so the same-key
    // resend this screen offers is not refused as "still in flight".
    const unknown: OutstandingState = {
      phase: 'unknown',
      snapshot: outstanding.snapshot,
      reason: outstanding.unresolvedSince ?? 'no-reply',
    }
    setOutstanding(storeKey, unknown)
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
          const settlement = classifyReply(reply, snapshot.binding.companyId, snapshot.request)
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
    const slot = recallSlot(storeKey)
    // Checked against the STORE, which is written synchronously: a second submit from the
    // same stale render sees the reservation even though React has not re-rendered yet.
    if (slot.outstanding !== null) return 'blocked'
    const identity = attemptIdentity(bound.binding, request)
    const { key, unresolvedSince } = keyForSubmit(slot, identity, generateAttemptKey)
    const snapshot: AttemptSnapshot = { key, binding: bound.binding, request, identity }
    setOutstanding(
      storeKey,
      unresolvedSince === undefined ? { phase: 'submitting', snapshot } : { phase: 'submitting', snapshot, unresolvedSince },
    )
    dispatch({ type: 'SUBMIT', snapshot, unresolvedSince })
    run(snapshot, storeKey, bound.namespace)
    return 'started'
  }

  const resend = (): SubmitResult => {
    if (port === null || bound === null || storeKey === null || state.phase !== 'unknown') return 'blocked'
    const outstanding = recallSlot(storeKey).outstanding
    if (outstanding === null || outstanding.phase !== 'unknown' || outstanding.snapshot.key !== state.snapshot.key) return 'blocked'
    const snapshot = state.snapshot
    setOutstanding(storeKey, { phase: 'submitting', snapshot, unresolvedSince: state.reason })
    dispatch({ type: 'RESEND' })
    run(snapshot, storeKey, bound.namespace)
    return 'started'
  }

  const abandon = () => {
    if (storeKey === null || (state.phase !== 'unknown' && state.phase !== 'conflicted')) return
    const reason: UnknownReason = state.phase === 'unknown' ? state.reason : 'idempotency-conflict'
    abandonOutstanding(storeKey, { snapshot: state.snapshot, reason })
    dispatch({ type: 'ABANDON' })
  }

  return { state, available: port !== null && bound !== null, submit, resend, abandon }
}

// ---------------------------------------------------------------------------------
// FIN-T06 maker-checker reversal commands
// ---------------------------------------------------------------------------------

export type ReversalOperation = 'propose' | 'approve' | 'reject'
export type ReversalRequest =
  | { readonly operation: 'propose'; readonly body: ProposeReversalRequest }
  | { readonly operation: 'approve'; readonly body: ApproveReversalRequest }
  | { readonly operation: 'reject'; readonly body: RejectReversalRequest }

export type ReversalCommandState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly key: string; readonly request: ReversalRequest }
  | { readonly phase: 'unknown'; readonly key: string; readonly request: ReversalRequest; readonly reason: 'no-reply' | 'malformed' | 'unavailable' }
  | { readonly phase: 'conflicted'; readonly key: string; readonly request: ReversalRequest }
  | { readonly phase: 'refused'; readonly request: ReversalRequest; readonly reason: 'denied' | 'validation' | 'version-conflict'; readonly issues: readonly ValidationIssue[] }
  | { readonly phase: 'succeeded'; readonly resource: ReversalProposalView; readonly replayed: boolean }

type ReversalUnknownReason = 'no-reply' | 'malformed' | 'unavailable'

interface StoredReversalAttempt {
  readonly key: string
  readonly request: ReversalRequest
  readonly reason: ReversalUnknownReason
  readonly unresolvedSince?: ReversalUnknownReason
}

const reversalAttempts = new Map<string, StoredReversalAttempt>()

export function clearReversalAttemptStore(): void {
  reversalAttempts.clear()
}

function reversalTarget(request: ReversalRequest): string {
  return request.operation === 'propose' ? request.body.sourceId : request.body.proposalId
}

export interface ReversalCommand {
  readonly state: ReversalCommandState
  readonly available: boolean
  submit(request: ReversalRequest): SubmitResult
  resend(): SubmitResult
  abandon(): void
}

export function useReversalCommand(operation: ReversalOperation, target: string): ReversalCommand {
  const port = useContext(FinancePortContext)
  const bound = useFinanceBinding()
  const slot = bound === null ? null : JSON.stringify([bound.namespace, 'finance-reversal', operation, target])
  const stored = slot === null ? undefined : reversalAttempts.get(slot)
  const [state, setState] = useReducer(
    (_: ReversalCommandState, next: ReversalCommandState) => next,
    stored === undefined
      ? { phase: 'idle' }
      : { phase: 'unknown', key: stored.key, request: stored.request, reason: stored.reason },
  )
  const shownSlot = useRef(slot)
  useEffect(() => {
    shownSlot.current = slot
    const current = slot === null ? undefined : reversalAttempts.get(slot)
    if (slot !== null && current !== undefined && current.unresolvedSince === undefined) {
      reversalAttempts.set(slot, { ...current, unresolvedSince: current.reason })
    }
    setState(current === undefined
      ? { phase: 'idle' }
      : { phase: 'unknown', key: current.key, request: current.request, reason: current.reason })
  }, [slot])

  const run = useCallback((key: string, request: ReversalRequest, currentSlot: string, companyId: string) => {
    if (port === null) return
    const scope = { companyId }
    let pending: Promise<PortReply>
    try {
      pending = request.operation === 'propose'
        ? port.proposeReversal(scope, key, request.body)
        : request.operation === 'approve'
          ? port.approveReversal(scope, key, request.body)
          : port.rejectReversal(scope, key, request.body)
    } catch {
      pending = Promise.resolve({ kind: 'no-reply' })
    }
    void pending.catch((): PortReply => ({ kind: 'no-reply' })).then((reply) => {
      const active = reversalAttempts.get(currentSlot)
      const unresolvedSince = active?.unresolvedSince
      if (reply.kind === 'no-reply') {
        reversalAttempts.set(currentSlot, { key, request, reason: 'no-reply', unresolvedSince: unresolvedSince ?? 'no-reply' })
        if (shownSlot.current === currentSlot) setState({ phase: 'unknown', key, request, reason: 'no-reply' })
        return
      }
      const decoded = decodeReversalProposalCommandResult(reply.body)
      if (!decoded.ok) {
        reversalAttempts.set(currentSlot, { key, request, reason: 'malformed', unresolvedSince: unresolvedSince ?? 'malformed' })
        if (shownSlot.current === currentSlot) setState({ phase: 'unknown', key, request, reason: 'malformed' })
        return
      }
      const result = decoded.value
      if (result.outcome === 'Unavailable') {
        reversalAttempts.set(currentSlot, { key, request, reason: 'unavailable', unresolvedSince: unresolvedSince ?? 'unavailable' })
        if (shownSlot.current === currentSlot) setState({ phase: 'unknown', key, request, reason: 'unavailable' })
        return
      }
      if (unresolvedSince !== undefined && !result.isReplay
        && result.outcome !== 'Succeeded' && result.outcome !== 'IdempotencyConflict') {
        reversalAttempts.set(currentSlot, { key, request, reason: unresolvedSince, unresolvedSince })
        if (shownSlot.current === currentSlot) {
          setState({ phase: 'unknown', key, request, reason: unresolvedSince })
        }
        return
      }
      reversalAttempts.delete(currentSlot)
      if (shownSlot.current !== currentSlot) return
      if (result.outcome === 'Succeeded') {
        setState({ phase: 'succeeded', resource: result.resource, replayed: result.isReplay })
      } else if (result.outcome === 'IdempotencyConflict') {
        setState({ phase: 'conflicted', key, request })
      } else {
        setState({
          phase: 'refused',
          request,
          reason: result.outcome === 'Denied' ? 'denied'
            : result.outcome === 'VersionConflict' ? 'version-conflict' : 'validation',
          issues: result.issues,
        })
      }
    })
  }, [port])

  const submit = (request: ReversalRequest): SubmitResult => {
    if (port === null || bound === null || slot === null || request.operation !== operation
      || reversalTarget(request) !== target || state.phase === 'submitting' || state.phase === 'unknown'
      || state.phase === 'conflicted') return 'blocked'
    const previous = reversalAttempts.get(slot)
    // State updates are asynchronous. The store reservation is synchronous, so a second submit
    // from the same stale render cannot issue another request or allocate another key.
    if (previous !== undefined) return 'blocked'
    const key = generateAttemptKey()
    reversalAttempts.set(slot, { key, request, reason: 'no-reply' })
    setState({ phase: 'submitting', key, request })
    run(key, request, slot, bound.binding.companyId)
    return 'started'
  }

  const resend = (): SubmitResult => {
    if (bound === null || slot === null || state.phase !== 'unknown') return 'blocked'
    setState({ phase: 'submitting', key: state.key, request: state.request })
    run(state.key, state.request, slot, bound.binding.companyId)
    return 'started'
  }

  const abandon = () => {
    if (slot === null || (state.phase !== 'unknown' && state.phase !== 'conflicted')) return
    reversalAttempts.delete(slot)
    setState({ phase: 'idle' })
  }

  return { state, available: port !== null && bound !== null, submit, resend, abandon }
}

// ---------------------------------------------------------------------------------
// Bank-account commands (F-04: propose / approve, FIN-UI-1a)
// ---------------------------------------------------------------------------------

function sendBank(port: FinanceReceiptPort, snapshot: BankAccountSnapshot): Promise<PortReply> {
  const scope = { companyId: snapshot.binding.companyId }
  const { request, key } = snapshot
  // A switch rather than a ternary chain: each operation names its own call, so a new one added to
  // BankAccountRequest cannot silently fall into another operation's branch.
  switch (request.operation) {
    case 'bank-account-propose':
      return port.proposeBankAccount(scope, key, request.body)
    case 'bank-account-approve':
      return port.approveBankAccount(scope, key, request.accountId, request.body)
    case 'bank-account-iban-change':
      return port.proposeBankAccountIbanChange(scope, key, request.accountId, request.body)
    case 'bank-account-iban-change-approve':
      return port.approveBankAccountIbanChange(scope, key, request.accountId, request.body)
  }
}

export interface BankAccountCommand {
  readonly state: BankAttemptState
  readonly available: boolean
  submit(request: BankAccountRequest): SubmitResult
  resend(): SubmitResult
  abandon(): void
}

/**
 * A bank-account command bound to ONE (operation, target) in the CURRENT context — the same
 * discipline as `useReceiptCommand`: the key is reserved in the store before the request leaves,
 * an unresolved attempt survives a remount and is offered only as a same-key resend, and an answer
 * captured under another context never becomes UI state here.
 */
export function useBankAccountCommand(operation: BankAccountOperation, target: string): BankAccountCommand {
  const port = useContext(FinancePortContext)
  const bound = useFinanceBinding()
  const storeKey = bound === null ? null : bankStoreKey(bound.binding, operation, target)
  const [state, dispatch] = useReducer(bankAttemptReducer, BANK_IDLE)

  const shownStoreKey = useRef<string | null>(storeKey)
  const shownNamespace = useRef<string | null>(bound?.namespace ?? null)
  useEffect(() => {
    shownStoreKey.current = storeKey
    shownNamespace.current = bound?.namespace ?? null
  })

  useEffect(() => {
    const outstanding = storeKey === null ? null : recallBankSlot(storeKey).outstanding
    if (storeKey === null || outstanding === null) {
      dispatch({ type: 'LOAD', state: BANK_IDLE })
      return
    }
    if (outstanding.phase !== 'submitting') {
      dispatch({ type: 'LOAD', state: outstanding })
      return
    }
    // Sent by an instance that no longer exists: its answer cannot reach this screen.
    const unknown: BankOutstandingState = {
      phase: 'unknown',
      snapshot: outstanding.snapshot,
      reason: outstanding.unresolvedSince ?? 'no-reply',
    }
    setBankOutstanding(storeKey, unknown)
    dispatch({ type: 'LOAD', state: unknown })
  }, [storeKey])

  const run = useCallback(
    (snapshot: BankAccountSnapshot, slot: string, namespace: string) => {
      if (port === null) return
      let pending: Promise<PortReply>
      try {
        pending = sendBank(port, snapshot)
      } catch {
        pending = Promise.resolve({ kind: 'no-reply' })
      }
      void pending
        .catch((): PortReply => ({ kind: 'no-reply' }))
        .then((reply) => {
          const settlement = classifyBankReply(reply, snapshot.binding.companyId, snapshot.request)
          settleBankStore(slot, snapshot, settlement)
          if (shownStoreKey.current !== slot || shownNamespace.current !== namespace) return
          dispatch({ type: 'SETTLE', key: snapshot.key, settlement })
        })
    },
    [port],
  )

  const submit = (request: BankAccountRequest): SubmitResult => {
    if (port === null || bound === null || storeKey === null) return 'blocked'
    if (request.operation !== operation || bankAttemptTarget(request) !== target) return 'blocked'
    if (isBankFrozen(state)) return 'blocked'
    const slot = recallBankSlot(storeKey)
    if (slot.outstanding !== null) return 'blocked'
    const identity = bankAttemptIdentity(bound.binding, request)
    const { key, unresolvedSince } = bankKeyForSubmit(slot, identity, generateAttemptKey)
    const snapshot: BankAccountSnapshot = { key, binding: bound.binding, request, identity }
    setBankOutstanding(
      storeKey,
      unresolvedSince === undefined ? { phase: 'submitting', snapshot } : { phase: 'submitting', snapshot, unresolvedSince },
    )
    dispatch({ type: 'SUBMIT', snapshot, unresolvedSince })
    run(snapshot, storeKey, bound.namespace)
    return 'started'
  }

  const resend = (): SubmitResult => {
    if (port === null || bound === null || storeKey === null || state.phase !== 'unknown') return 'blocked'
    const outstanding = recallBankSlot(storeKey).outstanding
    if (outstanding === null || outstanding.phase !== 'unknown' || outstanding.snapshot.key !== state.snapshot.key) return 'blocked'
    const snapshot = state.snapshot
    setBankOutstanding(storeKey, { phase: 'submitting', snapshot, unresolvedSince: state.reason })
    dispatch({ type: 'RESEND' })
    run(snapshot, storeKey, bound.namespace)
    return 'started'
  }

  const abandon = () => {
    if (storeKey === null || (state.phase !== 'unknown' && state.phase !== 'conflicted')) return
    const reason: BankUnknownReason = state.phase === 'unknown' ? state.reason : 'idempotency-conflict'
    abandonBankOutstanding(storeKey, { snapshot: state.snapshot, reason })
    dispatch({ type: 'ABANDON' })
  }

  return { state, available: port !== null && bound !== null, submit, resend, abandon }
}
