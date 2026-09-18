/**
 * The logical attempt behind one Finance command (DraftSave / Record / Reverse).
 *
 * A minimal Finance adaptation of the rule `../organization/activeStateAttempt.ts` and
 * `../product/pendingAttempt.ts` enforce — written for this contract, not imported:
 * #258 carries the outcome in the BODY (`CommandResult.outcome`, `isReplay`), and has an
 * `IdempotencyConflict` outcome neither of those surfaces has. Cari's ACK protocol and
 * Product's group-shared key model do not apply.
 *
 * THE RULES (#258 README + spec #254 §4 + Business #66/5653637829 + launch lessons):
 *  1. One Idempotency-Key per attempt IDENTITY = operation + company + actor + session +
 *     normalised request. Same identity → same key, whether it is a double click, a
 *     resend, or a re-submit after abandoning. A different identity is a different command.
 *  2. No trustworthy answer (no reply, undecodable body, `Unavailable`, a success body
 *     about another record) is UNKNOWN: key, company, actor and request are kept and the
 *     form is frozen. Only an explicit resend (same key) or an explicit abandon leaves it.
 *  3. After an unknown, a later non-replayed refusal (Denied / ValidationFailed /
 *     VersionConflict) is a gate turning away the RESEND; it proves nothing about the
 *     first request, so the attempt stays unknown. A replayed refusal IS the original's.
 *     This also holds when an abandoned attempt is re-submitted with the same payload.
 *  4. `IdempotencyConflict` ("same key/different payload") does not prove the earlier
 *     command never wrote. It freezes like unknown; no automatic new key.
 *  5. A sent request is not cancelled by a company switch. Its answer never becomes UI
 *     state in another context; back in the original context it may be confirmed by a
 *     same-key resend. Nothing is sent automatically.
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

export type UnknownReason = 'no-reply' | 'malformed' | 'unavailable' | 'idempotency-conflict'
export type RefusalReason = 'denied' | 'validation' | 'version-conflict'

export type Settlement =
  | { readonly type: 'succeeded'; readonly resource: SourceDetail; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly reason: RefusalReason; readonly issues: readonly ValidationIssue[]; readonly replayed: boolean }
  | { readonly type: 'idempotency-conflict' }
  | { readonly type: 'unknown'; readonly reason: Exclude<UnknownReason, 'idempotency-conflict'> }

/**
 * A `Succeeded` resource only settles the command it answers: it must be about the record
 * the request named and, where #258 fixes it, in the stage that command produces ("A
 * Succeeded Draft resource means only draft persistence, never financial recording").
 * Another record from the same company is not this command's answer.
 */
export function resourceAnswers(request: CommandRequest, resource: SourceDetail): boolean {
  const { source } = resource
  switch (request.operation) {
    case 'draft-save':
      return source.stage === 'Draft' && (request.body.draftId === null || source.id === request.body.draftId)
    case 'record':
      return source.stage === 'Recorded' && source.id === request.body.draftId
    case 'reverse':
      // Either the original, now reversed, or the linked reversal source pointing at it.
      return source.id === request.body.sourceId || resource.reversesSourceId === request.body.sourceId
  }
}

