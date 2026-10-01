import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeFinanceAllocationPage, type FinanceAllocation } from './allocationContract'
import { readFinanceAllocation } from './allocationHttp'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const ALLOCATION = '22222222-2222-4222-8222-222222222222'
const OTHER_ALLOCATION = '33333333-3333-4333-8333-333333333333'

function allocation(id = ALLOCATION): FinanceAllocation {
  return {
    id,
    companyId: COMPANY,
    sourceId: '44444444-4444-4444-8444-444444444444',
    openItemId: '55555555-5555-4555-8555-555555555555',
    currencyId: '66666666-6666-4666-8666-666666666666',
    amount: '125.50',
    status: 'Active',
    version: '1',
    reason: 'SENTETIK strict reader',
    createdBy: 'reader',
    createdAtUtc: '2026-09-30T10:00:00Z',
    releasedBy: null,
    releasedAtUtc: null,
    releaseReason: null,
  }
}

afterEach(() => vi.unstubAllGlobals())

describe('strict allocation reads', () => {
  it('rejects duplicate, oversized, and incomplete full windows', () => {
    const item = allocation()
    expect(decodeFinanceAllocationPage({ companyId: COMPANY, items: [item, item], windowFull: false }, COMPANY))
      .toEqual({ ok: false })
    expect(decodeFinanceAllocationPage({
      companyId: COMPANY,
      items: Array.from({ length: 51 }, (_, index) =>
        allocation(`00000000-0000-4000-8000-${String(index + 1).padStart(12, '0')}`)),
      windowFull: true,
    }, COMPANY)).toEqual({ ok: false })
    expect(decodeFinanceAllocationPage({ companyId: COMPANY, items: [item], windowFull: true }, COMPANY))
      .toEqual({ ok: false })
  })

  it('rejects malformed allocation members', () => {
    const item = allocation()
    for (const malformed of [
      { ...item, amount: 12.5 },
      { ...item, status: 'Unknown' },
      { ...item, version: 2 },
      { ...item, unexpected: true },
    ]) {
      expect(decodeFinanceAllocationPage({ companyId: COMPANY, items: [malformed], windowFull: false }, COMPANY))
        .toEqual({ ok: false })
    }
  })

  it('rejects a detail response whose allocation identity differs from the request', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      allocation: allocation(OTHER_ALLOCATION),
      openItemVersion: '3',
    }), { status: 200 })))

    await expect(readFinanceAllocation({ companyId: COMPANY, actorId: 'reader', sessionEpoch: 1 }, ALLOCATION))
      .resolves.toEqual({ kind: 'malformed' })
  })
})
