/** Synthetic wire/attempt sources only. Real SQL and browser acceptance are NOT_RUN. */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { initialShellState } from '../shell/shellState'
import { beginTransferDepartureAttempt, decodeTransferDepartureCandidates, decodeTransferDepartureFacts, fetchTransferDepartureCandidates,
  fetchTransferDepartureFacts, finishTransferDepartureAttempt, leaveTransferDepartureAttempt, recordTransferDeparture,
  resetTransferDepartureAttemptsForTests, transferDepartureAttempt, transferDepartureBinding, transferDepartureFrozen,
  transferDepartureKilograms, TRANSFER_DEPARTURE_SOURCE as SOURCE, type RecordTransferDeparture, type TransferDepartureCandidate } from './transferDepartureApi'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const TRANSFER = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const OTHER = '44444444-4444-4444-8444-444444444444'
const AT = '2026-11-17T10:10:00+00:00'
const candidate = (): TransferDepartureCandidate => ({ transferId: TRANSFER, version: '9', status: 'InTransit', sourceLocationId: LOCATION,
  sourceLocationCode: 'DEP-01', sourceLocationName: 'SENTETIK kaynak', destinationLocationId: OTHER,
  destinationLocationCode: 'DEP-02', destinationLocationName: 'SENTETIK hedef', dispatchedAtUtc: '2026-11-17T10:00:00+00:00' })
const choices = () => ({ companyId: COMPANY, items: [candidate()], windowFull: false })
const facts = () => ({ companyId: COMPANY, transferId: TRANSFER, sourceLocationId: LOCATION, association: 1, version: 1,
  exitWeighingAtUtc: null as string | null, vehicleExitAtUtc: null as string | null, noFurtherExit: false, officialDocument: null as unknown, sourceVersion: `${SOURCE}/1` })
const proof = () => ({ companyId: COMPANY, transferId: TRANSFER, officialDocumentId: OTHER, version: 4, isLinked: true,
  linkedAtUtc: '2026-11-17T09:00:00+00:00', isSentToGib: false, sentAtUtc: null, cancelConfirmationVersion: null })
const payload = (): RecordTransferDeparture => ({ expectedVersion: '1', kind: 1, occurredAtUtc: AT, reason: 'SENTETIK tartı kaydı',
  weighing: { direction: 1, firstKg: '12.125', secondKg: '15.250' } })
const snapshot = () => beginTransferDepartureAttempt('synthetic-binding', COMPANY, TRANSFER, LOCATION, payload(), () => 'synthetic-key')!
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
const mutation = (isReplay = false) => ({ facts: { ...facts(), version: 2, sourceVersion: `${SOURCE}/2`, exitWeighingAtUtc: AT }, isReplay, originalCorrelationId: 'synthetic-original' })
afterEach(() => { resetTransferDepartureAttemptsForTests(); vi.unstubAllGlobals() })

