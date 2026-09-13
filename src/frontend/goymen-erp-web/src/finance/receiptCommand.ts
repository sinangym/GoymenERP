/**
 * The logical attempt behind one Finance command (DraftSave / Record / Reverse).
 *
 * A minimal Finance adaptation of the rule `../organization/activeStateAttempt.ts` and
 * `../product/pendingAttempt.ts` enforce — written for this contract, not imported:
 * #258 carries the outcome in the BODY (`CommandResult.outcome`, `isReplay`), and has an
 * `IdempotencyConflict` outcome neither of those surfaces has. Cari's ACK protocol and
 * Product's group-shared key model do not apply.
 *
 * THE RULES (#258 README + spec #254 §4 + launch lessons):
 *  1. One Idempotency-Key per attempt IDENTITY = operation + company + actor + session +
 *     normalised request. Same identity → same key, whether it is a double click, a
 *     resend, or a re-submit after abandoning. A different identity is a different command.
 *  2. No trustworthy answer (no reply, undecodable body, `Unavailable`) is UNKNOWN: the
 *     key, company, actor and request are kept and the form is frozen. Only an explicit
 *     resend (same key) or an explicit abandon leaves that state. Nothing resends on its own.
 *  3. After an unknown, a later non-replayed refusal (Denied / ValidationFailed /
 *     VersionConflict) is a gate turning away the RESEND; it proves nothing about the
 *     first request, so the attempt stays unknown. A replayed refusal IS the original's.
 *  4. `IdempotencyConflict` ("same key/different payload") does not prove the earlier
 *     command never wrote. It freezes like unknown; no automatic new key.
 *  5. A late settle for a key the screen no longer shows, or from another company
 *     context, never becomes UI state. Abandoning keeps the key on record so a later
 *     identical submit reuses it; a late answer cannot re-open the abandoned attempt.
 *  6. `Succeeded` closes the attempt. If the fresh read afterwards fails, the command is
 *     NOT sent again — the screen offers a re-read, not a second Record.
 */

import {
  decodeCommandResult,
  type DraftSaveRequest,
  type RecordRequest,
  type ReverseRequest,
  type SourceDetail,
  type ValidationIssue,
} from './contract'
import type { PortReply } from './port'

export type ReceiptOperation = 'draft-save' | 'record' | 'reverse'

export type CommandRequest =
  | { readonly operation: 'draft-save'; readonly body: DraftSaveRequest }
  | { readonly operation: 'record'; readonly body: RecordRequest }
  | { readonly operation: 'reverse'; readonly body: ReverseRequest }

export interface AttemptBinding {
  readonly sessionEpoch: number
  readonly actorId: string
  readonly companyId: string
}

export interface AttemptSnapshot {
  readonly key: string
  readonly binding: AttemptBinding
  readonly request: CommandRequest
  readonly identity: string
}

export type UnknownReason = 'no-reply' | 'malformed' | 'unavailable'
export type RefusalReason = 'denied' | 'validation' | 'version-conflict'

export type Settlement =
  | { readonly type: 'succeeded'; readonly resource: SourceDetail; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly reason: RefusalReason; readonly issues: readonly ValidationIssue[]; readonly replayed: boolean }
  | { readonly type: 'idempotency-conflict' }
  | { readonly type: 'unknown'; readonly reason: UnknownReason }

