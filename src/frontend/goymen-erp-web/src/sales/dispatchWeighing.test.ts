import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeDispatchWeighing, fetchDispatchWeighing } from './dispatchWeighing'
import { classifyOutcome } from '../shell/uiState'

const companyId = '99999999-9999-9999-9999-999999999999'
const dispatchNoteId = 'd7d7d7d7-0000-4000-8000-000000000001'
const recordId = 'a7a7a7a7-0000-4000-8000-000000000001'
const recorded = {
  dispatchNoteId, companyId, state: 'Recorded', recordId, theoreticalKg: '100.001', scaleKg: '99.999',
  differenceKg: '-0.002', totalExcessKg: '0', bandLevel: 'None', decision: 'NoExcess',
  recordState: 'Recorded', saleMayProceed: true, invoiceWait: null, version: 1,
}
const missing = {
  dispatchNoteId, companyId, state: 'NotRecorded', recordId: null, theoreticalKg: null, scaleKg: null,
  differenceKg: null, totalExcessKg: null, bandLevel: null, decision: null, recordState: null,
  saleMayProceed: false, invoiceWait: 'NotRecorded', version: null,
}
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('dispatch weighing owner contract', () => {
  it('preserves exact kilograms and distinguishes absence from zero excess', () => {
    expect(decodeDispatchWeighing(recorded)).toEqual(recorded)
    expect(decodeDispatchWeighing(missing)).toEqual(missing)
    expect(decodeDispatchWeighing({ ...missing, totalExcessKg: '0' })).toBeNull()
    expect(decodeDispatchWeighing({ ...missing, saleMayProceed: true })).toBeNull()
  })

  it('refuses malformed quantities, versions, decisions and contradictory approval facts', () => {
    for (const change of [
      { theoreticalKg: 100.001 }, { scaleKg: '1e2' }, { totalExcessKg: '-1' }, { differenceKg: '0.0001' },
      { version: 1.2 }, { version: '1' }, { decision: 'Guess' }, { state: 'Unavailable' },
      { recordState: 'PendingApproval', saleMayProceed: true, invoiceWait: 'PendingApproval' },
      { recordState: 'PendingApproval', saleMayProceed: false, invoiceWait: null },
      { hiddenAmount: '15' },
    ]) expect(decodeDispatchWeighing({ ...recorded, ...change })).toBeNull()
    const { invoiceWait: _removed, ...incomplete } = recorded
    expect(decodeDispatchWeighing(incomplete)).toBeNull()
  })

  it('sends a cancellable no-store company read and refuses another company or dispatch', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(recorded), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    const signal = new AbortController().signal
    expect(await fetchDispatchWeighing(dispatchNoteId, companyId, signal)).toEqual({ type: 'ok-single', data: recorded })
    const [url, init] = fetch.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(`/api/v1/sales/dispatches/${dispatchNoteId}/weighing-difference`)
    expect(new Headers(init.headers).get('X-Company-Id')).toBe(companyId)
    expect(init.cache).toBe('no-store')
    expect(init.signal).toBe(signal)
    for (const change of [{ companyId: recordId }, { dispatchNoteId: recordId }]) {
      fetch.mockResolvedValueOnce(new Response(JSON.stringify({ ...recorded, ...change }), { status: 200 }))
      expect(await fetchDispatchWeighing(dispatchNoteId, companyId)).toEqual({ type: 'malformed' })
    }
  })

  it.each([[403, 'forbidden'], [404, 'not-found'], [409, 'conflict'], [503, 'service-unavailable']] as const)(
    'preserves HTTP %s as %s rather than a missing weighing record', async (status, kind) => {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{}', { status })))
      expect(classifyOutcome(await fetchDispatchWeighing(dispatchNoteId, companyId)).kind).toBe(kind)
    })

  it('distinguishes invalid JSON, no network and an aborted obsolete read', async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response('{', { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchDispatchWeighing(dispatchNoteId, companyId)).toEqual({ type: 'malformed' })
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    fetch.mockRejectedValueOnce(new TypeError('network'))
    expect(await fetchDispatchWeighing(dispatchNoteId, companyId)).toEqual({ type: 'offline' })
    const obsolete = new AbortController()
    obsolete.abort()
    fetch.mockRejectedValueOnce(new DOMException('obsolete', 'AbortError'))
    expect(await fetchDispatchWeighing(dispatchNoteId, companyId, obsolete.signal)).toEqual({ type: 'fence-stale' })
  })
})