describe('transfer departure company-bound owner decoders', () => {
  it('retains the genuine source names and opaque Inventory version without reconstructing a plan', () => {
    expect(decodeTransferDepartureCandidates(choices(), COMPANY)).toEqual(choices())
    expect(decodeTransferDepartureCandidates({ ...choices(), windowFull: true, items: [] }, COMPANY)).not.toBeNull()
  })
  it.each([
    { ...choices(), companyId: OTHER }, { ...choices(), items: [candidate(), candidate()] },
    { ...choices(), items: [{ ...candidate(), transferId: 'not-a-guid' }] },
    { ...choices(), items: [{ ...candidate(), sourceLocationId: '00000000-0000-0000-0000-000000000000' }] },
    { ...choices(), items: [{ ...candidate(), version: 9 }] }, { ...choices(), items: [{ ...candidate(), version: '09' }] },
    { ...choices(), items: [{ ...candidate(), version: '9223372036854775808' }] },
    { ...choices(), items: [{ ...candidate(), dispatchedAtUtc: '2026-02-31T10:00:00Z' }] },
    { ...choices(), items: [{ ...candidate(), status: 'Unknown' }] }, { ...choices(), items: [{ ...candidate(), status: ['InTransit'] }] },
    { ...choices(), items: [{ ...candidate(), price: '10' }] },
    { ...choices(), items: Array.from({ length: 51 }, () => candidate()) },
  ])('refuses a malformed, duplicate, foreign or extended candidate frame %#', (raw) => {
    expect(decodeTransferDepartureCandidates(raw, COMPANY)).toBeNull()
  })
  it('keeps legacy unknown distinct from explicit not-linked and a permanent no-exit fact', () => {
    const legacy = { ...facts(), association: 0, version: null, sourceVersion: `${SOURCE}/legacy-unknown` }
    expect(decodeTransferDepartureFacts(legacy, COMPANY, TRANSFER, LOCATION)).toEqual(legacy)
    expect(decodeTransferDepartureFacts(facts(), COMPANY, TRANSFER, LOCATION)).toEqual(facts())
    expect(decodeTransferDepartureFacts({ ...facts(), noFurtherExit: true }, COMPANY, TRANSFER, LOCATION)?.noFurtherExit).toBe(true)
  })
  it.each([
    { ...facts(), companyId: OTHER }, { ...facts(), transferId: OTHER }, { ...facts(), sourceLocationId: OTHER },
    { ...facts(), version: '1' }, { ...facts(), version: 0 }, { ...facts(), version: 9007199254740992 },
    { ...facts(), sourceVersion: `${SOURCE}/2` }, { ...facts(), association: 0 }, { ...facts(), association: 3 },
    { ...facts(), association: 2 }, { ...facts(), officialDocument: proof() },
    { ...facts(), noFurtherExit: true, vehicleExitAtUtc: AT }, { ...facts(), exitWeighingAtUtc: '2026-11-17T10:00:00+03:00' },
  ])('rejects contradictory departure facts instead of enabling a write %#', (raw) => {
    expect(decodeTransferDepartureFacts(raw, COMPANY, TRANSFER, LOCATION)).toBeNull()
  })
  it('accepts an actual linked owner proof, including a verified GIB timestamp and cancellation revision', () => {
    expect(decodeTransferDepartureFacts({ ...facts(), association: 2, officialDocument: proof() }, COMPANY, TRANSFER, LOCATION)?.officialDocument?.isSentToGib).toBe(false)
    const sent = { ...proof(), isSentToGib: true, sentAtUtc: AT, cancelConfirmationVersion: 2 }
    expect(decodeTransferDepartureFacts({ ...facts(), association: 2, officialDocument: sent }, COMPANY, TRANSFER, LOCATION)?.officialDocument).toEqual(sent)
  })
  it.each([
    { ...proof(), companyId: OTHER }, { ...proof(), transferId: OTHER }, { ...proof(), officialDocumentId: 'wrong' },
    { ...proof(), isSentToGib: null }, { ...proof(), isSentToGib: true }, { ...proof(), sentAtUtc: AT },
    { ...proof(), isSentToGib: true, sentAtUtc: '2026-11-17T08:59:00Z' }, { ...proof(), isLinked: false },
    { ...proof(), version: 0 }, { ...proof(), cancelConfirmationVersion: 0 },
  ])('refuses missing or mismatched official source fields without claiming not-linked %#', (officialDocument) => {
    expect(decodeTransferDepartureFacts({ ...facts(), association: 2, officialDocument }, COMPANY, TRANSFER, LOCATION)).toBeNull()
  })
  it.each(['0', '0.000', '-1', '1,2', '1.0001', '1000000000000000', '01', '1e3'])('rejects invalid weighing quantity text %s without calculating net', (value) => {
    expect(transferDepartureKilograms(value)).toBe(false)
  })
  it.each(['0.001', '12', '12.250', '999999999999999.999'])('preserves valid weighing text %s', (value) => {
    expect(transferDepartureKilograms(value)).toBe(true)
  })
})

describe('transfer departure transport and immutable success proof', () => {
  it('reads candidates without query or an extra operational grant and binds detail to its actual source location', async () => {
    const fetcher = vi.fn(async (url: RequestInfo | URL) => json(String(url).endsWith('/candidates') ? choices() : facts()))
    vi.stubGlobal('fetch', fetcher)
    expect((await fetchTransferDepartureCandidates(COMPANY)).type).toBe('ok-single')
    expect((await fetchTransferDepartureFacts(COMPANY, candidate())).type).toBe('ok-single')
    expect(fetcher.mock.calls.map((call) => String(call[0]))).toEqual([
      '/api/v1/logistics/transfer-departures/candidates', `/api/v1/logistics/transfer-departures/${TRANSFER}`,
    ])
  })
  it('rejects a read that settles after its company-bound request is aborted', async () => {
    const controller = new AbortController()
    vi.stubGlobal('fetch', vi.fn(async () => { controller.abort(); return json(choices()) }))
    expect(await fetchTransferDepartureCandidates(COMPANY, controller.signal)).toEqual({ type: 'fence-stale' })
  })
  it('posts the exact version, UTC occurrence and raw scale text then verifies the recorded event', async () => {
    const fetcher = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => json(mutation()))
    vi.stubGlobal('fetch', fetcher)
    const command = snapshot()
    expect(await recordTransferDeparture(command)).toMatchObject({ type: 'applied', replayed: false })
    expect(fetcher.mock.calls[0][0]).toBe(`/api/v1/logistics/transfer-departures/${TRANSFER}`)
    const init = fetcher.mock.calls[0][1]!
    expect(JSON.parse(String(init.body))).toEqual(payload())
    expect(new Headers(init.headers).get('X-Company-Id')).toBe(COMPANY)
    expect(new Headers(init.headers).get('Idempotency-Key')).toBe('synthetic-key')
    expect(JSON.parse(String(init.body))).not.toHaveProperty('netKg')
  })
  it('accepts a body/header coherent replay and equivalent UTC serialization of the same event', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ ...mutation(true), facts: { ...mutation().facts, exitWeighingAtUtc: '2026-11-17T10:10:00.000Z' } }, 200,
      { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' })))
    expect(await recordTransferDeparture(snapshot())).toMatchObject({ type: 'applied', replayed: true })
  })
  it.each([
    { ...mutation(), facts: { ...mutation().facts, companyId: OTHER } },
    { ...mutation(), facts: { ...mutation().facts, sourceLocationId: OTHER } },
    { ...mutation(), facts: { ...mutation().facts, transferId: OTHER } },
    { ...mutation(), facts: { ...mutation().facts, version: 3, sourceVersion: `${SOURCE}/3` } },
    { ...mutation(), facts: { ...mutation().facts, exitWeighingAtUtc: null, vehicleExitAtUtc: AT } },
    { ...mutation(), facts: { ...mutation().facts, exitWeighingAtUtc: '2026-11-17T10:11:00Z' } },
    { ...mutation(), facts: { ...mutation().facts, association: 2 } }, { ...mutation(), isReplay: true },
    { ...mutation(), originalCorrelationId: null }, {},
  ])('keeps malformed success as unknown so the original key survives %#', async (raw) => {
    vi.stubGlobal('fetch', vi.fn(async () => json(raw)))
    expect(await recordTransferDeparture(snapshot())).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it.each([201, 204, 302, 500, 503])('does not treat unexpected or unavailable status %i as successful', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => status === 204 ? new Response(null, { status }) : json({}, status)))
    expect((await recordTransferDeparture(snapshot())).type).toBe('unknown')
  })
  it.each([401, 403, 404, 409, 422])('keeps definite fresh refusal status %i separate from unavailable', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'logistics_transfer_departure_version_conflict' }, status)))
    expect(await recordTransferDeparture(snapshot())).toMatchObject({ type: 'refused', status })
  })
  it('retains offline and opaque network outcomes as unknown', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK network') }))
    const command = snapshot()
    vi.stubGlobal('navigator', { onLine: false })
    expect(await recordTransferDeparture(command)).toEqual({ type: 'unknown', reason: 'offline' })
    vi.stubGlobal('navigator', { onLine: true })
    expect(await recordTransferDeparture(command)).toEqual({ type: 'unknown', reason: 'network-error' })
  })
})

