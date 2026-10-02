import { afterEach, describe, expect, it } from 'vitest'
import type { ShellState } from '../../shell/shellState'
import type { PriceListCommand } from './api'
import {
  applyPriceListOutcome,
  decidePriceListSubmit,
  isPriceListOutcomeUnknown,
  priceListBinding,
  recallPriceListAttempt,
  rememberPriceListAttempt,
  resetRetainedPriceListAttempts,
  type PriceListAttempt,
} from './priceListAttempt'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LIST = '22222222-2222-4222-8222-222222222222'
const command: PriceListCommand = { kind: 'publish', priceListId: LIST, payload: { expectedVersion: '1', versionNo: 1 } }

const binding = priceListBinding({
  activeContext: { userId: 'actor-1', companyId: COMPANY, scopeMode: 'single' },
  sessionEpoch: 7,
} as unknown as ShellState, `product.price-list.publish:${LIST}`) as string

function first(): Extract<ReturnType<typeof decidePriceListSubmit>, { type: 'send' }> {
  const decision = decidePriceListSubmit({ phase: 'idle' }, { binding, companyId: COMPANY, command }, () => 'key-1')
  if (decision.type !== 'send') throw new Error(decision.reason)
  return decision
}

afterEach(resetRetainedPriceListAttempts)

describe('price-list retained command attempt', () => {
  it('recalls an in-flight send as unknown after a route remount and reuses its exact snapshot', () => {
    const sent = first().snapshot
    rememberPriceListAttempt(binding, { phase: 'sending', snapshot: sent })

    const recalled = recallPriceListAttempt(binding)
    expect(recalled).toMatchObject({ phase: 'unknown', snapshot: sent, reason: 'network-error' })
    expect(decidePriceListSubmit(recalled, { binding, companyId: COMPANY, command }, () => 'key-2'))
      .toMatchObject({ type: 'send', snapshot: { key: 'key-1' } })
  })

  it.each([401, 403, 409])('keeps the original unknown snapshot after a retry is refused with %s', (status) => {
    const sent = first().snapshot
    const unknown: PriceListAttempt = { phase: 'unknown', snapshot: sent, reason: 'network-error' }
    const sending: PriceListAttempt = { phase: 'sending', snapshot: sent }
    const refused = applyPriceListOutcome(sending, sent, binding, {
      type: 'http', status, problem: { code: null, errors: [] },
    }, unknown)

    expect(refused).toMatchObject({ phase: 'failed', status, unresolvedSince: 'network-error' })
    expect(isPriceListOutcomeUnknown(refused)).toBe(true)
    expect(decidePriceListSubmit(refused, { binding, companyId: COMPANY, command }, () => 'key-2'))
      .toMatchObject({ type: 'send', snapshot: { key: 'key-1' } })
  })

  it('discards a late success after the actor/company binding changed', () => {
    const sent = first().snapshot
    const sending: PriceListAttempt = { phase: 'sending', snapshot: sent }
    const changedBinding = JSON.stringify(['actor-2', '7', COMPANY, `product.price-list.publish:${LIST}`])
    expect(applyPriceListOutcome(sending, sent, changedBinding, {
      type: 'succeeded',
      data: { priceList: { id: LIST } as never, isReplay: false, originalCorrelationId: 'c1' },
    })).toBe(sending)
  })
})