export function classifyReply(reply: PortReply, companyId: string, request: CommandRequest): Settlement {
  if (reply.kind === 'no-reply') return { type: 'unknown', reason: 'no-reply' }
  const decoded = decodeCommandResult(reply.body, companyId)
  if (!decoded.ok) return { type: 'unknown', reason: 'malformed' }
  const result = decoded.value
  switch (result.outcome) {
    case 'Succeeded':
      return resourceAnswers(request, result.resource)
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
  /** `unresolvedSince` is set when an abandoned, still-unresolved key is being reused. */
  | { readonly type: 'SUBMIT'; readonly snapshot: AttemptSnapshot; readonly unresolvedSince?: UnknownReason }
  | { readonly type: 'RESEND' }
  | { readonly type: 'SETTLE'; readonly key: string; readonly settlement: Settlement }
  | { readonly type: 'ABANDON' }
  /** Replace the shown attempt with the one stored for the (new) slot. */
  | { readonly type: 'LOAD'; readonly state: AttemptState }

export const IDLE: AttemptState = { phase: 'idle' }

/** Frozen: a key whose outcome is not settled is outstanding. */
export function isFrozen(state: AttemptState): state is OutstandingState {
  return state.phase === 'submitting' || state.phase === 'unknown' || state.phase === 'conflicted'
}

export function attemptReducer(state: AttemptState, event: AttemptEvent): AttemptState {
  switch (event.type) {
    case 'SUBMIT':
      if (isFrozen(state)) return state
      return event.unresolvedSince === undefined
        ? { phase: 'submitting', snapshot: event.snapshot }
        : { phase: 'submitting', snapshot: event.snapshot, unresolvedSince: event.unresolvedSince }
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

export type OutstandingState = Extract<AttemptState, { phase: 'submitting' | 'unknown' | 'conflicted' }>

/** An unresolved attempt the user stepped away from. Its request may still have been recorded. */
export interface AbandonedAttempt {
  readonly snapshot: AttemptSnapshot
  readonly reason: UnknownReason
}

/**
 * One (session, actor, company, operation, target) slot. `abandoned` is a LIST: starting a
 * different payload after abandoning must not erase the earlier key, or re-submitting the
 * earlier payload later would mint a second key for a command that may already exist.
 */
export interface StoredSlot {
  readonly outstanding: OutstandingState | null
  readonly abandoned: readonly AbandonedAttempt[]
}

const EMPTY_SLOT: StoredSlot = { outstanding: null, abandoned: [] }

export function attemptStoreKey(binding: AttemptBinding, operation: ReceiptOperation, target: string): string {
  return JSON.stringify([binding.sessionEpoch, binding.actorId, binding.companyId, operation, target])
}

const store = new Map<string, StoredSlot>()

function write(storeKey: string, slot: StoredSlot): void {
  if (slot.outstanding === null && slot.abandoned.length === 0) store.delete(storeKey)
  else store.set(storeKey, slot)
}

export function recallSlot(storeKey: string): StoredSlot {
  return store.get(storeKey) ?? EMPTY_SLOT
}

/**
 * Reserve / update the outstanding attempt. Reusing an abandoned key moves it back out of
 * the abandoned list, so it lives in exactly one place.
 */
export function setOutstanding(storeKey: string, state: OutstandingState): void {
  const slot = recallSlot(storeKey)
  write(storeKey, {
    outstanding: state,
    abandoned: slot.abandoned.filter((entry) => entry.snapshot.key !== state.snapshot.key),
  })
}

/** Move the outstanding attempt into the abandoned list. */
export function abandonOutstanding(storeKey: string, attempt: AbandonedAttempt): void {
  const slot = recallSlot(storeKey)
  const abandoned = slot.abandoned.filter((entry) => entry.snapshot.key !== attempt.snapshot.key)
  write(storeKey, { outstanding: null, abandoned: [...abandoned, attempt] })
}

/** Forget a key everywhere in the slot — only for a DEFINITIVE answer to that key. */
export function releaseKey(storeKey: string, key: string): void {
  const slot = recallSlot(storeKey)
  write(storeKey, {
    outstanding: slot.outstanding?.snapshot.key === key ? null : slot.outstanding,
    abandoned: slot.abandoned.filter((entry) => entry.snapshot.key !== key),
  })
}

/** Test-only. */
export function clearAttemptStore(): void {
  store.clear()
}

/**
 * The key for a new submit. An identical re-submit of an abandoned attempt reuses its key
 * AND inherits its uncertainty, so a later non-replayed refusal cannot close it (rule 3).
 */
export function keyForSubmit(
  slot: StoredSlot,
  identity: string,
  mint: () => string,
): { readonly key: string; readonly unresolvedSince?: UnknownReason } {
  const abandoned = slot.abandoned.find((entry) => entry.snapshot.identity === identity)
  return abandoned === undefined ? { key: mint() } : { key: abandoned.snapshot.key, unresolvedSince: abandoned.reason }
}

/**
 * Mirror a settlement into the store, for this key only.
 *  - the outstanding attempt follows the reducer; once definitive it is released;
 *  - an abandoned attempt was unresolved by definition, so only a success or a REPLAYED
 *    refusal (the original's own answer) settles it.
 */
export function settleStore(storeKey: string, snapshot: AttemptSnapshot, settlement: Settlement): void {
  const slot = recallSlot(storeKey)
  if (slot.outstanding?.snapshot.key === snapshot.key) {
    const next = attemptReducer(slot.outstanding, { type: 'SETTLE', key: snapshot.key, settlement })
    if (isFrozen(next)) setOutstanding(storeKey, next)
    else releaseKey(storeKey, snapshot.key)
    return
  }
  const definitive = settlement.type === 'succeeded' || (settlement.type === 'refused' && settlement.replayed)
  if (definitive && slot.abandoned.some((entry) => entry.snapshot.key === snapshot.key)) releaseKey(storeKey, snapshot.key)
}
