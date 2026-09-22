/**
 * The FIN-T04b HTTP port with a stubbed `fetch`. Unit evidence of the mapping only: not a live
 * API run, not browser evidence.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { API_BASE } from './http'
import {
  BANK_ACCOUNTS_PATH,
  BANK_ACCOUNT_CURRENCY_CANDIDATES_PATH,
  BANK_ACCOUNT_MASTER_PATH,
  DRAFTS_PATH,
  MANUAL_RECEIPTS_PATH,
  createHttpFinancePort,
} from './httpPort'

const SCOPE = { companyId: '11111111-1111-4111-8111-111111111111' }

afterEach(() => {
  vi.unstubAllGlobals()
})

function stubFetch(response: Response | Error) {
  const fetchMock = vi.fn(async (_url: string, _init?: RequestInit) => {
    if (response instanceof Error) throw response
    return response
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function headersOf(init: RequestInit | undefined): Record<string, string> {
  return (init?.headers ?? {}) as Record<string, string>
}

describe('reads', () => {
  it('restates a 200 as a Found ReadResult and binds the shell company in X-Company-Id', async () => {
    const page = { companyId: SCOPE.companyId, items: [] }
    const fetchMock = stubFetch(new Response(JSON.stringify(page), { status: 200 }))

    const reply = await createHttpFinancePort().listBankAccounts(SCOPE, new AbortController().signal)

    expect(reply).toEqual({ kind: 'body', body: { outcome: 'Found', resource: page } })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API_BASE}${BANK_ACCOUNTS_PATH}`)
    expect(headersOf(init)['X-Company-Id']).toBe(SCOPE.companyId)
  })

  it.each([
    [403, 'Denied'],
    [404, 'NotFound'],
    [503, 'Unavailable'],
  ])('restates %i as %s with no resource', async (status, outcome) => {
    stubFetch(new Response(null, { status }))

    const reply = await createHttpFinancePort().sourceDetail(SCOPE, { sourceId: 'a b' }, new AbortController().signal)

    expect(reply).toEqual({ kind: 'body', body: { outcome, resource: null } })
  })

  it('passes any other status on undecodable, never as a ReadResult', async () => {
    stubFetch(new Response(null, { status: 400 }))

    const reply = await createHttpFinancePort().listSources(SCOPE, { cursor: 'bad' }, new AbortController().signal)

    expect(reply).toEqual({ kind: 'body', body: undefined })
  })

  it('sends the cursor only when there is one, and escapes the detail id', async () => {
    const fetchMock = stubFetch(new Response(JSON.stringify({}), { status: 200 }))
    const port = createHttpFinancePort()

    await port.listSources(SCOPE, { cursor: null }, new AbortController().signal)
    await port.listSources(SCOPE, { cursor: 'c+/=' }, new AbortController().signal)
    await port.sourceDetail(SCOPE, { sourceId: 'x/y' }, new AbortController().signal)

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      `${API_BASE}${MANUAL_RECEIPTS_PATH}`,
      `${API_BASE}${MANUAL_RECEIPTS_PATH}?cursor=c%2B%2F%3D`,
      `${API_BASE}${MANUAL_RECEIPTS_PATH}/x%2Fy`,
    ])
  })

  it('is no-reply when nothing arrived', async () => {
    stubFetch(new TypeError('network'))

    expect(await createHttpFinancePort().listBankAccounts(SCOPE, new AbortController().signal)).toEqual({ kind: 'no-reply' })
  })
})

describe('draftSave', () => {
  const REQUEST = { draftId: null, expectedVersion: null } as never

  it.each([200, 409, 422])('passes the CommandResult body of %i on as it is', async (status) => {
    const result = { outcome: 'ValidationFailed', resource: null, isReplay: false, originalCorrelationId: 'c', issues: [] }
    const fetchMock = stubFetch(new Response(JSON.stringify(result), { status }))

    const reply = await createHttpFinancePort().draftSave(SCOPE, 'key-1', REQUEST)

    expect(reply).toEqual({ kind: 'body', body: result })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API_BASE}${DRAFTS_PATH}`)
    expect(init?.method).toBe('POST')
    expect(headersOf(init)['Idempotency-Key']).toBe('key-1')
    expect(headersOf(init)['X-Company-Id']).toBe(SCOPE.companyId)
    // The company is never in the body.
    expect(init?.body).toBe(JSON.stringify(REQUEST))
  })

  it.each([
    [403, 'Denied'],
    [503, 'Unavailable'],
  ])('restates the platform problem %i as the %s outcome with no resource', async (status, outcome) => {
    stubFetch(new Response(JSON.stringify({ code: 'x' }), { status }))

    expect(await createHttpFinancePort().draftSave(SCOPE, 'key-1', REQUEST)).toEqual({
      kind: 'body',
      body: { outcome, resource: null, isReplay: false, originalCorrelationId: '', issues: [] },
    })
  })

  it.each([400, 401, 415, 500])('carries no command result for %i', async (status) => {
    stubFetch(new Response('{}', { status }))

    expect(await createHttpFinancePort().draftSave(SCOPE, 'key-1', REQUEST)).toEqual({ kind: 'body', body: undefined })
  })

  it('is no-reply, an unknown outcome, when nothing arrived', async () => {
    stubFetch(new TypeError('network'))

    expect(await createHttpFinancePort().draftSave(SCOPE, 'key-1', REQUEST)).toEqual({ kind: 'no-reply' })
  })
})

describe('record and reverse', () => {
  it('records through its route with the #258 RecordRequest, the key and the company header (FIN-T05a)', async () => {
    const refused = {
      outcome: 'ValidationFailed',
      resource: null,
      isReplay: false,
      originalCorrelationId: 'c-1',
      issues: [{ field: 'period', code: 'finance_period_rule_unavailable' }],
    }
    const fetchMock = stubFetch(new Response(JSON.stringify(refused), { status: 422 }))
    const draftId = 'dddddddd-0000-4000-8000-000000000001'

    const reply = await createHttpFinancePort().record(SCOPE, 'key-r', { draftId, expectedVersion: '3' })

    // A refused Record is a definite body (here the rule could not decide), never an unknown outcome to resend.
    expect(reply).toEqual({ kind: 'body', body: refused })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API_BASE}${MANUAL_RECEIPTS_PATH}/${draftId}/record`)
    expect(init?.method).toBe('POST')
    expect(headersOf(init)['Idempotency-Key']).toBe('key-r')
    expect(headersOf(init)['X-Company-Id']).toBe(SCOPE.companyId)
    expect(init?.body).toBe(JSON.stringify({ draftId, expectedVersion: '3' }))
  })

  it('reverse answers an explicit local Unavailable and sends nothing: there is no route (FIN-T06)', async () => {
    const fetchMock = stubFetch(new Response(null, { status: 200 }))

    const reply = await createHttpFinancePort().reverse(SCOPE, 'k', { sourceId: 's' } as never)

    expect(reply).toEqual({
      kind: 'body',
      body: { outcome: 'Unavailable', resource: null, isReplay: false, originalCorrelationId: '', issues: [] },
    })
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

describe('bank-account master (FIN-UI-1a)', () => {
  it('reads the master list and the currency candidates as ReadResults', async () => {
    const fetchMock = stubFetch(new Response(JSON.stringify({ companyId: SCOPE.companyId, items: [] }), { status: 200 }))
    const port = createHttpFinancePort()

    await port.listBankAccountMaster(SCOPE, new AbortController().signal)
    await port.bankAccountCurrencyCandidates(SCOPE, new AbortController().signal)

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
      `${API_BASE}${BANK_ACCOUNT_MASTER_PATH}`,
      `${API_BASE}${BANK_ACCOUNT_CURRENCY_CANDIDATES_PATH}`,
    ])
  })

  it('proposes with the key, the company header and exactly the proposal body', async () => {
    const result = { outcome: 'ValidationFailed', resource: null, isReplay: false, originalCorrelationId: 'c', issues: [] }
    const fetchMock = stubFetch(new Response(JSON.stringify(result), { status: 422 }))
    const proposal = {
      displayName: 'SENTETIK',
      currencyId: 'cur',
      bankName: 'SENTETIK Banka',
      accountType: 'SENTETIK Vadesiz',
      moneyMovementEligible: false,
      branchName: null,
      branchCode: null,
      iban: null,
      accountNumber: null,
    }

    const reply = await createHttpFinancePort().proposeBankAccount(SCOPE, 'key-p', proposal)

    expect(reply).toEqual({ kind: 'body', body: result })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API_BASE}${BANK_ACCOUNTS_PATH}`)
    expect(init?.method).toBe('POST')
    expect(headersOf(init)['Idempotency-Key']).toBe('key-p')
    expect(headersOf(init)['X-Company-Id']).toBe(SCOPE.companyId)
    expect(init?.body).toBe(JSON.stringify(proposal))
  })

  it('approves on the account\'s route with only the expected version in the body', async () => {
    const fetchMock = stubFetch(new Response(JSON.stringify({ code: 'forbidden' }), { status: 403 }))

    const reply = await createHttpFinancePort().approveBankAccount(SCOPE, 'key-a', 'a/b', { expectedVersion: '1' })

    // Ö4 and every other Denied is the platform's 403, restated as the Denied outcome.
    expect(reply).toEqual({
      kind: 'body',
      body: { outcome: 'Denied', resource: null, isReplay: false, originalCorrelationId: '', issues: [] },
    })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${API_BASE}${BANK_ACCOUNTS_PATH}/a%2Fb/approval`)
    expect(init?.body).toBe(JSON.stringify({ expectedVersion: '1' }))
  })
})
