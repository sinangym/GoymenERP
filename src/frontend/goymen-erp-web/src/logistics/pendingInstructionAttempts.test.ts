import { beforeEach, describe, expect, it } from 'vitest'
import type { IssueRequest } from './contract'
import { beginSend, type AttemptSnapshot, type AttemptState } from './instructionAttempt'
import { recallInstructionAttempt, rememberInstructionAttempt, resetPendingInstructionAttempts } from './pendingInstructionAttempts'

const snapshot: AttemptSnapshot<IssueRequest> = {
  key: '0123456789abcdef0123456789abcdef',
  path: '11111111-1111-1111-1111-111111111111/issue',
  binding: '["actor",1,"company","scope"]',
  companyId: '22222222-2222-2222-2222-222222222222',
  payload: { expectedVersion: '7' },
}

beforeEach(resetPendingInstructionAttempts)

describe('pending Logistics attempts', () => {
  it('reopens an in-flight attempt as unknown with the same key, payload and form values', () => {
    rememberInstructionAttempt('slot', beginSend(snapshot), { selectedLine: 'line-1' })

    expect(recallInstructionAttempt<IssueRequest, { selectedLine: string }>('slot')).toEqual({
      attempt: { phase: 'unknown', snapshot, reason: 'network-error' },
      values: { selectedLine: 'line-1' },
    })
  })

  it('keeps abandoned work in its exact slot and refuses cross-context recall', () => {
    rememberInstructionAttempt('slot', { phase: 'abandoned', snapshot, reason: 'service-unavailable' }, null)

    expect(recallInstructionAttempt<IssueRequest, null>('slot')?.attempt).toMatchObject({ phase: 'abandoned', snapshot })
    expect(recallInstructionAttempt<IssueRequest, null>('other-slot')).toBeNull()
  })

  it('does not let a late old result overwrite a newer attempt in the same record slot', () => {
    rememberInstructionAttempt('slot', { phase: 'unknown', snapshot, reason: 'network-error' }, null)
    const newer = { ...snapshot, key: 'fedcba9876543210fedcba9876543210' }
    rememberInstructionAttempt('slot', beginSend(newer), null)
    rememberInstructionAttempt('slot', { phase: 'unknown', snapshot, reason: 'offline' }, null)

    expect(recallInstructionAttempt<IssueRequest, null>('slot')?.attempt).toMatchObject({ snapshot: newer })
  })

  it('clears a definite refusal and prevents a late unknown resurrection', () => {
    const refusal: AttemptState<IssueRequest, never> = { phase: 'failed', snapshot, failure: 'conflict', problem: null }
    rememberInstructionAttempt('slot', refusal, null)
    rememberInstructionAttempt('slot', { phase: 'unknown', snapshot, reason: 'network-error' }, null)

    expect(recallInstructionAttempt<IssueRequest, null>('slot')).toBeNull()
  })
})
