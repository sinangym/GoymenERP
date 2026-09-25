/**
 * HTTP adapter for the one published Finance read. Stubbed `fetch`; not a live API run.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { DRAFT_CONTEXT_EXAMPLE, EXAMPLE_COMPANY } from './contractExamples'
import { API_BASE, draftContextUrl, fetchDraftContext } from './http'

const REQUEST = DRAFT_CONTEXT_EXAMPLE.request

afterEach(() => {
  vi.unstubAllGlobals()
})

function stubFetch(response: Response | Error) {
  const fetchMock = vi.fn(async () => {
    if (response instanceof Error) throw response
    return response
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('draftContextUrl', () => {
  it('uses the published path with all four required query fields and nothing else', () => {
    const url = new URL(draftContextUrl(REQUEST), 'http://localhost')
    expect(url.pathname).toBe(`${API_BASE}/finance/manual-receipts/draft-context`)
    expect(Object.fromEntries(url.searchParams)).toEqual({
      treasuryAccountId: REQUEST.treasuryAccountId,
      partyId: REQUEST.partyId,
      companyAccountId: REQUEST.companyAccountId,
      occurredOn: '2026-09-13',
    })
  })
})

describe('fetchDraftContext', () => {
  it('sends the shell company in X-Company-Id and returns the 200 body', async () => {
    const fetchMock = stubFetch(new Response(JSON.stringify(DRAFT_CONTEXT_EXAMPLE.response), { status: 200 }))
    const reply = await fetchDraftContext(EXAMPLE_COMPANY, REQUEST, new AbortController().signal)
    expect(reply).toEqual({ kind: 'ok', body: DRAFT_CONTEXT_EXAMPLE.response })
    const init = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect((init[1].headers as Record<string, string>)['X-Company-Id']).toBe(EXAMPLE_COMPANY)
    expect(init[1].method ?? 'GET').toBe('GET')
    expect(init[1].body).toBeUndefined()
  })

  it.each([400, 401, 403, 404, 500, 503])('classifies HTTP %s by status without trusting its body', async (status) => {
    stubFetch(new Response(JSON.stringify({ code: 'anything' }), { status }))
    expect(await fetchDraftContext(EXAMPLE_COMPANY, REQUEST, new AbortController().signal)).toEqual({ kind: 'status', status })
  })

  it('a 2xx other than 200 is not a context', async () => {
    stubFetch(new Response(null, { status: 204 }))
    expect(await fetchDraftContext(EXAMPLE_COMPANY, REQUEST, new AbortController().signal)).toEqual({ kind: 'status', status: 204 })
  })

  it('an unparseable 200 body reaches the decoder as undefined, not as a context', async () => {
    stubFetch(new Response('not json', { status: 200 }))
    expect(await fetchDraftContext(EXAMPLE_COMPANY, REQUEST, new AbortController().signal)).toEqual({ kind: 'ok', body: undefined })
  })

  it('a network failure is no reply', async () => {
    stubFetch(new TypeError('Failed to fetch'))
    expect(await fetchDraftContext(EXAMPLE_COMPANY, REQUEST, new AbortController().signal)).toEqual({ kind: 'no-reply' })
  })
})
