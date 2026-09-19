import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchAccountOptions, fetchContactInformationOptions, fetchCurrencyOptions } from './api'
import {
  NO_LIST_ROUTE,
  useHttpContactInformation,
  useHttpCurrencyOptions,
  useHttpReferenceOptions,
} from './referenceSource'

const COMPANY = '99999999-9999-9999-9999-999999999999'
const PARTY = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const ACCOUNT = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const CURRENCY = '33333333-3333-3333-3333-333333333333'
const PROFILE = 'cccccccc-1111-4111-8111-000000000401'
const ADDRESS = 'aaaaaaaa-1111-4111-8111-000000000401'
const CONTACT = 'bbbbbbbb-1111-4111-8111-000000000401'
const DATE = '2026-09-13'

const ACCOUNTS_BODY = {
  companyId: COMPANY,
  accounts: [{ partyId: PARTY, companyAccountId: ACCOUNT, label: 'ACME · Ana hesap', partyVersion: '3', accountVersion: '2' }],
  nextCursor: null,
}
const currencyRow = (effectiveOn: string) => ({
  companyId: COMPANY,
  effectiveOn,
  currencyId: CURRENCY,
  code: 'TRY',
  label: 'TRY',
  profileId: PROFILE,
  profileVersion: '3',
})
const CURRENCIES_BODY = { companyId: COMPANY, effectiveOn: DATE, currencies: [currencyRow(DATE)] }
const CONTACT_BODY = {
  companyId: COMPANY,
  partyId: PARTY,
  companyAccountId: ACCOUNT,
  accountVersion: '2',
  addresses: [
    {
      id: ADDRESS,
      version: '4',
      purpose: 'delivery',
      text: 'Sanayi Cad. 1, Kocaeli, TR',
      countryCode: 'TR',
      line1: 'Sanayi Cad. 1',
      line2: null,
      city: 'Kocaeli',
      district: null,
      postalCode: null,
    },
  ],
  contacts: [{ id: CONTACT, version: '2', displayName: 'Ayşe Yükleme', jobTitle: null, purpose: 'delivery' }],
}

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

/** One answer per route, chosen by the request path; the real app reads several lists at once. */
function respondBy(routes: Record<string, { status: number; body: unknown }>) {
  const calls: { url: string; init: RequestInit }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init: RequestInit) => {
      calls.push({ url, init })
      const path = new URL(url, 'http://x').pathname
      const answer = routes[path]
      if (answer === undefined) throw new TypeError(`no route for ${url}`)
      return new Response(JSON.stringify(answer.body), { status: answer.status, headers: { 'Content-Type': 'application/json' } })
    }),
  )
  return calls
}

afterEach(() => vi.unstubAllGlobals())

