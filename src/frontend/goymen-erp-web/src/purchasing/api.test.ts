import { afterEach, describe, expect, it, vi } from 'vitest'
import { createPurchaseOrder, fetchPurchaseOrder, fetchPurchaseOrders, fetchPurchasingProductCandidates, fetchPurchasingSupplierOptions } from './api'
import { approvePurchaseOrder } from './approvalApi'

const company = '22222222-2222-4222-8222-222222222222'
const order = '11111111-1111-4111-8111-111111111111'
const commandSummary = { id: order, companyId: company, status: 'Draft', version: '1', supplierPartyId: '33333333-3333-4333-8333-333333333333', companyAccountId: '44444444-4444-4444-8444-444444444444', sourceRequestId: null, effectiveOn: '2026-09-22', createdAtUtc: '2026-09-22T10:00:00Z', updatedAtUtc: '2026-09-22T10:00:00Z', lineCount: 0 }

const supplierDisplay = { partyId: commandSummary.supplierPartyId, code: 'CAR-000042', legalName: 'Tedarikçi Sanayi A.Ş.', tradeName: null }
/** GET list/detail carry the #661 read summary; create/approve responses keep `commandSummary`. */
const summary = { ...commandSummary, supplierDisplay }

afterEach(() => vi.unstubAllGlobals())

