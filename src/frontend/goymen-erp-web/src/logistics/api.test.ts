import { afterEach, describe, expect, it, vi } from 'vitest'
import createRequestRaw from './examples/create-request.json?raw'
import createResponseRaw from './examples/create-response.json?raw'
import replayResponseRaw from './examples/replay-response.json?raw'
import detailResponseRaw from './examples/detail-response.json?raw'
import issuedResponseRaw from './examples/issued-response.json?raw'
import loadResponseRaw from './examples/load-response.json?raw'
import listResponseRaw from './examples/list-response.json?raw'
import errorCapRaw from './examples/error-cap.json?raw'
import { createInstruction, fetchApprovedOrderLines, fetchInstructionDetail, fetchInstructionPage, issueInstruction, recordLoad } from './api'
import type { CreateInstructionRequest } from './contract'
import { approvedOrderLine, COMPANY_A, INSTRUCTION, LINE } from './testHarness'

const REQUEST = JSON.parse(createRequestRaw) as CreateInstructionRequest
const CONTEXT = { idempotencyKey: 'k-1', companyId: COMPANY_A }
const COMMAND = { ...CONTEXT, instructionId: INSTRUCTION }

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

describe('createInstruction — 201 first commit vs 201 replay', () => {
  it('201 with isReplay=false is success, sent under the frozen key and company, body without company or product', async () => {
    const calls = respond(201, createResponseRaw, { Location: `/api/v1/logistics/loading-instructions/${INSTRUCTION}` })
    expect((await createInstruction(REQUEST, CONTEXT)).type).toBe('succeeded')
    const headers = calls[0].init.headers as Record<string, string>
    expect(calls[0].url).toBe('/api/v1/logistics/loading-instructions')
    expect(calls[0].init.method).toBe('POST')
    expect(headers['Idempotency-Key']).toBe('k-1')
    expect(headers['X-Company-Id']).toBe(COMPANY_A)
    expect(JSON.parse(calls[0].init.body as string)).toEqual(REQUEST)
    expect(calls[0].init.body as string).not.toMatch(/companyId|productId|version|status|price/)
  })

  it('201 with Idempotency-Replayed and isReplay=true is the same attempt replayed', async () => {
    respond(201, replayResponseRaw, { 'Idempotency-Replayed': 'true' })
    const outcome = await createInstruction(REQUEST, CONTEXT)
    expect(outcome.type === 'succeeded' && outcome.data.isReplay).toBe(true)
  })

  it.each([
    ['201 claiming a replay without the header', 201, replayResponseRaw, {}],
    ['201 with the replay header claiming a first commit', 201, createResponseRaw, { 'Idempotency-Replayed': 'true' }],
    ['200 claiming a first commit', 200, createResponseRaw, {}],
  ])('%s is not success: the instruction may exist, so the outcome is unknown', async (_, status, body, headers) => {
    respond(status, body, headers)
    expect((await createInstruction(REQUEST, CONTEXT)).type).toBe('malformed')
  })

  it.each([
    ['another company', (b: any) => (b.detail.instruction.companyId = '99999999-9999-9999-9999-999999999998')],
    ['another customer account', (b: any) => (b.detail.instruction.companyAccountId = '55555555-5555-5555-5555-555555555556')],
    ['another business date', (b: any) => (b.detail.instruction.effectiveOn = '2026-09-20')],
    ['another line id', (b: any) => (b.detail.lines[0].lineId = '66666666-6666-6666-6666-666666666667')],
    ['another source line', (b: any) => (b.detail.lines[0].sourceOrderLineId = '66666666-6666-6666-6666-666666666667')],
    ['a rewritten quantity text', (b: any) => { b.detail.lines[0].instructed = '4.50'; b.detail.lines[0].remainingToLoad = '4.50' }],
    ['an already issued instruction', (b: any) => (b.detail.instruction.status = 'Issued')],
    ['a blocked line', (b: any) => (b.detail.lines[0].isBlocked = true)],
  ])('a body describing %s is not an answer to this attempt', async (_, change) => {
    respond(201, tweak(createResponseRaw, change))
    expect((await createInstruction(REQUEST, CONTEXT)).type).toBe('malformed')
  })

  it('a Location for another record is not trusted', async () => {
    respond(201, createResponseRaw, { Location: '/api/v1/logistics/loading-instructions/cccccccc-cccc-cccc-cccc-ccccccccccc1' })
    expect((await createInstruction(REQUEST, CONTEXT)).type).toBe('malformed')
  })

  it('no response is a network error, not a refusal', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))))
    expect((await createInstruction(REQUEST, CONTEXT)).type).toBe('network-error')
  })
})

