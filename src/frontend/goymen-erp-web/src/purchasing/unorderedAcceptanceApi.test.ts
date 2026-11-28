import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchUnorderedAcceptance, prepareUnorderedAcceptanceAttempt, recallUnorderedAcceptanceAttempt, recordUnorderedAcceptance,
  type UnorderedAcceptanceScope } from './unorderedAcceptanceApi'
import type { RecordUnorderedAcceptance } from './unorderedAcceptanceContract'

afterEach(() => vi.unstubAllGlobals())
const id = (n: number) => `${n.toString().padStart(8, '0')}-abcd-4abc-8abc-abcdefabcdef`
let sequence = 0
function rig() {
  const scope: UnorderedAcceptanceScope = { sessionBinding: `SENTETIK-server-session-${++sequence}`, actorId: 'SENTETIK-actor', companyId: id(10), contextRevision: 1 }
  let live: UnorderedAcceptanceScope | null = { ...scope }
  const request: RecordUnorderedAcceptance = { occurrence: { acceptanceId: id(1), acceptanceLineId: id(2), expectedVersion: '0', supplierPartyId: id(3),
    companyAccountId: id(4), expectedPartyVersion: 'party-2', expectedAccountVersion: 'account-3', subject: { kind: 'Goods', productId: id(5), productVersion: '7' },
    unitId: id(6), unitProfile: { profileId: id(7), version: 8 }, acceptedQuantity: '1.25', occurredAtUtc: '2026-11-20T01:02:03.0000001Z',
    evidence: { documentId: id(8), documentVersion: 9, sha256: 'a'.repeat(64), sourceLineId: 'satır-1' }, controlEvidence: [] },
  businessDate: '2026-10-09', reason: '  ham\nnot  ' }
  const source = () => ({ companyId: scope.companyId, acceptanceId: id(1), acceptanceLineId: id(2), version: 1, supplierPartyId: id(3), companyAccountId: id(4),
    subject: request.occurrence.subject, unitId: id(6), acceptedQuantity: '1.25', uninvoicedQuantity: '1.25', occurredAtUtc: request.occurrence.occurredAtUtc,
    evidence: request.occurrence.evidence, controlEvidence: request.occurrence.controlEvidence, declaration: { businessDate: request.businessDate, reason: request.reason } })
  const answer = (replay = false) => ({ record: { source: source(), partyVersion: 'party-2', accountVersion: 'account-3', unitProfile: request.occurrence.unitProfile,
    recordedBy: scope.actorId, recordedAtUtc: '2026-11-28T04:05:06.1234567Z' }, isReplay: replay, originalCorrelationId: 'SENTETIK-original' })
  return { scope, request, source, answer, read: () => live, set: (next: UnorderedAcceptanceScope | null) => { live = next } }
}
const json = (body: unknown, status = 201, replay = false) => new Response(JSON.stringify(body), { status,
  headers: replay ? { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' } : {} })

describe('NOPO native transport and original attempt, controlled fetch only (not HTTP acceptance)', () => {
  it('dispatches exact V2 JSON, company and original key, binds the real actor and clears only an applied attempt', async () => {
    const r = rig(), fetcher = vi.fn(async () => json(r.answer()))
    vi.stubGlobal('fetch', fetcher)
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-key')!
    expect((await recordUnorderedAcceptance(attempt, r.read)).type).toBe('applied')
    const [path, init] = fetcher.mock.calls[0] as unknown as [string, RequestInit]
    expect(path).toBe('/api/v1/purchasing/unordered-acceptances')
    expect(init.method).toBe('POST'); expect(init.body).toBe(JSON.stringify(r.request)); expect(init.cache).toBe('no-store')
    const headers = new Headers(init.headers)
    expect(headers.get('X-Company-Id')).toBe(r.scope.companyId); expect(headers.get('Idempotency-Key')).toBe('SENTETIK-key')
    expect(recallUnorderedAcceptanceAttempt(r.scope)).toBeNull()
  })
  it.each([401, 403, 500, 503])('retains dispatched HTTP %s as unknown and reuses the identical body/key on original replay', async status => {
    const r = rig(), fetcher = vi.fn().mockResolvedValueOnce(json({}, status)).mockResolvedValueOnce(json(r.answer(true), 201, true))
    vi.stubGlobal('fetch', fetcher)
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-original-key')!
    const first = await recordUnorderedAcceptance(attempt, r.read)
    expect(first.type).toBe('unknown'); expect(recallUnorderedAcceptanceAttempt(r.scope)).toBe(attempt)
    expect(prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-new-key')).toBeNull()
    expect(prepareUnorderedAcceptanceAttempt(r.scope, { ...r.request, reason: '' }, attempt.key)).toBeNull()
    expect(prepareUnorderedAcceptanceAttempt(r.scope, r.request, attempt.key)).toBe(attempt)
    const replay = await recordUnorderedAcceptance(attempt, r.read)
    expect(replay.type).toBe('applied')
    if (replay.type === 'applied') { expect(replay.response.isReplay).toBe(true); expect(replay.response.originalCorrelationId).toBe('SENTETIK-original') }
    expect(fetcher.mock.calls[1][1].body).toBe(fetcher.mock.calls[0][1].body)
    expect(new Headers(fetcher.mock.calls[1][1].headers).get('Idempotency-Key')).toBe('SENTETIK-original-key')
  })
  it('freezes the whole request before awaited work; caller evidence/Reason changes cannot rebind original JSON', async () => {
    const r = rig(), controls = [r.request.occurrence.evidence], request = { ...r.request, occurrence: { ...r.request.occurrence, controlEvidence: controls } }
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, request, 'SENTETIK-frozen')!, body = attempt.body
    controls.push({ ...controls[0], documentId: id(99) }); request.reason = 'changed'; request.occurrence.evidence = { ...request.occurrence.evidence, documentVersion: 10 }
    vi.stubGlobal('fetch', vi.fn(async () => json({}, 503)))
    await recordUnorderedAcceptance(attempt, r.read)
    expect(recallUnorderedAcceptanceAttempt(r.scope)?.body).toBe(body)
    expect(attempt.request.reason).toBe('  ham\nnot  '); expect(attempt.request.occurrence.controlEvidence).toHaveLength(1)
    expect(attempt.request.occurrence.evidence.documentVersion).toBe(9)
    expect(Object.isFrozen(attempt.request.occurrence.controlEvidence)).toBe(true)
  })
  it.each(['session', 'actor', 'company', 'logout'])('never recovers or dispatches an old original attempt under a new %s scope', async change => {
    const r = rig(), fetcher = vi.fn(async () => json({}, 503)); vi.stubGlobal('fetch', fetcher)
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-scoped')!
    await recordUnorderedAcceptance(attempt, r.read)
    const other = change === 'logout' ? null : { ...r.scope, ...(change === 'session' ? { sessionBinding: 'SENTETIK-other' }
      : change === 'actor' ? { actorId: 'SENTETIK-other' } : { companyId: id(99) }) }
    r.set(other)
    expect(recallUnorderedAcceptanceAttempt(other)).toBeNull()
    expect(await recordUnorderedAcceptance(attempt, r.read)).toEqual({ type: 'fence-stale' }); expect(fetcher).toHaveBeenCalledTimes(1)
    expect(recallUnorderedAcceptanceAttempt({ ...r.scope })).toBe(attempt) // Same real server session remount only.
  })
  it('does not invent a session or dispatch under a missing binding or initial cancellation', async () => {
    const r = rig(), fetcher = vi.fn(); vi.stubGlobal('fetch', fetcher)
    expect(prepareUnorderedAcceptanceAttempt({ ...r.scope, sessionBinding: '' }, r.request, 'key')).toBeNull()
    expect(recallUnorderedAcceptanceAttempt({ ...r.scope, sessionBinding: '' })).toBeNull()
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-cancelled')!, controller = new AbortController(); controller.abort()
    expect(await recordUnorderedAcceptance(attempt, r.read, controller.signal)).toEqual({ type: 'fence-stale' })
    expect(fetcher).not.toHaveBeenCalled()
  })
  it.each(['company', 'session', 'cancel', 'permission'])('suppresses private late POST facts on %s loss, retaining the original unknown attempt', async change => {
    const r = rig(), controller = new AbortController(), answer = r.answer()
    vi.stubGlobal('fetch', vi.fn(async () => {
      if (change === 'cancel') controller.abort()
      else r.set(change === 'permission' ? null : { ...r.scope, ...(change === 'company' ? { companyId: id(99) } : { sessionBinding: 'SENTETIK-new' }) })
      return json(answer)
    }))
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-late')!
    expect(await recordUnorderedAcceptance(attempt, r.read, controller.signal)).toEqual({ type: 'unknown', reason: 'fence-stale' })
    expect(recallUnorderedAcceptanceAttempt(r.scope)).toBe(attempt)
  })
  it('rechecks the scope after asynchronous response JSON, not only after response headers', async () => {
    const r = rig(), response = json(r.answer())
    vi.spyOn(response, 'json').mockImplementation(async () => { r.set(null); return r.answer() })
    vi.stubGlobal('fetch', vi.fn(async () => response))
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-json-late')!
    expect(await recordUnorderedAcceptance(attempt, r.read)).toEqual({ type: 'unknown', reason: 'fence-stale' })
    expect(recallUnorderedAcceptanceAttempt(r.scope)).toBe(attempt)
  })
  it('does not revive an A→B→A response, but permits a fresh authorized same-key retry after a context refresh', async () => {
    const r = rig(), fetcher = vi.fn().mockImplementationOnce(async () => {
      r.set({ ...r.scope, contextRevision: 3 }); return json(r.answer())
    }).mockResolvedValueOnce(json(r.answer(true), 201, true))
    vi.stubGlobal('fetch', fetcher)
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-context-refresh')!
    expect(await recordUnorderedAcceptance(attempt, r.read)).toEqual({ type: 'unknown', reason: 'fence-stale' })
    expect(recallUnorderedAcceptanceAttempt({ ...r.scope, contextRevision: 3 })).toBe(attempt)
    expect((await recordUnorderedAcceptance(attempt, r.read)).type).toBe('applied')
    expect(fetcher.mock.calls[1][1].body).toBe(fetcher.mock.calls[0][1].body)
  })
  it('does not dispatch a second concurrent attempt or a competing new key while the original fetch is unresolved', async () => {
    const r = rig(); let release: ((response: Response) => void) | undefined
    const fetcher = vi.fn(() => new Promise<Response>(resolve => { release = resolve })); vi.stubGlobal('fetch', fetcher)
    const first = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-first')!
    const competing = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-competing')!
    const sending = recordUnorderedAcceptance(first, r.read)
    expect((await recordUnorderedAcceptance(first, r.read)).type).toBe('unknown')
    expect(await recordUnorderedAcceptance(competing, r.read)).toEqual({ type: 'fence-stale' })
    expect(fetcher).toHaveBeenCalledTimes(1); expect(release).toBeDefined()
    release!(json(r.answer()))
    expect((await sending).type).toBe('applied')
  })
  it.each([200, 202, 204])('keeps unexpected successful status %s unknown, without accepting a forged receipt', async status => {
    const r = rig(); vi.stubGlobal('fetch', vi.fn(async () => status === 204 ? new Response(null, { status }) : json(r.answer(), status)))
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-status')!
    expect((await recordUnorderedAcceptance(attempt, r.read)).type).toBe('unknown'); expect(recallUnorderedAcceptanceAttempt(r.scope)).toBe(attempt)
  })
  it('retains network failure, malformed genuine binding and later refusal as unknown until original-key reconciliation', async () => {
    const r = rig(), fetcher = vi.fn().mockRejectedValueOnce(new Error('SENTETIK-network'))
      .mockResolvedValueOnce(json({ ...r.answer(), record: { ...r.answer().record, recordedBy: 'SENTETIK-other' } })).mockResolvedValueOnce(json({ code: 'forbidden' }, 409))
    vi.stubGlobal('fetch', fetcher)
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-unknown')!
    expect((await recordUnorderedAcceptance(attempt, r.read)).type).toBe('unknown')
    expect((await recordUnorderedAcceptance(attempt, r.read)).type).toBe('unknown')
    expect((await recordUnorderedAcceptance(attempt, r.read)).type).toBe('unknown'); expect(recallUnorderedAcceptanceAttempt(r.scope)).toBe(attempt)
  })
  it('keeps a definite first validation refusal distinct from an unknown previously dispatched command', async () => {
    const r = rig(); vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'validation_failed' }, 400)))
    const attempt = prepareUnorderedAcceptanceAttempt(r.scope, r.request, 'SENTETIK-refused')!
    expect(await recordUnorderedAcceptance(attempt, r.read)).toEqual({ type: 'failed', status: 400, code: 'validation_failed' })
    expect(recallUnorderedAcceptanceAttempt(r.scope)).toBeNull()
  })
  it('reads only the exact company/acceptance/line without query, with genuine v1 absent declaration', async () => {
    const r = rig(), { declaration: _declaration, ...legacy } = r.source()
    const fetcher = vi.fn(async () => json(legacy, 200)); vi.stubGlobal('fetch', fetcher)
    const outcome = await fetchUnorderedAcceptance(r.scope, id(1), id(2), r.read)
    expect(outcome).toEqual({ type: 'ok-single', data: legacy })
    const [path, init] = fetcher.mock.calls[0] as unknown as [string, RequestInit]
    expect(path).toBe(`/api/v1/purchasing/unordered-acceptances/${id(1)}/lines/${id(2)}`); expect(init.cache).toBe('no-store')
    expect(new Headers(init.headers).get('X-Company-Id')).toBe(r.scope.companyId)
  })
  it.each(['company', 'actor', 'session', 'cancel'])('hides a late GET after %s changes', async change => {
    const r = rig(), controller = new AbortController(), source = r.source()
    vi.stubGlobal('fetch', vi.fn(async () => {
      if (change === 'cancel') controller.abort()
      else r.set({ ...r.scope, ...(change === 'company' ? { companyId: id(99) } : change === 'actor' ? { actorId: 'other' } : { sessionBinding: 'other' }) })
      return json(source, 200)
    }))
    expect(await fetchUnorderedAcceptance(r.scope, id(1), id(2), r.read, controller.signal)).toEqual({ type: 'fence-stale' })
  })
  it('refuses foreign/malformed GET facts and initial stale scope without conflating a read with recording', async () => {
    const r = rig(), fetcher = vi.fn(async () => json({ ...r.source(), companyId: id(99) }, 200)); vi.stubGlobal('fetch', fetcher)
    expect(await fetchUnorderedAcceptance(r.scope, id(1), id(2), r.read)).toEqual({ type: 'malformed' })
    r.set(null)
    expect(await fetchUnorderedAcceptance(r.scope, id(1), id(2), r.read)).toEqual({ type: 'fence-stale' }); expect(fetcher).toHaveBeenCalledTimes(1)
  })
  it('checks GET JSON completion and context revision before releasing source facts', async () => {
    const r = rig(), response = json(r.source(), 200)
    vi.spyOn(response, 'json').mockImplementation(async () => { r.set({ ...r.scope, contextRevision: 3 }); return r.source() })
    vi.stubGlobal('fetch', vi.fn(async () => response))
    expect(await fetchUnorderedAcceptance(r.scope, id(1), id(2), r.read)).toEqual({ type: 'fence-stale' })
  })
})
