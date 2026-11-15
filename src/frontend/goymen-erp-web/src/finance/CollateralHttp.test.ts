import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  collateralCommandUrl,
  fetchCollateralAccount,
  fetchCollateralHistory,
  fetchCollateralTypeCandidates,
  sendCollateralCommand,
  type CollateralCommand,
} from './CollateralHttp'
import {
  account,
  changed,
  CO_ACCOUNT,
  CO_COLLATERAL,
  CO_COMPANY,
  CO_CURRENCY,
  CO_OTHER_ACCOUNT,
  CO_OTHER_COMPANY,
  CO_PARTY,
  CO_TYPE,
  historyPage,
  jsonResponse,
  recorded,
  refusal,
  released,
  succeeded,
  typeCandidates,
} from './collateralFixtures'

// SENTETIK transport evidence (NOT_ENV): the company only in X-Company-Id, replay headers agree with the body, a
// success answers the command that was sent, an unknown outcome is never shown as an answer.

const KEY = '0123456789abcdef0123456789abcdef'

const recordCommand: CollateralCommand = {
  operation: 'record',
  body: { partyId: CO_PARTY, companyAccountId: CO_ACCOUNT, collateralTypeId: CO_TYPE, currencyId: CO_CURRENCY,
    amount: '250000.50', validFrom: '2026-11-01', validUntil: null, documentReference: 'SENTETIK-BELGE-1' },
}
const changeCommand: CollateralCommand = {
  operation: 'change',
  collateralId: CO_COLLATERAL,
  body: { expectedRevision: '1', collateralTypeId: CO_TYPE, currencyId: CO_CURRENCY, amount: '300000',
    validFrom: '2026-11-01', validUntil: null, documentReference: 'SENTETIK-BELGE-1' },
}
const releaseCommand: CollateralCommand = {
  operation: 'release', collateralId: CO_COLLATERAL, body: { expectedRevision: '1', reason: 'SENTETIK çözüm gerekçesi' },
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

describe('collateral reads', () => {
  it('reads one account, its history and the type candidates with the verified company header', async () => {
    const seen = stubFetch(() => jsonResponse(account()))
    expect((await fetchCollateralAccount(CO_COMPANY, CO_ACCOUNT)).type).toBe('ok-single')
    expect(seen[0].method).toBe('GET')
    expect(seen[0].url).toMatch(new RegExp(`/finance/collaterals/accounts/${CO_ACCOUNT}$`))
    expect(seen[0].headers['X-Company-Id']).toBe(CO_COMPANY)

    const history = stubFetch(() => jsonResponse(historyPage([recorded()])))
    expect((await fetchCollateralHistory(CO_COMPANY, CO_ACCOUNT)).type).toBe('ok-single')
    expect(history[0].url).toMatch(new RegExp(`/finance/collaterals/accounts/${CO_ACCOUNT}/history$`))

    const types = stubFetch(() => jsonResponse(typeCandidates()))
    expect((await fetchCollateralTypeCandidates(CO_COMPANY)).type).toBe('ok-single')
    expect(types[0].url).toMatch(/\/finance\/collaterals\/type-candidates$/)
  })

  it('keeps a 404 history as an http outcome and an answer for another account as malformed', async () => {
    stubFetch(() => new Response(null, { status: 404 }))
    expect(await fetchCollateralHistory(CO_COMPANY, CO_ACCOUNT)).toEqual({ type: 'http', status: 404 })
    stubFetch(() => jsonResponse(account({ companyAccountId: CO_OTHER_ACCOUNT })))
    expect(await fetchCollateralAccount(CO_COMPANY, CO_ACCOUNT)).toEqual({ type: 'malformed' })
    stubFetch(() => jsonResponse(typeCandidates(undefined, CO_OTHER_COMPANY)))
    expect(await fetchCollateralTypeCandidates(CO_COMPANY)).toEqual({ type: 'malformed' })
  })

  it('does not call the server for an unbound company or account id', async () => {
    const seen = stubFetch(() => jsonResponse(account()))
    expect(await fetchCollateralAccount(CO_COMPANY, 'not-an-account')).toEqual({ type: 'malformed' })
    expect(await fetchCollateralHistory('not-a-company', CO_ACCOUNT)).toEqual({ type: 'malformed' })
    expect(await fetchCollateralTypeCandidates('')).toEqual({ type: 'malformed' })
    expect(seen).toHaveLength(0)
  })
})

describe('collateral command routes', () => {
  it('maps every operation to its route', () => {
    expect(collateralCommandUrl(recordCommand)).toMatch(/\/finance\/collaterals$/)
    expect(collateralCommandUrl(changeCommand)).toMatch(new RegExp(`/finance/collaterals/${CO_COLLATERAL}/changes$`))
    expect(collateralCommandUrl(releaseCommand)).toMatch(new RegExp(`/finance/collaterals/${CO_COLLATERAL}/release$`))
  })
})

describe('collateral commands', () => {
  it('sends the key and the company in headers, never the company in the body', async () => {
    const seen = stubFetch(() => jsonResponse(succeeded(recorded())))
    const reply = await sendCollateralCommand(CO_COMPANY, recordCommand, KEY)
    expect(reply.kind).toBe('answered')
    expect(seen[0].method).toBe('POST')
    expect(seen[0].headers['Idempotency-Key']).toBe(KEY)
    expect(seen[0].headers['X-Company-Id']).toBe(CO_COMPANY)
    expect(JSON.parse(seen[0].body ?? '{}')).toEqual(recordCommand.body)
    expect(seen[0].body).not.toContain(CO_COMPANY)
  })

  it('accepts a success only when it answers the command that was sent', async () => {
    stubFetch(() => jsonResponse(succeeded(changed())))
    expect((await sendCollateralCommand(CO_COMPANY, changeCommand, KEY)).kind).toBe('answered')
    stubFetch(() => jsonResponse(succeeded(released())))
    expect((await sendCollateralCommand(CO_COMPANY, releaseCommand, KEY)).kind).toBe('answered')

    // A change answered by a release, a record on another account or another collateral is not this command's answer.
    stubFetch(() => jsonResponse(succeeded(released())))
    expect(await sendCollateralCommand(CO_COMPANY, changeCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
    stubFetch(() => jsonResponse(succeeded(recorded({ companyAccountId: CO_OTHER_ACCOUNT }))))
    expect(await sendCollateralCommand(CO_COMPANY, recordCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
    stubFetch(() => jsonResponse(succeeded(changed({ collateralId: 'cccccccc-0000-4000-8000-000000000002' }))))
    expect(await sendCollateralCommand(CO_COMPANY, changeCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
  })

  it('accepts a replay only when its headers agree with the body', async () => {
    const replay = succeeded(recorded(), { isReplay: true, originalCorrelationId: 'corr-first' })
    stubFetch(() => jsonResponse(replay, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-first' }))
    expect((await sendCollateralCommand(CO_COMPANY, recordCommand, KEY)).kind).toBe('answered')

    stubFetch(() => jsonResponse(replay, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-other' }))
    expect(await sendCollateralCommand(CO_COMPANY, recordCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })

    stubFetch(() => jsonResponse(replay))
    expect(await sendCollateralCommand(CO_COMPANY, recordCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
  })

  it('requires the status to match the outcome', async () => {
    const instrument = [{ field: 'collateralTypeId', code: 'collateral_type_is_instrument' }]
    stubFetch(() => jsonResponse(refusal('ValidationFailed', instrument), 422))
    expect((await sendCollateralCommand(CO_COMPANY, recordCommand, KEY)).kind).toBe('answered')
    stubFetch(() => jsonResponse(refusal('ValidationFailed', instrument), 200))
    expect(await sendCollateralCommand(CO_COMPANY, recordCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
    stubFetch(() => jsonResponse(refusal('VersionConflict', [{ field: 'request', code: 'concurrent_change' }]), 409))
    expect((await sendCollateralCommand(CO_COMPANY, changeCommand, KEY)).kind).toBe('answered')
    stubFetch(() => jsonResponse(refusal('ValidationFailed'), 409))
    expect(await sendCollateralCommand(CO_COMPANY, changeCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
  })

  it('maps 5xx, a thrown fetch and an unexpected status to unknown, and 4xx platform problems to refused', async () => {
    stubFetch(() => new Response(null, { status: 503 }))
    expect(await sendCollateralCommand(CO_COMPANY, recordCommand, KEY))
      .toEqual({ kind: 'unknown', reason: 'service-unavailable' })
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK network') }))
    expect((await sendCollateralCommand(CO_COMPANY, recordCommand, KEY)).kind).toBe('unknown')
    stubFetch(() => new Response(null, { status: 302 }))
    expect(await sendCollateralCommand(CO_COMPANY, recordCommand, KEY))
      .toEqual({ kind: 'unknown', reason: 'unexpected-status' })
    for (const status of [400, 401, 403, 404, 415]) {
      stubFetch(() => new Response(null, { status }))
      expect(await sendCollateralCommand(CO_COMPANY, releaseCommand, KEY)).toEqual({ kind: 'refused', status })
    }
  })

  it('does not send for an unbound company', async () => {
    const seen = stubFetch(() => jsonResponse(succeeded(recorded())))
    expect(await sendCollateralCommand('', recordCommand, KEY)).toEqual({ kind: 'unknown', reason: 'undecodable' })
    expect(seen).toHaveLength(0)
  })
})
