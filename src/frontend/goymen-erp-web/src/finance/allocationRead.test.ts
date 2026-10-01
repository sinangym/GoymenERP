import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeFinanceAllocationDetail, decodeFinanceAllocationPage, type FinanceAllocation } from './allocationContract'
import { readFinanceAllocation, readFinanceAllocations } from './allocationHttp'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const ID = '22222222-2222-4222-8222-222222222222'
const SOURCE = '33333333-3333-4333-8333-333333333333'
const ITEM = '44444444-4444-4444-8444-444444444444'
const CURRENCY = '55555555-5555-4555-8555-555555555555'
const binding = { companyId: COMPANY, actorId: 'reader', sessionEpoch: 1 }

function allocation(overrides: Partial<FinanceAllocation> = {}): FinanceAllocation {
  return {
    id: ID, companyId: COMPANY, sourceId: SOURCE, openItemId: ITEM, currencyId: CURRENCY,
    amount: '125.50', status: 'Active', version: '1', reason: 'SENTETIK dağıtım', createdBy: 'maker',
    createdAtUtc: '2026-09-30T10:00:00Z', releasedBy: null, releasedAtUtc: null, releaseReason: null,
    ...overrides,
  }
}

afterEach(() => vi.unstubAllGlobals())

describe('F03 allocation reads', () => {
  it('strictly decodes the company-bound page and detail carrying the release version', () => {
    expect(decodeFinanceAllocationPage({ companyId: COMPANY, items: [allocation()], windowFull: false }, COMPANY))
      .toMatchObject({ ok: true, value: { items: [{ id: ID }] } })
    expect(decodeFinanceAllocationDetail({ allocation: allocation(), openItemVersion: '7' }, COMPANY))
      .toMatchObject({ ok: true, value: { allocation: { id: ID }, openItemVersion: '7' } })
    expect(decodeFinanceAllocationPage({ companyId: COMPANY, items: [allocation({ companyId: CURRENCY })], windowFull: false }, COMPANY).ok)
      .toBe(false)
    expect(decodeFinanceAllocationDetail({ allocation: allocation(), openItemVersion: 'version-7' }, COMPANY).ok).toBe(false)
  })

  it('gets list and detail with the same company and separates authorization', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ companyId: COMPANY, items: [allocation()], windowFull: false }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ allocation: allocation(), openItemVersion: '7' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 403 }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(readFinanceAllocations(binding)).resolves.toMatchObject({ kind: 'found', resource: { companyId: COMPANY } })
    await expect(readFinanceAllocation(binding, ID)).resolves.toMatchObject({ kind: 'found', resource: { openItemVersion: '7' } })
    await expect(readFinanceAllocations(binding)).resolves.toEqual({ kind: 'authorization', status: 403 })
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({ method: 'GET', headers: expect.objectContaining({ 'X-Company-Id': COMPANY }) })
  })
})
