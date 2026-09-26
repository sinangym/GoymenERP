import { afterEach, describe, expect, it, vi } from 'vitest'
import { recordLabelTransformation } from './labelTransformationApi'
import {
  DEFINITE_UNAVAILABLE_CODES,
  decodeLabelTransformationView,
  describesTransformation,
  gramsToKilograms,
  hasDuplicateLabelCodes,
  isKilogramText,
  isLabelTransformationRequest,
  kilogramsToGrams,
  sameLabelId,
  sumKilograms,
  transformationRefusalText,
  type LabelTransformationRequest,
} from './labelTransformationContract'
import {
  pendingTransformationKey,
  recallPendingTransformation,
  releasePendingTransformation,
  reservePendingTransformation,
} from './pendingTransformationStore'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const TRANSFORMATION = '22222222-2222-4222-8222-222222222222'
const SOURCE = '33333333-3333-4333-8333-333333333333'
const OUT_A = '44444444-4444-4444-8444-444444444444'
const OUT_B = '55555555-5555-4555-8555-555555555555'
const KEY = '66666666-6666-4666-8666-666666666666'

const split: LabelTransformationRequest = {
  transformationId: TRANSFORMATION,
  kind: 'Split',
  sources: [{ labelId: SOURCE, expectedLabelVersion: '3' }],
  outputs: [
    { labelId: OUT_A, code: 'LBL-100-A', quantityKg: '600.5' },
    { labelId: OUT_B, code: 'LBL-100-B', quantityKg: '650' },
  ],
  occurredAtUtc: '2026-09-26T08:00:00.000Z',
  reason: 'Müşteri paket ağırlığı',
}
const view = {
  transformationId: TRANSFORMATION, companyId: COMPANY, kind: 'Split', occurredAtUtc: '2026-09-26T08:00:00+00:00',
  reason: 'Müşteri paket ağırlığı', sourceLabelIds: [SOURCE], outputLabelIds: [OUT_B, OUT_A],
}

afterEach(() => { vi.unstubAllGlobals(); sessionStorage.clear() })

describe('label transformation kilograms', () => {
  it('sums exactly in grams and writes canonical kg text', () => {
    expect(sumKilograms(['600.5', '650'])).toBe('1250.5')
    expect(sumKilograms(['0.001', '0.999'])).toBe('1')
    expect(sumKilograms(['12345678901234567.125', '0.875'])).toBe('12345678901234568')
    expect(kilogramsToGrams('1.25')).toBe(1250n)
    expect(gramsToKilograms(1250n)).toBe('1.25')
  })
  it('accepts only positive canonical kg with at most three decimals', () => {
    expect(isKilogramText('1.125')).toBe(true)
    for (const value of ['0', '1.1250', '1.1234', '01', '-1', '1e3', '']) expect(isKilogramText(value)).toBe(false)
  })
})

describe('label transformation request shape', () => {
  it('accepts a split of one source into two outputs and a merge of two sources into one output', () => {
    expect(isLabelTransformationRequest(split)).toBe(true)
    expect(isLabelTransformationRequest({
      ...split, kind: 'Merge',
      sources: [{ labelId: SOURCE, expectedLabelVersion: '3' }, { labelId: OUT_B, expectedLabelVersion: '1' }],
      outputs: [{ labelId: OUT_A, code: 'LBL-M', quantityKg: '10' }],
    })).toBe(true)
  })
  it.each([
    ['one output split', { ...split, outputs: [split.outputs[0]] }],
    ['one source merge', { ...split, kind: 'Merge', outputs: [split.outputs[0]] }],
    ['duplicate codes', { ...split, outputs: [split.outputs[0], { ...split.outputs[1], code: 'LBL-100-A' }] }],
    ['codes equal ignoring case', { ...split, outputs: [split.outputs[0], { ...split.outputs[1], code: 'lbl-100-a' }] }],
    ['output reuses source id', { ...split, outputs: [split.outputs[0], { ...split.outputs[1], labelId: SOURCE }] }],
    ['numeric version', { ...split, sources: [{ labelId: SOURCE, expectedLabelVersion: 3 }] }],
    ['non-UTC time', { ...split, occurredAtUtc: '2026-09-26T11:00:00+03:00' }],
    ['blank reason', { ...split, reason: '  ' }],
    ['unknown member', { ...split, extra: true }],
    ['four decimals', { ...split, outputs: [split.outputs[0], { ...split.outputs[1], quantityKg: '1.2345' }] }],
  ])('refuses %s', (_name, value) => {
    expect(isLabelTransformationRequest(value)).toBe(false)
  })
})