describe('issue and record-load — 200 with the instruction after the transition', () => {
  it('issue: 200 is success only for THIS instruction under THIS company', async () => {
    const calls = respond(200, issuedResponseRaw)
    expect((await issueInstruction({ expectedVersion: '2' }, COMMAND)).type).toBe('succeeded')
    expect(calls[0].url).toBe(`/api/v1/logistics/loading-instructions/${INSTRUCTION}/issue`)
    respond(200, issuedResponseRaw)
    expect((await issueInstruction({ expectedVersion: '2' }, { ...COMMAND, instructionId: 'cccccccc-cccc-cccc-cccc-ccccccccccc1' })).type).toBe('malformed')
  })

  it('record-load: the recorded share must be the one sent, with the quantity text sent', async () => {
    const payload = { expectedVersion: '3', loadId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', quantity: '2.5', measurement: 'Measured' as const }
    const calls = respond(200, loadResponseRaw)
    expect((await recordLoad(LINE, payload, COMMAND)).type).toBe('succeeded')
    expect(calls[0].url).toBe(`/api/v1/logistics/loading-instructions/${INSTRUCTION}/lines/${LINE}/loads`)
    expect(JSON.parse(calls[0].init.body as string)).toEqual(payload)
    respond(200, loadResponseRaw)
    expect((await recordLoad(LINE, { ...payload, quantity: '2.50' }, COMMAND)).type).toBe('malformed')
    respond(200, loadResponseRaw)
    expect((await recordLoad(LINE, { ...payload, loadId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee1' }, COMMAND)).type).toBe('malformed')
  })

  it('a 409 carries its decoded problem and code', async () => {
    respond(409, errorCapRaw)
    const outcome = await recordLoad(LINE, { expectedVersion: '3', loadId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', quantity: '9', measurement: 'Measured' }, COMMAND)
    expect(outcome).toMatchObject({ type: 'http', status: 409, problem: { code: 'logistics_load_exceeds_instructed' } })
  })
})

describe('reads are bound to their company and record', () => {
  it('reads a page with the status filter and refuses one carrying another company’s row', async () => {
    const calls = respond(200, listResponseRaw)
    expect((await fetchInstructionPage(null, COMPANY_A, undefined, 'Draft')).type).toBe('ok-single')
    expect(calls[0].url).toBe('/api/v1/logistics/loading-instructions?pageSize=50&status=Draft')
    respond(200, listResponseRaw)
    expect((await fetchInstructionPage('c1', '99999999-9999-9999-9999-999999999998')).type).toBe('malformed')
  })

  it('reads a detail only for the id asked and the company it was read under', async () => {
    respond(200, detailResponseRaw)
    expect((await fetchInstructionDetail(INSTRUCTION, COMPANY_A)).type).toBe('ok-single')
    respond(200, detailResponseRaw)
    expect((await fetchInstructionDetail('cccccccc-cccc-cccc-cccc-ccccccccccc1', COMPANY_A)).type).toBe('malformed')
    respond(404, '{"status":404,"title":"Not Found"}')
    expect(await fetchInstructionDetail(INSTRUCTION, COMPANY_A)).toMatchObject({ type: 'http', status: 404 })
  })

  it('the picker reads the price-free approved-order-lines route under the company, with the cursor verbatim', async () => {
    const calls = respond(200, JSON.stringify({ items: [approvedOrderLine()], nextCursor: 'c2' }))
    const first = await fetchApprovedOrderLines(null, COMPANY_A)
    expect(calls[0].url).toBe('/api/v1/logistics/loading-instructions/approved-order-lines?pageSize=50')
    expect((calls[0].init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY_A)
    expect(first.type === 'ok-single' && first.data.nextCursor).toBe('c2')
    const more = respond(200, JSON.stringify({ items: [], nextCursor: null }))
    await fetchApprovedOrderLines('opaque/+=', COMPANY_A)
    expect(more[0].url).toBe('/api/v1/logistics/loading-instructions/approved-order-lines?pageSize=50&cursor=opaque%2F%2B%3D')
    respond(200, JSON.stringify({ items: [approvedOrderLine({ unitPrice: '1' })], nextCursor: null }))
    expect((await fetchApprovedOrderLines(null, COMPANY_A)).type).toBe('malformed')
    respond(403, '{"status":403,"title":"Forbidden"}')
    expect(await fetchApprovedOrderLines(null, COMPANY_A)).toMatchObject({ type: 'http', status: 403 })
  })
})
