/**
 * What the create screen tells the person about the save attempt, and which actions
 * it may offer. Pure, so the promises it makes are tested before any page exists.
 *
 * Two rules carried from the lessons the spec links (§6):
 *  - the text offers only actions the screen really has. Nothing says "reload" or
 *    "refresh the page": a reload is precisely what throws away the key of a write
 *    that may already be recorded;
 *  - "not saved" is said only when it is known. While the outcome is unknown — and
 *    after a refusal that only answered a retry — the text says it MAY be saved.
 */

import type { OrderAttemptFailure, OrderAttemptState } from './orderAttempt'
import { ORDER_DRAFT_TEXT, problemCodeText } from './salesText'

export type NoticeAction = 'retry' | 'abandon'

export interface AttemptNotice {
  readonly tone: 'progress' | 'unknown' | 'error' | 'success'
  /** `assertive` only for what needs a decision now. */
  readonly live: 'polite' | 'assertive'
  readonly message: string
  readonly actions: readonly NoticeAction[]
}

const FAILURE_MESSAGE: Record<OrderAttemptFailure, string> = {
  validation: ORDER_DRAFT_TEXT.attempt.validation,
  unauthenticated: ORDER_DRAFT_TEXT.attempt.unauthenticated,
  forbidden: ORDER_DRAFT_TEXT.attempt.forbidden,
  'not-found': ORDER_DRAFT_TEXT.attempt.notFound,
  conflict: ORDER_DRAFT_TEXT.attempt.conflict,
  unexpected: ORDER_DRAFT_TEXT.attempt.unexpected,
}

/**
 * The class says that nothing was saved; the stable code, when this build knows it,
 * says why. Both are shown — a code never replaces the verdict.
 */
function refusalNotice(failure: OrderAttemptFailure, code: string | null, priced: boolean): AttemptNotice {
  const reason = problemCodeText(code)
  // UI-14: a send carrying a price is also fenced by the price-write permission, and the 403 does not say which one.
  const message = failure === 'forbidden' && priced ? ORDER_DRAFT_TEXT.attempt.forbiddenPriced : FAILURE_MESSAGE[failure]
  return {
    tone: 'error',
    live: 'assertive',
    message: reason === null ? message : `${message} ${reason}`,
    actions: [],
  }
}

/**
 * `blockedChange` is true when the person tried to send a changed form while an
 * earlier outcome is still unknown (`decideSubmit` → `unresolved-other-payload`).
 */
export function attemptNotice<P, R>(
  state: OrderAttemptState<P, R>,
  blockedChange = false,
  /** UI-14: the refused send carried a line price (so the separate price-write fence applied too). */
  priced = false,
): AttemptNotice | null {
  const text = ORDER_DRAFT_TEXT.attempt
  switch (state.phase) {
    case 'idle':
      return null
    case 'submitting':
      return { tone: 'progress', live: 'polite', message: text.submitting, actions: [] }
    case 'unknown':
      return {
        tone: 'unknown',
        live: 'assertive',
        message: blockedChange ? text.unknownChangedBlocked : text.unknown,
        actions: ['retry', 'abandon'],
      }
    case 'abandoned':
      // Leaving does not settle anything; the same payload still retries under its key.
      return { tone: 'unknown', live: 'polite', message: text.abandoned, actions: [] }
    case 'failed':
      if (state.unresolvedSince !== undefined) {
        return {
          tone: 'unknown',
          live: 'assertive',
          message: blockedChange ? text.unknownChangedBlocked : text.refusedAfterUnknown,
          actions: ['retry', 'abandon'],
        }
      }
      return refusalNotice(state.failure, state.problem?.code ?? null, priced)
    case 'completed':
      return { tone: 'success', live: 'polite', message: text.completed, actions: [] }
    default:
      return null
  }
}
