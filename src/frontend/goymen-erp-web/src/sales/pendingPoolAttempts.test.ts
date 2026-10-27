import { afterEach, describe, expect, it } from 'vitest'
import { recallPoolAttempt, rememberPoolAttempt, resetPendingPoolAttempts } from './pendingPoolAttempts'
import type { OrderAttemptSnapshot, OrderAttemptState } from './orderAttempt'

afterEach(() => resetPendingPoolAttempts())

const BINDING = '["u","1","company-a","sales.request.create"]'
const snapshot: OrderAttemptSnapshot<{ readonly n: number }> = {
  key: 'k-1',
  path: '/sales/requests',
  binding: BINDING,
  companyId: 'company-a',
  payload: { n: 1 },
}
type State = OrderAttemptState<{ readonly n: number }, string>

describe('held S-01 create attempts', () => {
  it('a send torn down in flight comes back UNKNOWN with its key and values, under the same binding only', () => {
    rememberPoolAttempt<{ readonly n: number }, string, string>(BINDING, { phase: 'submitting', snapshot } as State, 'values', true)
    const recalled = recallPoolAttempt<{ readonly n: number }, string, string>(BINDING)
    expect(recalled?.attempt).toEqual({ phase: 'unknown', snapshot, reason: 'network-error' })
    expect(recalled?.values).toBe('values')
    expect(recallPoolAttempt('["u","1","company-b","sales.request.create"]')).toBeNull()
    expect(recallPoolAttempt(null)).toBeNull()
  })

  it('a definite refusal, or a success that was shown, settles it for good', () => {
    rememberPoolAttempt(BINDING, { phase: 'unknown', snapshot, reason: 'offline' } as State, 'v', true)
    rememberPoolAttempt(BINDING, { phase: 'failed', snapshot, failure: 'validation', problem: null } as State, 'v', true)
    expect(recallPoolAttempt(BINDING)).toBeNull()
    // A late outcome of the settled attempt cannot bring it back.
    rememberPoolAttempt(BINDING, { phase: 'unknown', snapshot, reason: 'offline' } as State, 'v', true)
    expect(recallPoolAttempt(BINDING)).toBeNull()
  })

  it('a success no screen has shown is kept until shown', () => {
    const completed = { phase: 'completed', snapshot, result: 'r' } as State
    rememberPoolAttempt(BINDING, completed, 'v', false)
    expect(recallPoolAttempt(BINDING)?.attempt).toEqual(completed)
    rememberPoolAttempt(BINDING, completed, 'v', true)
    expect(recallPoolAttempt(BINDING)).toBeNull()
  })
})
