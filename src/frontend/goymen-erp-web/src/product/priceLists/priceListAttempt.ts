/**
 * One logical command attempt on a price list — pure and framework-independent, the Sales/Logistics attempt rules
 * (ADR-011) in the small:
 *  - one Idempotency-Key per submitted SNAPSHOT (path + canonical payload + binding), minted when it is first sent;
 *  - no response, a 5xx or an unreadable success: the command MAY have applied. The outcome is unknown and the key is
 *    kept; the person retries the SAME snapshot (same key) or explicitly leaves it;
 *  - while an outcome is unknown a DIFFERENT payload does not quietly open a second attempt;
 *  - an outcome is applied only to the attempt AND binding that sent it (a late answer from a company the person has
 *    left writes nothing).
 * Module memory only: an unresolved attempt survives a route remount in this SPA, while the key and payload never
 * reach browser storage, a URL or a log.
 */

import type { ShellState } from '../../shell/shellState'
import type { PriceListCommand, PriceListCommandOutcome } from './api'
import type { PriceListProblem, PriceListResult } from './contract'

export interface PriceListSnapshot {
  readonly key: string
  readonly binding: string
  readonly companyId: string
  readonly command: PriceListCommand
  readonly fingerprint: string
}

export type PriceListAttempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: PriceListSnapshot }
  | { readonly phase: 'unknown'; readonly snapshot: PriceListSnapshot; readonly reason: PriceListUnknownReason }
  | { readonly phase: 'abandoned'; readonly snapshot: PriceListSnapshot; readonly reason: PriceListUnknownReason }
  | { readonly phase: 'failed'; readonly snapshot: PriceListSnapshot; readonly status: number; readonly problem: PriceListProblem;
      readonly unresolvedSince?: PriceListUnknownReason }
  | { readonly phase: 'completed'; readonly snapshot: PriceListSnapshot; readonly result: PriceListResult }

export type PriceListSubmitDecision =
  | { readonly type: 'send'; readonly snapshot: PriceListSnapshot }
  | { readonly type: 'blocked'; readonly reason: 'in-flight' | 'unresolved-other-payload' | 'no-context' }

export type PriceListUnknownReason = Extract<PriceListCommandOutcome, { readonly type: 'unknown' }>['reason']

const retained = new Map<string, Exclude<PriceListAttempt, { readonly phase: 'idle' }>>()
const settled = new Set<string>()

/** Actor, session, company and screen scope; null without a committed single-company context. */
export function priceListBinding(shell: ShellState, scope: string): string | null {
  const context = shell.activeContext
  if (context === null || context.scopeMode !== 'single' || context.companyId === null) return null
  return JSON.stringify([context.userId, String(shell.sessionEpoch), context.companyId, scope])
}

export function fingerprintOf(command: PriceListCommand): string {
  return JSON.stringify(command)
}

export function decidePriceListSubmit(
  state: PriceListAttempt,
  request: { readonly binding: string | null; readonly companyId: string | null; readonly command: PriceListCommand },
  newKey: () => string,
): PriceListSubmitDecision {
  if (request.binding === null || request.companyId === null) return { type: 'blocked', reason: 'no-context' }
  if (state.phase === 'sending') return { type: 'blocked', reason: 'in-flight' }
  const fingerprint = fingerprintOf(request.command)
  const live = livePriceListSnapshot(state)
  if (live !== null) {
    if (live.binding === request.binding && live.fingerprint === fingerprint) return { type: 'send', snapshot: live }
    if (state.phase === 'unknown') return { type: 'blocked', reason: 'unresolved-other-payload' }
    if (state.phase === 'failed' && state.unresolvedSince !== undefined) return { type: 'blocked', reason: 'unresolved-other-payload' }
  }

  return {
    type: 'send',
    snapshot: { key: newKey(), binding: request.binding, companyId: request.companyId, command: request.command, fingerprint },
  }
}

export function applyPriceListOutcome(
  current: PriceListAttempt,
  sent: PriceListSnapshot,
  currentBinding: string | null,
  outcome: PriceListCommandOutcome,
  previous: PriceListAttempt = { phase: 'idle' },
): PriceListAttempt {
  if (current.phase !== 'sending' || current.snapshot.key !== sent.key || currentBinding !== sent.binding) return current
  const unresolvedSince = previous.phase === 'unknown' || previous.phase === 'abandoned'
    ? previous.reason
    : previous.phase === 'failed'
      ? previous.unresolvedSince
      : undefined
  switch (outcome.type) {
    case 'succeeded':
      return { phase: 'completed', snapshot: sent, result: outcome.data }
    case 'http':
      return { phase: 'failed', snapshot: sent, status: outcome.status, problem: outcome.problem,
        ...(unresolvedSince === undefined ? {} : { unresolvedSince }) }
    case 'unknown':
      return { phase: 'unknown', snapshot: sent, reason: unresolvedSince ?? outcome.reason }
  }
}

export function livePriceListSnapshot(state: PriceListAttempt): PriceListSnapshot | null {
  if (state.phase === 'unknown' || state.phase === 'abandoned') return state.snapshot
  if (state.phase === 'failed' && state.unresolvedSince !== undefined) return state.snapshot
  return null
}

export function isPriceListOutcomeUnknown(state: PriceListAttempt): boolean {
  return state.phase === 'sending' || livePriceListSnapshot(state) !== null
}

/** The form stays immutable while a send can still be recorded; explicit abandon re-opens editing for a new payload. */
export function isPriceListAttemptFrozen(state: PriceListAttempt): boolean {
  return state.phase === 'sending' || state.phase === 'unknown'
    || (state.phase === 'failed' && state.unresolvedSince !== undefined)
}

export function rememberPriceListAttempt(binding: string, attempt: PriceListAttempt): void {
  if (attempt.phase === 'idle') return
  const token = `${binding}|${attempt.snapshot.key}`
  if (settled.has(token)) return
  if (isPriceListOutcomeUnknown(attempt)) {
    const current = retained.get(binding)
    if (current !== undefined && current.snapshot.key !== attempt.snapshot.key) settled.add(`${binding}|${current.snapshot.key}`)
    retained.set(binding, attempt)
    return
  }
  settled.add(token)
  if (retained.get(binding)?.snapshot.key === attempt.snapshot.key) retained.delete(binding)
}

export function recallPriceListAttempt(binding: string | null): PriceListAttempt {
  if (binding === null) return { phase: 'idle' }
  const held = retained.get(binding)
  if (held === undefined) return { phase: 'idle' }
  return held.phase === 'sending'
    ? { phase: 'unknown', snapshot: held.snapshot, reason: 'network-error' }
    : held
}

export function resetRetainedPriceListAttempts(): void {
  retained.clear()
  settled.clear()
}

export function generatePriceListKey(): string {
  return `price-list-${crypto.randomUUID()}`
}

export function newIdentity(): string {
  return crypto.randomUUID()
}
