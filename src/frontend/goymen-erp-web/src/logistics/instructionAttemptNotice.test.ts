import { describe, expect, it } from 'vitest'
import { LOGISTICS_PROBLEM_CODE, type LogisticsProblem } from './contract'
import type { AttemptState } from './instructionAttempt'
import { commandNotice, createNotice, refusalMovesRecord } from './instructionAttemptNotice'
import { INSTRUCTION_CREATE_TEXT, INSTRUCTION_DETAIL_TEXT, PROBLEM_CODE_TEXT } from './logisticsText'

type R = { readonly isReplay: boolean }
const snapshot = { key: 'k', path: 'p', binding: 'b', companyId: 'c', payload: {} }
const problem = (code: string, status = 409): LogisticsProblem => ({ status, title: '', code, correlationId: null, errors: [] })
const failed = (code: string | null, status = 409, unresolvedSince?: 'network-error'): AttemptState<unknown, R> => ({
  phase: 'failed',
  snapshot,
  failure: status === 403 ? 'forbidden' : status === 404 ? 'not-found' : status === 422 ? 'validation' : 'conflict',
  problem: code === null ? null : problem(code, status),
  ...(unresolvedSince === undefined ? {} : { unresolvedSince }),
})

describe('command notices on the detail', () => {
  it.each([
    LOGISTICS_PROBLEM_CODE.versionConflict,
    LOGISTICS_PROBLEM_CODE.instructionState,
    LOGISTICS_PROBLEM_CODE.loadExceedsInstructed,
    LOGISTICS_PROBLEM_CODE.lineBlocked,
    LOGISTICS_PROBLEM_CODE.duplicateShare,
    LOGISTICS_PROBLEM_CODE.sameKeyPayloadMismatch,
    LOGISTICS_PROBLEM_CODE.concurrentChange,
  ])('a 409 %s says the class and the code’s own reason, and offers no action', (code) => {
    const notice = commandNotice(failed(code))
    expect(notice).toMatchObject({ tone: 'error', live: 'assertive', actions: [] })
    expect(notice?.message).toBe(`${INSTRUCTION_DETAIL_TEXT.command.conflict} ${PROBLEM_CODE_TEXT[code]}`)
  })

  it('a code-less 403 and a 404 keep their class text; an unknown code keeps the class only', () => {
    expect(commandNotice(failed(null, 403))?.message).toBe(INSTRUCTION_DETAIL_TEXT.command.forbidden)
    expect(commandNotice(failed(null, 404))?.message).toBe(INSTRUCTION_DETAIL_TEXT.command.notFound)
    expect(commandNotice(failed('some_future_code'))?.message).toBe(INSTRUCTION_DETAIL_TEXT.command.conflict)
  })

  it('a refusal that answered a retry of an unknown outcome says it MAY be applied and offers retry/abandon', () => {
    const notice = commandNotice(failed(LOGISTICS_PROBLEM_CODE.versionConflict, 409, 'network-error'))
    expect(notice).toMatchObject({ tone: 'unknown', actions: ['retry', 'abandon'] })
    expect(notice?.message).toContain(INSTRUCTION_DETAIL_TEXT.command.refusedAfterUnknown)
  })

  it('unknown, submitting, completed and replayed have their own words', () => {
    expect(commandNotice({ phase: 'submitting', snapshot })).toMatchObject({ tone: 'progress', message: INSTRUCTION_DETAIL_TEXT.command.submitting })
    expect(commandNotice({ phase: 'unknown', snapshot, reason: 'offline' })).toMatchObject({ tone: 'unknown', actions: ['retry', 'abandon'] })
    expect(commandNotice({ phase: 'completed', snapshot, result: { isReplay: false } })?.message).toBe(INSTRUCTION_DETAIL_TEXT.command.completed)
    expect(commandNotice({ phase: 'completed', snapshot, result: { isReplay: true } })?.message).toBe(INSTRUCTION_DETAIL_TEXT.command.replayed)
    expect(commandNotice({ phase: 'idle' })).toBeNull()
  })

  it('a 503 is never a refusal: the attempt is unknown and the record may exist', () => {
    const notice = commandNotice({ phase: 'unknown', snapshot, reason: 'service-unavailable' })
    expect(notice?.message).toBe(INSTRUCTION_DETAIL_TEXT.command.unknown)
  })

  it('names the refusals after which the detail must be re-read', () => {
    for (const code of [
      LOGISTICS_PROBLEM_CODE.versionConflict,
      LOGISTICS_PROBLEM_CODE.instructionState,
      LOGISTICS_PROBLEM_CODE.loadExceedsInstructed,
      LOGISTICS_PROBLEM_CODE.lineBlocked,
      LOGISTICS_PROBLEM_CODE.duplicateShare,
      LOGISTICS_PROBLEM_CODE.concurrentChange,
    ]) {
      expect(refusalMovesRecord(failed(code))).toBe(true)
    }
    expect(refusalMovesRecord(failed(null, 403))).toBe(false)
    expect(refusalMovesRecord(failed(LOGISTICS_PROBLEM_CODE.unsupportedUnit, 422))).toBe(false)
  })
})

describe('create notices', () => {
  it('account and order-line 409s carry their reason; a changed form during an unknown outcome is blocked with its own text', () => {
    expect(createNotice(failed(LOGISTICS_PROBLEM_CODE.accountState))?.message).toBe(`${INSTRUCTION_CREATE_TEXT.attempt.conflict} ${PROBLEM_CODE_TEXT.account_state}`)
    expect(createNotice(failed(LOGISTICS_PROBLEM_CODE.orderLineState))?.message).toBe(`${INSTRUCTION_CREATE_TEXT.attempt.conflict} ${PROBLEM_CODE_TEXT.order_line_state}`)
    expect(createNotice(failed(LOGISTICS_PROBLEM_CODE.unsupportedUnit, 422))?.message).toBe(`${INSTRUCTION_CREATE_TEXT.attempt.validation} ${PROBLEM_CODE_TEXT.unsupported_unit}`)
    expect(createNotice({ phase: 'unknown', snapshot, reason: 'offline' }, true)?.message).toBe(INSTRUCTION_CREATE_TEXT.attempt.unknownChangedBlocked)
    expect(createNotice({ phase: 'abandoned', snapshot, reason: 'offline' })?.message).toBe(INSTRUCTION_CREATE_TEXT.attempt.abandoned)
  })
})
