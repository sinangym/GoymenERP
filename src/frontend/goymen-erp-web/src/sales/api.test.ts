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

  it('200 with isReplay=true is the same attempt replayed', async () => {
    respond(200, replayResponseRaw)
    const outcome = await createDraft(REQUEST, CONTEXT)
    expect(outcome.type === 'succeeded' && outcome.data.isReplay).toBe(true)
  })

  it.each([
    ['201 claiming a replay', 201, replayResponseRaw],
    ['200 claiming a first commit', 200, createResponseRaw],
  ])('%s is not success: the draft may exist, so the outcome is unknown', async (_, status, body) => {
    respond(status, body)
    expect((await createDraft(REQUEST, CONTEXT)).type).toBe('malformed')
  })

  it.each([
    ['another company', (b: any) => (b.detail.draft.companyId = '99999999-9999-9999-9999-999999999998')],
    ['another account', (b: any) => (b.detail.draft.companyAccountId = '55555555-5555-5555-5555-555555555556')],
    ['another line id', (b: any) => (b.detail.lines[0].lineId = '66666666-6666-6666-6666-666666666667')],
    ['another slice id', (b: any) => (b.detail.lines[0].requestedDeliverySlices[0].sliceId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbd')],
    ['a rewritten quantity text', (b: any) => (b.detail.lines[0].quantity = '1000.0')],
    ['a later revision', (b: any) => (b.detail.draft.revision = 'Rev.1')],
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
