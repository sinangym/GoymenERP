import { afterEach, describe, expect, it, vi } from 'vitest'
import { initialShellState } from '../shell/shellState'
import { beginWarehouseScaleAttempt, decodeTransferWarehouses, decodeWarehouseScale, fetchTransferWarehouses,
  fetchWarehouseScale, finishWarehouseScaleAttempt, leaveWarehouseScaleAttempt, recordWarehouseScale,
  resetWarehouseScaleAttemptsForTests, warehouseScaleAttempt, warehouseScaleBinding, warehouseScaleFrozen,
  WAREHOUSE_TRANSFER_SCALE_CAPABILITY, WAREHOUSE_TRANSFER_SCALE_SOURCE as SOURCE, type WarehouseScaleSnapshot } from './warehouseTransferScaleApi'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const LOCATION = '22222222-2222-4222-8222-222222222222'
const OTHER = '33333333-3333-4333-8333-333333333333'
const SITE = '44444444-4444-4444-8444-444444444444'
const choice = () => ({ locationId: LOCATION, locationCode: 'DEP-01', locationName: 'SENTETIK depo', siteId: SITE, siteCode: 'TES-01', siteName: 'SENTETIK tesis' })
const choices = () => ({ companyId: COMPANY, q: null, limit: 25, items: [choice()], searchWindowFull: false })
const virgin = () => ({ companyId: COMPANY, locationId: LOCATION, policy: { status: 1, companyId: COMPANY, locationId: LOCATION,
  requireExitWeighing: false, recordedVersion: null, sourceVersion: `${SOURCE}/not-configured` }, versions: [] })
const entry = () => ({ recordId: OTHER, version: '1', requireExitWeighing: false, locationVersion: 2,
  recordedBy: 'SENTETIK-actor', recordedAtUtc: '2026-11-17T08:00:00+00:00', reason: 'SENTETIK gerekçe' })
const configured = () => ({ ...virgin(), policy: { ...virgin().policy, status: 2, recordedVersion: 1, sourceVersion: `${SOURCE}/1` }, versions: [entry()] })
const payload = () => ({ expectedVersion: '0', requireExitWeighing: false, reason: 'SENTETIK gerekçe' })
function snapshot(): WarehouseScaleSnapshot { return { binding: 'synthetic-binding', companyId: COMPANY, locationId: LOCATION,
  payload: payload(), fingerprint: 'synthetic-fingerprint', key: 'synthetic-key' } }
const json = (raw: unknown, status = 200, headers: Record<string, string> = {}) => new Response(JSON.stringify(raw), { status, headers })
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); resetWarehouseScaleAttemptsForTests() })

