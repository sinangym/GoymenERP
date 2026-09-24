import { describe, expect, it } from 'vitest'
import { decodeFinanceAllocation, decodeFinanceAllocationCommandResult } from './allocationContract'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER_COMPANY = '22222222-2222-4222-8222-222222222222'

function active() {
  return {
    id: '33333333-3333-4333-8333-333333333333', companyId: COMPANY,
    sourceId: '44444444-4444-4444-8444-444444444444',
    openItemId: '55555555-5555-4555-8555-555555555555',
    currencyId: '66666666-6666-4666-8666-666666666666', amount: '250.10', status: 'Active', version: '1',
    reason: 'Fatura kapama', createdBy: 'actor-a', createdAtUtc: '2026-09-24T08:00:00+00:00',
    releasedBy: null, releasedAtUtc: null, releaseReason: null,
  }
}

describe('Finance allocation wire contract', () => {
  it('accepts the exact active and released traces without converting money to number', () => {
    const first = decodeFinanceAllocation(active(), COMPANY)
    expect(first.ok && first.value.amount).toBe('250.10')

    const released = decodeFinanceAllocation({
      ...active(), status: 'Released', version: '2', releasedBy: 'actor-b',
      releasedAtUtc: '2026-09-24T09:00:00Z', releaseReason: 'Yanlış dağıtım',
    }, COMPANY)
    expect(released.ok && released.value.status).toBe('Released')
  })

  it.each([
    ['foreign company', { ...active(), companyId: OTHER_COMPANY }],
    ['zero amount', { ...active(), amount: '0.00' }],
    ['numeric amount', { ...active(), amount: 250.10 }],
    ['noncanonical version', { ...active(), version: '01' }],
    ['active release trace', { ...active(), releasedBy: 'actor-b' }],
    ['released missing trace', { ...active(), status: 'Released' }],
    ['unknown field', { ...active(), extra: true }],
  ])('rejects %s', (_name, value) => {
    expect(decodeFinanceAllocation(value, COMPANY).ok).toBe(false)
  })

  it('requires a succeeded resource and keeps refusal resources closed', () => {
    const success = {
      outcome: 'Succeeded', resource: active(), isReplay: false, originalCorrelationId: '', issues: [],
    }
    expect(decodeFinanceAllocationCommandResult(success, COMPANY).ok).toBe(true)
    expect(decodeFinanceAllocationCommandResult({ ...success, resource: null }, COMPANY).ok).toBe(false)
    expect(decodeFinanceAllocationCommandResult({ ...success, issues: [{ field: 'amount', code: 'invalid' }] }, COMPANY).ok)
      .toBe(false)

    const refusal = {
      outcome: 'VersionConflict', resource: null, isReplay: true, originalCorrelationId: 'corr-1',
      issues: [{ field: 'expectedOpenItemVersion', code: 'version_conflict' }],
    }
    expect(decodeFinanceAllocationCommandResult(refusal, COMPANY).ok).toBe(true)
    expect(decodeFinanceAllocationCommandResult({ ...refusal, resource: active() }, COMPANY).ok).toBe(false)
    expect(decodeFinanceAllocationCommandResult({ ...refusal, originalCorrelationId: '' }, COMPANY).ok).toBe(false)
  })
})