describe('purchasing response binding', () => {
  it('refuses list and detail data from another company or order', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [{ ...summary, companyId: '99999999-9999-4999-8999-999999999999' }], nextCursor: null }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ order: { ...summary, id: '88888888-8888-4888-8888-888888888888' }, lines: [] }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect((await fetchPurchaseOrders(company)).type).toBe('malformed')
    expect((await fetchPurchaseOrder(company, order)).type).toBe('malformed')
  })

  it('reads the record-bound supplier identity through list and detail, and refuses a read without it', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [summary], nextCursor: null }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ order: summary, lines: [] }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [commandSummary], nextCursor: null }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ order: commandSummary, lines: [] }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ order: { ...summary, supplierDisplay: { ...supplierDisplay, partyId: company } }, lines: [] }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    const list = await fetchPurchaseOrders(company)
    expect(list).toEqual({ type: 'ok-single', data: { items: [summary], nextCursor: null } })
    const detail = await fetchPurchaseOrder(company, order)
    expect(detail.type === 'ok-single' ? detail.data.order : null).toEqual(summary)
    // One read each: the identity arrives with the order, never through a second lookup.
    expect(fetch).toHaveBeenCalledTimes(2)
    expect((await fetchPurchaseOrders(company)).type).toBe('malformed')
    expect((await fetchPurchaseOrder(company, order)).type).toBe('malformed')
    expect((await fetchPurchaseOrder(company, order)).type).toBe('malformed')
  })

  it('refuses an approve response whose order is read-shaped: commands keep the legacy summary', async () => {
    const body = (orderBody: unknown) => new Response(JSON.stringify({
      detail: { order: orderBody, lines: [] }, isReplay: false, originalCorrelationId: 'corr-1',
    }), { status: 200 })
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(body({ ...commandSummary, status: 'Approved', version: '2' }))
      .mockResolvedValueOnce(body({ ...summary, status: 'Approved', version: '2' })))
    // The same answer, same headers: applied in the legacy shape, refused once it carries a read-only field.
    expect((await approvePurchaseOrder(company, order, '1', 'key-1')).type).toBe('applied')
    expect(await approvePurchaseOrder(company, order, '1', 'key-1')).toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it('treats a replay body without matching replay headers as unknown', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      detail: { order: { ...commandSummary, status: 'Approved', version: '2' }, lines: [] },
      isReplay: true, originalCorrelationId: 'corr-1',
    }), { status: 200 })))
    expect(await approvePurchaseOrder(company, order, '1', 'key-1')).toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it('binds only the flat Product purchase candidate contract', async () => {
    const candidate = {
      productId: '55555555-5555-4555-8555-555555555555', productVersion: '7', code: 'SAC-01', name: 'Sac',
      unitId: '66666666-6666-4666-8666-666666666666', unitCode: 'kg',
      profileId: '77777777-7777-4777-8777-777777777777', profileVersion: '4',
    }
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [candidate], searchWindowFull: false }), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ productId: candidate.productId, productVersion: '7', code: 'SAC-01', name: 'Sac', orderUnits: [] }],
        searchWindowFull: false,
      }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)

    expect(await fetchPurchasingProductCandidates(company, '2026-09-23', ' sac ')).toEqual({
      type: 'ok-single', data: { items: [candidate], searchWindowFull: false },
    })
    expect(fetch).toHaveBeenNthCalledWith(1,
      '/api/v1/purchasing/references/products?effectiveOn=2026-09-23&limit=25&q=sac',
      expect.objectContaining({ cache: 'no-store' }),
    )
    expect((await fetchPurchasingProductCandidates(company, '2026-09-23')).type).toBe('malformed')
  })

  it('projects only the selected-company active Supplier account from the Cari owner list', async () => {
    const party = {
      id: '33333333-3333-4333-8333-333333333333', code: 'CAR-42', kind: 1,
      legalName: 'Tedarikçi AŞ', tradeName: null, isActive: true, version: 9,
      companyAccounts: [{
        id: '44444444-4444-4444-8444-444444444444', companyId: company,
        roles: 2, isActive: true, version: 6,
      }],
    }
    const page = (item: typeof party) => ({
      items: [item], nextCursor: 'next-1', hasMore: true, totalCount: null, countScope: null,
    })
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(page(party)), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(page({
        ...party, companyAccounts: [{ ...party.companyAccounts[0], roles: 1 }],
      })), { status: 200 }))
    vi.stubGlobal('fetch', fetch)

    expect(await fetchPurchasingSupplierOptions(company, { q: ' metal ', cursor: 'cursor-1' })).toEqual({
      type: 'ok-single',
      data: {
        items: [{
          supplierPartyId: party.id,
          companyAccountId: party.companyAccounts[0].id,
          label: 'CAR-42 — Tedarikçi AŞ',
        }],
        nextCursor: 'next-1',
      },
    })
    expect(fetch).toHaveBeenNthCalledWith(1,
      '/api/v1/counterparties?q=metal&role=Supplier&partyState=active&accountState=active&pageSize=50&cursor=cursor-1',
      expect.objectContaining({ headers: expect.objectContaining({ 'X-Company-Id': company }) }),
    )
    expect((await fetchPurchasingSupplierOptions(company)).type).toBe('malformed')
  })

  it('binds a 201 create answer to the exact supplier, product version, unit and quantity', async () => {
    const line = {
      lineId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      productId: '55555555-5555-4555-8555-555555555555', expectedProductVersion: '7',
      unitId: '66666666-6666-4666-8666-666666666666', quantity: '12.5', sourceRequestLineId: null,
    }
    const payload = {
      supplierPartyId: commandSummary.supplierPartyId, companyAccountId: commandSummary.companyAccountId,
      sourceRequestId: null, effectiveOn: '2026-09-23', lines: [line],
    }
    const createdOrder = { ...commandSummary, lineCount: 1, effectiveOn: payload.effectiveOn }
    const detailLine = {
      lineId: line.lineId, lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null,
      productId: line.productId, productVersion: '7', unitId: line.unitId,
      ordered: '12.5', technical: '0', committed: '12.5', accepted: '0', remaining: '12.5',
    }
    const response = new Response(JSON.stringify({
      detail: { order: createdOrder, lines: [detailLine] }, isReplay: false, originalCorrelationId: 'corr-1',
    }), { status: 201, headers: { Location: `/api/v1/purchasing/orders/${order}` } })
    const fetch = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetch)

    expect((await createPurchaseOrder(payload, { companyId: company, idempotencyKey: 'key-1' })).type).toBe('applied')
    expect(fetch).toHaveBeenCalledWith('/api/v1/purchasing/orders', expect.objectContaining({
      method: 'POST', body: JSON.stringify(payload), headers: expect.objectContaining({ 'Idempotency-Key': 'key-1' }),
    }))
  })
})

