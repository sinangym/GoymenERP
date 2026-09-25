/**
 * What a Logistics screen tells the person about one command attempt, and which actions it may
 * offer. Pure, so the promises it makes are tested before any page exists.
 *
 * Rules carried from the Sales lane's lessons: the text offers only actions the screen really has
 * (never "reload"); "not saved" is said only when it is known — while the outcome is unknown, and
 * after a refusal that only answered a retry, the text says it MAY be saved; a replay is shown as a
 * replay; a stable code refines the message, never the verdict.
 */

import { LOGISTICS_PROBLEM_CODE } from './contract'
import type { AttemptFailure, AttemptState } from './instructionAttempt'
import { INSTRUCTION_CREATE_TEXT, INSTRUCTION_DETAIL_TEXT, problemCodeText } from './logisticsText'

export type NoticeAction = 'retry' | 'abandon'

export interface AttemptNotice {
  readonly tone: 'progress' | 'unknown' | 'error' | 'success'
  /** `assertive` only for what needs a decision now. */
  readonly live: 'polite' | 'assertive'
  readonly message: string
  readonly actions: readonly NoticeAction[]
}

interface NoticeWords {
  readonly submitting: string
  readonly unknown: string
  readonly unknownChangedBlocked?: string
  readonly abandoned?: string
  readonly refusedAfterUnknown: string
  readonly validation: string
  readonly unauthenticated: string
  readonly forbidden: string
  readonly notFound: string
  readonly conflict: string
  readonly unexpected: string
  readonly completed: string
  readonly replayed: string
}

function failureMessage(words: NoticeWords, failure: AttemptFailure): string {
  switch (failure) {
    case 'validation':
      return words.validation
    case 'unauthenticated':
      return words.unauthenticated
    case 'forbidden':
      return words.forbidden
    case 'not-found':
      return words.notFound
    case 'conflict':
      return words.conflict
    default:
      return words.unexpected
  }
}

/**
 * The class says that nothing was applied; the stable code, when this build knows it, says why.
 * Both are shown — a code never replaces the verdict. Every 409 the routes answer has its own text:
 * `version_conflict` (the record moved; the detail is re-read), `logistics_instruction_state`,
 * `logistics_load_exceeds_instructed`, `logistics_line_blocked`, `logistics_duplicate_share_id`,
 * `account_state`, `order_line_state`, `same_key_payload_mismatch`, `concurrent_change`.
 */
function refusalNotice(words: NoticeWords, failure: AttemptFailure, code: string | null, unresolved: boolean): AttemptNotice {
  const reason = problemCodeText(code)
  const base = failureMessage(words, failure)
  const text = reason === null ? base : `${base} ${reason}`
  return {
    tone: unresolved ? 'unknown' : 'error',
    live: 'assertive',
    message: unresolved ? `${text} ${words.refusedAfterUnknown}` : text,
    actions: unresolved ? ['retry', 'abandon'] : [],
  }
}

function notice<P, R extends { readonly isReplay: boolean }>(
  words: NoticeWords,
  state: AttemptState<P, R>,
  blockedChange: boolean,
): AttemptNotice | null {
  switch (state.phase) {
    case 'idle':
      return null
    case 'submitting':
      return { tone: 'progress', live: 'polite', message: words.submitting, actions: [] }
    case 'unknown':
      return {
        tone: 'unknown',
        live: 'assertive',
        message: blockedChange && words.unknownChangedBlocked !== undefined ? words.unknownChangedBlocked : words.unknown,
        actions: ['retry', 'abandon'],
      }
    case 'abandoned':
      // Leaving does not settle anything; the same payload still retries under its key.
      return { tone: 'unknown', live: 'polite', message: words.abandoned ?? words.unknown, actions: [] }
    case 'failed':
      if (state.unresolvedSince !== undefined && blockedChange && words.unknownChangedBlocked !== undefined) {
        return { tone: 'unknown', live: 'assertive', message: words.unknownChangedBlocked, actions: ['retry', 'abandon'] }
      }
      return refusalNotice(words, state.failure, state.problem?.code ?? null, state.unresolvedSince !== undefined)
    case 'completed':
      return {
        tone: 'success',
        live: 'polite',
        message: state.result.isReplay ? words.replayed : words.completed,
        actions: [],
      }
    default:
      return null
  }
}

/** The create screen's notice. `blockedChange`: a changed form was sent while an earlier outcome is unknown. */
export function createNotice<P, R extends { readonly isReplay: boolean }>(
  state: AttemptState<P, R>,
  blockedChange = false,
): AttemptNotice | null {
  return notice(INSTRUCTION_CREATE_TEXT.attempt, state, blockedChange)
}

/** The detail's command notice (issue / record-load). */
export function commandNotice<P, R extends { readonly isReplay: boolean }>(state: AttemptState<P, R>): AttemptNotice | null {
  return notice(INSTRUCTION_DETAIL_TEXT.command, state, false)
}

/** Whether a refusal moved the record: the detail must be re-read after it. */
export function refusalMovesRecord(state: AttemptState<unknown, unknown>): boolean {
  if (state.phase !== 'failed') return false
  const code = state.problem?.code ?? null
  return (
    state.failure === 'conflict' &&
    (code === LOGISTICS_PROBLEM_CODE.versionConflict ||
      code === LOGISTICS_PROBLEM_CODE.instructionState ||
      code === LOGISTICS_PROBLEM_CODE.concurrentChange ||
      code === LOGISTICS_PROBLEM_CODE.duplicateShare ||
      code === LOGISTICS_PROBLEM_CODE.loadExceedsInstructed ||
      code === LOGISTICS_PROBLEM_CODE.lineBlocked)
  )
}