describe('warehouse transfer scale exact source states', () => {
  it('keeps virgin Off distinct from explicit recorded Off and owner failure', () => {
    expect(decodeWarehouseScale(virgin(), COMPANY, LOCATION)?.policy.recordedVersion).toBeNull()
    expect(decodeWarehouseScale(configured(), COMPANY, LOCATION)?.policy).toMatchObject({ status: 2, requireExitWeighing: false, recordedVersion: 1 })
    expect(decodeWarehouseScale({ ...virgin(), policy: { ...virgin().policy, status: 3, requireExitWeighing: null, sourceVersion: `${SOURCE}/unavailable` } }, COMPANY, LOCATION)?.policy.status).toBe(3)
  })
  it.each([
    { companyId: OTHER }, { locationId: OTHER }, { extra: true },
    { policy: { ...virgin().policy, companyId: OTHER } }, { policy: { ...virgin().policy, locationId: OTHER } },
    { policy: { ...virgin().policy, status: 'NotConfigured' } }, { policy: { ...virgin().policy, requireExitWeighing: null } },
    { policy: { ...virgin().policy, requireExitWeighing: true } }, { policy: { ...virgin().policy, recordedVersion: 0 } },
    { policy: { ...virgin().policy, sourceVersion: 'settings.other' } }, { policy: { ...virgin().policy, status: 2 } },
    { policy: { ...virgin().policy, status: 3 } }, { versions: [entry()] }, { versions: null },
  ])('refuses foreign or invented virgin proof %j', (change) => expect(decodeWarehouseScale({ ...virgin(), ...change }, COMPANY, LOCATION)).toBeNull())
  it.each([
    { policy: { ...configured().policy, recordedVersion: 9007199254740992 } },
    { policy: { ...configured().policy, requireExitWeighing: true } }, { versions: [] },
    { versions: [{ ...entry(), version: '01' }] }, { versions: [{ ...entry(), locationVersion: 9007199254740992 }] },
    { versions: [{ ...entry(), recordedAtUtc: '2026-11-17T09:00:00+01:00' }] },
    { versions: [{ ...entry(), reason: 'unsafe\nreason' }] }, { versions: [{ ...entry(), version: '2' }] },
    { versions: [{ ...entry(), extra: true }] },
  ])('refuses lost numeric precision or contradictory immutable history %j', (change) => expect(decodeWarehouseScale({ ...configured(), ...change }, COMPANY, LOCATION)).toBeNull())
  it('accepts contiguous newest-first actual history and rejects a reused record identity', () => {
    const second = { ...entry(), recordId: SITE, version: '2', requireExitWeighing: true }
    const view = { ...configured(), policy: { ...configured().policy, recordedVersion: 2, requireExitWeighing: true, sourceVersion: `${SOURCE}/2` }, versions: [second, entry()] }
    expect(decodeWarehouseScale(view, COMPANY, LOCATION)?.versions).toHaveLength(2)
    expect(decodeWarehouseScale({ ...view, versions: [{ ...second, recordId: OTHER }, entry()] }, COMPANY, LOCATION)).toBeNull()
  })
})
describe('warehouse candidates are actual company-bound identities', () => {
  it('accepts real empty and named owner answers', () => {
    expect(decodeTransferWarehouses(choices(), COMPANY, null)?.items[0]).toEqual(choice())
    expect(decodeTransferWarehouses({ ...choices(), items: [] }, COMPANY, null)?.items).toEqual([])
  })
  it.each([{ companyId: OTHER }, { q: 'other' }, { limit: 100 }, { items: [choice(), choice()] }, { searchWindowFull: true },
    { items: [{ ...choice(), locationId: 'not-an-id' }] }, { items: [{ ...choice(), siteName: 'bad\nname' }] },
    { items: [{ ...choice(), permission: 'derived' }] }, { extra: true }])('rejects forged or off-contract discovery %j', (change) => {
    expect(decodeTransferWarehouses({ ...choices(), ...change }, COMPANY, null)).toBeNull()
  })
})
describe('warehouse scale transport preserves terminal identity', () => {
  it('reads actual candidates and the chosen policy under the committed company header', async () => {
    const fetch = vi.fn(async (url: string) => url.includes('/warehouses?') ? json({ ...choices(), q: 'DEP-01' }) : json(virgin()))
    vi.stubGlobal('fetch', fetch)
    expect((await fetchTransferWarehouses(COMPANY, 'DEP-01')).type).toBe('ok-single')
    expect((await fetchWarehouseScale(COMPANY, LOCATION)).type).toBe('ok-single')
    expect(fetch.mock.calls[0][0]).toBe('/api/v1/settings/warehouse-transfer-scale/warehouses?limit=25&q=DEP-01')
    expect(fetch.mock.calls[1][0]).toBe(`/api/v1/settings/warehouse-transfer-scale/${LOCATION}`)
    const init = vi.mocked(fetch).mock.calls[0] as unknown as [string, RequestInit]
    expect(new Headers(init[1].headers).get('X-Company-Id')).toBe(COMPANY)
    expect(fetch.mock.calls.some(([url]) => url.includes('/organization/'))).toBe(false)
  })
  it('posts exact frozen false without treating it as an omitted choice', async () => {
    const fetch = vi.fn(async () => new Response(null, { status: 204 })); vi.stubGlobal('fetch', fetch)
    expect(await recordWarehouseScale(snapshot())).toEqual({ type: 'applied', replayed: false })
    expect(fetch.mock.calls[0]).toMatchObject([`/api/v1/settings/warehouse-transfer-scale/${LOCATION}/versions`, {
      method: 'POST', body: JSON.stringify(payload()), headers: { 'X-Company-Id': COMPANY, 'Idempotency-Key': 'synthetic-key' },
    }])
  })
  it('accepts an exact replay and retains malformed success or unavailable responses as unknown', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 204, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' } })))
    expect(await recordWarehouseScale(snapshot())).toEqual({ type: 'applied', replayed: true })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 204, headers: { 'Idempotency-Replayed': 'true' } })))
    expect(await recordWarehouseScale(snapshot())).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it.each([200, 201, 302, 500, 503])('does not accept unexpected command status %i as success', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({}, status)))
    expect((await recordWarehouseScale(snapshot())).type).toBe('unknown')
  })
  it('recognizes stored version refusal proof while a permission refusal cannot settle a prior unknown', async () => {
    const headers = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'synthetic-original' }
    vi.stubGlobal('fetch', vi.fn(async () => json({ code: 'settings_warehouse_transfer_scale_version_conflict' }, 409, headers)))
    expect(await recordWarehouseScale(snapshot())).toMatchObject({ type: 'refused', terminalReplay: true })
    vi.stubGlobal('fetch', vi.fn(async () => json({}, 403, headers)))
    expect(await recordWarehouseScale(snapshot())).toMatchObject({ type: 'refused', terminalReplay: false })
  })
})
describe('warehouse scale retained attempt', () => {
  it('retains immutable payload and key across route departure and refuses another warehouse until resolved', () => {
    const original = payload(); const first = beginWarehouseScaleAttempt('bound', COMPANY, LOCATION, original, () => 'same-key')!
    original.reason = 'changed live form'; leaveWarehouseScaleAttempt('bound')
    expect(warehouseScaleFrozen(warehouseScaleAttempt('bound'))).toBe(true)
    expect(first.payload.reason).toBe('SENTETIK gerekçe')
    expect(beginWarehouseScaleAttempt('bound', COMPANY, OTHER, payload())).toBeNull()
    const retry = beginWarehouseScaleAttempt('bound', COMPANY, LOCATION, payload(), () => 'must-not-be-used')!
    expect(retry).toBe(first)
    finishWarehouseScaleAttempt(retry, { type: 'refused', status: 403, code: null, terminalReplay: false })
    expect(warehouseScaleFrozen(warehouseScaleAttempt('bound'))).toBe(true)
    beginWarehouseScaleAttempt('bound', COMPANY, LOCATION, payload())
    finishWarehouseScaleAttempt(first, { type: 'refused', status: 409, code: 'settings_warehouse_transfer_scale_version_conflict', terminalReplay: true })
    expect(warehouseScaleFrozen(warehouseScaleAttempt('bound'))).toBe(false)
  })
  it('separates actual actor company and session epochs instead of persisting an attempt into a new identity', () => {
    const state = { ...initialShellState, sessionEpoch: 1, activeContext: { userId: 'SENTETIK-actor', scopeMode: 'single' as const,
      companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [WAREHOUSE_TRANSFER_SCALE_CAPABILITY] } }
    const binding = warehouseScaleBinding(state)
    expect(warehouseScaleBinding({ ...state, sessionEpoch: 2 })).not.toBe(binding)
    expect(warehouseScaleBinding({ ...state, activeContext: { ...state.activeContext, companyId: OTHER } })).not.toBe(binding)
    expect(warehouseScaleBinding({ ...state, activeContext: { ...state.activeContext, userId: 'SENTETIK-other' } })).not.toBe(binding)
  })
})