describe('transfer departure retained same-key attempt', () => {
  it('deep-freezes the two weighing values, retains them on route departure, and blocks another transfer', () => {
    const request = payload(); const first = beginTransferDepartureAttempt('bound', COMPANY, TRANSFER, LOCATION, request, () => 'same-key')!
    const external = request.weighing as { direction: number; firstKg: string; secondKg: string }; external.firstKg = '99'
    leaveTransferDepartureAttempt('bound')
    expect(first.payload.weighing?.firstKg).toBe('12.125')
    expect(beginTransferDepartureAttempt('bound', COMPANY, OTHER, LOCATION, payload())).toBeNull()
    const retry = beginTransferDepartureAttempt('bound', COMPANY, TRANSFER, LOCATION, payload(), () => 'must-not-be-used')!
    expect(retry).toBe(first)
    finishTransferDepartureAttempt(retry, { type: 'refused', status: 409, code: 'logistics_transfer_departure_version_conflict' })
    expect(transferDepartureFrozen(transferDepartureAttempt('bound'))).toBe(true)
    beginTransferDepartureAttempt('bound', COMPANY, TRANSFER, LOCATION, payload())
    const recorded = decodeTransferDepartureFacts(mutation().facts, COMPANY, TRANSFER, LOCATION)!
    finishTransferDepartureAttempt(first, { type: 'applied', facts: recorded, replayed: true })
    expect(transferDepartureFrozen(transferDepartureAttempt('bound'))).toBe(false)
  })
  it('does not issue a second concurrent send and permits a new request after a fresh refusal', () => {
    const first = snapshot()
    expect(beginTransferDepartureAttempt('synthetic-binding', COMPANY, TRANSFER, LOCATION, payload())).toBeNull()
    finishTransferDepartureAttempt(first, { type: 'refused', status: 422, code: 'logistics_transfer_departure_request_invalid' })
    expect(transferDepartureFrozen(transferDepartureAttempt('synthetic-binding'))).toBe(false)
    expect(beginTransferDepartureAttempt('synthetic-binding', COMPANY, TRANSFER, LOCATION, payload(), () => 'new-key')?.key).toBe('new-key')
  })
  it('separates real actor, company and session while the same route re-entry retains the attempt', () => {
    const context = { userId: 'SENTETIK-actor', scopeMode: 'single' as const, companyId: COMPANY, capabilityGeneration: 'g1', capabilities: ['logistics.transfer-departures.record'] }
    const state = { ...initialShellState, sessionEpoch: 1, activeContext: context }
    const binding = transferDepartureBinding(state)
    expect(transferDepartureBinding({ ...state, routeEntryGeneration: 99 })).toBe(binding)
    expect(transferDepartureBinding({ ...state, sessionEpoch: 2 })).not.toBe(binding)
    expect(transferDepartureBinding({ ...state, activeContext: { ...context, userId: 'SENTETIK-other' } })).not.toBe(binding)
    expect(transferDepartureBinding({ ...state, activeContext: { ...context, companyId: OTHER } })).not.toBe(binding)
  })
})
