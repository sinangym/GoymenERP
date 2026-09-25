/**
 * The logical attempt behind one bank-account command (F-04: propose / approve, FIN-UI-1a).
 *
 * The same rules as ./receiptCommand.ts (ADR-011 on the client; #258 carries the outcome in the
 * BODY), for a `BankAccountCommandResult` whose resource is a `BankAccountView`:
 *  1. One Idempotency-Key per attempt IDENTITY = operation + company + actor + session + target +
 *     normalised request. Same identity → same key (double click, resend, re-submit after
 *     abandoning). A different identity is a different command.
 *  2. No trustworthy answer (no reply, undecodable body, `Unavailable`, a success about another
 *     account) is UNKNOWN: key, context and request are kept and the form is frozen. Only an
 *     explicit resend (same key) or an explicit abandon leaves it.
 *  3. After an unknown, a later non-replayed refusal (Denied / ValidationFailed / VersionConflict)
 *     is the gate turning away the RESEND (the server measures authority before the replay, §5);
 *     it proves nothing about the first request, so the attempt stays unknown. A REPLAYED refusal
 *     is the original's own answer and settles it (§7: terminal refusals are recorded).
 *  4. `IdempotencyConflict` does not prove the earlier command never wrote: frozen, no new key.
 *  5. `Succeeded` closes the attempt; the list is re-read, the command is never re-sent.
 *
 * Kept in memory only — never storage, URL or log.
 */

import {
  decodeBankAccountCommandResult,
  type BankAccountApprovalBody,
  type BankAccountIbanChangeBody,
  type BankAccountProposeRequest,
} from './bankAccountContract'
import type { BankAccountView, ValidationIssue } from './contract'
import type { PortReply } from './port'
import { generateAttemptKey, type AttemptBinding } from './receiptCommand'

export type BankAccountOperation =
  | 'bank-account-propose'
  | 'bank-account-approve'
  // D4 (#66 5749046401): the IBAN change's own Ö4 pair, on the EXISTING propose/approve permissions.
  | 'bank-account-iban-change'
  | 'bank-account-iban-change-approve'

export type BankAccountRequest =
  | { readonly operation: 'bank-account-propose'; readonly body: BankAccountProposeRequest }
  | { readonly operation: 'bank-account-approve'; readonly accountId: string; readonly body: BankAccountApprovalBody }
  | {
      readonly operation: 'bank-account-iban-change'
      readonly accountId: string
      readonly body: BankAccountIbanChangeBody
    }
  | {
      readonly operation: 'bank-account-iban-change-approve'
      readonly accountId: string
      readonly body: BankAccountApprovalBody
    }

export interface BankAccountSnapshot {
  readonly key: string
  readonly binding: AttemptBinding
  readonly request: BankAccountRequest
  readonly identity: string
}

export type BankUnknownReason = 'no-reply' | 'malformed' | 'unavailable' | 'idempotency-conflict'
export type BankRefusalReason = 'denied' | 'validation' | 'version-conflict'

export type BankSettlement =
  | { readonly type: 'succeeded'; readonly resource: BankAccountView; readonly replayed: boolean }
  | {
      readonly type: 'refused'
      readonly reason: BankRefusalReason
      readonly issues: readonly ValidationIssue[]
      readonly replayed: boolean
    }
  | { readonly type: 'idempotency-conflict' }
  | { readonly type: 'unknown'; readonly reason: Exclude<BankUnknownReason, 'idempotency-conflict'> }

/**
 * A `Succeeded` account only settles the command it answers: a proposal answers a NEW, not yet
 * active account with the proposed name and currency; an approval answers the named account, now
 * active. Any other account is not this command's answer.
 */
export function accountAnswers(request: BankAccountRequest, account: BankAccountView): boolean {
  switch (request.operation) {
    case 'bank-account-propose':
      return (
        !account.isActive &&
        account.displayName === request.body.displayName &&
        account.currencyId === request.body.currencyId
      )
    case 'bank-account-approve':
      return account.id === request.accountId && account.isActive
    // D4: both answer the named account. The account's own members do not carry the IBAN — the card
    // does, and the card is not on this view — so identity is all this can check, and claiming more
    // would be asserting something the answer does not contain. The list is re-read after settling.
    case 'bank-account-iban-change':
    case 'bank-account-iban-change-approve':
      return account.id === request.accountId
  }
}

