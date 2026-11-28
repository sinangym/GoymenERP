import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchMergeImpact } from './mergeApi'
const selection = { partyId: '11111111-1111-1111-1111-111111111111', mergedPartyId: '22222222-2222-2222-2222-222222222222', companyId: 'aaaaaaaa-0000-0000-0000-000000000001', expectedVersion: '3', mergedPartyExpectedVersion: '5' }
afterEach(() => vi.unstubAllGlobals())
describe('read-only merge impact transport', () => {
  it('does not start a read for an already cancelled context', async () => {
    const fetch = vi.fn(); vi.stubGlobal('fetch', fetch)
    const abort = new AbortController(); abort.abort()
    expect(await fetchMergeImpact(selection, abort.signal)).toEqual({ type: 'aborted' }); expect(fetch).not.toHaveBeenCalled()
  })
  it('does not release a response whose body completed after cancellation', async () => {
    let release: (v: unknown) => void = () => { throw new Error('body was not requested') }
    vi.stubGlobal('fetch', vi.fn(async () => ({ status: 200, json: () => new Promise(resolve => { release = resolve }) })))
    const abort = new AbortController(); const answer = fetchMergeImpact(selection, abort.signal)
    await Promise.resolve(); abort.abort(); release({ companyId: selection.companyId })
    expect(await answer).toEqual({ type: 'aborted' })
  })
  it.each([204,403,409,503])('does not turn HTTP %s into empty successful impact', async status => {
    vi.stubGlobal('fetch', vi.fn(async () => status === 204 ? new Response(null, { status }) : new Response('{}', { status })))
    expect(await fetchMergeImpact(selection)).toEqual({ type: 'http', status, code: null })
  })
})
