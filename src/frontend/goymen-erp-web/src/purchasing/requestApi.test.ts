import { afterEach, describe, expect, it, vi } from 'vitest'
import { createPurchaseRequest } from './api'
import { submitPurchaseRequest } from './requestSubmissionApi'

const companyId = '11111111-1111-4111-8111-111111111111'
const requestId = '22222222-2222-4222-8222-222222222222'
const lineId = '33333333-3333-4333-8333-333333333333'
const productId = '44444444-4444-4444-8444-444444444444'
const unitId = '55555555-5555-4555-8555-555555555555'
const keyId = '66666666-6666-4666-8666-666666666666'

const line = { lineId, lineNo: 1, productId, productVersion: '7', unitId, quantity: '12.5' }
function envelope(status: 'Draft' | 'Submitted', version: string, replay = false) {
  return {
    detail: {
      request: {
        id: requestId, companyId, status, version, effectiveOn: '2026-09-23',
        createdAtUtc: '2026-09-23T10:00:00Z', updatedAtUtc: '2026-09-23T10:00:00Z', lineCount: 1,
      },
      lines: [line],
    },
    isReplay: replay,
    originalCorrelationId: 'corr-1',
  }
}

afterEach(() => vi.unstubAllGlobals())

describe('purchase request commands', () => {
  it('binds create to the selected Product identity, version, unit and Location', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(envelope('Draft', '2')), {
      status: 201,
      headers: { 'Content-Type': 'application/json', Location: `/api/v1/purchasing/requests/${requestId}` },
    }))
    vi.stubGlobal('fetch', fetch)
    const payload = { effectiveOn: '2026-09-23', lines: [{ lineId, productId, expectedProductVersion: '7', unitId, quantity: '12.5' }] }
    const outcome = await createPurchaseRequest(payload, { companyId, idempotencyKey: keyId })
    expect(outcome.type).toBe('applied')
    expect(JSON.parse(fetch.mock.calls[0][1].body as string)).toEqual(payload)
    expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe(keyId)
  })

  it('requires replay headers to agree with the submitted response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(envelope('Submitted', '3', true)), {
      status: 200,
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Replayed': 'true',
        'Idempotency-Original-Correlation-Id': 'corr-1',
      },
    })))
    const outcome = await submitPurchaseRequest(companyId, requestId, '2', keyId)
    expect(outcome).toMatchObject({ type: 'applied', replayed: true })
  })
})
