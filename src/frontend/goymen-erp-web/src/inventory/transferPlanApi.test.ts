/** Synthetic owner-wire and retained-intent source tests. SQL/browser acceptance is NOT_RUN. */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { initialShellState } from '../shell/shellState'
import { beginTransferPlanAttempt, decodeTransferPlanDestinations, decodeTransferPlanLabels, differentTransferPlanTarget,
  fetchTransferPlanDestinations, fetchTransferPlanLabels, finishTransferPlanAttempt, leaveTransferPlanAttempt,
  resetTransferPlanAttemptsForTests, sameTransferPlanSource, sendTransferPlan, transferPlanAttempt, transferPlanBinding,
  transferPlanFrozen, type PlanTransferRequest, type TransferPlanDestination, type TransferPlanLabel, type TransferPlanSnapshot } from './transferPlanApi'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LOCATION = '22222222-2222-4222-8222-222222222222'
const DESTINATION = '33333333-3333-4333-8333-333333333333'
const LABEL = '44444444-4444-4444-8444-444444444444'
const LABEL2 = '55555555-5555-4555-8555-555555555555'
const PRODUCT = '66666666-6666-4666-8666-666666666666'
const PARTY = '77777777-7777-4777-8777-777777777777'
const SITE = '88888888-8888-4888-8888-888888888888'
const ACTOR = 'SENTETIK-transfer-actor'
const AT = '2026-11-17T09:00:00.000Z'
const label = (): TransferPlanLabel => ({ labelId: LABEL, labelCode: 'SENTETIK-001', labelVersion: '7', productId: PRODUCT, productVersion: null,
  sourceLocationId: LOCATION, sourceCustodianKind: 'Company', sourceCustodianId: COMPANY,
  physicalKg: '10.125', blockedPhysicalKg: '2.1', activeAllocationKg: '3', documentStatus: 'PendingDocuments' })
const target = (): TransferPlanDestination => ({ locationId: DESTINATION, locationCode: 'SENTETIK-HEDEF', locationName: 'Sentetik hedef depo',
  siteId: SITE, siteCode: 'SENTETIK-TESIS', siteName: 'Sentetik tesis', locationVersion: 9, custodianKind: 'Counterparty', custodianId: PARTY })
const frame = (items: readonly unknown[], query: string | null = null) => ({ companyId: COMPANY, query, limit: 25, items, searchWindowFull: false })
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
function uuidFactory() { let next = 0; return () => `aaaaaaaa-aaaa-4aaa-8aaa-${String(++next).padStart(12, '0')}` }
function snapshot(labels: readonly TransferPlanLabel[] = [label()], destination = target()): TransferPlanSnapshot {
  return beginTransferPlanAttempt('SENTETIK-binding', COMPANY, ACTOR, labels, destination, AT, uuidFactory())!
}
const planned = (request: PlanTransferRequest) => ({ transferId: request.transferId, companyId: COMPANY, version: '1', status: 'Planned',
  sourceLocationId: request.sourceLocationId, sourceCustodianKind: request.sourceCustodianKind, sourceCustodianId: request.sourceCustodianId,
  destinationLocationId: request.destinationLocationId, destinationCustodianKind: request.destinationCustodianKind, destinationCustodianId: request.destinationCustodianId,
  plannedAtUtc: request.plannedAtUtc, dispatchedAtUtc: null,
  lines: request.lines.map((line) => ({ ...line, actualShippedKg: null, differenceKg: null, status: 'Planned' })) })
const replayHeaders = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original-correlation' }
afterEach(() => { resetTransferPlanAttemptsForTests(); vi.unstubAllGlobals() })

