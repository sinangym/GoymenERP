import { afterEach, expect, it, vi } from 'vitest'
import { createCounterpartyNeed } from './creationNeedApi'
import type { CreateTaskBody } from '../work/collaborationApi'
import { A_ID, B_ID, IDENTITY, json, problem } from './commandPageHarness'

const RECIPIENT = '11111111-1111-4111-8111-111111111111'
const body: CreateTaskBody = { title: '  Cari ihtiyacı  ', details: '  Açıklama\nkorunur  ', assigneeUserId: RECIPIENT,
  dueAtUtc: '2099-12-31T10:00:00.000Z', recordType: null, recordId: null }
const task = (over: Record<string, unknown> = {}) => ({ id: '22222222-2222-4222-8222-222222222222', companyId: A_ID,
  ...body, assignerUserId: IDENTITY.userId, assignerDisplayName: 'Atayan', assigneeDisplayName: 'Alıcı',
  dueAtUtc: '2099-12-31T10:00:00+00:00', status: 'open', version: '1', createdAtUtc: '2026-11-28T08:00:00+00:00',
  closedAtUtc: null, closedByUserId: null, cancelReason: null, ...over })
const replay = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'first-request' }
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks() })

it('sends only the existing six Work fields to the need endpoint, with the caller-owned key and company', async () => {
  const fetch = vi.fn<typeof globalThis.fetch>(async () => json(task(), 201)); vi.stubGlobal('fetch', fetch)
  expect((await createCounterpartyNeed(A_ID, IDENTITY.userId, 'frozen-key', body)).type).toBe('applied')
  expect(fetch).toHaveBeenCalledTimes(1)
  const [url, init] = fetch.mock.calls[0]
  expect(url).toBe('/api/v1/work/counterparty-creation-needs')
  expect(JSON.parse(String(init?.body))).toEqual(body)
  expect(Object.keys(JSON.parse(String(init?.body))).sort()).toEqual(['title', 'details', 'assigneeUserId', 'dueAtUtc', 'recordType', 'recordId'].sort())
  expect(new Headers(init?.headers).get('Idempotency-Key')).toBe('frozen-key')
  expect(new Headers(init?.headers).get('X-Company-Id')).toBe(A_ID)
})
it.each([401, 403] as const)('keeps post-dispatch %i uncertain instead of authorizing a new key', async status => {
  vi.stubGlobal('fetch', vi.fn(async () => problem(status, 'forbidden')))
  expect(await createCounterpartyNeed(A_ID, IDENTITY.userId, 'key', body)).toEqual({ type: 'unknown', reason: 'authority', status })
})
it.each([409, 422])('preserves a definite native %i refusal', async status => {
  vi.stubGlobal('fetch', vi.fn(async () => problem(status, 'work.task.invalid')))
  expect(await createCounterpartyNeed(A_ID, IDENTITY.userId, 'key', body)).toMatchObject({ type: 'refused', status })
})
it.each([
  { companyId: B_ID }, { assignerUserId: RECIPIENT }, { assigneeUserId: IDENTITY.userId }, { title: 'changed' }, { details: null },
  { dueAtUtc: '2099-12-31T10:01:00Z' }, { dueAtUtc: '2099-12-31T10:00:00.0000001Z' },
  { recordType: 'counterparty', recordId: RECIPIENT }, { version: 1 }, { partyId: RECIPIENT },
])('does not announce malformed or unbound task facts %j', async over => {
  vi.stubGlobal('fetch', vi.fn(async () => json(task(over), 201)))
  expect((await createCounterpartyNeed(A_ID, IDENTITY.userId, 'key', body)).type).toBe('unknown')
})
it.each(['completed', 'cancelled'] as const)('recovers the original task even when its current state is %s', async status => {
  const current = task({ status, version: '2', closedAtUtc: '2099-12-31T11:00:00Z',
    closedByUserId: status === 'completed' ? RECIPIENT : IDENTITY.userId, cancelReason: status === 'cancelled' ? 'İptal' : null })
  vi.stubGlobal('fetch', vi.fn(async () => json(current, 201, replay)))
  expect(await createCounterpartyNeed(A_ID, IDENTITY.userId, 'original-key', body)).toMatchObject({ type: 'applied', replayed: true, data: { status } })
  vi.stubGlobal('fetch', vi.fn(async () => json(current, 201)))
  expect((await createCounterpartyNeed(A_ID, IDENTITY.userId, 'key', body)).type).toBe('unknown')
})
it('keeps network, 503, wrong status and incomplete replay headers uncertain', async () => {
  for (const reply of [() => Promise.reject(new TypeError('offline')), async () => problem(503, null),
    async () => json(task(), 200), async () => json(task(), 201, { 'Idempotency-Replayed': 'true' })]) {
    vi.stubGlobal('fetch', vi.fn(reply))
    expect((await createCounterpartyNeed(A_ID, IDENTITY.userId, 'same-key', body)).type).toBe('unknown')
  }
})