describe('fetchAccountOptions — the real accounts list, strictly decoded', () => {
  it('reads the first page under the company header and keeps only the option fields', async () => {
    const calls = respond(200, ACCOUNTS_BODY)
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

describe('SALES-A2a: fetchCurrencyOptions — the real currencies list for a business date', () => {
  it('reads the list for the date under the company header and keeps the option fields', async () => {
    const calls = respond(200, CURRENCIES_BODY)
    const outcome = await fetchCurrencyOptions(DATE, COMPANY)
    expect(outcome).toEqual({ type: 'ready', data: [{ currencyId: CURRENCY, code: 'TRY', profileId: PROFILE, profileVersion: '3' }] })
    expect(calls[0].url).toBe('/api/v1/sales/references/currencies?effectiveOn=2026-09-13')
    expect((calls[0].init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY)
  })

  it('an empty list is "none offered", not a failure; another date or company is malformed', async () => {
    respond(200, { companyId: COMPANY, effectiveOn: DATE, currencies: [] })
    expect(await fetchCurrencyOptions(DATE, COMPANY)).toEqual({ type: 'ready', data: [] })
    respond(200, CURRENCIES_BODY)
    expect((await fetchCurrencyOptions('2026-09-14', COMPANY)).type).toBe('malformed')
    expect((await fetchCurrencyOptions(DATE, '11111111-1111-1111-1111-111111111111')).type).toBe('malformed')
  })

  it('carries the real refusal status and code, and a lost request as network', async () => {
    respond(400, { code: 'validation_failed', status: 400, title: 'x' })
    expect(await fetchCurrencyOptions(DATE, COMPANY)).toEqual({ type: 'http', status: 400, code: 'validation_failed' })
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('lost'))))
    expect((await fetchCurrencyOptions(DATE, COMPANY)).type).toBe('network')
  })
})

describe('SALES-A2a: fetchContactInformationOptions — the chosen account’s addresses and contacts', () => {
  it('reads the account’s lists under the company header and keeps the option fields', async () => {
    const calls = respond(200, CONTACT_BODY)
    const outcome = await fetchContactInformationOptions(PARTY, ACCOUNT, COMPANY)
    expect(outcome).toEqual({
      type: 'ready',
      data: {
        partyId: PARTY,
        companyAccountId: ACCOUNT,
        accountVersion: '2',
        addresses: [{ id: ADDRESS, version: '4', purpose: 'delivery', text: 'Sanayi Cad. 1, Kocaeli, TR' }],
        contacts: [{ id: CONTACT, version: '2', displayName: 'Ayşe Yükleme', jobTitle: null, purpose: 'delivery' }],
      },
    })
    expect(calls[0].url).toBe(`/api/v1/sales/references/contact-information?partyId=${PARTY}&companyAccountId=${ACCOUNT}`)
    expect((calls[0].init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY)
  })

  it('an answer about another account is malformed; a code-less 404 keeps its status', async () => {
    respond(200, CONTACT_BODY)
    expect((await fetchContactInformationOptions(PARTY, '11111111-1111-1111-1111-111111111111', COMPANY)).type).toBe('malformed')
    respond(404, { status: 404, title: 'Not Found' })
    expect(await fetchContactInformationOptions(PARTY, ACCOUNT, COMPANY)).toEqual({ type: 'http', status: 404, code: null })
  })
})

describe('SALES-A2a: useHttpCurrencyOptions — one read per company and date', () => {
  it('is idle without a real date, loading while pending, then the owner’s list; another date is read again', async () => {
    const calls = respond(200, CURRENCIES_BODY)
    const { result, rerender } = renderHook(({ date }) => useHttpCurrencyOptions(COMPANY, date), { initialProps: { date: '' } })
    expect(result.current).toEqual({ status: 'idle' })
    expect(calls).toHaveLength(0)
    rerender({ date: DATE })
    expect(result.current).toEqual({ status: 'loading' })
    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.status === 'ready' && result.current.data.map((c) => c.code)).toEqual(['TRY'])
    rerender({ date: '2026-09-14' })
    expect(result.current).toEqual({ status: 'loading' })
    await waitFor(() => expect(calls).toHaveLength(2))
    expect(calls[1].url).toBe('/api/v1/sales/references/currencies?effectiveOn=2026-09-14')
  })

  it('a failing read is unavailable with its reason, and its retry reads again', async () => {
    const calls = respond(503, { code: 'service_unavailable', status: 503, title: 'x' })
    const { result } = renderHook(() => useHttpCurrencyOptions(COMPANY, DATE))
    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.status === 'unavailable' && result.current.reason).toBe('http-503:service_unavailable')
    act(() => {
      if (result.current.status === 'unavailable') result.current.retry()
    })
    await waitFor(() => expect(calls).toHaveLength(2))
  })
})

describe('SALES-A2a: useHttpContactInformation — one read per chosen account', () => {
  it('is idle until an account is chosen, then the owner’s lists', async () => {
    const calls = respond(200, CONTACT_BODY)
    const { result, rerender } = renderHook(({ account }) => useHttpContactInformation(COMPANY, PARTY, account), {
      initialProps: { account: '' },
    })
    expect(result.current).toEqual({ status: 'idle' })
    expect(calls).toHaveLength(0)
    rerender({ account: ACCOUNT })
    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current.status === 'ready' && result.current.data.addresses[0].text).toBe('Sanayi Cad. 1, Kocaeli, TR')
  })

  it('a lost request is unavailable as network', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('lost'))))
    const { result } = renderHook(() => useHttpContactInformation(COMPANY, PARTY, ACCOUNT))
    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.status === 'unavailable' && result.current.reason).toBe('network')
  })
})

