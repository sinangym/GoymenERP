/**
 * ORG-05 (KIM-S23) — the two VKN commands' wire rules: exact bodies, headers, and how every answer is classified.
 * `fetch` is stubbed with synthetic responses; nothing here is evidence of the real endpoint.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  sendCompanyTaxIdChangeRequest,
  sendCompanyTaxIdDecision,
  taxIdDecisionPath,
  taxIdDecisionWireBody,
  taxIdRequestWireBody,
} from './companyTaxIdApi'
import { COMPANY_TAX_ID_PROBLEM } from './companyTaxIdContract'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const REQUEST = 'cccccccc-0000-0000-0000-000000000003'
const KEY = 'key-1'

const REQUEST_COMMAND = { expectedVersion: 3, proposedVkn: ' 0987654321 ', reason: '  Vergi dairesi yazısı ' }
const DECISION_COMMAND = { requestId: REQUEST, expectedVersion: 3, decision: 'approve' as const, note: '   ' }

function answer(status: number, body: unknown = null, headers: Record<string, string> = {}): Response {
  return new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers },
  })
}

function stub(response: Response | Error) {
  const fn = vi.fn(async () => {
    if (response instanceof Error) throw response
    return response
  })
  vi.stubGlobal('fetch', fn)
  return fn
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('wire bodies', () => {
  it('sends only what the server takes, trimmed', () => {
    expect(taxIdRequestWireBody(REQUEST_COMMAND)).toEqual({
      expectedVersion: 3,
      proposedVkn: '0987654321',
      reason: 'Vergi dairesi yazısı',
    })
    expect(taxIdDecisionWireBody(DECISION_COMMAND)).toEqual({ expectedVersion: 3, decision: 'approve', note: null })
    expect(taxIdDecisionWireBody({ ...DECISION_COMMAND, note: ' tamam ' }).note).toBe('tamam')
  })

  it('encodes the request id into the decision path', () => {
    expect(taxIdDecisionPath(REQUEST)).toBe(`/organization/company/tax-id/change-requests/${REQUEST}/decision`)
  })
})

describe('sendCompanyTaxIdChangeRequest', () => {
  it('posts with the company, the caller’s key and JSON', async () => {
    const fn = stub(answer(201, { id: REQUEST, version: 1 }))
    const outcome = await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)
    expect(outcome).toEqual({ type: 'created', record: { id: REQUEST, version: 1 }, replayed: false })
    const [url, init] = fn.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toMatch(/\/organization\/company\/tax-id\/change-requests$/)
    expect(init.method).toBe('POST')
    const headers = init.headers as Record<string, string>
    expect(headers['X-Company-Id']).toBe(COMPANY)
    expect(headers['Idempotency-Key']).toBe(KEY)
    expect(headers['Content-Type']).toBe('application/json')
  })

  it('reports a replayed creation', async () => {
    stub(answer(201, { id: REQUEST, version: 1 }, { 'Idempotency-Replayed': 'true' }))
    const outcome = await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)
    expect(outcome.type === 'created' && outcome.replayed).toBe(true)
  })

  it('treats an unreadable 201 as unknown, never a refusal', async () => {
    stub(answer(201, { id: 'x' }))
    expect(await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)).toEqual({
      type: 'unknown',
      unknown: { kind: 'undecodable', status: 201 },
    })
  })

  it('classifies the VKN-in-use 422 by its code', async () => {
    stub(answer(422, { code: COMPANY_TAX_ID_PROBLEM.inUse }))
    expect(await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)).toEqual({
      type: 'failed',
      failure: { kind: 'tax-id-in-use' },
      replayed: false,
    })
  })

  it('classifies the check-digit 422 by its code (KIM-D40)', async () => {
    stub(answer(422, { code: COMPANY_TAX_ID_PROBLEM.checkDigitInvalid }))
    expect(await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)).toEqual({
      type: 'failed',
      failure: { kind: 'tax-id-check-digit-invalid' },
      replayed: false,
    })
  })

  it('treats a 422 without the in-use code as unknown', async () => {
    stub(answer(422, { code: COMPANY_TAX_ID_PROBLEM.changePending }))
    const outcome = await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)
    expect(outcome.type).toBe('unknown')
  })

  it.each([
    [COMPANY_TAX_ID_PROBLEM.changePending, 'tax-id-change-pending'],
    [COMPANY_TAX_ID_PROBLEM.unchanged, 'tax-id-unchanged'],
    [COMPANY_TAX_ID_PROBLEM.idempotencyConflict, 'idempotency-conflict'],
  ])('classifies the 409 %s', async (code, kind) => {
    stub(answer(409, { code }))
    const outcome = await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)
    expect(outcome.type === 'failed' && outcome.failure.kind).toBe(kind)
  })

  it('classifies a version conflict only with its conflict object, and marks a replayed one as not current', async () => {
    stub(
      answer(
        409,
        { code: COMPANY_TAX_ID_PROBLEM.versionConflict, conflict: { expectedVersion: 3, currentVersion: 4 } },
        { 'Idempotency-Replayed': 'true' },
      ),
    )
    expect(await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)).toEqual({
      type: 'failed',
      failure: {
        kind: 'version-conflict',
        conflict: { expectedVersion: 3, currentVersion: 4 },
        conflictIsCurrent: false,
      },
      replayed: true,
    })
    stub(answer(409, { code: COMPANY_TAX_ID_PROBLEM.versionConflict }))
    expect((await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)).type).toBe('unknown')
  })

  it('treats a code-less 409 as unknown', async () => {
    stub(answer(409, { title: 'Çakışma' }))
    expect(await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)).toEqual({
      type: 'unknown',
      unknown: { kind: 'undecodable', status: 409 },
    })
  })

  it.each([
    [400, 'validation'],
    [401, 'unauthenticated'],
    [403, 'forbidden'],
    [404, 'not-found'],
    [415, 'unsupported-media'],
  ])('classifies %i', async (status, kind) => {
    stub(answer(status, {}))
    const outcome = await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)
    expect(outcome.type === 'failed' && outcome.failure.kind).toBe(kind)
  })

  it('treats a 5xx, another 2xx and a transport failure as unknown', async () => {
    stub(answer(503, {}))
    expect(await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)).toEqual({
      type: 'unknown',
      unknown: { kind: 'service-unavailable', status: 503 },
    })
    stub(answer(200, {}))
    expect(await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)).toEqual({
      type: 'unknown',
      unknown: { kind: 'unexpected-success', status: 200 },
    })
    stub(new TypeError('failed to fetch'))
    const outcome = await sendCompanyTaxIdChangeRequest(COMPANY, KEY, REQUEST_COMMAND)
    expect(outcome.type).toBe('unknown')
  })
})

describe('sendCompanyTaxIdDecision', () => {
  it('posts to the request’s decision path and reads 204 as decided', async () => {
    const fn = stub(answer(204))
    expect(await sendCompanyTaxIdDecision(COMPANY, KEY, DECISION_COMMAND)).toEqual({
      type: 'decided',
      replayed: false,
    })
    const [url, init] = fn.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toMatch(new RegExp(`/change-requests/${REQUEST}/decision$`))
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: 3, decision: 'approve', note: null })
  })

  it.each([
    [COMPANY_TAX_ID_PROBLEM.selfDecision, 'tax-id-self-decision'],
    [COMPANY_TAX_ID_PROBLEM.changeNotPending, 'tax-id-change-not-pending'],
    [COMPANY_TAX_ID_PROBLEM.changeStale, 'tax-id-change-stale'],
  ])('classifies the 409 %s', async (code, kind) => {
    stub(answer(409, { code }))
    const outcome = await sendCompanyTaxIdDecision(COMPANY, KEY, DECISION_COMMAND)
    expect(outcome.type === 'failed' && outcome.failure.kind).toBe(kind)
  })

  it('treats a 201 on the decision as unknown', async () => {
    stub(answer(201, { id: REQUEST, version: 1 }))
    expect(await sendCompanyTaxIdDecision(COMPANY, KEY, DECISION_COMMAND)).toEqual({
      type: 'unknown',
      unknown: { kind: 'unexpected-success', status: 201 },
    })
  })
})
