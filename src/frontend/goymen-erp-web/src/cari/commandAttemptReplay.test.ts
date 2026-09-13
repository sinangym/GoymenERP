/**
 * A re-send's answer resolves the first send ONLY when it is that send's own stored
 * result — `Idempotency-Replayed: true`. Everything else keeps the attempt unresolved.
 */

import { describe, expect, it } from 'vitest'
import { beginAttempt, retryAttempt, settleAttempt, type AttemptState } from './commandAttempt'
import type { CommandOutcome } from './commandApi'

const snapshot = {
  key: 'k'.repeat(32),
  operation: 'counterparty.account-status:p',
  binding: 'b',
  payload: { expectedVersion: '7', isActive: false, reason: 'r' },
}

function resent(): AttemptState<typeof snapshot.payload, null> {
  const unknown = settleAttempt(beginAttempt<typeof snapshot.payload, null>(snapshot), { type: 'http', status: 500, code: null, body: null })
  expect(unknown.phase).toBe('unknown')
  return retryAttempt(unknown)
}

const http = (status: number, code: string | null, replayed?: boolean): CommandOutcome<null> => ({
  type: 'http',
  status,
  code: code as CommandOutcome<null> extends { code: infer C } ? C : never,
  body: null,
  ...(replayed === undefined ? {} : { replayed }),
})

describe('settling a re-send of an unresolved attempt', () => {
  it('a REPLAYED version_conflict is the first send’s verdict: definite, terminal', () => {
    const settled = settleAttempt(resent(), http(409, 'version_conflict', true))
    expect(settled.phase).toBe('failed')
    if (settled.phase === 'failed') expect(settled.failure).toBe('version-conflict')
  })

  it('a non-replayed conflict of the re-send says nothing about the first send: still unknown', () => {
    const settled = settleAttempt(resent(), http(409, 'version_conflict', false))
    expect(settled.phase).toBe('unknown')
  })

  it('a compatibility 409, a 500 and an unacknowledged answer leave it unresolved', () => {
    expect(settleAttempt(resent(), http(409, 'client_contract_required')).phase).toBe('unknown')
    expect(settleAttempt(resent(), http(500, null)).phase).toBe('unknown')
    expect(settleAttempt(resent(), { type: 'unacknowledged', status: 204 }).phase).toBe('unknown')
  })

  it('a replayed 204 completes it, marked as a replay', () => {
    const settled = settleAttempt(resent(), {
      type: 'applied',
      data: null,
      replayed: true,
      originalCorrelationId: null,
    })
    expect(settled.phase).toBe('completed')
    if (settled.phase === 'completed') expect(settled.replayed).toBe(true)
  })

  it('a first send’s definite refusal is definite with or without the marker', () => {
    const first = beginAttempt<typeof snapshot.payload, null>(snapshot)
    expect(settleAttempt(first, http(409, 'version_conflict')).phase).toBe('failed')
    expect(settleAttempt(first, http(409, 'version_conflict', true)).phase).toBe('failed')
  })
})