describe('useHttpReferenceOptions — the real source', () => {
  it('is unavailable until the answer arrives, then names what the owners do not offer and offers the header', async () => {
    respond(200, ACCOUNTS_BODY)
    const { result } = renderHook(() => useHttpReferenceOptions(COMPANY, ''))
    expect(result.current).toEqual({ status: 'unavailable' })
    await waitFor(() => expect(result.current.status === 'unavailable' && result.current.missing).toEqual([...NO_LIST_ROUTE]))
    expect(result.current.status === 'unavailable' && result.current.header?.accounts).toEqual([
      { partyId: PARTY, companyAccountId: ACCOUNT, label: 'ACME · Ana hesap' },
    ])
    // No business date yet: the currencies list is not asked for (idle), and it is not "missing".
    expect(result.current.status === 'unavailable' && result.current.header?.currencies).toEqual({ status: 'idle' })
  })

  it('reads the currencies for the business date beside the accounts, and names a failing currencies read', async () => {
    respondBy({
      '/api/v1/sales/references/accounts': { status: 200, body: ACCOUNTS_BODY },
      '/api/v1/sales/references/currencies': { status: 200, body: CURRENCIES_BODY },
    })
    const { result } = renderHook(() => useHttpReferenceOptions(COMPANY, DATE))
    await waitFor(() => expect(result.current.status === 'unavailable' && result.current.header?.currencies.status).toBe('ready'))
    expect(result.current.status === 'unavailable' && result.current.missing).toEqual([...NO_LIST_ROUTE])
    vi.unstubAllGlobals()

    respondBy({
      '/api/v1/sales/references/accounts': { status: 200, body: ACCOUNTS_BODY },
      '/api/v1/sales/references/currencies': { status: 503, body: { code: 'service_unavailable', status: 503, title: 'x' } },
    })
    const failing = renderHook(() => useHttpReferenceOptions(COMPANY, DATE))
    await waitFor(() =>
      expect(failing.result.current.status === 'unavailable' && failing.result.current.missing).toEqual([
        { source: 'currencies', reason: 'http-503:service_unavailable' },
        ...NO_LIST_ROUTE,
      ]),
    )
    // The header is still offered: the accounts are real, and the currency select says why it is empty.
    expect(failing.result.current.status === 'unavailable' && failing.result.current.header?.accounts).toHaveLength(1)
  })

  it('names the accounts answer when the real route refuses, and offers no header', async () => {
    respond(503, { code: 'service_unavailable', status: 503, title: 'x' })
    const { result } = renderHook(() => useHttpReferenceOptions(COMPANY, ''))
    await waitFor(() =>
      expect(result.current.status === 'unavailable' && result.current.missing).toEqual([
        { source: 'accounts', reason: 'http-503:service_unavailable' },
        ...NO_LIST_ROUTE,
      ]),
    )
    expect(result.current.status === 'unavailable' && result.current.header).toBeUndefined()
  })

  it('offers nothing for an empty company and never guesses products', () => {
    const calls = respond(200, { companyId: COMPANY, accounts: [], nextCursor: null })
    const { result } = renderHook(() => useHttpReferenceOptions('', ''))
    expect(result.current).toEqual({ status: 'unavailable' })
    expect(calls).toHaveLength(0)
    expect(NO_LIST_ROUTE.map((m) => m.source)).toEqual(['products'])
  })
})
