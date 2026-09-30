/**
 * One logical command attempt on a price list — pure and framework-independent, the Sales/Logistics attempt rules
 * (ADR-011) in the small:
 *  - one Idempotency-Key per submitted SNAPSHOT (path + canonical payload + binding), minted when it is first sent;
 *  - no response, a 5xx or an unreadable success: the command MAY have applied. The outcome is unknown and the key is
 *    kept; the person retries the SAME snapshot (same key) or explicitly leaves it;
 *  - while an outcome is unknown a DIFFERENT payload does not quietly open a second attempt;
 *  - an outcome is applied only to the attempt AND binding that sent it (a late answer from a company the person has
 *    left writes nothing).
 * Memory only: the key and payload never reach storage, a URL or a log.
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
  | { readonly phase: 'unknown'; readonly snapshot: PriceListSnapshot }
  | { readonly phase: 'abandoned'; readonly snapshot: PriceListSnapshot }
  | { readonly phase: 'failed'; readonly snapshot: PriceListSnapshot; readonly status: number; readonly problem: PriceListProblem }
  | { readonly phase: 'completed'; readonly snapshot: PriceListSnapshot; readonly result: PriceListResult }

export type PriceListSubmitDecision =
  | { readonly type: 'send'; readonly snapshot: PriceListSnapshot }
  | { readonly type: 'blocked'; readonly reason: 'in-flight' | 'unresolved-other-payload' | 'no-context' }

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
  if (state.phase === 'unknown' || state.phase === 'abandoned') {
    const live = state.snapshot
    if (live.binding === request.binding && live.fingerprint === fingerprint) return { type: 'send', snapshot: live }
    if (state.phase === 'unknown') return { type: 'blocked', reason: 'unresolved-other-payload' }
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
): PriceListAttempt {
  if (current.phase !== 'sending' || current.snapshot.key !== sent.key || currentBinding !== sent.binding) return current
  switch (outcome.type) {
    case 'succeeded':
      return { phase: 'completed', snapshot: sent, result: outcome.data }
    case 'http':
      return { phase: 'failed', snapshot: sent, status: outcome.status, problem: outcome.problem }
    case 'unknown':
      return { phase: 'unknown', snapshot: sent }
  }
}

export function generatePriceListKey(): string {
  return `price-list-${crypto.randomUUID()}`
}

export function newIdentity(): string {
  return crypto.randomUUID()
}