describe('label transformation response', () => {
  it('decodes the owner view and binds it to exactly this request and company', () => {
    const decoded = decodeLabelTransformationView(view)
    expect(decoded).not.toBeNull()
    expect(describesTransformation(decoded!, split, COMPANY)).toBe(true)
    expect(describesTransformation(decoded!, split, OUT_A)).toBe(false)
    expect(describesTransformation(decoded!, { ...split, reason: 'başka' }, COMPANY)).toBe(false)
    expect(describesTransformation({ ...decoded!, outputLabelIds: [OUT_A] }, split, COMPANY)).toBe(false)
  })
  it('refuses unknown kinds, members and ids', () => {
    expect(decodeLabelTransformationView({ ...view, kind: 1 })).toBeNull()
    expect(decodeLabelTransformationView({ ...view, extra: 1 })).toBeNull()
    expect(decodeLabelTransformationView({ ...view, sourceLabelIds: ['x'] })).toBeNull()
  })
  it('explains the writer refusals in Turkish and keeps unknown codes visible', () => {
    expect(transformationRefusalText(409, 'inventory_label_active_allocation_conflict')).toMatch(/aktif tahsis/)
    expect(transformationRefusalText(503, 'inventory_permission_unregistered')).toMatch(/hiçbir şey kaydedilmedi/)
    expect(transformationRefusalText(503, 'settings_provider_unavailable')).toMatch(/settings_provider_unavailable.*hiçbir şey kaydedilmedi/)
    expect(transformationRefusalText(403, null)).toMatch(/yetkiniz yok/)
    expect(transformationRefusalText(422, 'new_code')).toContain('new_code')
  })
})

describe('recordLabelTransformation', () => {
  it('posts the exact request with the key and accepts a first record', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(view), { status: 201 }))
    vi.stubGlobal('fetch', fetch)
    const outcome = await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY })
    expect(outcome).toMatchObject({ type: 'applied', replayed: false })
    expect(fetch.mock.calls[0][0]).toMatch(/\/inventory\/label-transformations$/)
    expect(JSON.parse(fetch.mock.calls[0][1].body as string)).toEqual(split)
    expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe(KEY)
    expect(fetch.mock.calls[0][1].headers['X-Company-Id']).toBe(COMPANY)
  })
  it('accepts a replay only with the canonical replay headers', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(view), {
      status: 200, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' },
    })))
    expect(await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY }))
      .toMatchObject({ type: 'applied', replayed: true })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(view), { status: 200 })))
    expect(await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'unknown', reason: 'undecodable' })
  })
  it('never shows a success that answers another request', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ ...view, outputLabelIds: [OUT_A] }), { status: 201 })))
    expect(await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'unknown', reason: 'undecodable' })
  })
  it('reports refusals with their code and keeps 5xx or transport failures unknown', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'inventory_label_code_taken' }), { status: 409 })))
    expect(await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'failed', status: 409, code: 'inventory_label_code_taken' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))
    expect(await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'unknown', reason: 'service-unavailable' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'something_else' }), { status: 503 })))
    expect(await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'unknown', reason: 'service-unavailable' })
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    expect((await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY })).type).toBe('unknown')
  })
})

describe('definite pre-write 503s and case rules', () => {
  it.each([...DEFINITE_UNAVAILABLE_CODES])('reports a coded 503 %s as a definite failure, nothing recorded', async (code) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code }), { status: 503 })))
    expect(await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'failed', status: 503, code })
  })
  it('never treats a coded 500 or 502 as definite', async () => {
    for (const status of [500, 502]) {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'inventory_permission_unregistered' }), { status })))
      expect((await recordLabelTransformation(split, { companyId: COMPANY, idempotencyKey: KEY })).type).toBe('unknown')
    }
  })
  it('compares label codes and ids the way the writer does', () => {
    expect(hasDuplicateLabelCodes(['K-1', 'k-1'])).toBe(true)
    expect(hasDuplicateLabelCodes(['K-1', 'K-2'])).toBe(false)
    expect(sameLabelId(SOURCE.toUpperCase(), SOURCE)).toBe(true)
  })
})

describe('pending transformation store', () => {
  it('remembers one attempt per session, actor, company and origin label and releases only its own key', () => {
    const key = pendingTransformationKey(1, 'actor-a', COMPANY, SOURCE)
    expect(pendingTransformationKey(1, null, COMPANY, SOURCE)).toBeNull()
    const pending = { actorId: 'actor-a', companyId: COMPANY, originLabelId: SOURCE, idempotencyKey: KEY, payload: split }
    reservePendingTransformation(key, pending)
    expect(recallPendingTransformation(key)).toEqual(pending)
    releasePendingTransformation(key, TRANSFORMATION)
    expect(recallPendingTransformation(key)).toEqual(pending)
    releasePendingTransformation(key, KEY)
    expect(recallPendingTransformation(key)).toBeNull()
  })
  it('refuses a remembered attempt that is not about its origin label', () => {
    const key = pendingTransformationKey(1, 'actor-a', COMPANY, OUT_A)
    sessionStorage.setItem(key!, JSON.stringify({ actorId: 'actor-a', companyId: COMPANY, originLabelId: OUT_A, idempotencyKey: KEY, payload: split }))
    expect(recallPendingTransformation(key)).toBeNull()
  })
})
