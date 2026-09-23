import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  decodeParentRollCandidates,
  fetchParentRollCandidates,
  PARENT_ROLL_CANDIDATES_PATH,
} from './planReferences'
import { COMPANY_A, json, problem } from './testHarness'

const PRODUCT = '10000000-0000-4000-8000-000000000001'
const LOT = '20000000-0000-4000-8000-000000000001'
const LABEL = '30000000-0000-4000-8000-000000000001'
const LOCATION = '40000000-0000-4000-8000-000000000001'

function candidate(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY_A,
    labelId: LABEL,
    labelVersion: '7',
    labelCode: 'LBL-0007',
    productId: PRODUCT,
    lot: { id: LOT, heatNumber: 'H-12', coilNumber: null },
    heatNumber: 'H-12',
    coilNumber: null,
    documentStatus: 'Complete',
    requiredDocuments: ['MillCertificate'],
    attachedDocuments: ['MillCertificate'],
    physicalKg: '1200.5',
    unblockedPhysicalKg: '1000.5',
    blockedPhysicalKg: '200',
    positions: [
      {
        ownerKind: 'Company',
        ownerId: COMPANY_A,
        custodianKind: 'Company',
        custodianId: COMPANY_A,
        locationId: LOCATION,
        productId: PRODUCT,
        block: 'Free',
        quantityKg: '1000.5',
      },
    ],
    draftSelectable: true,
    draftRefusalCode: null,
    productionUseRequiresRevalidation: true,
    ...overrides,
  }
}

afterEach(() => vi.unstubAllGlobals())

describe('decodeParentRollCandidates', () => {
  it('preserves label version, physical facts and selection/revalidation flags', () => {
    const body = { items: [candidate()], searchWindowFull: true }

    expect(decodeParentRollCandidates(body)).toEqual(body)
  })

  it('keeps a visible customer-owned reference nonselectable without inventing fason context', () => {
    const body = {
      items: [
        candidate({
          draftSelectable: false,
          draftRefusalCode: 'inventory_parent_roll_context_required',
          positions: [
            {
              ...candidate().positions[0],
              ownerKind: 'Customer',
              ownerId: '50000000-0000-4000-8000-000000000001',
            },
          ],
        }),
      ],
      searchWindowFull: false,
    }

    expect(decodeParentRollCandidates(body)).toEqual(body)
  })

  it.each([
    { ...candidate(), extra: true },
    candidate({ labelVersion: '07' }),
    candidate({ unblockedPhysicalKg: '1000.500' }),
    candidate({ productionUseRequiresRevalidation: false }),
    candidate({ draftSelectable: true, draftRefusalCode: 'inventory_parent_roll_context_required' }),
    candidate({ positions: [{ ...candidate().positions[0], productId: '60000000-0000-4000-8000-000000000001' }] }),
  ])('fails closed on a candidate outside the exact owner contract (%#)', (item) => {
    expect(decodeParentRollCandidates({ items: [item], searchWindowFull: false })).toBeNull()
  })

  it('rejects duplicate label ids and a partial page', () => {
    expect(decodeParentRollCandidates({ items: [candidate(), candidate()], searchWindowFull: false })).toBeNull()
    expect(decodeParentRollCandidates({ items: [candidate()] })).toBeNull()
  })
})

describe('fetchParentRollCandidates', () => {
  it('uses the declared route, bounded query, company context and private cache mode', async () => {
    const fetchMock = vi.fn(async () => json({ items: [candidate()], searchWindowFull: false }))
    vi.stubGlobal('fetch', fetchMock)

    const outcome = await fetchParentRollCandidates(COMPANY_A, '  LBL-7  ')

    expect(outcome).toMatchObject({ type: 'ok-single' })
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe(`${PARENT_ROLL_CANDIDATES_PATH}?limit=25&q=LBL-7`)
    expect(init).toMatchObject({ cache: 'no-store' })
    expect(init.headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
  })

  it('passes owner refusals through and fails closed on a foreign-company row', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => problem(409, { code: 'inventory_parent_roll_read_conflict' })))
    expect(await fetchParentRollCandidates(COMPANY_A)).toMatchObject({ type: 'http', status: 409 })

    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        json({
          items: [candidate({ companyId: 'bbbbbbbb-0000-0000-0000-000000000002' })],
          searchWindowFull: false,
        }),
      ),
    )
    expect(await fetchParentRollCandidates(COMPANY_A)).toEqual({ type: 'malformed' })
  })
})