describe('transfer plan actual label and destination candidate contracts', () => {
  it('preserves exact owner quantities opaque product provenance and server-typed external custodian', () => {
    const source = { ...label(), productVersion: 'SENTETIK-opaque-v2' }
    expect(decodeTransferPlanLabels(frame([source]), COMPANY, null)?.items[0]).toEqual(source)
    expect(decodeTransferPlanDestinations(frame([target()]), COMPANY, null)?.items[0]).toEqual(target())
    expect(decodeTransferPlanLabels({ ...frame([source]), searchWindowFull: true }, COMPANY, null)).not.toBeNull()
  })
  it.each([
    { ...label(), labelId: 'invalid' }, { ...label(), labelVersion: 7 }, { ...label(), labelVersion: '0' },
    { ...label(), labelVersion: '9223372036854775808' }, { ...label(), productVersion: '' }, { ...label(), productVersion: 'x'.repeat(129) },
    { ...label(), sourceCustodianKind: 'External' }, { ...label(), sourceCustodianId: '00000000-0000-0000-0000-000000000000' },
    { ...label(), labelCode: 'bad\ncode' }, { ...label(), labelCode: 'x'.repeat(65) }, { ...label(), physicalKg: '-1' },
    { ...label(), physicalKg: '1.0001' }, { ...label(), physicalKg: '01' }, { ...label(), blockedPhysicalKg: '-0.1' },
    { ...label(), activeAllocationKg: 3 }, { ...label(), documentStatus: 'Unknown' }, { ...label(), price: '10' },
  ])('refuses malformed label proof or unrelated extra fields %#', (row) => {
    expect(decodeTransferPlanLabels(frame([row]), COMPANY, null)).toBeNull()
  })
  it.each([
    { ...frame([label()]), companyId: PARTY }, { ...frame([label()]), query: '' }, { ...frame([label()]), limit: 100 },
    { ...frame([label()]), searchWindowFull: 1 }, { ...frame([label()]), extra: true }, frame([label(), { ...label(), labelId: LABEL.toUpperCase() }]),
    frame(Array.from({ length: 26 }, () => label())),
  ])('refuses a foreign echoed query unbounded window or duplicate label frame %#', (raw) => {
    expect(decodeTransferPlanLabels(raw, COMPANY, null)).toBeNull()
  })
  it.each([
    { ...target(), locationVersion: '9' }, { ...target(), locationVersion: 0 }, { ...target(), locationVersion: -1 },
    { ...target(), locationVersion: 1.5 }, { ...target(), locationVersion: Number.MAX_SAFE_INTEGER + 1 },
    { ...target(), custodianKind: 'External' }, { ...target(), custodianId: 'invalid' }, { ...target(), locationName: '' },
    { ...target(), siteCode: 'bad\tcode' }, { ...target(), isExternal: true },
  ])('rejects unsafe numeric location versions and derives no custodian from flags %#', (row) => {
    expect(decodeTransferPlanDestinations(frame([row]), COMPANY, null)).toBeNull()
  })
  it('refuses duplicate destinations and a contradictory full-window proof', () => {
    expect(decodeTransferPlanDestinations(frame([target(), target()]), COMPANY, null)).toBeNull()
    expect(decodeTransferPlanDestinations({ ...frame([target()]), searchWindowFull: true }, COMPANY, null)).toBeNull()
    expect(decodeTransferPlanDestinations(frame([]), COMPANY, null)?.items).toEqual([])
  })
  it('uses the actual bounded owner paths and echo-fences search and company', async () => {
    const fetcher = vi.fn(async (url: RequestInfo | URL, _init?: RequestInit) => json(frame(String(url).includes('/destinations') ? [target()] : [label()], 'A & B')))
    vi.stubGlobal('fetch', fetcher)
    expect((await fetchTransferPlanLabels(COMPANY, 'A & B')).type).toBe('ok-single')
    expect((await fetchTransferPlanDestinations(COMPANY, 'A & B')).type).toBe('ok-single')
    expect(fetcher.mock.calls[0][0]).toBe('/api/v1/inventory/transfer-plan-candidates?limit=25&q=A+%26+B')
    expect(fetcher.mock.calls[1][0]).toBe('/api/v1/inventory/transfer-plan-candidates/destinations?limit=25&q=A+%26+B')
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('X-Company-Id')).toBe(COMPANY)
    expect(fetcher.mock.calls[0][1]?.cache).toBe('no-store')
  })
  it('keeps owner failure distinct from empty and discards an aborted late success', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({}, 503)))
    expect(await fetchTransferPlanLabels(COMPANY, null)).toMatchObject({ type: 'http', status: 503 })
    const controller = new AbortController()
    vi.stubGlobal('fetch', vi.fn(async () => { controller.abort(); return json(frame([label()])) }))
    expect(await fetchTransferPlanLabels(COMPANY, null, controller.signal)).toEqual({ type: 'fence-stale' })
  })
})