export function classifyBankReply(reply: PortReply, companyId: string, request: BankAccountRequest): BankSettlement {
  if (reply.kind === 'no-reply') return { type: 'unknown', reason: 'no-reply' }
  const decoded = decodeBankAccountCommandResult(reply.body, companyId)
  if (!decoded.ok) return { type: 'unknown', reason: 'malformed' }
  const result = decoded.value
  switch (result.outcome) {
    case 'Succeeded':
      return accountAnswers(request, result.resource)
        ? { type: 'succeeded', resource: result.resource, replayed: result.isReplay }
        : { type: 'unknown', reason: 'malformed' }
    case 'Unavailable':
      return { type: 'unknown', reason: 'unavailable' }
    case 'IdempotencyConflict':
      return { type: 'idempotency-conflict' }
    case 'Denied':
      return { type: 'refused', reason: 'denied', issues: result.issues, replayed: result.isReplay }
    case 'ValidationFailed':
      return { type: 'refused', reason: 'validation', issues: result.issues, replayed: result.isReplay }
    case 'VersionConflict':
      return { type: 'refused', reason: 'version-conflict', issues: result.issues, replayed: result.isReplay }
  }
}

export type BankAttemptState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: BankAccountSnapshot; readonly unresolvedSince?: BankUnknownReason }
  | {
      readonly phase: 'unknown'
      readonly snapshot: BankAccountSnapshot
      readonly reason: BankUnknownReason
      readonly lastRefusal?: BankRefusalReason
    }
  | { readonly phase: 'conflicted'; readonly snapshot: BankAccountSnapshot }
  | {
      readonly phase: 'refused'
      readonly snapshot: BankAccountSnapshot
      readonly reason: BankRefusalReason
      readonly issues: readonly ValidationIssue[]
    }
  | {
      readonly phase: 'succeeded'
      readonly snapshot: BankAccountSnapshot
      readonly resource: BankAccountView
      readonly replayed: boolean
    }

export type BankAttemptEvent =
  | { readonly type: 'SUBMIT'; readonly snapshot: BankAccountSnapshot; readonly unresolvedSince?: BankUnknownReason }
  | { readonly type: 'RESEND' }
  | { readonly type: 'SETTLE'; readonly key: string; readonly settlement: BankSettlement }
  | { readonly type: 'ABANDON' }
  | { readonly type: 'LOAD'; readonly state: BankAttemptState }

export const BANK_IDLE: BankAttemptState = { phase: 'idle' }

export type BankOutstandingState = Extract<BankAttemptState, { phase: 'submitting' | 'unknown' | 'conflicted' }>

export function isBankFrozen(state: BankAttemptState): state is BankOutstandingState {
  return state.phase === 'submitting' || state.phase === 'unknown' || state.phase === 'conflicted'
}

export function bankAttemptReducer(state: BankAttemptState, event: BankAttemptEvent): BankAttemptState {
  switch (event.type) {
    case 'SUBMIT':
      if (isBankFrozen(state)) return state
      return event.unresolvedSince === undefined
        ? { phase: 'submitting', snapshot: event.snapshot }
        : { phase: 'submitting', snapshot: event.snapshot, unresolvedSince: event.unresolvedSince }
    case 'RESEND':
      return state.phase === 'unknown'
        ? { phase: 'submitting', snapshot: state.snapshot, unresolvedSince: state.reason }
        : state
    case 'ABANDON':
      return state.phase === 'unknown' || state.phase === 'conflicted' ? BANK_IDLE : state
    case 'LOAD':
      return event.state
    case 'SETTLE': {
      if (state.phase !== 'submitting' || state.snapshot.key !== event.key) return state
      const { snapshot } = state
      const s = event.settlement
      if (s.type === 'succeeded') return { phase: 'succeeded', snapshot, resource: s.resource, replayed: s.replayed }
      if (s.type === 'idempotency-conflict') return { phase: 'conflicted', snapshot }
      if (s.type === 'unknown') return { phase: 'unknown', snapshot, reason: state.unresolvedSince ?? s.reason }
      if (state.unresolvedSince !== undefined && !s.replayed) {
        return { phase: 'unknown', snapshot, reason: state.unresolvedSince, lastRefusal: s.reason }
      }
      return { phase: 'refused', snapshot, reason: s.reason, issues: s.issues }
    }
  }
}

