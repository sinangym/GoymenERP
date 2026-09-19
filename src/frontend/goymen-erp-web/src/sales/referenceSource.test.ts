import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchAccountOptions } from './api'
import { NO_LIST_ROUTE, useHttpReferenceOptions } from './referenceSource'

const COMPANY = '99999999-9999-9999-9999-999999999999'
const PARTY = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const ACCOUNT = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'

function respond(status: number, body: unknown) {
  const calls: { url: string; init: RequestInit }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init: RequestInit) => {
      calls.push({ url, init })
      return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
    }),
  )
  return calls
}

afterEach(() => vi.unstubAllGlobals())

describe('fetchAccountOptions — the real accounts list, strictly decoded', () => {
  it('reads the first page under the company header and keeps only the option fields', async () => {
    const calls = respond(200, {
      companyId: COMPANY,
      accounts: [{ partyId: PARTY, companyAccountId: ACCOUNT, label: 'ACME · Ana hesap', partyVersion: '3', accountVersion: '2' }],
      nextCursor: null,
    })
    const outcome = await fetchAccountOptions(COMPANY)
    expect(outcome).toEqual({ type: 'ready', accounts: [{ partyId: PARTY, companyAccountId: ACCOUNT, label: 'ACME · Ana hesap' }] })
    expect(calls[0].url).toBe('/api/v1/sales/references/accounts?pageSize=50')
    expect((calls[0].init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY)
  })

  it.each([
    ['another company', { companyId: '11111111-1111-1111-1111-111111111111', accounts: [], nextCursor: null }],
    ['a nil party id', { companyId: COMPANY, accounts: [{ partyId: '00000000-0000-0000-0000-000000000000', companyAccountId: ACCOUNT, label: 'x' }], nextCursor: null }],
    ['a blank label', { companyId: COMPANY, accounts: [{ partyId: PARTY, companyAccountId: ACCOUNT, label: ' ' }], nextCursor: null }],
    ['no accounts array', { companyId: COMPANY, nextCursor: null }],
  ])('%s is malformed, never an option', async (_, body) => {
    respond(200, body)
    expect((await fetchAccountOptions(COMPANY)).type).toBe('malformed')
  })

  it('carries the real refusal status and code', async () => {
    respond(503, { code: 'service_unavailable', status: 503, title: 'x' })
    expect(await fetchAccountOptions(COMPANY)).toEqual({ type: 'http', status: 503, code: 'service_unavailable' })
    respond(403, { code: 'forbidden', status: 403, title: 'x' })
    expect(await fetchAccountOptions(COMPANY)).toEqual({ type: 'http', status: 403, code: 'forbidden' })
  })
})

describe('useHttpReferenceOptions — the real source', () => {
  it('is unavailable until the answer arrives, then names what the owners do not offer', async () => {
    respond(200, { companyId: COMPANY, accounts: [{ partyId: PARTY, companyAccountId: ACCOUNT, label: 'ACME' }], nextCursor: null })
    const { result } = renderHook(() => useHttpReferenceOptions(COMPANY))
    expect(result.current).toEqual({ status: 'unavailable' })
    await waitFor(() => expect(result.current.status === 'unavailable' && result.current.missing).toEqual([...NO_LIST_ROUTE]))
  })

  it('names the accounts answer when the real route refuses', async () => {
    respond(503, { code: 'service_unavailable', status: 503, title: 'x' })
    const { result } = renderHook(() => useHttpReferenceOptions(COMPANY))
    await waitFor(() =>
      expect(result.current.status === 'unavailable' && result.current.missing).toEqual([
        { source: 'accounts', reason: 'http-503:service_unavailable' },
        ...NO_LIST_ROUTE,
      ]),
    )
  })

  it('offers nothing for an empty company and never guesses currencies or products', () => {
    const calls = respond(200, { companyId: COMPANY, accounts: [], nextCursor: null })
    const { result } = renderHook(() => useHttpReferenceOptions(''))
    expect(result.current).toEqual({ status: 'unavailable' })
    expect(calls).toHaveLength(0)
    expect(NO_LIST_ROUTE.map((m) => m.source)).toEqual(['currencies', 'products'])
  })
})
