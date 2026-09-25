import { afterEach, describe, expect, it, vi } from 'vitest'
import createRequestRaw from './examples/create-request.json?raw'
import createResponseRaw from './examples/create-response.json?raw'
import replayResponseRaw from './examples/replay-response.json?raw'
import detailResponseRaw from './examples/detail-response.json?raw'
import listResponseRaw from './examples/list-response.json?raw'
import errorReplayRaw from './examples/error-replay.json?raw'
import { createDraft, fetchDraftDetail, fetchDraftPage } from './api'
import type { DraftCreateRequest } from './contract'

// The examples' company. Synthetic.
const COMPANY = '99999999-9999-9999-9999-999999999999'
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const REQUEST = JSON.parse(createRequestRaw) as DraftCreateRequest
const CONTEXT = { idempotencyKey: 'k-1', companyId: COMPANY }

function respond(status: number, body: string, headers: Record<string, string> = {}) {
  const calls: { url: string; init: RequestInit }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init: RequestInit) => {
      calls.push({ url, init })
      return new Response(body, { status, headers: { 'Content-Type': 'application/json', ...headers } })
    }),
  )
  return calls
}

const tweak = (raw: string, change: (body: any) => void) => {
  const body = JSON.parse(raw)
  change(body)
  return JSON.stringify(body)
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('createDraft — 201 first commit vs 200 replay', () => {
  it('201 with isReplay=false is success, sent under the frozen key and company, body without company', async () => {
    const calls = respond(201, createResponseRaw, { Location: `/api/v1/sales/drafts/${DRAFT}` })
    const outcome = await createDraft(REQUEST, CONTEXT)
    expect(outcome.type).toBe('succeeded')
    const headers = calls[0].init.headers as Record<string, string>
    expect(calls[0].url).toBe('/api/v1/sales/drafts')
    expect(calls[0].init.method).toBe('POST')
    expect(headers['Idempotency-Key']).toBe('k-1')
    expect(headers['X-Company-Id']).toBe(COMPANY)
    expect(JSON.parse(calls[0].init.body as string)).toEqual(REQUEST)
    expect(calls[0].init.body as string).not.toMatch(/companyId|version|state/)
  })

  it('201 with Idempotency-Replayed and isReplay=true is the same attempt replayed', async () => {
    respond(201, replayResponseRaw, { 'Idempotency-Replayed': 'true', Location: `/api/v1/sales/drafts/${DRAFT}` })
    const outcome = await createDraft(REQUEST, CONTEXT)
    expect(outcome.type === 'succeeded' && outcome.data.isReplay).toBe(true)
  })

  it.each([
    ['201 claiming a replay without the header', 201, replayResponseRaw, {}],
    ['201 with the replay header claiming a first commit', 201, createResponseRaw, { 'Idempotency-Replayed': 'true' }],
    ['200 claiming a first commit', 200, createResponseRaw, {}],
    ['200 with the replay header', 200, replayResponseRaw, { 'Idempotency-Replayed': 'true' }],
  ])('%s is not success: the draft may exist, so the outcome is unknown', async (_, status, body, headers) => {
    respond(status, body, headers)
    expect((await createDraft(REQUEST, CONTEXT)).type).toBe('malformed')
  })

  it.each([
    ['another company', (b: any) => (b.detail.draft.companyId = '99999999-9999-9999-9999-999999999998')],
    ['another account', (b: any) => (b.detail.draft.companyAccountId = '55555555-5555-5555-5555-555555555556')],
    ['another line id', (b: any) => (b.detail.lines[0].lineId = '66666666-6666-6666-6666-666666666667')],
    ['another slice id', (b: any) => (b.detail.lines[0].requestedDeliverySlices[0].sliceId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbd')],
    ['a rewritten quantity text', (b: any) => (b.detail.lines[0].quantity = '1000.0')],
    ['a later revision', (b: any) => (b.detail.draft.revision = 'Rev.1')],
    // Same ids, different content: the review's counter-examples.
    ['the same slice ids planned as 500 + 500', (b: any) => {
      b.detail.lines[0].requestedDeliverySlices[0].quantity = '500'
      b.detail.lines[0].requestedDeliverySlices[1].quantity = '500'
    }],
    ['a slice moved to another date', (b: any) => (b.detail.lines[0].requestedDeliverySlices[1].requestedDeliveryDate = '2026-09-23')],
    ['a plan that became unknown', (b: any) => (b.detail.lines[0].requestedDeliverySlices = null)],
    ['another line due date', (b: any) => (b.detail.lines[0].requestedDeliveryDate = '2026-09-21')],
    ['another header due date', (b: any) => (b.detail.requestedDeliveryDate = '2026-09-30')],
    ['a line note the person did not write', (b: any) => (b.detail.lines[0].note = 'x')],
    ['a header note the person did not write', (b: any) => (b.detail.note = 'x')],
    ['a price filled in', (b: any) => {
      b.detail.lines[0].unitPrice = '0'
      b.detail.lines[0].priceUnitId = '44444444-4444-4444-4444-444444444444'
    }],
    ['a discount filled in', (b: any) => (b.detail.lines[0].discountPercent = '0')],
    ['another product version', (b: any) => (b.detail.lines[0].productVersion = '8')],
    ['an owner reference filled in', (b: any) => (b.detail.paymentTerms = { id: '12121212-1212-1212-1212-121212121212', expectedVersion: '1' })],
    ['another business date', (b: any) => (b.detail.draft.businessDate = '2026-09-14')],
  ])('a body describing %s is not an answer to this attempt', async (_, change) => {
    respond(201, tweak(createResponseRaw, change))
    expect((await createDraft(REQUEST, CONTEXT)).type).toBe('malformed')
  })

  it('a Location for another record is not trusted', async () => {
    respond(201, createResponseRaw, { Location: '/api/v1/sales/drafts/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab' })
    expect((await createDraft(REQUEST, CONTEXT)).type).toBe('malformed')
  })

  it('a refusal carries its decoded problem', async () => {
    respond(409, errorReplayRaw)
    const outcome = await createDraft(REQUEST, CONTEXT)
    expect(outcome).toMatchObject({ type: 'http', status: 409, problem: { code: 'same_key_payload_mismatch' } })
  })

  it('no response is a network error, not a refusal', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))))
    expect((await createDraft(REQUEST, CONTEXT)).type).toBe('network-error')
  })
})

