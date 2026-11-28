import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchMergeImpact, fetchMergePurposeImpact, approveMerge, approveMergePurpose, MERGE_REPLAYABLE } from './mergeApi'
import { COUNTERPARTY_CONTRACT_HEADER, COUNTERPARTY_CONTRACT_VERSION, recordContractRead, resetContractReadiness } from './clientContract'
const selection = { partyId: '11111111-1111-1111-1111-111111111111', mergedPartyId: '22222222-2222-2222-2222-222222222222', companyId: 'aaaaaaaa-0000-0000-0000-000000000001', expectedVersion: '3', mergedPartyExpectedVersion: '5' }
afterEach(() => { vi.unstubAllGlobals(); resetContractReadiness() })
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

describe('seen-impact purpose transport', () => {
  const digest = 'A'.repeat(64)
  const impact = () => ({ companyId: selection.companyId,
    survivor: { partyId: selection.partyId, code: 'C-000001', legalName: 'SENTETIK survivor', version: '3', isActive: true, mergedIntoPartyId: null },
    merged: { partyId: selection.mergedPartyId, code: 'C-000002', legalName: 'SENTETIK duplicate', version: '5', isActive: true, mergedIntoPartyId: null },
    holdingAccounts: [
      { companyId: selection.companyId, accountId: selection.partyId, partyId: selection.partyId, version: '1', isActive: true },
      { companyId: selection.companyId, accountId: selection.mergedPartyId, partyId: selection.mergedPartyId, version: '1', isActive: true },
    ], changes: { partyId: selection.mergedPartyId, mergedIntoPartyId: selection.partyId, isActive: false },
  })
  it('uses the purpose read route, exact selection and two-field wrapper without spending a key', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response(JSON.stringify({ impact: impact(), seenImpactDigest: digest }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchMergePurposeImpact(selection)).toEqual({ type: 'ok', data: { impact: impact(), seenImpactDigest: digest } })
    const url = fetch.mock.calls[0][0]
    const init = fetch.mock.calls[0][1]!
    expect(url).toBe(`/api/v1/counterparties/${selection.partyId}/merge/purpose-preview`)
    expect(init.method).toBe('POST'); expect(init.cache).toBe('no-store')
    expect(JSON.parse(init.body as string)).toEqual({ mergedPartyId: selection.mergedPartyId, expectedVersion: '3', mergedPartyExpectedVersion: '5' })
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBeUndefined()
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(selection.companyId)
  })
  it.each([false, true])('keeps legacy approve header-free and purpose approve header-bound (purpose=%s)', async purpose => {
    const contract = { origin: '/api/v1', namespace: 'SENTETIK-purpose' }
    recordContractRead(contract, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
    const context = { companyId: selection.companyId, idempotencyKey: 'SENTETIK-key', contract }
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response(null, { status: 204,
      headers: { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION } }))
    vi.stubGlobal('fetch', fetch)
    const approval = { mergedPartyId: selection.mergedPartyId, mergedPartyExpectedVersion: '5', reason: 'SENTETIK' }
    const answer = purpose ? await approveMergePurpose(context, selection.partyId, '3', approval, digest)
      : await approveMerge(context, selection.partyId, '3', approval)
    expect(answer.type).toBe('applied')
    const init = fetch.mock.calls[0][1]!
    expect(JSON.parse(init.body as string)).toEqual({ ...approval, expectedVersion: '3' })
    expect((init.headers as Record<string, string>)['X-Counterparty-Merge-Seen-Impact']).toBe(purpose ? digest : undefined)
  })
  it('never classifies the nonstored changed-impact refusal as a stored native replay', () => {
    expect(MERGE_REPLAYABLE.get(409)?.has('counterparty_merge_impact_changed')).toBe(false)
  })
  it('suppresses a purpose body completed after its context was cancelled', async () => {
    let release: (v: unknown) => void = () => { throw new Error('not started') }
    vi.stubGlobal('fetch', vi.fn(async () => ({ status: 200, json: () => new Promise(resolve => { release = resolve }) })))
    const abort = new AbortController(); const pending = fetchMergePurposeImpact(selection, abort.signal)
    await Promise.resolve(); abort.abort(); release({ impact: impact(), seenImpactDigest: digest })
    expect(await pending).toEqual({ type: 'aborted' })
  })
})