describe('full physical owner proof and explicit plan creation', () => {
  it('freezes ordered whole-label quantities versions and actual custodian without destination version or derived totals', () => {
    const rows = [label(), { ...label(), labelId: LABEL2, labelCode: 'SENTETIK-002', labelVersion: '8', physicalKg: '7.025' }]
    const destination = target(); const request = snapshot(rows, destination)
    expect(request.payload).toMatchObject({ sourceLocationId: LOCATION, sourceCustodianKind: 'Company', sourceCustodianId: COMPANY,
      destinationLocationId: DESTINATION, destinationCustodianKind: 'Counterparty', destinationCustodianId: PARTY, plannedAtUtc: AT })
    expect(request.payload.lines.map((row) => [row.labelId, row.expectedLabelVersion, row.plannedKg])).toEqual([[LABEL, '7', '10.125'], [LABEL2, '8', '7.025']])
    expect(request.payload).not.toHaveProperty('locationVersion'); expect(request.payload).not.toHaveProperty('totalKg')
    expect(new Set([request.payload.transferId, ...request.payload.lines.map((row) => row.lineId)]).size).toBe(3)
    rows[0] = { ...rows[0], labelVersion: '9' }
    expect(request.labels[0].labelVersion).toBe('7'); expect(Object.isFrozen(request.payload.lines[0])).toBe(true)
  })
  it.each<{ readonly rows: readonly TransferPlanLabel[] }>([
    { rows: [label(), { ...label(), labelId: LABEL2, sourceLocationId: DESTINATION }] },
    { rows: [label(), { ...label(), labelId: LABEL2, sourceCustodianKind: 'Counterparty' }] },
    { rows: [label(), { ...label(), labelId: LABEL2, sourceCustodianId: PARTY }] },
    { rows: [label(), label()] }, { rows: [{ ...label(), physicalKg: '0' }] }, { rows: [] },
  ])('refuses mixed source incomplete zero or duplicate label intents %#', ({ rows }) => {
    expect(beginTransferPlanAttempt('SENTETIK-binding', COMPANY, ACTOR, rows, target(), AT, uuidFactory())).toBeNull()
  })
  it('distinguishes a custodian transfer at the same location from an identical target tuple', () => {
    const sameLocation = { ...target(), locationId: LOCATION }
    expect(differentTransferPlanTarget(label(), sameLocation)).toBe(true)
    expect(snapshot([label()], sameLocation).payload.destinationCustodianId).toBe(PARTY)
    expect(differentTransferPlanTarget(label(), { ...sameLocation, custodianKind: 'Company', custodianId: COMPANY })).toBe(false)
    expect(sameTransferPlanSource(label(), { ...label(), sourceLocationId: LOCATION.toUpperCase() })).toBe(true)
  })
  it('accepts only the actual fresh raw plan response and sends no automatic dispatch', async () => {
    const request = snapshot(); const fetcher = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => json(planned(request.payload), 201))
    vi.stubGlobal('fetch', fetcher)
    expect(await sendTransferPlan(request)).toEqual({ type: 'applied', transfer: planned(request.payload), replayed: false })
    expect(fetcher).toHaveBeenCalledTimes(1); expect(fetcher.mock.calls[0][0]).toBe('/api/v1/inventory/transfers')
    const init = fetcher.mock.calls[0][1]; expect(init?.method).toBe('POST'); expect(JSON.parse(String(init?.body))).toEqual(request.payload)
    expect(new Headers(init?.headers).get('Idempotency-Key')).toBe(request.key)
    expect(new Headers(init?.headers).get('X-Company-Id')).toBe(COMPANY)
  })
  it('accepts a coherent original replay only with the replay headers and its initial state', async () => {
    const request = snapshot(); vi.stubGlobal('fetch', vi.fn(async () => json(planned(request.payload), 200, replayHeaders)))
    expect((await sendTransferPlan(request))).toMatchObject({ type: 'applied', replayed: true })
  })
  it.each(['company', 'transfer', 'source', 'custodian', 'target', 'version', 'state', 'date', 'line', 'label', 'labelVersion', 'quantity', 'lineState', 'extra'] as const)(
    'keeps an inconsistent positive %s response unknown', async (change) => {
      const request = snapshot(); const raw = planned(request.payload)
      if (change === 'company') raw.companyId = PARTY
      if (change === 'transfer') raw.transferId = PARTY
      if (change === 'source') raw.sourceLocationId = DESTINATION
      if (change === 'custodian') raw.sourceCustodianId = PARTY
      if (change === 'target') raw.destinationCustodianId = COMPANY
      if (change === 'version') raw.version = '2'
      if (change === 'state') raw.status = 'InTransit'
      if (change === 'date') raw.plannedAtUtc = '2026-11-17T10:00:00Z'
      if (change === 'line') raw.lines[0] = { ...raw.lines[0], lineId: PARTY }
      if (change === 'label') raw.lines[0] = { ...raw.lines[0], labelId: LABEL2 }
      if (change === 'labelVersion') raw.lines[0] = { ...raw.lines[0], expectedLabelVersion: '8' }
      if (change === 'quantity') raw.lines[0] = { ...raw.lines[0], plannedKg: '8.025' }
      if (change === 'lineState') raw.lines[0] = { ...raw.lines[0], status: 'InTransit' }
      const body = change === 'extra' ? { ...raw, cost: '10' } : raw
      vi.stubGlobal('fetch', vi.fn(async () => json(body, 201)))
      expect(await sendTransferPlan(request)).toEqual({ type: 'unknown', reason: 'malformed-response' })
    })
  it.each<readonly [number, Record<string, string>]>([[200, {}], [200, { 'Idempotency-Replayed': 'true' }], [201, replayHeaders], [202, {}]])(
    'refuses incoherent success status or replay provenance %s %#', async (status, headers) => {
      const request = snapshot(); vi.stubGlobal('fetch', vi.fn(async () => json(planned(request.payload), status, headers)))
      expect((await sendTransferPlan(request)).type).toBe('unknown')
    })
  it('rejects a reordered positive line set', async () => {
    const request = snapshot([label(), { ...label(), labelId: LABEL2 }]); const raw = planned(request.payload)
    vi.stubGlobal('fetch', vi.fn(async () => json({ ...raw, lines: [...raw.lines].reverse() }, 201)))
    expect((await sendTransferPlan(request)).type).toBe('unknown')
  })
  it('distinguishes explicit refusal service uncertainty and offline uncertainty', async () => {
    const request = snapshot(); vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'inventory_transfer_label_version_conflict' }, 409)))
    expect(await sendTransferPlan(request)).toMatchObject({ type: 'refused', status: 409 })
    vi.stubGlobal('fetch', vi.fn(async () => json({}, 503)))
    expect(await sendTransferPlan(request)).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    vi.stubGlobal('navigator', { onLine: false }); vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('SENTETIK offline') }))
    expect(await sendTransferPlan(request)).toEqual({ type: 'unknown', reason: 'offline' })
  })
})

