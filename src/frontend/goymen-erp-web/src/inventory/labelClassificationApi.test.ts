import { afterEach, describe, expect, it, vi } from 'vitest'
import { classifyInventoryLabel } from './labelClassificationApi'
import {
  decodeLabelClassificationView,
  describesLabelClassification,
  labelClassificationRefusal,
  type LabelClassificationRequest,
} from './labelClassificationContract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const LABEL = '22222222-2222-2222-2222-222222222222'
const KEY = 'classification-key-1'
const request: LabelClassificationRequest = { expectedLabelVersion: '7', classification: 'SecondQuality' }
const view = {
  labelId: LABEL, labelVersion: '8', classification: 'SecondQuality', classifiedAtUtc: '2026-10-02T08:00:00Z',
}

afterEach(() => vi.unstubAllGlobals())

describe('label classification contract', () => {
  it('accepts only the exact classification answer and exact next version', () => {
    const decoded = decodeLabelClassificationView(view)
    expect(decoded).not.toBeNull()
    expect(describesLabelClassification(decoded!, LABEL, request)).toBe(true)
    expect(decodeLabelClassificationView({ ...view, classification: 'FirstQuality' })).toBeNull()
    expect(decodeLabelClassificationView({ ...view, quantityKg: '1' })).toBeNull()
    expect(describesLabelClassification({ ...decoded!, labelVersion: '9' }, LABEL, request)).toBe(false)
  })

  it('keeps stable refusal text', () => {
    expect(labelClassificationRefusal(409, 'inventory_label_classification_label_version_conflict')).toMatch(/değişti/)
    expect(labelClassificationRefusal(403, null)).toMatch(/yetkiniz yok/)
  })
})

describe('classifyInventoryLabel', () => {
  it('posts one new classification with company and idempotency bindings', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(view), { status: 201 }))
    vi.stubGlobal('fetch', fetch)

    expect(await classifyInventoryLabel(LABEL, request, { companyId: COMPANY, idempotencyKey: KEY }))
      .toMatchObject({ type: 'applied', replayed: false })
    expect(fetch.mock.calls[0][0]).toMatch(new RegExp(`/inventory/labels/${LABEL}/classification$`))
    expect(fetch.mock.calls[0][1].headers['X-Company-Id']).toBe(COMPANY)
    expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe(KEY)
    expect(JSON.parse(fetch.mock.calls[0][1].body as string)).toEqual(request)
  })

  it('accepts same-key replay only with both replay headers', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(view), {
      status: 200,
      headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' },
    })))
    expect(await classifyInventoryLabel(LABEL, request, { companyId: COMPANY, idempotencyKey: KEY }))
      .toMatchObject({ type: 'applied', replayed: true })

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(view), { status: 200 })))
    expect(await classifyInventoryLabel(LABEL, request, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'unknown', reason: 'undecodable' })
  })

  it('never shows a foreign or stale answer and keeps transport failures unknown', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ ...view, labelVersion: '9' }), { status: 201 })))
    expect(await classifyInventoryLabel(LABEL, request, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'unknown', reason: 'undecodable' })

    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('network')))
    expect((await classifyInventoryLabel(LABEL, request, { companyId: COMPANY, idempotencyKey: KEY })).type)
      .toBe('unknown')
  })

  it('returns stable definite refusals', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      code: 'inventory_label_classification_label_version_conflict',
    }), { status: 409 })))
    expect(await classifyInventoryLabel(LABEL, request, { companyId: COMPANY, idempotencyKey: KEY }))
      .toEqual({ type: 'failed', status: 409, code: 'inventory_label_classification_label_version_conflict' })
  })
})