/**
 * PURCHASING-SUPPLIER-FILTER-UI-01: the list read forwards the accepted optional `supplierPartyId` query (#671).
 * The server filters; the client only sends the parameter and refuses an answer that is not that supplier's.
 */
describe('purchase order list supplier filter', () => {
  const supplier = commandSummary.supplierPartyId
  const otherSupplier = '77777777-7777-4777-8777-777777777777'
  const otherOrder = {
    ...summary, id: '88888888-8888-4888-8888-888888888888', supplierPartyId: otherSupplier,
    supplierDisplay: { ...supplierDisplay, partyId: otherSupplier, code: 'CAR-000099' },
  }
  const answer = (items: unknown[], nextCursor: string | null = null) =>
    new Response(JSON.stringify({ items, nextCursor }), { status: 200 })
  const sent = (fetch: ReturnType<typeof vi.fn>, call: number) => {
    const url = new URL(String(fetch.mock.calls[call][0]), 'http://local')
    return { path: url.pathname, query: Object.fromEntries(url.searchParams.entries()), headers: new Headers((fetch.mock.calls[call][1] as RequestInit).headers) }
  }

  it('forwards supplierPartyId as the server query, alone and together with the status and the cursor', async () => {
    const fetch = vi.fn().mockImplementation(async () => answer([summary], 'c-3'))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchPurchaseOrders(company, { supplierPartyId: supplier })).toEqual({ type: 'ok-single', data: { items: [summary], nextCursor: 'c-3' } })
    await fetchPurchaseOrders(company, { supplierPartyId: supplier, status: 'Approved', cursor: 'c-2' })
    expect(sent(fetch, 0).query).toEqual({ supplierPartyId: supplier })
    expect(sent(fetch, 1).query).toEqual({ cursor: 'c-2', status: 'Approved', supplierPartyId: supplier })
    // The same endpoint and the same company header as the unfiltered read; one request each.
    expect(sent(fetch, 0).path).toBe('/api/v1/purchasing/orders')
    expect(sent(fetch, 1).headers.get('X-Company-Id')).toBe(company)
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it.each([
    ['no query', undefined],
    ['an empty query', {}],
    ['a null supplier', { supplierPartyId: null }],
    ['a blank supplier', { supplierPartyId: '' }],
  ])('sends no supplier parameter for %s: the unfiltered read is unchanged', async (_name, query) => {
    const fetch = vi.fn().mockImplementation(async () => answer([summary, otherOrder]))
    vi.stubGlobal('fetch', fetch)
    const list = await fetchPurchaseOrders(company, query)
    expect(sent(fetch, 0).query).toEqual({})
    expect(String(fetch.mock.calls[0][0])).not.toContain('?')
    // An unfiltered page may hold any number of suppliers.
    expect(list).toEqual({ type: 'ok-single', data: { items: [summary, otherOrder], nextCursor: null } })
  })

  it('refuses a filtered page that holds another supplier’s order — whole, with nothing kept from it', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => answer([summary, otherOrder])))
    expect(await fetchPurchaseOrders(company, { supplierPartyId: supplier })).toEqual({ type: 'malformed' })
    expect(await fetchPurchaseOrders(company, { supplierPartyId: otherSupplier })).toEqual({ type: 'malformed' })
  })

  it('accepts an empty filtered page and still refuses another company’s order in a filtered one', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(answer([]))
      .mockResolvedValueOnce(answer([{ ...summary, companyId: '99999999-9999-4999-8999-999999999999' }]))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchPurchaseOrders(company, { supplierPartyId: supplier })).toEqual({ type: 'ok-single', data: { items: [], nextCursor: null } })
    expect(await fetchPurchaseOrders(company, { supplierPartyId: supplier })).toEqual({ type: 'malformed' })
  })

  it('hands a filter the server refuses back as the server’s own answer, with no second read', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ code: 'validation_failed' }), { status: 400 }))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchPurchaseOrders(company, { supplierPartyId: supplier })).toEqual({ type: 'http', status: 400, body: { code: 'validation_failed' } })
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
