import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  creditLimitCommandUrl,
  fetchCreditLimitAccount,
  fetchCreditLimitHistory,
  fetchCreditLimits,
  sendCreditLimitCommand,
  type CreditLimitCommand,
} from './CreditLimitHttp'
import {
  account,
  approvedSet,
  CL_ACCOUNT,
  CL_COMPANY,
  CL_CURRENCY,
  CL_OTHER_COMPANY,
  CL_PARTY,
  clId,
  historyPage,
  jsonResponse,
  listPage,
  refusal,
  succeeded,
} from './creditLimitFixtures'

// SENTETIK transport evidence (NOT_ENV): the company only in X-Company-Id, replay headers agree with the body, an
// unknown outcome is never shown as an answer.

const KEY = '0123456789abcdef0123456789abcdef'

const setCommand: CreditLimitCommand = {
  operation: 'set',
  body: { partyId: CL_PARTY, companyAccountId: CL_ACCOUNT, currencyId: CL_CURRENCY, amount: '150000.50', expectedRevision: '0' },
}

interface Seen {
  readonly url: string
  readonly method: string
  readonly headers: Record<string, string>
  readonly body: string | null
}

function stubFetch(answer: () => Response | Promise<Response>): Seen[] {
  const seen: Seen[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    seen.push({
      url: String(input),
      method: init?.method ?? 'GET',
      headers: { ...(init?.headers as Record<string, string> | undefined) },
      body: typeof init?.body === 'string' ? init.body : null,
    })
    return answer()
  }))
  return seen
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('credit limit reads', () => {
  it('reads the list with the verified company header and decodes it', async () => {
    const seen = stubFetch(() => jsonResponse(listPage([account()])))
    const outcome = await fetchCreditLimits(CL_COMPANY)
    expect(outcome.type).toBe('ok-single')
    expect(seen).toHaveLength(1)
    expect(seen[0].method).toBe('GET')
    expect(seen[0].url).toMatch(/\/finance\/credit-limits$/)
    expect(seen[0].headers['X-Company-Id']).toBe(CL_COMPANY)
  })

  it('reads one account and its history by account id', async () => {
    const seen = stubFetch(() => jsonResponse(account()))
    expect((await fetchCreditLimitAccount(CL_COMPANY, CL_ACCOUNT)).type).toBe('ok-single')
    expect(seen[0].url).toMatch(new RegExp(`/finance/credit-limits/accounts/${CL_ACCOUNT}$`))

    const history = stubFetch(() => jsonResponse(historyPage([approvedSet()])))
    expect((await fetchCreditLimitHistory(CL_COMPANY, CL_ACCOUNT)).type).toBe('ok-single')
    expect(history[0].url).toMatch(new RegExp(`/finance/credit-limits/accounts/${CL_ACCOUNT}/history$`))
  })

  it('turns a 403 into an http outcome and an answer of another company into malformed', async () => {
    stubFetch(() => new Response(null, { status: 403 }))
    expect(await fetchCreditLimits(CL_COMPANY)).toEqual({ type: 'http', status: 403 })
    stubFetch(() => jsonResponse(listPage([], CL_OTHER_COMPANY)))
    expect(await fetchCreditLimits(CL_COMPANY)).toEqual({ type: 'malformed' })
  })

  it('does not call the server for an unbound company or account id', async () => {
    const seen = stubFetch(() => jsonResponse(account()))
    expect(await fetchCreditLimits('not-a-company')).toEqual({ type: 'malformed' })
    expect(await fetchCreditLimitAccount(CL_COMPANY, 'not-an-account')).toEqual({ type: 'malformed' })
    expect(seen).toHaveLength(0)
  })
})