export function classifyReply(reply: PortReply, companyId: string): Settlement {
  if (reply.kind === 'no-reply') return { type: 'unknown', reason: 'no-reply' }
  const decoded = decodeCommandResult(reply.body, companyId)
  if (!decoded.ok) return { type: 'unknown', reason: 'malformed' }
  const result = decoded.value
  switch (result.outcome) {
    case 'Succeeded':
      return { type: 'succeeded', resource: result.resource, replayed: result.isReplay }
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

export type AttemptState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: AttemptSnapshot; readonly unresolvedSince?: UnknownReason }
  | {
      readonly phase: 'unknown'
      readonly snapshot: AttemptSnapshot
      readonly reason: UnknownReason
      readonly lastRefusal?: RefusalReason
    }
  | { readonly phase: 'conflicted'; readonly snapshot: AttemptSnapshot }
  | {
      readonly phase: 'refused'
      readonly snapshot: AttemptSnapshot
      readonly reason: RefusalReason
      readonly issues: readonly ValidationIssue[]
    }
  | { readonly phase: 'succeeded'; readonly snapshot: AttemptSnapshot; readonly resource: SourceDetail; readonly replayed: boolean }

export type AttemptEvent =
  | { readonly type: 'SUBMIT'; readonly snapshot: AttemptSnapshot }
  | { readonly type: 'RESEND' }
  | { readonly type: 'SETTLE'; readonly key: string; readonly settlement: Settlement }
  | { readonly type: 'ABANDON' }
  /** Replace the shown attempt with the one stored for the (new) slot. */
  | { readonly type: 'LOAD'; readonly state: AttemptState }

export const IDLE: AttemptState = { phase: 'idle' }

/** Frozen: a key whose outcome is not settled is outstanding. */
export function isFrozen(state: AttemptState): boolean {
  return state.phase === 'submitting' || state.phase === 'unknown' || state.phase === 'conflicted'
}

export function attemptReducer(state: AttemptState, event: AttemptEvent): AttemptState {
  switch (event.type) {
    case 'SUBMIT':
      return isFrozen(state) ? state : { phase: 'submitting', snapshot: event.snapshot }
    case 'RESEND':
      return state.phase === 'unknown'
        ? { phase: 'submitting', snapshot: state.snapshot, unresolvedSince: state.reason }
        : state
    case 'ABANDON':
      return state.phase === 'unknown' || state.phase === 'conflicted' ? IDLE : state
    case 'LOAD':
      return event.state
    case 'SETTLE': {
      if (state.phase !== 'submitting' || state.snapshot.key !== event.key) return state
      const { snapshot } = state
      const s = event.settlement
      if (s.type === 'succeeded') return { phase: 'succeeded', snapshot, resource: s.resource, replayed: s.replayed }
      if (s.type === 'idempotency-conflict') return { phase: 'conflicted', snapshot }
      if (s.type === 'unknown') {
        return { phase: 'unknown', snapshot, reason: state.unresolvedSince ?? s.reason }
      }
      if (state.unresolvedSince !== undefined && !s.replayed) {
        return { phase: 'unknown', snapshot, reason: state.unresolvedSince, lastRefusal: s.reason }
      }
      return { phase: 'refused', snapshot, reason: s.reason, issues: s.issues }
    }
  }
}

// ---------------------------------------------------------------------------------
// Attempt identity and key
// ---------------------------------------------------------------------------------

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

export function attemptIdentity(binding: AttemptBinding, request: CommandRequest): string {
  return JSON.stringify(stable({ binding, request }))
}

/** 128 random bits as hex. `getRandomValues`, not `randomUUID` (secure-context only). */
export function generateAttemptKey(source: Pick<Crypto, 'getRandomValues'> = globalThis.crypto): string {
  const bytes = new Uint8Array(16)
  source.getRandomValues(bytes)
  let key = ''
  for (const byte of bytes) key += byte.toString(16).padStart(2, '0')
  return key
}

/** Which record the command is about: a draft id, `new` for a create, a source id. */
export function attemptTarget(request: CommandRequest): string {
  switch (request.operation) {
    case 'draft-save':
      return request.body.draftId ?? 'new'
    case 'record':
      return request.body.draftId
    case 'reverse':
      return request.body.sourceId
  }
}

// ---------------------------------------------------------------------------------
// Survival store — in memory only; never storage, URL or log
// ---------------------------------------------------------------------------------

export type StoredAttempt =
  | { readonly phase: 'outstanding'; readonly state: AttemptState & { readonly snapshot: AttemptSnapshot } }
  /** Abandoned by the user while unresolved. Kept so an identical re-submit reuses the key. */
  | { readonly phase: 'abandoned'; readonly snapshot: AttemptSnapshot }

export function attemptStoreKey(binding: AttemptBinding, operation: ReceiptOperation, target: string): string {
  return JSON.stringify([binding.sessionEpoch, binding.actorId, binding.companyId, operation, target])
}

const store = new Map<string, StoredAttempt>()

export function recallAttempt(storeKey: string): StoredAttempt | null {
  return store.get(storeKey) ?? null
}

export function rememberAttempt(storeKey: string, entry: StoredAttempt): void {
  store.set(storeKey, entry)
}

/** Only the attempt with this key may be released — never a newer one in the same slot. */
export function releaseAttempt(storeKey: string, key: string): void {
  const current = store.get(storeKey)
  if (current === undefined) return
  const currentKey = current.phase === 'abandoned' ? current.snapshot.key : current.state.snapshot.key
  if (currentKey === key) store.delete(storeKey)
}

/** Test-only. */
export function clearAttemptStore(): void {
  store.clear()
}

/**
 * The key for a new submit: reuse the stored key when the identity matches (resend,
 * double submit after remount, identical re-submit after abandon); otherwise mint one.
 */
export function keyForSubmit(stored: StoredAttempt | null, identity: string, mint: () => string): string {
  if (stored === null) return mint()
  const snapshot = stored.phase === 'abandoned' ? stored.snapshot : stored.state.snapshot
  return snapshot.identity === identity ? snapshot.key : mint()
}
