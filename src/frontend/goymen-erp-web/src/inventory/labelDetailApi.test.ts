import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchInventoryLabelDetail } from './labelDetailApi'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const PRODUCT = '33333333-3333-3333-3333-333333333333'
const LOT = '44444444-4444-4444-4444-444444444444'
const PARTY = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'

const identity = {
  labelId: LABEL, companyId: COMPANY, code: 'LBL-100', productId: PRODUCT, lotId: LOT,
  heatNumber: null, coilNumber: null, documentStatus: 'Complete', requiredDocuments: [], documents: [], prints: [],
  lot: { id: LOT, heatNumber: null, coilNumber: null },
}
const balances = {
  labelId: LABEL, physicalKg: '10', freeKg: '10', blockedKg: '0',
  positions: [{
    ownerKind: 'Company', ownerId: PARTY, custodianKind: 'Company', custodianId: PARTY,
    locationId: LOCATION, productId: PRODUCT, block: 'Free', quantityKg: '10',
  }],
}
const genealogy = { labelId: LABEL, labelCode: 'LBL-100', edges: [], windowFull: false }

describe('fetchInventoryLabelDetail', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('reads both existing endpoints with one company fence and returns their bound result', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ labelVersion: '3', identity, balances, genealogy }), { status: 200 }))

    const result = await fetchInventoryLabelDetail(COMPANY, LABEL)

    expect(result).toMatchObject({ type: 'ok-single', data: { identity: { code: 'LBL-100' }, balances: { physicalKg: '10' } } })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toEqual([
      `/api/v1/inventory/labels/${LABEL}/detail`,
    ])
    for (const [, init] of fetchMock.mock.calls) {
      expect(init).toMatchObject({ cache: 'no-store', headers: { 'X-Company-Id': COMPANY } })
    }
  })

  it('does not render a valid-looking response from another company', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        labelVersion: '3', identity: { ...identity, companyId: PARTY }, balances, genealogy,
      }), { status: 200 }))
    await expect(fetchInventoryLabelDetail(COMPANY, LABEL)).resolves.toEqual({ type: 'malformed' })
  })

  it('preserves authorization and not-found HTTP outcomes without decoding their bodies as data', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ status: 404 }), { status: 404 }))
    await expect(fetchInventoryLabelDetail(COMPANY, LABEL)).resolves.toEqual({
      type: 'http', status: 404, body: { status: 404 },
    })
  })
})