describe('credit limit command routes', () => {
  it('maps every operation to its route', () => {
    const proposal = clId(2)
    expect(creditLimitCommandUrl(setCommand)).toMatch(/\/finance\/credit-limits$/)
    expect(creditLimitCommandUrl({ operation: 'remove', body: { partyId: CL_PARTY, companyAccountId: CL_ACCOUNT, expectedRevision: '1' } }))
      .toMatch(/\/finance\/credit-limits\/removals$/)
    expect(creditLimitCommandUrl({
      operation: 'propose',
      body: { partyId: CL_PARTY, companyAccountId: CL_ACCOUNT, kind: 'Remove', currencyId: null, amount: null, expectedRevision: '1' },
    })).toMatch(/\/finance\/credit-limits\/proposals$/)
    expect(creditLimitCommandUrl({ operation: 'approve', proposalId: proposal, body: { expectedVersion: '1' } }))
      .toMatch(new RegExp(`/finance/credit-limits/proposals/${proposal}/approval$`))
    expect(creditLimitCommandUrl({ operation: 'reject', proposalId: proposal, body: { expectedVersion: '1', reason: 'SENTETIK' } }))
      .toMatch(new RegExp(`/finance/credit-limits/proposals/${proposal}/rejection$`))
  })
})

describe('credit limit commands', () => {
  it('sends the key and the company in headers, never the company in the body', async () => {
    const seen = stubFetch(() => jsonResponse(succeeded(approvedSet())))
    const reply = await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)
    expect(reply.kind).toBe('answered')
    expect(seen[0].method).toBe('POST')
    expect(seen[0].headers['Idempotency-Key']).toBe(KEY)
    expect(seen[0].headers['X-Company-Id']).toBe(CL_COMPANY)
    expect(JSON.parse(seen[0].body ?? '{}')).toEqual(setCommand.body)
    expect(seen[0].body).not.toContain(CL_COMPANY)
  })

  it('accepts a replay only when its headers agree with the body', async () => {
    const replay = succeeded(approvedSet(), { isReplay: true, originalCorrelationId: 'corr-first' })
    stubFetch(() => jsonResponse(replay, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-first' }))
    expect((await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).kind).toBe('answered')

    stubFetch(() => jsonResponse(replay, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-other' }))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })

    stubFetch(() => jsonResponse(replay))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })

    stubFetch(() => jsonResponse(succeeded(approvedSet()), 200, { 'Idempotency-Replayed': 'true' }))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
  })

  it('requires the status to match the outcome', async () => {
    stubFetch(() => jsonResponse(refusal('ValidationFailed', [{ field: 'amount', code: 'credit_limit_amount_invalid' }]), 200))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })

    stubFetch(() => jsonResponse(refusal('ValidationFailed', [{ field: 'amount', code: 'credit_limit_amount_invalid' }]), 422))
    expect((await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).kind).toBe('answered')

    stubFetch(() => jsonResponse(refusal('VersionConflict', [{ field: 'expectedRevision', code: 'version_conflict' }]), 409))
    expect((await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).kind).toBe('answered')

    stubFetch(() => jsonResponse(refusal('ValidationFailed'), 409))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })

    stubFetch(() => jsonResponse(succeeded(approvedSet()), 422))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
  })

  it('treats a success for another company or an unparsable body as unknown', async () => {
    stubFetch(() => jsonResponse(succeeded(approvedSet({ companyId: CL_OTHER_COMPANY }))))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
    stubFetch(() => new Response('not json', { status: 200, headers: { 'Content-Type': 'application/json' } }))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
  })

  it('maps 5xx, a thrown fetch and an unexpected status to unknown, and 4xx platform problems to refused', async () => {
    stubFetch(() => new Response(null, { status: 503 }))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'service-unavailable' })

    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK network') }))
    const reply = await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)
    expect(reply.kind).toBe('unknown')

    stubFetch(() => new Response(null, { status: 302 }))
    expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'unexpected-status' })

    for (const status of [400, 401, 403, 404, 415]) {
      stubFetch(() => new Response(null, { status }))
      expect(await sendCreditLimitCommand(CL_COMPANY, setCommand, KEY)).toEqual({ kind: 'refused', status })
    }
  })

  it('does not send for an unbound company', async () => {
    const seen = stubFetch(() => jsonResponse(succeeded(approvedSet())))
    expect(await sendCreditLimitCommand('', setCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
    expect(seen).toHaveLength(0)
  })
})