function stable(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(stable)
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.keys(value as Record<string, unknown>)
        .sort()
        .map((key) => [key, stable((value as Record<string, unknown>)[key])]),
    )
  }
  return value
}

export function bankAttemptIdentity(binding: AttemptBinding, request: BankAccountRequest): string {
  return JSON.stringify(stable({ binding, request }))
}

/** Which account the command is about: `new` for a proposal, the account id for an approval. */
export function bankAttemptTarget(request: BankAccountRequest): string {
  return request.operation === 'bank-account-propose' ? 'new' : request.accountId
}

export { generateAttemptKey }

// ---------------------------------------------------------------------------------
// Survival store — in memory only
// ---------------------------------------------------------------------------------

export interface BankAbandonedAttempt {
  readonly snapshot: BankAccountSnapshot
  readonly reason: BankUnknownReason
}

export interface BankStoredSlot {
  readonly outstanding: BankOutstandingState | null
  readonly abandoned: readonly BankAbandonedAttempt[]
}

const EMPTY_SLOT: BankStoredSlot = { outstanding: null, abandoned: [] }

export function bankStoreKey(binding: AttemptBinding, operation: BankAccountOperation, target: string): string {
  return JSON.stringify([binding.sessionEpoch, binding.actorId, binding.companyId, operation, target])
}

const store = new Map<string, BankStoredSlot>()

function write(storeKey: string, slot: BankStoredSlot): void {
  if (slot.outstanding === null && slot.abandoned.length === 0) store.delete(storeKey)
  else store.set(storeKey, slot)
}

export function recallBankSlot(storeKey: string): BankStoredSlot {
  return store.get(storeKey) ?? EMPTY_SLOT
}

export function setBankOutstanding(storeKey: string, state: BankOutstandingState): void {
  const slot = recallBankSlot(storeKey)
  write(storeKey, {
    outstanding: state,
    abandoned: slot.abandoned.filter((entry) => entry.snapshot.key !== state.snapshot.key),
  })
}

export function abandonBankOutstanding(storeKey: string, attempt: BankAbandonedAttempt): void {
  const slot = recallBankSlot(storeKey)
  const abandoned = slot.abandoned.filter((entry) => entry.snapshot.key !== attempt.snapshot.key)
  write(storeKey, { outstanding: null, abandoned: [...abandoned, attempt] })
}

export function releaseBankKey(storeKey: string, key: string): void {
  const slot = recallBankSlot(storeKey)
  write(storeKey, {
    outstanding: slot.outstanding?.snapshot.key === key ? null : slot.outstanding,
    abandoned: slot.abandoned.filter((entry) => entry.snapshot.key !== key),
  })
}

/** Test-only. */
export function clearBankAttemptStore(): void {
  store.clear()
}

/** An identical re-submit of an abandoned attempt reuses its key and inherits its uncertainty. */
export function bankKeyForSubmit(
  slot: BankStoredSlot,
  identity: string,
  mint: () => string,
): { readonly key: string; readonly unresolvedSince?: BankUnknownReason } {
  const abandoned = slot.abandoned.find((entry) => entry.snapshot.identity === identity)
  return abandoned === undefined ? { key: mint() } : { key: abandoned.snapshot.key, unresolvedSince: abandoned.reason }
}

/** Mirror a settlement into the store, for this key only (see ./receiptCommand.ts#settleStore). */
export function settleBankStore(storeKey: string, snapshot: BankAccountSnapshot, settlement: BankSettlement): void {
  const slot = recallBankSlot(storeKey)
  if (slot.outstanding?.snapshot.key === snapshot.key) {
    const next = bankAttemptReducer(slot.outstanding, { type: 'SETTLE', key: snapshot.key, settlement })
    if (isBankFrozen(next)) setBankOutstanding(storeKey, next)
    else releaseBankKey(storeKey, snapshot.key)
    return
  }
  const definitive = settlement.type === 'succeeded' || (settlement.type === 'refused' && settlement.replayed)
  if (definitive && slot.abandoned.some((entry) => entry.snapshot.key === snapshot.key)) releaseBankKey(storeKey, snapshot.key)
}
