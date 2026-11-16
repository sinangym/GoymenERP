import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  fetchOpeningBalanceCurrencyCandidates,
  fetchOpeningBalances,
  fetchOpeningPeriod,
  openingBalanceCommandUrl,
  sendOpeningBalanceCommand,
  type OpeningBalanceCommand,
} from './OpeningBalanceHttp'
import {
  approved,
  currencyCandidates,
  jsonResponse,
  listPage,
  OB_CASH,
  OB_COMPANY,
  OB_CURRENCY,
  OB_OTHER_COMPANY,
  obId,
  period,
  proposed,
  refusal,
  rejected,
  succeeded,
} from './openingBalanceFixtures'

// SENTETIK transport evidence (NOT_ENV): the company only in X-Company-Id, replay headers agree with the body, a
// success answers the command that was sent, an unknown outcome is never shown as an answer.

const KEY = '0123456789abcdef0123456789abcdef'

const proposeCommand: OpeningBalanceCommand = {
  operation: 'propose',
  body: { targetKind: 'CashAccount', targetId: OB_CASH, partyId: null, currencyId: OB_CURRENCY, direction: null,
    amount: '150000.50', asOfDate: '2026-11-01' },
}
const approveCommand: OpeningBalanceCommand = {
  operation: 'approve', openingBalanceId: obId(3), body: { expectedVersion: '1' },
}
const rejectCommand: OpeningBalanceCommand = {
  operation: 'reject', openingBalanceId: obId(4), body: { expectedVersion: '1', reason: 'SENTETIK red gerekçesi' },
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

describe('opening balance reads', () => {
  it('reads the period, the list and the currency candidates with the verified company header', async () => {
    const seen = stubFetch(() => jsonResponse(period()))
    expect((await fetchOpeningPeriod(OB_COMPANY)).type).toBe('ok-single')
    expect(seen[0].method).toBe('GET')
    expect(seen[0].url).toMatch(/\/finance\/opening-balances\/period$/)
    expect(seen[0].headers['X-Company-Id']).toBe(OB_COMPANY)

    const list = stubFetch(() => jsonResponse(listPage([proposed()])))
    expect((await fetchOpeningBalances(OB_COMPANY)).type).toBe('ok-single')
    expect(list[0].url).toMatch(/\/finance\/opening-balances$/)

    const currencies = stubFetch(() => jsonResponse(currencyCandidates()))
    expect((await fetchOpeningBalanceCurrencyCandidates(OB_COMPANY)).type).toBe('ok-single')
    expect(currencies[0].url).toMatch(/\/finance\/opening-balances\/currency-candidates$/)
  })

  it('keeps 404 and 503 as http outcomes and another company as malformed', async () => {
    stubFetch(() => new Response(null, { status: 404 }))
    expect(await fetchOpeningBalances(OB_COMPANY)).toEqual({ type: 'http', status: 404 })
    stubFetch(() => new Response(null, { status: 503 }))
    expect(await fetchOpeningBalances(OB_COMPANY)).toEqual({ type: 'http', status: 503 })
    stubFetch(() => jsonResponse(period('Open', '7', OB_OTHER_COMPANY)))
    expect(await fetchOpeningPeriod(OB_COMPANY)).toEqual({ type: 'malformed' })
  })

  it('does not call the server for an unbound company', async () => {
    const seen = stubFetch(() => jsonResponse(period()))
    expect(await fetchOpeningPeriod('not-a-company')).toEqual({ type: 'malformed' })
    expect(await fetchOpeningBalances('')).toEqual({ type: 'malformed' })
    expect(await fetchOpeningBalanceCurrencyCandidates('x')).toEqual({ type: 'malformed' })
    expect(seen).toHaveLength(0)
  })
})

describe('opening balance commands', () => {
  it('posts each command to its route with the key and the company only in the header', async () => {
    expect(openingBalanceCommandUrl(proposeCommand)).toMatch(/\/finance\/opening-balances$/)
    expect(openingBalanceCommandUrl(approveCommand)).toMatch(new RegExp(`/finance/opening-balances/${obId(3)}/approve$`))
    expect(openingBalanceCommandUrl(rejectCommand)).toMatch(new RegExp(`/finance/opening-balances/${obId(4)}/reject$`))

    const seen = stubFetch(() => jsonResponse(succeeded(proposed())))
    const reply = await sendOpeningBalanceCommand(OB_COMPANY, proposeCommand, KEY)
    expect(reply.kind).toBe('answered')
    expect(seen[0].method).toBe('POST')
    expect(seen[0].headers['Idempotency-Key']).toBe(KEY)
    expect(seen[0].headers['X-Company-Id']).toBe(OB_COMPANY)
    expect(JSON.parse(seen[0].body ?? 'null')).toEqual(proposeCommand.body)
    expect(seen[0].body).not.toContain(OB_COMPANY)
  })

  it('answers approve and reject only with the named opening in its decided status', async () => {
    stubFetch(() => jsonResponse(succeeded(approved())))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY)).kind).toBe('answered')
    stubFetch(() => jsonResponse(succeeded(rejected())))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, rejectCommand, KEY)).kind).toBe('answered')
    stubFetch(() => jsonResponse(succeeded(approved({ id: obId(9) }))))
    expect(await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY))
      .toEqual({ kind: 'unknown', reason: 'undecodable' })
    stubFetch(() => jsonResponse(succeeded(rejected({ id: obId(3) }))))
    expect(await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY))
      .toEqual({ kind: 'unknown', reason: 'undecodable' })
  })

  it('treats a success for another target or currency as unknown', async () => {
    stubFetch(() => jsonResponse(succeeded(proposed({ targetId: obId(8) }))))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, proposeCommand, KEY)).kind).toBe('unknown')
    stubFetch(() => jsonResponse(succeeded(proposed({ currencyId: obId(7) }))))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, proposeCommand, KEY)).kind).toBe('unknown')
  })

  it('requires replay headers to agree with the body', async () => {
    const replay = succeeded(proposed(), { isReplay: true, originalCorrelationId: 'corr-0' })
    stubFetch(() => jsonResponse(replay, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-0' }))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, proposeCommand, KEY)).kind).toBe('answered')
    stubFetch(() => jsonResponse(replay))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, proposeCommand, KEY)).kind).toBe('unknown')
    stubFetch(() => jsonResponse(succeeded(proposed()), 200, { 'Idempotency-Replayed': 'true' }))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, proposeCommand, KEY)).kind).toBe('unknown')
  })

  it('maps 422 and 409 only to their outcomes', async () => {
    stubFetch(() => jsonResponse(refusal('ValidationFailed', [{ field: '', code: 'opening_period_closed' }]), 422))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, proposeCommand, KEY)).kind).toBe('answered')
    stubFetch(() => jsonResponse(refusal('VersionConflict'), 409))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY)).kind).toBe('answered')
    stubFetch(() => jsonResponse(refusal('ValidationFailed'), 409))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY)).kind).toBe('unknown')
    stubFetch(() => jsonResponse(refusal('VersionConflict'), 200))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY)).kind).toBe('unknown')
  })

  it('keeps definite refusals definite and 5xx, network loss or an odd status unknown', async () => {
    for (const status of [400, 401, 403, 404, 415]) {
      stubFetch(() => new Response(null, { status }))
      expect(await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY)).toEqual({ kind: 'refused', status })
    }
    stubFetch(() => new Response(null, { status: 503 }))
    expect(await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY))
      .toEqual({ kind: 'unknown', reason: 'service-unavailable' })
    stubFetch(() => new Response(null, { status: 302 }))
    expect(await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY))
      .toEqual({ kind: 'unknown', reason: 'unexpected-status' })
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('network') }))
    expect((await sendOpeningBalanceCommand(OB_COMPANY, approveCommand, KEY)).kind).toBe('unknown')
  })
})