describe('actor company session and immutable unknown plan intent', () => {
  it('retains the original transfer ID line IDs key payload and proof when reentered', () => {
    const request = snapshot(); leaveTransferPlanAttempt(request.binding)
    expect(transferPlanFrozen(transferPlanAttempt(request.binding))).toBe(true)
    const retry = beginTransferPlanAttempt(request.binding, COMPANY, ACTOR, request.labels, request.destination, AT, () => { throw new Error('No new IDs on retry') })
    expect(retry).toBe(request); expect(retry?.payload).toBe(request.payload)
  })
  it('blocks a new intent and keeps a later conflict unresolved until original positive proof', () => {
    const request = snapshot(); finishTransferPlanAttempt(request, { type: 'unknown', reason: 'network-error' })
    expect(beginTransferPlanAttempt(request.binding, COMPANY, ACTOR, [{ ...label(), labelVersion: '8' }], target(), AT, uuidFactory())).toBeNull()
    const retry = beginTransferPlanAttempt(request.binding, COMPANY, ACTOR, request.labels, request.destination, AT, uuidFactory())!
    finishTransferPlanAttempt(retry, { type: 'refused', status: 409, code: 'idempotency_conflict' })
    expect(transferPlanAttempt(request.binding)).toMatchObject({ phase: 'failed', unresolved: true })
    expect(transferPlanFrozen(transferPlanAttempt(request.binding))).toBe(true)
    finishTransferPlanAttempt(request, { type: 'applied', transfer: planned(request.payload), replayed: true })
    expect(transferPlanAttempt(request.binding)).toMatchObject({ phase: 'completed', replayed: true })
  })
  it.each([401, 403])('retains even a first %i because final authorization may follow commit', (status) => {
    const request = snapshot(); finishTransferPlanAttempt(request, { type: 'refused', status, code: null })
    expect(transferPlanFrozen(transferPlanAttempt(request.binding))).toBe(true)
  })
  it('allows a distinct deliberate intent after a first definitive conflict and ignores a stale former result', () => {
    const first = snapshot(); finishTransferPlanAttempt(first, { type: 'refused', status: 409, code: null })
    const next = beginTransferPlanAttempt(first.binding, COMPANY, ACTOR, [{ ...label(), labelVersion: '8' }], target(), AT,
      (() => { let n = 10; return () => `bbbbbbbb-bbbb-4bbb-8bbb-${String(++n).padStart(12, '0')}` })())!
    finishTransferPlanAttempt(first, { type: 'applied', transfer: planned(first.payload), replayed: false })
    expect(transferPlanAttempt(first.binding)).toMatchObject({ phase: 'sending', snapshot: { key: next.key } })
  })
  it('namespaces pending plans by API actor company and session but preserves them over route reentry', () => {
    const shell = { ...initialShellState, phase: 'idle' as const, sessionEpoch: 1,
      activeContext: { userId: ACTOR, scopeMode: 'single' as const, companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [] } }
    const binding = transferPlanBinding(shell)
    expect(transferPlanBinding({ ...shell, routeEntryGeneration: 2, committedContextRevision: 3 })).toBe(binding)
    expect(transferPlanBinding({ ...shell, sessionEpoch: 2 })).not.toBe(binding)
    expect(transferPlanBinding({ ...shell, activeContext: { ...shell.activeContext, userId: 'SENTETIK-other' } })).not.toBe(binding)
    expect(transferPlanBinding({ ...shell, activeContext: { ...shell.activeContext, companyId: PARTY } })).not.toBe(binding)
    expect(transferPlanBinding({ ...shell, sessionInvalid: true })).toBeNull()
  })
})