describe('reads are bound to their company and record', () => {
  it('reads a page and refuses one carrying another company’s row', async () => {
    const calls = respond(200, listResponseRaw)
    expect((await fetchDraftPage(null, COMPANY)).type).toBe('ok-single')
    expect(calls[0].url).toBe('/api/v1/sales/drafts?pageSize=50')
    respond(200, listResponseRaw)
    expect((await fetchDraftPage('c1', '99999999-9999-9999-9999-999999999998')).type).toBe('malformed')
  })

  it('sends the opaque cursor verbatim', async () => {
    const calls = respond(200, listResponseRaw)
    await fetchDraftPage('opaque/+=', COMPANY)
    expect(calls[0].url).toBe('/api/v1/sales/drafts?pageSize=50&cursor=opaque%2F%2B%3D')
  })

  it('reads a detail only for the id asked and the company it was read under', async () => {
    respond(200, detailResponseRaw)
    expect((await fetchDraftDetail(DRAFT, COMPANY)).type).toBe('ok-single')
    respond(200, detailResponseRaw)
    expect((await fetchDraftDetail('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab', COMPANY)).type).toBe('malformed')
    respond(200, detailResponseRaw)
    expect((await fetchDraftDetail(DRAFT, '99999999-9999-9999-9999-999999999998')).type).toBe('malformed')
  })

  it('passes a 404 through as a status, never as an empty record', async () => {
    respond(404, '{"status":404,"title":"Not Found"}')
    expect(await fetchDraftDetail(DRAFT, COMPANY)).toMatchObject({ type: 'http', status: 404 })
  })
})
